# コマンドリファレンス

CLI・DAW・MCP のそれぞれから何ができるかを、引数まで含めて並べる。
導入と最小の使い方は README にある。

## CLI

ビルド済みの `arpeggio` 実行ファイルを PATH に置いて使用する。

```sh
arpeggio new melody.arpeggio.json --chip nes --tempo 150 --length-beats 16 --title melody
arpeggio note add melody.arpeggio.json --track 0 --tick 0 --duration 24 --note C5
arpeggio note add melody.arpeggio.json --track 0 --tick 24 --duration 24 --note E5
arpeggio note add melody.arpeggio.json --track 0 --tick 48 --duration 24 --note G5
arpeggio note add melody.arpeggio.json --track 0 --tick 72 --duration 24 --note C6
arpeggio show melody.arpeggio.json --from-tick 0 --to-tick 96
arpeggio info melody.arpeggio.json --json
arpeggio export wav melody.arpeggio.json melody.wav --loops 1 --sample-rate 44100 --tail 0.5
arpeggio chip-reference nes
```

`--chip` は `nes / gameboy / snes`。ノート名は `C5 / C#5 / Db5 / 60`（MIDI 60 = C4）。トラックは 0 始まり、四分音符は 48 tick。`note move` は `--to-tick` と任意の `--note`、`note resize` は `--duration`、`note remove` は `--track` と `--tick` を指定する。

`show` は 12 tick ごとの表で、開始音を `C-5 15 01`、継続を `...`、無音を `---`、効果付きを末尾 `*` で示す。行の途中で開始する音は `@実tick`、同じ行の複数音は `;` で併記する。範囲は `[from-tick,to-tick)`、行は from-tick 起点。`--track` で絞り込み、`--json` で正確なノート値と表示テキストを取得する。

```sh
arpeggio instrument list melody.arpeggio.json --json
arpeggio instrument add melody.arpeggio.json --kind NesPulse --name lead --duty 25 --volume-macro "15,14,12/2"
arpeggio instrument set melody.arpeggio.json --id 2 --arpeggio-macro "0,4,7/0" --pitch-macro "null"
arpeggio note add melody.arpeggio.json --track 1 --tick 0 --duration 48 --note C4 --instrument 2 --effect PitchSlide=-4 Vibrato=20
arpeggio undo melody.arpeggio.json
arpeggio redo melody.arpeggio.json
```

音色追加の ID は自動採番して標準出力へ返す。`instrument set` は指定項目だけを更新する。マクロは `値,値,.../loopIndex`（省略時 -1）、`null` で解除する。不適合な kind のオプションは拒否する。

| 音色のオプション | 入力 |
|---|---|
| `--duty` | 12.5 / 25 / 50 / 75 |
| `--volume-macro` / `--arpeggio-macro` / `--pitch-macro` / `--duty-macro` | 整数列。単位は `chip-reference` を参照 |
| `--noise-mode` | NES Noise: Long / Short |
| `--initial-volume` / `--envelope-increasing` / `--envelope-step-frames` | GB Pulse: 0〜15 / true・false / 60 Hz のフレーム数 |
| `--waveform` / `--output-level` | GB Wave: 32 個の 0〜15 のカンマ区切り / 0・25・50・100 |
| `--lfsr-width` | GB Noise: 7 / 15 |
| `--waveform` / `--loop` | SNES: Sine・Square・Saw・Triangle・Pulse・Noise / true・false |
| `--adsr` | SNES: attack秒,decay秒,sustain(0〜1),release秒 |
| `--echo-send` / `--pan` | SNES: 0〜1 / -1〜1 |

`instrument remove <path> --id N` は未参照の音色だけを削除する。`instrument set --kind` は ID・名前と両 kind で共通する項目を保ち、専用項目を新 kind の既定値へ切り替える。

### バッチ

`arpeggio apply <path> --operations operations.json` で次の JSON 配列を適用する。`--operations -` は標準入力。`--track N` は各操作の track 省略時だけ使う。1 バッチは 1 回の undo 単位で、途中の不正操作では曲・履歴・ファイルを変更しない。

```json
[
  { "kind": "AddInstrument", "instrument": { "id": 2, "name": "lead", "kind": "NesPulse", "duty": "Percent25" } },
  { "kind": "AddNote", "track": 0, "tick": 0, "durationTicks": 24, "midiNote": 60, "volume": 15, "instrumentId": 2, "effects": [] },
  { "kind": "SetTempo", "tempoBpm": 180 }
]
```

