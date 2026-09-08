# Arpeggio

ファミコン（NES）・ゲームボーイ（DMG）・スーパーファミコン（SPC700 風）の曲と効果音を、AI と人が一緒に作るチップチューン DAW。

- `Arpeggio.Core` — 曲データモデル・3 チップの合成エンジン・WAV 書き出し（.NET 10、Unity 非依存）
- `arpeggio` — CLI。AI が曲データ（`.arpeggio.json`）を編集・書き出しする
- `arpeggio-mcp` — stdio MCP サーバー。Claude Code / Codex から直接叩く
- `arpeggio-daw` — Avalonia 製 DAW。ピアノロールで人が再生・微調整する（macOS / Windows）

設計の正本は [docs/design.md](docs/design.md)。マイルストーンと未決事項もそこに置く。

## 状態

M1（Core / CLI / MCP / DAW）と M2（効果音プリセット・音声解析・OGG 書き出し・SNES への WAV 取り込み・DAW の効果音／解析パネル）を実装済み。次は M3（NSF / VGM 書き出し・MIDI 取り込み）。

## AI 向けの基本手順

1. `arpeggio chip-reference nes` でチップの制約（チャンネル構成・音域・音色 kind・エフェクトの単位）を読む
2. `arpeggio new` で曲、または `arpeggio sfx new --preset jump` で効果音の雛形を作る
3. `note add` / `instrument add` / `apply` で打ち込む。`--instrument` を省略するとトラックのチャンネルに合う音色を自動で選ぶ
4. `show` で曲の形を目視し、`export wav` で書き出す
5. `analyze` で音量・周波数・無音・クリップを数値で確認し、直す
6. 人に渡すときは `arpeggio-daw <path>` で開いてもらう。DAW での保存は CLI / MCP 側からそのまま読める

## ビルド

```sh
dotnet build Arpeggio.slnx -nologo -v q -clp:ErrorsOnly
dotnet test Arpeggio.slnx -nologo -v q
```

## 配布ビルド

`.NET 10 SDK` と NuGet への接続がある環境で実行する。配布物には .NET ランタイム、Avalonia、SDL3 のネイティブ依存を含めるため、利用者の SDK／ランタイムのインストールは不要。単一ファイル化・トリミング・AOT は使わない。

```sh
# macOS 上で実行。Apple Silicon と Intel は別々の配布物にする。
bash tools/build_app.sh osx-arm64 1.0.0 1
bash tools/build_app.sh osx-x64 1.0.0 1

# Windows 用も同じスクリプトでクロス publish できる。
bash tools/build_app.sh win-x64 1.0.0 1
bash tools/build_app.sh win-arm64 1.0.0 1
```

引数は RID・バージョン・ビルド番号。後ろ二つは省略すると `1.0.0`・`1` になる。macOS のパッケージ化には macOS 標準の `plutil`・`codesign`・`ditto` を使う。Windows ZIP の生成には `zip` と `curl` が必要で、macOS／Linux／Windows の Git Bash で実行できる。Windows 上でビルドする場合も .NET 10 SDK・`zip`・`curl` を PATH に用意する。

| 対象 | 展開済みの出力 | 配布用 ZIP（上の指定の場合） |
|---|---|---|
| macOS Apple Silicon | `artifacts/osx-arm64/Arpeggio.app` | `artifacts/Arpeggio-1.0.0-1-osx-arm64.zip` |
| macOS Intel | `artifacts/osx-x64/Arpeggio.app` | `artifacts/Arpeggio-1.0.0-1-osx-x64.zip` |
| Windows x64 | `artifacts/win-x64/Arpeggio/` | `artifacts/Arpeggio-1.0.0-1-win-x64.zip` |
| Windows ARM64 | `artifacts/win-arm64/Arpeggio/` | `artifacts/Arpeggio-1.0.0-1-win-arm64.zip` |

再実行すると同じ RID の展開済み出力と同名 ZIP を置き換える。publish・パッケージ化に失敗した場合は前回の配布物を保持する。macOS は .NET 10 の対応範囲に合わせて **macOS 14 以降**を対象とする。[.NET 10 対応 OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)

