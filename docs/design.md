# Arpeggio 設計書

ファミコン（NES / 2A03）・ゲームボーイ（DMG）・スーパーファミコン（SPC700 風）の曲と効果音を、AI（Claude Code / Codex）と人が一緒に作るチップチューン DAW。
コアライブラリ（`Arpeggio.Core`）の上に CLI（`arpeggio`）・MCP サーバー（`arpeggio-mcp`）・Avalonia 製 DAW（`arpeggio-daw`）を載せる。
AI は CLI / MCP で曲データ（JSON）を編集し、人は DAW のピアノロールで再生・微調整する。

## 決定事項（2026-09-07 対話で確定）

| 項目 | 決定 |
|---|---|
| スタック | .NET 10。Core の言語機能は C# 9 を基本とし、`readonly record struct` のみ例外とする。Unity 非依存で、将来の移植を考慮する（JSON の属性ベース多態化には対応する System.Text.Json が別途必要） |
| インターフェース | CLI + MCP（stdio）+ Avalonia DAW（GUI 主役）。すべて `Arpeggio.Core` を呼ぶだけ |
| 対象チップ | NES（2A03）/ Game Boy（DMG）/ SNES（SPC700 風）。**最初から 3 チップのデータモデルと合成を持つ** |
| GUI の編集方式 | ピアノロール主体 |
| 対象 OS | macOS + Windows。オーディオ出力はクロスプラットフォームのライブラリで抽象化 |
| 中間表現 | JSON（`.arpeggio.json`）。人と AI が読み書きする正本。git 差分でレビューできる |
| SNES の音色 | **合成で自前生成**（波形種別＋パラメータから内蔵サンプルを作る）。外部素材ゼロで鳴る。WAV 取り込みは M2 |
| AI の自己確認 | 書き出した音を数値解析（RMS・ピーク・クリップ・周波数分布）して MCP で返す。画像は出さない（M2） |
| 書き出し | WAV（M1）/ OGG（M2）/ NSF・VGM（M3）/ Unity ランタイム再生（M4） |
| 依存 | System.CommandLine 2.0.11、ModelContextProtocol 2.2.0、Avalonia 12.1.2、オーディオ出力は SDL3-CS 3.4.16 ＋ SDL3-CS.Native 3.4.2（M1-C 指定） |

## マイルストーン

| M | 範囲 |
|---|---|
| **M1** | `Arpeggio.Core`（データモデル・3 チップ合成・WAV 書き出し・履歴）＋ CLI ＋ MCP ＋ DAW（ピアノロール編集・トラック一覧・音色エディタ・リアルタイム再生） |
| M2 | 効果音エディタ（周波数スイープ・エンベロープ）＋ AI 向け音声解析ツール ＋ OGG 書き出し ＋ WAV 取り込み（SNES） |
| M3 | NSF / VGM 書き出し ＋ MIDI 取り込み（チップ制約への落とし込み） |
| M4 | Unity ランタイム再生ライブラリ（`Arpeggio.Core` をそのまま参照して合成再生） |

M1 は Codex への委譲を 3 回に分ける: **A. Core ＋テスト → B. CLI ＋ MCP → C. DAW**。

## 用語

- **ソング**: 1 つの `.arpeggio.json`。チップ種別・テンポ・音色・トラック列を持つ。効果音も「短いソング」として同じ形式で扱う
- **チップ**: `ChipKind { None = 0, Nes = 1, GameBoy = 2, Snes = 3 }`。チップがチャンネル構成（数と種類）を決める
- **チャンネル**: チップ固有の発音単位。`ChannelKind { None = 0, Pulse = 1, Triangle = 2, Noise = 3, Dpcm = 4, Wave = 5, Sample = 6 }`
- **トラック**: 1 チャンネルに対応するノート列。トラック数はチップで固定（NES 5 / GB 4 / SNES 8）
- **ノート**: 開始 tick・長さ tick・MIDI ノート番号・音量（0〜15）・音色 ID・エフェクト列
- **音色（Instrument）**: チップ固有のパラメータ。チャンネル種別ごとに使えるものが決まる
- **tick**: 時間の最小単位。`ticksPerBeat = 48` 固定（4 分音符 = 48 tick。3 連符・16 分・32 分を整数で表せる）
- **マクロ**: 発音開始からフレーム（1/60 秒）ごとに適用する値列（音量・アルペジオ・ピッチ・デューティ）。FamiTracker の macro と同じ概念。ループ位置を持てる

## ファイル形式 `.arpeggio.json`

```json
{
  "version": 1,
  "title": "boss-theme",
  "chip": "Nes",
  "tempoBpm": 150,
  "ticksPerBeat": 48,
  "lengthTicks": 768,
  "loopStartTick": 0,
  "instruments": [
    {
      "id": 1,
      "name": "lead",
      "kind": "NesPulse",
      "duty": "Percent50",
      "volumeMacro": { "values": [15, 14, 12, 10, 9, 9], "loopIndex": 5 },
      "arpeggioMacro": { "values": [0, 4, 7], "loopIndex": 0 },
      "pitchMacro": null,
      "dutyMacro": null
    }
  ],
  "tracks": [
    {
      "channel": "Pulse",
      "channelIndex": 0,
      "name": "Pulse 1",
      "muted": false,
      "notes": [
        { "tick": 0, "durationTicks": 24, "midiNote": 60, "volume": 15, "instrumentId": 1, "effects": [] },
        { "tick": 24, "durationTicks": 24, "midiNote": 64, "volume": 15, "instrumentId": 1,
          "effects": [ { "kind": "PitchSlide", "value": -4 } ] }
      ]
    }
  ]
}
```

- `chip` / `kind` / `channel` / `duty` などの enum は**文字列**で保存する（差分の可読性のため。整数も受け付ける）
- `tracks` はチップの規定チャンネル構成と**順序・数が一致**していなければ不正（`SongValidator` が弾く）。`channelIndex` は同じ種類のチャンネル内で 0 始まり
- `midiNote` は 0〜127。チップの発音可能音域外は合成時に**最寄りの可能音へクランプ**し、`RenderReport` に警告として残す（例外にしない。AI が直せるよう情報を返す）
- `volume` は 0〜15（チップ共通の 4 bit 表現。SNES では 0〜127 へ線形変換）。三角波はチップ仕様どおり音量固定（値は無視して警告）
- `version` は破壊的変更用。今は 1 固定。読み込み時に 1 以外なら例外
- 保存は `System.Text.Json`、インデント付き、キー順は上の例と同じ。`null` の音色マクロは省略せず `null` で書く

### エフェクト（ノート単位）

`NoteEffectKind { None = 0, PitchSlide = 1, Vibrato = 2, VolumeSlide = 3, Arpeggio = 4, Delay = 5 }`

| kind | value の意味 |
|---|---|
| PitchSlide | ノート期間中に value 半音ぶんスライド（正で上、負で下）。効果音のスイープに使う |
| Vibrato | value = 深さ（セント）。速度は 6 Hz 固定 |
| VolumeSlide | ノート期間中に音量を value ぶん線形変化（-15〜15） |
| Arpeggio | 音色マクロと別に、ノート単位で `value` を「半音オフセットを 2 桁 16 進で 2 つ」（FamiTracker の `0xy`）。例: `0x47` = +4, +7 |
| Delay | 発音を value tick 遅らせる |

## チップ仕様（合成に必要な範囲）

### NES（2A03）