| kind | 必須項目と任意項目 |
|---|---|
| AddNote | track・tick・durationTicks・midiNote。volume=15・instrumentId=1・effects=[] は省略可 |
| RemoveNote | track・tick |
| MoveNote | track・tick・toTick。midiNote は任意 |
| ResizeNote | track・tick・durationTicks |
| UpdateNote | track・tick。toTick・durationTicks・midiNote・volume・instrumentId・effects の指定項目だけを更新。effects=[] で解除 |
| AddInstrument / UpdateInstrument | instrument（保存形式と同じオブジェクト。Update は全置換） |
| RemoveInstrument | instrumentId |
| SetTempo / SetLength / SetLoopStart | tempoBpm / lengthTicks / loopStartTick |
| SetTrackMuted / SetTrackPan | track と muted / pan |

各操作直後にソング制約を検証するため、音色追加はその音色を使うノート追加より先に指定する。音色削除は参照ノート削除より後に指定する。

終了コードは成功 0、引数・操作エラー 1、ドキュメント不正 2、I/O エラー 3。`export wav` の警告は stderr に出すが成功コードは 0。`--json` 指定時は結果の `warnings` と `droppedWarningCount` に含める。

CLI 履歴は `<path>.history/state.json` に最大 50 件保存する。外部更新後は古い履歴を無効にする。履歴ファイルが破損している場合は `<path>.history` を退避して再実行する。

### 効果音と解析

スライダーや AI で再調整する効果音は **`sfx create`** で作る。`sfx new` は従来のソング雛形を作る入口で、パラメータ定義を付けない。どちらも `jump / coin / hit / explosion / powerup / laser / blip / select` の8用途×NES / Game Boy / SNESに対応する。同じ用途名でも新旧の音は別であり、自動移行はしない。

```sh
arpeggio sfx list --editable --json
arpeggio sfx create jump.arpeggio.json --preset jump --chip nes --json
arpeggio sfx params jump.arpeggio.json --schema --json
arpeggio sfx tweak jump.arpeggio.json --frequency 220 --slide 96 --decay 0.2 --json
arpeggio export wav jump.arpeggio.json jump.wav --tail 0
arpeggio analyze jump.arpeggio.json            # ソングをレンダリングして解析（--track N でソロ）
arpeggio analyze wav jump.wav --window-ms 100  # 既存 WAV を解析
```

従来の音は `arpeggio sfx list` と `arpeggio sfx new legacy-jump.arpeggio.json --preset jump --chip nes` で作る。作成後は通常のノート・音色編集を使う。16種類の SNES 内蔵音色は別のカタログである。

`analyze` は長さ・RMS・ピーク・クリップ数・無音割合・左右差・帯域比率・窓ごとの支配的周波数と音名、および警告（クリップ・小音量・先頭無音・末尾無音・左右差）と合成側の警告（音域クランプ等）を返す。`--json` で全窓を取得できる。

#### パラメータと探索

`sfx params --chip gameboy --json` はファイルを作らず初期値と schema を返す。保存済みファイルの `params` では `editable`・`reason`・`revision` を確認する。全項目の正規パス・範囲・単位・対応チップは `--schema`、厳密な生成規則は [SFX 設計書](docs/design-sfx.md) を参照する。

| 調整するもの | CLI の主なオプションと単位 |
|---|---|
| 音の構成 | `--tone-enabled true/false`、`--noise-enabled true/false`。最低一声を有効にする |
| トーン音程 | `--frequency` Hz、`--slide` 半音/秒、`--delta-slide` 半音/秒²、`--vibrato-depth` cent、`--vibrato-speed` Hz |
| 独立した包絡 | `--volume` 0〜15、`--attack` / `--sustain` / `--decay` 秒、`--punch` 0〜1。ノイズ側は `--noise-volume` など `noise-` を付ける |
| 音程変化・反復 | `--pitch-change` 半音、`--pitch-change-time` 秒、`--repeat-period` 秒（0は無効） |
| NES / GB | `--duty` 12.5 / 25 / 50 / 75%、`--duty-sweep` %ポイント/秒。NESは `--noise-period` 0〜15・`--noise-mode long/short`、GBは `--noise-selection` 0〜127・`--noise-width` 7 / 15 |
| ノイズの変化 | NESの `--noise-slide` は添字/秒、GBは選択値/秒。トーンのスライド・反復とは独立 |
| SNES | `--waveform sine/square/saw/triangle/pulse`、`--noise-rate` 1〜31。duty sweep・noise slideは使えない |

