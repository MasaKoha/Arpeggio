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

# M3-E1 / E2 実装記録（2026-09-08）

## M3-E 設計との差

- 設計書は変更しない。未指定の中間 API は `MidiReader.Read(Stream, ConversionReport)` → 不変 `MidiFile`（入力エラー時 null）、`MidiNoteCollector.Collect(MidiFile, ConversionReport)` → 不変ノート列とする。診断は同じ MIDI レポートへ追記する。strict は保存可否を拒否するが、中間解析は警告後も継続し、後続の全診断を収集できるようにする。既存エラーがある場合は部分結果を返さない。
- E1 の実時間上限を E3 待ちにしないため、安定整列した Tempo と EOT に対する checked 整数分子の時間積算だけを E1 で実装する。出力 BPM・量子化・テンポ診断・任意時刻照会は E3 に残す。
- 「既知固定長 meta」は Sequence Number=2、Channel Prefix=1、MIDI Port=1、EOT=0、Tempo=3、SMPTE Offset=5、Time Signature=4、Key Signature=2 byte と補完する。未使用 payload は長さ検証後に読み捨て、全イベントの元番号と資源計数には含める。
- 音量 0 の On も FIFO 対応用に保持し、完成ノートの公開時に省略する。sustain 中にキーを離した旋律も入力終端まで鳴っていれば `UnclosedNote` の対象とする。打楽器は元 Off の有無と CC120 の上限 tick を別に保持し、固定実時間 gate の確定は E5 に残す。

- E2 の補完判断: 打楽器の同 tick On / Off は「元 gate を音長に使わない」専用規則を優先して保持する。同 tick CC120 は最終 gate も 0 になるため破棄する。`ControllerDuringNoteIgnored` は E2 では完成済みの旋律区間に対して確定し、同 tick 終端・ゼロ長・音量 0 を除く。打楽器は元 Off 後も固定 gate が鳴るため、固定 gate が確定する E5 で元 MIDI の CC7 / CC11 / CC121 と照合する必要がある（本ランでドラム表・実時間 gate を先行実装しない）。

## M3-E1 実装・静的確認

- SMF format 0 / 1、PPQN、MThd 拡張、MTrk 宣言数、未知 chunk、全 channel message 長、トラック独立 running status、SysEx / meta 後の status リセット、4 byte VLQ、既知固定長 meta、必須 EOT / 後続禁止、port 0 制約を実装した。
- 入力はシーク不要。総 byte とチャンク残量を検証し、未使用 payload は固定 4096 byte バッファで読み捨てる。入力 32 MiB・イベント 1000000 件・正 velocity の On 250000 件・実時間 1800 秒を拒否境界にした。Stream は閉じず、I/O 例外を伝播する。
- channel message / Tempo / EOT を不変の元位置付きイベントとして安定整列する。スキップしたイベントも資源数・元イベント番号に含める。track 0 の最初の非空名を strict UTF-8 / Latin-1 で復号する。
- 正常・不正入力・資源上限のテストコードを追加。全 byte 切断、短い読み取り、読み取り失敗、最大許容量、同 tick の別トラック Tempo、32 bit 超の tick を含む。1800 秒の固定 VLQ は独立計算で E9 BC 00 と照合した。
- E1 のコード・テスト作成と静的読解を先に終えてから E2 に着手した。コンパイル・テスト成功を確認済みとは扱わない。

## M3-E2 実装・判断

- `MidiNoteCollector` は reader の確定順を変更せず処理する。全 MTrk に共有される 16 channel の Program / CC7 / CC11 / sustain と、pitch ごとの FIFO を持つ。完成列は On の元順序で返し、次段の量子化・声割り当てに必要な出自を保つ。
- velocity 0 は Off。同音再打鍵、別 MTrk の Off、pedal 中の解放音と押下キーを区別する。CC120 は pedal を無視して当該 channel を即停止、CC123 は全キーの Off、CC121 は CC7=100 / CC11=127 / sustain off とし Program・押下キーを維持する。
- 旋律の未終了音は曲入力終端で閉じ、不明 Off と元ゼロ長も診断する。音量 0 の On を FIFO の途中から消さないため、後の Off が別の有音 On を停止することを防ぐ。
- On 時点の Program / CC7 / CC11 / velocity、4 bit 化前の実効音量と整数 Volume を不変コピーする。実効音量は整数積を作ってから共通分母で割り、同じ積の発音の優先順位が浮動小数点演算順で変わることを防ぐ。
- 発音中の CC7 / CC11 / CC121 の音量変更は次の On から適用する。完成旋律区間と CC 列を線形走査し、同 tick 終端・元ゼロ長・無音・別 channel を `ControllerDuringNoteIgnored` に誤算入しない。pan、非零 bank、中央以外の bend、非零圧力／CC1、その他の非対応 CC を規定コードで診断する。
- 打楽器の元 Off と CC120 上限を分離し、元 Off 後に届く CC120 も最初の一回だけ保持する。元 gate の欠落に旋律用の不明 Off／未終了警告を付けない。ドラム表・音色・実時間固定 gate・DrumGateReplaced は E5 に接続する。
- FIFO の On は一度だけ取り出す。pedal 解放・全キー Off・全音停止は対象の発音だけを処理し、全履歴を CC ごとに再走査しない。公開コレクションは独立配列の読み取り専用ビューとした。

## M3-E テストコード

7 テストクラス、56 メソッド／164 ケースと、入力作成・シーク不能 Stream の補助 2 型を追加した。ケース数は属性から静的集計した値であり、テストランナーの検出件数ではない。

| ファイル | 検証内容 |
|---|---|
| MidiReaderTests | format 0 / 1、PPQN 1 / 480 / 32767、全 channel message と running status、16 channel、元イベント番号、MTrk 安定統合、拡張 header／未知 chunk、最大 VLQ・32 bit 超 tick、固定長 meta、Track Name 復号、Stream 所有権・I/O・不変性 |
| MidiReaderInvalidInputTests | 非対応 header／division／system status、track count、5 byte VLQ、データ MSB、running status の境界、meta 長、EOT、port 非零、過大長、全 byte 切断、重複／過不足 chunk、先行エラー |
| MidiReaderLimitsTests | 256 MTrk、32 MiB と 1 byte 超過、全 meta 込み 1000000 イベント、250000 On、1800 秒の両側、checked 実時間分子の overflow、別トラック・同 tick Tempo |
| MidiNoteCollectorTests | 同音 FIFO、MTrk 横断、channel 独立、velocity 0、最遅 EOT、不明 Off と元位置、元ゼロ長・同 tick 順、strict の全診断、収集間の状態分離・不変性 |
| MidiSustainTests | pedal 閾値・再打鍵・押下キーの維持、別 MTrk・最遅 EOT、CC120 他声維持、CC123、CC121 の既定値復元と Program 維持、同 tick pedal 順 |
| MidiControllerTests | velocity 1 / 64 / 127、CC7×CC11、最低 1、無音 On の FIFO、同 tick Program／CC、完成区間に基づく警告、sustain 中 CC、pan／bank／bend／圧力／RPN 等、同音量積の完全一致 |
| MidiDrumCollectionTests | 不明 Off／未終了の警告除外、元ゼロ gate の保持、sustain／CC123、元 Off 後の CC120 上限、同 tick 即停止 |

PCM、レジスタ再合成、外部エミュレータ、ファイル保存を新規テストの経路に入れていない。アロケーション計測テストは追加していない。

## M3-E 静的確認

- 追加型の定義・namespace、使用する BCL の .NET 10 ローカル参照 XML、既存の xUnit 呼び出しを照合した。C# の括弧対応、summary XML、公開メンバーへの summary 隣接、ブロック namespace、末尾空白を検査した。
- 開始時の SHA-256 と比較し、既存ファイルの変更は `docs/implementation.md` だけ。Core・設計書 2 ファイル・既存 Formats・既存テスト・プロジェクト参照を変更していない。Formats の依存は Core と BCL のまま。
- git 操作、Unity 起動、コンパイル、`dotnet build` / `dotnet test`、PCM 生成は行っていない。開始から 30 分以内で作業を終了した。

## M3-E 未完了

- E1 / E2 の予定実装と上記テストコードは追加済み。受け入れの実行確認は依頼者側に残る。`dotnet build Arpeggio.slnx` のエラー・警告ゼロ、既存 812 件と追加 164 ケース（静的集計）の全成功・実際の検出件数は未確認。
- 打楽器の固定実時間 gate 内の `ControllerDuringNoteIgnored` は E5 で確定する。E2 では元イベント・On 設定・元 Off・CC120 を保持した。上記「設計との差」に記した分担判断は依頼者の確認対象。
- E3 の任意 tick 実時間照会・出力 BPM・テンポ診断・量子化、E4 の声割り当て、E5 の音色／ドラム固定 gate、E6 の Importer・Song 検証・JSON 保存は本ランの対象外。

## M3-E 変更ファイル一覧

更新:

- `docs/implementation.md`

新規実装:

- `src/Arpeggio.Formats/Midi/MidiReader.cs`
- `src/Arpeggio.Formats/Midi/MidiBinaryInput.cs`
- `src/Arpeggio.Formats/Midi/MidiReadException.cs`
- `src/Arpeggio.Formats/Midi/MidiDurationValidator.cs`
- `src/Arpeggio.Formats/Midi/MidiMessageKind.cs`
- `src/Arpeggio.Formats/Midi/MidiEvent.cs`
- `src/Arpeggio.Formats/Midi/MidiFile.cs`
- `src/Arpeggio.Formats/Midi/MidiNoteCollector.cs`
- `src/Arpeggio.Formats/Midi/MidiChannelState.cs`
- `src/Arpeggio.Formats/Midi/MidiPendingNote.cs`
- `src/Arpeggio.Formats/Midi/MidiNote.cs`

新規テスト・補助型:

- `tests/Arpeggio.Core.Tests/Formats/MidiReaderTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiReaderInvalidInputTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiReaderLimitsTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiNoteCollectorTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiSustainTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiControllerTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiDrumCollectionTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiFileFixture.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiFragmentedStream.cs`
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

# M3-D1 / D2 実装記録（2026-09-08）

## M3-D1 / D2 設計との差

- 未指定の API は `NsfFrameCompiler.Compile(ControlTimeline, ConversionReport)` → `NsfFrameTimeline?`、`NsfDataEncoder.Encode(NsfFrameTimeline, ConversionReport)` → `NsfEncodedData?` とする。PLAY 単位の書き込みは `NsfRegisterWrite` でサンプル単位と区別する。既存 NES 変換へ渡す内部列だけ、PLAY 時刻に最も近い整数サンプルで表す。この代表サンプルの再量子化は必ず元 PLAY 番号へ戻り、誤差診断は代表サンプルを介さず元サンプル対 PLAY 時刻で計算する。
- 同フレームの On 後の更新は最終制御値を持つ On 一つへ集約し、Off 前の継続更新は停止で置き換える。削除した制御値ごとに `ControlUpdateCoalesced` を記録する。通常の 60 Hz 更新間隔は PLAY より長いため、更新同士だけの衝突は通常入力では発生しないが、同じ集約規則で扱う。On／Off 衝突は集約前に拒否する。
- ドライバーの独立 CPU 実行器は設計の分割表どおり D3、NSF ヘッダー・ROM 配置とファイルの受理／予算拒否の統合は D4 に残す。D2 では命令列・ラベル・生成コードに基づく保守的な静的上限を提供する。

## M3-D1 実装範囲とテストコード

- `NsfTiming` は checked 整数式で絶対時刻を量子化する。`NsfFrameCompiler` は元ノート位置付きの時刻誤差（µs）・衝突・集約診断を返し、全 Off → トラック順 On／更新を維持して既存 NES コンパイラーへ渡す。元の制御列・Song・レジスタ変換規則は変更しない。
- `NsfDataEncoder` は時刻順・許可アドレス・1800 秒相当の終端を検証し、サイズ算定と上限確認後にだけデータ配列を確保する。WAIT は 1〜65535、END は一つ。同値書き込みも順序どおり保持する。予定サイズはヘッダー＋固定 bank＋4 KiB 整列データ bank、データ型は不変コピーとする。
- `NsfFrameCompilerTests` は絶対時刻・半 PLAY 境界・三周の誤差・短音拒否・二重 On・隣接 Off／On・On 後のマクロ集約・Off 前の更新抑止・strict／明細ゼロ・元 Song 保持を検証する。
- `NsfDataEncoderTests` は WAIT 0／1／65535／65536、先頭無音・終端・同値書き込み、4 KiB 境界・ROM 上限、実現可能なデータ上限直前／直後、禁止アドレス・不正時刻・逆順・strict・不変性を固定値で検証する。データ長は 3n+1 なので、曲データ容量 1044480 に対する直前／直後は 1044478／1044481 byte となる。

## M3-D2 実装範囲と命令列