| チャンネル | 数 | 特性 |
|---|---|---|
| Pulse | 2 | デューティ 12.5 / 25 / 50 / 75 %。音量 0〜15。周期レジスタ 11 bit（約 54 Hz〜12.4 kHz） |
| Triangle | 1 | 4 bit 階段状の三角波（32 段階）。音量固定。1 オクターブ下の音域 |
| Noise | 1 | 15 bit LFSR。長周期 / 短周期（`NoiseMode { None = 0, Long = 1, Short = 2 }`）。周期は 16 段階のテーブル |
| Dpcm | 1 | 1 bit デルタ変調サンプル。M1 では**ノートを無視して無音**（データモデルには含める。M2 で対応） |

- 音量マクロ・アルペジオマクロ・ピッチマクロ・デューティマクロを持つ
- 出力は非線形ミキサー（`pulse_out = 95.88 / (8128 / (p1 + p2) + 100)`、`tnd_out = 159.79 / (1 / (t/8227 + n/12241 + d/22638) + 100)`）を再現する

### Game Boy（DMG）

| チャンネル | 数 | 特性 |
|---|---|---|
| Pulse | 2 | デューティ 12.5 / 25 / 50 / 75 %。音量 0〜15。ハードウェアエンベロープ（初期音量・方向・ステップ長） |
| Wave | 1 | 32 サンプル × 4 bit の波形メモリ。音量は 0 / 25 / 50 / 100 % の 4 段階 |
| Noise | 1 | 7 bit / 15 bit LFSR。音量 0〜15 |

- 音量マクロ・アルペジオマクロ・ピッチマクロを持つ。Wave 音色は 32 要素の 4 bit 配列を持つ
- 出力はステレオ（左右のチャンネル割り当て）。M1 ではモノラル合成にステレオパンを付ける

### SNES（SPC700 風）

| チャンネル | 数 | 特性 |
|---|---|---|
| Sample | 8 | サンプル再生。ADSR エンベロープ。左右音量。ピッチは 14 bit（P=4096 で原速、最大 4 倍未満） |

- 音色は**合成波形**から作る: `SnesWaveformKind { None = 0, Sine = 1, Square = 2, Saw = 3, Triangle = 4, Pulse = 5, Noise = 6 }`＋ループ有無＋ ADSR（attack / decay / sustain / release）＋ エコー送り量
- エコー: ディレイ（0〜240 ms、16 ms 刻み）・フィードバック・出力音量。ソング単位の設定
- M1 では省略した BRR 圧縮・ガウス補間・レート表 ADSR・FIR・ピッチ変調・ノイズを M2-E で追加する。確定した再生仕様は「M2-E の SNES DSP 再現」を参照する。

## `Arpeggio.Core` の構成

名前空間は `Arpeggio.Core` 直下にフォルダ名を足す（`Arpeggio.Core.Synthesis` 等）。1 ファイル 1 型。ファイルスコープ namespace は使わない。

```
src/Arpeggio.Core/
  Document/
    Song.cs                    ソング（Title・Chip・TempoBpm・LengthTicks・LoopStartTick・Instruments・Tracks・SnesEcho）。可変
    Track.cs                   トラック（Channel・ChannelIndex・Name・Muted・Note のリスト）。ノートは tick 昇順を保つ
    Note.cs                    ノート（Tick・DurationTicks・MidiNote・Volume・InstrumentId・NoteEffect のリスト）
    NoteEffect.cs              readonly record struct（Kind・Value）
    NoteEffectKind.cs
    ChipKind.cs
    ChannelKind.cs
    ChipLayout.cs              ChipKind → 規定のチャンネル構成（順序付き）。Song の新規作成と検証が使う
    SongSerializer.cs          .arpeggio.json の読み書き
    SongValidator.cs           tick 範囲・音量範囲・音色参照・チャンネル構成一致の検証（読み込み直後と保存直前に呼ぶ）
    SongFactory.cs             チップ種別から空ソングを作る（規定トラック・既定音色 1 つ）
  Instruments/
    Instrument.cs              抽象基底（Id・Name・Kind）。JSON の "kind" で派生を判別
    InstrumentKind.cs          enum { None = 0, NesPulse, NesTriangle, NesNoise, NesDpcm, GbPulse, GbWave, GbNoise, SnesSample }
    Macro.cs                   フレーム単位の値列 ＋ LoopIndex（-1 でループなし）
    NesPulseInstrument.cs      Duty ＋ 音量/アルペジオ/ピッチ/デューティ マクロ
    NesTriangleInstrument.cs   アルペジオ/ピッチ マクロ
    NesNoiseInstrument.cs      NoiseMode ＋ 音量/ピッチ マクロ
    NesDpcmInstrument.cs       M1 ではプレースホルダー（サンプル参照無し）
    GbPulseInstrument.cs       Duty ＋ ハードウェアエンベロープ ＋ 音量/アルペジオ/ピッチ マクロ
    GbWaveInstrument.cs        32 × 4 bit 波形 ＋ 出力レベル ＋ アルペジオ/ピッチ マクロ
    GbNoiseInstrument.cs       LFSR 幅 ＋ 音量/ピッチ マクロ
    SnesSampleInstrument.cs    波形種別 ＋ ループ ＋ ADSR ＋ エコー送り ＋ アルペジオ/ピッチ マクロ
    DutyCycle.cs               enum { None = 0, Percent12_5 = 1, Percent25 = 2, Percent50 = 3, Percent75 = 4 }
    NoiseMode.cs
    SnesWaveformKind.cs
    AdsrEnvelope.cs            readonly record struct
  Sequencing/
    TickClock.cs               BPM・ticksPerBeat・サンプルレートから「サンプル位置 → tick」「tick → サンプル位置」を計算
    FrameClock.cs              60 Hz のフレーム境界（マクロ進行用）
    NoteEvent.cs               合成側へ渡すノートオン/オフ（サンプル位置付き）
    TrackSequencer.cs          1 トラックのノート列を時間順の NoteEvent 列へ展開（ループ・Delay エフェクト込み）
  Synthesis/
    IChannelSynthesizer.cs     1 チャンネルの合成器。NoteOn / NoteOff / Render(Span<float>)
    ChannelSynthesizerFactory.cs  ChipKind ＋ ChannelKind → 合成器
    PitchTable.cs              MIDI ノート → 周波数、およびチップ固有の周期レジスタ値へのクランプ
    MacroRunner.cs             Macro をフレームごとに進める共通処理
    Nes/
      NesPulseSynthesizer.cs
      NesTriangleSynthesizer.cs
      NesNoiseSynthesizer.cs
      NesDpcmSynthesizer.cs    M1 は無音
      NesMixer.cs              非線形ミックス
    GameBoy/
      GbPulseSynthesizer.cs
      GbWaveSynthesizer.cs
      GbNoiseSynthesizer.cs
      GbMixer.cs
    Snes/
      SnesWaveformBuilder.cs   SnesWaveformKind → 内蔵サンプル（float 配列）を生成
      SnesVoiceSynthesizer.cs  サンプル再生 ＋ ADSR ＋ ピッチ
      SnesEcho.cs              ディレイライン
      SnesMixer.cs
  Render/
    RenderSettings.cs          サンプルレート（既定 44100）・チャンネル数（2）・ループ回数・末尾余白
    SongRenderer.cs            Song → float PCM。内部で TrackSequencer ＋ 合成器 ＋ ミキサーを組む。ストリーミング API（Render(Span<float>) を繰り返す）と一括 API（RenderAll）
    RenderReport.cs            音域クランプ・音量無視などの警告一覧（AI が読んで直せる形）
    WavWriter.cs               16 bit PCM WAV 書き出し
  History/
    SongHistory.cs             スナップショット方式の undo / redo（Song を JSON で丸ごと保持、最大 50 件）
  Session/
    EditSession.cs             1 ドキュメントの編集セッション（開く・保存・履歴・編集操作の入口）。CLI と MCP が共有
    NoteEditor.cs              ノートの追加・削除・移動・長さ変更・音量変更（tick 昇順を維持）
    InstrumentEditor.cs        音色の追加・更新・削除（参照中は削除不可）
```