制御は60 Hz、実数は小数6桁へ正規化する。sustain は**保持時間**、decay は**ゼロまでの減衰時間**。punch はピーク音量を増やさず、保持冒頭以外を相対的に下げる。repeat は音程曲線・ジャンプ・duty の時間を戻し、包絡・ビブラート・発音位相は継続する。本体長は実効包絡長＋最後のゼロ音量1フレームで、export の追加 `tail` とは別である。

ノイズの選択値は音階ではない。GBの選択値はNR43のraw値とも異なり、大きくすれば常に明るくなるとは限らない。SNESの内蔵周期波形の上限は約999.94 Hzで、高い要求値や上昇曲線は音域制限の診断を確認する。

```sh
arpeggio sfx randomize jump.arpeggio.json --category jump --seed 1 --dry-run --json
arpeggio sfx randomize jump.arpeggio.json --category jump --seed 1 --json
arpeggio sfx mutate jump.arpeggio.json --seed 42 --strength 0.1 --lock tone.baseFrequencyHz --lock tone.envelope.decaySeconds --json
arpeggio undo jump.arpeggio.json
arpeggio redo jump.arpeggio.json
```

randomize はカテゴリから全レシピを入れ替える。カテゴリは8用途と `any`、seed は必須の0〜4294967295。同じチップ・カテゴリ・seed・アルゴリズム版で同じ値になる。mutate は現在値からの変異で、`--lock` を繰り返して保持する正規パスを指定する。変異の再現には seed に加えて変更前の値・strength・locks が必要。`strength=0` や同値結果は保存・履歴を増やさない。

複数値は `--patch patch.json` または `--patch -`（標準入力）でも一操作として適用できる。内容は `{"tone":{"slideSemitonesPerSecond":-12,"envelope":{"decaySeconds":0.2}}}` のような部分オブジェクト。省略は保持し、null・未知/重複キー・空patchは拒否する。個別オプションとの混在はできない。

#### CLI の AI 調整ループ

次は POSIX シェルと Python 3 を使い、読んだ revision に対してだけ変更する例。未使用の保存先で実行する。

```sh
set -e
arpeggio sfx create laser.arpeggio.json --chip nes --preset laser --json
arpeggio sfx params laser.arpeggio.json --schema --json > laser.params.json
sfx_revision=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["revision"])' laser.params.json)
arpeggio sfx tweak laser.arpeggio.json --slide -12 --decay 0.2 --expected-revision "$sfx_revision" --dry-run --json > laser.candidate.json
```

`laser.params.json` の `editable=true` と、候補の `warnings`・要求値／実効値を確認してから適用する。dry-run の `revision` は現在値、`candidateRevision` は適用予定の値であり、ファイルと履歴は変わらない。

```sh
arpeggio sfx tweak laser.arpeggio.json --slide -12 --decay 0.2 --expected-revision "$sfx_revision" --json
arpeggio analyze laser.arpeggio.json --loops 1 --window-ms 20 --json > laser.song-analysis.json
arpeggio export wav laser.arpeggio.json laser.wav --loops 1 --sample-rate 44100 --tail 0
arpeggio analyze wav laser.wav --window-ms 20 --json > laser.wav-analysis.json
```

次の調整では params と revision を取り直す。`RevisionConflict` なら古い値で再試行せず、最新状態を読み直す。`rmsDbfs`・`peakDbfs`・`clippedSampleCount` と窓ごとの変化、ノイズは `bandEnergy`・`spectralCentroidHz` を比較し、候補比較には undo/redo を使う。解析・WAVに自動のrevision照合はないため、併行編集した場合は対象を取り直す。

ソング解析は44100 Hz・tail=0のfloat PCM、WAV解析は16 bit量子化後なので音響数値の完全一致は要求しない。20 ms窓の支配的周波数は基音推定ではなく、短音・倍音・ノイズから音階を断定できない。低音は50〜100 ms窓でも比較する。OGGは短音の最終granuleに既知の制限があり、このループの長さ基準に使わない。

新SFXコマンドの `--json` は成功・失敗ともstdoutに一つのJSONを返す。終了コードは0=成功、1=入力・操作拒否、2=元ドキュメント不正、3=I/O。`TimeQuantized`・`DutyQuantized` は離散化の情報、`PitchClamped`・`NoiseSelectionClamped`・`InactiveRepeat` などは調整可能な警告。SFX編集にstrictはなく、NSF/VGM書き出し側の `--strict` と分ける。

#### 保存と互換性