macOS は ZIP を展開し、`Arpeggio.app` を `/Applications` に移して起動する。バンドルは `Contents/MacOS/` に publish 出力一式、`Contents/Resources/arpeggio.icns` にアイコン、`Contents/Info.plist` にアプリ情報を持つ。Windows は ZIP **全体**を展開して `Arpeggio/arpeggio-daw.exe` を起動する。exe だけを取り出さない。Windows アイコンは既存の `ApplicationIcon` を apphost に反映する。

Windows の初回起動前に、同梱の `Arpeggio/Prerequisites/vc_redist.x64.exe`（ARM64 版では `vc_redist.arm64.exe`）を実行する。SDL3 が必要とする **Visual C++ v14 ランタイム**の導入用で、新しい対応ランタイムが導入済みなら不要。ビルドスクリプトは Microsoft の公式 URL からビルド時の最新版インストーラーを取得して ZIP に含める。導入時は管理者権限を求められる場合がある。[Microsoft のランタイム配布情報](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)

```powershell
# Windows の PowerShell。ファイルを exe へドロップしても同じ起動引数になる。
& .\Arpeggio\arpeggio-daw.exe "C:\Music\song.arpeggio.json"
```

ファイル指定なしの起動では、同梱の SNES デモをユーザーデータ領域の `Arpeggio/welcome.arpeggio.json` へ初回だけコピーして開く。通常は macOS の `~/Library/Application Support/Arpeggio/`、Windows の `%LOCALAPPDATA%\Arpeggio\`。保存した編集は次回も残る。バンドルや exe の隣へ書き込まない。

### 未署名配布と初回起動

Developer ID 署名・公証、Windows の Authenticode 署名は行わない。macOS のスクリプトは Apple Silicon での実行用に **ad-hoc 署名**を付けるが、開発元を証明する署名ではなく、Gatekeeper の警告は解消しない。

信頼できる配布物であることを確認し、初回は Finder でアプリを右クリック →「開く」を試す。現行 macOS で許可できない場合は、一度起動を試した後に「システム設定 → プライバシーとセキュリティ → このまま開く」を使う。[Apple の初回起動手順](https://support.apple.com/ja-jp/102445)

手元で検証する配布物の quarantine を明示的に解除する場合は、対象のアプリだけを指定する。スクリプトからは自動解除しない。

```sh
xattr -d com.apple.quarantine "/Applications/Arpeggio.app"
# 内部ファイルにも付いている場合のみ再帰的に解除する。
xattr -dr com.apple.quarantine "/Applications/Arpeggio.app"
```

Windows でも未署名のため SmartScreen の警告が出る場合がある。

### 曲ファイルの関連付け

macOS は `CFBundleDocumentTypes` と UTI `dev.pisuke.arpeggio.song` に `.arpeggio.json` を登録する。Finder のダブルクリック／Dock のアプリアイコンへのドロップは、起動済みの場合も Avalonia のファイル通知から `MainWindowPresenter.Open` → `DawDocument.Open` に渡る。CLI の `arpeggio-daw <path>` と同じ読み込み・監視・再生準備を使う。

複合拡張子が `public.json` と判定されたり、既存の JSON アプリが優先されたりする場合に備え、JSON の代替ハンドラーも登録する。その場合は対象ファイルの「情報を見る → このアプリケーションで開く」で Arpeggio を選び、以後はそのファイルをダブルクリックする。一般の `.json` 全体の関連付けを変えないよう「すべてを変更」は押さない。アプリ側では `.arpeggio.json` 以外を拒否する。Windows ZIP は関連付けのレジストリを変更しないため、exe へのドロップか上記コマンドで開く。

現在の DAW は一文書なので、曲は一つずつ開く。複数ファイルの一括ドロップはエラー表示し、未保存の編集があるときの別文書への切り替えも拒否する。保存してから再度開く。同じパスの通知は前面化だけを行い、未保存の編集を再読み込みで失わない。

### macOS の前面化・受け入れ確認（依頼者が実行）

同じ Bundle ID の古い Arpeggio が起動していない状態で、確認対象の `.app` を `/Applications` に置く。以下は実装時には実行していない。

```sh
open "/Applications/Arpeggio.app"
```

ウィンドウが表示されたらターミナルを前面にし、次を一度に実行する。前面化の反映を待ってから同じスクリプト内で照合する。

```sh
osascript \
  -e 'tell application id "dev.pisuke.arpeggio" to activate' \
  -e 'delay 1' \
  -e 'tell application "System Events" to get bundle identifier of first application process whose frontmost is true'