### 合成パイプライン

```
Song
 └ TrackSequencer × N  → NoteEvent 列（サンプル位置付き）
      └ IChannelSynthesizer × N → float バッファ（各チャンネル）
           └ チップ固有 Mixer → ステレオ float
                └ SongRenderer.Render(Span<float>) → 呼び出し側（WavWriter / DAW のオーディオ出力）
```

- **ホットパス（`Render`）ではアロケーション禁止**。バッファは `SongRenderer` 生成時に確保し、`Span<float>` へ書き込む。LINQ 禁止。`// perf:` コメントで意図を残す
- 合成は「サンプル単位のレジスタエミュレーション」ではなく「チップの制約を再現した波形生成」。位相累積 ＋ デューティ比較で矩形波を作り、周期レジスタの量子化だけ実機に合わせる（ピッチの実機っぽいズレを再現するため）
- エイリアシング対策は M1 ではしない（チップの音そのものがエイリアスを含む）

### 履歴と検証

- `EditSession` の編集操作は成功時に自動保存し、`SongHistory` へスナップショットを積む。colors と同じ運用
- `SongValidator` は読み込み直後と保存直前に必ず呼ぶ。不正なら `SongValidationException`

### M1-A の境界・単位（2026-09-07 実装時確定）

SNES のピッチ・ADSR・補間については後述の M2-E が本節を更新する。

- ノートは同一トラック内で `[tick, tick + durationTicks)` が重なれば不正。隣接は許可し、終端は `lengthTicks` 以下。Delay は `0 <= value < durationTicks` とし、発音終了は元のノート終端のまま
- ループ回数は最初の全曲再生を含む。2 回目以降は `[loopStartTick, lengthTicks)` を再生する。ループ開始をまたぐノートは境界で再発音する
- `PositionSamples`・`Seek`・`Render` の戻り値はステレオのフレーム数（左右一組）。曲本体と末尾余白をそれぞれサンプル単位へ四捨五入（中間値は絶対値の大きい側）して加算する。`RenderAll` は現在位置から残りを返す。未使用バッファ領域はゼロにする
- マクロの 0 フレーム目を NoteOn で適用する。音量マクロは 0〜15 をノート音量に乗算、ピッチマクロはセント、アルペジオは半音、デューティマクロは `DutyCycle` の整数値。空列は未指定と同じ扱い
- PitchSlide / VolumeSlide の進行は Delay 後の実際の発音期間を基準にする。ノート期間はシーケンサーが合成器の内部制御へ渡す。直接 `IChannelSynthesizer` を使うときの既定期間は 1 秒
- ピッチ・音量効果はマクロと同じ 60 Hz で更新する。1 フレーム未満のノートは効果が進行する前に終了しうる。ループ境界・編集で途中再発音した場合は、その時点からノート終端までを新しい効果の期間とする
- ノイズの MIDI 番号は M1 では音階でなく周期選択に使う。NES はマクロ・効果適用後の音程を 0〜127 に丸めて下位 4 bit で 16 周期表を選ぶ。GB は同じ選択値の下位 3 bit＋1 を分周比、`(127 - 選択値) / 8` をシフト数とする。ノイズに平均律の周波数検証は適用せず、LFSR 幅・モード・決定性・RMS を検証する
- GB エンベロープは `InitialVolume`（0〜15）、`EnvelopeIncreasing`、`EnvelopeStepFrames`（60 Hz のフレーム数、0 で無効）で表す。Wave は `Waveform`（32 要素、0〜15）と `OutputLevel`（0 / 25 / 50 / 100）を持ち、ノート音量はこの出力レベルに乗算する
- SNES の ADSR は秒と 0〜1 の sustain、波形は一周期の内蔵サンプル。Pulse は 25 %、Square は 50 %。`Loop=false` は一周期で終了する。ピッチ倍率は MIDI 60 を基準に最大 4 倍。エコー送りは音色別に適用する
- SNES 風のピッチ倍率は 16 bit の整数へ量子化し、1 倍を 16384、最大値を 65535 とする（最大倍率は厳密には 4 未満）。M1 独自の近似であり、実機レジスタのビット配置互換は対象外
- `Track.Pan` と SNES 音色の `Pan` は -1〜1（左〜右、既定 0）。`Song.SnesEcho` は `DelayMilliseconds`（0〜240、16 刻み）、`Feedback`（絶対値 1 未満）、`Volume`（0〜1）を持つ。0 ms はエコー無効
- チャンネル合成器は -1〜1 のモノラル波形を加算する。NES ミキサーは正負それぞれに非線形式を適用して差を取り、無音時の DC オフセットを避ける
- 三角波の音量警告は固定音量 15 以外を指定したときに記録する。音域警告はノートの基本音に対するクランプを通知する（レジスタ量子化の微差は警告しない）
- 警告は型付きの値で保持し、同じトラック・tick・警告種別を重複登録しない。オーディオ処理の確保を防ぐため最大 4096 件を事前確保し、超過は `DroppedWarningCount` に記録する
- 再生中編集ではノート列・音色列をコピーして参照を交換し、公開済みリストを直接変更しない。`EditSession` は Song と既存 Track の参照を保って変更を公開する。`TrackSequencer` は Render ごとにノート列を取得する。テンポ・長さ・チップ・エコー設定の変更は `Reset` またはレンダラー再生成で反映する
- `Seek` は先頭から捨て読みして位相・マクロ・エコーを復元する。`Reset` / `Seek` はオーディオコールバック外で呼ぶ。通常のノート変更と音色差し替えは次の Render で検出し、変更された発音を再発音する

## CLI `arpeggio`（M1-B）

```sh
arpeggio new boss.arpeggio.json --chip nes --tempo 150 --length-beats 16
arpeggio note add boss.arpeggio.json --track 0 --tick 0 --duration 24 --note C5 --volume 15 --instrument 1
arpeggio note remove boss.arpeggio.json --track 0 --tick 0
arpeggio instrument add boss.arpeggio.json --kind NesPulse --name lead --duty 50
arpeggio instrument set boss.arpeggio.json --id 1 --volume-macro "15,14,12,10,9,9/5"
arpeggio apply boss.arpeggio.json --operations ops.json   # バッチ。`-` は標準入力
arpeggio show boss.arpeggio.json [--track 0] [--json]      # テキストのピアノロール/トラッカー風表示
arpeggio info boss.arpeggio.json --json
arpeggio export wav boss.arpeggio.json boss.wav [--loops 2] [--sample-rate 48000]
arpeggio undo / redo boss.arpeggio.json
```

- ノート名は `C5` / `C#5` / `Db5`（MIDI 60 = C4）。数値も受け付ける
- 終了コードは colors と同じ: 成功 0、引数・操作エラー 1、ドキュメント不正 2、I/O エラー 3
- `show` の既定はトラッカー風テキスト（縦が tick、横がチャンネル）。AI が目視するための読み取り専用