Song の `version` は1のまま、optional な `sfx` に全パラメータ・版・出自・指紋を保存する。再生入力は同時保存されたマクロ・ノートであり、読み込みや保存だけでは再生成しない。

| 経路・状態 | 動作 |
|---|---|
| 旧ファイル → 新アプリ | 旧JSONの正規表現と音を維持。タイトルやプリセット名から定義を自動追加しない |
| 新ファイル → 新アプリ | パラメータと生成列を保持。タイトル変更だけでは同期を失わない |
| 新ファイル → 旧アプリ | 形式上は読めても、GB `DutyMacro` と SNES `VolumeMacro` は無視される。GB sweep／SNES包絡の同音再生・再編集は非互換。旧版で保存すると `sfx` と新マクロを失う |
| 生成列や保存パラメータを手修正 | `editable=false`。`savedParameters` は保存意図であり現在の音を表す値ではない。明示の再生成まで元の音を維持 |
| 未知のschema／generator／乱数版 | 定義のJSONを不透明なデータとして保持し、通常再生・保存は可能。パラメータ編集と再生成は拒否 |

生成列を意図的に置換するときは、まず `arpeggio sfx regenerate jump.arpeggio.json --replace-generated --dry-run --json` で対象件数を確認し、その `revision` を `--expected-revision` に渡して `--dry-run` を外す。曲名以外の生成領域を全置換し、undo 一回で手動編集へ戻せる。`arpeggio sfx detach jump.arpeggio.json --json` は定義だけを除去し、音を保つ。**detach は旧版向け変換ではない。同じ音を旧環境へ配布するときは WAV を使う。**

Phaser・直接音のLPF/HPF、sfxr/Bfxrファイル互換、旧音からのパラメータ逆推定、チップ間変換は対象外。パラメータSFXはNES/GBのPulse＋Noise、SNESの内蔵周期波形＋DSP Noiseに限定する。NES Triangle/DPCM・GB Wave・SNES外部サンプル／16音色バンクはこのエディタの対象外。受け入れの実行状況と実機確認項目は [実装記録の SFX-G1 節](docs/implementation.md) を参照する。

### NSF / VGM 書き出し

```sh
arpeggio export nsf melody.arpeggio.json melody.nsf --author "Composer" --copyright "Owner"
arpeggio export vgm melody.arpeggio.json melody.vgm --loops 2 --dry-run --json
arpeggio export vgm melody.arpeggio.json melody.vgm --overwrite
```

NSF v1 は NTSC NES の Pulse / Triangle / Noise、VGM v1.71 は NES / Game Boy に対応する。DPCM・拡張音源・SNES は対象外。`--loops` は 1〜16（既定 1）の有限展開で、無限ループは保存しない。`--author` は両形式、`--copyright` は NSF 専用。`--tail` と `--sample-rate` は受け付けない。

`--dry-run` は全変換・検証・予定サイズを返し、ファイルと履歴を変更しない。既存出力も診断でき、`destinationExists` で存在を返す。通常保存は既存ファイルを保護し、`--overwrite` で明示置換する。入力と同じパスには保存できない。`--strict` は変換警告があれば exit 1 で保存を拒否する。位相・ミキサー等の恒常的な制限だけでは拒否しない。

通常は要約と制限を stdout、警告・エラーを stderr へ出す。`--json` は成功・失敗とも `report` を含む一つの JSON を返す。`report` は形式・チップ・演奏秒数・予定 byte 数・位置付き診断・全件数・統計を含む。終了コードは 0 / 1 / 2 / 3 を維持し、保存競合は I/O エラー 3。既存 PCM とのビット一致や実機互換性の検証済みを意味しない。

### MIDI 取り込み

```sh
arpeggio import midi melody.mid imported.arpeggio.json --chip nes --dry-run --json
arpeggio import midi melody.mid imported.arpeggio.json --chip gameboy --tempo 120 --quantize-ticks 12
arpeggio import midi melody.mid imported.arpeggio.json --chip snes --channel-map map.json --polyphony drop-new --title "Imported"
```

SMF format 0 / 1、PPQN、MIDI 1.0 を対象に、新規の version 1 JSON を作る。`--chip nes|gameboy|snes` は必須。`--tempo` は 1〜1000、省略時は MIDI の先頭有効テンポを基準とする。テンポ変化は元の実時間を固定 BPM のノート位置へ焼き込む。`--quantize-ticks` は 48 の正の約数（既定 1）。`--polyphony` は `steal-oldest`（既定）または `drop-new`。

