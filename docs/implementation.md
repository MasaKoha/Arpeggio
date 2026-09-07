# M1-A 実装記録（2026-09-07）

## 実装範囲

`Arpeggio.Core` のデータモデル、3 チップの合成、シーケンス、ストリーミング／一括レンダリング、WAV、履歴、編集セッションと対応テストを初回実装した。CLI / MCP / DAW、プロジェクト設定、パッケージ、ソリューションは変更していない。git 操作も実施していない。

設計変更・未指定だった境界条件は、先に `docs/design.md` の「M1-A の境界・単位」へ追記した。

## 主要な実装判断

- JSON は System.Text.Json の属性による多態化を維持する。読み込み時だけ `kind` をメタデータの先頭へ移し、保存時は音色の `id`・`name`・`kind` 順へ戻す。全プロパティは順序を明示し、UTF-8 BOM なし・2スペースインデント・マクロ null を維持する。読み込み直後／保存直前に Validator を必ず通す。
- 編集は独立した候補ソングを検証・保存してから公開する。保存失敗時には編集中ソング・履歴を変更しない。未変更の Song / Track / Note / Instrument とリストは参照を維持し、変更されたノート列・音色列だけを公開する。公開済み列への直接追加・削除は再生中に行わない。
- シーケンサーは Render ごとにノート列参照を取り直す。ノート検索は二分探索、イベントは値型で、Delay・整数サンプル境界・周回開始の再発音を扱う。
- 合成器は加算型のモノラル API。共通のマクロ・効果・位相管理を基底と小さな合成状態へ分離し、各チップは波形とエンベロープを担当する。SNES の全内蔵波形は構築時に生成し、NoteOn で確保しない。
- Renderer は生成時にチャンネル領域・警告領域・シーク作業領域を確保する。通常の Render で新しい音色・ノートへ遷移しても、シーケンス・合成・混合・警告記録は確保不要の経路を使う。
- Render の戻り値・PositionSamples・Seek はステレオのフレーム数。RenderAll は残りの PCM を返す。Seek は先頭から同じ Render 経路で捨て読みし、位相・マクロ・エコーを復元する。
- 音色更新時は次の Render で再発音し、その時点から終端までの残り時間をスライド期間に使う。テンポ・長さ・チップ・エコー設定の変更は Reset または再生成で反映する。
- NES DPCM は予約チャンネルとして無音。ノイズは周期選択であり平均律の音程を持たない。三角波は固定音量。SNES の圧縮・実機補間やアンチエイリアシングは M1 の対象外。これらは設計書の範囲に従う。
- `new RenderSettings()` は標準値を返す明示コンストラクターを持つ。`default(RenderSettings)` はゼロ初期化なのでレンダラーが不正設定として拒否する。record struct とそのコンストラクターは C# 9 制限の明示例外として扱う。

## 設計ツリーに追加した型と理由

| 型 | 理由 |
|---|---|
| Document/SongValidationException | 設計本文が要求する入力不正例外を、ファイル一覧にも実体化する |
| Document/SnesEchoSettings | ソング単位のエコー設定を名前付きのモデルで保持する |
| Document/InstrumentValidator | チップ固有音色・マクロの検証をソングの構造検証から分離する |
| Session/SongSnapshotPublisher | 未変更参照を維持して編集結果を公開し、無関係な発音のリセットを防ぐ |
| Synthesis/ChannelSynthesizer | IChannelSynthesizer の共通発音寿命・位相・効果連携を、実際の各チャンネルの基底として保持する |
| Synthesis/VoiceModulation | 音量・アルペジオ・ピッチ・デューティとノート効果の進行を一箇所で扱う |
| Synthesis/NoiseOscillator | NES / GB に共通する LFSR のクロック進行を、チップ固有の周期選択から分離する |
| Render/RenderMixer | Renderer の時刻管理から、定位・チップミキサー・エコー送りの配線を分離する |
| Render/RenderWarningKind / RenderWarning | 警告内容を文字列生成なしの値として記録し、呼び出し側が箇所と補正値を取得できるようにする |
| Tests/Analysis/SignalAnalysis | 実装の計算式を呼ばず、ゼロクロス・High 比率・RMS を計算する |
| Tests/Analysis/SynthSamples | 独立した合成器へ 60 Hz の進行を与える |
| Tests/Analysis/TestSongFactory | 全チャンネルに実際の発音・停止・マクロ・エコー遷移を含む検証ソングを用意する |
| Tests/Analysis/WavReader | 書き込み実装から独立して RIFF・fmt・data を検証し、16 bit PCM を読み戻す |