## MCP `arpeggio-mcp`（M1-B）

colors-mcp と同じく stdio、`EditSession` を DI で共有。ツール名は CLI と対応させる:
`new_song` / `open_song` / `save_song` / `song_info` / `show_song` / `add_note` / `remove_note` / `update_note` / `apply_operations` / `add_instrument` / `update_instrument` / `remove_instrument` / `export_wav` / `undo` / `redo` / `chip_reference`（チップの制約を説明する文字列を返す。AI がプロンプトなしで制約を知るため）。

## DAW `arpeggio-daw`（M1-C）

Avalonia 12。MVP。`Presenters/` にプレゼンター、`Views/` に AXAML と View。

- **トラック一覧**（左）: チャンネル名・ミュート・選択
- **ピアノロール**（中央）: 縦が音程、横が tick。複数選択・矩形選択・一括移動／長さ変更・ペイント削除・内部クリップボード。既定グリッドは 16 分音符で切替可能。再生位置カーソル、上部のスナップ選択、下部の固定 80px 音量レーンを持つ。操作は「FL 式ピアノロール」を参照する
- **音色エディタ**（右）: 選択トラックのチャンネル種別で使える音色パラメータ。マクロは数値列の編集
- **トランスポート**（下）: 再生 / 停止 / ループ切替 / テンポ / 位置表示
- **オーディオ出力**: `IAudioOutput`（Start / Stop / コールバックでバッファ要求）を抽象化し、実装は SDL3-CS 3.4.16 ＋ SDL3-CS.Native 3.4.2。`SongRenderer.Render(Span<float>)` をコールバックから呼ぶ。編集は再生中でも反映される（Song の変更を次バッファから拾う。ロックは最小限）
- **ファイル監視**: AI が CLI / MCP で保存したら自動で再読み込み（colors-viewer と同じ）。編集中の競合は「外部変更を検知したら再読み込みの確認」で対処
- `Awake` 相当の暗黙初期化を持たず、`MainWindowPresenter` を `Program` から明示的に組み立てる
- 配布はランタイム同梱の macOS `.app`／Windows ZIP。`tools/build_app.sh` で生成し、macOS の Bundle ID は `dev.pisuke.arpeggio`。Developer ID 署名・公証は行わず、ローカル実行用の ad-hoc 署名だけを付ける。
- 引数なしでは既存の SNES デモをユーザーデータ領域へ初回コピーして開く。macOS の関連付け／Dock ドロップは `App` が受信し、CLI と同じ Presenter／文書読み込み経路へ渡す。一文書を維持し、複数ファイルの一括要求と未保存時の別文書への切り替えは拒否する。同一パスの通知は前面化のみ。

### Avalon による GUI 観測（2026-09-08）

- Debug かつ隣接 `../Avalon/src/Avalon/Avalon.csproj` が存在する場合だけ参照と `AVALON` 定数を有効にする。Release の配布物へ操作サーバーを含めない。Avalon がない環境で単独ビルドできる構成を維持する。
- Program の AppBuilder に組み込み、`onStarted` で状態取得関数を登録する。画面生成前でも登録できるよう Presenter / View は取得時に解決する。編集・再生の状態は Presenter の読み取り専用プロパティ、座標は View の現在のレイアウトから取得する。
- 文書の読み込み・明示保存の成功通知と、書き出しの既存表示通知を診断ログへ接続する。終了時に購読・状態登録・ホストを解放する。
- 座標はウィンドウ内 DIP とし、可視領域の原点・縦横スクロール・tick 幅・半音高を公開する。詳細なキー・座標式・操作 JSON は [avalon.md](avalon.md) を正本とする。
- この worktree の現行編集は単一選択。FL 式の複数選択・コピペ・音量レーン・スナップ選択は未反映であり、Avalon 統合を理由に編集挙動は変更しない。

### M1-C の編集・再生境界（2026-09-07）

- DAW は `DawDocument` が一時作業ファイル上の `EditSession` を所有する。Core の自動保存・検証・参照交換・`SongHistory` をそのまま利用し、正本への書き込みは Ctrl+S で正本用 `EditSession.Save()` を呼ぶ。Core / CLI / MCP の自動保存仕様は変えない。
- ノートドラッグは有効な途中位置を作業セッションへ公開する。押下時に履歴を控え、終了時にドラッグ全体を一操作へまとめる。不正な重なりは確定せず、最後に成功した位置を維持する。Alt は整数 tick 単位、通常は選択したスナップ単位（既定 12 tick）。複数編集は全件を原子的に公開する。
- 音色の既定選択は DAW セッション内でトラック別に保持する。選択ノートがあればその音色を表示・割り当てる。音色パラメータと名前は「音色を適用」で一履歴に確定し、次のバッファから反映する。マクロ空欄または `null` は解除。
- SDL audio stream の get コールバックが 44100 Hz・512 frames の float stereo バッファを要求する。合成はコールバック内の `SongRenderer.Render` のみ。表示用位置・完走・警告は値を公開し、UI は 33 ms 間隔で読む。
- Space／再生ボタンは停止位置からの再開と停止を切り替える。停止ボタンは先頭へ戻す。ループ切替・テンポ・長さ変更は停止中に設定を取り直し、再生中だった場合は先頭から再開する。テンポ・長さを含みうる undo/redo も停止・Reset の境界で行う。
- ファイル監視は保存内容の正規化 JSON と保存基準を比較する。自己保存の遅延通知を時間窓では判定しない。未保存時の外部更新はステータス表示で R の確認を待ち、保存直前にも外部更新を確認する。
- ピアノロールは可視範囲だけカスタム描画し、鍵盤・ルーラーをスクロールと同期する。ブラシ・Pen・音名・可視ルーラーラベルを描画前に用意する。音色一覧と音色参照が変わらない限り入力部品を作り直さない。

### FL 式ピアノロール（2026-09-08）

- 選択は選択トラックの開始 tick 集合。Ctrl＋空白左ドラッグで矩形選択し、時間範囲の境界への接触・部分重なりと、両端の音高行を含める。既存選択は置換する。Shift＋ノート左クリックは選択を反転する。選択自体は履歴を作らない。
- 空白の通常左クリックはノート追加。ただし二音以上の選択中は選択解除だけを行う。初期長は 24 tick（1/8）で、直前に置いた／掴んだノートの長さを記憶する。押下したまま横へ動かすと、ポインターを終端として長さを変更する。追加と長さ決定は合わせて一履歴。
- 選択ノートの本体ドラッグは選択全体を同じ tick・音高差で移動する。選択外のノートを掴む場合はその一音を選択する。右端ドラッグは全選択の長さを同じ差分で変え、各音を最短 1 tick に制限する。重複・曲の範囲・MIDI 音高・効果制約は Core Validator に任せ、一件でも不正ならその変更全体を拒否して直前の有効位置を維持する。
- 右押下でヒットした音を即削除し、そのまま右ドラッグすると入力通知間の線分を含む軌跡上の音を削除する。他トラックのゴーストは対象外。全ドラッグを一履歴にまとめる。
- スナップは `1/1=192`、`1/2=96`、`1/4=48`、`1/8=24`、`1/16=12`（既定）、`1/3=16`（一拍を三分割）、`なし=1` tick。Alt は一時的な解除で、選択した単位は変えない。
- 音量レーンは開始 tick に縦棒を描き、選択音は Accent、それ以外はチャンネル色。上下入力の位置から掴んだ音の音量を指定し、その音との差分を全選択へ適用する。各音を 0〜15 に制限し、ドラッグ全体を一履歴にする。選択外の棒を押すとその音だけを選択する。横スクロールとズームはピアノロールに同期する。
- コピーは相対 tick・音高・長さ・音量・音色 ID・独立した効果配列を保存する。貼り付けは同じ文書・トラックのみ。位置は現在の再生カーソルを切り捨てた整数 tick（未進行なら 0）とし、スナップで丸めない。時間区間が重なる既存音を音高によらず置き換える。隣接は置き換えず、置き換え件数をステータスに表示する。貼り付けた音だけを新たな選択にする。
- 選択ノートは既存の Accent 2px 枠。矩形選択中は半透明 Accent の矩形を重ねる。ステータスに `3 音選択` の形式で件数を表示し、未選択なら省略する。既存の音色・効果パネルは選択集合の先頭 tick を代表音として表示する。

