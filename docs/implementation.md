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
