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


# M1-C 実装記録（2026-09-07）

## 実装範囲

Avalonia 12.1.2 の DAW、カスタム描画ピアノロール、トラック選択・ミュート・ゴースト、全 8 kind の音色編集、トランスポート、SDL3 リアルタイム再生、明示保存・履歴、外部ファイル監視を追加した。Program が依存を組み立て、Views は入力通知と表示に限定する。Core / CLI / MCP は変更していない。git 操作、ビルド、テスト実行、アプリ起動は行っていない。

## M1-C の実装判断

- **明示保存と Core の自動保存の両立**: Core の公開 API を変更できないため、DAW 内の `DawDocument` が一時作業ファイルの `EditSession` を所有する。編集・検証・自動保存・参照交換を既存 API に任せ、Ctrl+S でのみ正本用セッションに現在の内容を渡して `Save()` する。一時ファイルは Dispose で削除する。
- **ドラッグ履歴**: ドラッグ途中でも `NoteEditor.Update` で公開し、押下前の履歴を保持して終了時に一操作へまとめる。無効位置への移動は Core の検証で拒否し、最後の有効位置を維持する。作業ファイルへの I/O と JSON コピーは UI の編集操作に発生するため、大曲でのドラッグ性能は実測対象。
- **音声スレッド**: `IAudioOutput` と `PlaybackEngine` を明示的に結線。SDL3 stream の get コールバックに固定 pin バッファを渡す。停止時は get コールバックを解除して進行中呼び出しの完了を待ち、stream を破棄してから pin を解放する。通常の合成は `Render` だけで、UI は `Render` / `Seek` / `RenderAll` を呼ばない。
- **表示状態の公開**: 位置は Interlocked、警告は事前確保配列へ追記して件数を Volatile 公開する。UI はライブの RenderReport リストを列挙しない。ネイティブコールバック内の例外は境界で捕捉し、Poll で停止とステータス表示へ渡す。
- **ループ**: Renderer の既存ループ機構に `int.MaxValue` 回を指定する。表示位置はソングのループ区間へ折り返す。ループ切替は先頭から再開する。テンポ・長さ・undo/redo は出力停止後に Reset する。
- **音色**: チャンネルと互換のある音色だけを一覧へ出す。実際の音色 JSON に存在する項目から専用入力を作り、ADSR は項目別、GB 波形は 32 整数列で編集する。名前とパラメータは適用ボタンで一履歴にまとめる。マクロパーサは CLI 変更禁止を優先して DAW 内に同形式の小さな実装を置いた。
- **外部変更**: colors の watcher の変更集約・置換検出・購読解除を踏襲した。正規 JSON の内容比較により自己保存・重複通知を除外する。dirty なら R の確認待ちにし、Ctrl+S 前にも再検査して通知遅延による上書きを防ぐ。OS をまたぐ同時書き込み全体のトランザクションは提供しない。
- **描画**: ノート別コントロールは生成しない。可視範囲のグリッドとノートだけを描き、Pen/Brush と文字を再利用する。停止中は位置が変わらなければ再描画しない。音色パネルも参照が変わらない限り入力内容を維持する。

