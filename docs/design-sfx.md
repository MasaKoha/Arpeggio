# Arpeggio SFX エディタ設計書

2026-09-09 時点の実装に対して、sfxr / Bfxr 式の効果音エディタを設計する。本書は SFX パラメータ編集の追加仕様の正本とする。[design.md](design.md) の短いソング・合成・保存・履歴の規則と、[design-m3.md](design-m3.md) の書き出し境界を継承する。本設計ランの成果物は本書だけであり、以下の追加項目・型・コマンドは**今後実装する仕様**である。

## 決定事項

| 項目 | 決定 | 根拠・工数判断 |
|---|---|---|
| 編集方式 | **共通パラメータ＋チップ固有設定**。トーン一声とノイズ一声をスライダーで直接編集する | 同じ単位で AI と人が調整でき、音階とノイズ周期を混同しない。汎用の多声 SFX シーケンサーには広げない |
| 対象 | NES / Game Boy / SNES。NES / GB のトーンは Pulse、SNES は内蔵周期波形。ノイズは各チップの LFSR | 既存 SFX と同じ二声で jump / hit / explosion を作れる。NES Triangle・DPCM、GB Wave、SNES 外部サンプル・音色バンクは通常の編集タブに残す |
| sfxr との関係 | 操作方式と音作りの概念を採用する。原典の非線形な内部パラメータ値、ファイル形式、PCM は互換にしない | Hz・秒・半音/秒を公開する方が調整量を説明できる。チップ量子化と 60 Hz 制御を優先する |
| 生成 | **パラメータを 60 Hz マクロへ展開し、通常の SongRenderer で鳴らす**。SFX 専用の別合成器は作らない | DAW / WAV / analyze / チップ書き出しが同じ生成結果を扱える。既存 NoteEffectKind の意味を変えない |
| 必要な合成拡張 | `GbPulseInstrument.DutyMacro` と `SnesSampleInstrument.VolumeMacro` を optional で追加する | GB の段階デューティ変化と、SNES の ASDecay・punch を位相再開なしで表すため。既存 VoiceModulation を使い、効果 enum や DSP ADSR を増やさない |
| 捨てるもの | Phaser、共通の LPF / HPF とその sweep / resonance、最小周波数による自動停止 | 既存の発音制御では表せない。SNES エコー FIR を直接音のフィルターに見せかけない。音長は包絡で明示する |
| 再編集 | **パラメータと生成済み音色・ノートを両方保存**する。パラメータは作成意図、生成列は再生入力 | プリセット名だけでは微調整を戻せない。読み込み時に再生成して既存音を変えない |
| 保存形式 | Song の `version: 1` を維持し、optional な `sfx` と上記二つのマクロだけを追加する | M2 の追加方式に揃える。ただし旧アプリによる新マクロの同音再生は保証しない。互換性を後述の方向別契約で明示する |
| プリセット | 現行の **8 種を legacy として維持**し、同じ用途のパラメータプリセット 8 種を追加する | この worktree の SfxPresetCatalog は 8 種。16 種あるのは SnesInstrumentCatalog。多音の旧 coin / powerup は新しい単一音の曲線へ無損失変換できない |
| ランダム | カテゴリ生成と小さな変異を両方採用。独自の固定 PRNG・シード・アルゴリズム版・全パラメータを保存 | 再試行・比較・不具合再現を可能にし、ランタイムの System.Random 実装へ依存させない |
| UI | 既存の右ペイン「SFX」タブを作り替える。**操作確定時の自動試聴を既定 ON**、手動再生・停止も常設 | ドラッグを離すと直ちに一回鳴る。動かし続ける間の再トリガー連打を避ける |
| 文書境界 | 未保存の作成候補で試聴できる。保存と現在曲を開き替える操作を分離する | 現 SfxCreationPresenter の自動保存付き切替は新 UI へ引き継がない。既存曲を開いている状態でも安全に音を探せる |
| 実装 | Core → 保存・履歴 → CLI / MCP → DAW の順に、40 分以下の独立した受け入れ単位に分割する | MVP と既存プロジェクト構成を使う。新アセンブリ・Clean Architecture・汎用 DSP グラフは不要 |

### 判断の根拠となる現状

以下のパスは `src/Arpeggio.Core/` 相対。名称やドキュメントの説明だけでなく、合成側で値を読む箇所を確認した。[implementation.md](implementation.md) の M1-A/B/C、M2-A/C、M2-E-A/B/C、M3 の制御列・書き出し・DAW の判断を併せて参照する。

| コード | 確認した事実 | 設計への反映 |
|---|---|---|
| `Sfx/SfxPresetCatalog.cs` / `SfxPresetFactory.cs` | jump / coin / hit / explosion / powerup / laser / blip / select の 8 種。テンポ150。トーン＋ノイズ、ノート効果で生成 | 16 種を存在するものとして移行しない。旧 factory は維持する |
| `Instruments/Snes/SnesInstrumentCatalog.cs` | strings から crash までの 16 音色。効果音ソングのカタログではない | SFX プリセットと音色プリセットを別の用語で表示する |
| `Synthesis/VoiceModulation.cs` | PitchMacro はセント、ArpeggioMacro は半音。PitchSlide はノート全期間の総半音差。Vibrato は深さだけ指定でき速度は6 Hz。Arpeggio 効果は `[0,x,y]` を3フレーム周期で反復 | 半音/秒の slide をそのまま PitchSlide.Value に入れない。可変速度と曲線はマクロへ展開する |
| `Synthesis/Nes/` | Pulse は全4マクロ、Noise は音量・ピッチ。Triangle の ReadSample は Volume を読まず固定音量。DPCM は無音 | Triangle に包絡が使えると表示しない。DPCM を SFX 選択肢にしない |
| `Synthesis/GameBoy/` | Pulse は初期音量×Volume、DutyMacro 入力なし。Wave は音量マクロなし。Noise の選択値は平均律ではない | Pulse の hardware envelope は固定し、自由包絡を VolumeMacro に一本化。DutyMacro の接続を追加する |
| `Synthesis/Snes/SnesVoiceSynthesizer.cs` / `SnesEnvelope.cs` | 音量マクロを Configure していない。秒 ADSR はレジスタへ量子化、release は最大約8 msの固定速度。NoiseEnabled 中は音程からノイズ速度を変えない | SFX decay を ReleaseSeconds に代入しない。VolumeMacro を追加し、DSP ADSR は最速 attack＋全量保持に固定する |
| `Synthesis/Snes/SnesWaveformBuilder.cs` | Pulse=25%、Square=50%、内蔵128サンプルの周期波形。Waveform.Noise はループするサンプル | 新 SFX ノイズには NoiseEnabled を使い、旧 SNES SFX の周期ノイズサンプルと区別する |
| `Document/SongSerializer.cs` | version=1だけを受理。既知型へ復元すると未知プロパティは保持しない。保存は一時ファイルの置換 | 旧版は sfx を読み飛ばして保存時に失う。新コードでは未知 sfx 版の保持を明示実装する |
| `Session/SongSnapshotPublisher.cs` / DAW `Editing/DawDocument.cs` | Song のフィールドを個別にコピーする。JSON への追加だけでは公開・正本保存に流れない | 両コピー境界、undo/redo、ファイル監視の同値判定まで受け入れ条件に含める |
| `../Arpeggio.Formats/Export/ControlTrackCursor.cs` | GB の制御列も DutyMacro を渡していない | 合成器だけ直して VGM の duty が固定になる接続漏れを防ぐ |

## 用語

- **SFX 定義**: `sfx` に保存する、版・パラメータ・出自・生成結果の指紋。1 Song に最大一つ。
- **パラメータプリセット**: SFX 定義の初期値一式。名前の参照で再生する SNES 音色プリセットとは異なる。
- **トーン／ノイズ**: 同時に開始する二つのレイヤー。音量と包絡は独立。任意の追加レイヤーや遅延開始は今回の対象外。
- **制御フレーム**: 曲全体の 1/60 秒。テンポ150・48 ticks/beat では2 tick。オーディオの左右一組のフレームとは別。
- **sustain / decay**: 本書ではそれぞれ「保持時間」「保持後にゼロまで下がる時間」。ADSR の sustain level / decay to sustain level ではない。
- **音程ジャンプ**: 原典 sfxr の arpeggio/change。待ち時間後に一度だけ音程を変える。既存 Arpeggio 効果の高速3音循環とは区別する。
- **repeat**: 音量包絡を進めたまま、スライド・音程ジャンプ・デューティの時間だけを先頭へ戻す。ノートの再打鍵やソングのループではない。
- **同期済み／生成列を編集済み**: 保存した生成指紋と現在の生成領域が一致するか。後者の音からパラメータを推定しない。