## 受け入れ条件とテスト

| 条件 | 対応テスト |
|---|---|
| JSON のバイト単位往復・キー順・整数 enum | Document/SongSerializerTests |
| tick・音量・音色参照・構成・version・重複の拒否 | Document/SongValidatorTests |
| 各周期波形の周波数誤差 2 %・矩形デューティ誤差 3 %・RMS | Synthesis/Nes、GameBoy、Snes の各合成器テスト |
| 三角波の音量固定・DPCM 無音・ノイズの決定性とモード差 | 上記のチャンネル別テスト |
| 全チップの 1 秒間の Render がウォームアップ後 0 byte | Render/SongRendererAllocationTests |
| 4 小節・150 BPM・ループ・末尾余白・非無音 | Render/SongRendererTests |
| 分割バッファの一括一致・Seek/Reset・次バッファ編集反映 | Render/SongRendererTests |
| 16 bit / ステレオ / 指定レートの WAV 再読込 | Render/WavWriterTests + Analysis/WavReader |
| 音域・三角波音量の警告件数と重複除去 | Render/SongRendererTests |
| 最大 50 件・古い順破棄・redo 破棄 | History/SongHistoryTests |
| 基本編集・参照中音色削除拒否・保存失敗時の状態維持・参照維持 | Session/EditSessionTests |
| マクロ、スライド、GB エンベロープ、SNES エコー、周期境界 | Synthesis・Sequencing の個別テスト |
| 極端なピッチ値での整数オーバーフロー回帰 | Synthesis/PitchTableTests と NES / GB Noise テスト |

## 実施済みの静的確認

- 設計書全文・リポジトリ指示・C# 規約・colors の JSON／履歴／セッションを参照。
- 自前型の定義と namespace、BCL／xunit の参照定義を検索して using を照合。
- 全 C# ファイルの文字列・コメントを除いた括弧対応、public / protected summary を確認。
- ファイルスコープ namespace、禁止した新構文、省略名、状態を持つ static、Unity 固有 API を検索。編集 API の Update と内部の Start は Unity lifecycle ではない。
- Render / AdvanceFrame / マクロの呼び出し先まで確保経路を読み取り確認。数値指標と GC 差分の実測は未実施。

## 未完了

コードとテストの予定範囲は実装済み。以下の実行確認は依頼者が行う。Codex はビルド・コンパイル・テストを一切実行していないため、警告ゼロ・全件成功・音響指標・GC 0 byte の実測結果は未確定。

```sh
dotnet build src/Arpeggio.Core/Arpeggio.Core.csproj
dotnet build tests/Arpeggio.Core.Tests/Arpeggio.Core.Tests.csproj
dotnet test tests/Arpeggio.Core.Tests
```

## 変更ファイル一覧

更新: `docs/design.md`。新規: 本ファイルと以下の C# ファイル。

### Core（66 ファイル）