| キー | 操作 |
|---|---|
| Delete / Backspace | 選択全削除 |
| Ctrl+A | 選択トラックの全ノートを選択 |
| Ctrl+C / X / V | コピー／切り取り／再生カーソルへ貼り付け |
| Ctrl+D | 選択の最初の開始から最後の終端までの長さだけ右へ複製。クリップボードは維持 |
| ↑ / ↓ | 全選択を半音上下 |
| Shift+↑ / ↓ | 全選択を一オクターブ上下 |
| ← / → | 全選択を現在のグリッド一つ分左右 |
| Ctrl+↑ / ↓ | 全選択の音量を ±1 |

数値・テキスト・ComboBox などの入力部品にフォーカスがある間は、既存どおりノート編集ショートカットを奪わない。

### DAW のビジュアル規約（M2-D / 2026-09-08）

判断順は **操作可能性 > 可読性 > 情報階層 > フィードバック > 一貫性 > アクセシビリティ > 効率 > 美観**。暗色のチップチューン・スタジオとして統一する。機能・ショートカット・文言・既存 AXAML 要素の親子関係・`x:Name`・Presenter は維持する。

`Themes/ArpeggioTheme.axaml` に Color（末尾 `.Color`）と同名の SolidColorBrush、寸法、コントロールスタイルを定義する。`App.axaml` は Icons / ArpeggioTheme の ResourceDictionary を読み込み、FluentTheme の後へ `Arpeggio.Styles` を適用する。Views の色リテラルは禁止する。

| ブラシトークン | 色 | 用途 |
|---|---|---|
| `Arpeggio.Background` | `#0E131A` | ウィンドウ・ピアノロール背景、色帯上の暗い文字 |
| `Arpeggio.Panel` | `#161D27` | 左右ペイン・トランスポート・ルーラー |
| `Arpeggio.PanelRaised` | `#1E2733` | 入力・ボタン・位置表示の面、黒鍵 |
| `Arpeggio.Border` | `#2A3644` | 枠・区切り・スクロールのつまみ |
| `Arpeggio.TextPrimary` | `#E6EDF3` | 本文・白鍵 |
| `Arpeggio.TextSecondary` | `#8B9BAE` | 補足・非選択タブ・拍目盛り |
| `Arpeggio.TextDisabled` | `#55637A` | 無効状態 |
| `Arpeggio.Accent` | `#F5C451` | 主操作・フォーカス・選択ノート枠・エフェクト印 |
| `Arpeggio.Danger` | `#FF6B7A` | 削除・ミュート中・警告あり |
| `Arpeggio.Playhead` | `#FF4D6D` | 再生位置の 2px 線と三角 |
| `Arpeggio.Channel.Pulse` | `#5DF2A4` | `P1` / `P2` |
| `Arpeggio.Channel.Triangle` | `#FFB347` | `TRI` |
| `Arpeggio.Channel.Noise` | `#FF6FD8` | `NOI` |
| `Arpeggio.Channel.Dpcm` | `#9AA5B1` | `DPCM`（無音チャンネルは控えめ） |
| `Arpeggio.Channel.Wave` | `#4FD1FF` | `WAV` |
| `Arpeggio.Channel.Sample` | `#B892FF` | `S1`〜`S8` |
| `Arpeggio.Grid.Bar` | `#4A5A6E` | 小節線 |
| `Arpeggio.Grid.Beat` | `#2E3A48` | 拍線 |
| `Arpeggio.Grid.Sixteenth` | `#1C2531` | 16 分線 |
| `Arpeggio.Grid.BlackKey` | `#0A0F15` | 黒鍵に対応する行の面 |

- 本文・補足は面とのコントラスト 4.5:1 以上。現在の本文の最小値は 12.76:1、補足は 5.31:1。チャンネル色上には Background 色の文字を載せ、最小 7.45:1 を確保する。無効表示は本文のコントラスト条件の対象外。
- 色と短い記号は `ChannelPalette.GetBrush(ChannelKind)` / `GetShortLabel(ChannelKind, int)` へ集約する。番号は Core と同じ同種内の 0 始まり。トラック一覧・ピアノロール・音色見出しの三か所から同じ関数を呼ぶ。None・未定義種別・範囲外番号は例外とする。
- パレットは UI を起動せず使える純関数とし、不変ブラシと記号を共有する。AXAML の対応色との一致を静的確認する。その他のカスタム描画色とフォントは `ThemeResources` が Application.Resources から各コントロールの初期化時に取得し、描画中にリソース検索しない。
- 本文は Inter。BPM・位置・tick・音色の数値入力・鍵盤・ルーラー・ノートの記号は `Cascadia Mono, Menlo, Consolas, monospace`。見出し 13 / 本文 12 / 補足 11 / 位置 18。位置は Accent の文字と PanelRaised の面で区別する。
- 余白・間隔は 4 / 8 / 12 / 16。角丸は入力・ボタン 4、カード・ToolTip 6。線幅は 1、フォーカス・選択タブ下線・選択ノート・再生線のみ 2。ノートの角丸は描画仕様として 2。
- Button は PanelRaised と Border、primary は Accent と暗い文字、danger は Danger の枠。hover / pressed / disabled / focus-visible を明示する。ボタンのフォーカス枠は重ね描きし、キーボード移動で内容をずらさない。TextBox / ComboBox / CheckBox は PanelRaised と Border、フォーカス時 Accent。選択タブは下線、スクロールは 8px、ToolTip は PanelRaised。
- アイコンは `Themes/Icons.axaml` の 24×24 塗りパス。`Icon.Play` / `Stop` / `Loop` / `Export` / `FolderOpen` / `Analyze`（波形と虫眼鏡）/ `Sfx` / `Mute` / `Unmute` / `Add` / `Remove` / `Save` / `Warning` / `Import` / `App`。ボタンは文字列 Content を保持したまま ContentTemplate 内の PathIcon とラベルで表示する。動的文言とツールチップを維持し、ミュートも CheckBox の入力契約を維持してテンプレートだけをボタン風にする。
- ピアノロールは黒鍵の行を暗くし、C の行の下端だけ薄く区切る。選択トラックのノートはチャンネル色、音量 0〜15 を 45〜100% の RGB 明度へ対応させる。下端に暗い 1px 線、選択に Accent の 2px 枠、効果ありに右上の三角を描く。他トラックは各色 25% の不透明度、枠なし。記号の小さな面は音量やゴーストの重なりによらず文字のコントラストを保つ。
- ノート内の記号は幅が足りる場合に描き、短いノートや縮小時はチャンネル記号・トラック名のツールチップを併用する。判別のためにクリック領域やノートの長さを広げない。鍵盤の行高は PianoRollControl.NoteHeight と完全に共有し、C 音のみ等幅 11 で表示する。ルーラーは小節番号と小さな拍目盛りを描く。
- Brush / Pen / Geometry / FormattedText は描画前に用意する。音量 16 段階の不変ブラシとチャンネル記号はチャンネルが変わったときだけ再生成する。
- アプリアイコンは `assets/icon/arpeggio.svg`（1024×1024）。角丸 180 の背景、Pulse → Triangle → Noise → Sample の上昇する四矩形、線幅 64 の一周期矩形波という六図形で構成する。外部参照なし。ヘッダーは同モチーフの Icon.App。PNG / icns / ico 変換と ApplicationIcon / Window.Icon の設定は依頼者が行う。