`--channel-map` は UTF-8 JSON ファイル。例 `{"1":[0,1],"2":[2],"10":[3]}` のキーは MIDI チャンネル 1〜16、配列は 0 始まりの出力トラック候補。指定チャンネルだけ自動割り当てを上書きし、空配列は除外する。候補の重複・範囲外・NES DPCM・NES / GB の旋律と Noise の相互指定は拒否する。

`--dry-run`・`--strict`・`--json` は上記の書き出しと同じ診断契約。GM 音色はチップ音色／SNES プリセットへ近似するため、通常の旋律でも警告が出る。保存は新規作成だけで、`--overwrite` はない。既存 JSON は保護し、新しい CLI 履歴は空から始める。古い側車の current が同じ JSON でも履歴を引き継がない。履歴を保存できない場合は今回作成した JSON を取り消す。

### MIDI から各形式へ書き出す

```sh
arpeggio import midi melody.mid imported.arpeggio.json --chip nes --title Imported
arpeggio export wav imported.arpeggio.json imported.wav
arpeggio export ogg imported.arpeggio.json imported.ogg
arpeggio export vgm imported.arpeggio.json imported.vgm
arpeggio export nsf imported.arpeggio.json imported.nsf
```

| 取り込み先 | WAV / OGG | VGM v1.71 | NSF v1 |
|---|---|---|---|
| NES | 対応 | 対応 | NTSC 基本 APU |
| Game Boy | 対応 | 対応 | 対象外 |
| SNES | 対応 | 対象外 | 対象外 |

JSON が編集用の正本になる。NSF / VGM の再読み込み・編集、MIDI への逆変換は対象外。NSF は起動可能な `.nes` ROM ではなく、標準バンク切り替えを扱える NSF プレイヤー向けのデータである。

### OGG 書き出しと WAV 取り込み

```sh
arpeggio export ogg melody.arpeggio.json melody.ogg --quality 0.5
arpeggio instrument import-wav drums.arpeggio.json --id 1 kick.wav --root C4 --loop-start 0 --loop-end 0
```

`export ogg` のオプションは `export wav` と同じ＋ `--quality`（-0.1〜1）。`import-wav` は SNES の音色に 16 bit PCM の WAV を埋め込む（ステレオはモノラルへミックス）。`--no-loop` でワンショット。


## DAW

AI による GUI の操作・観測は [Avalon 統合の手順・状態キー・座標計算](docs/avalon.md) を参照。Debug 限定で、隣接 Avalon がある場合に有効化できる。

```sh
arpeggio-daw melody.arpeggio.json
```

- 左: トラック一覧（選択・ミュート）。中央: ピアノロール（クリックで追加、ドラッグで移動、右端ドラッグで長さ、Delete で削除、上下キーで音量、Alt でスナップ解除、Ctrl＋ホイールでズーム）。右: 「編集」（ノートのエフェクト・音色）「解析」「SFX」「書き出し」「MIDI」タブ。下: 再生 / 停止 / ループ / BPM / 長さ / MIDI 取り込み / 書き出し
- Space 再生・停止、Ctrl+S 保存、Ctrl+Z / Ctrl+Shift+Z、Ctrl+E 書き出し（拡張子で WAV / OGG / NSF / VGM。チップに適合する候補だけを表示）
- AI が CLI / MCP で保存すると自動で再読み込みする。未保存の編集がある場合はステータスバーで確認を待つ
- 音声出力は SDL3（macOS / Windows）

### DAW の効果音エディタ

1. 右ペインの「SFX」を開き、「新規候補」で作成を始める。候補のチップとパラメータプリセットを選ぶ。候補の編集・試聴は現在曲・その正本と履歴を変更しない。
2. トーン／ノイズを選び、数値欄またはスライダーで調整する。ラベル・単位・要求時間と実効時間、診断を確認する。ノイズOFF中のノイズ欄は無効。各項目の「初期値へ」とグループの変異ロックも使える。
3. 自動試聴は既定ON。ドラッグを離す、数値をEnter／フォーカス離脱で確定する、キー入力が150 ms止まると一操作として試聴する。「再生」「停止」は常設。試聴音量はモニター専用で、WAV／解析・保存パラメータへ影響しない。
4. 「ランダム」はカテゴリから作り直し、「変異」は現在値のロック外を調整する。詳細にはseedと強度がある。「同じ seed で再生成」はカテゴリ生成であり、直前の変異を繰り返すボタンではない。比較は「元に戻す」「やり直す」で行う。
5. 「新規保存」で新しいJSONへ保存する。**保存だけでは現在曲を切り替えない。** 続けて「保存したSFXを開く」を押す。現在曲に未保存編集・外部競合がある場合は先に保存／再読み込みを行う。候補を保存後に変更すると、最新候補を新規保存するまでOpenは無効になる。
6. 開いたSFXは通常どおり Ctrl/Cmd+S で正本へ保存する。「現在曲を表示」で候補から現在曲へ戻れる。WAV書き出し・解析は未保存候補にも使え、44100 Hz・一回・tail=0。編集後の結果には古いrevisionの表示が付くため、必要なら再実行する。