## パラメータ仕様

### 原典パラメータの採否

原典の repeat は周波数・デューティを周期的に戻し、包絡は継続する。arpeggio は待ち時間後の音程変更である。これらの意味は [DrPetter の sfxr README](https://raw.githubusercontent.com/grimfang4/sfxr/master/sfxr/README.txt) で確認した。Bfxr の追加波形・二段 pitch change・独自の合成処理は本仕様へ自動的に取り込まない。[Bfxr の合成実装](https://github.com/increpare/bfxr/blob/master/src/com/increpare/bfxr/synthesis/Synthesizer/SfxrSynth.as)

「現行」はこの worktree の実装、「追加後」は本設計の二つの optional マクロ接続後を指す。チップで表せることと、現在のモデルに入力口があることを分ける。

| 原典の項目 | NES | GB | SNES | 採否・写像・差 |
|---|---|---|---|---|
| base frequency | Pulse / Triangle の周期に量子化 | Pulse / Wave の周期に量子化 | 内蔵波形の14 bit pitchに量子化 | 採用。Hz → 基準ノート＋セント。SFX のトーン選択範囲は前述どおり |
| slide | PitchMacro / PitchSlide | 同左 | 同左、DSPノイズには無効 | 採用。半音/秒の直線を PitchMacro へ。原典の周期倍率の式は採用しない |
| delta slide | PitchMacro へ曲線を展開できる | 同左 | 同左 | 採用。半音/秒²の加速度。新 NoteEffectKind は不要 |
| square duty | Pulse の4段階 | Pulse の4段階 | 内蔵 Pulse 25% / Square 50%のみ | NES / GB の固有項目。SNES は波形選択に写像し、連続 duty は捨てる |
| duty sweep | DutyMacro で段階変化 | チップは段階変化可能だが現行モデルは未対応 | 動的な周期波形の差替え口なし | NES 採用、GB は DutyMacro を追加して採用。SNES は捨てる。音色の分割再打鍵で位相を戻さない |
| vibrato depth / speed | PitchMacro で任意速度 | 同左 | 同左、DSPノイズには無効 | 両方採用。既存 Vibrato は6 Hz固定なので生成しない |
| attack / sustain / decay | Pulse / Noise は VolumeMacro。Triangle は固定音量 | Pulse / Noise は VolumeMacro。Wave は自由包絡マクロなし | 現 ADSR だけでは ASDecay の時系列を独立制御できない | トーン・ノイズに採用。SNES VolumeMacro を追加。Triangle / Wave の包絡を擬似的な NoteOn 列で作らない |
| sustain punch | 音量マクロの相対ピークで近似 | 同左 | 追加する音量マクロで近似 | 採用。ピークを保ったまま保持冒頭を相対的に強くする。クリップを伴う原典の増幅はしない |
| repeat speed | マクロ列へ周期的な再計算を展開 | 同左 | トーンのみ同左 | 採用。周期を秒で指定し、包絡・位相・ビブラートはリセットしない |
| phaser offset / sweep | 対応する遅延経路なし | 同左 | Echo は16 ms刻みの残響経路 | 捨てる。Echo は原典の短い可変遅延と同等でない。Pulse 二声の detune にも置き換えない |
| low-pass cutoff / sweep / resonance | 発音後の可変フィルターなし | 同左 | FIR はエコー戻り音だけ | 捨てる。sine への変更は音色選択でありフィルターの代用とは表示しない |
| high-pass cutoff / sweep | 対応なし | 同左 | エコー用 FIR のみ | 捨てる。直接音まで HPF が掛かるという誤認を作らない |
| arpeggio amount / speed | ArpeggioMacro で一回の段差 | 同左 | トーンは同左 | 採用。半音差＋待ち秒数。3音循環効果へ勝手に変換しない |
| min frequency / cutoff | 音域クランプはあるが自動停止はない | 同左 | 同左 | 今回は捨てる。下限通過で音長が暗黙に変わる機能は足さず、全軌跡のクランプを診断する |

SNES のエコー／FIR・ピッチ変調、GB の任意 Wave RAM、NES Triangle の固定音量は通常エディタで引き続き使える。SNES はフィルター済みの素材をサンプルとして鳴らすこと自体は可能だが、現在の音色・効果入力で直接音へ可変フィルターを掛けられることとは別である。パラメータ SFX では素材への後処理焼き込みを採用せず、対応外の値を隠れて音へ足さない。

### 構造・単位・既定値

`parameters` は `tone`、`noise`、現在のチップ一つの固有オブジェクト（`nes` / `gameBoy` / `snes`）からなる。チップは Song.Chip を正とし二重保存しない。`tone.envelope` と `noise.envelope` は同じ仕様の値オブジェクト。異なるチップの固有オブジェクトの同時指定、未知キー、型違い、非有限数、範囲外は拒否する。

数値欄は実数を受理する。小数は小数点以下6桁へ AwayFromZero で正規化して保存する。音量・半音ジャンプ・選択値・seed は整数。表の刻みは UI キー操作の刻みであり、秒数は保存前にフレームへ置き換えず、要求秒数と実効フレーム数を併記する。CLI / MCP は表示値より細かい小数も上記6桁まで扱える。

| 共通パス | 範囲・単位 | 初期値 | UI 刻み・意味 |
|---|---|---|---|
| `tone.enabled` | bool | true | トーン発音の有無 |
| `tone.baseFrequencyHz` | 20〜12000 Hz | 440 | 対数スライダー。キーは1半音、微調整は1 cent。数値直接入力可 |
| `tone.slideSemitonesPerSecond` | -360〜360 半音/秒 | 0 | 1。正は上昇 |
| `tone.deltaSlideSemitonesPerSecondSquared` | -1440〜1440 半音/秒² | 0 | 1。スライド速度の変化 |
| `tone.vibratoDepthCents` | 0〜200 cent | 0 | 1。片振幅 |
| `tone.vibratoSpeedHz` | 0〜20 Hz | 6 | 0.1。0は揺れなし、開始位相0 |
| `tone.pitchChangeSemitones` | -24〜24 半音 | 0 | 1。0はジャンプなし |
| `tone.pitchChangeTimeSeconds` | 0〜5 秒 | 0.05 | 1/60秒。0なら発音先頭で変更 |
| `tone.repeatPeriodSeconds` | 0、または 1/60〜5 秒 | 0 | 1/60秒。0は無効。「速度」でなく周期と表示 |
| `noise.enabled` | bool | false | トーンと同時発音。トーンなしのノイズ単独も可 |
| `tone.envelope.volume` / `noise.envelope.volume` | 0〜15 | 12 / 12 | 1。レイヤーのピーク音量。マスター音量ではない |
| 各 `envelope.attackSeconds` | 0〜1 秒 | 0 | 1/60秒 |
| 各 `envelope.sustainSeconds` | 0〜2 秒 | 0.05 | 1/60秒。保持の長さ |
| 各 `envelope.decaySeconds` | 1/60〜2 秒 | 0.15 | 1/60秒。最低1制御フレームの減衰を確保 |
| 各 `envelope.punch` | 0〜1 | 0 | 0.01。保持冒頭の相対的な強調 |

有効レイヤーは最低一つ必要。volume=0 は有効な編集値として許可し、全有効レイヤーが0なら `SilentParameters` 警告を出す。無効レイヤーの入力も保存・検証するが生成・音長計算には使わない。punch>0 は量子化後の sustain が1フレーム以上ある場合だけ有効とし、それ以外は入力エラーにする。

| チップ固有パス | 範囲・初期値 | 生成先・制限 |
|---|---|---|
| `nes.dutyPercent` | 12.5 / 25 / 50 / 75、初期25 | Pulse の DutyCycle |
| `nes.dutySweepPercentPerSecond` | -100〜100 %ポイント/秒、初期0 | 連続目標を上記4段階へ丸める |
| `nes.noiseMode` | `long` / `short`、初期long | NoiseMode。発音中は固定 |
| `nes.noisePeriodIndex` | 0〜15、初期12 | NES の16周期表の添字。大きいほど遅いクロック |
| `nes.noiseSlideIndicesPerSecond` | -60〜60 添字/秒、初期0 | 添字を0〜15へ飽和。Modulo による折返しを作らない |
| `gameBoy.dutyPercent` / `dutySweepPercentPerSecond` | NES と同じ | 追加する GbPulseInstrument.DutyMacro |
| `gameBoy.noiseWidth` | 7 / 15、初期15 | LfsrWidth |
| `gameBoy.noiseSelection` | 0〜127、初期96 | 既存 GB 合成器の選択値。NR43 の raw byte ではない |
| `gameBoy.noiseSlideSelectionsPerSecond` | -240〜240 選択値/秒、初期0 | 選択値を0〜127へ飽和。値の大小と明るさの単調関係は保証しない |
| `snes.waveform` | `sine` / `square` / `saw` / `triangle` / `pulse`、初期pulse | 既存 SnesWaveformKind。常に Loop=true。pulse=25%、square=50% |
| `snes.noiseRate` | 1〜31、初期24 | NoiseEnabled=true の固定 NoiseRate。0の停止は noise.enabled=false で表す |

SNES の noise slide・幅・duty sweep は提供しない。チップを変更する汎用 tweak も提供しない。DAW のチップ変更は新しい作成候補を、そのチップの選択中プリセットから作り直す一操作とし Undo で戻せる。既存パラメータ SFX のチップ変更は別の新規候補として扱い、保存済み Song を自動変換しない。

### 時間・ピッチ・包絡の規範

1. 各レイヤーの attack / sustain / decay を `round(seconds × 60, AwayFromZero)` でフレーム数にする。attack / sustain は0以上、decay は最低1。各レイヤーの和を N とし、N は最大300。元の秒数は保持し、丸め差を `TimeQuantized` で返す。
2. 各レイヤーは tick 0 で一度だけ NoteOn。マクロは n=0〜N を持ち、n=N の音量を0にする。ノート終端は `2 × (N+1)` tick。最後の一制御フレームは停止前のゼロ音量保持とする。SNES の固定releaseへ減衰を依存させず、音声レート変換の直前サンプルも曲末尾までに消すためである。tail=0で曲末尾のNoteOffを処理するサンプルがなくても既に音量0になる。末尾余白ありでは既存のNoteOff/releaseが進むが、音量マクロは0を保持する。
3. `lengthTicks = 2 × (max(N)+1)`、tempoBpm=150、ticksPerBeat=48、loopStartTick=0。最大本体長は301/60秒。UI/report は要求秒数の和、量子化後の包絡長 N/60、終端余白1/60秒を含む本体長を分ける。通常の export の `tail` とは別物である。
4. repeat の周期もフレームへ丸め、0は無効、正値は最低1。トーンのフレーム n に対し、時刻 t=n/60、曲線用時刻 q=(n modulo repeatFrames)/60 とする。repeat 無効時は q=t。音程ジャンプの待ち時間も同じ丸め。待ちフレーム以上でジャンプし、repeat で解除する。ジャンプ量が非ゼロで、待ち時間が周期以上またはトーンのN以上なら `InactivePitchChange`。有効repeatがN以上、または戻すslide/delta/jump/dutySweepが全て0なら `InactiveRepeat` を返す。
5. 基準 MIDI 値は `69 + 12 × log2(baseFrequencyHz / 440)`。整数 anchor はその値の AwayFromZero 丸め。PitchMacro は `round(100 × (基準MIDI−anchor + slide×q + deltaSlide×q²/2) + vibratoDepth×sin(2π×vibratoSpeed×t), AwayFromZero)`。ArpeggioMacro はジャンプ前0、後は指定半音差。repeat でもビブラート位相は継続する。
6. 合成器は anchor＋PitchMacro/100＋ArpeggioMacro を既存 PitchTable で制限・周期量子化する。全フレームの要求値と実効値を生成診断で計算する。SongRenderer の基本音だけの警告を、スライド全域の検証に代用しない。NES Pulse は約54.62〜12429 Hz、GB Pulse は64〜131072 Hz、SNES 内蔵波形の上限は `250 × 16383/4096 ≒ 999.94 Hz`。入力Hzの上限12000は音域保証ではない。
7. duty 目標は `dutyPercent + sweep×q` を12.5〜75へ飽和し、最寄りの12.5 / 25 / 50 / 75へ量子化する。同距離は小さい比率。原典の連続変化と違い段階的に音色が変わることを表示する。
8. ノイズはトーンの slide / repeat / vibrato / arpeggio を継承しない。NES / GB の選択値は `round(baseSelection + noiseSlide×t, AwayFromZero)` を固有範囲へ飽和。`MidiNote=baseSelection`、PitchMacro は `100 × (実効selection−baseSelection)` とする。SNES は固定 NoiseRate、MidiNote=60、PitchMacroなし。

マクロは有限長を全展開し、LoopIndex=-1。末尾保持の意味を変えず、ランダムや数学関数を Render 中に実行しない。フレーム境界は既存 FrameClock に従う。44100 Hz では735サンプル、48000 Hzでは800サンプルごと。他レートでの切上げ境界も既存挙動を使い、サンプルごとの補間を追加しない。

包絡の規範は次のとおり。A / S / D は量子化後のフレーム数、p は punch とする。

| 区間 | ピーク正規化前の包絡 E(n) |
|---|---|
| `0 <= n < A` | `n/A` |
| `A <= n < A+S` | `1 + 2p × (1−(n−A)/S)` |
| `A+S <= n < N` | `1−(n−A−S)/D` |
| `n = N` | 0 |

長さ0の区間は評価せず次へ進む。VolumeMacro は `round(volume × E(n)/(1+2p), AwayFromZero)`、0〜15。Note.Volume は15固定。punch によって保持冒頭の音量比は変わるが、ピークを volume より上げない。ピーク以外が相対的に下がる仕様をヘルプに示す。SNES でも0〜15の共通制御を採用し、DSP の既存0〜127の乗算はその後に行う。チップ間の RMS 一致は保証しない。

固定例: A=0、S=0、D=3、volume=12、punch=0 なら VolumeMacro=`[12,8,4,0]`、Note.DurationTicks=8、曲本体は約66.67 ms。slide=-12半音/秒・delta=0・vibrato=0・基準440 Hzなら最初の PitchMacro は `[0,-20,-40,-60]` cent。repeat=2フレームなら `[0,-20,0,-20]`、包絡は同じ列を進み続ける。

### プリセットとランダム化

#### 旧プリセットとの関係

`SfxPresetCatalog` / `SfxPresetFactory` / 既存 `sfx new` / `new_sfx` の出力と既定動作を変更しない。新しい `SfxParameterPresetCatalog` は以下の完全なパラメータ初期値を提供する。共通初期値に表の差分を適用し、全値を保存する。名前は同じでも UI は「パラメータ」「従来のソング雛形」の見出しで区別する。

包絡欄は A/S/D の**制御フレーム数**。秒の保存値は各値/60を6桁へ丸める。記載のない値は前表の初期値、punch=0、各slide=0、repeat=0。トーン／ノイズの有効性は表で上書きする。

| 名前 | トーン | トーン包絡・変化 | ノイズ | 旧版を移行しない理由 |
|---|---|---|---|---|
| jump | ON、196 Hz | 0/1/8、slide=128 | OFF | 旧 PitchSlide +19 の終点定義とマクロの秒速度は別。用途を継承する |
| coin | ON、523.251131 Hz | 0/3/6、+7半音を3フレーム後、punch=0.25 | OFF | 旧版は二回再打鍵＋後半3音循環。新定義は一回のジャンプ |
| hit | ON、196 Hz | 0/0/9、slide=-80 | ON、volume=12、0/0/4。NES short/3、GB 7/120、SNES rate31 | 旧 SNES は周期ノイズサンプル。新規は DSP ノイズ |
| explosion | OFF | 値は共通初期値を保持 | ON、volume=12、0/1/23。NES long/12、GB 15/96、SNES rate18 | 元のノート終端と新しいゼロ音量保持フレームは異なる |
| powerup | ON、196 Hz | 0/6/18、slide=24、delta=48、+12半音を12フレーム後 | OFF | 旧版の四段の再打鍵＋0x47循環は、この一声曲線と等価でない |
| laser | ON、880 Hz | 0/0/9、slide=-240 | OFF | 旧版の総量-36を速度として定義し直す。最終非ゼロフレームと連続終点を区別する |
| blip | ON、523.251131 Hz | 0/1/1 | OFF | 旧版は5 tick。新版は終端保持込み6 tick＝50 ms |
| select | ON、523.251131 Hz | 0/3/3、+5半音を3フレーム後 | OFF | 旧版の二回再打鍵を、一回の連続した音程ジャンプにする |

全プリセットで NES / GB duty=25、SNES waveform=pulse。新プリセットは音の用途を再定義したもので、旧 PCM の置換ではない。既存ファイルを開いたとき、タイトル一致や音色名だけから sfx 定義を付けない。16 SNES 音色を8 SFXへ分解・置換する作業も行わない。

#### 決定性と探索規則

- カテゴリは上記8名に `any` を追加する。`pickup` は coin、`power-up` は powerup の入力別名。保存は正式名。`any` は PRNG の最初の出力から `floor(8u)` で8カテゴリを表示順から一つ選ぶ。出自には要求category=anyと、選ばれたsourcePresetの両方を残す。
- PRNG は **xorshift32、algorithmVersion=1**。uint32 の状態へ左13、右17、左5の XOR シフトをこの順で適用し、各演算は32 bitで折り返す。seed は0〜4294967295。初期状態0だけ `0x6D2B79F5` に置換する。出力を `u=uint32/4294967296` とし `[0,1)` にする。`System.Random`、文字列ハッシュ、日時を生成式に使わない。
- CLI / MCP の seed は必須。DAW の「ランダム」「変異」は操作開始時に seed を一度発行して数値欄へ表示し、成功結果に保存する。「同じ seed で再生成」も用意する。音声 LFSR の seed とは別であり、再生時に乱数を引き直さない。
- `randomize` はカテゴリの完全初期値から下表の順に抽選する。現在値に依存せず、同じ chip / category / seed / algorithmVersion なら同じ正規化パラメータになる。無効レイヤーでも所定の抽選数を消費し、値の適用だけを省く。any 以外ではカテゴリ選択用の一回を消費しない。

| 抽選順 | randomize の規則（u は毎回一つ進める） |
|---|---|
| 1 | トーンHzを `2^((12u−6)/12)` 倍（±6半音）。SFX入力範囲へ制限 |
| 2〜4 | トーン A / S / D を各 `0.5+u` 倍。0は0を保持。Dは最低1/60秒 |
| 5〜6 | トーン slide / delta を各 `0.5+u` 倍。符号と0を維持 |
| 7〜8 | トーン vibratoDepth を `round(50u)` cent、speed を `3+6u` Hzにする |
| 9 | トーン volume を `10+floor(4u)` にする |
| 10〜12 | ノイズ A / S / D を各 `0.5+u` 倍。Dは最低1/60秒 |
| 13 | ノイズ volume を `10+floor(4u)` にする |
| 14 | NES / GB dutyを4選択肢から `floor(4u)` で選ぶ。SNESは波形を維持して抽選だけ消費 |
| 15 | ノイズ選択値へ NES/SNES は `floor(7u)−3`、GBは `floor(17u)−8` を加えて範囲へ制限 |

その他の値はカテゴリ初期値を維持する。音域を越す軌跡は勝手に別カテゴリへ再抽選せず、生成診断を返す。カテゴリの性格・再現性を保つため、最初から全パラメータを全域で抽選するボタンは設けない。

`mutate` は現在パラメータへ `strength`（0〜1、既定0.1）に比例した対称差を加える。対象順は tone のHz・slide・delta・depth・speed・volume・A・S・D・punch・pitchChangeSemitones・pitchChangeTime・repeatPeriod、noise のvolume・A・S・D・punch、現在チップのduty・dutySweep・noise選択値・noiseSlideの順。該当しないチップ項目も一回消費する。noiseMode / noiseWidth / waveform / enabled は変異しない。

| 種類 | strength=1 の最大変化幅 |
|---|---|
| 周波数 | 対数音程で±12半音 |
| slide / delta / depth / speed | ±60半音/秒、±240半音/秒²、±100 cent、±5 Hz |
| 音量 / A / S / D / punch | ±4、±0.1秒、±0.2秒、±0.2秒、±0.25 |
| 音程ジャンプ量 / 時刻 / repeat | ±4半音、±0.1秒、±0.1秒 |
| duty / dutySweep | ±25 %ポイントを最寄り段階へ、±25 %ポイント/秒 |
| noise選択 / noiseSlide | NES ±3 / ±12、GB ±16 / ±48、SNES rate±3 / slideなし |

各差は `(2u−1) × strength × 幅`。範囲へ制限して正規化し、整数項目はAwayFromZeroで整数へ丸める。時間は保存する秒数からフレームへ量子化して検証する。無効レイヤーは抽選だけ消費し値を保持する。repeat=0 と pitchChangeSemitones=0 は変異では0を維持し、離散機能を勝手に有効化しない。元が有効なrepeatを減らす場合は最低1/60秒に制限し、無効化もしない。

`locks` は正規パラメータパスの配列。変異ではロックした値を保持するが抽選は消費する。unknown pathはエラー。Sが0フレームかつpunch>0になった場合、punchが未ロックなら0へ戻し、punchがロック済みなら未ロックのsustainを1/60秒へ戻す。両方ロックなら元の有効値のままであり補正不要。補正内容は変更一覧に出し、ロックした値を補正で変更しない。

randomize はロックを受け付けず、全レシピを入れ替える操作とする。strength=0または全結果が同値なら `changed=false`、保存・履歴を増やさない。変異の出自は操作・seed・strength・locks・変更前パラメータの SHA-256 を記録し、Undo が変更前の全値を保持する。seedだけで未知の変更前状態を復元できるとは扱わない。出自は最後に成功した乱数操作の記録であり、その後の手動tweakが現在値を変えていても履歴情報として保持する。

## 既存構造への写像

### 生成する Song の規則

全トラック構成は SongFactory / ChipLayout の NES 5 / GB 4 / SNES 8 を維持する。使わないトラックは空で残す。音色IDはトーン=1、ノイズ=2。無効レイヤーの音色・ノートは生成しない。トラック名は SongFactory 既定、Muted=false、Pan=0、DefaultInstrumentId=null。SNES 音色のPan=0、EchoSend=0、Song.SnesEcho は既定値を明示し、DelayMilliseconds=0で無効化する。

| パラメータ・規則 | 生成先 | Note.Effects |
|---|---|---|
| トーン enabled、包絡長 | NES / GB track0、SNES voice0。tick0、anchor、volume15、instrument1、duration=`2(N+1)` | 空配列 |
| 基準Hzの端数＋slide＋delta＋vibrato | 音色1の PitchMacro。n=0〜Nのセント列 | PitchSlide / Vibrato は生成しない |
| 音程ジャンプ＋待ち時間＋repeat | 音色1の ArpeggioMacro。半音差列。0なら全0列 | Arpeggio は生成しない |
| 各 ASDecay・volume・punch | 各音色の VolumeMacro。N+1要素、最終0 | VolumeSlide は生成しない |
| NES duty / sweep / repeat | NesPulseInstrument.Duty と DutyMacro。値はDutyCycleの整数1〜4 | 空配列 |
| GB duty / sweep / repeat | GbPulseInstrument.Duty と追加DutyMacro。InitialVolume=15、EnvelopeStepFrames=0、EnvelopeIncreasing=false | 空配列 |
| NES noise | track3、NesNoiseInstrument、NoiseMode、MidiNote=periodIndex、PitchMacroで添字変化 | 空配列 |
| GB noise | track3、GbNoiseInstrument、LfsrWidth、MidiNote=selection、PitchMacroで選択値変化 | 空配列 |
| SNES tone | voice0、SnesSampleInstrument、Waveform、Loop=true、Preset=null、SampleData=null、PitchModulation=false、NoiseEnabled=false | 空配列 |
| SNES noise | voice1、SnesSampleInstrument、NoiseEnabled=true、NoiseRate、MidiNote=60。Waveform=Sineを固定値として保存 | 空配列 |
| SNES 共通包絡 | 追加VolumeMacro。AdsrRegisters=(15,0,7,0)、Envelope=(0,0,1,0)、Loop=true。DSP attackは約2サンプル、保持後releaseは既存固定速度 | 空配列 |
| repeat | 同一ノート中のマクロ値を展開し直す。ノート分割、LoopStartTick変更なし | Delay も再打鍵も生成しない |

現在のエフェクト列でも直線slide・6 Hz vibrato・ノート全期間の減衰は作れる。しかし条件によって効果とマクロを切り替えると、速度・端数・更新順の差がパラメータ変更時に現れる。**新 generatorVersion=1 の Effects は常に空**に固定し、意図を既存の低水準値へ一通りに写像する。通常編集・旧SFXのエフェクトは維持する。

全マクロを、値が一定でも所定の要素数で保存する。音色名は `sfx-tone` / `sfx-noise`。配列順・ID・丸め・既定設定まで generatorVersion の契約に含め、同値の短縮表現へ無断で変更しない。生成は純粋なオフライン処理とし、入力を変更せず新しい Song と診断を返す。

### optional な合成入力の追加範囲

| 追加 | 必須の接続 | 既存挙動の保持 |
|---|---|---|
| `GbPulseInstrument.DutyMacro` | JSON末尾、InstrumentValidatorの1〜4検証、GbPulseSynthesizer.ConfigureMacrosのduty引数、Formats ControlTrackCursorの同じ引数 | null/未指定/空列なら従来Duty。音声・VGM両経路で同じ制御値。既存GBハードウェア包絡の更新順は変更しない |
| `SnesSampleInstrument.VolumeMacro` | JSON末尾、InstrumentValidatorの0〜15検証、SnesVoiceSynthesizer.ConfigureMacrosのvolume引数、ApplyPresetで他マクロと同様に保持 | null/未指定/空列なら従来音量。ADSR計算・BRR・Gaussian・ミキサー・NoteOffを変更しない |

追加プロパティのnullは保存時に省略する。既存の汎用音色コピー・JSON編集・音色切替が二つのマクロを捨てないことも検証する。SFX用スライダーの提供と、通常の音色パネルに全マクロ編集UIを追加する作業は分け、今回後者の全面改修は行わない。

### 保存・同期・互換性

Song の末尾に optional な `sfx` を置く。未指定・nullは通常ソングとして扱い、保存時は省略する。以下は新規定義の構造例であり、`parameters` の省略を許すファイル例ではない。

| sfx の項目 | 仕様 |
|---|---|
| `schemaVersion` | 1。パラメータの構造・単位の版 |
| `generatorVersion` | 1。マクロ・ノートへの展開規則の版。読み込み時に最新版へ更新しない |
| `parameters` | 前表の全共通値と現在チップの固有値。既知schemaでは省略キーを既定値で補完せず、不完全な保存データを拒否する |
| `parametersHash` | schemaVersion / generatorVersion / parametersをこの順で持つcanonical JSONのSHA-256、小文字64桁。JSONでパラメータだけを手修正した場合も同期切れを検出する |
| `sourcePreset` | 正式名またはnull。手動編集後も出自として保持する。再生の解決キーにはしない |
| `lastRandomization` | null、または operation / algorithmVersion / seed / category / strength / locks / baseParametersHash。適用外の項目はnull。全パラメータ自体を省略する根拠にはしない |
| `generatedHash` | 下記の生成領域の canonical UTF-8 JSON の SHA-256、小文字64桁 |

**生成領域は Song 全体から title と sfx を除いた部分**。音色名・トラック名・ミュート・定位・空トラック・tempo・length・echoも含む。タイトルだけの変更では同期を失わない。生成直後に Serializer と同じキー順・2スペース字下げ・改行LF・UTF-8 BOMなし・末尾改行なしの正規JSON表現から指紋を計算し、Serialize時の改行環境へ依存させない。parametersも固定の型のプロパティ順で正規化する。指紋は改ざん防止署名ではなく、別エディタによる変更を検出するための値である。

| 状態・操作 | 決定した動作 |
|---|---|
| sfxなし | 通常ソング／legacy。再生・通常編集可。paramsは `editable=false, reason=MissingDefinition`。tweak / mutate / randomizeは拒否 |
| 既知版・両hash一致 | `editable=true`。一回のパラメータ変更で全生成領域とsfx、両hashを候補上で同時に再生成し、一履歴で公開 |
| 既知版・generatedHash不一致 | Songはそのまま有効で再生可。`editable=false, reason=GeneratedContentChanged`。保存した値は `savedParameters` と表示し、現在の音のパラメータと呼ばない |
| parametersHash不一致 | `editable=false, reason=SavedParametersChanged`。生成列も変わっていれば両方の不一致を診断する。読み取った値はsavedParametersとして表示し、再生成の明示要求があるまで音へ反映しない |
| 生成列を編集済みから復帰 | 明示の「保存パラメータから生成列を置換」／regenerateだけで戻す。titleを保持、生成領域を全置換。削除・変更対象の件数を事前表示し、Undo一回で編集した音へ戻る |
| 「通常ソングとして編集」／detach | sfxだけを除去する一履歴。生成列と音は保持。自動的な逆変換や定義への再接続はしない |
| 既知schemaの不正値 | 読み込み時にドキュメントエラー。生成列との不一致だけは上記の編集済み状態として受理 |
| 未知schema / generator / random algorithm版 | 新アプリはsfxを不透明なJSONとして保持し、通常の再生・保存は可能。パラメータ編集・再生成を拒否し `UnsupportedSfxVersion`。未知schemaを既定値で部分復元しない |

未知版のJSON保持は今回追加する保存責務であり、現 Serializer が既に行うと解釈しない。既知版のDTOと未知版のJSON保持を保存境界で分け、音声側はsfxを解釈しない。読み込み・保存・SongValidatorに音声生成やマクロの自動再生成を持ち込まない。同期状態は指紋で判定し、パラメータと生成列を手で同時改変した高度な編集の同値性までは証明しない。

**version=1のまま追加可能と判断する。ただし互換性は次の範囲に限定する。**

- 旧ファイル→新アプリ: sfx / 新マクロなしでは音・正規JSONを維持する。旧8プリセットの出力も変更しない。
- 新ファイル→新アプリ: パラメータ・生成列・出自を往復保持し、保存だけで音を変えない。sfxがなくても新マクロを再生できる。
- 新ファイル→旧アプリ: 既知のSong / Instrument型なので形式として読み込めるが、旧版はGB DutyMacroとSNES VolumeMacroを無視する。**GB sweepとSNES包絡の同音再生・再編集は非互換**。旧版で保存するとsfxと新マクロは失われる。
- 旧アプリへの同音配布はWAVを使う。version=1であることを旧再生エンジン互換の判定に使わない。sfx削除は旧版向け変換ではない。旧版で新ファイルを確実に拒否させる要件は今回採用しないため、Song全体のversionを2へ上げない。

### 編集トランザクションと責務

Core の `SfxParameterValidator` は値とチップ適合性、`SfxSongCompiler` は純粋な生成と診断、`SfxParameterRandomizer` は固定乱数からの変更、`SfxEditor` は既存 EditSession への一括適用を担当する。パラメータ仕様の一覧は `SfxParameterCatalog` 一つを CLI / MCP / DAW で共有し、単位・範囲・可否・JSONパスをフロントエンドで再定義しない。これらを新しい抽象レイヤー群へ包まない。

- patchは現在値へ指定項目だけ適用する。全件検証→全生成→Song検証→保存→履歴→公開の順。途中失敗で部分的なマクロ・パラメータ・履歴を残さない。
- EditSession.Change、SongSnapshotPublisher、DawDocument.Save、SongHistoryのスナップショットと外部変更判定にsfxを通す。CLIは既存CliExecution.Editと側車履歴の復元・保存失敗時のロールバックを使う。MCPは共有セッションロック内で同じSfxEditorを呼ぶ。
- CLIの新規createは古い同名の側車履歴を引き継がない。新規保存と履歴初期化の両方が完了して成功とし、履歴I/O失敗では自分が作った未変更の新規ファイルを除去して開始前の側車状態を復元する。プロセス強制終了まで含む二ファイルの永続トランザクションは既存同様に対象外。MCPのcreateはセッションとその履歴を変更しない。
- 生成領域の手動変更を禁止しない。変更後はhash不一致となり再生成を拒否する。外部編集を知らずにtweakして上書きすることを防ぐ。
- `revision` はsfxを含む現在Song全体の正規JSONのSHA-256。読み取り結果から渡したexpectedRevisionが違えば `RevisionConflict`。同じセッション内の検証・適用は一体で行い、CLIは保存直前にも元ファイルを再確認する。プロセス間の確認と置換の間までロックする分散トランザクションは保証しない。
- 同値patchは `changed=false`、履歴・出自・指紋を変更しない。dry-runは生成診断と候補値・指紋を返すが、ファイル・履歴・再生を変更しない。

## UI

### 配置と操作

既存 `Views/SfxCreationView.axaml` と SfxCreationPresenter を、パラメータ編集を担う View / Presenter に置き換える。MainWindow の右ペインSFXタブと既存「SFX を作る」入口を使い、別タブを増やさない。右ペインの幅で横スクロールが発生しない一列構成とする。上部と下部の主操作を固定し、中段のパラメータだけを縦スクロールする。

| 上からの配置 | 内容・表示規則 |
|---|---|
| 固定ヘッダー | 「SFX」、現在のチップ、編集中のファイル名または「新規候補」、未保存／生成列を編集済みの状態 |
| 固定試聴列 | 大きい「再生」「停止」、自動試聴トグル（既定ON）、試聴専用音量。音量はアプリ設定で保持し書き出し音量に使わない |
| プリセット | パラメータ8種の選択、ランダム、変異。折り畳み詳細にカテゴリ・seed・strength・再生成。旧8種は「従来のソング雛形」内に分離 |
| 音の構成 | トーンON/OFF、ノイズON/OFF。チップ変更は新規候補だけ。SNESはここに波形選択 |
| トーン音程 | 基準Hz、slide、delta slide、vibrato深さ、速度。この順を固定 |
| トーン音量 | volume、attack、sustain、punch、decay。要求時間と実効時間、静的な包絡線を近くに表示 |
| 音程変化・反復 | ジャンプ量、待ち時間、repeat周期。初回は展開し、以後の折り畳み状態を保持 |
| チップ固有音色 | NES / GB dutyとduty sweep。SNESは波形の説明を表示し、使えないduty sliderを置かない |
| ノイズ | 周期選択／幅・mode／rate、対応するnoise slide、独立volumeとASDecay・punch。OFFなら入力を無効化し理由を表示 |
| 診断 | 量子化後の本体長、音域制限・効かない設定・停止状態を短く表示。詳細へフレーム範囲と要求→実効値 |
| 固定フッター | 新規候補では「新規保存」「保存したSFXを開く」。編集中ファイルでは通常の保存状態と「通常ソングとして編集」。WAV書き出し、解析への導線 |

スライダーには常時、ラベル・数値入力・単位を付ける。細かい操作をポインターだけに依存させない。初期フォーカスは再生、Tab順は上表どおり。左右キーで一刻み、Shift併用で細かい刻み（通常の1/10、整数・enumは最小1段階）とし、数値欄では標準の文字編集を優先する。Spaceは入力欄にフォーカスがないときだけSFX試聴の再生／停止。Escは進行中ジェスチャー取消、なければ試聴停止。既存ピアノロールのキーをSFX入力中に発火させない。

各項目のリセットは「初期値へ」操作、各グループには変異用ロックを設ける。グループロックは含まれる正規パスの集合へ展開し、保存するlocksにグループ名を入れない。値の訂正、プリセット変更、ランダム、変異はUndo可能な一操作にする。プリセットを選ぶだけで保存先ピッカーを開かない。

### 即時試聴・保存の寿命

- マウス／タッチのドラッグ中は数値と包絡線を即時更新し、メモリ内候補を作る。**離した時点で一回確定し自動試聴**する。数値欄はEnterまたはフォーカス離脱で確定。キー連続変更は最後の入力から150 msで一回へまとめる。自動試聴OFFなら確定しても鳴らさない。
- 有効な確定値から生成を始め、最新の生成世代だけを採用する。待機中も停止操作を受け付ける。短いSFXの操作確定から出音まで100 ms以内を実機での目標とするが、未測定の保証値として表示しない。
- `SfxPreviewPlayer` が生成済みスナップショットを既存SongRendererへ渡す。44100 Hz、1回、tail=0、常に先頭から。既存PlaybackEngineのtail=0.5秒・文書参照をそのまま試聴に流用しない。既存IAudioOutput/SDL経路と停止同期を再利用する。
- 新しい試聴は古い試聴を停止・破棄してから鳴らす。同時重ね鳴らし・待ち行列は作らない。クリック対策として試聴出力の切替時だけ最大5 msのフェードを使う。これはモニター処理であり保存PCMやanalyzeには含めない。
- DAWは通常ソング再生とSFX試聴の音声出力所有者を一つにする。SFX試聴開始時は通常再生を停止し、通常再生開始時は試聴を停止する。通常曲を勝手に再開しない。生成・JSON処理・レンダラー構築・Resetは音声コールバック外。コールバック内はRenderと事前確保した試聴ゲイン処理だけ。
- 新規候補はメモリ内の専用履歴を持ち、現在のDawDocument・ファイル・その履歴を変えない。新規保存はSfxPresetFileと同じ隣接一時ファイル→上書きしない移動。保存した候補をさらに変更した場合、保存したファイルを開く操作は古い値であると表示し、最新候補の保存が済むまで無効にする。
- 現在のパラメータSFXを編集する場合は作業セッションへ確定値を一回適用する。Ctrl+Sで正本保存する既存規則は維持する。ドラッグ途中にCtrl+S／タブ移動した場合は有効な現在値を一回確定、Escなら開始値へ戻す。
- 新規保存と「開く」は別操作。「開く」は未保存編集・外部変更待ちを検査し、存在すれば保存／再読込を促して拒否する。**旧SFX作成のprepareSwitchによる暗黙保存は呼ばない**。候補保存は成功済みとして維持し、開けなかったことと混ぜない。
- 外部変更・Undo/Redo・文書切替・タブ離脱・終了時は試聴を停止し生成世代を無効化する。読み込み・Undoそのものでは自動試聴しない。古い非同期完了が現在の文書を上書きしない。終了時に音声出力、購読、CancellationTokenSourceを解放する。

### 優先順位と状態

`~/.claude/rules/game-ui-design.md` の **操作可能性 > 可読性 > 情報階層 > フィードバック > 一貫性 > アクセシビリティ > 効率性 > 美観** を適用する。

| 優先する観点 | 受け入れ条件 |
|---|---|
| 操作可能性 | 最小ウィンドウ1050×560とキーボードだけで作成→調整→試聴→保存を完遂できる。固定ボタンがスクロール外へ消えない |
| 可読性 | ラベル・符号・最大桁・単位を同時に読める。数値欄とスライダーを幅不足時は二段にし、省略記号で重要値を隠さない |
| 情報階層 | 現在音、再生／停止、保存状態を最初に見つけられる。低頻度のseed詳細で主要音程スライダーを押し下げない |
| フィードバック | 生成中／試聴中／無効入力／外部競合／生成列編集済みを文字で示す。値を変えたのに音が変わらない量子化も実効値で説明する |
| 一貫性 | 既存Arpeggioテーマの色・間隔・入力部品・Danger表示・Undo/Saveを使う。既存SFXタブ以外の配置は今回改修しない |
| アクセシビリティ | 色だけに依存しない。フォーカス可視、ラベル付き入力、拡大時の折返し、試聴OFF・停止・独立モニター音量を備える |
| 効率性・美観 | 一操作一履歴、数値直接入力、ロックを提供。装飾アニメーションより列の整列と応答性を優先する |

通常／hover／focus／dragging／生成中／試聴中／invalid／保存失敗／競合／sfxなし／生成列編集済み／保存パラメータ手修正／未知版／破棄中を明示状態として扱う。invalidでは該当欄に理由と範囲を示し、最後の有効音を保持する。再生ボタンのラベルは「最後の有効値を再生」とし、無効値が適用されたように見せない。失敗後も入力を保持し再試行できるようにする。

## CLI / MCP

### CLI

現行 `sfx new <path> --preset ...` / `sfx list` はlegacyのまま維持する。新APIは `sfx create` を入口とする。以下の新コマンドは全て `--json` を持ち、成功・失敗ともstdoutに一つのJSONを返す。通常表示は要約をstdout、警告をstderrへ出す。

| コマンド | 引数・動作 |
|---|---|
| `arpeggio sfx create <path>` | `--chip nes`（nes/gameboy/snes）、`--preset jump`、`--title`、`--dry-run`。新定義付きSongを新規保存。上書きオプションなし |
| `arpeggio sfx list --editable` | 新パラメータ8種、用途・全初期値・対応チップ。従来の引数なしlistのテキストを変えない。`--json`も追加 |
| `arpeggio sfx params [path]` | pathありは現在値・同期状態・revision・診断。`--schema`で全項目の単位・範囲・刻み・可否を付ける。pathなしでは`--chip nes`の初期値とschema。読み取り専用 |
| `arpeggio sfx tweak <path>` | 表のパラメータ指定、または`--patch <patch.json\|->`。複数指定を一操作で適用。`--expected-revision`、`--dry-run` |
| `arpeggio sfx randomize <path>` | `--category <8名\|any>`、`--seed <uint32>`必須。`--expected-revision`、`--dry-run`。チップとtitleを保持 |
| `arpeggio sfx mutate <path>` | `--seed`必須、`--strength 0.1`、反復指定可の`--lock <正規パス>`、`--expected-revision`、`--dry-run` |
| `arpeggio sfx regenerate <path> --replace-generated` | 保存パラメータから生成領域を置換。必須フラグ自体を明示的な置換要求としCLIで対話確認はしない。`--expected-revision`、`--dry-run` |
| `arpeggio sfx detach <path>` | sfxを除去。音は保持。`--expected-revision`、`--dry-run` |

`--patch` はparametersに対するネストした部分オブジェクト。例 `{"tone":{"slideSemitonesPerSecond":-12,"envelope":{"decaySeconds":0.2}}}`。省略は保持、nullは全パラメータで不許可。未知キー・重複キー・配列・空patchを拒否する。`--patch` と個別パラメータオプションの混在は拒否する。ファイル名・enum・数値の解釈はカルチャ非依存。

| 個別CLIオプション | 正規パス・規則 |
|---|---|
| `--tone-enabled` / `--noise-enabled` | tone.enabled / noise.enabled。true/falseを明示 |
| `--frequency` / `--slide` / `--delta-slide` | tone.baseFrequencyHz / slideSemitonesPerSecond / deltaSlideSemitonesPerSecondSquared |
| `--vibrato-depth` / `--vibrato-speed` | tone.vibratoDepthCents / vibratoSpeedHz |
| `--volume` / `--attack` / `--sustain` / `--decay` / `--punch` | tone.envelopeの対応値。時間は秒 |
| `--noise-volume` / `--noise-attack` / `--noise-sustain` / `--noise-decay` / `--noise-punch` | noise.envelopeの対応値 |
| `--pitch-change` / `--pitch-change-time` / `--repeat-period` | tone.pitchChangeSemitones / pitchChangeTimeSeconds / repeatPeriodSeconds |
| `--duty` / `--duty-sweep` | 現在チップnes/gameBoyのdutyPercent / dutySweepPercentPerSecond。SNESはエラー |
| `--noise-period` / `--noise-mode` | NESのnoisePeriodIndex / noiseModeのみ |
| `--noise-selection` / `--noise-width` | GBのnoiseSelection / noiseWidthのみ |
| `--noise-slide` | NESのindices/秒、GBのselections/秒。schemaとhelpへ単位を明示。SNESはエラー |
| `--waveform` / `--noise-rate` | SNESのwaveform / noiseRateのみ |

新コマンドの引数解析エラーもJSONに整形する。現 CliExecution.Runの一般例外ハンドラーだけではstdoutのJSON契約を満たさないため、既存CliConversionExecutionにならってSFX専用の実行境界を追加する。他コマンドのエラー表示は変更しない。

### MCP

既存 `new_sfx` / `sfx_presets` の意味と戻り値は維持する。新ツールも既存ArpeggioToolsの共有EditSession・直列Invoke・**JSON文字列の戻り値**に揃え、StructuredContentへ切り替えない。

| ツール | 引数・契約 |
|---|---|
| `create_sfx` | `path, chip="nes", preset="jump", title=null, dryRun=false`。新規保存だけを行い、現在セッションを変更しない。続けて既存open_songで開く |
| `sfx_parameter_presets` | 新8種の完全初期値・説明・対応チップ。現在Song不要 |
| `sfx_parameters` | `includeSchema=false, chip=null`。現在Songの値・同期状態・revision。未オープンでのschema取得は`chip`を明示して初期値とschemaを返す。Songありで異なるchip指定は拒否 |
| `tweak_sfx` | `parameters`（上記patchのJSON文字列）、`expectedRevision=null, dryRun=false` |
| `randomize_sfx` | `category, seed, expectedRevision=null, dryRun=false` |
| `mutate_sfx` | `seed, strength=0.1, locks=null`（正規パス配列のJSON文字列）、`expectedRevision=null, dryRun=false` |
| `regenerate_sfx` | `replaceGenerated`必須true、`expectedRevision=null, dryRun=false` |
| `detach_sfx` | `expectedRevision=null, dryRun=false` |

新規作成以外の編集対象は開いているSong。成功時は既存EditSessionと同じ自動保存＋一履歴。sfxなしや未知版の曲へ暗黙に定義を付けない。可否・引数・エラーはCLIと同じCore定義を使う。

### 戻り値・診断・AI調整ループ

新SFX操作の共通結果は `operation, changed, dryRun, editable, reason, revision, candidateRevision, parameters, savedParameters, generation, randomization, warnings, limitations`。読み取り専用のschemaは要求時だけ追加する。適用前のrevisionを成功時のrevisionに残さず、成功後の状態を返す。dry-runではrevisionは現状態、candidateRevisionは予定状態。createのdry-runは現状態がないためrevision=null。

`generation` はgeneratorVersion、tempoBpm、lengthTicks、bodyDurationSeconds、各レイヤーのrequestedEnvelopeSeconds / envelopeFrames / trackIndex、generatedHashを持つ。警告は `code, parameterPath, layer, fromFrame, toFrame, requested, actual, message`。同種の連続フレームをまとめる。エラーは既存 `error / exitCode` に安定した `code` と位置を加える。

| 分類 | 代表コード・意味 |
|---|---|
| 正常な離散化の情報 | `TimeQuantized`、`DutyQuantized`。要求と実効が違う箇所を返す。通常の周期量子化・15段階音量はlimitationsにも記載 |
| 変更可能な警告 | `PitchClamped`、`NoiseSelectionClamped`、`DutyClamped`、`InactivePitchChange`、`InactiveRepeat`、`SilentParameters`。値を黙って書き換えず、生成で使う実効値と区別 |
| 操作エラー、exit1 | `InvalidParameter`、`UnsupportedParameter`、`MissingDefinition`、`GeneratedContentChanged`、`SavedParametersChanged`、`UnsupportedSfxVersion`、`RevisionConflict`、`DestinationExists` |
| ドキュメント不正、exit2 | Song / 既知schemaの不正。tweakの新しい不正入力はexit1、元ファイルが不正ならexit2 |
| I/O、exit3 | 読み書き・権限・履歴保存失敗。dry-runでも入力読み取り失敗はexit3 |

SFXのstrictモードは作らない。通常のチップ量子化をエラーにするとスライダー操作が成立しないためである。音響の良否は既存analyzeで確認し、NSF / VGMのstrictは既存の書き出し側で選ぶ。

```sh
arpeggio sfx create laser.arpeggio.json --chip nes --preset laser --json
arpeggio sfx params laser.arpeggio.json --schema --json
arpeggio sfx tweak laser.arpeggio.json --slide -12 --decay 0.2 --json
arpeggio analyze laser.arpeggio.json --window-ms 20 --json
arpeggio export wav laser.arpeggio.json laser.wav --loops 1 --sample-rate 44100 --tail 0
arpeggio analyze wav laser.wav --window-ms 20 --json
```

AIはparamsのrevisionを次の変更のexpectedRevisionへ渡す。スクリプトでの値の受け渡し方法には依存しない。調整の一巡は「現在値・可否取得→1〜数項目の変更→analyze_song→WAV書き出し→analyze_wav」。MCPもcreate_sfx→open_song→sfx_parameters→tweak_sfx→analyze_song→export_wav(tail=0)→analyze_wavの順。候補比較は既存undo/redoを使える。

- 包絡・音量はrmsDbfs / peakDbfs / clippedSampleCount / windows、ノイズはbandEnergy / spectralCentroidHzを比較する。周波数はwindows.dominantFrequencyHzを見るが、これは基音推定ではない。短音・矩形波の倍音・ノイズから音階を断定しない。
- 20 ms窓は短い効果音の変化を見る既存オプションの利用例。低音の周波数精度が不足する場合は50〜100 ms窓でも比較する。生成の全フレーム音域診断と音声FFTは役割が違う。
- analyze_songはfloat PCM、analyze_wavは16 bit量子化後を読むため、音響値の完全一致は要求しない。同じsampleRate / loops / tail=0を使い、本体長の一致と許容差を検証する。OGGは短音の最終granuleに既知の制限があるため、この調整ループの長さ基準にしない。
- DAW試聴も同じ生成Songを鳴らすが、モニター音量と切替フェードは解析に入れない。解析・書き出し結果には対象revisionをUI側で紐付け、編集したら古い結果と表示する。

## テスト方針

本ランではテストコードも実行も追加しない。実装ランでは以下の意味のある境界・統合テストを作り、依頼者またはClaude Codeがコンパイル・テスト・試聴を実行する。既存合成の期待値を新プリセットの音に合わせて緩めない。

| 対象 | 受け入れ条件 |
|---|---|
| パラメータ | 型・単位・全端点・NaN/Infinity・未知/重複キー・部分patch・不正チップ項目・sustain0とpunch・全レイヤーOFF・同値変更を検証 |
| 時間 | 0／半フレーム境界／最大300フレーム、異なる二包絡長、最後の0フレーム、2tick対応、44100/48000と割り切れないレートの境界。前述8tick包絡の固定例を独立期待値で検証 |
| ピッチ | A4・負のslide・deltaの二次項・6 Hz以外のvibrato・ジャンプ前後・repeat境界。深さ0/速度0/待ち0/到達不能を含め、先頭値飛ばしと二重加算がない |
| 包絡 | `[12,8,4,0]`、attack開始0、sustain/punch境界、punch=1でもピーク<=volume、volume0、最短decay。二レイヤーを独立に検証 |
| チップ制御 | NES4 dutyと添字0/15・下限飽和で折返さないこと、GB dutyマクロと分周グループ境界、SNES pitch上限・noiseRate・トーンslideがノイズへ漏れないこと |
| 合成拡張回帰 | 新マクロnull/空列で旧GB/SNES PCMが一致。SNES ADSR/BRR/補間/ノイズの既存テストを維持。追加マクロが音色ApplyPreset・コピー・置換で保持される |
| 出力接続 | GBのDutyMacroがCore PCMとFormatsの制御列の両方に伝わる。NES/GB SFX→VGM、NES→NSFで既存診断を保持。新SNESは既存どおりWAV/OGGのみ |
| 音声 | 新8種×3チップで有限PCM、非無音、クリップなし。全ゼロ音量だけは意図的無音。SNES末尾のゼロ保持とtailあり/なし・固定releaseの境界、二回試聴の同一性、Renderバッファ分割不変 |
| 旧プリセット | 8種×3チップのfactory出力・PCMを依頼者側で基準採取し維持。16 SNES音色の名前・素材・推奨設定も変更しない |
| 保存 | version1往復、sfx未指定の旧JSON不変、新二マクロのnull省略、未知sfx版の保持、キー順違い、titleだけの変更、マクロ/ノート/ミュート変更とparametersだけの手修正による各hash不一致 |
| 再編集と履歴 | tweak→save→open→params、Undo/Redoでパラメータと生成列と出自が同時復元。再生成後のUndoで手動編集を復元、detach前後でPCM一致、DawDocument正本保存でsfx消失なし |
| 決定的探索 | seed=1のxorshift32先頭出力は270369、67634689、2647435461。seed0/max、any、lock、strength0/1、全同値、無効レイヤーで固定消費順。カテゴリ別の独立goldenパラメータをmacOS/Windowsで一致確認 |
| 原子性 | patch途中の不正、生成失敗、revision違い、保存競合、I/O・CLI履歴失敗、dry-runで元Song・出力・履歴が不変。一時ファイルを残さない |
| CLI / MCP | 全パラメータの等価patch、新旧コマンド共存、引数解析失敗も単一JSONと0/1/2/3、MCP文字列JSON・未オープンschema・createで共有セッション維持 |
| UI Presenter | ドラッグ100更新が一履歴・一試聴、150 ms集約、無効数値、最新世代優先、停止、外部変更、Undo、タブ離脱、終了、保存成功後Open拒否を偽音声出力・時計で検証 |
| DAW実機 | 最小サイズ・拡大表示・日本語最大桁・Tab/Space/Esc・OS保存ピッカー、確定→出音の遅延、切替時クリック、通常再生との排他をmacOS/Windowsで確認 |
| ホットパス | Render/NoteOn/AdvanceFrameで配列生成・LINQなし。既存GC0回帰に追加マクロありのケースを加える。生成/解析/履歴のオフライン確保は許可 |

生成定義の決定性は正規化パラメータと整数マクロ列を基準にする。Math.Sin等の浮動小数点差が丸め境界を跨がないことを対応OSのgoldenで検証し、差があればgeneratorVersion=1出荷前に同じ整数結果へ固定する。将来ジェネレーターの変更は版を上げ、既存保存列を自動再生成しない。全codec・異なる旧エンジンとのPCMビット一致は契約に含めない。

## 実装ランの分割

各行はCodex一回、最大40分の枠とする。リポジトリCLAUDE.mdに従い実装プロファイルは **top＋high**。各ランの実装・静的確認は30分以内に保存し、30分を過ぎたら「未完了」に残りを書いて停止する。依頼者／Claude Codeによるレビュー・ビルド・テスト実行までが受け入れであり、Codexは実行済みと報告しない。依存先の未確認を完了扱いで通さない。

| ラン | 前提 | 追加する範囲 | 受け入れ条件 |
|---|---|---|---|
| SFX-A1 | 本設計レビュー | パラメータ型・Catalog・Validator・patch解析 | 全値の範囲/単位/可否、部分patchとエラーが表どおり。生成・UIへの公開なし |
| SFX-A2 | A1 | GbPulse DutyMacroとFormats接続 | null時の旧出力不変、4段階のCore/制御列一致、JSON検証・VGM接続テスト |
| SFX-A3 | A1 | SnesSample VolumeMacro | 旧PCM不変、全量保持ADSRとの積・最終0・ApplyPreset保持・GC回帰ケース |
| SFX-B1 | A1 | 時間量子化・包絡・ピッチ曲線の純粋生成 | 手計算マクロ、delta/vibrato/jump/repeat、0長区間、資源上限、診断の固定テスト |
| SFX-B2 | B1,A2 | NES/GBのSong組立・noise/duty曲線 | 固定トラック・ID・終端・Effects空、Noise添字の飽和、Validator成功、全軌跡音域診断 |
| SFX-B3 | B1,A3 | SNESのSong組立・DSPノイズ・終端 | pulse/sine等5波形、noiseRate、約1000Hz上限、包絡がDSP releaseに化けない、WAV経路へ接続 |
| SFX-C1 | A1 | sfx保存形式・既知/未知版・hash・状態判定 | 旧JSON不変、新形式往復、未知版保持、title除外と生成列編集検出。ロードで再生成しない |
| SFX-C2 | C1,B2,B3 | SfxEditor・EditSession・コピー境界・履歴 | 一操作で定義と生成列を保存、Undo/Redo、detach/regenerate、I/O失敗・revision拒否・DAW正本保存 |
| SFX-D1 | B2,B3 | パラメータ8プリセット | 全初期値を表どおり生成。8×3で有効Song、旧8種と16音色の変更なし |
| SFX-D2 | D1,A1 | 固定PRNG・randomize・mutate・locks | seed固定値、全抽選順、カテゴリ性格、同値no-op、出自保存・再現テスト |
| SFX-E1 | C2,D1 | CLI create/list/params/tweakとJSON実行境界 | 個別引数とpatchの同値、全パラメータ、旧new維持、引数エラーJSON、0/1/2/3 |
| SFX-E2 | E1,D2 | CLI randomize/mutate/regenerate/detach、AIループ統合 | dry-runと履歴原子性、analyze→export tail0→analyze wav、revision競合の契約テスト |
| SFX-E3 | E2 | MCP追加ツール | 既存名維持、同じCore結果、JSON文字列・直列実行、createで現セッション維持、未オープンschema |
| SFX-F1 | C2,D2 | DAW候補/編集中モデル・Presenter・一操作履歴 | 文書不変の候補、ドラッグ確定/取消、ロック/変異、保存とOpen分離、競合・未知版の状態テスト |
| SFX-F2 | F1 | SfxPreviewPlayer・通常再生との排他 | 偽IAudioOutputで最新世代、停止・切替・tail0・終了破棄を検証。音声コールバックに生成なし |
| SFX-F3 | F1,F2 | SFXタブのスライダー・数値・状態・主要動線 | 全パラメータ操作、固定試聴/保存列、キーボード、150ms集約、一操作一履歴、最低サイズの確認項目を提出 |
| SFX-G1 | E3,F3 | 全体回帰・利用手順と互換性説明 | 8×3の保存/CLI/MCP/DAW値一致、旧音保持、NSF/VGM既存診断、実機UI/試聴の記録・未実行項目の明記 |

17ラン、上限の単純合計11時間20分。レビュー・実行待ち・実機試聴は含まない。保存・音声・UIを一ランにまとめない。特にC2のコピー境界、A2のFormats接続、F2の音声寿命は単体のモデル追加と別に受け入れる。各ランはテスト作成まで含めて上記粒度とし、未完成の入口をユーザーへ公開しない。

## 未決事項

実装開始を止める仕様の未決事項はない。パラメータ集合、単位、原典からの写像、二つの合成入力追加、保存版、旧プリセット共存、乱数、UIの自動試聴タイミング、CLI/MCP、実装分割は本書で確定した。

Phaser/直接音フィルター、任意の多声レシピ、旧Songからのパラメータ逆推定、NES Triangle/DPCM・GB Waveのパラメータ編集、SNES外部サンプル/16音色のSFX取り込み、SNES duty/noise-rateの動的マクロ、チップ間変換、sfxrファイル読み込みは今回の対象外と決定する。未決のまま実装ランへ渡す項目ではない。

**未実行の確認事項**はコンパイル、dotnet build / dotnet test、生成音声・PCM・WAV/NSF/VGMの実測、GUI表示・操作、試聴遅延・クリック、OS間の決定性、旧アプリでの互換動作である。本ランはコードと規約・原典の読み取りによる設計のみを行い、実装コード・テスト・既存文書を変更していない。