## テスト方針

- `tests/Arpeggio.Core.Tests`（xunit）。合成器はチップごとに**周波数・デューティ・音量の期待値を数値で検証**する（ゼロクロス数から周波数を推定、矩形波の High 比率からデューティを検証、RMS から音量を検証）
- `SongSerializer` はラウンドトリップ、`SongValidator` は不正ケースを網羅
- `SongRenderer` は短いソングを一括レンダリングして長さ・無音でないこと・`RenderReport` の警告を検証
- ホットパスのアロケーションは `GC.GetAllocatedBytesForCurrentThread` の差分で 0 を検証する

## M2-E の SNES DSP 再現（2026-09-08）

- M1-A / M2-B の SNES 再生仕様を本節で更新する。JSON は version 1 の追加のみ。PCM Base64・元レート・元サンプル単位のループ位置は保存形式として維持する。
- 全ボイス・ADSR・ノイズ・エコーを 32000 Hz で進め、ステレオ合成後に線形補間で出力レートへ変換する。変換は過去二点を使う因果的処理で、最大一 DSP サンプルの遅延を持つ。シーケンサーと 60 Hz のマクロ更新は出力時間軸のまま。
- ピッチは 14 bit（0〜16383）、原速は 4096、増分は P / 4096。内蔵波形は 128 サンプル周期とし、P = round(周波数 × 128 / 32000 × 4096)。埋め込みは P = round(2^((note-root)/12) × 元レート / 32000 × 4096)。範囲外はレジスタ端へクランプする。
- 内蔵・埋め込みとも BRR のエンコード→デコードを経由する。9 byte / 16 sample、shift 0〜12 × filter 0〜3 の誤差比較。終端の不足サンプルは最終値で埋める。ループ開始は下側の 16 境界、終端は上側の 16 境界へ丸める。キャッシュとループ情報は音色の設定時に準備し、NoteOn は参照を選ぶだけ。
- ガウス補間は 512 エントリの実機係数表を使う。4 点の係数は table[255-f], table[511-f], table[256+f], table[f]（f は 8 bit 小数部）、スケールは 2048。
- ADSR は 11 bit 音量、32 エントリの周期表を使用する。attack は 2×A+1、decay は 2×D+16、sustain rate は直接指定。attack=15 は 2 DSP サンプルで最大、sustainLevel=7 / sustainRate=0 は最大値を保持する。release は毎 DSP サンプル 8 減少する固定動作。秒指定は attack / decay の所要時間が最も近いレジスタへ量子化し、sustain は 1/8 刻み、sustain rate は保持の 0 とする。ReleaseSeconds は保存するが固定 release を優先する。AdsrRegisters があれば秒指定より優先。
- エコーは 16 ms = 512 DSP サンプル、0 ms は従来どおり無効。遅延読み出し→8 タップ FIR→wet 出力とフィードバックの順。係数は signed 8 bit / 128。左右履歴は独立し、フィードバック書き込みとマスター出力は 16 bit に飽和する。
- PitchModulation はボイス 1〜7 のみ有効。同じ DSP 時刻の直前ボイスの ADSR 後・左右音量前の signed 16 bit 出力で P' = clamp(P + P×prev/32768, 0, 16383)。ミュートは聴取出力とエコー送りを消し、変調源の DSP は継続する。
- NoiseEnabled はサンプル経路を 15 bit LFSR に置換する。NoiseRate は ADSR と共通の周期表（0 は停止）。音色ごとのレート指定を成立させるためノイズ状態はボイスごとに持ち、NoteOn で同じ seed へ戻す。
- ノート音量は 0〜127、合成 Pan は左右 0〜127 に丸めて signed 8 bit 音量経路へ渡す。既存の八ボイス分のヘッドルームを維持する。SPC の RAM・命令・共有カウンター位相・BRR ループごとの予測履歴再デコードは対象外。

| FIR プリセット | 係数（新しい履歴から順） | 用途 |
|---|---|---|
| Flat | 127, 0, 0, 0, 0, 0, 0, 0 | ほぼフラット |
| LowPass | 16, 32, 32, 32, 16, 0, 0, 0 | 高域を抑えた残響 |
| HighPass | 64, -64, 0, 0, 0, 0, 0, 0 | 差分による低域除去 |
| Wide | 64, 0, 32, 0, 16, 0, 16, 0 | 時間方向に広がる残響 |

## M2-E の SNES 音色バンク（2026-09-08）

- `SnesSampleInstrument.Preset` は小文字の正式名を保存する。JSON version は 1。生成 PCM / BRR は保存せず、プリセット名から復元する。`sampleData` は従来どおり PCM Base64 であり、両方を指定したドキュメントはエラー。検証を経由しないボイス単体では `SampleData` を優先する。
- `SnesInstrumentCatalog` が名前・カテゴリ・説明・推奨 ADSR レジスタ・ルート MIDI 音・ループ・EchoSend・生成長を持つ。`SnesInstrumentBank.Build(name)` は素材を合成し、`BrrSample.Create` で BRR 往復した新しいキャッシュを返す純関数。推奨値は `Catalog.Get(name)` から取得する。
- 素材は全て 32000 Hz。持続系は 128 サンプルの一周期ループ、ループ範囲は全体。BRR ブロック境界を保つため周期は整数とし、原音 250 Hz に最も近い root MIDI 59 を割り当てる。C4 再生は約 264.9 Hz（平均律から約 +21 cent、受け入れ範囲内）。減衰系は root 60、基音は平均律 C4。ドラムの root 60 は原速再生の基準。
- 持続系の strings は高次倍音を削った鋸歯状倍音の二声。短い周期内でデチューンの位相差を滑らかに戻し、ループ境界を接続する。brass は奇数倍音中心＋微小位相変調。organ は第 1〜4 倍音の固定比。choir は基本波中心と第 4 倍音（素材の約 1 kHz）の山。flute は弱い第 2 倍音と息のノイズ。lead は 25 % パルスの帯域制限合成。bass は三角波寄りの奇数倍音と第 2 倍音。
- piano は 800 ms、高次倍音が先に消えるアタックと遅い基音減衰。pluck は 500 ms の速い倍音別減衰。bell は 1000 ms、1 : 2.76 : 5.4 の非整数倍音。末尾フェードを掛け、いずれも既定でワンショット。
- ドラムは kick 300 ms（150→50 Hz を 60 ms で下降＋微小クリック）、snare 150 ms（200 Hz のトーン＋ノイズ）、hat 40 ms、openhat 200 ms、tom 200 ms、crash 800 ms。ドラムの個別指定時間を「減衰系 0.3〜1.0 秒」の共通目安より優先する。hat / openhat / crash は二階差分で高域を強調したノイズ。乱数はプリセット別の名前付き固定シードの `System.Random` を使う。
- 生成素材は DC を除去し、ピークを 0.72 にそろえて BRR へ入力する。キャッシュの生成は Preset の代入時だけ。Loop / LoopStart / LoopEnd の変更は同じ PCM のループ情報だけ更新する。NoteOn / Render では生成・復号・辞書登録を行わない。
- 推奨値は未指定プロパティの既定値として働く。JSON の項目順に依存せず、明示した Loop / SampleRate / RootMidiNote / EchoSend / AdsrRegisters を優先する。`adsrRegisters: null` は秒指定 Envelope に戻す。CLI `--preset` は既存素材・ループ・ルート・ADSR・EchoSend を推奨値へリセットしてから同じコマンドの明示オプションを適用する。Pan / 名前 / マクロは保持する。NoiseEnabled は false に戻す。
- `instrument import-wav` は検証成功後に Preset を解除する。プリセットへ切り替える CLI は SampleData を解除する。入力 JSON の二重指定を黙って解消することはしない。
- `new --chip snes --bank` / MCP `new_song(bank: ...)` は八音色を ID 1〜8 として作り、トラック名をプリセット名にする。`Track.DefaultInstrumentId`（省略時 null、保存時 null は省略）で割り当てを永続化する。SNES 専用で、存在・チャンネル適合性を検証し、割り当て中の音色削除は拒否する。音色 ID 省略時の解決順は明示 ID → トラック既定 ID → 従来のチャンネル別選択。改名・保存復元・履歴操作でも割り当てを保持する。