- `NsfDriverBuilder` は 123 命令と許可アドレス表 24 byte、合計 **290 byte** を生成する。残り 3806 byte をゼロ埋めして bank 0 を 4096 byte とする。INIT=`$8000`、PLAY=`$8064`、ReadByte=`$80E5`、許可表=`$810A`。コードは曲内容に依存しない。
- `NsfCodeBuilder` は限定命令のラベルを解決し、未定義ラベル・相対分岐の範囲外・固定 bank 超過を生成エラーにする。長距離の条件分岐は「逆条件で直後の JMP を飛ばす」命令列とし、相対分岐の暗黙の切り詰めは行わない。`NsfDriverImage` は実バイト・実アドレス・解決済み命令列・ラベルを不変ビューで公開する。
- 使用 opcode は `05 18 20 38 4C 60 85 8D 90 9D A0 A5 A9 AA B1 BD C6 C9 D0 E6 F0`。`NsfOpcode.None=0` は生成不可で、BRK として扱わない。IRQ／DMA／PPU／SP 変更／ROM 自己書き換えは生成しない。命令長・サイクルの参照は設計の [NESdev 命令仕様](https://www.nesdev.org/wiki/Instruction_reference) と [6502 命令表](https://www.nesdev.org/obelisk-6502-guide/reference.html)。この環境から本文の直接取得は 403 だったため、検索可能な記載と命令表の固定値を照合した。独立 CPU での実行照合は D3 に残る。

作業 RAM は `$00/$01`=データポインター、`$02`=bank、`$03/$04`=残り PLAY 間隔、`$05`=終端状態。INIT は予約分を含む `$00–$1F` をゼロにし、APU 初期化後にポインター `$9000` と bank 1 を明示設定する。SP は操作せず、ReadByte の JSR は成功・失敗とも RTS で対になって戻る。

| 経路 | 命令列の要点 |
|---|---|
| 再 INIT | `LDA #0; STA $00 … STA $1F` → `$4015/$4010/$4011=0`、sweep=`08`、frame counter=`C0` → `$01=90; $02=1; $5FF9=1; RTS` |
| PLAY 入口 | `$05 != 0` なら RTS。待機が正なら、low=0 のとき high を DEC してから low を DEC。16 bit 全体が 0 になった呼び出しでだけ NextCommand へ進む |
| WRITE | ReadByte → offset が `$18` 未満かつ許可表で 1 か確認 → X に offset → ReadByte → `STA $4000,X` → NextCommand |
| WAIT | ReadByte を二回使い `$03/$04` へ little-endian で格納。0 は Fail、正ならそのまま RTS。WAIT=1 の次の PLAY が次群を実行する |
| END | `$05=1; RTS`。以後の PLAY はデータを読まず RTS。正常 END では直前の停止済み APU 状態を保持する |
| Fail | `$4015=0` → END と同じ停止状態を設定。未知データ命令、不正アドレス、WAIT=0、bank 255 越えを防御的に停止する |
| ReadByte | 読み出し前にポインター high=`A0` を検査。bank=`FF` なら SEC／RTS。それ以外は bank を増やして `$5FF9` だけへ書き、high=`90`。`LDY #0; LDA ($00),Y; INC $00`、桁上がり時だけ `INC $01`、CLC／RTS |

読み出し後に bank を変更しないため、命令／オペランド途中の `$9FFF` 越えも次 byte から新 bank となる。bank 255 の最終 END は high が `A0` になっても正常に返り、次のデータ読み出しを行わず終了する。ReadByte は読み出した A とオフセット X をカーソル更新で壊さず、Y=0 と carry による成功／失敗を呼び出し契約に使う。

## M3-D2 静的サイクル上限

`NsfCycleAnalyzer` が解決済み命令グラフを走査する。各条件分岐は常に 4 cycles、間接 Y 読み出しは 6 cycles、絶対 X 読み出しは 5 cycles として、成立しない同時ページ越えも許した保守的上限を取る。JSR／RTS を含め、境界以外の循環は生成エラーにする。

| 項目 | 上限 cycles |
|---|---:|
| INIT／再 INIT | **146** |
| ReadByte（JSR 自体を除く） | 65 |
| PLAY 入口／待機減算 | 53 |
| WRITE 一回（dispatch・3 byte 読み出し・bank 越え・末尾 JMP 込み） | 257 |
| 最後の WAIT／END／防御停止 | 270 |
| PLAY 全体 | **323 + 257 × 最大同一 PLAY 書き込み数** |

通常の四声同時 On＋初期化の 23 書き込みは **6234 cycles**。29 書き込みは **7776 cycles** で受理し、30 書き込みは **8033 cycles** で `NsfCpuBudgetExceeded` とする。INIT 20000／PLAY 8000 の両方を生成時に検証し、レポートの `maximumInitCycles`／`maximumPlayCycles` へ返す。D4 のファイル保存経路はこの受理結果を利用する。これらは命令グラフの上限であり、実行器で測定した値ではない。

`NsfDriverBuilderTests` は実 byte 列の限定 opcode／長さ／最大 cycles、全ラベルと命令境界、固定 bank・ゼロ埋め、再 INIT・待機借り下がり・終端・byte 読み出し・許可表の固定バイト列、静的予算境界、再生成の決定性・不変性・strict を検証する。

## M3-D1 / D2 静的確認

- 指定設計書全文、M3-A／B1／B2／C2 記録、既存制御列・レジスタ列・NES／GB コンパイラーと規約を参照した。自前型の定義・namespace、.NET 10 のローカル参照 XML（ArgumentNullException／Array／Dictionary）、既存 xUnit の API を照合した。
- 変更 C# 18 ファイルの括弧対応・日本語 summary XML と public メンバーへの隣接・一ファイル一型・ブロック namespace・末尾空白を検査した。禁止省略名、Unity lifecycle／コンポーネント API、Render／ReadSample／NoiseOscillator／Subscribe の追加はない。
- C# の命令生成記述を静的に展開して、全ラベルのアドレスと相対分岐範囲、命令長、上限式を別の算術集計で照合した。使用量 290 byte、123 命令、INIT 146／ReadByte 65／入口 53／WRITE 257／終端 270 cycles と一致した。C# のコンパイル・生成プログラムの実行・CPU 実行テストを行ったという意味ではない。
- テストは **3 クラス、25 メソッド／51 ケース**を追加した。四声の制御列→量子化→データ符号化→ドライバー生成の接続、Delay・ミュート・DPCM 拒否も含む。既存 963 件を基準とした予定総数は **1014 件**で、検出件数と成功は未確認。新規テストは PCM を生成せず、アロケーション計測も追加していない。
- 作業開始時のハッシュと比較し、既存ファイルの変更は `ControlTimeline.cs` の内部コピー用コンストラクター追加と本記録だけ。Core、設計書二つ、既存テスト、プロジェクト依存とフロントエンドは不変。Formats の依存は Core と BCL のみ、JSON version 1 は維持した。
- git 操作、Unity／アプリ起動、コンパイル、`dotnet build`／`dotnet test`、PCM／NSF ファイル生成は実施していない。指定作業ディレクトリ外への書き込みはない。

## M3-D1 / D2 未完了

D1／D2 の予定実装とテストコードは追加済み。受け入れ条件のうち次の実行確認は依頼者側に残る。実行済み・全受け入れ済みとは扱わない。

- `dotnet build Arpeggio.slnx` の警告・エラーゼロ。
- 既存 963 件と追加 51 ケースの全件成功、および実際の検出件数の確認。
- D3: 独立した限定 6502 実行器による flags／stack／分岐／cycles の検証、生成 INIT／PLAY の実行トレースと再 INIT・待機・終端の確認。
- D4: `NsfWriter`・ヘッダー・bank 配置・独立ロードと保存可否の統合。命令／オペランド／WAIT 途中の 4 KiB 越え、bank 255 最終 END と越境停止、実サイクル数が静的上限以下であることの実行確認。
- 実機・外部プレイヤーでの互換性・聴取確認は未実施。

## M3-D1 / D2 変更ファイル一覧

更新:

- `src/Arpeggio.Formats/Export/ControlTimeline.cs`
- `docs/implementation.md`

新規（Formats）:

- `src/Arpeggio.Formats/Export/NsfTiming.cs`
- `src/Arpeggio.Formats/Export/NsfRegisterWrite.cs`
- `src/Arpeggio.Formats/Export/NsfFrameTimeline.cs`
- `src/Arpeggio.Formats/Export/NsfFrameCompiler.cs`
- `src/Arpeggio.Formats/Export/NsfDataFormat.cs`
- `src/Arpeggio.Formats/Export/NsfEncodedData.cs`
- `src/Arpeggio.Formats/Export/NsfDataEncoder.cs`
- `src/Arpeggio.Formats/Export/NsfOpcode.cs`
- `src/Arpeggio.Formats/Export/NsfInstructionSet.cs`
- `src/Arpeggio.Formats/Export/NsfDriverInstruction.cs`
- `src/Arpeggio.Formats/Export/NsfCodeBuilder.cs`
- `src/Arpeggio.Formats/Export/NsfCycleAnalyzer.cs`
- `src/Arpeggio.Formats/Export/NsfDriverImage.cs`
- `src/Arpeggio.Formats/Export/NsfDriverBuilder.cs`

新規（Tests）:

- `tests/Arpeggio.Core.Tests/Formats/NsfFrameCompilerTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfDataEncoderTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfDriverBuilderTests.cs`
# M3-E3 / E4 実装記録（2026-09-08）

## M3-E3 / E4 設計との差

- 設計書は変更しない。未指定の中間 API は `MidiTempoMap.Create`、`MidiTickQuantizer.Create` と単音の量子化、不変 `MidiQuantizedNote` とする。実時間は PPQN を分母とする checked 64 bit 整数分子で渡し、固定実時間 gate を E5 から指定できるようにする。最終グリッドへの除算だけ decimal を使用し、区間ごとの丸めを避ける。
- `PitchTable.GetRange` は private のため、Core を変更せず公開 `ClampMidiNote` の MIDI 0 / 127 に対する結果から整数音域を得る。SNES は別途 sampleRate / root を含む未クランプのレジスタ値で音域を判定する。
- E4 は音色 ID 採番前の不変割り当て結果を返す。全 ChipLayout トラックと元ノートを保持し、音色生成・ID 採番・Song 構築は E5 / E6 に残す。SNES のドラム予約は明示除外前の有効な入力 On の有無で決める（除外は候補だけを上書きする）。

## M3-E3 / E4 未完了

- E3 / E4 の予定実装とテストコードは追加済み。実行を含む受け入れ確認は未完了。依頼者側で `dotnet build Arpeggio.slnx` のエラー・警告ゼロと、既存 1127 件＋追加テストの全成功を確認する必要がある。今回の追加は静的集計で 6 テストクラス・48 メソッド・113 ケースであり、ランナーの検出件数は未確認。
- E5 は `MidiVoiceNote` に変換音程／Noise selection・`MidiDrumPriority`・選択サンプルの `MidiPitchRange` を渡す。打楽器終端は `map.GetTimeNumerator(OnTick) + gateMicroseconds × map.TicksPerQuarterNote` と CC120 の早い方を量子化器へ渡す。ドラム表・音色生成・固定 gate 内の CC 警告・採用順の音色 ID 採番は今回の対象外。
- E6 は不変 `MidiVoiceTrack` 全列から Song を構築し、Muted=false・Pan=0・DefaultInstrumentId=null・全 Note の InstrumentId・LoopStartTick=0 を設定する。曲長には割り当て後の全 `EndTick` を `GetLengthTicks` へ渡す。最終曲長の資源検証、SongValidator、JSON version 1 保存・往復は E6 に残す。量子化／短音延長による終端移動は保持し、上限付近を無断で切り詰めない。

## M3-E3 実装・静的確認

- `MidiTempoMap` は同 tick の最後の Tempo を選び、非 conductor・競合・実効変化・基準 BPM 整数化を診断する。既定 500000 µs、明示 BPM 1〜1000、checked 64 bit 共通分子、二分探索による任意 tick 照会を実装した。E1 の入力上限検証は維持した。
- `MidiTickQuantizer` は 48 の約数 grid を検証し、開始／終了／入力 EOT を独立して絶対量子化する。正の短音だけを延長し、最大誤差を出力 tick 単位で元発音へ集約する。曲長は割り当て後終端列と EOT・最小 48 から計算する。打楽器は E5 の確定実時間終端を受け、元 Off を誤って量子化しない。
- E3 の実装とテストコード作成、名前空間・API・括弧対応・境界式の静的読解を終えてから E4 に着手した。0/48/144、端数 BPM、同 tick 競合、先頭無音、1800 秒、10000 音の端数区間、全グリッド、短音・元ゼロ長・EOT・固定実時間 gate のテストを追加した。コンパイル／実行成功は未確認。

## M3-E4 実装・静的確認

- `MidiChannelCandidates` はチップ別の自動候補を作り、指定 ch だけを明示 map で上書きする。候補配列はコピーして昇順化し、重複・範囲外・DPCM・NES / GB の旋律／Noise 相互指定を未使用 ch も含めて拒否する。複数 ch の候補共有と空配列の明示除外を扱う。
- `MidiVoiceAllocator` は確定した gate を開始 tick、ドラム優先／分類、実効音量、旋律 pitch、ch、元 MTrk／event の規定順で配置する。終了済み候補を先に使い、steal-oldest / drop-new と同時 On 保護を実装した。打ち切りは不変結果を置き換えて反映し、元ノートは変更・再開しない。
- `MidiPitchRange` は NES Pulse=33〜126、Triangle=21〜114、GB Pulse=36〜127、Wave=24〜127 の整数端点を使う。SNES は未クランプ pitch の四捨五入結果が 1〜16383 となる音を調べ、root=60 の 32 kHz=0〜83、16 kHz=0〜95、24 kHz=0〜88 等を固定テストにした。Noise selection はクランプしない。Triangle だけ Volume=15 を反映して診断する。
- 全 ChipLayout トラックを音色 ID 採番前の不変列で保持し、採用数・破棄数・打ち切り数・明示除外 ch／ノート数・実際の ch→出力トラック別件数を report に残す。全音消失は `NoPlayableNotes` として部分結果を返さない。strict 警告だけでは残りの割り当て診断を止めない。
- NES 四和音、GB 全候補、SNES 6＋2／8 声、量子化で同時になった On、同分類ドラムの順序、後発打撃、候補共有・除外、全チップ／両モードの 160 音列の正の長さ・昇順・非重複・再実行決定性をテストコードで検証対象にした。E5 のドラム表は実装せず、分類値をテストから直接与えている。

## M3-E3 / E4 静的確認と未実行の確認事項

- 既存の型定義・namespace と BCL の .NET 10 ローカル参照 XML を照合した。公開宣言の summary 隣接、日本語 XML コメントの整形式、括弧対応、ブロック namespace、末尾空白を静的に検査した。音域端点と MIDI 時間の固定値は独立した式でも照合した。
- 開始時の SHA-256 と比較し、既存ファイルの変更は `docs/implementation.md` だけ。設計書 2 ファイル・Core・既存 Formats／テスト・プロジェクト参照・JSON version は変更していない。Formats の依存は Core と BCL のまま。
- git 操作、Unity 起動、コンパイル、`dotnet build` / `dotnet test`、PCM 生成は実施していない。テスト成功・警告ゼロは未確認。30 分以内で実装・記録を終えた。

## M3-E3 / E4 変更ファイル一覧

更新:

- `docs/implementation.md`

新規実装:

- `src/Arpeggio.Formats/Midi/MidiTempoSegment.cs`
- `src/Arpeggio.Formats/Midi/MidiTempoMap.cs`
- `src/Arpeggio.Formats/Midi/MidiTickQuantizer.cs`
- `src/Arpeggio.Formats/Midi/MidiQuantizedNote.cs`
- `src/Arpeggio.Formats/Midi/MidiPitchRange.cs`
- `src/Arpeggio.Formats/Midi/MidiDrumPriority.cs`
- `src/Arpeggio.Formats/Midi/MidiVoiceNote.cs`
- `src/Arpeggio.Formats/Midi/MidiAllocatedNote.cs`
- `src/Arpeggio.Formats/Midi/MidiVoiceTrack.cs`
- `src/Arpeggio.Formats/Midi/MidiChannelCandidates.cs`
- `src/Arpeggio.Formats/Midi/MidiVoiceAllocator.cs`

新規テスト・補助型:

- `tests/Arpeggio.Core.Tests/Formats/MidiTempoMapTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiTickQuantizerTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiVoiceAllocatorTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiPolyphonyTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiChannelMapTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiPitchRangeTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiVoiceFixture.cs`
# M3-C1 実装記録（2026-09-08）

## M3-C1 設計との差

- 未指定の writer API は `VgmWriter.CalculateSize(RegisterTimeline, title, author, ConversionReport)` と `VgmWriter.Write(Stream, RegisterTimeline, title, author, ConversionReport)` で補完する。前者は保存前の全サイズ検証と予定サイズの返却（拒否時 null）、後者は同じ事前検証後の書き込み（拒否時 false）を担う。チップが異なるレポートは引数例外とし、I/O 例外は呼び出し元へ伝える。曲名はスナップショットの `ControlTimeline.Title` を渡す。copyright を受け取る入口は設けない。
- Stream の現在位置から一つの完全な VGM を追記し、相対オフセットはその開始位置を基準にする。Seek／Length／Position を要求せず、呼び出し側の Stream は閉じない。パス単位の隣接一時ファイル・上書き保護・ChipExportService は設計の M3-F1 に残す。
- GD3 の不正な単独サロゲートの扱いは未指定。文字を無断で置換しないため `InvalidMetadata` として書き込み前に拒否する。既定の NUL／1024 UTF-16 コード単位検証は既存 `ConversionLimits` を internal で共用する。
- writer の形式符号化は設計どおり NES／GB の二種を扱う。今回の全往復検証は NES を対象とし、GB の Noise／時間 envelope／四声を通した VGM 接続は M3-C3 に残す。レジスタ列の時刻・順序・停止は既存の不変列とコンパイラーが保証し、writer は並べ替え・同値除去・停止の再生成を行わない。

## M3-C1 実装範囲と判断

- `VgmWriter` は 256 byte ヘッダー、BCD version `0x171`、EOF／GD3／data の相対オフセット、44100 Hz の総待機数、単一チップのクロック欄を出す。未使用欄・loop offset／samples・rate はゼロ。NES は `B4` と `$4000` 基準、GB は `B3` と `$FF10` 基準でアドレスを符号化する。
- 入力列の絶対時刻差から正の `61 ll hh` だけを生成する。65536 は 65535＋1、131070 は 65535＋65535、131071 は 65535＋65535＋1。ゼロ待機・短縮命令・データブロックを出さず、同時刻の同値書き込みも全順序を保持する。既存の終端停止列の後に `66`、直後に GD3 を配置する。
- `Gd3Tag` は GD3 v1.00 の 11 個の NUL 終端 UTF-16LE 文字列を作る。曲名は原語欄に保存し、ASCII の場合だけ英語欄へも複写する。system 英語欄と変換者 Arpeggio、指定された author 原語欄以外は空。BOM・実行日・パス・推測した翻訳は入れない。補助平面の文字はサロゲートペアのまま保存する。
- 書き込み前にメタデータと全コマンド／GD3 の予定サイズを算定し、既存 `ValidateExportSize` でレジスタ数／VGM 容量を検証する。`OutputBytes` と `DurationSeconds` を予定値へ設定する。先行エラー・strict 警告時は Stream に一切書かない。I/O 失敗時の `OutputBytes` は実際に保存できた長さではなく予定値のままとする。
- `BinaryWriter` は using と `leaveOpen: true` で管理する。ヘッダーの後戻り修正もファイル全体の中間バイト配列も不要。呼び出し側は FileStream を渡して NES VGM を保存できる。パス単位の安全保存 API はこの writer の責務へ混ぜない。
- 既存変更は `ConversionLimits.ValidateMetadata` の private → internal と、この実装記録の追記のみ。Formats の参照は Core と BCL のまま。Core・設計書・JSON version・プロジェクト定義・フロントエンドは変更していない。

## M3-C1 テストコード

4 テストクラスに **25 メソッド／72 ケース**と、独立パーサー・解析結果型・故障注入 Stream を追加した。依頼の 963 件に対して **1035 件見込み**。件数は属性の静的集計であり、検出・実行結果ではない。PCM・音声合成器・外部エミュレーター・ネットワークを使用せず、アロケーション計測テストも追加していない。

| ファイル | 検証内容 |
|---|---|
| VgmWriterTests | 空曲の全 256 byte ヘッダー／全コマンド／410 byte 全長の固定値、待機 1／65535／65536／131070／131071 の正確な byte 列、735→736 の 1 サンプル差、先頭無音、NES 四声の同時 Off→On と同値再トリガー、非ゼロ loopStartTick の有限二周、全停止直後の END／GD3、1800 秒上限、時刻・アドレス・値・全順序番号の完全往復と決定性 |
| VgmMetadataTests | 日本語・補助平面文字の固定 UTF-16LE byte 列と 116 byte ペイロード、全 11 欄、スナップショットの曲名、ASCII の英語欄条件、ASCII author の原語欄限定、1024 コード単位の両端、NUL／null／単独サロゲート／上限超過の保存前拒否 |
| VgmWriterContractTests | FileStream の実保存・閉じた後の再読込、Seek 不可、既存 prefix を持つ Stream の相対位置、予定サイズの一致、明細保持ゼロでの先行エラー／strict 拒否、制限だけの場合の成功、ヘッダー／命令／GD3 途中の I/O 失敗、Stream 非所有、チップ不一致・形式不一致・書き込み不可の拒否。GB は空曲による単一クロック・B3・system 名の形式選択だけを確認 |
| IndependentVgmParserTests | writer を使わない手書き 300 byte ファイルによる既知の二書き込み／待機／GD3、待機の上位 byte、magic／BCD version／全相対 offset／総待機／loop／rate／未使用欄／チップ／未知命令／ゼロ待機／END／GD3 長・終端の破損検出、全 byte 境界の切断と末尾余剰の拒否 |

パーサーと `ParsedVgm` は Formats／Core の型・定数を参照しない。ヘッダーを手動の little-endian 読み取りで解析し、命令の待機をヘッダーとは独立に合計して `(sample,address,value,order)` を再構築する。生成した配列をそのまま期待値にせず、空曲ヘッダー・初期化・待機・日本語・手書きパーサー入力の固定値も併用する。

## M3-C1 静的確認

- 指定の設計書全文、M3-A／B1／B2／C2 の実装記録、既存 Formats／テストと C# 規約を読んだ。Core／Formats の使用型・namespace と .NET 10 の BinaryWriter／BinaryPrimitives／UnicodeEncoding／Stream／File API、xUnit 2.9.2 の参照定義を照合した。
- 新規・変更 C# の括弧対応、summary の XML と public／protected メンバーへの隣接、ブロック namespace、一ファイル一型、末尾空白、禁止省略名、Unity lifecycle／コンポーネント API の不使用を確認した。固定サンプル時刻は有理数の独立算術で照合した。
- 作業開始時のハッシュから、既存ファイルの変更が `ConversionLimits.cs` と `docs/implementation.md` のみであることを確認した。Core・両設計書・既存テスト・依存定義は不変。独立パーサーには Formats の参照・生成定数の共用がない。
- git 操作、コンパイル、`dotnet build`／`dotnet test`、アプリ起動、PCM・実 VGM ファイル生成は実施していない。FileStream の保存も今回作成した未実行テストに含む。作業ディレクトリ外への書き込みは行っていない。

## M3-C1 未完了

予定した実装・テストコードと静的確認は完了。受け入れ条件の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` の警告・エラーゼロ。
- 既存 963 件と追加 72 ケースの全件成功、および実際の検出件数の確認。
- FileStream による実ファイル保存、独立パーサーによる全往復、I/O 失敗・所有権契約のテスト実行。
- 外部プレイヤー・実機での再生は未検証。M3-C3 の GB 四声 VGM 接続、M3-F1 の ChipExportService／パス単位の安全保存／CLI は設計の後続ランに残る。

## M3-C1 変更ファイル一覧

更新:

- `src/Arpeggio.Formats/ConversionLimits.cs`
- `docs/implementation.md`

新規:

- `src/Arpeggio.Formats/Export/VgmWriter.cs`
- `src/Arpeggio.Formats/Export/Gd3Tag.cs`
- `tests/Arpeggio.Core.Tests/Formats/VgmWriterTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/VgmMetadataTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/VgmWriterContractTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/IndependentVgmParser.cs`
- `tests/Arpeggio.Core.Tests/Formats/IndependentVgmParserTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/ParsedVgm.cs`
- `tests/Arpeggio.Core.Tests/Formats/VgmTestStream.cs`
# M3-E5 / E6 実装記録（2026-09-08）

## M3-E5 / E6 設計との差

- 設計書は変更しない。E5 の入口を `MidiInstrumentMapper.Map`（分類・実時間 gate・量子化）と `CreateInstruments`（採用順の音色生成）に分ける。ID の同 tick 順は E4 の割り当て比較順を共用する。NES / GB はチャンネル種別、Noise は分類、SNES は preset 名で共有する。
- E2 の申し送りどおり、打楽器の固定 gate 内の CC7 / CC11 / CC121 の実効変更を E5 で診断する。元 Off との比較は量子化前の実時間で行い、CC120 適用後の gate を比較対象とする。
- SNES の既存 Preset setter は内部で再生用サンプルを準備する。Core の変更禁止と既存プリセット利用を優先してこの既存経路を使用する。Formats / テストから PCM 配列・レンダリングを作らず、ドキュメントの設定値を検証する。
- E6 の保存 API は `MidiSongFile.Write(result, path, dryRun, cancellationToken, sourcePath)` とし、`MidiSongFileResult` で Written / DestinationExists / Report を返す。入力パスを保持できるフロントエンドは sourcePath を渡す。Stream の Import は入力名を SourceName で受ける既存仕様を維持する。I/O・保存競合・キャンセルは例外を伝播し、変換 report を I/O の失敗で汚さない。
- MidiImportResult は検証済み JSON を不変文字列として保持し、Song は編集用に独立した候補とする。候補の後編集はこの結果の保存内容へ反映しない。strict 警告時も候補と予定サイズを返すが保存を拒否し、変換エラー時は候補を返さない。

## M3-E5 / E6 未完了

- E5 / E6 の予定実装とテストコードは追加済み。受け入れ条件の実行確認は未完了で、依頼者側に残る。
- `dotnet build Arpeggio.slnx` の警告・エラーゼロ、既存 1363 件と今回の追加 75 ケースの全成功。静的集計では合計 1438 件見込みだが、ランナーの検出件数・成功は未確認。
- 実ファイルの保存往復、同時新規保存の競合、一時ファイル清掃、部分 Stream の I/O 失敗とキャンセルのテスト実行。コード読解とテスト作成を実行確認済みとは扱わない。
- CLI / MCP / DAW への接続は設計の F2 / F3 / G2 に残る。ファイル入力を扱う側は保存 API の sourcePath に実入力パスを渡す。PCM 再生・VGM / NSF との全体統合回帰は後続ランの対象。

## M3-E5 実装・静的確認

- GM 全 128 program、既定 NES / GB 音色、SNES の preset 推奨値・エコー無効、全ドラム表と未知番号のフォールバック、固定 gate・CC120・Noise 減衰マクロを実装した。
- 音色 ID は採用ノートを E4 と同じ順で走査して 1 から採番し、未採用音色を生成しない。ProgramApproximated は採用した channel / program ごとに一件。打楽器の音量 CC 警告は量子化前の固定 gate で判定する。
- MidiInstrumentMapperTests / MidiDrumMappingTests と専用 fixture を追加。全 program、音色共有、ID 決定性、全表・未知ドラム、同時優先・後発打撃・両 polyphony、元 Off / sustain / CC120 / CC123、固定 gate 内の CC を対象とする。
- E5 のコード・テストコード・型と namespace の照合・括弧対応・境界値の静的確認を終えて E6 に進む。コンパイル・テスト成功は未確認。

## M3-E6 実装・静的確認

- `MidiImporter.Import(Stream, options)` から reader → collector → tempo → 音色分類・固定 gate・量子化 → allocator → 採用音色 → SongSerializer / SongValidator を接続した。全固定トラック、空トラック、明示 InstrumentId、Muted=false / Pan=0 / DefaultInstrumentId=null / LoopStartTick=0 を保持する。
- 曲長は入力 EOT・採用後ノート終端・最小 48 tick の最大値とし、固定 gate と短音延長後にも 1800 秒上限を検証する。曲名の優先順位、version 1、SNES エコー無効、既存統計と生成音色一覧の統計をまとめ、UTF-8 BOM なしの予定サイズを算定する。
- strict は診断収集・有効 Song・JSON の確定を止めず、保存だけを拒否する。変換エラー時は Song / Json とも null。元 MIDI byte、options の候補配列、他の取り込み結果を変更しない。
- `MidiSongFile` は確定 JSON を隣接一時ファイルに CreateNew で書き、flush・キャンセル確認後に上書きなしの File.Move で確定する。既存ファイル、存在確認後の競合、入力同一パスを保護し、失敗時は自分が作った一時ファイルを清掃する。履歴・現在セッションには触れない。
- dry-run は Import で確定した全診断・予定サイズを保持し、保存先の存在を DestinationExists で返す。Stream 書き込みは Seek を要求せず、leaveOpen=true で所有権を維持する。I/O 失敗・キャンセルによる部分 Stream は巻き戻せない。

## M3-E5 / E6 テストコード

`tests/Arpeggio.Core.Tests/Formats/` に 4 クラス・35 メソッド・75 ケースと fixture 1 型を追加した。ケース数は属性の静的集計。PCM レンダリング・外部エミュレーター・アロケーション計測は追加していない。

| ファイル | 検証内容 |
|---|---|
| MidiInstrumentMapperTests | 全 128 program の対応と三チップでの実生成、推奨値・エコー無効、音色共有、未採用音色の除外、同 tick / 異なる出力トラックの時系列採番、独立インスタンス、channel / program ごとの警告 |
| MidiDrumMappingTests | 全ドラム表・未知番号、Noise selection と減衰マクロの全値、SNES 原速、固定 gate・元 Off との実時間比較、CC120 / CC123 / sustain / 音量 CC、同時分類優先、後発打撃の両モード、SNES 6＋2 / 8 声 |
| MidiImporterTests | 三チップ・format 0 / 1 の正規 JSON 往復、全トラックと音色互換、先頭無音と tempo 積分、タイトル優先・Latin-1、strict と制限のみの成功、4096 明細上限後の全警告数、空・不正入力と設定、固定 gate / 短音延長後の資源超過、元データ保持、入力 Stream 所有権 |
| MidiSongFileTests | 三チップの BOM なし保存・サイズ・再読込、dry-run と DestinationExists、strict / 変換失敗時の無書き込み、既存パスと同時保存競合、入力同一パス拒否、候補後編集からの確定 JSON の隔離、キャンセル、保存先 I/O 失敗、部分 Stream と非所有契約 |

## M3-E5 / E6 静的確認と未実行の確認事項

- 自前型の定義・namespace・公開範囲、.NET 10 の File / FileStream / StreamWriter / CancellationToken と xUnit 2.9.2 の API を照合した。テストの Core internal 型参照は独立した型対応の検証へ直し、Core の可視性を変更していない。
- 変更 C# の構造上の括弧対応、日本語 summary XML と public メンバーへの隣接、一ファイル一型、ブロック namespace、末尾空白、禁止省略名・Unity lifecycle・PCM Render 呼び出しの不使用を確認した。`Assert.Single(collection, predicate)` を使用し、Where を渡す書き方を追加していない。
- 開始時の SHA-256 と比較し、既存コードの変更は MidiVoiceAllocator の比較メソッドを internal にした点と MidiImportOptions の古い summary 更新だけ。Core・設計書二つ・既存テスト・プロジェクト定義は不変。Formats の依存は Core と BCL のみで、NuGet の追加はない。
- git 操作、Unity / アプリ起動、コンパイル、`dotnet build` / `dotnet test`、PCM・実 JSON 出力の生成は実施していない。指定作業ディレクトリ外への書き込みはない。30 分以内に実装・テストコード・記録をまとめた。

## M3-E5 / E6 変更ファイル一覧

更新:

- `src/Arpeggio.Formats/Midi/MidiVoiceAllocator.cs`
- `src/Arpeggio.Formats/Midi/MidiImportOptions.cs`
- `docs/implementation.md`

新規（Formats）:

- `src/Arpeggio.Formats/Midi/MidiInstrumentMapper.cs`
- `src/Arpeggio.Formats/Midi/MidiInstrumentMap.cs`
- `src/Arpeggio.Formats/Midi/MidiDrumDefinition.cs`
- `src/Arpeggio.Formats/Midi/MidiDrumControllerDiagnostics.cs`
- `src/Arpeggio.Formats/Midi/MidiImporter.cs`
- `src/Arpeggio.Formats/Midi/MidiImportResult.cs`
- `src/Arpeggio.Formats/Midi/MidiSongFile.cs`
- `src/Arpeggio.Formats/Midi/MidiSongFileResult.cs`

新規（Tests）:

- `tests/Arpeggio.Core.Tests/Formats/MidiMappingFixture.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiInstrumentMapperTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiDrumMappingTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiImporterTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiSongFileTests.cs`

# M3-D3 / D4 実装記録（2026-09-08）

## M3-D3 / D4 設計との差

- 未指定の writer API は `NsfWriter.Write(Stream, NsfEncodedData, title, ConversionReport, author="", copyright="")` → bool とする。容量検証済みの不変データを受け、同じデータからドライバーを生成して CPU 予算を再検証し、全ヘッダー検証後にだけ保存する。予定サイズは既存 `NsfEncodedData.OutputBytes` を使用する。パス単位の隣接一時ファイル・上書き保護は、VGM と同じく設計の F1 に残す。
- 不正な単独サロゲートは既存 GD3 と同じ `InvalidMetadata` で拒否する。正常な補助平面文字は一つの Unicode scalar として `?` 一文字へ縮約する。切り詰め後に見えなくなる位置も UTF-16 の正当性を検証する。
- 通常データ長は `3n+1` のため、bank 255 の最終 byte に END を置く列は現 encoder から生成できない。最大容量の正常出力を独立ロードする検証に加え、テスト側だけで保存済みファイルの末尾を改変しカーソルを設定して、最終 byte の END とその先への防御停止を検証する。生成形式・容量・設計書は変更しない。
- 限定 CPU は使用 21 opcode だけを実装する。APU 書き込みは命令の最終 cycle を記録し、RAM の RMW は旧値／新値の二書き込みを再現する。IRQ・DMA・未使用 opcode・APU 合成・全ダミー読み出しを備える汎用エミュレーターには拡張しない。

## M3-D3 実装・静的確認

- `Limited6502` と `Limited6502Memory` は Core／Formats の型・定数・命令表・ラベル・サイクル解析を参照しない。手書き命令列で全使用命令の flags、アドレスの折り返し、ページ越え、前後分岐、入れ子 JSR／RTS と SP 復元、未知 opcode・非復帰の拒否を検証するテストを追加した。
- 生成 INIT の 146 cycles、RAM・bank 1・APU 初期化、再 INIT、WAIT 1／256／65535／65536、同値 trigger の順序、END 後の追加 PLAY と読み出し停止、未知データ命令・不正レジスタ・ゼロ WAIT の防御停止を検証するテストを追加した。実行トレースは PLAY 番号・cycle・アドレス・値を持つ。
- D3 の実装・テストコード作成と静的読解を先に終えてから D4 に着手した。コンパイル・テスト実行は依頼により行っておらず、D3 の受け入れ実行確認が済んだという意味ではない。

## M3-D4 実装・テストコード

- `NsfWriter` は 128 byte の NSF v1 ヘッダー、実 INIT／PLAY アドレス、NTSC=16639／PAL 欄=19997、初期 bank `[0,1,0,0,0,0,0,0]`、region／expansion／予約 byte=0 を生成する。固定 bank と不変データを 4096 byte の作業バッファで順に書き、末尾 bank はゼロ埋めする。
- writer は渡されたデータから `NsfDriverBuilder.Build` を呼び、別データに対する古い CPU 上限を流用しない。29 WRITE／PLAY の 7776 cycles は保存、30 WRITE／PLAY の 8033 cycles は `NsfCpuBudgetExceeded` として最初の byte を書く前に拒否する。Stream の Seek／Length／Position を要求せず、呼び出し元所有の Stream を閉じない。I/O 失敗は伝播し、予定サイズと部分出力の実長を混同しない。
- `NsfMetadata` は title／author／copyright の ASCII 31 byte＋NUL を生成する。`MetadataReduced` は縮約した欄数を occurrenceCount とし、共通レポートで元位置なしの同一コードが集約されても情報を失わないよう、変更した全欄の名称・元値・結果を一明細に記録する。strict は明細保持ゼロでも保存を拒否する。
- `IndependentNsfLoader` は生成側の型・定数を参照せず、ヘッダーから独立メモリへ bank を配置する。署名・version・曲数・load・固定 bank 内の両入口・速度・初期 bank・予約欄・ASCII NUL・ROM 整列／上限を検証する。writer を使わない手書きファイルによるローダー自身の正常・破損・全 byte 切断テストも追加した。
- FileStream を閉じた後にファイルパスから独立ロードし、四声・先頭無音・同時 Off→On・同値 trigger・非ゼロ loopStartTick の二周・全停止・再 INIT を `NsfFrameCompiler` の期待列と完全比較する。テスト用ファイルはテストの作業ディレクトリ内へ一意名で作成し、finally で削除する。
- WRITE の offset／value、WAIT の low／high、命令先頭、END の直前にある 4 KiB 境界を実ファイルで検証する。END が bank 最終 byte にある場合の先行切り替え禁止と、次 bank 先頭にある場合の必要な切り替えを両方扱う。
- 最大容量の正常入力は 336154 WRITE を最大 28 WRITE／PLAY に分散し、12005 WAIT を挟んだデータ 1044478 byte、ROM 256 bank、末尾 2 byte パディングとする。全 INIT／PLAY を独立 CPU で実行し静的上限以下を確認するテストを追加した。追加一 WRITE の 1044481 byte は符号化前に拒否する。bank 255 最終 END と WRITE／WAIT オペランド不足の防御停止は、保存済みデータをテスト側で改変して別の実ファイルへ保存・独立ロードする。
- CPU 観測は APU／bank の順序と cycle を保持する。banked 実行では予約領域外への書き込みを即時拒否し、長曲の観測量を抑えるため RAM／stack 書き込みの全履歴は保持しない。CPU 単体テストの非 banked メモリでは stack と RMW の旧値／新値も観測する。

## M3-D3 / D4 静的確認

- 追加は **7 テストクラス・35 メソッド・117 ケース**（D3=61、D4=56、属性による静的集計）。基準 1363 件を含めると 1480 件見込みであり、ランナーによる検出・成功を確認した値ではない。PCM と合成器、外部エミュレーターを呼ばない。アロケーション計測テストは追加していない。
- 自前型の定義・namespace、.NET 10.0.8 のローカル参照 XML、xUnit 2.9.2 の参照 API を照合した。全新規 C# の括弧対応・日本語 summary XML と public 宣言への隣接・ブロック namespace・一ファイル一型・末尾空白・省略名・禁止 API を静的に検査した。`Assert.Single(collection, predicate)` を使用し、Where を渡す形式はない。
- 6502 の一次参照は [NESdev 命令一覧](https://www.nesdev.org/obelisk-6502-guide/instructions.html)・[命令リファレンス](https://www.nesdev.org/wiki/Instruction_reference)・[アドレス方式](https://www.nesdev.org/wiki/Addressing_modes)。本文直接取得は 403 だったため、取得できた検索記載で flags・JSR／RTS・間接ポインター折り返し・ページ追加 cycles を照合した。
- 4 KiB 境界の byte 位置、bank 切り替え PLAY 番号、END の配置、最大容量の命令数を独立の整数算術で読み合わせた。これは生成プログラムや CPU テストを実行したという意味ではない。
- 既存ファイルの変更は本記録のみ。Core、設計書二つ、既存 Formats／テスト、プロジェクト・依存定義・JSON version 1 を変更していない。新規 Formats の依存は既存 Core と BCL のみ。git 操作、コンパイル、`dotnet build`／`dotnet test`、アプリ起動、PCM 生成は行っていない。指定作業ディレクトリ外への書き込みも行っていない。

## M3-D3 / D4 未完了

D3／D4 の予定実装・テストコード作成と静的確認は完了。次の受け入れ実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` の警告・エラーゼロ。
- 既存 1363 件を含む全件と今回追加 117 ケースの成功、および実際のテスト検出件数。
- 独立 CPU 単体テスト、生成 INIT／PLAY、実 NSF ファイル保存・独立ロード・全トレース、最大 bank／CPU 予算／strict／I/O 失敗のテスト実行。実ファイルの作成も今回は未実行のテストコードに含む。
- 外部プレイヤー・実機での再生互換性は未検証。パス単位の安全保存・ChipExportService は F1、VGM 相互比較・レジスタ再合成は H 系列の対象のままとする。

## M3-D3 / D4 変更ファイル一覧

更新:

- `docs/implementation.md`

新規 Formats:

- `src/Arpeggio.Formats/Export/NsfWriter.cs`
- `src/Arpeggio.Formats/Export/NsfMetadata.cs`

新規テスト・補助型:

- `tests/Arpeggio.Core.Tests/Formats/Limited6502.cs`
- `tests/Arpeggio.Core.Tests/Formats/Limited6502Memory.cs`
- `tests/Arpeggio.Core.Tests/Formats/Limited6502Tests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfExecutionFixture.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfDriverExecutionTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/IndependentNsfLoader.cs`
- `tests/Arpeggio.Core.Tests/Formats/IndependentNsfLoaderTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfWriterFixture.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfWriterTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfBankExecutionTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfMetadataTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/NsfWriterContractTests.cs`
# M3-C3 実装記録（2026-09-08）

## M3-C3 設計との差

- 設計書は変更しない。NoiseRateQuantized の original／converted と MaximumError の単位は未指定のため Hz とし、選択自体は規定の対数比で行う。元ノートへの集約と strict の拒否は既存の診断契約に従う。
- C1 の VgmWriter は既に GB のクロック・B3・Wave RAM・GD3 を符号化できるため、その API へ四声の完成したレジスタ列を渡す。形式 writer の重複実装や新しい公開入口は追加しない。
- C2 の「時間 envelope／Noise は次ラン」の暫定期待値だけを完成仕様の検証へ更新する。その他の既存テストは保持する。

## M3-C3 実装範囲と判断

- `GameBoyRegisterCompiler` に第四声の Noise を接続した。NR43 は変調後 selection を 0〜127 に制限して ToEven で選び、Core と同じ目標クロックへ divisor code 0〜7／shift 0〜13 の対数比が最小の組を総当たりする。同点は小さい NR43、7 bit 幅は bit 3 とし、停止する shift 14／15 は候補へ入れない。
- Pulse の E は不変な音色設定と `ControlEvent.Frame` から計算する。step=0 は初期値保持、増減は整数除算の後に 0〜15 へ制限し、共通音量 V と掛けて AwayFromZero で最終整数化する。グローバルな 60 Hz 更新、Delay 後開始、隣接 On と周回再発音でのフレームリセットは既存制御列をそのまま利用する。
- Pulse と Noise の音量ゲートは同じ処理へまとめた。正音量の開始／変更では自声 routing 解除 → DAC off → 必要な周期設定 → 一定音量 → trigger → routing 復元とする。Noise は NR43 だけを周期として更新し、NR44 の trigger は `$80`、length enable=0。継続ピッチだけでは trigger を出さない。
- 音量 0 は DAC と routing を停止し、同じ整数音量の継続では再起動しない。ゼロから正音量への復帰を含む継続再起動に `EnvelopeRetriggered` を付け、Noise の説明には LFSR 再初期化を含めた。NR51 shadow は他三声の左右ビットを保ち、開始後の NR52 リセットは行わない。
- `NoiseRateQuantized`／`VolumeQuantized`／`EnvelopeRetriggered` は既存レポートへ元ノート単位で集約する。strict は明細保持ゼロでも全発生数で拒否する。書き込み上限と有限終端の全停止を維持し、C2 の段階的な未対応 limitation を削除した。
- C1 の既存 writer へ GB 四声の列を渡せる状態とした。B3・クロック 4194304・Wave RAM の 20〜2F offset・GD3・待機／END の符号化は変更不要。C3 は Formats の Stream 書き出しまでで、ChipExportService／パス保存／CLI は設計どおり F1 に残る。

## M3-C3 テストコード

3 クラスに **22 メソッド／56 ケース**を追加した。既存 C2 の暫定 2 メソッド／3 ケースを完成仕様へ更新し、削除・緩和はしていない。依頼の既存 1363 件に対して **1419 件見込み**で、属性の静的集計による値であり実際の検出件数ではない。PCM・音声合成・外部エミュレーターを使わず、レジスタ列・バイト列・ドキュメントモデルを直接検証する。

| ファイル | 検証内容 |
|---|---|
| GameBoyNoiseRegisterTests | 全 128 selection×両 LFSR 幅について整数比の交差積による独立した最短距離／同点順／shift 上限検証、低速端と通常域の固定 NR43、変調後の ToEven／範囲制限、周期差分だけの更新、幅交換・同値 On の強制 trigger、元位置と Hz 誤差集約、strict／明細ゼロ、ミュート、Noise の左右パン境界 |
| GameBoyEnvelopeRegisterTests | 両 Pulse の増減／step=0／任意整数間隔／0・15 保持、開始音量 0 からの復帰、V×マクロ×VolumeSlide×E の最終丸め、同じ整数音量の更新抑止、時間 envelope 再起動時の他三声維持、Noise の周期・音量同時変更とゼロ保持／復帰、四声それぞれの個別 Off、Delay／global frame／隣接 On／有限二周での E リセット、strict、元音色編集からの隔離 |
| GameBoyVgmTests | 空曲の全 256 byte header・全命令列・386 byte 全長の固定値、四声の時刻／アドレス／値／全順序番号の完全往復、先頭無音・同時 Off→On・Noise 復帰・時間 envelope・有限二周、日本語 GD3 全 11 欄、Wave RAM 両端の B3 offset、長待機分割、全停止直後の END、決定性・元 JSON 不変・Stream 所有権・strict 拒否、writer を使わない手書き GB ファイルによるパーサー自身の照合 |
| GameBoyRegisterDiagnosticsTests（更新） | 暫定だった時間 envelope 初期値保持を各フレームの増減へ、Noise 無視を NR42／NR43／NR44／routing の On／Off 固定列へ更新 |

## M3-C3 静的確認

- 設計書全文、既存 Formats・M3 各節の判断と申し送り、C# 規約を参照した。Core／Formats の型定義と namespace、.NET 10 の Math／Array／Stream と xUnit 2.9.2 の参照定義を照合した。
- 変更・新規 C# 7 ファイルの括弧対応、summary XML と public 宣言への隣接、ブロック namespace、末尾空白、未使用定数、禁止省略名、`Assert.Single(...Where(...))` と Unity lifecycle／コンポーネント API の不使用を確認した。
- 低速端・同点・上端の NR43 固定値、空 GB VGM の command 46 byte／GD3 84 byte／全長 386 byte、長待機の開始時刻は独立した有理数の算術で照合した。C# のコンパイル・テストコードや writer の実行を行ったという意味ではない。
- 開始時の SHA-256 と比較し、既存変更は GB 実装 3 ファイル・C2 診断テスト・本記録だけであることを確認した。Core、両設計書、既存 VgmWriter／独立パーサー、他の既存テスト、プロジェクト依存とフロントエンドは不変。Formats の依存は Core と BCL のみ、JSON version 1 を維持した。
- git 操作、Unity／アプリ起動、コンパイル、`dotnet build`／`dotnet test`、PCM・実 VGM ファイル生成は実施していない。作業ディレクトリ外への書き込みとアロケーション計測テストの追加もない。

## M3-C3 未完了

予定した実装・テストコードと静的確認は完了。次の受け入れ条件の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` の警告・エラーゼロ。
- 既存 1363 件と追加 56 ケースの全成功、および実際の検出件数。
- 独立パーサーを通した GB 四声 VGM の完全往復、Noise の全 selection、ソフトウェア envelope／再 trigger／他声維持のテスト実行。
- 外部プレイヤー・実機の動作と聴取は未検証。GB の独立レジスタ再合成は設計済み H2、パス保存・CLI 接続は F1 の範囲に残る。

## M3-C3 変更ファイル一覧

更新:

- `src/Arpeggio.Formats/Export/GameBoyRegisterCompiler.cs`
- `src/Arpeggio.Formats/Export/GameBoyRegisterValues.cs`
- `src/Arpeggio.Formats/Export/GameBoyRegisters.cs`
- `tests/Arpeggio.Core.Tests/Formats/GameBoyRegisterDiagnosticsTests.cs`
- `docs/implementation.md`

新規:

- `tests/Arpeggio.Core.Tests/Formats/GameBoyNoiseRegisterTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/GameBoyEnvelopeRegisterTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/GameBoyVgmTests.cs`

# M3-F1 / F2 実装記録（2026-09-08）

## M3-F1 設計との差

- 公開 API は設計どおり Prepare → ChipExportPlan → Write とする。Stream 版と CancellationToken、ファイル版の任意 sourcePath を補完する。Song だけから入力パスは復元できないため、CLI は sourcePath を必ず渡して入力同一パスを拒否する。
- 既存の下位コンパイラーは strict 警告時に部分列を返さない。サービス内では strict=false で全変換・サイズ・CPU・メタデータ検証を完了し、最終レポートの独立コピーへ要求された strict を適用する。単独 API の挙動を変更せず、明細上限後の件数もコピーする。
- writer の既存公開 Stream API は維持し、検証済みの列・ヘッダー・メタデータを保存する内部入口を抽出する。plan はその確定済み内容を所有し、保存時の再変換と診断の二重加算を防ぐ。I/O・保存競合は exit 3、同一入力パスは exit 1 とする。
- CLI の変換コマンドは成功・失敗とも report を含む一つの JSON を返す。解析段階の引数エラーも対象とし、変換前でチップ不明なら None とする。I/O 例外は外側の code / error / exitCode で返し、既に確定した変換 report を変更しない。

## M3-F1 実装・静的確認

- ChipExportService / ChipExportPlan を追加。NSF は制御列 → PLAY 量子化 → NES レジスタ → データ符号化 → CPU 予算・メタデータ検証、VGM は制御列 → NES / GB レジスタ → GD3・サイズ検証を一度だけ実行する。plan は不変な列と検証済みメタデータを保持する保存関数を所有する。
- ファイル保存は隣接 CreateNew 一時ファイル → flush → File.Move。既定上書き拒否、明示上書き、入力同一パス拒否、キャンセル、失敗後の一時ファイル清掃を接続した。Stream は呼び出し元所有で、I/O 例外と部分書き込みを伝播する。
- CLI に export nsf / vgm を追加し、既存 wav / ogg の引数・既定値は維持した。共通の変換応答で report、written、dryRun、destinationExists、code / error / exitCode を返す。通常表示は要約・制限を stdout、位置付き診断を stderr とする。
- ChipExportCommandsTests / ChipExportServiceTests に対応チップ、独立パース、loops・author・copyright、strict 全診断・サイズ、dry-run、DPCM と元位置、文書・引数・I/O 終了コード、上書き・元 JSON・履歴の保護、plan の隔離、診断二重加算防止、明細上限後の件数、部分 Stream・キャンセル・移動失敗後の清掃を追加した。
- 自前型の定義・namespace と System.CommandLine のローカル参照 XML を照合し、括弧・末尾空白・禁止 API を確認した。Core と設計書二つは開始時ハッシュと同じ。F1 の実装・テストコード・静的確認を先に終えてから F2 へ進む。

## M3-F1 未完了

- 予定コードとテストコードは追加済み。dotnet build / dotnet test は依頼に従い未実行。警告・エラーゼロ、既存全テストと追加テストの成功、実際の検出件数は依頼者側の確認待ち。
- 実 NSF / VGM ファイル保存、独立パース、キャンセル・I/O・競合のテスト実行は未確認。外部プレイヤー・実機互換性の検証済みとは扱わない。

## M3-F2 設計との差

- channel-map の JSON 構造を CLI で厳密に読み、数値キーの重複（同値の別表記を含む）・非整数候補を拒否する。チップ別候補の互換性・範囲・重複は既存 MidiImporter へ委ねる。UTF-8 の不正 byte は置換せず操作エラーとする。
- 新規 JSON 保存後、新しく開いた空履歴の EditSession を CliHistoryStore.Save へ渡す。古い側車を Load しない。同一内容の古い current が残っていても undo / redo を引き継がない。履歴保存に失敗した場合は今回新規作成した JSON を取り消す。dry-run・strict・変換拒否・保存競合では側車に触れない。

## M3-F2 実装・静的確認

- import midi を CommandFactory に登録。必須 chip、任意 tempo、48 の約数 quantize-ticks、両 polyphony、UTF-8 channel-map、title、strict、dry-run、json を既存 MidiImporter へ接続した。SourceName に入力パスを渡し、MIDI メタデータがなければ入力ファイル名から曲名を決める。
- map はオブジェクト・ch 1〜16・整数候補配列・数値としての重複キーを検証し、候補の互換性と声競合は Formats の既存規則へ渡す。BOM 付き UTF-8 を許可し、不正 UTF-8 / JSON / map は InvalidChannelMap、読み取り I/O は exit 3。
- 全診断を得た結果を MidiSongFile で新規保存し、CliMidiSongFile で空の側車履歴を保存する。古い current と今回の正規 JSON が一致していても履歴を復元しない。履歴 I/O 失敗時は今回の新規 JSON を取り消し、既存側車の内容を保つ。
- MidiImportCommandsTests / MidiChannelMapCommandsTests に三チップ・format 0 / 1・version 1 往復・全固定トラック・曲名優先順・120→60 BPM の焼き込み・明示基準 tempo・量子化による端点変更・map の除外／声競合／両 polyphony・不正 map と UTF-8・strict / dry-run / 保存競合・古い同一 current の側車・履歴 I/O 失敗を追加した。
- README に CLI 三コマンド、対応形式・チップ、全オプション、診断・終了コード、有限展開と GM 音色近似、上書きと履歴保護を追記した。MCP / DAW の記載を実装済み扱いへ先行変更していない。

## M3-F1 / F2 最終静的確認

- 新規テストは 4 クラス・30 メソッド・84 ケース（属性による静的集計）。アロケーション計測は追加していない。既存テストの削除・緩和・変更はない。
- 自前型の定義・namespace、System.CommandLine 2.0.11 の ParseResult / OptionResult / SetAction、.NET 10.0.8 の File / FileStream / UTF8Encoding / JsonElement、xUnit 2.9.2 の Assert.Single(collection, predicate) 等をローカル参照と照合した。新規・変更 C# の字句上の括弧対応、日本語 summary XML と public への隣接、ブロック namespace、末尾空白を確認した。
- strict の最終レポートは診断コレクション・全件数・code 別件数・未保持件数・統計を独立コピーする。保存前後で report と確定 byte 列が変わらないことをテスト対象にした。
- 開始時ハッシュと照合し、Core、設計書二つ、MCP、DAW、既存テストは不変。Formats の依存は Core と BCL のみ。CLI に Formats の ProjectReference を追加し、NuGet と JSON version は変更していない。
- git 操作、コンパイル、dotnet build / dotnet test、Unity / アプリ起動、音声生成、作業ディレクトリ外への書き込みは行っていない。

## M3-F2 未完了

- F1 / F2 の予定実装とテストコードは追加済み。受け入れの実行確認は依頼者側に残る。dotnet build Arpeggio.slnx の警告・エラーゼロ、既存全テストと今回の追加テストの成功、実際の検出件数は未確認。
- 実 MIDI → JSON の保存、基準テンポ・map・声競合、strict / dry-run、側車初期化と履歴失敗時の取り消しを含む CLI 統合テストの実行は未確認。NSF / VGM の実機・外部プレイヤー検証も未実施。
- 本ランのコード実装に残タスクはない。MCP は F3、DAW は G1 / G2、全体の再合成・実行回帰は H 系列の範囲のまま。

## M3-F1 / F2 変更ファイル一覧

更新:

- `README.md`
- `docs/implementation.md`
- `src/Arpeggio.Cli/Arpeggio.Cli.csproj`
- `src/Arpeggio.Cli/CliExecution.cs`
- `src/Arpeggio.Cli/CommandFactory.cs`
- `src/Arpeggio.Cli/SongCommands.cs`
- `src/Arpeggio.Formats/ConversionDiagnosticCollection.cs`
- `src/Arpeggio.Formats/ConversionReport.cs`
- `src/Arpeggio.Formats/Export/NsfWriter.cs`
- `src/Arpeggio.Formats/Export/VgmWriter.cs`

新規:

- `src/Arpeggio.Cli/ChipExportCommands.cs`
- `src/Arpeggio.Cli/CliConversionExecution.cs`
- `src/Arpeggio.Cli/CliMidiSongFile.cs`
- `src/Arpeggio.Cli/MidiChannelMapFile.cs`
- `src/Arpeggio.Cli/MidiImportCommands.cs`
- `src/Arpeggio.Formats/Export/ChipExportPlan.cs`
- `src/Arpeggio.Formats/Export/ChipExportService.cs`
- `tests/Arpeggio.Core.Tests/Cli/ChipExportCommandsTests.cs`
- `tests/Arpeggio.Core.Tests/Cli/CliConversionFixture.cs`
- `tests/Arpeggio.Core.Tests/Cli/MidiChannelMapCommandsTests.cs`
- `tests/Arpeggio.Core.Tests/Cli/MidiImportCommandsTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/ChipExportServiceTests.cs`
# M3-H1 / H2 実装記録（2026-09-08）

## M3-H1 / H2 設計との差

- 再合成器の未指定 API はテスト内の `IRegisterTraceChip`（実機アドレスの Apply・整数 CPU cycles の AdvanceCycles・左右出力）と `RegisterTraceRenderer`（44100 Hz の絶対時刻走査）で補完する。チップごとの状態再構成は独立型とし、Core／Formats の合成器・周期計算・生成定数・既存ゲートモデルを使用しない。
- 検証対象は生成サブセットに限定する。NES は length index 0／halt／constant volume／sweep=$08／即時 linear load、GB は length 無効／sweep 無効／envelope pace=0 を扱い、対象外の命令・設定は明示的に拒否する。NES の frame counter 遅延、アナログミキサー／フィルター、GB の Wave 読み出しバッファの起動遅延は対象外。Triangle の停止後 DAC 保持と Pulse の位相・divider の再起動差、Noise の状態遷移は保持する。
- `RegisterTimeline` の構築が internal のため、NSF 用量子化列の VGM 化はテスト専用の B4／61／66 符号化で補完する。公開 API の拡張や reflection は行わない。PLAY→44100 Hz は絶対位置を整数丸めし、独立 VGM パース後の整数時刻・全書き込み順を CPU 実行トレースと照合する。

## M3-H1 実装・静的確認

- H1 のコード・テスト作成と静的読解を先に終えてから H2 に着手した。受け入れの実行確認が済んだという意味ではない。
- NES の両 Pulse の一周期 duty 全ビット／比率、divider の残時間と high の sequencer リセット、enable 前の length load 無効、個別 Off の他声保持、Triangle の linear load／32 段波形／停止 DAC 保持、Noise 全16周期／長短フィードバック／再 On で seed を保持するケースを追加した。
- Song→既存 VgmWriter→独立パース→再合成で、両 Pulse／Triangle の A4・一オクターブ下の周期誤差2%以内、非無音、先頭無音、停止、音量の単調性を検証する。Noise は周期設定・非無音・停止を検証し、周波数推定は使わない。
- NSF は四声・先頭無音・同値再発音・duty macro・個別 Off・有限一／二周を実ファイルへ保存し、独立ロードした INIT／PLAY の APU トレースを量子化 VGM と照合する。終端後二回の PLAY を含め、整数時刻／address／value／order と左右再合成列の全値を一致条件にした。実機 CPU cycles による各書き込みの物理時間差は比較時刻へ混ぜない。
- 使用型の定義・namespace、括弧対応・末尾空白、合成器／PitchTable／生成定数への非依存を静的に確認した。テスト・コンパイルは未実行。

## M3-H2 実装・テストコード

- `GameBoyRegisterTraceChip` は両 Pulse の全 duty、11 bit 周期と divider、一定音量の trigger 時ロード、DAC off と再 enable／trigger の区別、Wave RAM の全ニブルと NR32 右シフト、NR51 の声別左右選択、NR50 の左右倍率を独立実装する。Pulse の初回出力ゼロと、再 trigger では duty 位相を保持して divider を戻す条件も検証する。
- Noise は NR43 の divisor 0〜7／shift 0〜13 と両幅を扱う。Pan Docs の XNOR／zero seed 表現を採用し、手書き seed 遷移、周期の直前／直後、trigger による再初期化を固定値で照合する。shift 14／15・length 有効・hardware envelope pace 非ゼロ・sweep 有効はサブセット外として失敗する。
- Song→既存 VgmWriter→独立パース→再合成で、両 Pulse／Wave の A4 と一オクターブ下の周波数2%以内、四声の左右／中央のパン境界、先頭無音／非無音／停止、両幅 Noise の周期設定、Pulse／Wave／Noise の音量単調性を検証する。
- 音量 `15→0→0→8` の Pulse／Noise を他二声と同時に鳴らし、ゼロ保持／復帰時の active／DAC／routing／trigger 回数と、他声・Wave RAM の維持を観測する。60 Hz envelope 増加と同時の音程変更は、trigger 時点で新しい周期・音量が揃い、routing がまだ解除されていることを検証する。
- Wave の各 On は DAC off 中に16 byteすべてを書き、周期を設定して trigger することを逐次観測する。同時 Off→On と非ゼロ loopStartTick の有限二周で、RAM 再ロード回数と再 trigger を検証する。

## M3-H1 / H2 検証範囲と静的確認

| 新規テストクラス | メソッド数 | 属性からのケース数 | 主な検証対象 |
|---|---:|---:|---|
| NesRegisterTraceChipTests | 8 | 37 | 両 Pulse 全 duty、低音 sweep、length／linear、high と divider、Triangle DAC 保持、Noise 全周期・両 mode、対象外拒否 |
| NesRegisterResynthesisTests | 6 | 20 | 生成 NES VGM の周期／duty／gate／振幅／Noise／停止、NSF 実行トレースと量子化 VGM の完全一致 |
| GameBoyRegisterTraceChipTests | 7 | 33 | 両 Pulse 全 duty／trigger、DAC、全 Wave ニブルと NR32、Noise 全 divisor／shift 端／両幅、NR50／NR51、対象外拒否 |
| GameBoyRegisterResynthesisTests | 6 | 35 | 生成 GB VGM の周期／duty／左右／Noise／振幅／個別 Off／全停止 |
| GameBoyRegisterRetriggerTests | 3 | 3 | ゼロ復帰と他声保持、60 Hz envelope／同時周期変更、Wave 再 On／有限二周 |
| RegisterTraceRendererTests | 3 | 5 | 整数サンプル境界と書き込み順、観測区間・順序の拒否、量子化 VGM の固定時刻と長待機分割 |
| **合計** | **33** | **133** | 検出数・成功数ではなく、未実行テスト属性の静的集計 |

- NES の duty と high／divider、Noise の period／feedback は [NESdev Pulse](https://www.nesdev.org/wiki/APU_Pulse)・[NESdev Noise](https://www.nesdev.org/wiki/APU_Noise) の取得できた検索本文を参照した。直接取得は403だった。GB の duty 位相／DAC／Noise の状態表現は [Pan Docs Audio Details](https://raw.githubusercontent.com/gbdev/pandocs/master/src/Audio_details.md)、周期／trigger／NR32／NR50 は [Audio Registers](https://raw.githubusercontent.com/gbdev/pandocs/master/src/Audio_Registers.md) と照合した。
- 再合成器本体は BCL とテスト内インターフェースだけを参照する。Song／ControlTimeline／Formats の型を渡すのはテストの接続部分だけであり、既存の合成器・PitchTable・NoiseOscillator・生成レジスタ定数・既存ゲートモデルを呼ばない。Core PCM とのビット／RMS 一致は要求しない。
- 使用した自前型の定義と namespace、.NET 10.0.8 のローカル参照 XML、xUnit 2.9.2 の Assert API を確認した。追加12 C#ファイルの括弧対応、日本語 summary XML／public への隣接、ブロック namespace、末尾空白、未使用定数・フィールドを静的に確認した。`Assert.Single(collection, predicate)` を使用し、Where を渡す形式はない。
- `src/` 全体、両設計書、README、Directory.Build.props、テスト csproj の SHA-256 は編集前と一致する。Core／Formats／CLI／MCP／DAW／NuGet 依存／JSON version 1 の変更はない。既存テストの削除・期待値変更も行っていない。
- git 操作、アプリ／Unity 起動、コンパイル、`dotnet build`／`dotnet test`、生成プログラムや再合成テストの実行は行っていない。アロケーション計測テストは追加していない。実 NSF 保存・CPU 実行・PCM 相当の配列生成も、今回作成した未実行テストコードの内容である。

## M3-H1 / H2 未完了

H1→H2 の順で予定した実装・テストコード作成と静的確認を完了した。コードの残作業はない。次の受け入れ条件の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` の警告・エラーゼロ。xUnit アナライザを含むコンパイル確認。
- 今回追加133ケースと既存テスト全件の成功・実際の検出件数。独立モデル単体、実ファイルを通す NSF／VGM 相互比較、再合成の周期／左右／非無音／停止を含む。
- 既存 PCM・JSON・GC 回帰。Core と既存テストは変更していないが、回帰テストの成功を再確認したわけではない。
- 外部プレイヤー／実機での互換性・聴取は未検証。テスト用の線形ミキサー、frame counter 即時モデル、GB Wave 起動バッファ省略は実機サイクル互換の保証ではない。

## M3-H1 / H2 変更ファイル一覧

更新:

- `docs/implementation.md`

新規（すべて `tests/Arpeggio.Core.Tests/Formats/`）:

- `IRegisterTraceChip.cs`
- `RegisterTraceRenderer.cs`
- `RegisterTraceAssertions.cs`
- `RegisterTraceRendererTests.cs`
- `NesRegisterTraceChip.cs`
- `NesRegisterTraceChipTests.cs`
- `NesRegisterResynthesisTests.cs`
- `QuantizedNesVgmFixture.cs`
- `GameBoyRegisterTraceChip.cs`
- `GameBoyRegisterTraceChipTests.cs`
- `GameBoyRegisterResynthesisTests.cs`
- `GameBoyRegisterRetriggerTests.cs`

# M3-F3 / G1 実装記録（2026-09-08）

## M3-F3 設計との差

- 新三ツールだけに共通の MCP 変換応答を設け、既存 Invoke のセッションロック内で実行する。成功は既存 MCP と同じく error を省略し、path / written / dryRun / destinationExists / exitCode / report を返す。失敗は code / error / exitCode を加え、I/O 失敗で確定済み report を変更しない。
- channelMap の文字列境界は CLI の map と同じ厳密なキー・配列検証を MCP 内で行う。CLI のファイル読み取りや側車履歴に依存させず、MCP import は MidiSongFile だけで新規 JSON を保存する。既存 MCP と同じく側車履歴は操作しない。

## M3-F3 実装・静的確認

- ArpeggioTools に export_nsf / export_vgm / import_midi を追加。既存 23 ツールのメソッド・引数・処理は変更していない。登録名の既存契約テストは 26 名へ拡張した。
- export は共有ロック内で現在の Song を Prepare し、session.Path を入力保護へ渡す。import は未オープンでも実行でき、SourceName・基準テンポ・量子化・polyphony・JSON 文字列 map・title・strict を既存 Formats へ接続する。
- ConversionToolsTests に引数既定値、現在曲と共通サービスの byte / report 一致、三チップ import、dry-run、strict、0 / 1 / 2 / 3、既存出力・入力保護、共有 Song / Path / undo / redo 不変、不正 map / MIDI を追加した。
- 自前型の定義・namespace、既存 SessionOutput / Invoke の JSON とロック、保存 API の所有権・例外分類を照合した。F3 のコード・テスト作成と静的確認を先に完了して G1 へ進む。

## M3-F3 未完了

- コードの予定範囲は追加済み。dotnet build / dotnet test は依頼に従い未実行。警告・エラーゼロ、既存全件と新規契約テストの成功は依頼者側の確認待ち。
- MCP ホストからの呼び出し、実ファイル保存、外部プレイヤー／実機確認は未実施。

## M3-G1 設計との差

- 設定・診断は既存右ペインへ「書き出し」タブを追加して表示する。ExportPresenter が選択先・設定の有効性・診断済み plan を保持し、保存時に再変換しない。設定変更は plan を破棄するが、診断後の通常の曲編集は開始時スナップショットを維持する。
- Prepare に CancellationToken がないため、Task.Run の開始前・変換終了後にキャンセルを確認する。終了・文書切替では結果を破棄して保存と View 通知を抑止する。保存は既存 Write のキャンセルと一時ファイル清掃へ委ねる。
- OS ピッカーの ShowOverwritePrompt を有効にし、選択確定時に存在した出力だけ上書き許可を渡す。新規候補が診断中に作られた場合は Write の新規移動で拒否する。入力パスは選択時に固定し、手入力した拡張子も Presenter と Formats の双方で検証する。

## M3-G1 実装・静的確認

- ExportFileTypes のチップ別拡張子候補を OS ピッカーと手入力の検証で共用する。NES は WAV / OGG / NSF / VGM、GB は WAV / OGG / VGM、SNES は WAV / OGG。既存の WAV / OGG の設定と SongFileExporter は変更していない。
- ChipExportView を右ペインへ追加。loops / author / NSF 専用 copyright / strict、形式選択時の制限、診断と保存を非モーダルに表示する。末尾余白・sample rate は追加していない。低いウィンドウでも入力と診断をスクロールできる。
- ExportPresenter が選択時の入力パス・上書き許可・診断済み plan を保持する。Prepare は開始時の独立 Song、Save は同一 plan と確定パスを使用し、成功後だけ LastExportedPath を更新する。失敗時は同じ plan で再試行できる。設定変更・文書切替・終了では候補を破棄する。
- 既存の Task.Run / IsRunning / CancellationTokenSource / UI ディスパッチの流儀に合わせ、音声・診断・保存を相互に二重開始不可とした。文書の Opened 購読は Dispose で解除する。終了後・キャンセル後はキュー済み通知も公開せず、View が終了通知を破棄しても実行状態を解放する。
- ChipExportPresenterTests に候補の大小文字・適合性、同じ plan の保存、長さ／予定サイズ／位置付き警告、strict と恒常的制限の区別、設定変更、既存出力・診断中の新規競合・入力保護、I/O 再試行、文書切替を追加。ChipExportLifetimeTests に準備／保存中の二重開始、編集からの隔離、終了キャンセル、キュー済み結果抑止、終了通知破棄、変換中の文書切替を追加した。

## M3-F3 / G1 最終静的確認

- 新規テストは 3 クラス・21 メソッド・51 ケース（属性からの静的集計）。MCP の既存ツール列挙テストを 26 名へ拡張し、FakeMainWindowView に任意の遅延ディスパッチ境界を追加した。既存テストの削除・期待値の緩和はない。アロケーション計測テストは追加していない。
- Core / Formats の型定義と namespace、Avalonia 12.1.2 のローカル参照 XML（TextChangedEventArgs / IsCheckedChanged / FilePickerSaveOptions）、.NET 10.0.8 の Task / TaskCompletionSource / File / Path / Array、xUnit 2.9.2 の Assert API を照合した。
- 変更 C# の字句上の括弧対応・ブロック namespace・summary XML・末尾空白、XAML と csproj の XML を静的確認した。View に Presenter というプロパティは追加していない。Assert.Single に Where の結果を渡していない。
- 開始時 SHA-256 と比較して Core / Formats / CLI / 両設計書は不変。MCP と DAW に Formats の ProjectReference だけを追加し、NuGet と JSON version 1 は変更していない。
- git 操作、作業ディレクトリ外への書き込み、アプリ／Unity 起動、コンパイル、dotnet build / dotnet test は行っていない。README に新三ツールの引数・report と DAW の診断／保存手順を追記した。

## M3-G1 未完了

コードの予定範囲とテストコードは追加済み。受け入れ条件の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` の警告・エラーゼロ。C# / XAML のコンパイルと xUnit アナライザを含む。
- 既存全件と追加 51 ケースの成功・実際の検出件数。既存 WAV / OGG、MCP の全ツール、保存競合・キャンセル・一時ファイル清掃・同一 plan の保存を含む。
- DAW 実画面のレイアウト、NSF / VGM の OS ピッカーの候補と手入力・上書き確定、Ctrl+E、設定変更・strict・保存・終了時の表示。MCP stdio ホスト経由の呼び出し。
- NSF / VGM の外部プレイヤー／実機・聴取確認。今回の静的確認と未実行テストコードを実機検証済みとは扱わない。

## M3-F3 / G1 変更ファイル一覧

更新（12 ファイル）:

- `README.md`
- `docs/implementation.md`
- `src/Arpeggio.Mcp/Arpeggio.Mcp.csproj`
- `src/Arpeggio.Mcp/ArpeggioTools.cs`
- `src/Arpeggio.Daw/Arpeggio.Daw.csproj`
- `src/Arpeggio.Daw/Presenters/ExportPresenter.cs`
- `src/Arpeggio.Daw/Presenters/MainWindowPresenter.cs`
- `src/Arpeggio.Daw/Views/AudioFilePicker.cs`
- `src/Arpeggio.Daw/Views/MainWindow.axaml`
- `src/Arpeggio.Daw/Views/MainWindow.axaml.cs`
- `tests/Arpeggio.Core.Tests/Mcp/ArpeggioToolsTests.cs`
- `tests/Arpeggio.Core.Tests/Daw/FakeMainWindowView.cs`

新規（9 ファイル）:

- `src/Arpeggio.Mcp/McpConversionExecution.cs`
- `src/Arpeggio.Mcp/McpMidiChannelMap.cs`
- `src/Arpeggio.Daw/Presenters/ExportFileTypes.cs`
- `src/Arpeggio.Daw/Presenters/ChipExportReportText.cs`
- `src/Arpeggio.Daw/Views/ChipExportView.axaml`
- `src/Arpeggio.Daw/Views/ChipExportView.axaml.cs`
- `tests/Arpeggio.Core.Tests/Mcp/ConversionToolsTests.cs`
- `tests/Arpeggio.Core.Tests/Daw/ChipExportPresenterTests.cs`
- `tests/Arpeggio.Core.Tests/Daw/ChipExportLifetimeTests.cs`


## 提案

- channelMap の JSON 解析を Formats に共通化する。CLI / MCP と今後の G2 で重複キー・数値境界の検証がずれるのを防ぐ。見積もり: 1 ラン。本ランでは実装しない。

# M3-G2 / H3 実装記録（2026-09-09）

## M3-G2 設計との差

- 既存 SFX の文書切替準備は未保存編集を自動保存する。MIDI の「勝手に保存・破棄しない」を優先し、「開く」の直前にドラッグを確定し、未保存・外部変更確認待ち・正本の外部変更を検査して拒否する。利用者が既存の保存／再読み込み操作で解消した後に、既存 Open 経路へ渡す。新規保存ではこの準備を呼ばない。
- 右ペインに MIDI タブを追加する。入力文字列の検証と map ファイル境界は専用 Presenter 側に置く。過去の共通 map 解析の提案は本ランでは実装せず、CLI と同じ厳密な UTF-8／重複キー検証を DAW に限定して追加する。
- Import は CancellationToken を受けないため、開始前・終了後にキャンセルを確認し、遅い結果とキュー済み通知を抑止する。保存は MidiSongFile のキャンセル・新規移動・一時ファイル清掃に委ねる。

## M3-G2 未完了

- G2 の予定実装・テストコード作成と静的確認は完了した。ビルド・テスト実行・実画面確認は依頼者側に残る。受け入れ条件の実行確認が済んだという意味ではない。

## M3-G2 実装・静的確認

- 専用 MidiImportPresenter / View と右ペインの MIDI タブ、下部の「MIDI を取り込む」を追加した。MIDI・新規 JSON・任意 map の OS ピッカー、チップ・基準 BPM・量子化・声数不足時の処理・任意曲名・strict を共通 Import へ渡す。
- 変換時の MidiImportResult が持つ確定済み JSON を新規保存する。元 MIDI・候補 Song・現文書の後編集を保存へ混ぜない。採用・声数不足による破棄・打ち切り・明示除外・ゼロ音量／ゼロ長と、実際の channel → 出力トラック別採用数を表示する。共通診断表示に sourceChannel を追加した。
- 新規保存は current Song / Path / undo / redo を変更せず、側車履歴を操作しない。Open だけがドラッグ確定・未保存／外部競合の検査後に既存 Open・再生／画面／ファイル監視の切替を呼ぶ。Open の失敗でも保存成功と再試行先を保持する。
- 確認・保存の二重開始を拒否する。設定変更・候補破棄・文書切替で旧候補を破棄し、処理中のキャンセルと終了後の遅延結果／キュー済み通知を抑止する。保存競合・I/O 失敗は候補を保持して再試行可能。確定済みファイルはキャンセル後も保持する。
- Presenter テストに三チップの候補／JSON 一致、current と undo / redo の保持、独立 Open・未保存・外部競合・Open 読み取り失敗、strict、保存先競合、map・数値・不正 SMF、終了／キャンセル・通知破棄・文書切替を追加した。
- 自前型の定義・namespace と Avalonia 12.1.2 のローカル参照 XML を照合し、追加 C# の括弧対応・ブロック namespace・日本語 summary XML、XAML XML、禁止省略名・禁止 API を確認した。View に Presenter というプロパティは追加していない。Assert.Single に Where を渡していない。
- G2 のコード・テスト作成と静的確認を先に完了して H3 へ進む。コンパイル・アプリ起動・テスト実行は行っていない。

## M3-H3 設計との差

- 仕様変更はない。ビルド・テスト実行禁止の依頼に従い、全件・GC 回帰はテストコードの追加と静的確認までとする。設計書の受け入れ条件が求める実行成功は未確認として分離する。
- OGG は既存エンコーダーがストリーム識別子をランダムに生成するため、フロントエンド間の全 byte 一致を要求しない。入力 PCM の非無音、コンテナ境界・EOS・最終 granule のフレーム数を検証する。Vorbis デコード後の音声比較は追加していない。

## M3-H3 実装・静的確認

- format 1・PPQN 480、120→60 BPM、三和音と kick、後半 A4 を含む共通 MIDI を追加した。三チップで version 1 JSON、5 採用音、144 tick／1.5 秒、全体テンポ焼き込み、候補と保存往復後の PCM 全ビット・RenderReport 一致を検証する。
- MIDI→JSON→WAV／OGG を三チップで接続。WAV の読み戻しレート・長さ・非無音、OGG のページ境界・EOS・最終 granule を確認するテストを追加した。
- NES／GB の MIDI→JSON→VGM は有限一／二周、独立パースの待機総和、独立レジスタ再合成の後半 A4 周波数・非無音・終端停止を検証する。NES はさらに NSF 保存→独立ロード→限定 CPU の INIT／PLAY→全トレース照合→再 INIT を接続した。
- CLI と MCP は同じ MIDI の新規保存・明示 Open から全対応形式へ接続する。JSON・WAV・VGM・NSF の byte 一致と OGG の長さを検証する。DAW も候補確認→新規保存→明示 Open→既存 WAV／OGG および対応 VGM／NSF を通し、切り替えたチップと曲が実際の出力へ渡ることを検証する。
- strict・対象外チップ・loops 上限・既存出力競合・キャンセル・移動先ディレクトリとの競合で、元 MIDI・保存 JSON・元 Song・既存出力を保持し、隣接一時ファイルを残さないテストを追加した。OS により IOException／UnauthorizedAccessException が異なる移動失敗は両方を認め、ファイル保持を主条件にする。
- MidiRenderAllocationTests は AllocationCollection に所属する。三チップの保存往復・チップ変換後に二周を暖機し、次の一周の発音・音色交代・周回で Render が GC 0 byte を保つことを検証する。既存の Render／NoteOn／音色交換のアロケーションテストは変更していない。
- README の状態・DAW 操作を更新し、MIDI→JSON→各形式の CLI 例、チップ別対応表、DAW の候補・保存・独立 Open、既知制限・実機未検証を追記した。既存 CLI／MCP のコマンド名・引数・終了コードは変更していない。

## M3-G2 / H3 最終静的確認

| 新規テストクラス | メソッド数 | 属性からのケース数 |
|---|---:|---:|
| MidiImportPresenterTests | 8 | 11 |
| MidiImportInputTests | 6 | 22 |
| MidiImportLifetimeTests | 4 | 6 |
| MidiImportOutputTests | 1 | 3 |
| MidiPipelineTests | 3 | 10 |
| MidiRenderAllocationTests | 1 | 3 |
| MidiOutputPipelineTests | 1 | 3 |
| **合計** | **24** | **58** |

- 件数は未実行のテスト属性の静的集計で、ランナーの検出件数・成功件数ではない。既存テストの削除・期待値の緩和はない。既存テスト補助型 FakeMainWindowView に MIDI 表示通知の記録だけを追加した。
- 自前型の定義・namespace、Avalonia 12.1.2 と .NET 10.0.8 のローカル参照 XML、既存 xUnit API の利用例を照合した。新規 C# の括弧対応・ブロック namespace・日本語 summary XML／public への隣接・末尾空白、XAML XML を検査した。View の Presenter プロパティ、Assert.Single に Where を渡す形式、禁止 API の追加はない。
- 編集前 SHA-256 と照合し、Core／Formats／CLI／MCP／Codecs の実装、設計書二つ、プロジェクト依存、既存テスト本体は不変。ファイル削除なし。Formats は Core と BCL のみ、NuGet と JSON version 1 は変更していない。
- git 操作、作業ディレクトリ外への書き込み、コンパイル、dotnet build／dotnet test、アプリ／Unity 起動、音声生成は行っていない。上記のファイル生成・CPU 実行・再合成は今回作成した未実行テストコードの内容である。

## M3-H3 既知の制限・実機で未検証の条件

- **NSF／VGM は実機で未検証。外部プレイヤーでの互換性・聴取も未検証。** 自動テストに合格した場合でも実機検証済みとは扱わない。本ランでは自動テスト自体も未実行。
- NSF は NTSC 基本 NES APU と標準 NSF バンク切り替え向け。NSF2／NSFe、PAL 演奏、DPCM、拡張音源、起動可能な NES ROM は対象外。PLAY 量子化・極短ノート衝突・CPU／ROM 上限があり、ASCII メタデータへの縮約を診断する。
- VGM は v1.71 の NES APU／DMG、一ファイル一チップ。SNES VGM／SPC、VGZ、MIDI 書き出しは対象外。SNES の配布は既存 WAV／OGG を使う。NSF／VGM は 1〜16 回の有限展開で、無限ループ情報を保存しない。
- 通常の Core PCM と実機レジスタ出力のビット一致・同音は保証しない。NES Pulse の high 書き込みによる位相再開、Triangle DAC 保持、Noise seed、GB の音量段階・再トリガー・DAC／LFSR と Wave RAM、連続パンやミキサー等に差がある。独立再合成器も限定サブセットであり、アナログ特性や全 CPU／APU サイクルを再現するものではない。
- MIDI は SMF format 0／1・PPQN・MIDI 1.0 のファイル入力のみ。format 2、SMPTE、RMID、UMP／MIDI 2.0、ライブ入力、SysEx 実行は非対応。固定整数 BPM／48 ticks、チップ声数・音域・4 bit 音量・GM 近似音色・固定ドラム gate に変換する。元のテンポ地図・表情・和音・音色へ無損失には戻せない。SNES プリセットの既存の音程偏差とワンショット終端も維持する。
- DAW の Import 本体は同期 API のため、実行開始後の解析計算は即時中断できない。キャンセル・終了時は結果の公開と次の保存を抑止し、保存 API へはキャンセルを渡す。保存先への移動が既に完了したファイルは取り消さず保持する。キャンセルと保存確定の競合ではファイルが存在する場合がある。
- 実機実績の記載前に、使用機種・NSF カートリッジ／プレイヤー名と版・NTSC 動作を記録する。先頭発音、3 分以上のテンポ、4 KiB bank 越え、終了・再 INIT、Pulse high 境界、Triangle／Noise の発音・停止・聴取差を実際に確認する。GB VGM も利用プレイヤー／実機経路・機種を明記して Wave／DAC／再トリガー／routing の差を確認する。

## M3-H3 未完了

G2→H3 の順で予定したコード・テストコード・README／実装記録を追加した。コードの予定範囲に残作業はない。受け入れ条件の次の実行確認は依頼者側に残る。

- `dotnet build Arpeggio.slnx` の警告・エラーゼロ。C#／XAML のコンパイルと xUnit アナライザを含む。
- 既存 676 件を含む全テストと今回追加 58 ケースの成功、実際の検出件数。676 は M3 前の基準であり現在の総数ではない。
- 既存 PCM／JSON／RenderReport と全 GC 回帰、今回の MIDI 保存往復・各出力・NSF CPU／VGM 再合成・ファイル保持・キャンセル境界の実行。M3 前の別途採取済み PCM 基準があればその比較も行う。
- DAW 実画面での五つのタブ、低いウィンドウでのスクロール、各 OS ピッカーのキャンセル・新規パス、入力エラー・strict・候補統計・独立 Open・未保存／外部競合保護・終了時表示。MCP stdio ホスト経由の既存／新規ツール呼び出し。
- 外部プレイヤーと実機・聴取の検証、および上記の機種・版・条件の記録。

## M3-G2 / H3 変更ファイル一覧

更新（8 ファイル）:

- `README.md`
- `docs/implementation.md`
- `src/Arpeggio.Daw/Presenters/MainWindowPresenter.cs`
- `src/Arpeggio.Daw/Presenters/IMainWindowView.cs`
- `src/Arpeggio.Daw/Presenters/ChipExportReportText.cs`
- `src/Arpeggio.Daw/Views/MainWindow.axaml`
- `src/Arpeggio.Daw/Views/MainWindow.axaml.cs`
- `tests/Arpeggio.Core.Tests/Daw/FakeMainWindowView.cs`

新規（15 ファイル）:

- `src/Arpeggio.Daw/Presenters/MidiImportInput.cs`
- `src/Arpeggio.Daw/Presenters/MidiImportPresenter.cs`
- `src/Arpeggio.Daw/Presenters/MidiImportChannelMapFile.cs`
- `src/Arpeggio.Daw/Presenters/MidiImportReportText.cs`
- `src/Arpeggio.Daw/Views/MidiImportView.axaml`
- `src/Arpeggio.Daw/Views/MidiImportView.axaml.cs`
- `tests/Arpeggio.Core.Tests/Daw/MidiImportDawFixture.cs`
- `tests/Arpeggio.Core.Tests/Daw/MidiImportPresenterTests.cs`
- `tests/Arpeggio.Core.Tests/Daw/MidiImportInputTests.cs`
- `tests/Arpeggio.Core.Tests/Daw/MidiImportLifetimeTests.cs`
- `tests/Arpeggio.Core.Tests/Daw/MidiImportOutputTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiPipelineFixture.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiPipelineTests.cs`
- `tests/Arpeggio.Core.Tests/Formats/MidiRenderAllocationTests.cs`
- `tests/Arpeggio.Core.Tests/Cli/MidiOutputPipelineTests.cs`

## 既知の制限（依頼者側で実測・2026-09-09）

### OGG の最終 granule が入力より 1024 サンプル小さい

`OggVorbisEncoder` 1.2.2 が書く最終 granule position は、入力フレーム数より**常に 1024（1 ブロック）小さい**。4410 / 22050 / 44100 / 66150 / 132300 フレームで実測し、**曲の長さに依らず一定**であることを確認した。終端のパケット排出から `OggStream.Finished` のガードを外しても変わらないため、こちらの書き出し漏れではなくライブラリの granule 計算のクセと判断した。

再生側が末尾 23 ms を切るかどうかは未確認（このリポジトリに Vorbis デコーダが無いため）。`MidiPipelineTests.AssertOggEndOfStream` は実測値に合わせ、定数 `VorbisGranuleDeficit = 1024` で判定している。WAV 書き出しにはこの制限はない。



# SFX-A1 実装記録（2026-09-09）

## SFX-A1 設計との差

- API の具体形は未指定のため、C# 9 の immutable record によるパラメータ値、仕様 Catalog、Validator、部分 JSON patch の純粋適用を追加する。チップは引数で受け取り、パラメータ内へ重複保持しない。プリセット Catalog は D1、保存形式への接続は C1 へ残す。
- 数値は丸め前に型・有限性・範囲を検証し、実数を小数点以下6桁 AwayFromZero へ正規化してから項目間の制約を検証する。範囲外を丸めで救済しない。duty の12.5は表の明示値を優先し、整数限定の選択値には含めない。
- 保存往復で punch の可否が変わらないよう、半フレームの判定も正規化した秒数を用いる。例: 1/120秒は0.008333秒となり0フレーム、0.0083335秒は0.008334秒となり1フレーム。整数項目の12.0・1.2e1は整数値として受理するが、小数部分の丸めやアンダーフローによる救済は行わない。
- 空 patch の境界を明確にするため、空の入れ子オブジェクトも InvalidParameter とする。キーと文字列選択値は表の正式表記に限定する。未知パス・異なるチップの項目は UnsupportedParameter、既知項目の型・値・重複・構造違反は InvalidParameter とし、正規パスを例外へ保持する。

## SFX-A1 実装範囲

- `SfxParameters` とトーン・ノイズ・共通包絡・NES/GB/SNES の値オブジェクトを追加。公開値は `init` のみで、部分変更は record の値比較と `with` で扱う。チップ固有設定は現在の一つだけ必須とする。
- `SfxParameterCatalog` は共通20項目＋NES 5項目＋GB 5項目＋SNES 2項目の計32項目。パス、入力型、単位、既定値、範囲、離散選択、ゼロ無効値、UI刻み・微調整・対数軸、条件の説明とチップ適合性を共有する。値読み取り・候補への置換も同じ定義へ紐付け、Validator と patch に範囲表を複製しない。
- `SfxParameterValidator` は非有限数、範囲外、不正 enum、欠落したオブジェクト、異なるチップの混在、全レイヤー OFF、sustain/punch、各包絡の300フレーム上限を検証する。無効レイヤーも検証し、全有効レイヤーの音量が0の場合だけ `SilentParameters` を返す。実数は6桁 AwayFromZero と正のゼロへ正規化する。
- `SfxParameterPatch.Apply` はネスト構造・重複キーを含め全件解析し、完全な候補に対して項目間制約を検証する。省略は保持、元値は不変。正規化後に同値なら `Changed=false`。null・配列・空patch・未知項目・型違い・異なるチップの項目は、安定コードとパスを持つ `SfxParameterException` で拒否する。
- JSON の数値解析はカルチャ非依存。整数は指数・小数表記でも数学的な整数なら受理する。double 変換で小数が消えるケースと、微小な正の repeat が0へアンダーフローして無効化されるケースを拒否する。`JsonDocument` は解析呼び出し内で破棄する。
- 合成、Song、Serializer、保存・履歴、CLI/MCP/DAW、旧プリセット、音色バンク、csproj/slnx、依存パッケージ、JSON version は変更していない。パラメータ8プリセット・生成診断・生成器・保存形式への接続は後続ランのまま。

## SFX-A1 テストコード

- `SfxParameterCatalogTests`: 設計表を独立に記述した全28数値行の既定値・単位・型・両端・範囲外、32パスの一意性、三チップの可否、UI刻み、全選択肢。
- `SfxParameterValidatorTests`: 全21実数項目への NaN/正負Infinity、不正構造・チップ・enum、無効レイヤー、全OFFと無音の区別、半フレーム前後のpunch条件、独立包絡・最大300フレーム、最短decay、repeatの離散した範囲、正負の6桁丸め・負ゼロ。
- `SfxParameterPatchTests`: 全項目の型への写像、3チップの完全patch、順序不変、部分更新・省略保持・途中失敗時の不変、同値no-op、全パスのnull/配列/型違い、重複（エスケープ同値キーを含む）、未知キー、全離散選択値、指数表記・整数の精度落ち・アンダーフロー。
- テスト用JSON読み取りは Catalog の内部アクセサーを呼ばず、パラメータ型を独立にシリアライズして写像を照合する。既存テストの期待値は変更していない。

## SFX-A1 静的確認

- 指定設計・M2-A/SFX関連の実装判断、既存SFX/音色/VoiceModulation、CLI/MCP/DAWの既存入口を参照した。
- 既存型の定義と namespace、追加使用したBCL APIのローカル .NET 10参照XMLを照合した。
- 新規C# 19ファイルの括弧対応・doc XML・public summaryの隣接・1ファイル1型・ブロックnamespaceを確認。禁止省略名、Unity lifecycle API、`Assert.Single(...Where(...))` の追加なし。Catalogの32パスと読み取り先の一致を確認した。
- 開始時に採取したSHA-256と照合し、既存 `src/`・`tests/` 全ファイル、および変更禁止の3設計書が不変であることを確認した。指紋の一時ファイルは除去した。git操作は行っていない。
- この静的確認はコンパイル・テスト成功やPCM一致の実測を意味しない。

## SFX-A1 未完了

A1 の実装上の残タスクはなし。受け入れ完了には依頼者／Claude Codeによる次の確認が必要。

- `dotnet build Arpeggio.slnx` の警告ゼロとコンパイル成功。
- 新規テストを含む全既存テストの成功、および xunit アナライザの確認。
- 旧8プリセット×3チップのJSON/PCM回帰確認。合成・保存経路を変更していないことは静的に確認済みだが、音声生成・実測は未実行。

依頼に従い `dotnet build` / `dotnet test` / コンパイル / アプリ起動は実行していない。

## SFX-A1 変更ファイル一覧

- `src/Arpeggio.Core/Sfx/SfxEnvelopeParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxGameBoyParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxNesParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxNoiseParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterCatalog.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterDescription.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterException.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterPatch.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterPatchResult.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterValidator.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterValueKind.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterWarning.cs`
- `src/Arpeggio.Core/Sfx/SfxParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxSnesParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxToneParameters.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxParameterCatalogTests.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxParameterPatchTests.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxParameterTestJson.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxParameterValidatorTests.cs`
- `docs/implementation.md`

# SFX-C1 実装記録（2026-09-09）

## SFX-C1 設計との差

- 公開 API の具体形は未指定のため、既知版 DTO と不透明 JSON を排他的に保持する `SfxDefinition`、保存境界、指紋計算、読み取り専用の同期判定を分ける。C1 では生成器や編集入口を追加せず、コピー境界・履歴・DAW 正本保存の接続は分割表どおり C2 に残す。
- 未知版の構造を現在版で検証しないため、正の整数の schemaVersion → generatorVersion → random algorithmVersion の順で判定し、未知版に到達した時点で sfx 全体を保持する。既知版の必須キー欠落・重複・未知キー・不正 hash はドキュメントエラー。未知版の保持は JSON のキー・値・配列順を対象とし、入力の字下げや文字列エスケープの表記は保存時の整形に従う。
- 出自の適用外項目は明示 null とし、randomize は category 必須・strength/locks/baseParametersHash は null、mutate は category=null・strength/locks/baseParametersHash 必須とする。正式名・正規ロックパスを検証する。両 hash 不一致時は SavedParametersChanged を主理由とし、GeneratedContentChanged も診断一覧へ残す。

## SFX-C1 実装範囲と判断

- `Song.Sfx` を末尾の optional プロパティとして追加した。未指定・null は保存時に省略し、JSON version=1、既存キー順・改行設定・音色マクロの null 表現を維持する。重複したトップレベル sfx は拒否する。
- `SfxDefinition` は既知版 `SfxDefinitionData` と未知版 `JsonElement` を排他的に保持する。未知版は Clone で読み込み元 JsonDocument から独立させ、破棄後もキー・値・配列順を再保存できる。音声側は定義を解釈しない。
- 既知版の保存パラメータは全正規パスの存在を先に確認し、A1 の patch 解析と Validator で型・範囲・未知/重複キー・チップ適合性・項目間制約を検証する。保存キーからチップを仮判定した後、SongValidator で Song.Chip との一致を必ず確認する。欠落を初期値で補完しない。
- パラメータ型7ファイルに JsonPropertyOrder を付け、型の宣言順を明示した。現在チップ以外の null 設定は sfx 内でのみ省略し、noiseMode / waveform は小文字の正式名、実数は6桁 AwayFromZero・正のゼロへ正規化する。保存は元の値オブジェクトや両指紋を書き換えない。
- 出自は sourcePreset と、operation / algorithmVersion / uint32 seed / category / strength / locks / baseParametersHash を保存する。保存済み locks は読み取り専用の配列参照にし、保存順を維持する。乱数生成・プリセット生成は追加していない。
- `SfxHash` は parametersHash、generatedHash、後続の競合照合に使う revision を提供する。Utf8JsonWriter の2スペース・LF・BOMなし・末尾改行なしを明示する。生成指紋は Serializer の規定順から title と sfx だけを除き、revision は両方を含む。Serialize の OS 既定改行は変更しない。
- `SfxSynchronization.Inspect` は MissingDefinition / UnsupportedSfxVersion / SavedParametersChanged / GeneratedContentChanged を返す。既知版で両指紋が一致した場合だけ editable=true と現在 parameters を返し、不一致時は savedParameters と全理由を返す。読み込み・保存・SongValidator・状態取得による生成や指紋の更新はない。

## SFX-C1 テストコード

- `SfxDefinitionSerializationTests`: 旧8種×3チップの sfx 省略/null、三チップの完全往復、乱数出自の seed=0/max、null 項目、正規化、キー順/CRLF入力、schema/generator/random algorithm の未知版保持、ファイル保存と一時ファイル後始末。
- `SfxDefinitionValidationTests`: 全保存パラメータ・定義・出自の必須キー欠落、版/型/範囲/未知キー/エスケープ同値の重複、不正 hash、チップ不一致、正式名、カテゴリ生成と変異の適用外項目、ロックの不正パス、直接構築した NaN を文書エラーで拒否。
- `SfxHashTests`: 既存 NES の正規 JSON を独立固定し UTF-8 バイト列を比較。三チップの初期パラメータと NES 生成領域の SHA-256 を独立に計算した固定期待値で検証。版・6桁丸め・無効ノイズ値・title/sourcePreset/sfx と revision の関係も検証する。
- `SfxSynchronizationTests`: タイトルのみの変更、マクロ/ノート/効果/音色名/トラック名/ミュート/定位/空トラック/テンポ/長さ/ループ/エコー/FIR/既定音色/音色配列順の編集検出。パラメータだけの JSON 手修正と両方の不一致を区別する。既知・未知定義の保存往復で旧24プリセットの PCM が変わらないことを確認するコードを追加。
- C1 のテスト用定義は、保存済み生成列の指紋を明示的に記録する。B2/B3 の生成器を実装した、あるいはパラメータと生成列の音響的同値性を証明したものではない。既存テストの期待値は変更していない。

## SFX-C1 静的確認

- 指定設計、既存 SFX・音色・VoiceModulation と保存/コピー境界、CLI/MCP/DAW の既存入口を参照した。新規参照型の定義と namespace、.NET 10 の JsonConverter / JsonElement.Clone / JsonNode / JsonWriterOptions.NewLine / SHA256 / Convert.ToHexStringLower をローカル参照 XML と照合した。
- 変更した C# 27ファイルの括弧対応、public summary の隣接、doc XML、ブロック namespace、省略名、xunit の禁止パターンを静的確認した。コンパイルやアナライザを実行した結果ではない。
- 開始時の SHA-256 と比較し、変更禁止の3設計書、既存合成・音色・旧プリセット/音色バンク・CLI/MCP/DAW・既存テスト・プロジェクト/依存設定の不変を確認した。実装記録の既存節もバイト列を維持し、末尾だけに C1 を追記した。
- 生成指紋の計算は保存用キー順を継承し、SongValidator に hash 比較を入れないため再帰しない。Render / AdvanceFrame / NoteOn を変更せず、JSON・hash・正規化をホットパスへ接続していない。
- git 操作、コンパイル、dotnet build / dotnet test、アプリ起動、PCM/WAV生成、試聴は行っていない。

## SFX-C1 未完了

C1 の実装上の残タスクはなし。受け入れ完了には依頼者／Claude Codeによる以下の実行確認が必要。

- `dotnet build Arpeggio.slnx` のコンパイル成功・警告ゼロと xunit アナライザの確認。
- 新規テストおよび既存テスト全件の成功。旧 JSON/PCM 不変・新形式/未知版往復・生成列を再生成しないことの実測。
- macOS / Windows の固定 SHA-256 一致と、各 OS における従来の正規 JSON バイト列の維持。

C2 への申し送り: SongSnapshotPublisher と DawDocument.Save の個別コピーは本ランで接続していない。SfxEditor・一履歴での適用・Undo/Redo・detach/regenerate・revision 拒否・DAW 正本保存を C2 で実装し、既知版・未知版の両方をコピー境界で保持すること。現段階でフロントエンドの SFX 再編集まで受け入れ済みとは扱わない。

## SFX-C1 変更ファイル一覧

- `src/Arpeggio.Core/Document/Song.cs`
- `src/Arpeggio.Core/Document/SongSerializer.cs`
- `src/Arpeggio.Core/Document/SongValidator.cs`
- `src/Arpeggio.Core/Sfx/SfxDefinition.cs`
- `src/Arpeggio.Core/Sfx/SfxDefinitionData.cs`
- `src/Arpeggio.Core/Sfx/SfxDefinitionJsonConverter.cs`
- `src/Arpeggio.Core/Sfx/SfxDefinitionValidator.cs`
- `src/Arpeggio.Core/Sfx/SfxEditabilityReason.cs`
- `src/Arpeggio.Core/Sfx/SfxEnvelopeParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxGameBoyParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxHash.cs`
- `src/Arpeggio.Core/Sfx/SfxNesParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxNoiseParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxParameterJson.cs`
- `src/Arpeggio.Core/Sfx/SfxParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxRandomization.cs`
- `src/Arpeggio.Core/Sfx/SfxRandomizationOperation.cs`
- `src/Arpeggio.Core/Sfx/SfxSnesParameters.cs`
- `src/Arpeggio.Core/Sfx/SfxSynchronization.cs`
- `src/Arpeggio.Core/Sfx/SfxSynchronizationState.cs`
- `src/Arpeggio.Core/Sfx/SfxToneParameters.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxCanonicalJsonExamples.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxDefinitionSerializationTests.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxDefinitionValidationTests.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxDocumentTestData.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxHashTests.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxSynchronizationTests.cs`
- `docs/implementation.md`
# SFX-B1 実装記録（2026-09-09）

## SFX-B1 設計との差

- B1 単独の API 名・中間結果型は未指定のため、`SfxCurveGenerator.Generate(parameters, chip)` と専用の曲線結果を追加する。既存 `Macro` を呼び出しごとに新規生成し、Song・音色・ノートは組み立てない。全軌跡のチップ音域診断と duty/noise 曲線は分割表どおり B2/B3 に残す。
- A1 の判断を継承し、全入力を検証・小数6桁へ正規化した後に時間を量子化する。結果は正規化パラメータ、要求包絡秒数、区間別フレーム数、ゼロ保持込みの長さを別々に保持する。無効レイヤーも検証するが、曲線・時間診断・本体長へ含めない。
- 診断値の具体型・位置規則は未指定。B1 は数値の requested/actual を nullable double、layer を tone/noise（全体は null）とする。時間診断は単一パラメータの秒数比較なのでフレーム範囲は null。無効 jump/repeat はトーン全域の 0〜N（両端を含む）を一件で示し、実効数値を一意に表せないため actual は null とする。時間診断は正規化後の要求秒数と実効秒数が異なる場合だけ返す。
- 診断順はトーン A/S/D → repeat周期 → jump待ち時間 → 無効jump → 無効repeat → ノイズ A/S/D → 無音で固定する。jump=0 でも有効トーンの待ち時間の量子化は報告し、無効jump警告は出さない。InactiveRepeat の条件は設計の明示式（N以上、または slide/delta/jump/dutySweep が全0）をそのまま使い、音響的な追加判定は行わない。

## SFX-B1 実装範囲

- `SfxCurveGenerator` は既存 Validator で全レイヤーを検証してから、有効レイヤーの曲線をオフライン生成する。時間量子化、ASDecay/punch、ピッチの展開はそれぞれ小さな内部型へ分離した。
- `SfxEnvelopeCurve` は A/S/D、要求秒数、実効包絡秒数、終端ゼロ込みの本体秒数と tick 数、VolumeMacro を保持する。全区間を量子化し、0長区間を除算せず、最短減衰1フレーム・各最大300フレーム・列最大301要素・全体最大602 tickを維持する。
- `SfxToneCurve` は anchor、repeat/jump待ちフレーム数、セントの PitchMacro と半音の ArpeggioMacro を保持する。delta は二次項として積分し、repeat 時もビブラートの位相と包絡は進め続ける。ジャンプはセント列へ二重加算せず、終端フレームも同じ式で評価する。
- 全マクロは一定値でも短縮せず、LoopIndex=-1 とする。マクロと配列は呼び出しごと・レイヤーごとに所有し、入力の immutable record は変更しない。結果のマクロは既存型と同じ可変モデルなので、利用開始後は呼び出し側で変更しない。
- `SfxGenerationWarning` は TimeQuantized / InactivePitchChange / InactiveRepeat / SilentParameters を保持する。無音警告は既存 Validator の判定を利用し、dutySweep だけの repeat も NES/GB の有効な戻し対象に含める。
- Song、Serializer、既存音色・合成器・FrameClock・VoiceModulation、旧プリセット、CLI/MCP/DAW、csproj、依存、JSON version は変更していない。

## SFX-B1 テストコード

3クラス・25メソッド・89ケースを静的集計した。テストランナーによる検出・実行は未確認。

| ファイル | 固定した境界・契約 |
|---|---|
| `SfxEnvelopeCurveTests` | 三チップ共通の `[12,8,4,0]` と8 tick、0長 A/S・attack開始0・punch各境界・音量の中間値、6桁正規化後の半フレーム、独立二包絡と有効性、300フレーム/602 tick・過大/非有限の拒否、44100/48000/44101 Hz の既存時計と終端0保持 |
| `SfxPitchCurveTests` | A4と基準Hz端数、正負のdelta二次項・セント中間値、10/15/20 Hz vibrato・深さ0/速度0、待ち0/前後/終端/到達不能のjump、1/2/N/長周期repeat、delta/jumpの解除とビブラート継続、既存VoiceModulationで先頭・一回加算・末尾保持、入力不変と再生成配列の非共有 |
| `SfxGenerationWarningTests` | 時間診断の正規パス・レイヤー・要求/実効値・順序、差なしの省略、無効jump/repeatのN境界と同時診断、dutySweepのチップ差、無効トーンからの診断/長さ漏れ防止、無音の全体一件化、全入力の事前検証 |

## SFX-B1 静的確認

- 指定設計書、M2-A・SFX関連の実装判断、A1の申し送り、既存SFX/音色/VoiceModulationとCLI/MCP/DAWの入口を参照した。
- 使用型の定義・namespace、既存時計・マクロ適用の公開API、ローカル .NET 10 と xunit 2.9.2 の参照XMLを照合した。
- 追加C#の括弧対応・doc XML・public summaryの隣接・1ファイル1型・ブロックnamespaceを確認。省略名、ブレース省略、Unity lifecycle API、`Assert.Single(...Where(...))` を追加していない。
- 開始時のSHA-256と照合し、既存 `src/`・`tests/` と変更禁止の3設計書（計500ファイル）が不変であることを確認した。git操作は行っていない。
- この確認はコンパイル成功・アナライザ警告ゼロ・テスト成功・音声一致の実測を意味しない。

## SFX-B1 未完了

B1の実装上の残タスクはなし。A1の実行確認を含め、受け入れ完了には依頼者／Claude Codeによる以下の確認が必要。

- `dotnet build Arpeggio.slnx` のコンパイル成功と警告ゼロ。
- 既存全件＋追加テストの成功、xunitアナライザの確認。
- macOS/Windowsで固定マクロ列、特に Math.Sin と丸め境界が一致すること。
- 旧8プリセット×3チップのJSON/PCM回帰。既存経路のコード不変は確認済みだが、出力の実測は未実行。

依頼に従い、コンパイル・`dotnet build` / `dotnet test`・アプリ起動は実行していない。Song組立、チップ別duty/noise曲線、全軌跡音域診断、WAVの生成確認はB2/B3の範囲として残る。

## SFX-B1 変更ファイル一覧

- `src/Arpeggio.Core/Sfx/SfxCurveGenerator.cs`
- `src/Arpeggio.Core/Sfx/SfxCurveGenerationResult.cs`
- `src/Arpeggio.Core/Sfx/SfxEnvelopeCurve.cs`
- `src/Arpeggio.Core/Sfx/SfxEnvelopeGenerator.cs`
- `src/Arpeggio.Core/Sfx/SfxPitchCurveGenerator.cs`
- `src/Arpeggio.Core/Sfx/SfxTimeQuantizer.cs`
- `src/Arpeggio.Core/Sfx/SfxToneCurve.cs`
- `src/Arpeggio.Core/Sfx/SfxGenerationWarning.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxEnvelopeCurveTests.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxPitchCurveTests.cs`
- `tests/Arpeggio.Core.Tests/Sfx/SfxGenerationWarningTests.cs`
- `docs/implementation.md`

# SFX-A2 実装記録（2026-09-09）

## SFX-A2 設計との差

- CLI はシリアライズ後のキーの存在でオプション可否・kind 切替の共通項目を判定している。新マクロの null 省略により未設定状態での指定・保持が失敗するため、GbPulse の dutyMacro に限り編集用 JSON に null のキーを補う。保存形式は設計どおり省略する。通常音色パネルの全面改修は行わない。

## SFX-A2 実装・テストコード

- GbPulseInstrument の JSON 末尾に optional DutyMacro を追加。null は省略し、未指定・null・空列は従来 Duty を使う。既存 Validator の1〜4・LoopIndex検証、GbPulseSynthesizer、Formats ControlTrackCursor に接続した。ハードウェア包絡・位相・VGMレジスタ生成の計算順は変更していない。
- `GameBoyDutyMacroTests`: 全4段階・先頭適用・終端保持・LoopIndex、連続位相の固定Duty PCMとの一致、両Pulseの制御値とVGM固定レジスタ値・書込時刻・再トリガー抑止、制御スナップショットの隔離。null/空列は旧波形・ハードウェア包絡の独立計算に対するPCM全バイト一致と、VGM全バイトの等価性を検証する。
- 同テストで旧音色JSONの固定文字列との全UTF-8バイト一致、新プロパティの末尾追加・明示null省略・version1往復・独立コピー・範囲外値/LoopIndex拒否を検証する。
- `InstrumentCommandsTests`: 未設定からの指定、名前だけの部分更新による保持、null解除。`OptionalInstrumentMacroToolsTests`: MCPのJSON全体置換・保存・再オープン。`OptionalInstrumentMacroTests`: DAWの入力コピー隔離・音色選択・名前編集・マクロ編集・Undo/Redo・正本保存。
- `SongRendererAllocationTests` の既存3チップケースを保持し、DutyMacroありのGBケースを追加。発音・マクロ進行・有限ループを通る計測は既存の AllocationCollection 内で行う。

## SFX-A2 未完了

実装・テストコード作成済み。A2 の入力・合成・Formats・保存・通常編集の接続を静的確認してから A3 の実装へ進めた。ビルド・テスト・PCM/VGM実測による受け入れは未完了で、依頼者／Claude Code の実行待ち。

# SFX-A3 実装記録（2026-09-09）

## SFX-A3 設計との差

- A2 と同じ CLI のキー存在判定を SnesSample の volumeMacro にも補完する。ApplyPreset は既存実装がマクロを変更しないため、処理は変更せず保持をテストで固定する。ADSR・DSP音量丸め・BRR・補間・ミキサー・NoteOffの計算順は変更しない。

## SFX-A3 実装・テストコード

- SnesSampleInstrument の JSON 末尾に null 省略の optional VolumeMacro を追加。既存 Validator の0〜15・LoopIndex検証と SnesVoiceSynthesizer の ConfigureMacros の volume 引数へ接続した。未指定・null・空列は従来音量、値ありは既存の Note.Volume／VolumeSlide と0〜15の乗算後、既存の0〜127丸め・ADSR乗算を通る。
- `SnesVolumeMacroTests`: 全量保持ADSR=(15,0,7,0) の初回2サンプルと `[12,8,4,0]`、Note.Volume=15/9、最終0保持を独立したDSP期待値で検証。null/空列は追加前のDSP音量計算との全バイト比較。ゼロ中もサンプル位置を進め、LoopIndexで音量復帰しても位相を再開しないことと、再発音の先頭復帰を検証する。
- 同テストで6内蔵波形・DSPノイズ・埋め込み素材・プリセットのnull/空列/全量マクロ、既存ピッチ・アルペジオ・VolumeSlide・NoteOffを含むPCM全バイト等価を44100/48000/44101 Hzで検証する。
- `SnesVolumeMacroRenderTests`: 8tickの包絡列と終端ゼロフレームをSongRendererへ接続。トーン/DSPノイズ、tail=0/0.05、44100/48000/44101 Hz、DSPレート変換の履歴が消えた後と本体最終サンプルの0、1/733/1024フレームでの分割RenderとReset後の再生の全バイト一致。
- `SnesVolumeMacroInstrumentTests`: 固定した旧音色JSONの全バイト不変、末尾追加・null省略・version1往復・独立コピー、0/15の受理と範囲外・null配列・不正LoopIndex拒否、全16音色のApplyPreset後のマクロ保持。
- CLIの未設定からの指定・プリセット差替え保持・解除、MCPのJSON置換・保存・再オープン、DAWのコピー・選択・通常編集・履歴・正本保存、および既存プリセット選択テストへVolumeMacro保持を追加した。
- `LegacySfxMacroCompatibilityTests`: 旧8プリセット×GB/SNES、44100/48000 Hz、tail=0/0.05でnullの保存往復と空列を比較し、float PCMとWAVを全バイト照合する。旧プリセットfactory自体は変更していない。
- `SongRendererAllocationTests` にVolumeMacroありのSNESケースを追加し、NoteOn・AdvanceFrame・Render・ループの既存GC0計測へ含める。配列は計測前に用意する。

## SFX-A2 / A3 静的確認

- 指定設計、M2-A/SFXとSNES/Formatsの既存判断、Sfx・Instruments・VoiceModulation、CLI/MCP/DAWの入力・コピー・保存経路を参照した。
- 追加参照型の定義・namespaceと公開APIを検索照合し、MemoryMarshal.AsBytes / JsonIgnoreCondition.WhenWritingNull と xunit のコレクション用assertのローカル参照XMLを確認した。
- 変更17 C#ファイルの括弧対応・doc XML・public summary隣接を静的確認。新規7ファイルは1ファイル1型・ブロックnamespace。禁止省略名、Unity lifecycle API、Assert.Single(...Where(...)) の追加なし。
- 音声側の変更は既存ConfigureMacros引数への接続だけ。Render / AdvanceFrame / NoteOnに配列生成・LINQ・追加リソース所有を持ち込んでいない。
- 開始時のSHA-256との照合で変更禁止の3設計書、全csproj、SfxPresetFactory/カタログ、ADSR/BRR/Gaussian/ミキサー/VoiceModulation/SongRenderer本体の不変を確認した。Coreの依存、JSON version、既存テストの期待値は変更していない。
- 作業は指定worktree内に限定。git操作・コンパイル・dotnet build/test・アプリ起動は行っていない。

## SFX-A3 未完了・未実行の確認事項

実装上の残タスクはなし。受け入れ完了には依頼者／Claude Codeによる次の実行確認が必要。

- `dotnet build Arpeggio.slnx` のコンパイル成功・警告ゼロと、xunitアナライザを含む全既存・追加テストの成功。
- GBの4段階PCM/制御列/VGM、SNESのADSR積・終端0・分割再生・保存編集境界、両追加マクロのGC0実測。
- 旧8プリセット×3チップの旧エンジンで採取した基準PCM/JSONとの実測比較。今回の独立計算テストとnull/空列等価テストは追加済みだが、旧エンジンの全プリセットgoldenを採取・実行したわけではない。
- 必要な試聴・DAW実機確認。静的確認をビルド成功・テスト成功・旧音の実測一致として扱わない。

## SFX-A2 / A3 変更ファイル一覧

既存更新11ファイル、新規7ファイル、計18ファイル。

```text
src/Arpeggio.Core/Instruments/GbPulseInstrument.cs
src/Arpeggio.Core/Instruments/SnesSampleInstrument.cs
src/Arpeggio.Core/Document/InstrumentValidator.cs
src/Arpeggio.Core/Synthesis/GameBoy/GbPulseSynthesizer.cs
src/Arpeggio.Core/Synthesis/Snes/SnesVoiceSynthesizer.cs
src/Arpeggio.Formats/Export/ControlTrackCursor.cs
src/Arpeggio.Cli/InstrumentOptions.cs
tests/Arpeggio.Core.Tests/Formats/GameBoyDutyMacroTests.cs（新規）
tests/Arpeggio.Core.Tests/Synthesis/Snes/SnesVolumeMacroTests.cs（新規）
tests/Arpeggio.Core.Tests/Instruments/SnesVolumeMacroInstrumentTests.cs（新規）
tests/Arpeggio.Core.Tests/Render/SnesVolumeMacroRenderTests.cs（新規）
tests/Arpeggio.Core.Tests/Sfx/LegacySfxMacroCompatibilityTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/OptionalInstrumentMacroTests.cs（新規）
tests/Arpeggio.Core.Tests/Mcp/OptionalInstrumentMacroToolsTests.cs（新規）
tests/Arpeggio.Core.Tests/Render/SongRendererAllocationTests.cs
tests/Arpeggio.Core.Tests/Cli/InstrumentCommandsTests.cs
tests/Arpeggio.Core.Tests/Daw/SnesInstrumentPresenterTests.cs
docs/implementation.md
```

# SFX-B2 実装記録（2026-09-09）

## SFX-B2 設計との差

- 具体的な公開 API は未指定のため、`SfxSongCompiler.Compile(parameters, chip, title)` と結果型を追加する。結果は新規 Song、B1 の曲線・時間情報、全トーンフレームの要求音程／要求 Hz／実効 Hz、生成警告を保持する。定義・出自・hash の Song への適用は C2、プリセットは D1、CLI/MCP/DAW の新入口は後続ランへ残す。
- 同種の連続フレーム警告は code/path/layer ごとにまとめ、requested/actual は範囲先頭の代表値とする。ピッチの全フレーム値は別の一覧で失わず保持する。基準 Hz のパスに曲線適用後の Hz を報告する。通常の周期量子化はクランプ警告に含めない。
- 許容される最大 slide/delta の組合せでは要求 Hz が double の上限超過または0へのアンダーフローになるため、正の double として表現できない場合だけ要求 Hz を null とし、有限の要求 MIDI 値を全フレーム一覧に保持する。実効 Hz は既存 PitchTable の結果を使い、合成処理や保存マクロを変更しない。

## SFX-B2 実装・テストコード

- B1 の検証・正規化・曲線を利用し、NES 5 / GB 4 トラックを SongFactory の名前・配置で生成。tone=ID1/track0、noise=ID2/track3、無効レイヤーは音色・ノートだけ省略する。音色名、tick0、volume15、Effects空、テンポ150、48 ticks/beat、各 `2(N+1)` tick の終端を固定する。
- duty は repeat の曲線時刻を使い、12.5〜75へ飽和→四段階の最近傍（同距離は小さい比率）。noise は独立した経過時刻を使い、AwayFromZero丸め→NES0〜15/GB0〜127へ飽和→基準選択との差×100を PitchMacro にする。GB の hardware envelope は全量固定。
- 全トーンフレームで anchor＋arpeggio＋pitch の要求値と PitchTable の実効 Hz を記録。範囲制限だけを PitchClamped とし、終端0フレーム・repeatで音域へ戻る箇所・最大300フレームも含める。警告順は B1 の診断→duty→noise→pitch。
- 追加テストは、固定トラック・ID・名前・無効レイヤー・異なる包絡長・保存往復・入力と再生成間の可変配列隔離、全4 duty・3中間点・上下飽和・repeat、noiseの両端・GB分周グループ跨ぎ・丸め・mode/width・トーン変調からの独立、通常周期量子化・上下音域・途中クランプ・repeat範囲・最大曲線の有限診断を固定する。

## SFX-B2 静的確認・未完了

B2 の実装とテストコードを作成し、型定義・namespace・接続・固定期待値・括弧・doc XML・public summary隣接・禁止パターンを静的確認してから B3 へ進んだ。ビルド・テスト・音声実測は実行していない。A2/B1を含む依存先の受け入れ確認と、全テスト成功・警告ゼロは依頼者／Claude Code の実行待ち。

# SFX-B3 実装記録（2026-09-09）

## SFX-B3 設計との差

- SNES の波形再生で参照しないメタデータ（sampleRate=44100、rootMidiNote=60、loopStart/loopEnd=0）とトーン音色の無効時 NoiseRate=31 は既存音色の既定値を維持する。ノイズ音色だけパラメータの noiseRate を適用する。Echo設定は全既定値を明示し、DelayMilliseconds=0 とする。
- B2 と同じ compiler へ SNES の組立を接続する。WAVへの接続は通常の SongRenderer→WavWriter→WavReader を通る統合テストで固定し、新しいCLI/MCP入口や別合成器を追加しない。

## SFX-B3 実装・テストコード

- SNES 8ボイスを維持し、tone=ID1/voice0、noise=ID2/voice1へ独立した一回の発音を配置する。トーンは指定の pulse/sine/square/saw/triangle、ノイズは Waveform=Sine・NoiseEnabled=true・固定noiseRate・MidiNote=60・pitch/arpeggioマクロなしとする。
- 両音色とも Loop=true、Preset/SampleData=null、Pan/EchoSend=0、PitchModulation=false。ADSR=(15,0,7,0)、秒包絡=(0,0,1,0)を固定し、B1のASDecay/punchはVolumeMacroへだけ接続する。各ノートの最終0フレームと長さをB2と共通に保つ。
- 全音程診断はSNESのSample用PitchTableへ接続。14bit上限16383・原速4096・内蔵128サンプル周期による約999.94Hz上限を返す。1000Hz要求は基準音のセント丸めで83.21 MIDIとなり上限内へ入るため、保存列の要求値を正として警告しない。1001/12000Hzと途中slideの上限超過を別テストで固定する。
- `SfxSnesSongCompilerTests`: 全5波形、固定8トラック・ID・名前・Notes/Effects、全31noiseRate、ADSRとマクロ、JSON往復、実際のSnesVoiceSynthesizer.PitchRegisterとの全フレーム一致、二声の独立終端、トーン変調がノイズPCMへ漏れないこと、再生成間の配列・エコー係数隔離を検証する。
- `SfxCompiledAudioTests`: 全5波形の保存往復→SongRenderer→WAV→再解析、noiseRate端点によるPCM変化、トーン/DSPノイズのtail=0/0.05、44100/48000/44101Hz、DSPレート変換履歴後と曲最終サンプルの0、1/733/1024フレームの分割Render・Reset後の全PCMバイト一致を検証する。0.4秒decayが約8msへ短縮されず0.3秒時点でも鳴ること、3チップの二声と意図的な音量0も含める。
- 最大曲線の診断テストを三チップ・正負両方向へ拡張し、上限超過／0へのアンダーフローでも要求MIDIと実効Hzが有限で、診断をJSONへ保存できることを固定する。

## SFX-B2 / B3 静的確認

- design-sfx全文、designのチップ・マクロ・ノート効果・SNES DSP仕様、M2-AとSFX各実装記録・未完了事項、Sfx/Instruments/VoiceModulation/PitchTableとCLI/MCP/DAWの既存入口を参照した。
- 追加参照型の定義・namespace・公開シグネチャを検索照合。15新規C#ファイルの括弧対応、1ファイル1型、ブロックnamespace、doc XML、public summary隣接、禁止省略名・Unity API・Assert.Single内Whereの不使用を静的確認した。
- 生成・診断はオフラインだけで実行し、Render/NoteOn/AdvanceFrameへ処理・確保・LINQを追加していない。新規リソースはテストのMemoryStreamのみで、usingで破棄する。GC計測テストは追加せず、A2/A3の既存計測ケースを維持する。
- 開始時のSHA-256と照合し、既存src/testsと変更禁止の3設計書、計535ファイルが不変であることを確認した。既存合成・JSON保存・旧8プリセット・16 SNES音色・依存パッケージを変更していない。JSON version=1。git操作、コンパイル、dotnet build/test、アプリ起動は行っていない。
- この静的確認はコンパイル・xunitアナライザ・テスト成功、または旧PCM/JSONの実測一致を意味しない。

## SFX-B3 未完了・未実行の確認事項

B2→B3の順で実装とテストコード作成を完了。実装上の残タスクはなし。受け入れ完了には依頼者／Claude Codeによる以下の実行確認が必要。

- A1/A2/A3/B1を含む依存先と今回のコードのレビュー、`dotnet build Arpeggio.slnx` の成功・警告ゼロ、既存全件と追加テストの成功。
- NES/GBのduty/noise制御・全軌跡音域診断、SNESの14bit上限・5波形・DSPノイズ・長いdecay・末尾ゼロ、保存往復とWAV経路・分割再生・Resetの実測。
- 旧8プリセット×3チップの基準JSON/PCMの全バイト回帰、既存GC0テスト、およびmacOS/Windowsの丸め境界・決定性確認。既存コードの不変確認だけではこれらを実行済みとしない。
- 必要な試聴。CLI/MCP/DAWの新パラメータ操作は分割表の後続ランのままで、今回のcompilerは既存legacy入口へ接続していない。

## SFX-B2 / B3 変更ファイル一覧

新規Core 9ファイル、テスト6ファイル、既存文書1ファイル、計16ファイル。

```text
src/Arpeggio.Core/Sfx/SfxSongCompiler.cs
src/Arpeggio.Core/Sfx/SfxSongCompilationResult.cs
src/Arpeggio.Core/Sfx/SfxPulseSongBuilder.cs
src/Arpeggio.Core/Sfx/SfxSnesSongBuilder.cs
src/Arpeggio.Core/Sfx/SfxDutyCurveGenerator.cs
src/Arpeggio.Core/Sfx/SfxNoiseCurveGenerator.cs
src/Arpeggio.Core/Sfx/SfxPitchDiagnostics.cs
src/Arpeggio.Core/Sfx/SfxPitchFrame.cs
src/Arpeggio.Core/Sfx/SfxFrameWarningCollector.cs
tests/Arpeggio.Core.Tests/Sfx/SfxCompilationTestData.cs
tests/Arpeggio.Core.Tests/Sfx/SfxSongCompilerTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxChipCurveTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxPitchDiagnosticsTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxSnesSongCompilerTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxCompiledAudioTests.cs
docs/implementation.md
```

# SFX-D1 実装記録（2026-09-09）

## SFX-D1 設計との差

- 公開APIの具体形は未指定のため、`SfxParameterPresetCatalog.GetAll(chip)` / `Get(kind, chip)` / `Parse(name)` と、完全初期値・対象チップを持つ専用descriptionを追加する。用途識別子だけ既存の `SfxPresetKind` を共有し、旧カタログ・factoryは変更しない。Song生成は既存 `SfxSongCompiler.Compile`、定義・指紋の付与はC2の責務を維持する。

## SFX-D1 実装・静的確認

- 八用途の完全初期値を固定表示順で提供。包絡のフレーム数を6桁AwayFromZeroの秒数へ変換し、hitの三チップ固有ノイズとexplosionのSNES rate18を適用する。無効レイヤーも共通初期値を保持する。
- `SfxParameterPresetCatalogTests` に全24組み合わせの全フィールド独立期待値、Song検証・保存往復・固定長・Effects空、44100/48000Hzの有限・非無音・非クリップPCM、正式名・別名・不正入力、旧八種と十六音色の名前および旧factory呼び出しの不変を検証するコードを追加。
- 型定義・namespace・既存公開API・括弧・表の秒数と終端長を静的照合。D1の実装とテストコード作成を完了してからD2へ進む。コンパイル・テスト・音声実測は実行していない。

## SFX-D1 未完了・未実行の確認事項

実装上の残タスクはなし。依存先B2/B3を含むレビュー、警告ゼロのビルド、全既存・追加テスト、旧24種の採取済みJSON/PCM基準とのバイト比較は依頼者／Claude Codeの実行待ち。

## SFX-D1 変更ファイル一覧

- `src/Arpeggio.Core/Sfx/SfxParameterPresetDescription.cs`（新規）
- `src/Arpeggio.Core/Sfx/SfxParameterPresetCatalog.cs`（新規）
- `tests/Arpeggio.Core.Tests/Sfx/SfxParameterPresetCatalogTests.cs`（新規）
- `docs/implementation.md`

# SFX-D2 実装記録（2026-09-09）

## SFX-D2 設計との差

- 具体APIは未指定のため、`SfxParameterRandomizer.Randomize(current, chip, category, seed)` / `Mutate(current, chip, seed, strength, locks)` を純粋な候補生成として提供する。結果は完全パラメータ、同値判定、値の変更一覧、成功時だけの乱数出自を返す。randomizeのsourcePresetだけ結果で指定し、mutateでは呼び出し側が既存sourcePresetを保持する。保存・履歴への適用は未実装のC2および後続E2/F1の責務とし、本ランでは保存形式との往復を統合テストで固定する。
- ロックは現在チップの正規パスだけ受理し、重複は先頭を残して除く。比較はOrdinal、保存順は入力順。strengthは有限の0〜1をそのまま計算・保存し、パラメータの6桁規則はstrengthへ拡張しない。
- チップ固有duty/dutySweepはtone、noise選択/noiseSlideはnoiseの有効性に従う。無効レイヤーやSNESの非対応スロットでも必ず一回消費する。punch補正は6桁正規化後のsustainで判定し、変更一覧に補正前の抽選値も残す。同値結果では出自更新を返さない。

## SFX-D2 実装・テストコード

- `SfxRandomGenerator` は独自xorshift32。uint32の13/17/5シフト、seed0の置換、2^32での除算を固定し、System.Random・日時・文字列hashを生成式に使わない。乱数とパラメータ変換はオフラインの候補生成に限定した。
- `SfxCategoryRandomization` はカテゴリ初期値から15回、`SfxParameterMutation` は現在値から22回を固定順で消費する。anyだけ先頭で一回消費し、要求anyと選択後の正式sourcePresetを分ける。pickup/power-upは正式名へ正規化する。
- `SfxRandomizationCandidate` が仕様Catalogの読み取り・置換・範囲・選択肢とValidatorの6桁正規化を利用する。抽選と適用の責務を分け、ロックや無効レイヤーによって乱数位置を変えない。dutyの等距離は小さい段階、整数の中間点はAwayFromZero。repeatとジャンプの0保持、有効repeatの最低一フレーム、独立したsustain/punch補正を実装した。
- 成功時はoperation/algorithmVersion/seedと、randomizeのcategory、またはmutateのstrength/独立した読み取り専用locks/baseParametersHashを返す。同値時はChanged=false・元の正規化パラメータ参照・出自更新null。変更一覧は元値と最終値を持ち、補正した場合はCorrectedFromへ補正前の抽選値を残す。randomizeによるenabledやnoiseMode等の変更も一覧に含む。
- `SfxRandomGeneratorTests`: seed1の22整数出力、seed0/最大値の固定先頭列、0置換と実数への正確な除算。
- `SfxCategoryRandomizationTests`: seed1の全8×3の完全パラメータ黄金値、無効レイヤーの保持、初期値からの生成、別名、anyの追加消費・全八区間の表示順、seed端点でのカテゴリ性格、同じ結果への再適用no-op。
- `SfxParameterMutationTests`: seed0/1/最大値の全22スロットの独立黄金値、三チップの幅、SNESの非対応スロット消費、既定強度0.1、正確なduty中間点。
- `SfxRandomizationLocksTests`: 全72正規パスの単独ロックと後続値の一致、三チップの無効レイヤー、punch優先補正・ロック済みpunchに対するsustain補正・両ロック・両レイヤーの独立補正、全ロック/strength0/丸め後同値、ロック配列の隔離と重複除去。
- `SfxRandomizationBoundsTests`: 離散機能の0保持、repeatの最低秒数、各種飽和、正規化後の半フレーム境界、正負整数と深さの正確な中間点、三チップの21番目のノイズ抽選、非有限strength・不正パス・不正現在値・不正チップの拒否。
- `SfxRandomizationProvenanceTests`: 三チップ×seed端点の生成→定義付与→JSON往復→出自から再現、mutateの変更前hash・全パラメータ・生成列指紋、既存SongHistoryでのUndo/Redoスナップショットと出自復元、sourcePreset保持、手動tweak後も最後の乱数記録が履歴情報として保存可能なこと。C2のEditSessionへの自動適用を実装したテストではない。

## SFX-D1 / D2 最終静的確認

- 新規17 C#ファイルの型定義・namespace・公開API、括弧、doc XML、public summary隣接、1ファイル1型・ブロックnamespace、省略名・Unity lifecycle API・Assert.Single内Whereの不使用を確認した。使用するBCLとxunitのAPIをローカル参照XMLで照合した。
- 黄金値はアプリや本実装を呼ばず、独立した整数・スカラー計算で固定した。二進数で正確な中間値となるPRNG出力からseedを逆算し、整数・duty・depthの丸めを固定する。テスト期待値を実装の乱数器から生成しない。
- 開始時のSHA-256との照合で、既存C#・csproj・変更禁止の三設計書、計533ファイルが不変。旧八種factory、十六SNES音色と素材生成、合成・JSON保存・CLI/MCP/DAW・依存設定・既存テスト期待値を変更していない。JSON version=1、CoreはBCLのみ。
- Render/AdvanceFrame/NoteOnへ変更・アロケーション・LINQを追加していない。既存GC0テストを維持し、オフライン候補生成へGC0を要求する新しい計測は追加していない。
- 作業ディレクトリ外への書き込み、git操作、コンパイル、dotnet build/test、アプリ起動、PCM生成・試聴は実行していない。静的確認をコンパイル成功・警告ゼロ・テスト成功・旧音の実測一致として扱わない。

## SFX-D2 未完了・未実行の確認事項

D1→D2の実装・テストコード作成と静的確認は完了。実装上の残タスクはなし。受け入れ完了には依頼者／Claude Codeによる以下の実行確認が必要。

- 依存先A1/B2/B3/C1を含むレビューと、`dotnet build Arpeggio.slnx` の成功・警告ゼロ、xunitアナライザと全既存・追加テストの成功。
- 全24パラメータプリセットのPCM（有限・非無音・非クリップ）と、旧24プリセットの採取済みJSON/PCM基準との全バイト比較、十六音色の回帰、既存GC0の実測。
- macOS/Windowsで固定PRNG・正規化パラメータ・整数マクロ列の黄金値一致。浮動小数点のPow/Sinを含む生成決定性は実行確認待ち。
- 保存・履歴の実運用への接続は分割表のC2/E2/F1へ申し送る。呼び出し側はChanged=falseで保存・履歴・出自更新を行わず、mutateのSourcePreset=nullでは元のsourcePresetを維持する。Randomization=nullも既存のlastRandomizationを維持する意味であり、消去要求ではない。生成診断は候補パラメータを既存SfxSongCompilerへ渡して取得する。

## SFX-D2 変更ファイル一覧

新規Core7ファイル、テスト7ファイル、実装記録1ファイル。D1との合計は新規Core9・テスト8・既存文書1の18ファイル。

```text
src/Arpeggio.Core/Sfx/SfxRandomGenerator.cs
src/Arpeggio.Core/Sfx/SfxParameterRandomizer.cs
src/Arpeggio.Core/Sfx/SfxCategoryRandomization.cs
src/Arpeggio.Core/Sfx/SfxParameterMutation.cs
src/Arpeggio.Core/Sfx/SfxRandomizationCandidate.cs
src/Arpeggio.Core/Sfx/SfxParameterRandomizationResult.cs
src/Arpeggio.Core/Sfx/SfxParameterChange.cs
tests/Arpeggio.Core.Tests/Sfx/SfxRandomGeneratorTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxCategoryRandomizationTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxParameterMutationTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxRandomizationLocksTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxRandomizationBoundsTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxRandomizationProvenanceTests.cs
tests/Arpeggio.Core.Tests/Sfx/SfxRandomizationTestData.cs
# SFX-C2 実装記録（2026-09-09）

## SFX-C2 設計との差

- 公開 API の具体形は未指定のため、既存の Notes / Instruments と同じ `EditSession.Sfx` に `SfxEditor` を結び付ける。Tweak / Regenerate / Detach は候補・生成診断・同期状態・現 revision と候補 revision を返す。新規候補の定義付き生成も同じ Editor の純粋な CreateCandidate にまとめ、ファイル新規作成・プリセット・CLI/MCP/UI の入口は後続ランに残す。
- 保存直前の revision 再確認は SFX 編集から渡す EditSession.Change の保存前検証で行い、通常編集の保存規則は変更しない。呼び出しは既存どおり同一セッション内で直列化する（MCP の共有セッションロック、DAW の UI スレッド）。プロセス間の確認・置換間のロックは設計どおり保証しない。
- Regenerate の事前確認は dry-run の候補と、全置換対象の音色・トラック・ノート件数で提供する。MissingDefinition の Detach は操作エラーとし、同期済み状態で再生成して全 JSON が同値なら保存・履歴を増やさない。未知版は Detach だけ許可し、Undo で不透明 JSON ごと復元する。

## SFX-C2 実装範囲と判断

- `EditSession.Sfx` の `Tweak(patch, expectedRevision?, dryRun)` は既知版・両 hash 一致だけを受理する。全 patch 検証と生成を候補上で完了し、定義・全生成領域・両 hash を一度の Change で保存→履歴→公開する。title・sourcePreset・lastRandomization は保持する。
- `CreateCandidate` は三チップの既存 compiler に定義と指紋を付ける純粋生成。パラメータを正規化し、出自の可変 locks は保存境界のコピーで隔離する。呼び出し元の候補・生成曲線・履歴と公開ソングの可変配列を共有しない。
- 正規化後の同値 patch は出自・指紋・保存日時・Undo/Redo を保持する。無効レイヤーの値だけの変更は生成列が同じでも定義の変更として一履歴にする。dry-run は保存せず、現 revision と候補 revision、候補同期状態、生成曲線と警告を返す。
- `Regenerate(replaceGenerated: true, ...)` は既知版の保存意図から title 以外の全生成領域を置換し、二つの指紋を更新する。候補と置換対象件数は dry-run で取得できる。手動編集・両 hash 不一致も Undo 一回で復元する。
- `Detach` は定義だけを除去し、既知・未知版とも通常ソングへ移行する。生成列は変更せず、Undo/Redo は不透明 JSON を含む定義と出自を復元する。
- `SfxEditException.Code` は MissingDefinition / UnsupportedSfxVersion / GeneratedContentChanged / SavedParametersChanged / RevisionConflict。入力 patch のエラーは既存 SfxParameterException、元文書の不正は既存 SongValidationException、I/O は既存例外を維持する。
- revision は開始時の expectedRevision 照合に加え、Change の適用対象と保存直前の公開 Song・保存先ファイルで照合する。検証・生成・保存前照合に失敗するとファイル・公開参照・Undo/Redo を変更しない。成功結果の revision は適用後の値とする。
- `SongSnapshotPublisher.Apply` と `DawDocument.Save` に Sfx のコピーを追加した。前者はトラック数が違う早期 return より前に適用する。SongHistory、CliHistoryStore、DAW の IsDirty / HasExternalChange は既に全 Song の JSON を比較・複製しているため、本体変更なしで定義が通る。
- DAW は作業セッションへの自動保存と Ctrl+S の正本保存を維持する。既存 CLI/MCP の legacy 入口、プリセット、renderer、Serializer の出力規則は変更していない。CLI/MCP の新コマンド・共通 JSON 表示形式と UI は分割表の後続ランで接続する。

## SFX-C2 テストコード

| 対象 | 追加した検証 |
|---|---|
| `SfxEditorTests` | 三チップの二声 patch・一保存一履歴・固定 MIDI/tick/pitch 列・保存再読込、定義/生成列/出自の Undo/Redo、返却候補/生成曲線/履歴のコピー隔離、入力 locks の非共有、無効レイヤーの定義だけの変更、全生成領域の再生成と手動編集の復元、両 hash 修復、detach 前後の float PCM 全バイト一致 |
| `SfxEditFailureTests` | 同値 patch と同期済み再生成の no-op・保存日時と redo 保持、三操作の dry-run、途中不正・型/キー/項目間制約、title を含む revision 拒否、保存直前のファイル revision 拒否、保存パラメータ/生成列の同期拒否、明示再生成要求、定義なし、未知 schema/generator/乱数版の通常編集と detach 履歴、I/O 失敗で三操作/Undo/Redo の公開状態・履歴維持 |
| `SfxDocumentTests` | 三チップの作業保存→正本保存→Undo/Redo→detach→再読込、未知三版の正本保持、sfx のみの dirty/外部変更検知、正本置換失敗の保存基準/通知/履歴維持・一時ファイル除去・再試行、既存 Presenter の外部変更保護 |
| `SfxHistoryBoundaryTests` | Core の編集履歴を既存側車形式から復元して CLI の Undo/Redo へ接続、未知三版の detach 履歴、sfx のみの外部変更で側車失効、既存通常編集の履歴 I/O 失敗で改行・定義を含む元ファイル全バイト復元 |

新 SFX CLI 操作のエラー JSON・dry-run での側車書込抑止・create の二ファイル失敗処理は E1/E2 の範囲であり、今回の側車テストをその受け入れ済み証拠にはしない。

## SFX-C2 静的確認

- 指定設計、M2-A と SFX 各ランの実装記録・未完了事項、既存 Sfx/音色/VoiceModulation、CLI/MCP/DAW の保存・履歴・外部変更経路を参照した。
- 追加使用型の定義・namespace・公開メンバーを検索照合した。MemoryMarshal.AsBytes と xunit の例外/コレクション assert はローカル参照 XML でも確認した。
- 変更・追加した12 C#ファイルの括弧対応、doc XML、public summary の隣接、ブロック namespace、禁止省略名、Unity lifecycle API、Assert.Single 内 Where の不使用を静的確認した。新規ファイルは1ファイル1型。
- 開始時の SHA-256 と比較し、変更禁止の3設計書、既存合成・音色・旧プリセット/音色バンク・Serializer・全 csproj と既存テストの不変を確認した。実装記録の既存内容はバイト列を保持し、末尾へ追記した。JSON version=1、Core は BCL のみ。
- Render / AdvanceFrame / NoteOn と合成への接続は変更していない。追加の JSON・生成・履歴処理は編集時のオフライン処理。新規購読はテストの Saved 通知だけで finally 解除し、DawDocument・fixture を using で破棄する。
- 作業は指定 worktree 内だけ。git 操作・コンパイル・dotnet build/test・アプリ起動・音声生成は実行していない。静的確認はビルド成功、警告ゼロ、テスト成功、PCM 実測一致を意味しない。

## SFX-C2 未完了・未実行の確認事項

実装とテストコード作成は完了。実装上の残タスクはなし。C1/B2/B3 とその依存先を含む受け入れ確認は、依頼者／Claude Code のレビュー・実行待ち。

- `dotnet build Arpeggio.slnx` の成功・警告ゼロ、xunit アナライザを含む既存全件と追加テストの成功。
- 三チップの保存・履歴・detach/regenerate・revision/I/O 拒否・DAW 正本保存と CLI 側車境界の実行確認。
- detach の PCM 全バイト一致、旧8プリセット×3チップの既存 JSON/PCM 回帰、既存 GC0 テスト、macOS/Windows の保存・置換失敗挙動。
- 必要な DAW 実機確認と試聴。SFX パラメータ UI・新 CLI/MCP 入口は後続ランの対象のまま。

## SFX-C2 変更ファイル一覧

既存更新4ファイル、新規9ファイル、計13ファイル。

```text
src/Arpeggio.Core/Session/EditSession.cs
src/Arpeggio.Core/Session/SongSnapshotPublisher.cs
src/Arpeggio.Core/Session/SfxEditor.cs（新規）
src/Arpeggio.Core/Sfx/SfxEditException.cs（新規）
src/Arpeggio.Core/Sfx/SfxEditResult.cs（新規）
src/Arpeggio.Core/Sfx/SfxReplacementSummary.cs（新規）
src/Arpeggio.Daw/Editing/DawDocument.cs
tests/Arpeggio.Core.Tests/Sfx/SfxEditFixture.cs（新規）
tests/Arpeggio.Core.Tests/Sfx/SfxEditorTests.cs（新規）
tests/Arpeggio.Core.Tests/Sfx/SfxEditFailureTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/SfxDocumentTests.cs（新規）
tests/Arpeggio.Core.Tests/Cli/SfxHistoryBoundaryTests.cs（新規）
docs/implementation.md
```

# SFX-E1 実装記録（2026-09-10）

## SFX-E1 設計との差

- JSON の具体的なオブジェクト配置は未指定のため、共通結果を最上位へ置き、generation の tone / noise に各包絡時間・フレーム数・trackIndex を持たせる。同期不一致では generation を null とし、保存意図を現在音の診断として返さない。schema は全 Catalog 項目に supported を添えて返す。
- create の既定 preset は MCP と同じ jump、title 省略時は正式プリセット名とする。個別オプションは CLI 名から正規パスへの写像だけを持ち、型・範囲・可否は Core Catalog と patch 検証へ委譲する。
- 新規保存は既存側車の置換が成功するまでを一操作とし、失敗時は自分が作った内容が未変更の場合だけ除去する。側車の原子置換前に失敗するため既存 state.json は保持され、新規の空履歴ディレクトリだけ後始末する。

## SFX-E1 実装・テストコード・静的確認

- create / list --editable / params / tweak を追加。旧 new の処理と引数なし list のテキストは維持。個別指定全32正規パスの写像を Catalog と静的照合した。
- JSON 出力ではパラメータの小文字 enum と非対象チップの省略を保存形式へ揃え、現在値／保存意図・現 revision／候補 revision・生成警告を分離する。通常表示の警告は stderr。引数解析失敗も SFX 専用境界で error / exitCode / code / parameterPath に整形する。
- CLI の既存編集境界に履歴保存要否の任意述語を追加。SFX の dry-run と同値操作は側車を書き直さず、既存コマンドは従来どおり保存する。create は隣接一時ファイルの新規移動と履歴初期化をまとめ、失敗時に作成ファイルだけを戻す。
- テストコードは全項目の個別指定／patch 保存バイト同値（三チップ・小数点カンマのカルチャ）、stdin、schema・現在値の読み取り不変、旧 list/new、新8種×3チップ一覧、引数・入力・文書・I/O の 0/1/2/3、patch 構造違反、create dry-run・古い側車初期化・履歴失敗時の復元を追加。
- 使用型の定義・namespace と System.CommandLine のローカル参照 XML、追加 C# の doc XML・配置・禁止パターンを確認した。E1 の実装とテスト作成を先に揃えてから E2 に進む。コンパイル・テストは実行していない。

## SFX-E1 未完了・未実行の確認事項

実装上の残タスクはなし。依存ランを含む受け入れは未確認。依頼者／Claude Code による警告ゼロのビルド・全件テスト・旧 JSON/PCM 比較が必要。

## SFX-E1 変更ファイル一覧

- `src/Arpeggio.Cli/CliExecution.cs`
- `src/Arpeggio.Cli/SfxCommands.cs`
- `src/Arpeggio.Cli/Sfx/CliSfxExecution.cs`
- `src/Arpeggio.Cli/Sfx/EditableSfxCommands.cs`
- `src/Arpeggio.Cli/Sfx/SfxFileTransaction.cs`
- `src/Arpeggio.Cli/Sfx/SfxOutput.cs`
- `src/Arpeggio.Cli/Sfx/SfxParameterOptions.cs`
- `tests/Arpeggio.Core.Tests/Cli/Sfx/SfxParameterCommandsTests.cs`
- `tests/Arpeggio.Core.Tests/Cli/Sfx/SfxCommandFailureTests.cs`
- `docs/implementation.md`

# SFX-E2 実装記録（2026-09-10）

## SFX-E2 設計との差

- C2 の SfxEditor には乱数操作の接続がないため Randomize / Mutate を追加し、D2 の純粋候補を既存 Complete の保存・履歴・revision 境界へ通す。変更一覧を SfxEditResult に optional 追加し、CLI 結果の changes に返す。sourcePreset / lastRandomization の同値時保持は D2 の申し送りを継承する。
- CLI randomize の category は必須とする。省略時の既定カテゴリは設計にないため、MCP と同じ明示カテゴリの入力とする。seed / strength はカルチャ非依存に解析し、ロックは一指定一パスの反復オプションとする。

## SFX-E2 実装・テストコード

- randomize / mutate / regenerate / detach を新 SFX 実行境界へ登録。seed 必須、uint32 端点、strength、反復 lock、replace-generated の明示要求、expected-revision、dry-run を扱う。乱数出自は小文字の操作名と正式 sourcePreset を返す。
- SfxEditor の Randomize / Mutate は同期済みの定義だけを受理し、純粋な D2 候補を一回の Complete で保存・履歴・公開する。変更一覧と包絡補正を返し、同値時は sourcePreset / lastRandomization・両指紋・redo を維持する。Core の依存追加はない。
- `SfxExplorationCommandsTests`: 三チップと seed=0/1/max、any と選択結果の保存、preview→適用の revision 一致、反復ロック、変更前 hash、出自と生成列の Undo/Redo、手動 tweak 後の出自保持、同値 randomize・strength0・全ロック時の保存日時と redo 保持、引数エラーを固定。
- `SfxTransactionCommandsTests`: tweak/randomize/mutate/regenerate/detach の全五操作で dry-run と実適用の候補一致、履歴 I/O 失敗時の CRLF を含む元バイト復元、title の外部更新と stale revision の通常／予行拒否、両 hash 不一致の表示と再生成、未知版 detach と Undo、側車未作成を固定。
- `SfxAnalysisLoopTests`: 新8用途×3チップの create→params→revision付きtweak→analyze（20ms）→export wav（44100Hz/loops1/tail0）→analyze wav。本体長は一サンプル以内、float と16bitの RMS/peak は0.01dB以内、窓数・非無音・非クリップ・入力と履歴不変を検証するコードを追加。三チップの detach 前後の WAV 全バイト比較も含む。
- `SfxRandomizationEditorTests`: Randomize / Mutate の保存直前の外部 revision 再確認、候補と公開ソングの隔離、一履歴 Undo/Redo、I/O 失敗時の参照・定義・履歴不変を Core セッション境界で固定。
- E1 の静的確認を補完し、CLI からの internal メンバー参照を解消。カルチャ検証は InvariantGlobalization 環境でも利用できる、InvariantCulture の複製に小数点カンマを設定する方式とした。警告の出力先と個別オプション重複拒否もテストコードへ追加。

## SFX-E1 / E2 最終静的確認

- System.CommandLine 2.0.11 のローカル参照 XML で Option / ParseResult / OptionResult / Arity / Implicit / IdentifierTokenCount を照合。Core の追加参照型・namespace・公開メンバー、JSON／履歴／生成の接続をソースで照合した。
- 追加・変更 C# の括弧対応、doc XML、public summary の隣接、1ファイル1型、ブロック namespace、禁止省略名・Assert.Single 内 Where・Unity API の不使用を確認。コンパイラ・xunit アナライザによる確認ではない。
- 開始時の SHA-256 と比較し、既存4 C#だけが変更対象。変更禁止の三設計書、旧プリセット、16 SNES 音色、全合成・音色・Serializer、全 csproj、既存テストを含む554ファイルが不変。実装記録は既存節を保持して末尾へ追加した。
- Render / AdvanceFrame / NoteOn は変更なし。生成・JSON・履歴は既存どおりオフライン。JSON version=1、Core は BCL のみ。新規購読・音声リソースは追加せず、テストの入力・出力・JSON文書・作業フォルダは破棄する。
- 作業ディレクトリ外への書き込み、git 操作、コンパイル、dotnet build/test、アプリ起動、PCM/WAV生成・試聴は実行していない。

## SFX-E2 未完了・未実行の確認事項

E1→E2 の実装・テストコード作成と静的確認を完了。実装上の残タスクはなし。受け入れ完了には依存先を含む依頼者／Claude Code のレビューと以下の実行確認が必要。

- `dotnet build Arpeggio.slnx` の成功・警告ゼロ、xunit アナライザ、既存全件および追加テストの成功。
- CLI の全パラメータ、bool の明示値、負数、反復 lock、引数エラー単一JSON、0/1/2/3、dry-run・同値・履歴失敗・revision の実行確認。
- 全24新プリセットの解析／WAV許容差、detach の全バイト一致、旧8種×3チップの採取済み JSON/PCM 基準との比較、既存GC0とmacOS/Windowsの決定性・I/O回帰。
- MCP の新ツールは E3、DAW の新操作は F1以降の予定どおり対象外。後続は SfxEditor.Randomize / Mutate と SfxEditResult.Changes を利用し、同値時の出自保持を継承する。

## SFX-E1 / E2 変更ファイル一覧

既存 C# 4ファイル、追加 C# 12ファイル（CLI6・テスト6）、実装記録1ファイル、計17ファイル。

```text
src/Arpeggio.Cli/CliExecution.cs
src/Arpeggio.Cli/SfxCommands.cs
src/Arpeggio.Cli/Sfx/CliSfxExecution.cs
src/Arpeggio.Cli/Sfx/EditableSfxCommands.cs
src/Arpeggio.Cli/Sfx/SfxExplorationCommands.cs
src/Arpeggio.Cli/Sfx/SfxFileTransaction.cs
src/Arpeggio.Cli/Sfx/SfxOutput.cs
src/Arpeggio.Cli/Sfx/SfxParameterOptions.cs
src/Arpeggio.Core/Session/SfxEditor.cs
src/Arpeggio.Core/Sfx/SfxEditResult.cs
tests/Arpeggio.Core.Tests/Cli/Sfx/SfxParameterCommandsTests.cs
tests/Arpeggio.Core.Tests/Cli/Sfx/SfxCommandFailureTests.cs
tests/Arpeggio.Core.Tests/Cli/Sfx/SfxExplorationCommandsTests.cs
tests/Arpeggio.Core.Tests/Cli/Sfx/SfxTransactionCommandsTests.cs
tests/Arpeggio.Core.Tests/Cli/Sfx/SfxAnalysisLoopTests.cs
tests/Arpeggio.Core.Tests/Session/SfxRandomizationEditorTests.cs
docs/implementation.md
```

## SFX-E1 / E2 Claude レビュー時の修正（実行確認）

Codex 実装後、依頼者側で `dotnet build` / `dotnet test` を実行して発見した2件を修正した。

- **`SfxEditException` のコンストラクタが `internal`**: 別アセンブリ `Arpeggio.Cli` の `SfxFileTransaction.cs` から
  `new SfxEditException(...)` を呼んでおり CS1729 でビルド不可だった。`public` にし、`<summary>` を追加した。
- **System.CommandLine 2.0.11 のオプション貪欲消費バグ**: 値未指定のオプション（`--tone-enabled` 単体、`--lock` 単体）が
  末尾にあると、直後の兄弟オプション（`--json`）のトークンをそのまま値として飲み込み、解析エラーにならず
  `IOException`（存在しないファイル参照）や `UnsupportedParameter`（`--json` を値扱い）として誤った終了コードになっていた。
  同様に、`params` の `path`（`ZeroOrOne`）は未知オプション `--unknown` を正規の値として吸収し、解析エラーを起こさなかった。
  `Arpeggio.Cli/Sfx/CliArgumentGuard.cs` を新規追加し、値が `--` から始まるトークンを解析エラーへ変換する
  `Validator`（`Option<string>` 用と `Argument` 用の2オーバーロード）を用意して、`SfxParameterOptions` の全パラメータオプション・
  `--lock`・`params` の `path` 引数に適用した。あわせて `CliSfxExecution.ArgumentFailure` の `--json` 判定を
  `ParseResult` からではなく生の `arguments` 配列から行うよう変更した（トークンが飲み込まれると `--json` の
  `OptionResult` 自体が生成されないため）。
- 修正後、`dotnet build Arpeggio.slnx`（0警告0エラー）・`dotnet test Arpeggio.slnx`（2784件全成功）を確認した。

## SFX-E1 / E2 追加変更ファイル

```text
src/Arpeggio.Cli/Sfx/CliArgumentGuard.cs（新規）
```


# SFX-F1 / F2 実装記録（2026-09-10）

## SFX-F1 設計との差

- F3 まで既存 View と legacy 作成入口を維持し、新しい SfxEditorPresenter を MainWindowPresenter に併設する。候補・ジェスチャーと専用履歴は SfxEditingModel、保存済み候補の識別は SfxCandidateFile に分ける。新 UI の配線は F3 で切り替える。
- C2 の SfxEditor は tweak/regenerate/detach のみで、D2 の乱数出自を一履歴で適用する API がない。同期済み定義を受け取る ApplyParameters を最小追加し、既存の revision・保存・履歴境界を再利用する。旧 JSON 出力と既存メソッドの動作は変えない。
- Rx 規約に従い DAW にのみ System.Reactive 6.1.0 を追加し、通知・入力集約・試聴要求の寿命を Observable と購読で管理する。Core は BCL のみを維持する。150 ms 集約は偽時計で検証できる Presenter API とし、入力部品の接続は F3。

## SFX-F1 実装・静的確認

- SfxEditingModel の候補は専用 SongHistory をメモリ内に持つ。ドラッグは最後の有効値だけを更新し、確定で一履歴、取消で開始値へ戻す。現在文書の編集は確定時にだけ作業セッションへ適用し、Ctrl+S の正本保存を維持する。
- SfxEditorPresenter は Rx の通知・150ms集約・確定／停止／Open要求を公開する。プリセット、チップ切替、固定seedのrandomize／mutate、正規パスのグループロック、Undo/Redo、regenerateの事前件数／revision確認、detachを接続した。
- 新規保存は隣接一時ファイルから非上書き移動。保存パスとrevisionを成功後だけ記録し、候補変更後のOpenを無効化する。Openは現在文書のdirty／外部変更と保存済みファイルのrevisionを再検査し、prepareSwitchを呼ばない。
- MainWindowPresenterに保存・Undo/Redo・読み込み・外部変更・終了の寿命を接続。新しいViewは追加していない。旧SfxCreationPresenterとlegacyテストの契約を維持した。
- F1のテストコードと型定義／namespace／公開シグネチャを静的確認してからF2へ進む。ビルド・テスト実行による受け入れは未完了。

## SFX-F1 未完了・未実行の確認事項

- 実装とテストコード作成済み。警告ゼロのビルド、既存全件と新規テストの実行、正本保存・OS間のI/O失敗時の動作は依頼者／Claude Codeの確認待ち。
- 入力部品・タブ選択・フォーカス・ショートカットからPresenterへの配線は設計のF3。150ms集約へ渡す時計はUIスレッド上で通知するISchedulerを使う。

## SFX-F2 設計との差

- 音声出力の所有は既存 PlaybackEngine に集約し、通常／試聴の各 IAudioOutput 接続を AudioOutputOwnership で仲裁する。試聴の停止同期は既存 Stop 契約を使う。
- 最新世代のレンダラー準備は Rx Switch とキャンセル付き非同期生成で管理する。監視・終了はオフラインの通知経路で扱い、音声コールバックから Subject 通知や Stop／破棄を行わない。

## SFX-F2 実装・静的確認

- SfxPreviewPlayer は入力を複製してから既存 SongRenderer を非同期で構築する。44100 Hz／一回／tail=0／先頭開始を固定し、通常再生のtail=0.5と文書参照を引き継がない。
- Rx Switch が旧準備の CancellationToken を取り消し、停止・新要求・文書境界・終了で世代を更新する。取消を無視した準備の完了も世代照合で拒否する。ObserveOnでSwitch内部のロックと音声所有権ロックの順序逆転を避ける。非同期完了はDAW文書や候補モデルへ書き込まない。
- AudioOutputOwnership が一つの実デバイスを所有し、用途別の開始停止を仲裁する。試聴の準備開始時に通常再生を止め、通常再生開始時には準備待ちの試聴も失効させる。旧出力のStop完了後にだけ新出力を開始し、通常曲を自動再開しない。
- コールバックは既存Render、試聴専用ゲイン、先頭220フレーム（約4.99ms）のモニターフェードと状態値の受け渡しだけ。生成・JSON・Reset・Rx通知・破棄はコールバック外。ゲインとフェードは保存・解析・書き出しへ渡さない。
- 自然終了／音声障害は既存MainWindow.Poll境界で停止し、停止同期後にrenderer参照を解放する。SongRenderer自体はIDisposableではない。終了時は試聴購読とRxが所有する取消処理を終了し、PlaybackEngineから共有デバイスを一度だけ破棄する。

## SFX-F1 / F2 テストコード

- SfxEditorPresenterTests: 文書・正本不変、100更新の一履歴／一試聴、取消、不正入力の保持、偽時計150ms、グループロック・変異・出自とUndo、チップ切替、保存とOpen拒否の分離、保存済み候補の陳腐化、保存失敗と再試行。
- SfxEditingModelTests: 作業保存とCtrl+S、一履歴、三種の未知版、生成列／保存パラメータの別状態、明示再生成とUndo、外部revision競合、出自付き変異、作業保存I/O失敗からの再試行。
- SfxPreviewPlayerTests: 偽IAudioOutputによる三チップのtail0／再試聴一致、約5msのゲイン曲線、最新世代優先、取消無視の旧完了、停止・終了、準備待ちを含む通常再生との排他、スナップショット隔離、モニター音量、生成／出力失敗後の再試行。
- SfxPreviewLifetimeTests: 自動試聴OFFと手動再生、Undo・タブ離脱・Open・外部変更での停止、MainWindow終了によるデバイス解放。SfxPreviewAllocationTestsはAllocationCollectionで三チップの発音／制御フレーム／ゲイン処理のGC0を計測するコードを追加。

## SFX-F1 / F2 最終静的確認

- 変更・新規C#の括弧対応、doc XML、public summary隣接、ブロックnamespace、省略名、Assert.Single内Whereの不使用を確認した。使用するCore／DAW型の定義とnamespace、RxのSubject・Switch・FromAsync・ObserveOn・HistoricalScheduler・ToTask等をローカル参照XMLで照合した。新規ファイルは機能別フォルダへ配置した。
- 開始時のSHA-256との比較で、変更禁止の設計書3本、既存Coreの合成／音色／Render／Serializer／旧プリセット、CLI／MCP、View／AXAML、Core依存設定と既存テストの不変を確認した。追加依存はDAWのSystem.Reactiveのみ。JSON version=1を維持する。
- 既存のPCMとJSONの生成経路は変更していないが、バイト一致を実測したわけではない。コンパイル・アナライザ・テスト実行による結果も主張しない。作業ディレクトリ外への書き込み、git操作、dotnet build/test、アプリ起動は行っていない。

## SFX-F2 未完了・未実行の確認事項

F1→F2の実装とテストコード作成は完了。両ランの受け入れ確認は依頼者／Claude Codeのレビュー・実行待ち。

- `dotnet build Arpeggio.slnx` の成功・警告ゼロ、xunitアナライザ、および既存全件と追加テストの成功。
- 三チップのPCM／tail0／分割バッファ／ゲイン／GC0、保存・Undo/Redo・競合と未知版、OSごとのI/O失敗・SDLの停止同期。旧JSON／PCMの採取済み基準との全バイト比較。
- 実機の確定→出音遅延と切替時クリック。100ms以内は設計の目標であり未測定。
- F3では新PresenterへViewを切り替え、パラメータ部品・キー入力／フォーカス・タブ離脱を接続する。StatesはUIスケジューラーで購読する。試聴音量のアプリ設定への保存、WAV／解析への新候補の動線もF3のUI接続時に行う。旧legacy入口は本ランでは維持した。

## SFX-F1 / F2 変更ファイル一覧

既存更新5ファイル、新規15ファイル、計20ファイル。

```text
docs/implementation.md
src/Arpeggio.Core/Session/SfxEditor.cs
src/Arpeggio.Daw/Arpeggio.Daw.csproj
src/Arpeggio.Daw/Audio/AudioOutputLease.cs（新規）
src/Arpeggio.Daw/Audio/AudioOutputOwnership.cs（新規）
src/Arpeggio.Daw/Audio/PlaybackEngine.cs
src/Arpeggio.Daw/Audio/Sfx/SfxPreviewPlayer.cs（新規）
src/Arpeggio.Daw/Audio/Sfx/SfxPreviewRequest.cs（新規）
src/Arpeggio.Daw/Audio/Sfx/SfxPreviewState.cs（新規）
src/Arpeggio.Daw/Editing/Sfx/SfxCandidateFile.cs（新規）
src/Arpeggio.Daw/Editing/Sfx/SfxEditingModel.cs（新規）
src/Arpeggio.Daw/Editing/Sfx/SfxEditingState.cs（新規）
src/Arpeggio.Daw/Presenters/MainWindowPresenter.cs
src/Arpeggio.Daw/Presenters/Sfx/SfxEditorPresenter.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Audio/Sfx/PreviewAudioOutput.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Audio/Sfx/SfxPreviewAllocationTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Audio/Sfx/SfxPreviewPlayerTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Editing/Sfx/SfxEditingModelTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Presenters/Sfx/SfxEditorPresenterTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Presenters/Sfx/SfxPreviewLifetimeTests.cs（新規）
```

## SFX-F1 / F2 Claude レビュー時の修正（実行確認）

- `SfxEditException` のコンストラクタが `internal` のままで、`Arpeggio.Daw` 側の `SfxEditingModel` / `SfxCandidateFile` /
  `SfxEditorPresenter` から `new SfxEditException(...)` を呼べず CS1729 でビルド不可だった（SFX-E1/E2 と同一原因。
  develop 未マージのため個別に踏んだ）。`public` にし `<summary>` を追加して解消した。
- 修正後、`dotnet build Arpeggio.slnx`（0警告0エラー）・`dotnet test Arpeggio.slnx`（2728件全成功）を確認した。


# SFX-F3 実装記録（2026-09-12）

## SFX-F3 設計との差

- アプリ設定の既存保存口がないため、SFX のモニター音量と反復グループの展開状態だけをユーザー設定ファイルへ保存する。ソング JSON には混ぜない。実装中はアプリを起動せず設定ファイルも生成しない。
- 数値部品の具体 API は未指定のため、常時二段（ラベル・数値・単位／スライダー）とし、Core Catalog の範囲・刻み・選択肢を使う。型アクセサーは Core 内部用のため、表示値は保存スナップショットの parameters JSON から読む。
- 未保存候補の WAV／解析はスナップショットを直接処理する専用 Presenter に接続し、結果に revision と陳腐化表示を付ける。現在曲の解析・書き出し設定は変更しない。


## SFX-F3 実装・確認範囲

- 既存 SfxCreationView を F1 の SfxEditorPresenter／F2 の SfxPreviewPlayer へ切り替えた。固定ヘッダー・再生／停止・既定 ON の自動試聴・モニター音量と、固定フッターの保存／Open／WAV／解析を置き、中段だけを縦スクロールする。数値欄は128 DIPを確保した二段構成、ラベルと単位は折返し、Views にリテラル色は追加していない。
- Core Catalog の全32パスから現在チップの25項目（NES／GB）または22項目（SNES）を構成する。数値・対数周波数・bool・離散選択・初期値リセット・グループ単位の変異ロックを接続。ノイズ OFF 時はノイズ入力を無効にし、理由を表示する。SNES に duty slider は置かない。
- ドラッグは途中値と静的包絡だけを更新し、離したとき一操作で確定。数値は Enter／フォーカス離脱、キーと Home／End／ホイールの変更は最後の150ms後に確定する。UIスレッドの時計を既存 BindKeyboard に接続し、確定／新しいジェスチャーで旧タイマーを失効させた。
- 数値欄の未確定値を一つのpatchとして保持する。別欄へフォーカスを移しても Invalid を解除せず、全欄の訂正が済むまで履歴・確定試聴を増やさない。JSON数値の整数端数と極小指数をdouble化で失わず、Coreへ検証を渡す。Escは開始値へ戻し、取消後の入力へ古い文字列を混入させない。
- Tabで再生から入力を移動でき、左右キー／ShiftはCatalogの刻みを使う。SFX入力中はMainWindowのピアノロールショートカットを遮断。Spaceは文字入力・選択・チェック入力を妨げず試聴を切り替え、Escはジェスチャー取消または停止。Ctrl/Cmd+S と Undo/Redo、タブ離脱の確定／停止を接続する。
- 通常／操作中／生成中／試聴中／無効入力／保存失敗／外部競合／定義なし／生成列編集済み／保存パラメータ手修正／未知版／破棄中を文字で示す。無効入力では「最後の有効値を再生」と該当欄の理由・範囲を表示する。要求包絡秒数、実効フレーム／秒数、本体長、警告のフレーム区間と要求→実効値を表示する。
- 新規保存はOSピッカーから非上書き保存、Openは別操作の保護検査を維持。保存後に変更するとOpenを無効化して古い保存内容であることを明示する。生成列の置換は音色・トラック・ノート件数を確認したrevisionだけへ適用する。従来8雛形はfactoryの既存出力をそのまま新規候補へ取り込み、暗黙保存付きの旧作成APIは呼ばない。
- WAV／解析は現在候補の複製を直接処理し、現在文書を変更しない。WAVは44100Hz・一回・tail0、非上書き保存。結果にrevisionを付け、編集後は古い結果と表示する。モニター音量・試聴フェードは入れない。アプリ設定の保存失敗は画面内に表示し、購読・ピッカー・非同期結果は終了時に解放／失効させる。

## SFX-F3 テストコード

- 必須: `SfxParameterInputTests` は周波数の半音／1cent、反復の禁止区間、整数微調整、離散選択、カルチャ非依存の複数patch、不正文字列、整数端数／極小指数の保持を検証する。
- 必須: `SfxParameterFormTests` は複数欄の不正入力、フォーカス移動後のInvalid保持、訂正の一履歴、旧150msタイマーによる次ドラッグの途中確定防止、連続入力の集約、破棄後の確定抑止、取消後のバッファ破棄を検証する。
- 必須: `SfxOutputPresenterTests` は三チップの候補WAVと既存Core tail0出力の全バイト比較、現在文書と正本の不変、モニター音量0の非混入、既存出力の非上書き、一時ファイル後始末、解析revisionの陳腐化判定を検証する。
- 有用: 既存 `SfxEditorPresenterTests` に従来factoryの正規JSONと候補の一致・独立履歴とUndoを追加した。F1/F2の100更新一履歴／一試聴・保存とOpen拒否・タブ離脱・音声世代／停止のテストは維持した。
- 冗長: 部品のgetter／単純委譲・既定値だけの追加テストは不要。理由: 今回は数値変換・編集境界・保存不変の実害がある振る舞いを守り、GUI固有の配線とレイアウトは下記の実機項目で確認する。テストコードを実行した結果ではない。

## SFX-F3 静的確認

- 使用するCore／DAW型・namespaceと、Avalonia 12.1.2／Rx 6.1.0の入力プロパティ・イベント購読・UIスケジューラーをローカルソース／参照XMLで照合した。XMLに記載のないAvaloniaSynchronizationContextのコンストラクタ、Polyline.Points、Points等は参照DLLのメタデータを読み取って確認した（コードの実行・コンパイルではない）。
- AXAMLのXML構文、名前付き部品とRequire／BindButtonの参照、C#の区切り括弧・public summaryの隣接・1ファイル1型・ブロックnamespace・省略名・Assert.Single内Where不使用を静的確認した。Viewのリテラル色追加はゼロ。新規ファイルはViews/Sfx・Presenters/Sfx・Platform/Sfxと対応するテストフォルダへ置いた。
- 作業開始時のSHA-256と照合し、変更禁止の三設計書、Core全体（合成・音色・プリセット・Serializerを含む）、CLI／MCP、プロジェクト依存設定の不変を確認した。JSON version=1、Core BCLのみ、Render／AdvanceFrameの変更なし。旧音とJSONのバイト一致を実測したという意味ではない。
- git操作、ビルド、テスト、アプリ起動、音声生成・試聴、作業ディレクトリ外への書き込みは実行していない。

## SFX-F3 未完了・未実行の確認事項

実装とテストコードの作成・静的確認を完了。受け入れ完了は依頼者／Claude Codeのレビューと実行確認待ち。

1. `dotnet build Arpeggio.slnx` の成功・警告ゼロ、xunitアナライザ、既存全件および今回の追加テストの成功。F1/F2を含む既存テスト結果は本ランで再確認していない。
2. macOS／Windowsで1050×560、通常サイズ、125%／150%／200%表示を確認する。ヘッダーと試聴列・保存列が固定され、横スクロールなしで全項目へ到達できること。日本語ラベル、-1440、12000、0.016667、seed=4294967295、長いファイル名の符号・桁・単位が欠けないこと。
3. キーボードだけで新規候補→チップ／プリセット→全項目調整→試聴→保存→Openを完遂する。初期フォーカス、Tab順、チェック／コンボのSpace、数値の文字編集、左右／Shift、Home／End、Ctrl/Cmd+S／Z、Esc、ピアノロールへの漏れを確認する。
4. マウス／タッチの100更新ドラッグを一履歴・一試聴にすること。離す直前の値、領域外で離す、pointer capture消失、キー後のドラッグ、150ms直前の再入力、無効数値／別欄訂正、ドラッグ途中の保存／タブ移動／Escを確認する。
5. 自動試聴ON/OFF、手動再生・停止、生成待ちの停止、通常曲との排他、Undo／外部変更／文書切替／タブ離脱／終了後に古い音が鳴らないこと。確定→出音100ms以内は未測定の目標。クリック・SDL停止同期・CPU負荷も実機で確認する。
6. モニター音量と反復グループ展開の再起動後保持、設定I/O失敗、OSピッカー取消／同名拒否／保存中の候補変更、保存成功後のOpen拒否と再試行、保存後の候補変更・Undo、正本Ctrl+Sと外部競合を確認する。
7. 定義なし、既知同期、生成列編集、保存パラメータ手修正、未知版の表示・可否、件数確認→生成列置換→Undo、detach後の音保持。8プリセット×3チップの旧JSON／PCM基準比較とWAV／解析の結果revision・モニター非混入も実行確認する。

## SFX-F3 変更ファイル一覧

本ランの変更は計21ファイル。

```text
src/Arpeggio.Daw/Platform/Sfx/SfxUserSettings.cs（新規）
src/Arpeggio.Daw/Presenters/Sfx/SfxOutputPresenter.cs（新規）
src/Arpeggio.Daw/Presenters/Sfx/SfxParameterForm.cs（新規）
src/Arpeggio.Daw/Presenters/Sfx/SfxEditorPresenter.cs
src/Arpeggio.Daw/Presenters/Sfx/SfxParameterInput.cs（新規）
src/Arpeggio.Daw/Editing/Sfx/SfxEditingModel.cs
src/Arpeggio.Daw/Views/MainWindow.axaml.cs
src/Arpeggio.Daw/Views/SfxCreationView.axaml
src/Arpeggio.Daw/Views/SfxCreationView.axaml.cs
src/Arpeggio.Daw/Views/Sfx/SfxParameterRow.cs（新規）
src/Arpeggio.Daw/Views/Sfx/SfxEnvelopeView.cs（新規）
src/Arpeggio.Daw/Views/Sfx/SfxFileActions.cs（新規）
src/Arpeggio.Daw/Views/Sfx/SfxParameterPanel.cs（新規）
src/Arpeggio.Daw/Views/Sfx/SfxViewEvents.cs（新規）
src/Arpeggio.Daw/Views/Sfx/SfxParameterLayout.cs（新規）
src/Arpeggio.Daw/Themes/ArpeggioTheme.axaml
tests/Arpeggio.Core.Tests/Daw/Presenters/Sfx/SfxParameterInputTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Presenters/Sfx/SfxParameterFormTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Presenters/Sfx/SfxOutputPresenterTests.cs（新規）
tests/Arpeggio.Core.Tests/Daw/Presenters/Sfx/SfxEditorPresenterTests.cs
docs/implementation.md
```

## SFX-F3 Claude レビュー時の修正（実行確認）

- `SfxEnvelopeView` / `SfxParameterPanel` / `SfxParameterRow` が `this.FindResource(...)` をコンストラクタ内で呼んでおり、
  未だ論理ツリーへアタッチされていない時点でテーマリソース（`Arpeggio.Sfx.EnvelopeWidth` 等）が解決できず
  `UnsetValueType` を返し、直後の `(double)` / `(IBrush)` キャストで `InvalidCastException` となり
  DAW 起動直後にクラッシュしていた（`SfxCreationView.Bind` → `SfxParameterPanel` ctor → `SfxEnvelopeView` ctor）。
  `App.axaml` でテーマ辞書は Application レベルにマージ済みのため、`this.FindResource` を
  `Application.Current!.FindResource` に置き換えて解消した。
- 修正後、`dotnet build Arpeggio.slnx`（0警告0エラー）・`dotnet test Arpeggio.slnx`（2837件全成功）を確認した。
  DAW 起動時のクラッシュも解消し、目視検証（visual-verifier）で再確認済み。