```text
Document/ChannelKind.cs
Document/ChipKind.cs
Document/ChipLayout.cs
Document/InstrumentValidator.cs
Document/Note.cs
Document/NoteEffect.cs
Document/NoteEffectKind.cs
Document/SnesEchoSettings.cs
Document/Song.cs
Document/SongFactory.cs
Document/SongSerializer.cs
Document/SongValidationException.cs
Document/SongValidator.cs
Document/Track.cs
History/SongHistory.cs
Instruments/AdsrEnvelope.cs
Instruments/DutyCycle.cs
Instruments/GbNoiseInstrument.cs
Instruments/GbPulseInstrument.cs
Instruments/GbWaveInstrument.cs
Instruments/Instrument.cs
Instruments/InstrumentKind.cs
Instruments/Macro.cs
Instruments/NesDpcmInstrument.cs
Instruments/NesNoiseInstrument.cs
Instruments/NesPulseInstrument.cs
Instruments/NesTriangleInstrument.cs
Instruments/NoiseMode.cs
Instruments/SnesSampleInstrument.cs
Instruments/SnesWaveformKind.cs
Render/RenderMixer.cs
Render/RenderReport.cs
Render/RenderSettings.cs
Render/RenderWarning.cs
Render/RenderWarningKind.cs
Render/SongRenderer.cs
Render/WavWriter.cs
Sequencing/FrameClock.cs
Sequencing/NoteEvent.cs
Sequencing/TickClock.cs
Sequencing/TrackSequencer.cs
Session/EditSession.cs
Session/InstrumentEditor.cs
Session/NoteEditor.cs
Session/SongSnapshotPublisher.cs
Synthesis/ChannelSynthesizer.cs
Synthesis/ChannelSynthesizerFactory.cs
Synthesis/GameBoy/GbMixer.cs
Synthesis/GameBoy/GbNoiseSynthesizer.cs
Synthesis/GameBoy/GbPulseSynthesizer.cs
Synthesis/GameBoy/GbWaveSynthesizer.cs
Synthesis/IChannelSynthesizer.cs
Synthesis/MacroRunner.cs
Synthesis/Nes/NesDpcmSynthesizer.cs
Synthesis/Nes/NesMixer.cs
Synthesis/Nes/NesNoiseSynthesizer.cs
Synthesis/Nes/NesPulseSynthesizer.cs
Synthesis/Nes/NesTriangleSynthesizer.cs
Synthesis/NoiseOscillator.cs
Synthesis/PitchTable.cs
Synthesis/Snes/SnesEcho.cs
Synthesis/Snes/SnesMixer.cs
Synthesis/Snes/SnesVoiceSynthesizer.cs
Synthesis/Snes/SnesWaveformBuilder.cs
Synthesis/VoiceModulation.cs
```

### Tests（30 ファイル）

```text
Analysis/SignalAnalysis.cs
Analysis/SynthSamples.cs
Analysis/TestSongFactory.cs
Analysis/WavReader.cs
Document/SongSerializerTests.cs
Document/SongValidatorTests.cs
History/SongHistoryTests.cs
Render/SongRendererAllocationTests.cs
Render/SongRendererTests.cs
Render/WavWriterTests.cs
Sequencing/FrameClockTests.cs
Sequencing/TickClockTests.cs
Sequencing/TrackSequencerTests.cs
Session/EditSessionTests.cs
Synthesis/GameBoy/GbEnvelopeTests.cs
Synthesis/GameBoy/GbNoiseSynthesizerTests.cs
Synthesis/GameBoy/GbPulseSynthesizerTests.cs
Synthesis/GameBoy/GbWaveSynthesizerTests.cs
Synthesis/MacroRunnerTests.cs
Synthesis/Nes/NesDpcmSynthesizerTests.cs
Synthesis/Nes/NesMixerTests.cs
Synthesis/Nes/NesModulationTests.cs
Synthesis/Nes/NesNoiseSynthesizerTests.cs
Synthesis/Nes/NesPulseSynthesizerTests.cs
Synthesis/Nes/NesTriangleSynthesizerTests.cs
Synthesis/PitchTableTests.cs
Synthesis/Snes/SnesEchoTests.cs
Synthesis/Snes/SnesVoiceSynthesizerTests.cs
```