| バンク | トラック 0〜7 |
|---|---|
| orchestral | strings / brass / flute / choir / bass / kick / snare / hat |
| band | lead / organ / pluck / bass / piano / kick / snare / hat |
| chip | lead / lead / bass / organ / bell / kick / snare / hat |

orchestral の依頼一覧は kick / snare を分けると 9 音色になるため、8 ボイス制約を優先して piano を除いた暫定編成とする。piano 自体はバンクのプリセットとして利用できる。bank 未指定時は従来どおり名前 lead の合成波形音色が一つだけで、Preset は null。

- CLI: `instrument presets snes`、`instrument add ... --kind SnesSample --preset strings --name str`、`instrument set ... --id N --preset brass`。推奨レジスタは `--adsr-registers attack,decay,sustainLevel,sustainRate`、ルートは `--root C4` で上書きできる。`--adsr` はレジスタ指定を解除して秒指定に戻す。両方ある場合は `--adsr-registers` を優先する。
- MCP: `snes_presets()` は一覧を返し、既存 `add_instrument` / `update_instrument` の音色 JSON に `preset` を指定できる。更新は既存契約どおりオブジェクト全体の置換。show / instrument list は `SnesSample strings` の形で名前参照を表示する。
- M2-E-A の BRR・補間・ADSR・FIR 自体は変更しない。release は既存 DSP の固定約 8 ms。既存 SFX の素材は維持し、DAW のプリセット選択 UI は対象外。

### M2-E-C の DAW SNES 音色編集（2026-09-08）

- SNES 音色パネルの先頭に、持続系／減衰系／ドラムの選択不可見出しを備えたプリセット ComboBox を表示する。先頭の「（合成波形）」は Preset を解除する。プリセット選択はカタログの推奨 ADSR・ルート音・素材レート・ループ・EchoSend を反映し、NoiseEnabled を解除する。名前・Pan・マクロは保持し、InstrumentEditor.Update の一履歴で公開する。
- 埋め込み SampleData がある状態のプリセット選択は「埋め込みサンプルを使用中。先に解除してください」で拒否する。明示的な「埋め込みサンプルを解除」ボタンを用意し、解除も Undo 可能な一履歴にする。
- ADSR レジスタは四つの整数欄とし、全欄空白なら AdsrRegisters を null にして秒指定へ戻す。部分空白・非整数・範囲外は Danger 枠で示し、名前・DSP フラグも含めた適用全体を拒否する。DSP フラグとレートは専用のチェックとスライダー・数値表示で編集し、既存の適用ボタンで一履歴にまとめる。
- ボイス 0 の PitchModulation は入力を無効化し、理由をツールチップで示す。既に他ボイスと共有している変調音色の設定は、ボイス 0 で別の項目を編集しても保持する。Preset・SampleData・未確定の NoiseEnabled 入力のいずれかが有効なら Waveform を無効化する。
- ソング全体のエコーは SNES のみトランスポート脇の Flyout に置く。遅延は 0〜240 ms の 16 ms 刻み、フィードバックは絶対値 1 未満、音量は 0〜1、FIR は Flat / LowPass / HighPass / Wide。任意 FIR はプリセットを選ぶまで保持する。適用は一履歴とし、再生中は停止→設定公開→Reset→先頭から再開する。
- CLI バンクの Track.DefaultInstrumentId をパネルの既定選択にも使う。DAW の新規作成や SFX タブへのバンク作成機能は追加しない。

## 未決事項

- SPC ファイル・RAM・命令単位までの互換性は M3 以降で決める
- DAW での MIDI キーボード入力 — 要望が出たら

### M2-A の境界・単位（2026-09-07）

- 解析入力は有限の float ステレオ（左右一組）。長さはフレーム数 / sampleRate、RMS は左右全サンプルの二乗平均、ピークは絶対値最大。クリップ数は左右を別サンプルとして `abs(value) >= 1` を数える。空入力は長さ・割合・帯域比率を 0 とする。
- dBFS は振幅 1 を 0 dBFS、ゼロおよび極小振幅は -160 dBFS に制限する。JSON に非有限数を出さない。左右バランスは左 RMS dBFS − 右 RMS dBFS（正が左、両側無音は 0）。
- 時系列は先頭から重複しない既定 100 ms 窓。窓長はサンプルへ四捨五入し最低 1 フレーム、最終端数窓も一窓と数える。無音割合は設定閾値未満の窓数 / 全窓数。`new AnalysisSettings()` は既定値、default は不正設定。
- FFT は既定 2048 点の基数 2。各時系列窓を FFT サイズ以下の連続ブロックに分け、実データ長の Hann 窓を掛けてゼロ詰めする（1〜2 フレームは矩形窓）。左右それぞれのブロック平均を除去してから変換し、パワーを加算して逆相による相殺を避ける。非対称 Pulse の直流成分を音程と誤認しないための除去であり、RMS・ピーク・警告は元波形から計算する。Hann の二乗和で補正し実フレーム数で重み付けする。作業配列は解析呼び出し内で再利用する。
- 支配的周波数は DC を除く最大パワービンを対数パワーの放物線で補間する。最寄り音名は平均律 A4=440 Hz、MIDI 0〜127 外と無音は null。スペクトル重心は DC を除く片側パワー加重平均。帯域比率も同じパワーを全窓で集計する。DC 除去後のパワーがゼロなら全比率を 0 とし、低域 <200 Hz・中域 200〜2000 Hz（両端を含む）・高域 >2000 Hz とする。音名は倍音・ノイズを含む音の基音推定ではない。
- 警告はクリップ、全体 RMS < -30 dBFS、先頭 50 ms の RMS が無音閾値未満、末尾から 50 ms 刻みで連続無音が 1 秒以上、左右差の絶対値 >6 dB。50 ms 未満の入力は存在する先頭区間で判定し、空入力は小音量警告のみ。端点判定は表示窓長に依存しない。
- ソング解析は 44100 Hz・既定 1 周・末尾余白 0 秒。複製した Song にのみソロを適用し、選択トラックの既存ミュートを解除する。解析レポートの renderWarnings / droppedRenderWarningCount に合成警告を保持し、テキストは音響警告と同じ警告節に表示する。WAV 自体から合成警告は復元できない。
- テキストは要約 → 警告 → 先頭 40 窓、JSON / MCP は全窓。WAV 読み込みは RIFF PCM 16 bit のモノ / ステレオ。PCM は負側 32768・正側 32767 で正規化し、正負の飽和端点を ±1 に対応させる。未知チャンクと偶数境界パディングを読み飛ばし、宣言長・フレーム境界・形式を検証する。Stream オーバーロードは呼び出し側が所有し、読み取り可能かつシーク可能なストリームを受ける。
- SFX はテンポ 150、Jump 18 tick（150 ms）、Coin 18（150 ms）、Hit 18（150 ms）、Explosion 48（400 ms）、PowerUp 48（400 ms）、Laser 18（150 ms）、Blip 5（約 41.7 ms）、Select 12（100 ms）。Blip は個別指定の 30〜50 ms を共通目安の 0.1 秒より優先する。ノート終端を lengthTicks とし、SNES はループする内蔵 Pulse / Noise とリリース 0 秒で構成する。
- `sfx new` / `new_sfx` は既存パスの上書きを拒否し、完成した Song を保存後にセッションを開く。既定チップは NES。既存 export の末尾余白 0.5 秒は変更しない。SFX 本体長の検証・書き出しでは CLI `--tail 0` / MCP `tail: 0` を指定する。

