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
| Sample | 8 | サンプル再生。ADSR エンベロープ。左右音量。ピッチは 16 bit（サンプル周波数の 0〜4 倍） |

- 音色は**合成波形**から作る: `SnesWaveformKind { None = 0, Sine = 1, Square = 2, Saw = 3, Triangle = 4, Pulse = 5, Noise = 6 }`＋ループ有無＋ ADSR（attack / decay / sustain / release）＋ エコー送り量
- エコー: ディレイ（0〜240 ms、16 ms 刻み）・フィードバック・出力音量。ソング単位の設定
- 実機の BRR 圧縮・ガウス補間は M1 では省略（線形補間）。NSF/VGM 相当の実機互換は M3 の課題

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
- **ピアノロール**（中央）: 縦が音程、横が tick。ノートの追加（クリック）・移動（ドラッグ）・長さ変更（右端ドラッグ）・削除（Delete）。グリッドは 16 分音符。再生位置カーソル
- **音色エディタ**（右）: 選択トラックのチャンネル種別で使える音色パラメータ。マクロは数値列の編集
- **トランスポート**（下）: 再生 / 停止 / ループ切替 / テンポ / 位置表示
- **オーディオ出力**: `IAudioOutput`（Start / Stop / コールバックでバッファ要求）を抽象化し、実装は SDL3-CS 3.4.16 ＋ SDL3-CS.Native 3.4.2。`SongRenderer.Render(Span<float>)` をコールバックから呼ぶ。編集は再生中でも反映される（Song の変更を次バッファから拾う。ロックは最小限）
- **ファイル監視**: AI が CLI / MCP で保存したら自動で再読み込み（colors-viewer と同じ）。編集中の競合は「外部変更を検知したら再読み込みの確認」で対処
- `Awake` 相当の暗黙初期化を持たず、`MainWindowPresenter` を `Program` から明示的に組み立てる

### M1-C の編集・再生境界（2026-09-07）

- DAW は `DawDocument` が一時作業ファイル上の `EditSession` を所有する。Core の自動保存・検証・参照交換・`SongHistory` をそのまま利用し、正本への書き込みは Ctrl+S で正本用 `EditSession.Save()` を呼ぶ。Core / CLI / MCP の自動保存仕様は変えない。
- ノートドラッグは有効な途中位置を作業セッションへ公開する。押下時に履歴を控え、終了時にドラッグ全体を一操作へまとめる。不正な重なりは確定せず、最後に成功した位置を維持する。Alt は整数 tick 単位、通常は 12 tick 単位。
- 音色の既定選択は DAW セッション内でトラック別に保持する。選択ノートがあればその音色を表示・割り当てる。音色パラメータと名前は「音色を適用」で一履歴に確定し、次のバッファから反映する。マクロ空欄または `null` は解除。
- SDL audio stream の get コールバックが 44100 Hz・512 frames の float stereo バッファを要求する。合成はコールバック内の `SongRenderer.Render` のみ。表示用位置・完走・警告は値を公開し、UI は 33 ms 間隔で読む。
- Space／再生ボタンは停止位置からの再開と停止を切り替える。停止ボタンは先頭へ戻す。ループ切替・テンポ・長さ変更は停止中に設定を取り直し、再生中だった場合は先頭から再開する。テンポ・長さを含みうる undo/redo も停止・Reset の境界で行う。
- ファイル監視は保存内容の正規化 JSON と保存基準を比較する。自己保存の遅延通知を時間窓では判定しない。未保存時の外部更新はステータス表示で R の確認を待ち、保存直前にも外部更新を確認する。
- ピアノロールは可視範囲だけカスタム描画し、鍵盤・ルーラーをスクロールと同期する。ブラシ・Pen・音名・可視ルーラーラベルを描画前に用意する。音色一覧と音色参照が変わらない限り入力部品を作り直さない。

## テスト方針

- `tests/Arpeggio.Core.Tests`（xunit）。合成器はチップごとに**周波数・デューティ・音量の期待値を数値で検証**する（ゼロクロス数から周波数を推定、矩形波の High 比率からデューティを検証、RMS から音量を検証）
- `SongSerializer` はラウンドトリップ、`SongValidator` は不正ケースを網羅
- `SongRenderer` は短いソングを一括レンダリングして長さ・無音でないこと・`RenderReport` の警告を検証
- ホットパスのアロケーションは `GC.GetAllocatedBytesForCurrentThread` の差分で 0 を検証する

## 未決事項

- OGG 書き出しのエンコーダ選定（純 C# の Vorbis エンコーダの有無）— M2
- SNES の実機互換（BRR・ガウス補間）をどこまで追うか — M3 で NSF/VGM と一緒に決める
- DAW での MIDI キーボード入力 — 要望が出たら

### M1-B の入力・出力境界（2026-09-07 実装時確定）

- バッチは `kind` ごとに必須値を検証するモデルとし、任意値を nullable で表す。UpdateNote は指定した項目だけを更新し、`tick` が検索位置、`toTick` が移動先。effects の省略は保持、空配列は解除。詳細な JSON 項目は README のバッチ表を参照する。
- バッチの track は各操作を優先し、省略時だけ CLI `--track` / MCP `track` を使う。どちらも無ければノート・トラック操作は引数エラー。空バッチは拒否する。
- 操作列全体を先に解析し、候補ソング上で各操作後に制約を検証する。最後に既存 `EditSession.Change` で一度だけ保存・履歴確定する。失敗時は公開状態を変更しない。
- `show` は `[fromTick,toTick)` を fromTick 起点の 12 tick 行で示す。行内の非グリッド開始は `@実tick`、複数開始および継続音との併存は `;` で併記し、短い音も省略しない。JSON は範囲に交差するノートの元の tick・長さとテキストを返す。
- CLI の音色追加は既存最大 ID + 1。set は指定値だけを更新し、マクロ文字列 `null` は解除。kind 変更は ID・name と意味が同じ共通項目を保持し、専用項目は新 kind 既定値。MCP とバッチの音色更新は保存形式の JSON 全体で置き換える。
- CLI の履歴は colors と同じ `<path>.history/state.json`（current/undo/redo）を使う。外部更新との current 不一致では履歴を復元しない。履歴保存の I/O 失敗時は曲を操作前のバイト列へ戻す。曲と履歴は別ファイルの置換であり、プロセス強制終了まで含む二ファイルの永続トランザクションは対象外。
- MCP は `EditSession` を DI 共有し、同じセッションのツール実行を直列化する。戻り値は文字列、エラーは error/exitCode JSON。show の既定と chip_reference は CLI と同じテキスト。`UseStructuredContent` は指定しない。