Tabで移動し、スライダーは左右キー、Shift併用で微調整する。Spaceは文字入力や選択操作を妨げない場所で試聴を切り替え、Escは操作中なら取消、そうでなければ停止する。ドラッグ途中の保存／タブ移動は有効値を確定する。無効入力時は理由を訂正するかEscで戻し、「最後の有効値を再生」で適用済みの音を確認する。

通常ソング再生とSFX試聴は排他で、通常曲を自動再開しない。Undo/Redo・外部変更・文書切替・タブ離脱・終了では試聴を停止する。操作確定から出音100 ms以内は実機での目標であり、未測定の保証値ではない。

「従来のソング雛形」は旧音の候補を作り、パラメータ編集はできない。生成列を手修正したSFXは保存パラメータとして表示する。「生成列の置換内容を確認」から件数を確認して置換するか、「通常ソングとして編集」で定義を外す。変更はUndoで戻せる。

### DAW のチップ書き出し

「書き出し」または Ctrl+E で保存先を選ぶ。NES は NSF / VGM、GB は VGM に対応し、SNES は WAV / OGG を使う。手入力した拡張子もチップ適合性を検証する。

NSF / VGM を選ぶと右ペインの「書き出し」タブで、有限再生回数（1〜16）・著作者・NSF の権利表記・strict を設定できる。「変換を確認」で開始時の曲を診断し、長さ・予定サイズ・位置付き警告／エラー・方式の制限を表示する。「書き出す」は診断した同じ内容を保存する。曲を後から編集しても診断済みの内容は変わらないため、最新の編集を反映するときは再度「変換を確認」を押す。

エラーまたは strict の警告があれば保存できない。設定変更・文書切替では古い診断を破棄する。既存ファイルの上書きは OS の保存ピッカーで確定し、診断後に新しく作られた同名ファイルは上書きしない。診断・保存・音声書き出しの二重開始を拒否し、画面終了時はキャンセルして遅れた通知を抑止する。

### DAW の MIDI 取り込み

1. 下部の「MIDI を取り込む」または右ペインの「MIDI」タブを開く。
2. MIDI 入力と**新規** JSON の保存先、NES / Game Boy / SNES を選ぶ。基準テンポは空欄で MIDI 基準、量子化幅は既定 1。必要なら声数不足時の処理・map ファイル・曲名・strict を設定する。
3. 「変換を確認」で採用・破棄・打ち切り数、実際の MIDI channel → 出力トラック対応、位置付き警告・エラー・制限を確認する。チャンネルは 1 始まり、出力トラックは 0 始まり。
4. 「新規保存」で確認した同じ JSON を保存する。既存ファイルは上書きしない。現在の曲・未保存編集・undo / redo は維持する。
5. 編集対象を切り替えるときだけ「開く」を押す。現文書に未保存編集があれば先に Ctrl+S、外部競合があれば既存の再読み込み操作で解消する。Open に失敗しても、新規保存の成功は取り消さない。

設定変更で候補を破棄する。確認後に元 MIDI や現文書を編集しても候補へは反映しないため、更新を取り込むには再度「変換を確認」を押す。「キャンセル・候補を破棄」や画面終了では進行中の結果公開を取り消す。既に保存が確定したファイルは保持する。通常の旋律でも GM 音色近似の警告が出るため、strict では保存が拒否される場合がある。

### M3 の既知制限と検証状況

NSF / VGM は有限回数の演奏だけを保存し、DPCM・拡張音源・SNES VGM・SPC には対応しない。NSF の PLAY 時刻への量子化、NES Pulse の位相再開、GB の音量段階・再トリガー、位相・LFSR・DAC・ミキサーの差により、通常の WAV / OGG / DAW 再生とのビット一致・同音は保証しない。

MIDI は format 0 / 1 の PPQN ファイル入力のみ。和音数・音域・音色・表情は出力チップの制約に従い、テンポ変化を固定 BPM へ焼き込む。format 2・SMPTE・ライブ入力・MIDI 書き出しは対象外。