### ミュートの意味論（2026-09-08 変更）

- `Track.Muted` は**ミキサー段で出力を 0 にする**だけで、シーケンサと合成器は鳴らし続ける。SPC700 のピッチモジュレーションはボリューム前の前ボイス出力（OUTX）を参照するため、ミュートした低音を変調源として使える。NES / GB でも同じ扱い（負荷は増えるがチャンネル数が少ないので無視できる）

### M1-B の入力・出力境界（2026-09-07 実装時確定）

- バッチは `kind` ごとに必須値を検証するモデルとし、任意値を nullable で表す。UpdateNote は指定した項目だけを更新し、`tick` が検索位置、`toTick` が移動先。effects の省略は保持、空配列は解除。詳細な JSON 項目は README のバッチ表を参照する。
- バッチの track は各操作を優先し、省略時だけ CLI `--track` / MCP `track` を使う。どちらも無ければノート・トラック操作は引数エラー。空バッチは拒否する。
- 操作列全体を先に解析し、候補ソング上で各操作後に制約を検証する。最後に既存 `EditSession.Change` で一度だけ保存・履歴確定する。失敗時は公開状態を変更しない。
- `show` は `[fromTick,toTick)` を fromTick 起点の 12 tick 行で示す。行内の非グリッド開始は `@実tick`、複数開始および継続音との併存は `;` で併記し、短い音も省略しない。JSON は範囲に交差するノートの元の tick・長さとテキストを返す。
- CLI の音色追加は既存最大 ID + 1。set は指定値だけを更新し、マクロ文字列 `null` は解除。kind 変更は ID・name と意味が同じ共通項目を保持し、専用項目は新 kind 既定値。MCP とバッチの音色更新は保存形式の JSON 全体で置き換える。
- CLI の履歴は colors と同じ `<path>.history/state.json`（current/undo/redo）を使う。外部更新との current 不一致では履歴を復元しない。履歴保存の I/O 失敗時は曲を操作前のバイト列へ戻す。曲と履歴は別ファイルの置換であり、プロセス強制終了まで含む二ファイルの永続トランザクションは対象外。
- MCP は `EditSession` を DI 共有し、同じセッションのツール実行を直列化する。戻り値は文字列、エラーは error/exitCode JSON。show の既定と chip_reference は CLI と同じテキスト。`UseStructuredContent` は指定しない。

### M2-B の境界・単位（2026-09-07）

SNES の保存形式は維持し、キャッシュ・補間・ピッチ・ループ再生については M2-E が本節を更新する。

- JSON version は 1 のまま。SNES の `sampleData` は little-endian PCM 16 bit モノラルの Base64、null は従来の合成波形。空・奇数バイト・不正 Base64 を拒否し、上限は 2 MiB（2,097,152 byte、1,048,576 サンプル）。Base64 文字列長にも対応する上限を設ける。
- `sampleRate` の既定は 44100 Hz、埋め込み時は正の整数。`rootMidiNote` は 0〜127、既定 60（C4）。ループは `[loopStart, loopEnd)`、単位はモノラルのサンプル数。既定は両方 0、loopEnd=0 は末尾。loop=true かつ埋め込み時だけ `0 <= start < end <= count` を検証する。合成波形では追加メタデータを再生に使わない。
- WAV は既存 WavReader と同じ PCM 16 bit mono/stereo。左右の算術平均を正負端点に対応する PCM へ四捨五入する。元レートを保持し、リサンプル・正規化・トリミングはしない。WavReader の内部上限指定で PCM 配列確保前に過大な入力を拒否する。壊れた WAV・上限超過は引数エラー、ファイル I/O は I/O エラーとする。取り込み失敗時は音色を変更しない。CLI / MCP は複製音色を既存 InstrumentEditor.Update で一履歴として保存する。
- Base64 の代入時（JSON 読み込み・取り込み・音色編集側）に float 配列を作り、音色が所有する。検証失敗情報も保持し InstrumentValidator で拒否する。NoteOn は配列参照だけ取得し、Render 中のデコード・配列生成・辞書登録を行わない。
- 埋め込みサンプルは先頭から再生し、初回のみループ前区間を通る。倍率は `2^((note-root)/12)` を 1/16384 刻み・1〜65535 の整数へ制限し、位置増分は倍率 × 元レート / 出力レート。マクロ・効果もこの root 基準。線形補間はループ終端から開始へ接続し、複数周分の飛び越しも剰余で処理する。非ループ終端では最終値を補間用に保持し、末尾到達で停止する。ADSR・エコー・パンは既存仕様を継続する。
- show（JSON 内の text を含む）と instrument list は `sample 12345 smp @ 22050 Hz` の要約を表示する。list --json は既存音色のプロパティを保持し sampleData のみ除去して sampleSummary を加える。保存 JSON には Base64 を保持する。
- OGG は Arpeggio.Codecs に隔離し OggVorbisEncoder 1.2.2 を参照する。Core は BCL のみ。入力は有限の float interleaved stereo、範囲外振幅は ±1 にクランプする。quality は有限の -0.1〜1、既定 0.5 の VBR。WAV と同じ loops / sample-rate / tail と警告出力を使う。
- OggWriter の Stream は呼び出し側所有で閉じない。固定長の左右バッファでエンコーダへ供給し、三ヘッダーをフラッシュしてから音声を送り、EOS と残ページを出力する。無効入力は出力ファイルを開く前に拒否する。OGG の短い入力ではヘッダー分が WAV より大きくなりうるため、サイズ比較は数秒の同一波形で行う。