# M1-B 実装記録（2026-09-07）

## 実装範囲

CLI の全コマンドと stdio MCP の全 16 ツールを実装した。colors の `CliExecution` / `CommandFactory` / コマンド群、MCP のツール属性・DI 共有・stdio ホストに揃えた。MCP の例外は今回の指定に従い `error` / `exitCode` の JSON 文字列へ変換する。

合成器・シーケンサ・レンダラー・WavWriter は変更していない。プロジェクト設定の変更はテスト csproj の CLI / MCP ProjectReference 追加のみ。DAW は終了コード 0 の最小エントリーポイントだけを追加した。git 操作は実施していない。

## M1-B の実装判断

- Core の既存公開 API は維持した。`EditSession.New` に曲名を受け取るオーバーロードを追加し、CLI/MCP のタイトル指定を一回の保存にした。既存 4 引数呼び出しの挙動は同じ。
- `SongHistory` にスナップショットの取得・全件検証後の復元を追加した。CLI の呼び出し間履歴にだけ必要な薄い追加で、既存の Record / Undo / Redo は変更していない。
- バッチは `BatchOperation` と kind enum、JSON 入力、候補ソングへの適用に分けた。既存 `EditSession.Change` をアセンブリ内部から呼び、一度の保存・履歴記録・参照公開へ接続する。各操作後に検証し、途中の不正状態を後続の操作で打ち消すバッチは拒否する。
- 音色 JSON は discriminator の位置を正規化し、保存ファイルと同じ id・name・kind 順や整数 enum を受け付ける。未知のプロパティは引数エラー。CLI の kind 別オプションは生成済み音色の JSON プロパティに対応付け、未指定値保持と不適合オプション拒否を両立する。
- CLI の履歴は colors と同じ current / undo / redo の側車ファイル。current 不一致は外部更新として履歴を失効させる。側車保存の I/O 失敗では曲を元のバイト列に復元する。強制終了や復元先まで書けなくなる複合 I/O 障害を含む二ファイルの永続トランザクションは保証しない。
- MCP は共有 `EditSession` をロックし、ツール間の競合を避ける。通常戻り値は JSON 文字列、show の既定と chip_reference は生テキスト。UseStructuredContent は付けていない。
- show の非グリッド音・複数開始を落とさない表記、バッチ項目、音色更新の差は設計書の「M1-B の入力・出力境界」と README に記録した。
- WAV の警告は CLI の通常出力で stderr、JSON と MCP では warnings / droppedWarningCount。警告だけでは失敗にしない。

## M1-B の確認対応

| 受け入れ条件 | 対応テスト |
|---|---|
| 3 チップ新規作成、info/show、全 note 操作、終了コード | Cli/CliExecutionTests |
| 4 音の NES ソングから非無音の 16 bit ステレオ WAV | Cli/CliExecutionTests.FourNotesExportNonSilentStereoWav |
| WAV 警告の stderr / JSON 振り分け、成功コード維持 | Cli/CliExecutionTests.ExportWarningsDoNotFail |
| CLI 呼び出し間 undo/redo、外部更新失効、バッチ失敗で側車不変 | Cli/CliExecutionTests |
| 履歴側車の保存失敗で曲を復元 | Cli/CliExecutionTests.HistoryWriteFailureRestoresSong |
| 全音色 kind・パラメータ・部分更新・マクロ解除 | Cli/InstrumentCommandsTests、Cli/MacroOptionTests |
| 全 13 バッチ kind、一履歴、必須値、JSON 形、保存失敗・操作失敗の巻き戻し | Session/BatchOperationTests |
| MIDI 全範囲往復、異名同音、無効名 | Document/NoteNameTests |
| 12 tick 表示、効果・継続・無音・区間・非グリッド音 | Document/SongTextRendererTests |
| チップ説明の構成・kind・単位・無効チップ | Document/ChipReferenceTests |
| MCP 全16ツール属性、DI共有相当、操作・音色JSON・バッチ・WAV警告・エラー文字列 | Mcp/ArpeggioToolsTests |