**NSF / VGM は実機で未検証。** 自動テストは形式・限定レジスタモデル・限定 6502 実行器を対象とし、実機検証の代替ではない。実機の機種／プレイヤー版／NTSC 動作、長時間テンポ、bank 越え、終了・再 INIT、音源の副作用と聴取差の記録は未実施。今回追加したテストも未実行。


## MCP

復元・ビルド済みの `arpeggio-mcp` 実行ファイルを MCP クライアントの stdio サーバーとして登録する。

```json
{
  "mcpServers": {
    "arpeggio": {
      "command": "/absolute/path/to/arpeggio-mcp",
      "args": []
    }
  }
}
```

ツールは `new_song` / `open_song` / `save_song` / `song_info` / `show_song` / `add_note` / `remove_note` / `update_note` / `apply_operations` / `add_instrument` / `update_instrument` / `remove_instrument` / `export_wav` / `export_ogg` / `undo` / `redo` / `chip_reference` / `analyze_song` / `analyze_wav` / `new_sfx` / `sfx_presets` / `snes_presets` / `import_wav_sample` / `export_nsf` / `export_vgm` / `import_midi`。

パラメータSFX用に `create_sfx` / `sfx_parameter_presets` / `sfx_parameters` / `tweak_sfx` / `randomize_sfx` / `mutate_sfx` / `regenerate_sfx` / `detach_sfx` も使える。従来の `new_sfx` / `sfx_presets` は旧音の契約を維持する。

`new_song` と編集ツールは成功時に自動保存する。`open_song` は現在の曲とセッション履歴を差し替える。MCP の履歴はセッション内に保持し、CLI の履歴とは共有しない。`add_instrument` / `update_instrument` の `instrument` と `apply_operations` の `operations` は JSON **文字列**で渡す。音色更新はオブジェクト全体の置き換え。ノートの `effects` も JSON 配列文字列、省略で既存値を保持し `[]` で解除する。

戻り値は JSON 文字列。ただし `show_song` の既定と `chip_reference` はテキストをそのまま返す。入力・操作の失敗は `{"error":"説明","exitCode":1}` などの JSON 文字列を返す。

### MCP の効果音調整

`create_sfx(path, chip="nes", preset="laser")` → `open_song(path)` → `sfx_parameters(includeSchema=true)` → `tweak_sfx(parameters, expectedRevision)` → `analyze_song(windowMs=20)` → `export_wav(path, loops=1, sampleRate=44100, tail=0)` → `analyze_wav(path, windowMs=20)` の順で調整する。`create_sfx` は保存だけで現在セッションを変えない。旧 `new_sfx` は保存後にその曲を開く。

`tweak_sfx` の `parameters` は `"{\"tone\":{\"slideSemitonesPerSecond\":-12,\"envelope\":{\"decaySeconds\":0.2}}}"` のような JSON **文字列**。`expectedRevision` は直前の `sfx_parameters` の `revision`、`dryRun=true` は予行。未オープンで初期値・schemaを取得する場合は `sfx_parameters(chip="gameboy")` とチップを明示する。

`randomize_sfx(category, seed)`、`mutate_sfx(seed, strength=0.1, locks="[\"tone.baseFrequencyHz\"]")` で探索する。`regenerate_sfx(replaceGenerated=true, dryRun=true)` で置換対象を確認し、revisionを指定して適用する。`detach_sfx()` は音を保って定義だけ外す。編集は共有セッションで直列化し、成功時に自動保存＋一履歴、同値・予行・失敗時は変更しない。戻り値とエラーの安定コードはCLIと共通で、MCPもJSON文字列を返す。

### MCP のチップ書き出し・MIDI 取り込み

| ツール | 引数 |
|---|---|
| `export_nsf` | `path, loops=1, author="", copyright="", strict=false, dryRun=false, overwrite=false` |
| `export_vgm` | `path, loops=1, author="", strict=false, dryRun=false, overwrite=false` |
| `import_midi` | `midiPath, path, chip, tempo=null, quantizeTicks=1, polyphony="steal-oldest", channelMap=null, title=null, strict=false, dryRun=false` |

export は現在開いている曲を対象とする。NSF は NES、VGM は NES / GB に対応する。`loops` は 1〜16。`channelMap` は `"{\"1\":[0,1],\"10\":[3]}"` のような JSON **文字列**で渡す。MIDI は新規保存だけで、曲を開かず実行できる。現在の曲・保存先・undo / redo は維持し、切り替えるときだけ別途 `open_song(path)` を呼ぶ。