```

`-1728` なしで成功し、Arpeggio のウィンドウが前面に現れ、`dev.pisuke.arpeggio` を返すことを確認する。Automation の許可を求められた場合は実行元のターミナル／自動化アプリに許可する。クリック・キー送信には、その実行元のアクセシビリティ権限も必要。

ファイル通知だけをコマンドで確認する場合は `--args` を付けずに渡す。

```sh
open -a "/Applications/Arpeggio.app" "/absolute/path/song.arpeggio.json"
```

| 確認 | 期待結果 |
|---|---|
| アプリ未起動で曲をダブルクリック | 指定した曲のタイトル・ノートが表示される |
| 起動済み／最小化中に別の曲を Dock へドロップ | ウィンドウが復帰し、指定曲へ切り替わる |
| 空白・日本語・`#` を含むパス | ファイル URL が復号され、指定曲を開ける |
| 編集中に別の曲を開く／同じ曲を開く | 別の曲は拒否、同じ曲は前面化のみ。編集内容は維持 |
| 不正 JSON・存在しないファイル・通常の `.json`・複数ドロップ | ステータスにエラー、現在の曲は維持 |
| NES／GB／SNES の再生と保存、外部更新の再読み込み | SDL3 の読み込み・音声出力・既存の編集機能が動作 |
| Windows のクリーンな環境で ZIP を展開し、同梱 VC++ ランタイムを導入して起動 | .NET の追加インストールなしで動作し、exe のアイコンを表示 |

配布前には次も実行する。今回の実装では **ビルド・テスト・publish・アプリ起動は未実行**で、警告ゼロ／既存 676 件の成功は未確認。

```sh
dotnet build Arpeggio.slnx
dotnet test Arpeggio.slnx
```

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

```sh
arpeggio sfx list
arpeggio sfx new jump.arpeggio.json --preset jump --chip nes
arpeggio export wav jump.arpeggio.json jump.wav --tail 0
arpeggio analyze jump.arpeggio.json            # ソングをレンダリングして解析（--track N でソロ）
arpeggio analyze wav jump.wav --window-ms 100  # 既存 WAV を解析
```

プリセットは `jump / coin / hit / explosion / powerup / laser / blip / select`。`analyze` は長さ・RMS・ピーク・クリップ数・無音割合・左右差・帯域比率・窓ごとの支配的周波数と音名、および警告（クリップ・小音量・先頭無音・末尾無音・左右差）と合成側の警告（音域クランプ等）を返す。`--json` で全窓を取得できる。

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

- 左: トラック一覧（選択・ミュート）。中央: ピアノロール（クリックで追加、ドラッグで移動、右端ドラッグで長さ、Delete で削除、上下キーで音量、Alt でスナップ解除、Ctrl＋ホイールでズーム）。右: 「編集」（ノートのエフェクト・音色）「解析」「SFX」タブ。下: 再生 / 停止 / ループ / BPM / 長さ / 書き出し
- Space 再生・停止、Ctrl+S 保存、Ctrl+Z / Ctrl+Shift+Z、Ctrl+E 書き出し（拡張子で WAV / OGG）
- AI が CLI / MCP で保存すると自動で再読み込みする。未保存の編集がある場合はステータスバーで確認を待つ
- 音声出力は SDL3（macOS / Windows）

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

ツールは `new_song` / `open_song` / `save_song` / `song_info` / `show_song` / `add_note` / `remove_note` / `update_note` / `apply_operations` / `add_instrument` / `update_instrument` / `remove_instrument` / `export_wav` / `export_ogg` / `undo` / `redo` / `chip_reference` / `analyze_song` / `analyze_wav` / `new_sfx` / `sfx_presets` / `snes_presets` / `import_wav_sample`。

`new_song` と編集ツールは成功時に自動保存する。`open_song` は現在の曲とセッション履歴を差し替える。MCP の履歴はセッション内に保持し、CLI の履歴とは共有しない。`add_instrument` / `update_instrument` の `instrument` と `apply_operations` の `operations` は JSON **文字列**で渡す。音色更新はオブジェクト全体の置き換え。ノートの `effects` も JSON 配列文字列、省略で既存値を保持し `[]` で解除する。

戻り値は JSON 文字列。ただし `show_song` の既定と `chip_reference` はテキストをそのまま返す。入力・操作の失敗は `{"error":"説明","exitCode":1}` などの JSON 文字列を返す。

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