テストは CLI を `CliExecution.Run`、MCP を `ArpeggioTools` から直接呼び、プロセス起動を使わない。CLI の Console 差し替えは同じ xunit collection にまとめて競合を防ぐ。

## M1-B の静的確認

- colors の CLI / MCP / 永続履歴 / テスト / README を読み、System.CommandLine 2.0.11 と ModelContextProtocol 2.2.0 の API をローカル NuGet の参照 XML で照合した。
- 新規参照する Core の型・namespace と .NET の JSON API を定義・参照 XML から照合した。
- 対象 C# 33 ファイルの文字列・コメントを除いた括弧対応、public summary、ブロック namespace、明示ローカル型を静的スクリプトで確認した。
- MCP ツール名 16 件と string 戻り値、UseStructuredContent の未指定をコードとテストで確認した。

## M1-B 未完了

実装予定範囲は追加済み。以下の実行確認は依頼者が行う。Codex はコンパイル・ビルド・テスト・MCP ホスト起動を実行していないため、警告ゼロ、テスト全件成功、WAV の非無音、stdio 接続の実測は未確認。

```sh
dotnet build Arpeggio.slnx
dotnet test tests/Arpeggio.Core.Tests
```

ビルド済み `arpeggio-mcp` を README の例で登録し、new_song → add_note → show_song → export_wav の stdio 接続確認も必要。

## M1-B 変更ファイル一覧

更新:

```text
docs/design.md
docs/implementation.md
README.md
src/Arpeggio.Core/Session/EditSession.cs
src/Arpeggio.Core/History/SongHistory.cs
tests/Arpeggio.Core.Tests/Arpeggio.Core.Tests.csproj
```

新規:

```text
src/Arpeggio.Cli/Program.cs
src/Arpeggio.Cli/CliExecution.cs
src/Arpeggio.Cli/CliHistoryStore.cs
src/Arpeggio.Cli/CommandFactory.cs
src/Arpeggio.Cli/SongCommands.cs
src/Arpeggio.Cli/NoteCommands.cs
src/Arpeggio.Cli/NoteTargetOptions.cs
src/Arpeggio.Cli/InstrumentCommands.cs
src/Arpeggio.Cli/InstrumentOptions.cs
src/Arpeggio.Cli/MacroOption.cs
src/Arpeggio.Cli/BatchCommands.cs
src/Arpeggio.Mcp/Program.cs
src/Arpeggio.Mcp/ArpeggioTools.cs
src/Arpeggio.Daw/Program.cs
src/Arpeggio.Core/Document/NoteName.cs
src/Arpeggio.Core/Document/SongTextRenderer.cs
src/Arpeggio.Core/Document/ChipReference.cs
src/Arpeggio.Core/Session/InstrumentJson.cs
src/Arpeggio.Core/Session/SessionOutput.cs
src/Arpeggio.Core/Session/BatchOperation.cs
src/Arpeggio.Core/Session/BatchOperationKind.cs
src/Arpeggio.Core/Session/BatchOperationJson.cs
src/Arpeggio.Core/Session/BatchOperationApplier.cs
tests/Arpeggio.Core.Tests/Cli/CliExecutionTests.cs
tests/Arpeggio.Core.Tests/Cli/InstrumentCommandsTests.cs
tests/Arpeggio.Core.Tests/Cli/MacroOptionTests.cs
tests/Arpeggio.Core.Tests/Document/NoteNameTests.cs
tests/Arpeggio.Core.Tests/Document/SongTextRendererTests.cs
tests/Arpeggio.Core.Tests/Document/ChipReferenceTests.cs
tests/Arpeggio.Core.Tests/Session/BatchOperationTests.cs
tests/Arpeggio.Core.Tests/Mcp/ArpeggioToolsTests.cs
```