三ツールは `path / written / dryRun / destinationExists / exitCode / report` を返す。失敗時は `code / error` も含む。`report` は変換時間・予定バイト数・位置付き警告／エラー・恒常的な制限・統計を含む。終了コードは CLI と同じ 0 / 1 / 2 / 3。`dryRun` は全変換を診断して保存せず、既存出力があっても診断できる。`strict` は変換警告時に保存を拒否する。NSF / VGM の既存出力は `overwrite=true` でのみ置換し、入力と同じパスは拒否する。


## SNES 内蔵音色バンク

WAV 素材なしで 16 音色を使える。保存するのはプリセット名と設定値だけで、サンプルは固定シードの合成から生成し、BRR 往復・ガウス補間・DSP ADSR を通して再生する。

```sh
arpeggio instrument presets snes
arpeggio new orchestra.arpeggio.json --chip snes --bank orchestral
arpeggio instrument add orchestra.arpeggio.json --kind SnesSample --preset strings --name str
arpeggio instrument set orchestra.arpeggio.json --id 1 --preset brass
arpeggio instrument set orchestra.arpeggio.json --id 1 --preset organ --echo-send 0.2 --adsr-registers 15,0,7,0
```

- 持続系: `strings` / `brass` / `organ` / `choir` / `flute` / `lead` / `bass`
- 減衰系: `piano` / `pluck` / `bell`
- ドラム: `kick` / `snare` / `hat`（closed）/ `openhat` / `tom` / `crash`

`--bank orchestral|band|chip` は 8 トラックに音色を割り当てる。ノート追加時の `--instrument` 省略でも各トラックの音色が選ばれる。未指定時は従来の音色一つで作成する。

| bank | トラック順 |
|---|---|
| orchestral | strings / brass / flute / choir / bass / kick / snare / hat |
| band | lead / organ / pluck / bass / piano / kick / snare / hat |
| chip | lead / lead / bass / organ / bell / kick / snare / hat |

`--preset` の差し替えでは推奨 ADSR・ルート音・ループ・EchoSend を再適用し、同じコマンドの明示オプションを優先する。`--root C4`、`--loop false`、`--echo-send 0.3`、`--adsr-registers 15,3,6,0` で調整できる。`--adsr` はレジスタを解除して秒指定に戻す。DSP の release は固定約 8 ms。WAV 取り込みは Preset を解除し、プリセットへの差し替えは SampleData を解除する。

MCP では `snes_presets()`、`new_song(path: "orchestra.arpeggio.json", chip: "snes", bank: "orchestral")` を使う。`add_instrument` / `update_instrument` の `instrument` JSON 文字列は次の内容で指定できる。`preset` と `sampleData` の同時指定・未知のプリセット名はエラーになる。

```json
{"kind":"SnesSample","id":9,"name":"str","preset":"strings"}
```

4 小節・各トラック 4 音の例は `examples/snes-demo.arpeggio.json`。次の手順で同梱バッチから別パスへ再生成できる。

```sh
arpeggio new /tmp/snes-demo.arpeggio.json --chip snes --bank orchestral --length-beats 16 --title snes-demo
arpeggio apply /tmp/snes-demo.arpeggio.json --operations examples/snes-demo-ops.json
arpeggio export wav /tmp/snes-demo.arpeggio.json /tmp/snes-demo.wav --sample-rate 32000 --tail 0
```

デモ再生成・WAV の非無音／無クリップ確認は `SnesBankCommandsTests` に含む。今回の実装ではビルド・テスト・WAV 書き出しは未実行。

作曲指示書（`arpeggio brief`。[design-brief.md](design-brief.md)）から `arpeggio-compose` skill で AI に作らせた例が
`examples/brief-demo.arpeggio.json`。元の指示書は `examples/brief-demo.brief.json`、生成に使ったバッチ操作は
`examples/brief-demo-ops.json`（本体）と `examples/brief-demo-refinement-ops.json`（`analyze` の指摘を受けた微調整）。

```sh
arpeggio new /tmp/brief-demo.arpeggio.json --chip nes --tempo 96 --length-beats 176 --title "廃墟の朝"
arpeggio apply /tmp/brief-demo.arpeggio.json --operations examples/brief-demo-ops.json
arpeggio apply /tmp/brief-demo.arpeggio.json --operations examples/brief-demo-refinement-ops.json
arpeggio analyze /tmp/brief-demo.arpeggio.json
```

44 小節（イントロ8→メイン16×2→アウトロ4）・110 秒。`analyze` はクリップ 0・無音割合 0% を返す。