SDL の確認元: [公式 binding Audio PInvoke](https://github.com/edwardgushchin/SDL3-CS/blob/main/SDL3-CS/SDL/Audio/audio/PInvoke.cs)、[get コールバック解除の同期保証](https://wiki.libsdl.org/SDL3/SDL_SetAudioStreamGetCallback)、[stream 破棄](https://wiki.libsdl.org/SDL3/SDL_DestroyAudioStream)。使用 API は SDL 3.2.0 からの範囲に限定した。指定 NuGet の実体はローカルキャッシュになく、3.4.16 のパッケージ実体との完全照合は未確認。

## M1-C のテストコード

`tests/Arpeggio.Core.Tests/Daw/` に Avalonia を起動しない 23 ケースを追加した。`IMainWindowView` と `IAudioOutput` はテスト用実装へ置換する。

| 対象 | 検証内容 |
|---|---|
| MainWindowPresenter | ノート追加・移動・削除・undo/redo、Ctrl+S まで正本不変、自己保存の遅延通知、clean 外部更新の再読込、dirty 外部更新の確認待ち、通知前の保存保護、不正 JSON と重複ノートの状態保持 |
| PianoRollPresenter | 移動途中の参照交換、一ドラッグ一履歴、無移動で redo 維持、右端 resize と次ノート長、Alt スナップ解除、ゴースト非対象、音量上下限 |
| TransportPresenter | UI Poll では合成しない、再生・停止・先頭復帰、次バッファへのノート編集反映、テンポ変更と undo の停止・再開、完走、ループ、位置表記の境界 |

## M1-C の静的確認

- 設計書全文・既存実装記録・C# 規約・Colors.Viewer・avalon の記録を参照。
- Core の編集・履歴・音色・レンダラー型と namespace、Avalonia 12.1.2 のローカル参照 XML、SDL 公式 binding を照合。
- 新規 C# の括弧対応・public/protected summary・ブロック namespace・省略名・明示型を確認。AXAML・csproj・manifest の XML 形式を検証。
- イベント解除、音声停止前のリソース寿命、初期化失敗時の解放、View のテキスト／ComboBox 入力とグローバルショートカットの干渉を確認。

## M1-C 未完了

- **テストプロジェクト参照が確認待ち**: 今回の指示が csproj 変更を DAW のみに制限しているため、`tests/Arpeggio.Core.Tests/Arpeggio.Core.Tests.csproj` は変更していない。追加済みテストをコンパイルするには、同ファイルの ProjectReference 群に次の 1 行が必要。ユーザーへ例外許可を問い合わせ済み。参照追加前はソリューションビルドを完了できない。

  ```xml
  <ProjectReference Include="..\..\src\Arpeggio.Daw\Arpeggio.Daw.csproj" />
  ```

- **実行確認は依頼者側**: `dotnet build Arpeggio.slnx` の警告ゼロ、`dotnet test tests/Arpeggio.Core.Tests` の成功、`arpeggio-daw <path.arpeggio.json>` の起動、macOS/Windows のネイティブ SDL 読み込みと音声出力、数百ノートのドラッグ／横スクロール／ズーム／鍵盤同期を未確認。
- **音声の実測**: 次バッファでの編集反映・停止時の切断・再生カーソルと実音のバッファ遅延・ループ継ぎ目・全チップ音色・デバイスエラーを実機で確認する。表示位置は供給済みフレーム位置であり、スピーカー出力より SDL/OS のバッファ分だけ先行する。
- **固定パッケージの照合**: NuGet restore とコンパイルで SDL3-CS 3.4.16 / SDL3-CS.Native 3.4.2 の組み合わせを確認する。ネイティブ代替は採用していない。

## M1-C 変更ファイル一覧

更新: `docs/design.md`、`docs/implementation.md`、DAW の Program と csproj。以下は DAW の追加・更新ファイルおよび新規テストの全一覧。

```text
src/Arpeggio.Daw/App.axaml
src/Arpeggio.Daw/App.axaml.cs
src/Arpeggio.Daw/Arpeggio.Daw.csproj
src/Arpeggio.Daw/Audio/AudioCallback.cs
src/Arpeggio.Daw/Audio/IAudioOutput.cs
src/Arpeggio.Daw/Audio/PlaybackEngine.cs
src/Arpeggio.Daw/Audio/SdlAudioOutput.cs
src/Arpeggio.Daw/Editing/DawDocument.cs
src/Arpeggio.Daw/Presenters/IMainWindowView.cs
src/Arpeggio.Daw/Presenters/InstrumentMacroText.cs
src/Arpeggio.Daw/Presenters/InstrumentPanelPresenter.cs
src/Arpeggio.Daw/Presenters/InstrumentParameter.cs
src/Arpeggio.Daw/Presenters/InstrumentParameterEditor.cs
src/Arpeggio.Daw/Presenters/MainWindowPresenter.cs
src/Arpeggio.Daw/Presenters/PianoRollDragMode.cs
src/Arpeggio.Daw/Presenters/PianoRollPresenter.cs
src/Arpeggio.Daw/Presenters/TransportPresenter.cs
src/Arpeggio.Daw/Program.cs
src/Arpeggio.Daw/Views/InstrumentPanelView.axaml
src/Arpeggio.Daw/Views/InstrumentPanelView.axaml.cs
src/Arpeggio.Daw/Views/KeyboardStripControl.cs
src/Arpeggio.Daw/Views/MainWindow.axaml
src/Arpeggio.Daw/Views/MainWindow.axaml.cs
src/Arpeggio.Daw/Views/PianoRollControl.cs
src/Arpeggio.Daw/Views/TimeRulerControl.cs
src/Arpeggio.Daw/Views/TrackListView.axaml
src/Arpeggio.Daw/Views/TrackListView.axaml.cs
src/Arpeggio.Daw/Views/TransportView.axaml
src/Arpeggio.Daw/Views/TransportView.axaml.cs
src/Arpeggio.Daw/Watch/SongFileWatcher.cs
src/Arpeggio.Daw/app.manifest
tests/Arpeggio.Core.Tests/Daw/DawPresenterFixture.cs
tests/Arpeggio.Core.Tests/Daw/FakeAudioOutput.cs
tests/Arpeggio.Core.Tests/Daw/FakeMainWindowView.cs
tests/Arpeggio.Core.Tests/Daw/MainWindowPresenterTests.cs
tests/Arpeggio.Core.Tests/Daw/PianoRollPresenterTests.cs
tests/Arpeggio.Core.Tests/Daw/TransportPresenterTests.cs
```


# M2-A 実装記録（2026-09-07）

## 実装範囲

AI 向け音声解析と八種類の効果音プリセットを Core / CLI / MCP に追加した。合成・シーケンス・SongRenderer・既存公開 API・ソング JSON / version・DAW・csproj / slnx は変更していない。Core は BCL のみで、NuGet は追加していない。git 操作、ビルド、コンパイル、テスト実行、アプリ起動は行っていない。

## M2-A の実装判断

- **解析の単位**: ステレオフレームで時刻を計算し、全体と各窓の RMS は左右全サンプルから計算する。表示窓は重複なし・端数窓を含み、無音割合は窓数の比率。左右差は左−右。-160 dBFS を無音の有限表現にして、既存の SessionOutput.Serialize でそのまま JSON にできる。
- **周波数解析**: 基数 2 の自前 FFT、既定 2048 点。表示窓全体を連続ブロックに分割し、左右個別のブロック平均を除去、実長 Hann とゼロ詰めを適用する。パワーを合算して逆相相殺を防ぎ、Hann 二乗和と実フレーム数で重み付けする。DC を除外したパワーから支配的周波数・重心・帯域比率を求める。音程は対数パワーの放物線補間後に A4=440 Hz で最寄り音名へ変換する。基音推定ではなく、支配的な成分のラベルである。
- **作業配列と責務**: FastFourierTransform は in-place の複素順変換。SpectrumAnalysis が一回の解析中の配列再利用と帯域蓄積、SignalStatistics が元波形の音量統計を担当する。AnalysisBandEnergy は三帯域の比率を名前付きで公開する。窓ごとに作業配列を生成しない。
- **警告**: クリップ・小音量・先頭無音・末尾無音・左右差を分類する。先頭／末尾は表示窓と独立した 50 ms 単位。AudioAnalysisSource がソングを複製してソロ化し、44100 Hz・余白 0 秒で既存 Renderer を呼ぶ。RenderWarning と保持上限超過数も同じレポートに保存し、テキストは同じ警告節へ表示する。
- **WAV**: 必須範囲の PCM 16 bit モノラル／ステレオ。未知チャンク、奇数長パディング、data が fmt より前にある配置を扱う。形式・宣言長・バイトレート・フレーム境界・チャンク重複を検証する。正側 32767・負側 32768 で正規化し、書き出された正負の飽和端点を両方クリップとして解析できる。Stream は呼び出し側所有、読み取りとシークが必要。8 / 24 / 32 bit・float・WAVE_FORMAT_EXTENSIBLE は対象外として明示エラー。テスト用 WavReader は Core 呼び出しと既存 short 配列への変換だけにした。
- **プリセット**: Jump / Coin / Hit / Explosion / PowerUp / Laser / Blip / Select を、ノートの PitchSlide / VolumeSlide / Arpeggio と既存音色で構成する。NES / GB は Pulse と Noise、SNES はループする内蔵 Pulse / Noise、リリース 0 秒。テンポ 150、ノート終端と lengthTicks を一致させる。Blip は個別要件の 30〜50 ms を優先して 5 tick（約 41.7 ms）。通常のプリセットは 100〜400 ms。
- **保存とカタログ**: SfxPresetDescription / SfxPresetCatalog が名前・説明・長さを共有する。正規名は powerup、power-up も受理する。SfxPresetFile は生成・検証済みの JSON を一時ファイル経由で新規移動し、存在確認後の競合でも上書きしない。CLI / MCP は保存成功後に既存 EditSession.Open で開く。CLI は既存の側車履歴を初期化する。
- **既存 export との境界**: 既定余白 0.5 秒は変更していない。SFX 本体長で比較する場合は CLI --tail 0 / MCP tail: 0 を指定する。analyze_song / analyze <song> は常に余白 0 秒であり、既定 export の余白を含めた解析とは長さが異なる。

## M2-A の利用例

```sh
arpeggio sfx list
arpeggio sfx new jump.arpeggio.json --preset jump --chip nes
arpeggio analyze jump.arpeggio.json --track 0 --loops 1 --window-ms 50
arpeggio export wav jump.arpeggio.json jump.wav --tail 0
arpeggio analyze wav jump.wav --json
```

MCP は `new_sfx(path, preset, chip?, title?)`、`sfx_presets()`、`analyze_song(track?, loops?, windowMs?)`、`analyze_wav(path, windowMs?)` を追加した。四ツールとも JSON 文字列を返し、既存の共有セッションロックとエラー分類を使う。テキスト表示は AnalysisTextRenderer、JSON は全窓を保持する。

## M2-A のテストコード

| 対象 | 追加した検証 |
|---|---|
| FFT / dBFS | インパルス、DC、複素入力の直接 DFT との比較、不正点数、振幅往復、有限の無音下限 |
| AudioAnalyzer | 440 Hz / A4、既知 RMS・ピーク、三サンプルレート、逆相、DC オフセット、表示窓全体の集計、三帯域、端数窓の重み、クリップ、左右差、独立した無音端点、空入力、不正設定、40 窓表示と全件 JSON |
| WAV 入力 | 手組み RIFF の mono / stereo、正負端点、未知奇数チャンク、data/fmt 順序、非対応形式、欠損・過大宣言長・端数フレーム、Stream 所有権 |
| ソング解析 | ソロでミュート解除、元 JSON 不変、ループ長、合成警告、I/O と形式エラーの分類 |
| SFX Core | 八プリセット × 三チップの生成・検証・JSON 往復・WAV 書き出し／再解析・長さ・非無音・非クリップ、スライド方向、Explosion の減衰、共有可変状態なし |
| CLI | 全 24 組み合わせの sfx new → export wav → analyze wav、analyze song、ファイル・側車履歴不変、ソロ・ループ・合成警告、一覧・既定チップ・上書き拒否・不正引数 |
| MCP | 全 24 組み合わせの new_sfx → export_wav → analyze_wav、analyze_song、セッション参照・ミュート・履歴不変、未オープン WAV 解析、一覧・上書き拒否・エラー JSON、20 ツールの公開契約 |

## M2-A の静的確認

- 設計書全文・M1-A/B/C の判断・C# 規約を参照し、既存 API と名前空間を照合した。
- System.CommandLine 2.0.11 と .NET 10 のローカル参照 XML で追加使用する公開型・メンバーを確認した。
- 変更した C# 34 ファイルの括弧対応・doc XML・public summary の隣接を確認した。禁止した省略名・ファイルスコープ namespace・Unity lifecycle API は追加していない。
- 音声合成やコマンド実行による確認は行っていない。下記テストの成功や数値は実測結果として主張しない。

## M2-A 未完了

実装上の残タスクはなし。以下は依頼者による実行確認が必要。

- ソリューションのコンパイルと警告ゼロ、既存テストを含む全件成功。
- Core / CLI / MCP の全 24 プリセットに対する音響数値・想定長・クリップなしの実測。
- CLI の analyze 親コマンドと wav サブコマンドの引数解釈、および MCP ホスト経由の四ツール呼び出し。
- プリセットの試聴による音作りの確認。周波数スライドは既存合成器の 60 Hz 更新粒度に従う。

## M2-A 変更ファイル一覧

更新: `docs/design.md`、`docs/implementation.md`。C# は以下の 34 ファイル（新規 29、更新 5）。

```text
src/Arpeggio.Cli/AnalysisCommands.cs
src/Arpeggio.Cli/CommandFactory.cs
src/Arpeggio.Cli/SfxCommands.cs
src/Arpeggio.Core/Analysis/AnalysisBandEnergy.cs
src/Arpeggio.Core/Analysis/AnalysisReport.cs
src/Arpeggio.Core/Analysis/AnalysisSettings.cs
src/Arpeggio.Core/Analysis/AnalysisTextRenderer.cs
src/Arpeggio.Core/Analysis/AnalysisWarning.cs
src/Arpeggio.Core/Analysis/AnalysisWarningKind.cs
src/Arpeggio.Core/Analysis/AnalysisWindow.cs
src/Arpeggio.Core/Analysis/AudioAnalysisSource.cs
src/Arpeggio.Core/Analysis/AudioAnalyzer.cs
src/Arpeggio.Core/Analysis/DecibelScale.cs
src/Arpeggio.Core/Analysis/FastFourierTransform.cs
src/Arpeggio.Core/Analysis/SignalStatistics.cs
src/Arpeggio.Core/Analysis/SpectrumAnalysis.cs
src/Arpeggio.Core/Render/WavReader.cs
src/Arpeggio.Core/Sfx/SfxPresetCatalog.cs
src/Arpeggio.Core/Sfx/SfxPresetDescription.cs
src/Arpeggio.Core/Sfx/SfxPresetFactory.cs
src/Arpeggio.Core/Sfx/SfxPresetFile.cs
src/Arpeggio.Core/Sfx/SfxPresetKind.cs
src/Arpeggio.Mcp/ArpeggioTools.cs
tests/Arpeggio.Core.Tests/Analysis/AudioAnalysisSourceTests.cs
tests/Arpeggio.Core.Tests/Analysis/AudioAnalyzerTests.cs
tests/Arpeggio.Core.Tests/Analysis/DecibelScaleTests.cs
tests/Arpeggio.Core.Tests/Analysis/FastFourierTransformTests.cs
tests/Arpeggio.Core.Tests/Analysis/WavReader.cs
tests/Arpeggio.Core.Tests/Analysis/WavReaderTests.cs
tests/Arpeggio.Core.Tests/Cli/AnalysisSfxCommandsTests.cs
tests/Arpeggio.Core.Tests/Mcp/AnalysisSfxToolsTests.cs
tests/Arpeggio.Core.Tests/Mcp/ArpeggioToolsTests.cs
tests/Arpeggio.Core.Tests/Render/WavWriterTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxPresetFactoryTests.cs
```


# M2-B 実装記録（2026-09-07）

## M2-B の実装範囲

SNES 音色への WAV 埋め込み、キャッシュ済みサンプルの再生、CLI / MCP からの取り込みと OGG 書き出し、Arpeggio.Codecs と対応テストを追加した。JSON version は 1、Core は BCL のみ。DAW は csproj の Codecs 参照追加だけを行った。git 操作・ビルド・コンパイル・テスト実行・アプリ起動は行っていない。

## M2-B の実装判断

- **保存とキャッシュ**: PCM は little-endian 16 bit mono の Base64。上限は 2 MiB、空・奇数バイト・不正 Base64 を拒否する。SampleData の setter でデコードし、音色自身が float 配列を所有するため、別のグローバルキャッシュや音色 ID の辞書は設けない。新しい JSON・音色置換・undo/redo はそれぞれ読み込み側でキャッシュが完成し、既存の参照交換で公開される。setter の形式不正はエラー情報として保持し、プロパティ順に依存しない InstrumentValidator で拒否する。SampleCount / SampleSummary は JsonIgnore、デコード配列は internal で保存しない。
- **WAV**: WavReader の既存公開 API は維持し、内部だけフレーム数の上限指定を追加した。これにより、ステレオ WAV のファイルサイズではなくモノラル化後の PCM サイズで、配列確保前に制限できる。左右を算術平均し、正側 32767・負側 32768 で PCM に四捨五入する。元レートは保持する。壊れた WAV・上限超過は ArgumentException、空サンプルや音色のメタデータ不正は SongValidationException、ファイル I/O は既存分類を使う。
- **編集の原子性**: WavSampleImporter は候補サンプルを検証後にだけ渡された音色へ適用する。InstrumentEditor.ImportWavSample は既存音色を複製して取り込み、既存 Update で一履歴として保存する。名前・波形・ADSR・パン・エコー・マクロは保持する。読み込み・検証・保存に失敗した場合、公開済み音色と履歴を変更しない。
- **再生**: SampleData があれば ConfigureInstrument は既存配列を参照するだけ。ChannelSynthesizer に位相増分計算の protected virtual フックを一つ追加し、SNES 埋め込み時のみ RootMidiNote 基準の量子化倍率 × 元レート / 出力レートとする。合成波形と他チップの計算は従来どおり。PitchTable に埋め込み用倍率・クランプを追加し、SongRenderer の音域警告も同じ root 基準へ合わせた。
- **ループ**: 最初はサンプル先頭から再生し、[LoopStart, LoopEnd) を繰り返す。LoopEnd=0 は末尾。終端直前の線形補間は開始サンプルへ接続し、大きい再生増分は剰余で折り返す。非ループでは最後の値を補間用に保持し、末尾到達時に停止する。ADSR の NoteOff / リリース・マクロ・効果・パン・エコーは既存経路を使う。
- **表示**: show / show --json / show_song の text と instrument list は sample 数・元 Hz を表示する。instrument list --json は他の音色プロパティを保持し、sampleData の代わりに sampleSummary を加える。これは表示用 JSON で、音色更新へ渡す完全な保存 JSON とは異なる。song_info など他の既存 API の保存形式出力は変更していない。
- **OGG**: NuGet OggVorbisEncoder 1.2.2 の依存を Codecs へ隔離した。quality は有限の -0.1〜1、既定 0.5 の VBR。左右各 1024 フレームのバッファを再利用し、範囲外振幅は ±1 に制限する。入力検証と VorbisInfo 初期化をファイル作成前に済ませ、ヘッダー・音声パケット・EOS・残ページを順に出力する。Stream は呼び出し側所有で閉じない。CLI は WAV / OGG のオプション構築を共通化し、警告出力を維持した。

## M2-B の API 照合

ローカル `~/.nuget/packages/oggvorbisencoder/1.2.2/` にパッケージと DLL があることを確認した。XML ドキュメントは同梱されていないため、netstandard2.0 DLL のメタデータと IL を monodis で読み、以下のシグネチャを確認した（コンパイル・エンコーダ実行はしていない）。

- `VorbisInfo.InitVariableBitRate(int channels, int sampleRate, float baseQuality)`
- `ProcessingState.Create(VorbisInfo)`、`WriteData(float[][], int length, int read_offset = 0)`、`WriteEndOfStream()`、`PacketOut(out OggPacket)`
- `OggStream(int serialNumber)`、`PacketIn(OggPacket)`、`PageOut(out OggPage, bool force)`、`Finished`
- `HeaderPacketBuilder.BuildInfoPacket` / `BuildCommentsPacket` / `BuildBooksPacket`、`Comments()`、`OggPage.Header` / `Body`（byte[]）

これらのエンコーダ型は IDisposable を実装していない。ストリームだけ所有権に従って破棄する。IL で品質の端点処理とテンプレート不適合時の InvalidOperationException も確認した。DLL 全体の逆アセンブルでは未配置の System.Memory による一部シグネチャの読み取り警告があったが、上記の使用 API はすべて読み取れた。手順は [公式エンコード例](https://github.com/SteveLillis/.NET-Ogg-Vorbis-Encoder/blob/master/OggVorbisEncoder.Example/Encoder.cs) とも照合した。

## M2-B のテストコード

| 対象 | 検証内容 |
|---|---|
| Instruments/SampleDataCodecTests | little-endian バイト列、全 65536 PCM 値の float 往復、正負端点、空・不正 Base64・奇数バイト・上限超過・非有限 float |
| Instruments/SnesSampleInstrumentTests | 追加項目なしの旧 JSON、version 1 往復、既定値、キャッシュ非保存、サンプル解除、レート・基準音・ループ範囲・2 MiB 境界 |
| Import/WavSampleImporterTests | mono の保持、stereo の平均と逆相、音色設定保持、mono/stereo の取り込み上限ちょうど、失敗時の元音色保持 |
| Synthesis/Snes/SnesEmbeddedSampleTests | ループ継ぎ目の補間、非ループ末尾、単一サンプル・複数周飛び越し、基準音・元レートと三出力レート、アルペジオ、ADSR 解放、合成波形への復帰、NoteOn と全曲遷移・音色差し替えの GC 0 byte、root 基準の警告 |
| Codecs/OggWriterTests | OggS、三秒の同一 WAV より小さいサイズ、品質両端、EOS ページ、空・端数バッファ、Stream 所有権、無効入力で既存出力保持 |
| Cli/SampleCodecCommandsTests | import-wav 引数・保存・短い text/JSON 表示・undo/redo・no-loop、失敗時の曲と側車保持、export ogg の共通オプション・品質・警告・出力保護 |
| Mcp/SampleCodecToolsTests / ArpeggioToolsTests | 22 ツールの公開契約、共有セッションの取り込み・一履歴・undo/redo、OGG のレート・フレーム数・警告項目、エラー JSON・状態保持 |

## M2-B の静的確認

- docs/design.md 全文、docs/implementation.md の M1〜M2-A、CLAUDE.md と C# 規約を読んだ。
- 新規に使う Core の型と namespace、BCL の BinaryPrimitives / Base64 / JSON API、OggVorbisEncoder の上記 API を定義・参照 XML・DLL から照合した。
- C# 25 ファイルの括弧対応、doc XML、public/protected summary の隣接、ブロック namespace、および csproj / slnx の XML 形式を静的スクリプトで確認した。
- Render → NoteOn / フレーム更新 → サンプル補間・警告の経路にデコード・配列生成・LINQ・キャッシュ登録が入らないことを読み取り確認した。GC の数値は実測していない。
- Unity lifecycle / GetComponent / AddComponent は追加していない。検索に現れる既存の InstrumentEditor.Update / StartNote は通常の編集・発音メソッドであり Unity lifecycle ではない。

## M2-B 未完了

実装上の残タスクはなし。以下は依頼者による実行確認が必要。

- `dotnet build Arpeggio.slnx` のコンパイル・警告ゼロ、および `dotnet test tests/Arpeggio.Core.Tests` の既存テストを含む全件成功。Codex は両コマンドを実行していない。
- 実 WAV の取り込み・音程・ループ継ぎ目・ADSR の試聴、既存合成波形の回帰、NoteOn / Render / 音色差し替え時の GC 0 byte の実測。
- OGG の実ファイル出力、品質両端・サイズ・EOS、Unity 等での再生。指定どおりデコーダ依存の波形往復テストは追加していない。
- CLI の引数解釈と MCP ホスト経由の import_wav_sample / export_ogg 呼び出し。ここで追加したテストは公開コマンド・ツールをプロセス内で呼ぶ。

## M2-B の利用例

```sh
arpeggio instrument import-wav song.arpeggio.json --id 1 sample.wav --root C4 --loop-start 100 --loop-end 12000
arpeggio instrument import-wav song.arpeggio.json --id 1 hit.wav --no-loop
arpeggio instrument list song.arpeggio.json
arpeggio export ogg song.arpeggio.json song.ogg --loops 1 --sample-rate 44100 --tail 0.5 --quality 0.5
```

MCP: `import_wav_sample(instrumentId, wavPath, rootNote?, loopStart?, loopEnd?, loop?)`、`export_ogg(path, loops?, sampleRate?, tail?, quality?)`。import の省略値は C4・開始 0・終端 0（末尾）・loop=true。

## M2-B 変更ファイル一覧

33 ファイル（新規 12、更新 21）。DAW は csproj の参照一行のみ、Core の csproj と既存依存バージョンは変更していない。

```text
Arpeggio.slnx
docs/design.md
docs/implementation.md
src/Arpeggio.Core/Instruments/SnesSampleInstrument.cs
src/Arpeggio.Core/Instruments/SampleDataCodec.cs
src/Arpeggio.Core/Document/InstrumentValidator.cs
src/Arpeggio.Core/Document/SongTextRenderer.cs
src/Arpeggio.Core/Synthesis/ChannelSynthesizer.cs
src/Arpeggio.Core/Synthesis/PitchTable.cs
src/Arpeggio.Core/Synthesis/Snes/SnesVoiceSynthesizer.cs
src/Arpeggio.Core/Render/SongRenderer.cs
src/Arpeggio.Core/Render/WavReader.cs
src/Arpeggio.Core/Import/WavSampleImporter.cs
src/Arpeggio.Core/Session/InstrumentEditor.cs
src/Arpeggio.Core/Session/InstrumentJson.cs
src/Arpeggio.Codecs/Arpeggio.Codecs.csproj
src/Arpeggio.Codecs/OggWriter.cs
src/Arpeggio.Cli/Arpeggio.Cli.csproj
src/Arpeggio.Cli/InstrumentCommands.cs
src/Arpeggio.Cli/SongCommands.cs
src/Arpeggio.Mcp/Arpeggio.Mcp.csproj
src/Arpeggio.Mcp/ArpeggioTools.cs
src/Arpeggio.Daw/Arpeggio.Daw.csproj
tests/Arpeggio.Core.Tests/Arpeggio.Core.Tests.csproj
tests/Arpeggio.Core.Tests/Instruments/SampleDataCodecTests.cs
tests/Arpeggio.Core.Tests/Instruments/SnesSampleInstrumentTests.cs
tests/Arpeggio.Core.Tests/Import/SampleFileFixture.cs
tests/Arpeggio.Core.Tests/Import/WavSampleImporterTests.cs
tests/Arpeggio.Core.Tests/Synthesis/Snes/SnesEmbeddedSampleTests.cs
tests/Arpeggio.Core.Tests/Codecs/OggWriterTests.cs
tests/Arpeggio.Core.Tests/Cli/SampleCodecCommandsTests.cs
tests/Arpeggio.Core.Tests/Mcp/SampleCodecToolsTests.cs
tests/Arpeggio.Core.Tests/Mcp/ArpeggioToolsTests.cs
```

## M2-B の依頼者側修正（OggVorbisEncoder の静的テーブル汚染）

- 実測: OggVorbisEncoder 1.2.2 は同一プロセスで 32 kHz 以上をエンコードした後、32 kHz 未満・品質 0.5 未満（8 kHz / 11.025 kHz は全品質）のエンコードで `ResidueLookup` が `IndexOutOfRangeException` を投げる。逆順（低→高）と同レート同士は問題ない。テストが並列実行で順序依存に当たり 2 件落ちた
- 対処: エンコード本体を `Arpeggio.Codecs.Vorbis`（OggVorbisEncoder を参照する唯一のアセンブリ）へ切り出し、`Arpeggio.Codecs.IsolatedVorbisEncoder` が呼び出しごとに使い捨ての `AssemblyLoadContext` へ読み込んでリフレクションで実行する。`Resolving` イベントでは既定コンテキスト（deps.json 由来）が先に共有インスタンスを解決するため、`Load` をオーバーライドしたサブクラスで先回りして自分のディレクトリから読み込む
- 空入力（0 フレーム）は音声パケットが無く EOS ページが出ないため、無音 1 フレームを書いて閉じる
- 回帰テスト: `Codecs/OggWriterTests.Write_LowRateAfterHighRateDoesNotThrow`


# M2-C 実装記録（2026-09-07）

## M2-C の実装範囲と判断

- **構成**: `MainWindowPresenter` が NotePanel / Analysis / Export / SfxCreation の Presenter を明示的に生成する。右ペインは「編集」「解析」「SFX」のタブとし、編集タブ内でノートパネルを音色パネルの上へ配置した。MainWindowPresenter は 300 行以内。Core / CLI / MCP / Codecs と依存バージョンは変更していない。Codecs と DAW テストの ProjectReference は既存のものを使用する。
- **ノート効果**: 固定の一覧と共通入力欄を使い、行ごとの編集コントロールは増やさない。全五種類の kind と整数 value を追加・適用・削除する。Arpeggio は +0〜+15 の二つの半音差を上位・下位 4 bit に畳み、一覧には半音差と 16 進値を表示する。操作前にドラッグを確定し、元ノートを変更せず `NoteEditor.Update` を一度呼ぶ。Core の重複・範囲検証と次バッファへの公開経路をそのまま使う。エフェクト付きノートは右上に再利用 Pen で小さなマークを描く。
- **SFX 作成**: `SfxPresetCatalog` の全八種類を表示し、現在の正本と同じディレクトリの `sfx-<preset>.arpeggio.json` を既定にする。現在のチップで `SfxPresetFile.Create` → `MainWindowPresenter.Open` → `DawDocument.Open` 内の `EditSession.Open` を使用する。作成画面に「未保存の編集は保存して切り替える」と明記し、既存の Save 経路で旧文書を保護してから作成する。外部変更との競合は非モーダルなステータスで拒否する。成功後は音色の既定選択を初期化し、ファイル監視・既定保存先・表示位置を切り替える。
- **解析**: UI で開始時点の JSON を固定し、`Task.Run` 内で復元して `AudioAnalysisSource.AnalyzeSong` を呼ぶ。全トラック／番号指定のソロを選べる。テキストは `AnalysisTextRenderer` を使い、完了通知は `IMainWindowView.RunOnUiThreadAsync` → Avalonia Dispatcher を経由する。実行中表示・入力無効化・二重起動拒否・失敗後の再試行を備える。
- **解析結果と編集状態**: Core は未変更トラックやノートの参照を保持するため、トラックリストの参照だけでは編集を検知できない。文書参照と開始時の正規 JSON を比較し、実際の編集・再読込・文書切替で結果と警告件数を無効化する。選択変更だけでは維持する。比較は編集の Refresh 時だけで、描画タイマーでは行わない。大量の埋め込みサンプルを含むソングでの編集応答時間は実測対象。
- **警告**: 最新の有効な解析の音響警告・合成警告・保持上限超過数を、既存の再生警告件数へ加算する。表示上限は int 最大値。既存の警告展開から解析結果も参照できる。編集後は解析分だけを消す。
- **WAV 取り込み**: SNES 音色だけにルート音名（既定 C4）・全体ループ指定・取り込みボタンを表示する。`StorageProvider.OpenFilePickerAsync` 後、選択待ち中に音色が変わっていないことを確認する。音色を `InstrumentJson` で複製して `WavSampleImporter.Import` → `InstrumentEditor.Update` の一履歴で公開する。失敗時は元音色を保持する。`SampleSummary` を表示し、動的パラメータ入力から `sampleData` を除外して Base64 の大量表示と null 入力を防ぐ。通常の音色適用でも埋め込みデータを維持する。
- **書き出し**: トランスポート脇のボタン／Ctrl+E から OS 保存先ピッカーを開く。拡張子を大小文字非依存で WAV / OGG に振り分け、開始時のソングを `Task.Run` で `SongRenderer.RenderAll` → `WavWriter` / `OggWriter` へ渡す。設定は既存 RenderSettings の既定（44100 Hz・1 回・余白 0.5 秒）、OGG 品質は 0.5。隣接一時ファイルへ書き、成功時だけ保存先へ移動する。失敗時に既存出力を壊さず、一時ファイルを削除する。完了・失敗通知は Poll 後もステータスバーに残す。
- **寿命**: 解析・書き出しには操作ごとの CancellationTokenSource を持ち、画面破棄時に Cancel、非同期操作終了時に Dispose する。Core の `AnalyzeSong` / `RenderAll` とエンコーダはキャンセル引数を持たないため、進行中の同期呼び出しを途中で強制停止はしない。開始前・呼び出し後・保存先への移動前にキャンセルを確認し、破棄済み画面への結果通知を抑止する。ピッカーが返すファイルとフォルダは Dispose し、すべての追加イベントを解除する。

## M2-C のテストコード

Avalonia・SDL を起動しない Presenter テストを 30 ケース追加した。テストコードは追加のみで、実行していない。

| 対象 | ケース数 | 検証内容 |
|---|---:|---|
| NotePanelPresenterTests | 11 | 追加・変更・削除の各一履歴、undo、全 kind、旧スナップショット不変、不正入力・重複拒否、アルペジオ境界、次バッファ反映 |
| SfxCreationPresenterTests | 3 | 旧編集の保存・新規セッションと監視対象の切替、既定パス、上書き拒否、外部変更競合時の保持 |
| AnalysisPresenterTests | 8 | テキストと警告、ソロ、再生警告との合算、二重起動拒否、破棄キャンセル、編集による結果破棄、失敗後の再試行、選択変更と再オープン |
| ExportPresenterTests | 6 | WAV / OGG シグネチャ、成功と失敗通知、失敗後の再試行、既存ファイル保護、二重起動・スナップショット隔離、終了後の通知抑止 |
| InstrumentSamplePresenterTests | 2 | 取り込み一履歴・元レート・ルート音・ループ・短い表示・音色適用と undo による保持、失敗時の状態維持 |

## M2-C の静的確認

- M1-C / M2-A / M2-B の判断と M2-B のエンコーダ分離修正を確認した。
- 使用する Core / Codecs 型の定義と namespace、Avalonia 12.1.2 のローカル NuGet XML の OpenFilePickerAsync / SaveFilePickerAsync / TryGetFolderFromPathAsync / TryGetLocalPath / Dispatcher.InvokeAsync、.NET 10 参照 XML の Task・キャンセル・テスト同期 API を照合した。
- 追加・更新ファイルの括弧対応・doc XML・public summary の隣接、AXAML / csproj の XML 形式、XAML の名前と Require 呼び出し、追加イベントの解除対称性を静的スクリプトで確認した。
- git 操作、dotnet build、dotnet test、コンパイル、アプリ起動は実行していない。警告ゼロ・テスト成功・音響値の実測結果としては主張しない。

## M2-C 未完了

実装上の残タスクはなし。以下は依頼者側での確認が必要。

- `dotnet build Arpeggio.slnx` の警告ゼロ。実行禁止の指示に従い、受け入れ条件 7 は未検証。
- `dotnet test tests/Arpeggio.Core.Tests` の全件成功。追加 30 ケースを含むコンパイル・実行を未確認。
- Avalonia 上でのタブ・ノート選択・エフェクト入力・Ctrl+E・最小ウィンドウ幅での操作、OS ピッカーの WAV / OGG 拡張子確定と上書き確認、macOS / Windows の表示と入出力。
- 再生中の効果変更が次バッファへ反映されること、SNES の WAV 取り込み音程・ループ・音色再編集、OGG の試聴、SFX 切替後の新規ファイル監視。
- 長いソングの解析中／書き出し中の編集・終了、完了通知の Dispatcher 配送、キャンセル後の一時ファイル清掃。既存 Core 同期 API の呼び出し中はキャンセル完了を待つ制約がある。

## M2-C 変更ファイル一覧

新規:

```text
src/Arpeggio.Daw/Audio/SongFileExporter.cs
src/Arpeggio.Daw/Presenters/NotePanelPresenter.cs
src/Arpeggio.Daw/Presenters/AnalysisPresenter.cs
src/Arpeggio.Daw/Presenters/ExportPresenter.cs
src/Arpeggio.Daw/Presenters/SfxCreationPresenter.cs
src/Arpeggio.Daw/Views/NotePanelView.axaml
src/Arpeggio.Daw/Views/NotePanelView.axaml.cs
src/Arpeggio.Daw/Views/AnalysisView.axaml
src/Arpeggio.Daw/Views/AnalysisView.axaml.cs
src/Arpeggio.Daw/Views/SfxCreationView.axaml
src/Arpeggio.Daw/Views/SfxCreationView.axaml.cs
src/Arpeggio.Daw/Views/AudioFilePicker.cs
tests/Arpeggio.Core.Tests/Daw/NotePanelPresenterTests.cs
tests/Arpeggio.Core.Tests/Daw/AnalysisPresenterTests.cs
tests/Arpeggio.Core.Tests/Daw/ExportPresenterTests.cs
tests/Arpeggio.Core.Tests/Daw/SfxCreationPresenterTests.cs
tests/Arpeggio.Core.Tests/Daw/InstrumentSamplePresenterTests.cs
```

更新:

```text
src/Arpeggio.Daw/Presenters/MainWindowPresenter.cs
src/Arpeggio.Daw/Presenters/IMainWindowView.cs
src/Arpeggio.Daw/Presenters/InstrumentPanelPresenter.cs
src/Arpeggio.Daw/Presenters/InstrumentParameterEditor.cs
src/Arpeggio.Daw/Views/MainWindow.axaml
src/Arpeggio.Daw/Views/MainWindow.axaml.cs
src/Arpeggio.Daw/Views/InstrumentPanelView.axaml
src/Arpeggio.Daw/Views/InstrumentPanelView.axaml.cs
src/Arpeggio.Daw/Views/PianoRollControl.cs
tests/Arpeggio.Core.Tests/Daw/FakeMainWindowView.cs
docs/implementation.md
```

# M2-E-A 実装記録（2026-09-08）

## M2-E-A の実装判断

- **内部時間軸**: SNES のみ RenderMixer が全ボイスを 32000 Hz で順に進める。SongRenderer からの Render 呼び出しは全曲モードでは発音制御の経路として残し、DSP の二重進行を防ぐ。ADSR・ノイズ・ガウス補間・エコーを処理した後の左右二点を、整数のレート累積器で出力へ線形補間する。補間は過去二点を使うため一 DSP サンプル分の遅延がある。単独 SnesVoiceSynthesizer と SnesEcho にも出力レート変換を持たせた。バッファ分割・シークでは履歴とレート累積器も復元される。
- **ピッチ**: P は 0〜16383、原速 4096、進みは P / 4096。内蔵波形は 128 サンプル周期（P=4096 で 250 Hz）へ変更し、MIDI 周波数から P を量子化する。埋め込みは保存した元レートを P の計算に含める。したがって 44.1 kHz 素材の原音程は P≒5645、32 kHz 素材は P=4096。P の上限が同じなので元レートが高い素材ほど上方向の移調範囲は狭い。従来の SongRenderer の root 基準の音域警告は変更していないため、元レートを含む厳密な警告範囲への更新は今回の変更禁止範囲との境界として残る。
- **BRR**: 16 サンプル / 9 バイト。ブロックごとの shift 0〜12 × filter 0〜3 を復号誤差で比較する（誤差 0 なら残りの shift 探索は不要）。候補は復号済みの過去二点を引き継ぐ。ニブルは近隣候補も比較し、15 bit 正端から負端へ折り返す量子化事故を避ける。デコーダは整数予測、signed ニブル、shift 13〜15 の特殊値、16 bit 飽和後の倍化を扱う。最終ブロックの end / loop フラグを公開 API から取得できる。末尾は最終サンプルで埋める。
- **キャッシュ**: SampleData の setter で BRR 往復し、音色所有の BrrSample に格納する。Loop / LoopStart / LoopEnd の変更時は PCM を共有するループ情報だけ更新し、JSON のプロパティ順序へ依存しない。入力 PCM 配列はキャッシュ生成後に保持せず、元の SampleCount と Base64 は維持する。内蔵波形はボイス構築時に準備する。NoteOn と Render は復号・波形生成・辞書登録を行わない。
- **WAV 取り込み**: `import-wav` の保存形式は従来の PCM Base64 と元レートのまま。再生は必ず BRR エンコード→デコードを経由するため、取り込み結果の音は「BRR に丸めた音」になる。CLI / MCP / Codecs / インポーター自体は変更していない。ループ開始は下側、終端は上側の 16 サンプル境界へ丸める。初回のみ前奏区間を通り、ループ後の前一点もループ末尾へ接続する。
- **ガウス補間**: 近似生成ではなく、512 エントリの実機数値表を収録した。表の照合元は [Snes9x の SPC_DSP.cpp](https://github.com/snes9xgit/snes9x/blob/master/apu/bapu/dsp/SPC_DSP.cpp)。四係数の整数和は 2047〜2049（分母 2048）。補間積和は浮動小数点で行い、DSP 出力段で 16 bit に量子化する。実機の積ごとの切り捨て・途中のオーバーフローは再現対象外。
- **ADSR**: 11 bit 音量と共通 32 レート表。最大 attack は二サンプルで立ち上がり、最大 sustain / rate 0 は減衰しない。秒指定は attack 完了時間と指定 sustain 到達時間の最寄りレジスタへ量子化し、sustain rate は 0。明示 AdsrRegisters が優先する。release は毎 DSP サンプル 8 の固定減衰（最大音量から 256 サンプル = 8 ms）。ReleaseSeconds は JSON に保存するが再生速度へは適用しない。
- **FIR**: 16 ms = 512 DSP サンプル。遅延読み出し→左右独立の 8 タップ FIR→wet とフィードバックの順に処理する。FIR 係数は構築時に複製し、実行中の設定配列変更から切り離す。フラット係数 127 / 128 の利得差を既存テストにも反映した。Flat / LowPass / HighPass / Wide の係数表は design.md に記載した。
- **変調・ノイズ・音量**: 前ボイスの同じ DSP サンプルの ADSR 後・左右音量前の signed 16 bit 出力で後ボイスを変調する。ボイス 0 の変調源は 0。ミュートしても変調源は継続する。ノイズは音色別 NoiseRate を扱うためボイスごとの 15 bit LFSR とし、NoteOn で同じ seed へ戻す。左右は 127 段階、既存の八ボイス分のヘッドルームを保ち、エコー書き込みとマスターを 16 bit に飽和・量子化する。
- **境界**: NES / GB の波形・Sequencing / SongRenderer / CLI / MCP / DAW / Codecs と JSON version は変更していない。SPC の命令・RAM、実機共有カウンターの位相、共有ノイズレジスタ、BRR ループごとの予測履歴の再デコードまでの完全エミュレーションではない。

## M2-E-A のテストコード

| ファイル | 確認対象 |
|---|---|
| BrrCodecTests | 正弦波 2048 サンプルの相対 RMS 誤差 1 %、再現性、ブロック数、end / loop、既知ニブル、フィルタ別の履歴、ループ境界 |
| GaussianInterpolatorTests | 256 位相の四係数和、20 kHz 素材の 8 kHz 正弦波を 32 kHz 再生した際の 12 kHz 以上のイメージ成分と線形補間との FFT 比較 |
| SnesEnvelopeTests | 最大 attack と保持、固定 release、最遅 attack の周期、秒→レジスタの単調性、sustain rate の減衰 |
| SnesEchoTests | 512 サンプルの遅延と帰還、LowPass の 8 kHz 抑制、係数複製、0 ms 無効、リセット |
| SnesDspIntegrationTests | ノイズの決定性・重心・停止レート、ミキサー経由の変調周波数分散、明示 ADSR 優先、22.05 / 44.1 / 48 kHz の分割不変性と全 DSP 機能有効時の GC 0 byte |
| SnesDspSettingsTests | 新項目省略の JSON、version 1 往復、キャッシュ非保存、レジスタと FIR の入力境界、プリセット配列の独立性、14 bit ピッチ端点 |
| 既存 SnesEmbeddedSampleTests / PitchTableTests | 線形補間・秒指定 release・16 bit ピッチの旧期待値を新仕様へ更新。各テストは維持し、埋め込み音の BRR 量子化を追加 |

## M2-E-A の静的確認

- 変更対象と隣接する C# 27 ファイルについて括弧対応、doc XML の構文、public summary の隣接を確認した。ガウス表は 512 点、四係数の整数和は 2047〜2049 を確認した。
- Core 内の参照型と namespace、既存の System.Numerics / JSON / FFT / AudioAnalyzer API を定義・使用箇所から照合した。
- 音声経路のキャッシュ取得・レート更新・補間・FIR・変調に配列生成、LINQ、ボクシングが入らないことをコードで確認した。NoteOn のレジスタ生成は値型である。GC 数値は未実測。
- Unity lifecycle / GetComponent / AddComponent の追加はない。SnesEnvelope.Start は通常の DSP 状態初期化メソッド。
- git 操作、Unity 起動、コンパイル、dotnet build / dotnet test は実行していない。

## M2-E-A 未完了

依頼者側で以下の実行確認が必要。成功は未確認であり、既存 528 件および追加テストが通ったとは報告していない。

- ビルドのエラー・警告ゼロ、既存全テストと追加テストの成功。
- BRR 相対 RMS 誤差 1 %、ガウス / FIR の FFT 比較、変調の周波数分散、ノイズのスペクトル重心の実測。
- Render / NoteOn / 音色置換を含む GC 0 byte の実測、WAV 取り込み音・ループ継ぎ目・固定 release・エコーの試聴。
- SongRenderer の変更許可が得られる別作業で、埋め込み音の元レートを含むピッチクランプ警告範囲を再生範囲と一致させる（再生側の 14 bit クランプは実装済み）。

## M2-E-A 変更ファイル一覧

```text
src/Arpeggio.Core/Instruments/SnesSampleInstrument.cs
src/Arpeggio.Core/Instruments/SnesAdsrRegisters.cs
src/Arpeggio.Core/Document/SnesEchoSettings.cs
src/Arpeggio.Core/Document/SnesEchoFirPresets.cs
src/Arpeggio.Core/Document/InstrumentValidator.cs
src/Arpeggio.Core/Document/SongValidator.cs
src/Arpeggio.Core/Synthesis/PitchTable.cs
src/Arpeggio.Core/Synthesis/Snes/BrrCodec.cs
src/Arpeggio.Core/Synthesis/Snes/BrrSample.cs
src/Arpeggio.Core/Synthesis/Snes/GaussianInterpolator.cs
src/Arpeggio.Core/Synthesis/Snes/SnesRateTable.cs
src/Arpeggio.Core/Synthesis/Snes/SnesEnvelope.cs
src/Arpeggio.Core/Synthesis/Snes/SnesNoiseGenerator.cs
src/Arpeggio.Core/Synthesis/Snes/SnesVoiceSynthesizer.cs
src/Arpeggio.Core/Synthesis/Snes/SnesEcho.cs
src/Arpeggio.Core/Synthesis/Snes/SnesMixer.cs
src/Arpeggio.Core/Render/RenderMixer.cs
tests/Arpeggio.Core.Tests/Synthesis/PitchTableTests.cs
tests/Arpeggio.Core.Tests/Synthesis/Snes/BrrCodecTests.cs
tests/Arpeggio.Core.Tests/Synthesis/Snes/GaussianInterpolatorTests.cs
tests/Arpeggio.Core.Tests/Synthesis/Snes/SnesEnvelopeTests.cs
tests/Arpeggio.Core.Tests/Synthesis/Snes/SnesEchoTests.cs
tests/Arpeggio.Core.Tests/Synthesis/Snes/SnesEmbeddedSampleTests.cs
tests/Arpeggio.Core.Tests/Synthesis/Snes/SnesDspIntegrationTests.cs
tests/Arpeggio.Core.Tests/Synthesis/Snes/SnesDspSettingsTests.cs
docs/design.md
docs/implementation.md
```

## M2-E-B の実装判断

- **保存と既定値**: `SnesSampleInstrument.Preset` を追加した。推奨値の根拠は `SnesInstrumentCatalog` 一箇所。Loop / SampleRate / RootMidiNote / EchoSend は nullable な明示値とプリセット既定値を分けて保持し、ADSR は「未指定」と「明示 null」を区別する。これにより JSON の項目順にかかわらず上書きを保つ。保存には有効な設定値と preset 名を出し、生成 PCM / BRR を埋め込まない。
- **生成とキャッシュ**: `SnesInstrumentBank.Build` は呼び出しごとに新しい BrrSample を返す純関数。素材の合成責務は持続系・減衰系・ドラムの三型に分けた。Preset setter で Build を呼び、音色所有のキャッシュを用意する。ループ設定変更は BrrSample.WithLoop だけで、PCM を再生成しない。SnesVoiceSynthesizer は SampleData → PreparedPreset → 従来波形の順で参照を選ぶ。JSON の読み込み・編集の度に新しい音色キャッシュを準備し、グローバルな可変キャッシュは導入していない。
- **素材の音程と音量**: 持続系は全て 128 サンプル / 32000 Hz / root 59。250 Hz の整数周期を維持し、C4 は約 264.9 Hz（約 +21 cent）になる設計。半音以内の受け入れ範囲を満たす想定だが、実出力の値はテスト未実行につき未確認。減衰系は平均律 C4 / root 60、ドラムも root 60 を原速基準とする。DC 除去後の素材ピークを 0.72 に正規化して BRR へ渡し、ミキサーの既存ヘッドルームを使う。
- **strings のデチューン**: 128 サンプルで二つの非整数周期をそのまま切ると毎周回で位相が飛ぶため、二声の微小な位相差を周期内で戻す形にした。ゆっくり独立にうなる二発振器の完全再現ではない。鋸歯状倍音のロールオフと第七倍音の差をテストで確認する。
- **短いドラム**: snare / hat / openhat / tom は依頼の個別指定時間（150 / 40 / 200 / 200 ms）を優先する。kick は 300 ms、crash は 800 ms。高域ノイズは二階差分、クリックは微小な固定シードノイズ。ピッチ下降は位相を積分して連続にする。DSP NoiseEnabled は使用せず、ノイズ込み素材を BRR 往復させる。
- **入力境界**: 未知 preset と SampleData との二重指定は InstrumentValidator でエラーにする。検証を迂回したボイスでは SampleData の優先を維持した。WavSampleImporter は入力検証成功後に Preset を解除する。CLI の ApplyPreset は SampleData を解除して推奨値を再適用し、名前・Pan・マクロを保持する。明示 `--adsr` はレジスタ指定を解除し、`--adsr-registers` があればそちらを優先する。
- **トラックの割り当て**: 既存 Track には既定音色 ID がなかったため、SNES 用の nullable DefaultInstrumentId を追加した。名前から音色を推測すると改名や chip バンクの lead 二声を扱えないため、ID を保存する。Validator・DefaultInstrumentResolver・スナップショット公開・音色削除の両経路にも反映した。既存曲では null を保存しない。NES / GB の音色・合成器・既定音色選択は変更していない。
- **orchestral の編成**: 依頼一覧は kick と snare を分けると 9 音色。確認を提示し、返答待ちの暫定値として piano を除外した `strings / brass / flute / choir / bass / kick / snare / hat` を採用した。band / chip は指定どおり。bank 未指定時の既存 lead 一音色は変更していない。
- **CLI / MCP / 表示**: instrument presets snes、add / set の --preset、new の --bank、MCP snes_presets と new_song(bank) を追加。音色 JSON は既存の add / update 経路で扱う。show と instrument list は `SnesSample strings` を表示する。MCP の公開ツール数の既存テストを 23 個へ更新した。
- **音域警告**: SongRenderer の素材判定に Preset を加え、プリセットの警告も root 基準で出す。DSP・PitchTable の計算自体は変更していない。元 SampleRate を任意に上書きした場合の厳密な警告範囲は M2-E-A の既存課題を継承する。
- **SFX**: 既存 SNES SFX は維持した。効果音の高音域・急なピッチスライドに対する内蔵素材の改善を試聴で確認できないため、今回は任意の置き換えを採用しない。既存 Pulse / Noise を引き続き使用する。
- **デモ**: examples/snes-demo.arpeggio.json と snes-demo-ops.json を追加。4 小節、8 トラック、各 4 音と Pan。CLI と同じ既定値・操作列に相当する JSON を生成した。実 CLI 実行から得た成果物ではなく、下記テストで同値性を検証する構成。WAV 自体はまだ生成していない。

## M2-E-B のテストコード

| ファイル | 確認対象 |
|---|---|
| SnesInstrumentBankTests | 全 16 音色の二回生成一致、生成長・ループ境界、C4 一秒の非無音・無クリップ・音程、kick / hat 帯域、strings / organ 第七倍音比、全三編成の分割 Render 一致と GC 0 byte |
| SnesPresetDocumentTests | 名前のみの入力と推奨値、JSON 項目順、明示 null ADSR、保存再生一致、未知名・二重指定・不正メタデータの拒否、埋め込み優先、八トラックの既定 ID、従来曲の互換性 |
| SnesBankCommandsTests | 一覧・追加・差し替え・推奨値と明示上書き、不正入力時の保存維持、WAV とプリセットの相互切り替え・undo/redo、同梱バッチの CLI 再生成と同梱曲との同値性、export wav 後の AudioAnalyzer |
| SnesBankToolsTests / ArpeggioToolsTests | snes_presets のスキーマ、全三編成の new_song、音色 ID 省略、add/update JSON、保存再オープン、不正入力・削除拒否、公開ツール名 |

## M2-E-B の静的確認

- 変更 C# の括弧対応・doc XML・public summary の隣接を確認した。参照する型の定義と namespace、CLI / MCP / AudioAnalyzer / BrrSample / WavReader の既存 API を照合した。
- Render / NoteOn の変更はキャッシュ選択と素材判定のみ。生成・復号・新しい配列・辞書登録・LINQ を追加していない。GC 数値は未実測。
- デモの JSON 構文とテスト csproj の XML 構文を確認した。
- git 操作、Unity 起動、コンパイル、dotnet build / dotnet test、CLI の実行は行っていない。

## M2-E-B 未完了

依頼の「コンパイル・テスト実行は依頼者が行う」に従い、以下は実行していない。テスト成功や音響条件達成はまだ確認できていない。

- コンパイルのエラー・警告ゼロ、既存テスト全件および追加テストの実行。
- 全音色の C4・ドラム帯域・倍音比・非無音／無クリップの実測、全三バンクの Render GC 0 byte 実測。
- SnesBankCommandsTests のデモ再生成・同梱 JSON との同値性・export wav・解析の実行。実 CLI でのデモ生成と WAV の書き出しは README の手順で再現する。
- 試聴によるループ継ぎ目・音色の区別・減衰・エコーの確認。
- orchestral の 9 音色指定から除外する音色について、piano 除外の暫定編成を最終確認する。

## M2-E-B 変更ファイル一覧

```text
src/Arpeggio.Cli/InstrumentCommands.cs
src/Arpeggio.Cli/InstrumentOptions.cs
src/Arpeggio.Cli/SongCommands.cs
src/Arpeggio.Core/Document/InstrumentValidator.cs
src/Arpeggio.Core/Document/SongFactory.cs
src/Arpeggio.Core/Document/SongTextRenderer.cs
src/Arpeggio.Core/Document/SongValidator.cs
src/Arpeggio.Core/Document/Track.cs
src/Arpeggio.Core/Import/WavSampleImporter.cs
src/Arpeggio.Core/Instruments/Snes/SnesBankKind.cs
src/Arpeggio.Core/Instruments/Snes/SnesBankLayout.cs
src/Arpeggio.Core/Instruments/Snes/SnesInstrumentBank.cs
src/Arpeggio.Core/Instruments/Snes/SnesInstrumentCatalog.cs
src/Arpeggio.Core/Instruments/Snes/SnesInstrumentPreset.cs
src/Arpeggio.Core/Instruments/Snes/SnesInstrumentRecipeDecay.cs
src/Arpeggio.Core/Instruments/Snes/SnesInstrumentRecipeDrums.cs
src/Arpeggio.Core/Instruments/Snes/SnesInstrumentRecipeSustained.cs
src/Arpeggio.Core/Instruments/SnesSampleInstrument.cs
src/Arpeggio.Core/Render/SongRenderer.cs
src/Arpeggio.Core/Session/BatchOperationApplier.cs
src/Arpeggio.Core/Session/DefaultInstrumentResolver.cs
src/Arpeggio.Core/Session/EditSession.cs
src/Arpeggio.Core/Session/InstrumentEditor.cs
src/Arpeggio.Core/Session/SongSnapshotPublisher.cs
src/Arpeggio.Core/Synthesis/Snes/SnesVoiceSynthesizer.cs
src/Arpeggio.Mcp/ArpeggioTools.cs
tests/Arpeggio.Core.Tests/Cli/SnesBankCommandsTests.cs
tests/Arpeggio.Core.Tests/Instruments/Snes/SnesInstrumentBankTests.cs
tests/Arpeggio.Core.Tests/Instruments/Snes/SnesPresetDocumentTests.cs
tests/Arpeggio.Core.Tests/Mcp/ArpeggioToolsTests.cs
tests/Arpeggio.Core.Tests/Mcp/SnesBankToolsTests.cs
tests/Arpeggio.Core.Tests/Arpeggio.Core.Tests.csproj
examples/snes-demo.arpeggio.json
examples/snes-demo-ops.json
docs/design.md
docs/implementation.md
README.md
```

# M2-D 実装記録（2026-09-08）

## M2-D の実装範囲と判断

- ArpeggioTheme / Icons の二つの ResourceDictionary を App へ追加。FluentTheme を下敷きに、色・字体・寸法と各コントロールの状態を統一した。静的カラー規約とチャンネル記号は design.md の「DAW のビジュアル規約」を正本とする。
- ボタンの ContentTemplate で PathIcon と既存ラベルを併記する。Play / Stop の既存文字列に含まれる装飾記号はテンプレート内のベクターへ置き換え、ラベルの「再生」「停止」は維持した。動的 Content の再生状態・ループ・解析中・書き出し中・警告件数も維持する。ミュートは CheckBox と IsCheckedChanged の購読を維持し、テンプレートだけを変更した。
- ChannelPalette の不変ブラシ・記号をトラック一覧・ピアノロール・音色見出しで共有する。音色のキャッシュ早期 return で P1 → P2 / S1 → S2 の見出し更新が失われないよう、MainWindow.ShowSong から View の ShowChannel を先に呼ぶ。Presenter は変更していない。
- ピアノロールは音量 16 段階の RGB 明度、ゴースト 25%、選択枠、効果三角、下端の暗い縁、再生線と三角を描く。文字の背面は一定輝度とし、低音量・ゴーストの重なりでも判別できるようにした。狭いノートは記号を無理に詰め込まず、記号・トラック名のツールチップを併用する。ノートの当たり判定と編集操作は既存のまま。
- ThemeResources は各描画コントロールの初期化時だけ、MergedDictionaries を探索する TryGetResource でリソースを解決する。ブラシ・Pen・三角形 Geometry・文字はフィールドに保持し、Render 中に参照型オブジェクトを生成しない。UI 非依存の純関数を保つため ChannelPalette は固定の不変ブラシを持ち、AXAML と色が一致することを静的確認した。
- SVG は背景 + 四ブロック + 一周期の矩形波の六図形。1024 四方、背景の角丸 180、波の線幅 64、外部参照なし。PNG / icns / ico、ApplicationIcon / Window.Icon は変更していない。
- コードビハインドの変更は上記表示の反映に限定した。Presenters / Audio / Editing / Watch / Platform、Core / CLI / MCP / Codecs、パッケージ・プロジェクト設定は変更していない。git 操作は行っていない。

## M2-D のテストコードと静的確認

- ChannelPaletteTests に 27 ケースを追加。全有効種別の指定色と先頭記号、enum 網羅、P2 / S1〜S8、None / 未定義種別、範囲外番号、ブラシの再利用を検証する。アプリを起動しない純関数テスト。
- AXAML / SVG の XML 構文、リソースキー重複・参照先、Views の色リテラル不在、チャンネル色の AXAML / C# 一致を確認した。
- 既存 AXAML の全要素型・x:Name・親子関係を作業前コピーと照合した。ヘッダー TextBlock 内に追加した装飾 InlineUIContainer / Run を除き一致。既存要素の移動・削除はない。既存 Content / 文言は維持した。
- 本文・補足の面に対するコントラストは最小 12.76:1 / 5.31:1、色帯上の暗い文字は最小 7.45:1。ゴースト記号も背景面を一定にしてコントラストを維持する。
- 使用型の namespace と API を既存コード・Avalonia 12.1.2 のローカル NuGet XML / DLL のメタデータで照合した。Fluent のテンプレート内の状態リソース名もローカル DLL と照合し、内部パーツへの状態色を上書きする。ContentPresenter の既定パーツ名は維持した。
- ビルド・コンパイル・テスト実行・アプリ起動は指示に従い実施していない。静的確認は AXAML コンパイラや実行時の検証を代替しない。

## M2-D 未完了

実装上の残タスクはなし。以下は依頼者側の確認・変換が必要。

- コンパイルと AXAML のリソース / スタイル / テンプレートのロード確認。既存 502 件と追加 27 ケースのテスト成功は未確認。
- 起動後の macOS / Windows の Inter・等幅フォント、hover / pressed / disabled / focus-visible、Tab / Space、ミュート、警告色、動的ボタン文言の目視と操作確認。
- 最小ウィンドウ幅、全チップのトラック記号、P1 ↔ P2 / S1〜S8 の音色見出し、短いノートのツールチップ、音量 0 / 15、ゴーストの重なり、ズーム・スクロール・鍵盤同期・再生カーソルの目視確認。
- SVG の 16px 表示を含む目視、rsvg-convert / iconutil による PNG / icns / ico 変換、変換後の ApplicationIcon と Window.Icon の設定。

## M2-D 変更ファイル一覧

- `src/Arpeggio.Daw/Views/AnalysisView.axaml`
- `src/Arpeggio.Daw/Views/InstrumentPanelView.axaml`
- `src/Arpeggio.Daw/Views/InstrumentPanelView.axaml.cs`
- `src/Arpeggio.Daw/Views/KeyboardStripControl.cs`
- `src/Arpeggio.Daw/Views/MainWindow.axaml`
- `src/Arpeggio.Daw/Views/MainWindow.axaml.cs`
- `src/Arpeggio.Daw/Views/NotePanelView.axaml`
- `src/Arpeggio.Daw/Views/PianoRollControl.cs`
- `src/Arpeggio.Daw/Views/SfxCreationView.axaml`
- `src/Arpeggio.Daw/Views/TimeRulerControl.cs`
- `src/Arpeggio.Daw/Views/TrackListView.axaml`
- `src/Arpeggio.Daw/Views/TrackListView.axaml.cs`
- `src/Arpeggio.Daw/Views/TransportView.axaml`
- `src/Arpeggio.Daw/Views/TransportView.axaml.cs`
- `src/Arpeggio.Daw/App.axaml`
- `src/Arpeggio.Daw/Themes/ArpeggioTheme.axaml`
- `src/Arpeggio.Daw/Themes/ChannelPalette.cs`
- `src/Arpeggio.Daw/Themes/Icons.axaml`
- `src/Arpeggio.Daw/Themes/ThemeResources.cs`
- `assets/icon/arpeggio.svg`
- `tests/Arpeggio.Core.Tests/Daw/ChannelPaletteTests.cs`
- `docs/design.md`
- `docs/implementation.md`

# M2-E-C 実装記録（2026-09-08）

## M2-E-C の実装判断

- **プリセット**: 音色パネルの先頭に「（合成波形）」と、持続系／減衰系／ドラムの選択不可のカテゴリ見出しを持つ ComboBox を追加。カタログの説明は各項目のツールチップに表示する。選択は複製音色の `ApplyPreset` → `InstrumentEditor.Update` の一履歴とし、ADSR・元レート・ルート音・ループ・EchoSend の推奨値を再適用する。名前・Pan・マクロは保持し、NoiseEnabled は Core の既存方針どおり解除する。同じプリセットの再選択は履歴を増やさない。
- **埋め込み素材の保護**: SampleData がある音色へのプリセット選択は「埋め込みサンプルを使用中。先に解除してください」で拒否し、選択表示も確定値へ戻す。「埋め込みサンプルを解除」を明示操作として追加し、解除自体を Undo 可能な一履歴にする。プリセット解除とサンプル解除では素材固有のループ位置を初期化する。
- **ADSR / DSP**: `SnesDspView` に固定のレジスタ 4 欄・変調とノイズのチェック・整数ノイズレートのスライダーと数値表示を分離した。入力値は `SnesInstrumentInput` で検証し、名前と既存パラメータと合わせて「音色を適用」の一履歴で公開する。全レジスタ空欄は明示 null、途中の空欄・小数・範囲外は拒否する。無効欄はテーマの Danger 枠と説明文を表示し、Fluent のフォーカス・ホバー状態にもエラー色を渡す。
- **ボイスと波形**: 選択ボイスの ChannelIndex が 0 の場合は変調を有効化できない。同じ音色参照のままトラックを切り替えても無効状態と「ボイス 0 は変調できません」を更新する。他ボイスと共有する音色に既にある変調設定は、別項目の適用で暗黙解除しない。Waveform は Preset / SampleData / ノイズが有効なときに無効化し、未確定のノイズチェックにも追従する。
- **バンクの既定選択**: セッション内で明示した既定選択がなければ、保存されている Track.DefaultInstrumentId を優先してパネルに表示する。DAW の新規バンク作成や SFX タブへのバンク操作は追加しない。
- **エコー**: トランスポート脇に SNES 専用の「エコー」Flyout を追加。遅延は 16 ms 刻みの数値入力、フィードバック・音量は数値欄、FIR は Flat / LowPass / HighPass / Wide。任意の既存 FIR 係数は未選択のカスタムとして保持する。「エコーを適用」で一履歴とし、同値の適用では履歴・再生位置を変えない。
- **履歴と再生境界**: Core の EditSession.Change は internal で、公開バッチにもエコー操作がないため、DawDocument.UpdateSnesEcho が複製候補を検証・作業ファイルへ保存してから History.Record と設定の参照交換を行う。公開ソングを先行変更しない。編集は Transport.ChangeStructure を通し、停止→Reset→再生中だった場合のみ再開。SongRenderer.Reset が合成パイプラインとエコーを再構築する既存実装を利用する。保存失敗時も元設定で再生を再開する。
- **UI と寿命**: MainWindowPresenter がエコー Presenter を明示生成し、MainWindow が View と接続する。新規購読は所有 View の Dispose で解除する。既存 AXAML の名前を維持し、Views の追加色・寸法はテーマリソースを使用する。共有ワークスペースで更新されたテーマとエコー入力部品を保持して照合した。モーダル・Core / CLI / MCP / Codecs の変更は行っていない。

## M2-E-C のテストコードと静的確認

- `SnesInstrumentPresenterTests`: 10 メソッド／23 ケース。カテゴリ別の推奨値と一履歴・Undo/Redo、SampleData 拒否と明示解除、プリセット解除、全レジスタの範囲外・欠落・小数拒否、上限値と明示 null、ボイス 0、共有音色の保持、動的入力からの専用項目除外、ノイズレート境界、CLI バンクの既定音色。
- `SnesEchoPresenterTests`: 7 メソッド／16 ケース。再生中の停止・先頭リセット・再開、一履歴と Undo/Redo、再構築レンダラーとの PCM 一致、停止中の適用と保存復元、同値適用、範囲外・非有限値拒否、カスタム FIR 保持、他チップ拒否、作業ファイルの置換失敗での状態保持。
- `SnesEchoAcceptanceTests`: 1 メソッド／1 ケース。編集時の作業ファイル更新、明示保存前の正本保持、明示保存後の復元。
- AXAML の XML 構文、名前付きコントロールの解決、StaticResource の存在を確認した。C# の括弧対応、public summary の隣接、追加イベント購読の解除を確認した。
- Core の型定義・namespace・公開 API と、ローカル Avalonia 12.1.2 の XML API 資料を照合した。Unity lifecycle / GetComponent / AddComponent の追加はない。
- git 操作、コンパイル、dotnet build / dotnet test、Unity / DAW 起動は行っていない。

## M2-E-C 未完了

実装コードと Presenter テストの追加は完了。以下の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` の警告ゼロ、および既存テストを含む全件通過。新規 40 ケースも未実行。
- SNES / NES / GB の表示切替、プリセットカテゴリと選択後の推奨値、SampleData 拒否時のステータスと選択復帰、明示解除後の Undo。
- ADSR の空欄・範囲外・フォーカス中の Danger 枠、ボイス 0 の無効チェックとツールチップ、共有音色のトラック切替、Waveform の無効化、ノイズのスライダー表示。
- エコー Flyout の数値入力・カスタム FIR 表示、再生中適用と Undo/Redo の先頭再開、聴取上のエコー反映。

## M2-E-C 変更ファイル一覧

- `src/Arpeggio.Daw/Presenters/InstrumentPanelPresenter.cs`
- `src/Arpeggio.Daw/Presenters/InstrumentParameterEditor.cs`
- `src/Arpeggio.Daw/Presenters/SnesInstrumentInput.cs`（新規）
- `src/Arpeggio.Daw/Presenters/SnesEchoPresenter.cs`（新規）
- `src/Arpeggio.Daw/Presenters/MainWindowPresenter.cs`
- `src/Arpeggio.Daw/Editing/DawDocument.cs`
- `src/Arpeggio.Daw/Views/InstrumentPanelView.axaml`
- `src/Arpeggio.Daw/Views/InstrumentPanelView.axaml.cs`
- `src/Arpeggio.Daw/Views/SnesDspView.axaml`（新規）
- `src/Arpeggio.Daw/Views/SnesDspView.axaml.cs`（新規）
- `src/Arpeggio.Daw/Views/SnesEchoView.axaml`（新規）
- `src/Arpeggio.Daw/Views/SnesEchoView.axaml.cs`（新規）
- `src/Arpeggio.Daw/Views/MainWindow.axaml`
- `src/Arpeggio.Daw/Views/MainWindow.axaml.cs`
- `src/Arpeggio.Daw/Themes/Icons.axaml`
- `src/Arpeggio.Daw/Themes/ArpeggioTheme.axaml`（共有側で追加されたエコー幅・アイコン寸法トークンを使用）
- `tests/Arpeggio.Core.Tests/Daw/DawPresenterFixture.cs`
- `tests/Arpeggio.Core.Tests/Daw/SnesInstrumentPresenterTests.cs`（新規）
- `tests/Arpeggio.Core.Tests/Daw/SnesEchoPresenterTests.cs`（新規）
- `tests/Arpeggio.Core.Tests/Daw/SnesEchoAcceptanceTests.cs`（新規）
- `docs/design.md`
- `docs/implementation.md`


# FL 式ピアノロール（2026-09-08）

## FL 式ピアノロールの実装判断

- **選択と責務**: `NoteSelection` は開始 tick の集合を保持し、公開ソングから対象を解決する。移動成功後だけ新 tick 集合へ追従する。`PianoRollPresenter` は操作判断と結線、`PianoRollGesture` は押下時のノート・矩形・削除軌跡、`NoteEditGesture` は履歴統合を担当する。PianoRollPresenter は 300 行以内。VelocityLanePresenter は同じ選択集合を受け取り、PianoRollPresenter への逆依存を持たない。
- **複数編集の原子性**: 既存 `BatchOperationApplier.Apply(EditSession, ...)` へ全対象の RemoveNote → 全候補の AddNote を一括で渡す。選択同士が隣接していても中間重複で誤拒否せず、Core の検証・保存・参照交換は一回のセッション変更で完結する。重複・範囲・効果の一件の違反も全件拒否する。公開ノートを直接書き換えない。
- **履歴**: ノート追加開始前／移動・伸縮開始前／右削除開始前／音量入力開始前にソングと undo/redo を控え、解放時に押下前の履歴を復元して差分があれば一操作だけ記録する。追加しながら長さ決定する場合も一履歴。元へ戻したドラッグと選択操作は redo を維持する。キャプチャ喪失・トラック切替・Undo/Redo・別編集でも終了境界を通す。
- **入力**: Ctrl＋空白は矩形選択、Shift＋ノートはトグル、二音以上選択中の通常空白クリックは解除だけ。移動・伸縮は掴んだノートを基準とする。右削除は入力通知間の線分とノート領域の交差を判定し、飛び飛びの通知でも途中の音を消す。上書き以外の複数移動は座標をクランプせず Validator の範囲検証を通す。
- **クリップボード**: `NoteClipboard` がソング参照とトラック番号、相対 tick と全属性を保持する。効果配列も複製する。同一トラックへ戻れば貼り付け可能だが、文書を再オープンした後の古いコピーは拒否する。貼り付け・複製は時間範囲の重なる音を削除してから一括追加し、置換数と新選択を表示する。Ctrl+D はクリップボードを上書きしない。再生カーソルは MainWindowPresenter が PlaybackEngine から取得する。
- **UI**: 既存の AXAML 親子関係と名前を維持し、ピアノロールの親 Grid にツールバー行と固定 80px の音量レーン行を追加した。音量レーンの左端・幅・ズームを RollScroll の viewport と同期する。カスタム描画、フィールド保持した Brush/Pen、テーマトークンを利用する。スナップ選択の購読を MainWindow／ToolbarView の Dispose で解除する。既存パラメーター入力のショートカット保護は維持する。
- **変更境界**: Core / CLI / MCP / Codecs / Formats と依存パッケージは変更していない。git 操作も行っていない。

## FL 式ピアノロールのテストコードと静的確認

Avalonia／SDL を起動しない Presenter テストを 26 メソッド、43 ケース追加した。テストコードは未実行。

| ファイル | ケース数 | 対象 |
|---|---:|---|
| PianoRollSelectionTests | 21 | 矩形の境界・部分重なり・逆方向、Shift と redo、二音選択時の空白誤追加防止、隣接音の相対移動・範囲外／重複拒否、共通長さ差分・最短 1 tick・効果制約、追加ドラッグ、先頭以外の音のドラッグと長さ記憶 |
| PianoRollClipboardTests | 9 | 属性と効果配列の独立性、コピー・切り取り・複製の一履歴、部分重複上書きと件数・隣接保持、範囲外貼り付けで作業ファイル維持、トラック／文書境界、再生位置と先頭への貼り付け |
| PianoRollEditingTests | 13 | 右削除の線分軌跡と一履歴・ゴースト除外、全選択と矢印・オクターブ・音量・全削除、全スナップ単位と Alt、音量レーンの複数編集・上下限・Undo/Redo・トラック切替 |

- Core の Note / NoteEffect / BatchOperation / BatchOperationApplier / SongValidator と namespace、ローカル Avalonia 12.1.2 の Pointer Capture・DrawingContext.PushOpacity・ComboBox 選択 API、.NET 10 の LINQ API を照合した。
- AXAML の XML 構文、StaticResource と ThemeResources のキー、MainWindow の Require と x:Name、C# の括弧対応、public/protected の summary、追加購読の解除を静的確認した。
- dotnet build / dotnet test / コンパイル / アプリ起動は行っていない。警告ゼロ・既存テスト通過・目視済みとは主張しない。

## FL 式ピアノロール 未完了

実装コードとテストコードの追加は完了。以下は依頼者側で未実行の確認事項。

- `dotnet build Arpeggio.slnx` の警告ゼロと、既存テストを含む全件通過。追加 43 ケースも未実行。
- 実機入力での Ctrl／Shift／Alt、Delete／Backspace、矢印・オクターブ・音量・クリップボード、パラメーター入力フォーカスとの干渉。
- 連続する右削除、追加しながら長さ決定、矩形表示、キャプチャ喪失、音量レーンの上下ドラッグ、横スクロール／ズーム時の棒とノートの一致。
- 再生中の複数編集と次バッファ反映、および多数ノートでの Core バッチ・JSON 履歴統合の応答時間。

## FL 式ピアノロール 変更ファイル一覧

| 場所 | ファイル |
|---|---|
| `src/Arpeggio.Daw/Presenters/` 更新 | `PianoRollPresenter.cs`、`PianoRollDragMode.cs`、`MainWindowPresenter.cs` |
| 同上・新規 | `NoteSelection.cs`、`NoteSelectionRectangle.cs`、`NotePointerModifiers.cs`、`NoteClipboard.cs`、`SnapResolution.cs`、`SnapGrid.cs`、`NoteBatchEditor.cs`、`NoteEditGesture.cs`、`PianoRollGesture.cs`、`VelocityLanePresenter.cs` |
| `src/Arpeggio.Daw/Views/` 更新 | `PianoRollControl.cs`、`MainWindow.axaml`、`MainWindow.axaml.cs` |
| 同上・新規 | `VelocityLaneControl.cs`、`PianoRollToolbarView.axaml`、`PianoRollToolbarView.axaml.cs` |
| `src/Arpeggio.Daw/Themes/` | `ArpeggioTheme.axaml`、`ThemeResources.cs` |
| `tests/Arpeggio.Core.Tests/Daw/` 新規 | `PianoRollSelectionTests.cs`、`PianoRollClipboardTests.cs`、`PianoRollEditingTests.cs` |
| `docs/` | `design.md`、`implementation.md` |
# .app バンドル化（2026-09-08）

## .app の実装判断

- **配布形式**: `tools/build_app.sh <RID> [version] [build-number]` から `dotnet publish --self-contained true` を呼び、macOS `.app` と Windows の依存同梱フォルダを ZIP 化する。RID は `osx-arm64`／`osx-x64`／`win-x64`／`win-arm64`。既定バージョンは `1.0.0`、ビルド番号は `1`。Avalonia・SDL3・.NET のネイティブ探索経路を維持するため、単一ファイル化・トリミング・AOT は無効。パッケージのバージョンは変更しない。
- **生成と出力**: macOS の ZIP は標準の `ditto`、Windows は `zip` を使う。DMG 作成ツールへの追加依存は持たない。出力は worktree 内の `artifacts/` に固定し、RID／数値バージョンを検証してパス展開を制限する。同ディレクトリの一時領域で publish・署名・圧縮を完了してから、前回の同 RID 出力を置き換える。終了・失敗時に一時領域を削除する。macOS の生成は macOS 上に限定し、Windows 用はクロス publish も受け付ける。
- **macOS バンドル**: publish の全ファイルを `Contents/MacOS/` に置き、apphost `arpeggio-daw` を直接 `CFBundleExecutable` にする。シェルランチャーを挟まない。アイコンは既存の `arpeggio.icns` を `Contents/Resources/` へコピーする。plist テンプレートは `tools/macos/Info.plist`。名前は `Arpeggio`、ID は `dev.pisuke.arpeggio`、バージョンはスクリプトの入力を .NET のメタデータと plist に反映する。`CFBundleVersion` は正整数のビルド番号、`LSMinimumSystemVersion` は .NET 10 に合わせて `14.0`、`NSHighResolutionCapable` は true。バックグラウンドアプリにはしない。
- **署名**: Developer ID／公証／Windows Authenticode は使わない。Apple Silicon でのローカル実行を成立させるため、macOS の dylib を内側から ad-hoc 署名し、最後にバンドルを署名・検証する。Hardened Runtime は有効化しないため、JIT 用 entitlement は追加しない。ad-hoc は開発元の証明ではなく Gatekeeper の許可を代替しない。README に右クリック → 開く、現行 macOS の「このまま開く」、対象 `.app` に限定した quarantine 解除手順を記載する。ビルド時には起動・Launch Services 登録・quarantine 解除を行わない。
- **Windows のネイティブ前提**: ローカルの SDL3-CS.Native 3.4.2 の x64／ARM64 `SDL3.dll` は `VCRUNTIME140.dll` に依存する。.NET 同梱だけではクリーンな PC で不足するため、Microsoft の公式 `aka.ms/vc14` URL から対応する VC++ ランタイムインストーラーをビルド時に取得し、`Prerequisites/` と導入説明を ZIP に含める。取得されるインストーラーはビルド時の最新版で固定バージョンではない。初回は必要に応じて利用者が導入する。Arpeggio の exe は既存の `ApplicationIcon` を維持し、レジストリによる関連付けは変更しない。
- **関連付け**: `CFBundleDocumentTypes` と `UTExportedTypeDeclarations` に `dev.pisuke.arpeggio.song`／`arpeggio.json` を登録する。複合拡張子を JSON と判定する Finder への対応として `public.json` の Alternate ハンドラーも宣言する。既存の JSON 関連付けを強制変更しないため、必要なら対象ファイルだけの「情報を見る → このアプリケーションで開く」で選択する。「すべてを変更」は勧めない。アプリ側の OS 通知経路では `.arpeggio.json` 以外を拒否する。複合拡張子の自動判定・既定ハンドラー選択は実機確認が必要。
- **受信経路**: `App.OnFrameworkInitializationCompleted` で `IActivatableLifetime.Activated` を同期的に購読する。`FileActivatedEventArgs` の `IStorageItem` を `TryGetLocalPath()` で文字列に変換し、必ず Dispose する。`ProtocolActivatedEventArgs` の file URL も受け付ける。UI Dispatcher へ投稿することで初期ウィンドウ作成完了後に既存 Presenter の `Open` を呼ぶ。終了時は購読解除し、投稿済み処理の適用も止める。`Reopen` は最小化解除と前面化を行う。旧 `UrlsOpened` API は使わない。
- **文書の保護**: CLI の起動と OS 通知の双方が `MainWindowPresenter.Open` → `DawDocument.Open` を使う。既存の一文書構成を維持し、同一パスの通知は前面化だけ、別パスはドラッグ確定後に dirty を検査して未保存なら拒否する。複数ファイルの一括要求も拒否する。失敗は既存の `Execute` でステータス表示へ渡す。UI・Presenter・テーマのコードは変更しない。
- **引数なし起動**: 既存の `examples/snes-demo.arpeggio.json` を csproj の Content として出力へ同梱する。初回だけ `LocalApplicationData/Arpeggio/welcome.arpeggio.json` へコピーし、以後はその保存内容を開く。Finder の通常起動に必要な文書を確保し、読み取り専用の配布先や署名済みバンドルへ保存しない。CLI でファイルを指定した場合はコピーしない。

## .app の静的確認

- `bash -n`、`--help`、不正 RID／パストラバーサル／バージョン形式／ビルド番号／余分な引数の拒否 9 ケースを確認した。publish 経路は実行していない。
- `plutil -lint`、Python の plist 解析による置換後の名前・ID・実行ファイル名・バージョン・最小 OS・Retina・UTI 対応を確認した。csproj の XML と同梱デモの参照先も確認した。
- 既存 `.icns`／`.ico` の形式、SDL3 の各 RID のネイティブ資産と Windows の VC++ 依存を読み取り確認した。Avalonia／Core の使用型・namespace・公開 API をローカル NuGet XML と一次資料で照合した。
- イベント購読／解除、ストレージ項目の Dispose、終了後の投稿抑止、同一文書の再読み込み回避、dirty 時の切り替え拒否をセルフレビューした。追加の Unity lifecycle／`View.Presenter` はない。
- git 操作、コンパイル、`dotnet build`／`dotnet test`／`dotnet publish`、アプリ起動、osascript、署名コマンドの実行は行っていない。

確認元: [Avalonia の activation](https://docs.avaloniaui.net/docs/services/activatable-lifetime)、[FileActivatedEventArgs](https://api-docs.avaloniaui.net/docs/T_Avalonia_Controls_ApplicationLifetimes_FileActivatedEventArgs)、[Apple の plist キー](https://developer.apple.com/library/archive/documentation/General/Reference/InfoPlistKeyReference/Articles/CoreFoundationKeys.html)、[.NET 10 の対応 OS](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)、[VC++ ランタイム](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)、[Gatekeeper の初回許可](https://support.apple.com/ja-jp/102445)。

## .app 未完了

実装上の残タスクはなし。次の実行確認は依頼者側に残る。確認手順と期待結果は README の「配布ビルド」に記載した。

- `dotnet build Arpeggio.slnx` の警告ゼロ、既存テスト 676 件の全件成功。
- 4 RID の publish、macOS の ad-hoc 署名・検証・ZIP 展開後の起動、Windows ZIP と VC++ ランタイムの導入後の起動・アイコン・SDL3 音声出力。
- Finder の通常起動、未起動／起動済みでのファイルダブルクリック、Dock ドロップ、最小化からの復帰、osascript の `activate` と実際の前面ウィンドウの確認。
- 空白・日本語・`#` を含むパス、未保存の切り替え拒否、同一ファイル通知、複数ファイル拒否、不正ファイルの状態保持、保存・外部監視の継続。

## .app 変更ファイル一覧

- `tools/build_app.sh`（新規）
- `tools/macos/Info.plist`（新規）
- `tools/windows/README.txt`（新規）
- `src/Arpeggio.Daw/Program.cs`
- `src/Arpeggio.Daw/App.axaml.cs`
- `src/Arpeggio.Daw/Arpeggio.Daw.csproj`
- `.gitignore`
- `README.md`
- `docs/design.md`
- `docs/implementation.md`

# Avalon 統合 実装記録（2026-09-08）

## Avalon 統合の実装判断

- **任意の Debug 依存**: DAW の csproj 基準の `../../../Avalon/src/Avalon/Avalon.csproj` を `Exists()` で判定する。Debug と存在判定の同じ条件で ProjectReference と `AVALON` を設定し、呼び出し・using・統合クラスは `#if AVALON` で囲む。Release へ操作サーバー・ファイル監視・診断依存を含めず、配布ビルドを隣接開発リポジトリから独立させるため Debug に限定した。
- **起動順**: Avalon の実コードでは `UseAvalon` が `AfterSetup` からホストを起動し、通常は MainWindow / Presenter の生成より早い。`onStarted` で取得関数を登録し、観測時にインスタンスを解決する。生成前は動的キーを null とし、`daw.isReady` も公開する。起動時の初期読み込み成功後から登録するので、空の DawDocument を読まない。
- **取得元と副作用**: ソング・選択・再生は Presenter の現在の状態だけを読む。PianoRollPresenter には選択実数・選択 tick 文字列・固定スナップの public getter、TransportPresenter には再生状態・位置の public getter だけを追加した。判断・編集・ショートカットのロジックは変更していない。`song.isDirty` は既存の JSON 比較であり、大曲の高頻度観測では比較コストが残る。
- **トラック切替**: トラック番号は 0 始まり。文書 Opened の成功通知でキー数を同期し、SNES から GB などトラックが減る切替では古いキーを解除する。getter は観測のたびに現在の Presenter / Song を読むため、旧ソングの参照を保持しない。
- **診断ログ**: DawDocument の Open / Save が保存基準まで更新した後に成功イベントを通知する。外部変更の再読込・SFX の文書切替・同じパスへの再保存も既存経路から記録する。MainWindow の既存 ShowExportStatus 通知を購読して書き出しの開始・完了・失敗をログ化し、連続する同一表示を除外する。保存拒否を dirty の変化から推測して成功扱いすることはない。Presenter へログ処理を追加していない。
- **座標と要素名**: `PianoRoll` のローカル点 `(scrollOffsetX, scrollOffsetY)` を Window へ TranslatePoint した結果を可視原点とする。横幅は現行ズーム、半音高は既存定数、全体最上段は MIDI 127。DIP の計算式・可視範囲判定・スクロール後の再観測を docs/avalon.md に記載した。既存 x:Name と親子構造は維持し、ウィンドウ・タブ・スクロール・既存スナップ説明・動的トラック行と選択／ミュートへ名前だけを追加した。
- **寿命**: Program の finally で文書／View のイベント購読と状態キーを解除する。Avalon のデスクトップ lifetime による Dispose に加え、起動失敗でもホストを解放する。通常終了時の重複 Dispose は AvalonHost の既存の冪等性を利用する。
- **前提との差異**: 本 worktree の PianoRollPresenter は単一 SelectedTick を持ち、矩形選択・複数選択・コピペ・右ドラッグ連続削除の実装がない。MainWindow.axaml に音量レーン・スナップ選択もない。全 DAW ソースを検索して確認し、ユーザーへ実装場所を問い合わせた。編集挙動の変更禁止を優先して、存在しない状態やコントロールを捏造していない。

## Avalon 統合のテストコードと静的確認

- `DawObservationTests` を 8 ケース追加。ドラッグ中の読み取りで履歴・曲・正本・表示を変えないこと、解除／トラック切替／削除の選択追従、別編集経路で削除したノートの除外、再生状態取得で音声要求・表示更新が発生しないことを検証する。
- 文書通知について、読み込み成功後の状態と失敗時の無通知、正本保存後の通知・同一パスへの再保存、外部変更との競合拒否、書き込み失敗時の無通知と dirty 維持を検証する。
- Avalon の README / getting-started / ops-reference と src/Avalon の型定義・namespace・公開シグネチャを照合した。ドラッグの `modifiers` / `button` は依頼文では追加中とされていたが、参照時点の ActionExecutor / RoutedInputSender には実装が存在した。動作確認済みとは扱わない。
- Avalonia 12.1.2 のローカル API XML で TranslatePoint / ScrollViewer.Offset / Viewport を確認した。AXAML / csproj の XML、文書内 JSON 11 例、固定状態キー 22 件と文書の対応、public summary の隣接・ブロック namespace・括弧数・イベント解除を静的に確認した。参照とコンパイル定数の Debug＋Exists 条件一致も確認した。
- git 操作、dotnet build / dotnet test / コンパイル、DAW 起動は実行していない。Avalon を含む別リポジトリ、Core / CLI / MCP / Codecs / Formats は変更していない。

## Avalon 統合 未完了

- **FL 式編集の取り込み待ち**: 現状の選択数は 0 / 1 であり、複数選択検証の受け入れ条件は未達。実際の選択集合への getter 接続、昇順 tick の先頭 20 件＋総件数の表示、矩形選択・まとめて移動・右ドラッグ削除・コピペの実行確認が残る。docs/avalon.md の JSON は取り込み後の検証用で、貼付位置の決定規則も取り込み時に照合する。
- **音量レーン・スナップ選択 UI の取り込み待ち**: 対象要素が未実装のため名前を付けられない。既存の固定スナップ説明には SnapLabel を追加したが、選択 UI の代替とは扱わない。
- **依頼者側のビルド／テスト**: Debug＋Avalon あり、Debug＋Avalon なし、Release で `dotnet build Arpeggio.slnx` が警告ゼロ、追加 8 ケースを含む既存テスト全件成功。未実行のため受け入れ条件 1 は未検証。
- **依頼者側の実アプリ確認**: `.enabled` の有無と再起動、起動前の状態登録、ping / observe / act / logs、文書切替時のトラックキー増減、ズーム・縦横スクロール・リサイズ後の座標、ファイル読み込み／保存／書き出しログ、終了・初期化失敗時のホスト解放を確認する。

## Avalon 統合 変更ファイル一覧

- `src/Arpeggio.Daw/Arpeggio.Daw.csproj`
- `src/Arpeggio.Daw/Program.cs`
- `src/Arpeggio.Daw/Diagnostics/AvalonDawIntegration.cs`（新規）
- `src/Arpeggio.Daw/Editing/DawDocument.cs`
- `src/Arpeggio.Daw/Presenters/PianoRollPresenter.cs`
- `src/Arpeggio.Daw/Presenters/TransportPresenter.cs`
- `src/Arpeggio.Daw/Views/MainWindow.axaml`
- `src/Arpeggio.Daw/Views/MainWindow.axaml.cs`
- `src/Arpeggio.Daw/Views/TrackListView.axaml.cs`
- `tests/Arpeggio.Core.Tests/Daw/DawObservationTests.cs`（新規）
- `docs/avalon.md`（新規）
- `docs/design.md`
- `docs/implementation.md`
- `README.md`

## 提案

- 何を: 選択ノートの MIDI 音高・長さ・音量も上限付きで観測公開する。
  なぜ: tick と選択数だけでは、上下移動・リサイズ・音量編集が意図どおりか観測テキストで確定できない。
  見積もり: FL 式編集取り込み後に 1 ラン。
# M3-A1 / A2 実装記録（2026-09-08）

## M3-A 設計との差

- 設計書の変更は行わない。未指定の制御列 API は `ControlTimeline.Create(Song, ChipExportOptions)` とし、列（失敗時 null）と共通レポートを `ControlTimelineResult` で返す。ファイル用の Prepare / Write は後続ランに残す。
- 制御イベントには不変の元ノート情報・適用後の音程／正規化音量／デューティ・発音後の制御フレーム数を保持する。音色の固定パラメーターとトラック情報も不変コピーにし、可変な Song / Note / Instrument を外へ返さない。GB エンベロープ等の最終整数化は後続のレジスタ変換が担当する。
- 診断の全体件数・code 別件数は occurrenceCount の合計。保持明細だけを元位置と code で集約し、保持上限後の未保持発生数を別計数する。最大誤差は `MaximumError`（診断ごとの単位）、original / converted は文字列表現として補完する。strict は保持明細数に依存させない。
- VoiceModulation は別アセンブリの Formats から計算を共用するため public とする。既存の internal メンバーだけを公開し、明示 public コンストラクターと日本語 summary を追加する。InternalsVisibleTo・計算式変更・合成側の呼び出し変更は行わない。

## M3-A 実装範囲と判断

- **A1**: `Arpeggio.Formats` をソリューションへ登録し、参照は Core のみとした。共通 report / diagnostic / limits、ChipExportOptions、MidiImportOptions と必要 enum を追加した。MIDI の解析・音色変換・割り当ては E 系列で実装する。
- **診断**: warnings / errors は各 4096 明細まで。元ノートの同じ原因は発生数と最大誤差へ集約し、上限後の未保持数と code 別全数も残す。strict は WarningCount 全体で CanWrite を拒否し、limitations は除外する。明細保持上限はテキスト表示用途に規定値以下へ下げられる。
- **上限**: 有限 loops、展開後 1800 秒、元ノート 250000 件、メタデータの UTF-16 長・NUL・形式適合を制御列作成前に検査する。レジスタ数・VGM サイズ・NSF ROM／曲データ・MIDI 入力資源も共通の事前検証 API にした。ファイルや Stream への書き込み API はこのランでは追加していない。
- **A2**: SongValidator 成功後、JSON 往復で独立スナップショットを作る。SNES は複製前に拒否し、プリセット PCM / BRR の生成経路にも入らない。公開する列はイベント値・不変な元ノート／効果・トラック／音色情報だけとした。
- **走査**: 各トラックの元開始・Delay 後開始・元終端・周回開始の遅延列挙を、FrameClock の次境界と統合する。全境界の事前展開や毎サンプル走査をしない。使用中の列挙子は構築側の finally で破棄する。イベント数は元ノート数×最大周回数と演奏長×60 Hz×固定トラック数により有限に制限される。
- **更新順**: 既存 TrackSequencer の遷移を使い、グローバルフレーム更新後に交代を適用する。旧ノートの同時刻更新値は捨て、全トラックの Off → トラック順の On／継続更新とする。再発音のスライド期間は SongRenderer と同じ、丸めた残り tick のサンプル数から求める。
- **終端・衝突**: 曲本体の終端で空／ミュートを含む全チャンネルに停止を出す。正の発音期間が 0 サンプルへ潰れる場合は ControlEventCollision とし、部分列を返さない。曲全体が 0 サンプルへ丸められる場合も検査する。ミュートの制御列は終端停止だけとする。
- **後続への境界**: NES / GB の周期・最終整数音量・レジスタ副作用・DPCM 拒否は B / C、NSF の PLAY 時刻への量子化は D で扱う。共通 Volume はチップ固有 envelope / Wave 出力レベル適用前の V とし、GB 計算用の発音後フレーム数と固定音色設定を保持する。

## M3-A テストコードと既存出力の保持

`tests/Arpeggio.Core.Tests/Formats/` に 5 クラス、34 メソッド／59 ケースを追加した。アロケーション計測テストは追加していない。

| ファイル | 検証内容 |
|---|---|
| ConversionReportTests | strict と制限の区別、各 4096 明細の境界、明細保持 0 でも警告／エラーによる拒否、元ノート集約・最大誤差・全数／code 別数、読み取り専用ビュー |
| ConversionLimitsTests | options の既定値、loops 1/16、展開後 1800 秒、ミュート込み元ノート数、レジスタ数・VGM / NSF / MIDI サイズの両端、メタデータ・チップ適合 |
| ControlTimelineTests | Delay・残り期間・半サンプル丸め、フレーム途中 On、短音、同時二声の Off → On、有限二周と Delay 中の周回、既存シーケンサーの全サンプル観測との照合、ゼロサンプル衝突、ミュート・全停止、空一秒曲の 61 境界 |
| ControlModulationTests | 空マクロ・再発音、マクロ終端／LoopIndex・複合効果の固定値、GB envelope との分離、Triangle / Noise / Wave の配線 |
| ControlIsolationTests | 元 Song・Note・効果・マクロ・Wave 配列変更からの隔離、公開コレクションの不変性、三チップの変換前後の PCM 全ビット・JSON 全バイト・RenderReport 一致、アセンブリ依存方向 |

既存 PCM・JSON の保持は、依頼で許可された **既存レンダリング／JSON テストを変更せず通す方法**を採用した。既存 `SongRendererTests` の分割／一括／Seek 一致、各合成器テスト、`SongSerializerTests.RoundTripPreservesCanonicalUtf8Bytes` 等を維持し、上記の変換前後比較を加えた。ビルド・テスト実行が禁止されているため、M3 前 PCM の新規ハッシュ採取はしていない。新テストの前後比較は変換の非干渉を検証するもので、過去リビジョンの固定ハッシュとの比較ではない。

## M3-A 静的確認

- 自前型の定義・namespace と .NET 10 / xunit 2.9.2 のローカル参照 XML を照合した。C# の括弧対応、日本語 summary の隣接と XML、ブロック namespace、プロジェクト／ソリューション XML を検査した。
- 開始時のファイルハッシュと照合し、Core の既存変更は VoiceModulation だけ、既存テストコードはすべて不変であることを確認した。VoiceModulation の可視性・追加 summary・空の明示コンストラクターを取り除いた内容の SHA-256 は開始時と完全一致した。
- 設計書 2 ファイル、CLI / MCP / DAW / Codecs 群、Core の csproj・合成・シーケンサー・レンダラー・JSON 実装は開始時と同じバイト列。Formats の ProjectReference は Core だけ、PackageReference と InternalsVisibleTo の追加なし。
- Formats には Render / ReadSample / NoiseOscillator / PCM 配列／ファイル書き込みの呼び出しがないことを検索と呼び出し経路で確認した。Unity lifecycle / GetComponent / AddComponent の追加なし。VoiceModulation.Start は通常の状態初期化メソッド。
- git 操作、コンパイル、dotnet build / dotnet test、音声生成・アプリ起動は実施していない。

## M3-A 未完了

A1 / A2 の予定コードとテストコードは追加済み。受け入れ条件の実行確認は未完了で、依頼者側に残る。

- `dotnet build Arpeggio.slnx` のエラー・警告ゼロ。
- `dotnet test tests/Arpeggio.Core.Tests` の既存 676 件と今回の追加 59 ケースの成功。追加後の実際の検出件数も依頼者側で確認する。
- 既存レンダリング／JSON テストと今回の三チップ非干渉テストによる PCM・JSON・RenderReport の一致。依頼者が別途採取した M3 前の出力がある場合は、それとのバイト比較。
- 既存 4 クラスの AllocationCollection を含む、Render / NoteOn / 音色交換の GC 回帰確認。新たなアロケーション計測ケースは増やしていない。

## M3-A 変更ファイル一覧

更新:

- `Arpeggio.slnx`
- `src/Arpeggio.Core/Synthesis/VoiceModulation.cs`
- `tests/Arpeggio.Core.Tests/Arpeggio.Core.Tests.csproj`
- `docs/implementation.md`

新規:

- `src/Arpeggio.Formats/Arpeggio.Formats.csproj`
- `src/Arpeggio.Formats/ConversionFormat.cs`
- `src/Arpeggio.Formats/ConversionDiagnostic.cs`
- `src/Arpeggio.Formats/ConversionDiagnosticKey.cs`
- `src/Arpeggio.Formats/ConversionDiagnosticCollection.cs`
- `src/Arpeggio.Formats/ConversionReport.cs`
- `src/Arpeggio.Formats/ConversionLimits.cs`
- `src/Arpeggio.Formats/Export/ChipExportOptions.cs`
- `src/Arpeggio.Formats/Export/ControlEventKind.cs`
- `src/Arpeggio.Formats/Export/ControlEvent.cs`
- `src/Arpeggio.Formats/Export/ControlNote.cs`
- `src/Arpeggio.Formats/Export/ControlTrack.cs`
- `src/Arpeggio.Formats/Export/ControlInstrument.cs`
- `src/Arpeggio.Formats/Export/ControlTimeline.cs`
- `src/Arpeggio.Formats/Export/ControlTimelineResult.cs`
- `src/Arpeggio.Formats/Export/ControlTimelineBuilder.cs`
- `src/Arpeggio.Formats/Export/ControlTrackCursor.cs`
- `src/Arpeggio.Formats/Export/ControlBoundaries.cs`
- `src/Arpeggio.Formats/Midi/MidiImportOptions.cs`
- `src/Arpeggio.Formats/Midi/MidiPolyphonyMode.cs`
- `tests/Arpeggio.Core.Tests/Formats/ConversionReportTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/ConversionLimitsTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/ControlTimelineTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/ControlModulationTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/ControlIsolationTests.cs`

# M3-B1 実装記録（2026-09-08）

## M3-B1 設計との差

- 未指定の API は `NesRegisterCompiler.Compile(ControlTimeline, ConversionReport)` とし、A のレポートへ診断を追記して、不変の `RegisterTimeline`（エラーまたは strict 警告時は null）を返す。`RegisterWrite` に絶対サンプル位置・列全体での 0 始まりの順序・実機アドレス・byte 値を保持する。
- Noise / DPCM トラックはこのランでは無視する。DPCM 拒否・Noise・全レジスタの終端消音・PitchClamped 以外の変換警告の網羅は B2 に残す。対応する三声の終端 Off は既存制御列どおり処理する。
- On は enable → 制御値 → low → high を毎回書く。継続では副作用のない Pulse 制御値も変更時だけ書き、timer は low / high を独立比較する。On の同値 high と Triangle のフレームカウンター書き込みは省略しない。
- `PitchTable.GetRange` は現コードでは private。公開済みの `ClampMidiNote` / `GetFrequency` を使い、Core は変更しない。警告判定では周波数の連続範囲を比較し、MIDI→周波数→MIDI の微小誤差を誤ってクランプ警告にしない。

## M3-B1 実装範囲と判断

- `Export/RegisterTimeline` はチップ・書き込み列・終端サンプル位置を保持する。構築時に書き込みを独立配列へコピーし、読み取り専用ビューと getter のみの値型で公開する。元 Song・レポート・次の変換から変更できない。
- `NesRegisterCompiler` は呼び出しごとに独立した三声の状態と `$4015` shadow を作る。制御列が保証する全 Off → トラック番号順の On／更新を維持し、初期化を時刻 0 の先頭へ挿入する。
- 周期は `ClampMidiNote` の制限後に周波数へ戻し、Pulse は分母 16、Triangle は 32、ToEven の丸めと timer 8〜2047 を適用する。Pulse の最終音量は AwayFromZero、DutyCycle 1〜4 は bits 0〜3 に変換する。Triangle は音量を無視し固定 linear 値で起動する。
- sweep は両 Pulse とも `$08`。On は length index 0 を毎回ロードし、Pulse は halt / constant volume、Triangle は `$4008=$FF` と最後の `$4017=$C0` で保持する。他声の enable bit を変えずに On／Off を行う。
- `PitchClamped` は On と継続更新の両方で記録し、元トラック・元ノート番号・元 tick・出力トラック・変換前後の連続 MIDI 値を返す。`MaximumError` は半音単位。既存 report の集約で最大誤差と発生数を保持し、strict でも全更新の診断を集めた後に列の公開を拒否する。
- 書き込み列は `ConversionLimits.MaximumRegisterWrites` を超える前にエラーで停止し、部分列を返さない。成功時の件数は `registerWrites` に記録する。ファイルのサイズ算定・保存 API は追加しない。
- レジスタ定数は `NesRegisters`、各声の配置と直前値は `NesRegisterChannel` に置く。依存追加・合成器の呼び出し・PCM 生成・フロントエンドへの接続はない。

## M3-B1 テストコード

3 クラス、19 メソッド／34 ケースを追加した。生成したレジスタ列を直接検証し、PCM と外部エミュレータは使用しない。アロケーション計測テストは追加していない。

| ファイル | 検証内容 |
|---|---|
| NesRegisterCompilerTests | 初期化の順序・先頭無音・全順序番号、A4 の Pulse=253 / Triangle=126 と制御値、低音 timer=2033 と sweep 無効／target overflow 回避、三声の vibrato の low 差分と同音 On の high 強制、継続 high の両方向差分・同値保持・low 同値時の high 単独更新、duty と半整数音量、Triangle の起動順と Off 時の二声維持、三声同時交代の全書き込み順、非ゼロ loopStartTick の有限二周 |
| NesRegisterDiagnosticsTests | 三声それぞれの上下限 timer 8／2047、元位置と警告発生数、通常量子化での警告抑制、変調中の集約と最大誤差、明細保持ゼロの strict 拒否、ミュート、Noise／DPCM の現ランでの無視、チップ不一致 |
| RegisterTimelineTests | コレクションの変更拒否、元 Song・レポート・後続変換からの隔離、同じ制御列の再変換で状態が残らないこと、終端の保持、先行エラー時の部分列非公開 |

## M3-B1 静的確認

- 自前型の定義・namespace と .NET 10 のローカル参照 XML、既存 xUnit の使い方を照合した。追加 C# 全ファイルの括弧対応・summary XML・ブロック namespace・一ファイル一型・末尾空白を検査した。
- レジスタアドレスの名前付き定数、公開メンバーの日本語 summary、using の解決、初期化・共有 shadow・low／high 個別比較・On の強制書き込みを読み直した。A4・低音・high 境界の数値は生成コードの定数を流用せず算術で照合した。
- Render / ReadSample / NoiseOscillator / ファイル書き込み / Unity lifecycle の追加がないことを検索した。既存の Core・設計書・CLI／MCP／DAW／Codecs・既存テスト・プロジェクト参照は、作業開始時のハッシュと一致する。
- git 操作、Unity 起動、コンパイル、`dotnet build` / `dotnet test`、音声・NSF／VGM ファイル生成は実施していない。

## M3-B1 未完了

予定した実装とテストコードは追加済み。次の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` のエラー・警告ゼロ。
- 既存 778 件と追加 34 ケースのテスト成功、および実際の検出件数。
- 外部プレイヤー・実機での聴取と副作用の動作確認は未実施。自動テストの実行済み・実機検証済みとは扱わない。

## M3-B1 変更ファイル一覧

更新:

- `docs/implementation.md`

新規:

- `src/Arpeggio.Formats/Export/RegisterTimeline.cs`
- `src/Arpeggio.Formats/Export/RegisterWrite.cs`
- `src/Arpeggio.Formats/Export/NesRegisterCompiler.cs`
- `src/Arpeggio.Formats/Export/NesRegisterChannel.cs`
- `src/Arpeggio.Formats/Export/NesRegisters.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesRegisterCompilerTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesRegisterDiagnosticsTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/RegisterTimelineTests.cs`

# M3-C2 実装記録（2026-09-08）

## M3-C2 設計との差

- API と失敗時の契約は B1 と同じ `GameBoyRegisterCompiler.Compile(ControlTimeline, ConversionReport)` → 不変の `RegisterTimeline?` とする。エラーまたは strict 警告時は null とし、部分列を公開しない。
- 分割表に従い Noise と時間経過による Pulse envelope の増減は C3 へ残す。C2 の Pulse は `E=InitialVolume` を保持する。共通制御 V による音量変更には設計どおり DAC off／再 trigger を使う。この段階的制限は API と limitations にも明記する。
- Wave の On は routing 解除後に規定の RAM／DAC／trigger 順を実行し、trigger 後に routing を復元する。継続更新の high は trigger=0、Wave On の high は新周期と trigger を一度に書く。Pulse は low → high（trigger=0）で新周期を先に設定してから一定音量と trigger を書く。Wave の段階音量 0 は NR32=0 を維持し、DAC を止めるのは Off と RAM 更新時とする。
- パン診断は発音する各元ノートの On で一度記録する。Pulse／Wave の音量量子化と音域制限は各制御値を診断し、既存レポートで元ノートへ集約する。終端は対応三声の Off に続き、設計にある全体停止列を明示する（NR42=0 は Noise の発音実装を意味しない）。

## M3-C2 実装範囲と判断

- `GameBoyRegisterCompiler` は呼び出しごとに独立した三声の直前値と NR51 shadow を持つ。初期化は NR52=0 → NR52=$80 → NR50=$77 → NR51=0 → NR10=0 とし、以後 NR52 を書かない。length enable はリセット値 0 を保持し、全 frequency high／trigger 書き込みでも bit 6 を立てない。
- 周期は既存 `PitchTable.ClampMidiNote` の連続音域へ制限し、Pulse=131072、Wave=65536 の分子で ToEven の整数周期を求め、1〜2048 から register=2048−period へ写す。継続時の low／high は独立に比較し、trigger bit は shadow の周期値に混ぜない。
- Pulse の四種 duty は NR11／NR21 の上位 2 bit へ写す。最終音量は共通 V と初期 envelope 音量の積を AwayFromZero で整数化し、hardware envelope pace=0 とする。正の目標音量の変更では自声 routing 解除 → DAC off → 必要な周期差分 → 一定音量 → trigger → routing 復元とする。0 音量では DAC と routing を落とし、同じ 0 の継続では再書き込みしない。
- Wave On は同じ波形でも必ず全 16 byte を DAC off 中にロードし、偶数サンプルを上位ニブルへ詰める。NR32／NR33 → DAC on → NR34 trigger → routing 復元の順序を保つ。音量段階の境界を明示し、中点では小さい段階を選ぶ。継続の音量変更で RAM と trigger を更新しない。
- NR51 は左／右／両側の固定量子化とし、自声の二つのビットだけを置換する。個別 Off と Pulse 再トリガーで他声のパンを維持し、NR50 に声別音量の補償を持ち込まない。有限終端に NR51=0、NR12／NR22／NR42=0、NR30=0 を明示する。
- `PitchClamped`、`VolumeQuantized`、`WaveVolumeQuantized`、`PanReduced`、`EnvelopeRetriggered` を記録する。数値近似の最大誤差は順に半音、0〜15 レベル、0〜1 振幅、-1〜1 パンの単位。Pulse 音量の差は設計どおり 1e-9 以下を警告から除外し、通常の周期量子化は恒常的制限だけにする。
- `GameBoyRegisters` は実機アドレスとビット、`GameBoyRegisterChannel` は声の配置と直前値、`GameBoyRegisterValues` は量子化と元ノート診断を担当する。書き込みは 4000000 件を超える前にエラーとし、strict でも各制御値の診断を集めた後に列の公開を拒否する。

## M3-C2 テストコード

3 テストクラス、30 メソッド／86 ケースと共有テストデータを追加した。PCM を作らず、レジスタ列とドキュメントモデルを直接検証する。生成コードの定数はテストへ流用せず、固定アドレス・値を独立に置いた。

| ファイル | 検証内容 |
|---|---|
| GameBoyRegisterCompilerTests | 初期化・先頭無音・順序番号、両 Pulse の A4=1750 と四種 duty、Wave A4=1899 と全 RAM packing／起動順、NR32 全段階と三つの中点、Wave VolumeSlide、三声それぞれのパン ±1／±0.5001／±0.5／0 と元位置診断 |
| GameBoyRegisterTransitionTests | 三声の low／high 差分、high 単独変更、vibrato と同音 On の強制 trigger、音量／周期同時変更、0 音量保持と復帰、0 音量 On、全 Off → 全 On、各 Wave On の RAM 全ロードと DAC 状態の逐次観測、個別 Off の他声維持、length enable 無効、空曲／発音曲の終端、非ゼロ loopStartTick の有限二周 |
| GameBoyRegisterDiagnosticsTests | 三声の音域両端、通常周期量子化の警告抑制、変調中の最大誤差集約、初期 envelope との音量積と半整数丸め、C3 に残す時間 envelope、明細保持ゼロの strict 拒否、ミュート、Noise の段階的無視、元 Song／Wave 配列／レポート／次回変換からの隔離、読み取り専用列、先行エラーとチップ不一致 |
| GameBoyRegisterTestData | 150 BPM・1 フレーム=2 tick=735 sample の短い曲と、時刻ごとの書き込み観測を共用する |

## M3-C2 静的確認

- 設計書全文、M3-A／B1 の実装記録と既存 Formats／テスト、C# 規約を参照した。自前型の定義・namespace、.NET 10 の参照 XML、xUnit 2.9.2 の API を照合した。
- 追加 C# の括弧対応、summary XML、ブロック namespace、一ファイル一型、末尾空白を検査した。A4・オクターブ変更・high 単独変更の固定値は生成コードを実行せず独立した算術で照合した。
- 作業開始時のハッシュと比較し、既存ファイルの変更は `docs/implementation.md` だけであることを確認した。Core、両設計書、既存テスト、プロジェクト／依存定義は不変。Formats の依存は Core と BCL のままで、JSON version 1 の変更はない。
- 追加実装に Render／ReadSample／NoiseOscillator／PCM 生成、ファイル保存、Unity lifecycle の呼び出しはない。git 操作、Unity 起動、コンパイル、`dotnet build`／`dotnet test` は実施していない。アロケーション計測テストも追加していない。

## M3-C2 未完了

C2 の予定実装とテストコードは追加済み。受け入れ条件の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` のエラー・警告ゼロ。
- 既存 812 件と追加 86 ケースの成功。予定総数 898 件で、実際の検出件数は未確認。
- 外部プレイヤー・実機での聴取と副作用は未検証。C3 の Noise・時間経過によるソフトウェア envelope・VGM 接続は本ランの対象外であり未実装。

## M3-C2 変更ファイル一覧

更新:

# M3-B2 実装記録（2026-09-08）

## M3-B2 設計との差

- `SongValidator` が `Delay < DurationTicks` を保証するため、正常な制御列では初回演奏に全元ノートの On が現れる。DPCM は On の元ノートを事前走査し、有限周回による重複を除いて拒否する。制御列の型・Core・設計書は変更しない。
- 未指定の診断粒度を補完する。`PanReduced` は非ミュートの非ゼロ Pan を空トラックも含めトラックごとに一件、`UnsupportedDpcm` は元ノートごとに一件。`TriangleVolumeIgnored` は非 15 音量または VolumeSlide 指定を On ごとに一件とし、同じ元ノートの周回は既存 report で集約する。
- `PulsePhaseRestarted` は効果の種類によらず継続中に timer high を実際に書くたびに報告する。On の必須ロードは警告にしない。`VolumeQuantized` の誤差単位は 0〜15 のレベル、`PulsePhaseRestarted` は変更前後の high 値を記録し数値誤差は持たせない。
- 終端の制御列 Off を維持し、その後に `$4015=0` と Pulse 二声／Noise の volume=0 を必ず追記する。Pulse は最後の duty を保持し、未発音なら duty bits=0、halt / constant は維持する。同値の終端停止も省略しない。

## M3-B2 実装範囲と判断

- `NesRegisterCompiler` に Noise の `$400C/$400E/$400F` と enable bit 3 を追加した。変調後の値を 0〜127 に制限し ToEven で丸め、下位 4 bit を周期、音色の Short を bit 7 に詰める。継続時は音量／周期の変更分だけを書き、On では同値でも enable → control → period → length を保持する。
- Noise selection の制限・丸めは既存 Noise 合成の選択規則そのものであり、連続音程の損失を示す `PitchClamped` にはしない。LFSR seed の任意リセット不可、Pulse high の duty sequencer と timer divider の副作用の差を恒常的な制限へ追加した。
- DPCM はレジスタを生成する前に、非ミュートの元ノートの存在で拒否する。音量ゼロ・Delay 指定も除外しない。空トラック・未使用予約音色・ミュート済みノートは許可し、`$4015` の DMC enable は一度も立てない。元 Song の後編集と周回の重複に影響されない。
- 終端サンプル位置に全停止と三つの volume=0 を必ず書く。既存の全 Off → 全 On／更新、四声間の共有 shadow、On の length ロード順序を維持する。終端の書き込みも既存の 4000000 件上限と `registerWrites` 統計に含める。
- `NesConversionDiagnostics` にトラックの事前診断と、音量丸め・Triangle の音量省略・実際の Pulse high 更新の警告をまとめた。B1 の `PitchClamped` の診断構築も同型へ移し、判定式・元位置・最大誤差は維持した。元ノートの同原因を既存 report で集約し、strict は明細保持ゼロでも全警告数で部分列を拒否する。

## M3-B2 テストコード

5 テストクラスに **29 メソッド／65 ケース**と、独立したレジスタゲートモデルを追加した。合計は依頼の 812 件を基準に **877 件見込み**。件数は属性の静的集計であり、実際の検出・実行結果ではない。新規テストは PCM・音声合成器・外部エミュレーターを使用しない。

| ファイル | 検証内容 |
|---|---|
| NesNoiseRegisterTests | 両 mode 各 16 周期、0／127 と変調後の範囲外・半整数 ToEven・下位 bit 選択、On の順序、継続差分、音量ゼロから復帰、同値再発音、音色での mode 交代、ミュート、Delay と有限二周 |
| NesConversionDiagnosticsTests | Pulse／Noise の最終音量と最大誤差・発生数、全整数音量の丸め誤差抑止、半整数 AwayFromZero、Triangle 非 15 音量／VolumeSlide、パンの元位置・モノラル結果・空／ミュート、両 Pulse の vibrato による high 境界往復、Triangle との区別、各警告の strict・明細保持ゼロ、恒常的制限のみなら成功 |
| NesDpcmRejectionTests | 非ミュート DPCM の通常／strict 拒否、Delay・音量ゼロ、空トラック・未使用予約音色の許可、ミュートとスナップショット保持、二ノート・二周の重複除去、明細保持ゼロのエラー、DMC enable／アドレス／長さを開始しないこと |
| NesRegisterTerminationTests / NesRegisterGateModel | 四声の enable 前後の length load、Triangle high → 即時 linear load、他声を保持する個別 Off、四声同時交代の全 Off → 全 On、同値 length 再ロード、空／停止済み／終端まで発音する曲の停止列、音量ゼロと順序番号。ゲートモデルは生成サブセットの length index 0・共有 status・linear reload・Noise mode・Pulse 位相再開回数のみを独立して読む |
| NesEffectRegisterTests | Delay・PitchSlide・VolumeSlide・Vibrato・ノート Arpeggio と音色マクロを同時に適用した Pulse／Noise の手計算レジスタ値、Noise の LoopIndex と非ループ終端保持 |

B1 のテストは、暫定の Noise／DPCM 無視ケースを DPCM エラーへ更新した。Pulse 制御値の検証は終端消音の追加に合わせて時刻 0 を明示し、変調クランプの検証には新しい位相警告一件の検証を追加した。既存の期待値を削除して成功条件を緩和していない。

## M3-B2 静的確認

- Core・Formats の参照型と namespace、.NET 10 のローカル参照 XML（HashSet／Math／LINQ）、xUnit 2.9.2 の定義・既存使用例を照合した。レジスタ定数 34 件と診断メソッド 6 件の参照先を確認した。
- 変更・新規 C# の括弧対応、日本語 summary の隣接と XML、ブロック namespace、一ファイル一型、末尾空白、禁止省略名、Unity lifecycle／コンポーネント API の不使用を確認した。固定レジスタ期待値は算術でも照合した。
- 作業開始時のファイルハッシュと比較し、`src/Arpeggio.Core/`、設計書二つ、制御列の型、プロジェクト依存、CLI／MCP／DAW／Codecs、および B1 の対象二ファイル以外の既存テストが不変であることを確認した。NuGet・永続項目・JSON version は追加／変更していない。
- git 操作、コンパイル、`dotnet build`／`dotnet test`、アプリ起動、PCM・NSF／VGM ファイル生成は実施していない。指定作業ディレクトリ外への書き込みは行っていない。

## M3-B2 未完了

予定した実装とテストコードは追加済み。次の受け入れ条件の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` の警告・エラーゼロ。
- 既存 812 件を含む全件と追加 65 ケースの成功、実際のテスト検出件数の確認。
- 独立ゲートモデルの副作用検証と既存 PCM／JSON／GC 回帰の実行。新しいアロケーション計測テストは追加していない。
- 外部プレイヤー・実機の聴取／動作確認は未実施。レジスタ再合成と NSF／VGM の相互比較は設計済みの H 系列に残る。

## M3-B2 変更ファイル一覧

更新:

- `src/Arpeggio.Formats/Export/NesRegisterCompiler.cs`
- `src/Arpeggio.Formats/Export/NesRegisters.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesRegisterCompilerTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesRegisterDiagnosticsTests.cs`
- `docs/implementation.md`

新規:

- `src/Arpeggio.Formats/Export/GameBoyRegisterCompiler.cs`
- `src/Arpeggio.Formats/Export/GameBoyRegisters.cs`
- `src/Arpeggio.Formats/Export/GameBoyRegisterChannel.cs`
- `src/Arpeggio.Formats/Export/GameBoyRegisterValues.cs`
- `tests/Arpeggio.Core.Tests/Formats/GameBoyRegisterCompilerTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/GameBoyRegisterTransitionTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/GameBoyRegisterDiagnosticsTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/GameBoyRegisterTestData.cs`
- `src/Arpeggio.Formats/Export/NesConversionDiagnostics.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesNoiseRegisterTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesConversionDiagnosticsTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesDpcmRejectionTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesRegisterTerminationTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesRegisterGateModel.cs`
- `tests/Arpeggio.Core.Tests/Formats/NesEffectRegisterTests.cs`
