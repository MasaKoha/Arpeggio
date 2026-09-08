# Arpeggio M3 設計書

NSF / VGM 書き出しと MIDI 取り込みの仕様を、2026-09-08 時点の M2-E-C 後の実装に対して確定する。本書は M3 の追加仕様の正本とする。既存の [design.md](design.md) の M1〜M2 再生仕様は変更しない。本ランの成果物は本書だけであり、以下の型・コマンド・テストは今後実装する仕様である。

## 決定事項

| 項目 | 決定 | 根拠・価値・工数判断 |
|---|---|---|
| NSF | **NSF v1、NTSC NES 基本音源を実装する**。Pulse × 2 / Triangle / Noise。DPCM と拡張音源は対象外 | NES 向けの実機演奏用データを渡せる。VGM と NES レジスタ変換を共有できる。専用部分を 4 ランに分割する |
| NSF プレイヤー | **自作の 6502 機械語プレイヤーを同梱する**。曲の解釈はホスト側で済ませ、プレイヤーは待機とレジスタ書き込みだけを行う | ノート・マクロ計算まで 6502 に移植しない。既存プレイヤーの音色形式への再変換・ライセンス・外部アセンブラの導入を避ける |
| NSF2 / NSFe | M3 では実装しない | NSF2 も実行コードを必要とし、機械語を不要にする方式ではない。IRQ・非復帰 INIT・追加メタデータは今回の再生経路に不要 |
| VGM | **v1.71 の NES APU / Game Boy DMG を実装する**。一ファイルに一チップ | CPU コードなしでレジスタ演奏を配布できる。NES は NSF と共用、GB は 4 チャンネルの変換で完結する |
| SNES の VGM | 実装しない。SNES 入力はエラー | 採用する標準 VGM に S-DSP / SPC700 のクロック欄・書き込み命令がない。SN76489 は SNES 音源ではない。別チップの PCM 命令への偽装や私有命令は採用しない |
| SPC | **M3 では見送る** | BRR / ADSR の再現だけでは曲を実行できない。SPC700 プレイヤー、タイマー、サンプルディレクトリ、曲・BRR・エコーを同居させる 64 KiB RAM 配置が別途必要。NES / GB と共有できない作業と変換損失が大きく、SNES の配布は既存 WAV / OGG を使う |
| MIDI 取り込み | **SMF format 0 / 1、PPQN、MIDI 1.0 を NES / GB / SNES へ取り込む** | 外部で作った旋律を編集可能な Song にできる。SNES は既存 16 プリセットを活用できる。GM 音源の完全再現は約束しない |
| MIDI 対象外 | format 2、SMPTE division、RMID、UMP / MIDI 2.0、ライブ入力、MIDI 書き出し、SysEx の実行 | ファイル取り込みの用途に絞る。独立シーケンスの選択・実時間入力・機器制御は別機能とする |
| 書き出し経路 | **Song → 発音制御列 → レジスタ列 → ファイル**。PCM・位相・LFSR 状態から逆算しない | 制御前の情報から周期・音量を決められる。実機へ設定できない内部状態を捏造しない |
| プロジェクト | 新規 **Arpeggio.Formats** 一つに NSF / VGM / MIDI を置く。Core と BCL のみを参照する | Arpeggio.Codecs は Vorbis の隔離アセンブリを参照している。音声コーデック依存を形式変換へ持ち込まず、形式ごとの小プロジェクトも増やさない |
| 既存再生 | WAV / OGG / DAW 再生の音・更新順・JSON version 1 を維持する | M3 のために既存合成をレジスタエミュレーションへ置換しない |
| 繰り返し | `loops` の有限回数を展開する。既定 1、上限 16。NSF の内部無限ループ・VGM のループポインタは M3 では使わない | 既存の「初回全曲、以後 loopStartTick から」の意味と、曲全体に固定された 60 Hz マクロ位相を保持する。任意のループ長でレジスタ状態・マクロ位相が周期的になるとは限らない |
| 品質契約 | 楽曲制御の変換と形式の正しさを保証する。既存 PCM とのビット一致・全実機プレイヤーでの同音は保証しない | ハードウェアの位相、DC、ミキサー、DAC、再トリガー副作用は既存の符号付き波形生成と異なる |
| 診断 | 変換結果・警告・エラー・恒常的な制限を共通レポートで返す。`strict` は変換警告をエラー扱いにする | AI が変更箇所を特定でき、人も DAW で変換前に確認できる |

NSF / NSF2 の裁定は [NSF 仕様](https://www.nesdev.org/wiki/NSF)・[NSF2 仕様](https://www.nesdev.org/wiki/NSF2)、VGM の対象は [VGMPlay 管理リポジトリの v1.71 仕様](https://github.com/vgmrips/vgmplay-legacy/blob/master/VGMPlay/vgmspec171.txt)、SPC の必要状態は [SPC ファイル仕様](https://wiki.superfamicom.org/spc-and-rsn-file-format)に基づく。実機とは NSF 対応カートリッジ等で演奏する NES を指す。NSF は起動可能な `.nes` ROM ではない。

### 現状の忠実度と移植できない状態

| 現状の根拠 | 再現済み | 実機出力と同一ではない部分 |
|---|---|---|
| `Synthesis/Nes/`、`PitchTable`、`NoiseOscillator` | 周期量子化、4 種のデューティ、32 段 Triangle、15 bit LFSR、非線形ミキサー | Pulse は位相比較。ミキサーは正負別の非線形式の差。Triangle の位相と Noise の seed を NoteOn で戻す。実機の length / linear / sweep / frame counter は実行していない |
| `Synthesis/GameBoy/` | 32 × 4 bit 波形、Pulse エンベロープ、7 / 15 bit Noise | エンベロープ間隔は 60 Hz の任意整数、音量はマクロとの積。パンは連続値。Wave 音量もノート音量との連続的な積。実機の DAC・再トリガー・波形 RAM アクセス制約はない |
| `Synthesis/Snes/`、`RenderMixer` | BRR 往復、512 点ガウス係数、14 bit pitch、32 kHz ADSR・FIR エコー、ピッチ変調 | ガウス積和は float、BRR は復号済み PCM を循環参照。共有カウンター位相とループごとの予測履歴再復号はない。ノイズはボイス別、EchoSend は連続値。実機は共有ノイズ周波数とボイス別 EON ビットを持つ |
| `BrrSample` / `SnesSampleInstrument` | PCM / プリセットから読み込み時に準備する | BrrSample が保持するのは復号 PCM。SPC 用 BRR バイト列・RAM 内の配置・実行プログラムを保持していない。PCM 上限 2 MiB は SPC の RAM 制約と両立しない |
| `SongRenderer` / `TrackSequencer` / `VoiceModulation` | サンプル境界の NoteOn / Off、Delay、有限ループ、60 Hz マクロと効果、次バッファ編集 | マクロ更新は曲全体のフレーム境界で先に進め、その後ノートを交代する。NoteOn から厳密に 1/60 秒後の更新ではない |

根拠となるローカルファイルはすべて `src/Arpeggio.Core/` 配下である。[implementation.md](implementation.md) の M1-A/B/C・M2-A/B/C・M2-E-A/B/C の判断も継承する。特に M2-E は DSP の再現であり、SPC の機械語・RAM 互換を実装したという意味ではない。SNES の共有ノイズとエコー設定は [S-DSP 実装の参照元](https://github.com/snes9xgit/snes9x/blob/master/apu/bapu/dsp/SPC_DSP.cpp)とも区別する。

## 用語

- **SMF トラック**: MIDI ファイルの MTrk チャンク。音源の発音チャンネルではない。一つの MTrk に複数 MIDI チャンネルが入る場合もある。
- **MIDI チャンネル**: ユーザー向けは 1〜16、バイナリ内部は 0〜15。ch 10 を GM ドラムとして扱う。
- **出力トラック**: Arpeggio の固定チップ構成における 0 始まりのインデックス。同種内の `ChannelIndex` とは区別する。
- **制御時刻**: 44100 Hz の絶対サンプル位置。PCM の配列を作るという意味ではない。
- **制御フレーム**: 60 Hz のマクロ更新境界。44100 Hz 上では 735 サンプルごと。
- **PLAY フレーム**: NSF プレイヤーの呼び出し間隔。NTSC 既定の 16639 µs とし、制御フレームとは別の単位とする。
- **レジスタ列**: 時刻・順序・実機アドレス・8 bit 値を持つ書き込み列。書き込み副作用を含むため、同値書き込みを一律に削除してはならない。
- **警告**: 入力に対して実施した近似・省略・切り詰め。**制限**は出力方式に常に存在する位相・ミキサー等の差であり、入力ごとの警告と分ける。

## フォーマット仕様

### 共通の時間・繰り返し・上限

| 項目 | 仕様 |
|---|---|
| 元データ | SongValidator 成功後の独立スナップショット。変換中の編集を追従しない |
| 曲長 | `lengthTicks + (loops - 1) × (lengthTicks - loopStartTick)`。既存 TickClock と同じ絶対値からサンプル位置を丸める |
| 端点 | ノートは半開区間。Delay 後に発音し元のノート終端で停止。隣接ノートは Off → On。周回をまたぐノートは既存シーケンサーどおり残り期間で再発音する |
| 制御更新 | FrameClock / TrackSequencer を使う。各フレーム更新 → ノート交代 → 出力状態確定。交代と同時刻の旧ノートの更新値は出力しない |
| 丸め | 時刻・最終音量は中間値をゼロから遠ざける。NES / GB 周期の丸めだけは既存 PitchTable と同じ ToEven とする |
| 書き込み順 | 初期化 → 各時刻の Off → 各時刻の On / 継続更新。トラック番号昇順。共有レジスタは shadow を保持し、NES の enable は length load より前、GB の routing 復元は trigger より後というチップ固有の順序で書く |
| 有限終端 | 曲本体終端で必ず全チャンネルを停止する。末尾余白・フェードは 0 固定。`tail` / `sample-rate` は NSF / VGM の引数にしない |
| 繰り返しの保存 | 展開済み演奏だけを保存する。VGM の loop offset / loop samples は 0。NSF は終端後の PLAY に対して無音で RTS。元の LoopStartTick はファイルから復元できない |
| 資源上限 | 展開後の長さ 1800 秒、元ノート 250000 件、レジスタ書き込み 4000000 件、VGM 64 MiB。NSF は後述の ROM 上限を優先。超過は書き込み前のエラー |
| メタデータ | タイトル・author・copyright の入力は各 1024 UTF-16 コード単位以下、NUL を拒否。日付を自動挿入しない。出力パス・実行日時はファイルに埋め込まない |

長い待機は絶対時刻の差から分割する。毎 tick の丸め誤差を加算しない。有限ループの展開はファイルを大型化させるが、状態の安定周期を探す処理と無限ループ再開時の独自リセットを M3 へ持ち込まないため採用する。無限ループ未対応は結果の `limitations` に必ず記載する。

### NSF v1

| 項目 | 値・仕様 |
|---|---|
| ヘッダ | 128 byte。署名 `NESM` + `1A`、version=1、total songs=1、starting song=1 |
| アドレス | load=`$8000`。INIT / PLAY は生成プレイヤーのラベルの実アドレス。little-endian。両入口は固定 bank 0 内 |
| 再生周期 | NTSC speed=`16639` µs。PAL speed 欄は `19997`、region=0（NTSC 専用）。PAL データを作ったとは扱わない |
| バンク | 初期値 `[0,1,0,0,0,0,0,0]`。bank 0 を `$8000–$8FFF` に固定し、データ bank 1〜255 を `$9000–$9FFF` で順に読む。変更先は `$5FF9` だけ |
| 容量 | ROM は 4 KiB 整列で最大 256 bank = 1 MiB。プレイヤー 4096 byte、曲データ最大 1044480 byte。ヘッダは別。末尾 bank は 0 埋め |
| 音源フラグ | expansion=0、DMC の DMA / IRQ は使用しない。予約 byte はすべて 0 |
| 文字列 | title は Song.Title、author / copyright は任意引数・既定空。各 32 byte、ASCII 31 byte 以下 + NUL。非 ASCII の Unicode scalar を `?` に置換してから切り詰め、変更時に `MetadataReduced` |
| 実機条件 | NTSC 基本 APU と標準 NSF バンク切り替えに対応したプレイヤーを対象にする。PPU、コントローラー、外部 BIOS、IRQ 拡張は使わない |

レジスタ列を PLAY 時刻へ移す際は `frame = round(sample × 1000000 / (44100 × 16639))` とする。全イベントを絶対位置から変換するのでテンポ誤差を周回ごとに累積しない。時刻移動は `NsfTimingQuantized` として最大誤差を返す。量子化だけの誤差上限は 8319.5 µs とする。CPU が書き込みを順に実行する時間は別に `maximumPlayCycles` で返す。

同一トラックの On とその Off が同じ PLAY フレームへ潰れる場合、または異なる二つの On が同じフレームへ入る場合は `NsfEventCollision` エラーとする。ノートを無断で消したり 1 フレームへ伸ばしたりしない。隣接する旧 Off / 新 On の同時刻は許可する。継続音の複数マクロ更新が同一フレームになった場合は最終制御値を採用し `ControlUpdateCoalesced` を返す。圧縮はレジスタ化の前に行い、既に作ったトリガー書き込みを雑に除去しない。VGM の 44100 Hz 時刻でも正の発音期間が 0 サンプルへ潰れる極端なテンポは `ControlEventCollision` として拒否する。

#### 6502 プレイヤーの責務

ホスト側の `NsfDriverBuilder` が、使用命令を限定したラベル解決付き生成処理で機械語を作る。機械語の巨大な手書き byte 配列だけを正本にせず、命令・アドレス・分岐先をレビュー可能にする。汎用アセンブラ、外部プロセス、既存ゲームのドライバーは使用しない。

| データ命令 | 意味 |
|---|---|
| `00 aa dd` | 実機 `$4000 + aa` へ dd を書く。aa は NES 変換が許可するアドレスのオフセットのみ |
| `01 ll hh` | 正の 16 bit PLAY 間隔を待つ。65535 より長い間隔は分割する |
| `02` | 終端。停止済み状態を保持し、以後データを読まない |

INIT は作業 RAM・データカーソル・待機数を初期化し、APU を停止して RTS で戻る。最初の PLAY がフレーム 0 の書き込みを実行し、以後の PLAY は待機数を減らして 0 になったときだけ次の書き込み群を実行する。待機または終端に到達したら必ず RTS へ戻る。再 INIT でも bank 1 と先頭カーソルを明示的に復元する。先頭 PLAY を演奏時刻 0 と定義し、ホストの INIT 後の起動待ち時間は曲長に含めない。

カーソルは bank 番号と 12 bit オフセットを持ち、命令やオペランドが `$9FFF` をまたいでも byte 単位で次 bank へ進める。次 bank の設定は次の byte を読む直前とし、bank 255 の最終 byte にある END も正常に扱う。終端検出前の bank 255 越え、未知命令、不正レジスタでは `$4015=0` として防御的に停止する。作業 RAM は `$0000–$001F`、スタックは呼び出し元の `$0100–$01FF` を通常の JSR / RTS で使う。スタックポインタを勝手に初期化せず、ROM への自己書き換えやコード bank の切り替えをしない。

命令は NES の文書化された 6502 命令だけとし、IRQ・DMA・非復帰 PLAY は使わない。各呼び出しの最悪分岐・ページ越えを含む静的サイクル上限をドライバー生成側で算出し、INIT は 20000 cycles、PLAY は **8000 cycles 以下**を受理する。超過は `NsfCpuBudgetExceeded` とする。約 29780 cycles の NTSC 間隔に十分な余裕を残す。実サイクル数との照合は独立したテスト CPU で行う。[6502 命令の参照仕様](https://www.nesdev.org/wiki/Instruction_reference)

### VGM v1.71

| オフセット / 命令 | 出力 |
|---|---|
| `0x00` / `0x04` / `0x08` | `Vgm ` / ファイル長 − 4 / `0x00000171` |
| `0x14` | GD3 の絶対開始位置 − `0x14` |
| `0x18` | 待機サンプル総数。終端の停止書き込みまでを含む曲本体長 |
| `0x1C` / `0x20` / `0x24` | loop offset=0 / loop samples=0 / rate=0。時刻をプレイヤーの 50 / 60 Hz 設定で変更させない |
| `0x34` | `0xCC`。コマンド開始は絶対 `0x100` |
| `0x80` / `0x84` | GB は `4194304` / 0、NES は 0 / `1789773`。既存 Core のクロックに揃える |
| その他のヘッダ | 256 byte の予約・未使用チップ欄を 0。dual-chip / FDS フラグを立てない |
| `B4 aa dd` | NES `$4000 + aa` への書き込み |
| `B3 aa dd` | GB `$FF10 + aa` への書き込み。wave RAM は aa=`20–2F` |
| `61 ll hh` | 1〜65535 サンプル待機。M3 はこの待機命令だけを出し、短縮命令は生成しない |
| `66` | 全停止の書き込みに続く音源データ終端。その直後に GD3 を置く |

整数は little-endian、VGM 時刻は常に 44100 Hz。NES / GB 以外の音源命令、PCM データブロック、VGZ 圧縮は出力しない。ヘッダから独立して待機を合計し、全長・各相対オフセットを検証できる形式に固定する。[VGM コマンド・ヘッダ仕様](https://raw.githubusercontent.com/vgmrips/vgmplay-legacy/master/VGMPlay/vgmspec171.txt)

GD3 は v1.00、11 個の NUL 終端 UTF-16LE 文字列とする。曲名原語欄に Song.Title、英語欄にはタイトルが ASCII の場合だけ同じ文字列を入れる。system 英語欄は `Nintendo Entertainment System` / `Nintendo Game Boy`、author 原語欄は指定値、変換者欄は `Arpeggio`。それ以外は空とする。翻訳・発売日・著作者を推測しない。copyright は NSF 専用引数とし、VGM では受け付けない。[GD3 仕様](https://vgmrips.net/wiki/GD3_Specification)

### MIDI 入力のバイナリ境界

SMF の規範は [MIDI Association の Standard MIDI Files Specification](https://midi.org/standard-midi-files-specification)とする。M3 は次のサブセットと回復規則を固定し、壊れた長さを推測して読み進めない。

| 入力 | 処理 |
|---|---|
| MThd | 先頭に一つ、長さ 6 以上。既知 6 byte を読み拡張部分を飛ばす。big-endian の format / track count / division を検証する |
| format | 0 は MTrk 一つ、1 は 1〜256。2 と未知値は非対応エラー |
| division | bit 15=0、PPQN=1〜32767。SMPTE、0 はエラー |
| MTrk | 宣言された数と一致すること。各チャンク長内で解析する。未知チャンクは長さを検証してスキップし警告。途中切れ・超過・重複 MThd はエラー |
| VLQ | 最大 4 byte、値は `0x0FFFFFFF` 以下。delta の積算は checked 64 bit。5 byte 以上・終端なしはエラー |
| channel message | `80–EF` の全メッセージ長を認識する。データ byte は 0〜127。Program / Channel Pressure は 1 byte、それ以外は 2 byte |
| running status | MTrk ごとに独立。`80–EF` にだけ適用する。トラック先頭、SysEx / meta 後に status 省略を許さない |
| SysEx | F0 / F7 の宣言長だけ飛ばし `SysExIgnored`。機器へ送信せず、GM / GS / XG reset も解釈しない |
| meta | Tempo `FF 51` は長さ 3・値 1〜16777215、EOT `FF 2F` は長さ 0。既知固定長の不一致はエラー。その他は有効長を検証して必要情報だけ読む |
| EOT | 各 MTrk に必須。EOT 後の当該チャンク内の byte はエラー。曲の入力終端は全 MTrk の EOT の最遅時刻 |
| MIDI port | port 0 または省略のみ。非 0 の MIDI Port meta は多ポート非対応エラー。異なるポートを同じチャンネルとして混ぜない |
| その他の system status | SMF イベントとして対象外の status はエラー。リアルタイムストリームのパーサーにはしない |
| 資源上限 | 入力 32 MiB、イベント 1000000 件、NoteOn 250000 件、実時間 1800 秒。メタデータ・SysEx を含め入力 byte 上限を適用する |

## 変換規則

### 共通のマクロ・ノートエフェクト

`VoiceModulation` の現在の計算と順序を共用する。音程は `baseMidi + macroArpeggio + effectArpeggio + (macroPitch + vibrato) / 100 + pitchSlide × progress`。音量 V は `clamp(noteVolume + volumeSlide × progress, 0, 15) × macroVolume / 225`。`progress` は経過制御フレーム数 / `max(1, 発音残り秒数 × 60)` を 1 以下へ制限した値とする。無指定マクロは音量 15、それ以外 0、デューティだけ音色の初期値を使う。

| Arpeggio の表現 | NSF / NES VGM | GB VGM | 落ちる情報・診断 |
|---|---|---|---|
| ノート開始・終了・Delay | ゲートと必要レジスタを更新 | DAC / trigger / routing を更新 | NSF は PLAY 量子化。VGM は 1/44100 秒。失われる極短ノートは NSF でエラー |
| VolumeMacro / VolumeSlide | `round(15 × V)` を constant volume へ。Triangle は固定音量 | Pulse はエンベロープとの積、Noise は V、Wave は段階音量へ | 最終整数化は `VolumeQuantized`。Triangle の非 15 音量・VolumeSlide は `TriangleVolumeIgnored` |
| ArpeggioMacro | 半音加算後に周期値を再計算 | 同左 | 音階を保持するが実機の周期量子化を受ける |
| PitchMacro / PitchSlide | セント / 半音を合成し周期値を再計算。ハードウェア sweep は使わない | 同左、NR10 sweep は無効 | 範囲外はレジスタ端へ制限し `PitchClamped`。変調中のクランプも対象 |
| Vibrato | 6 Hz、セント指定、60 Hz 更新を維持 | 同左 | NES timer high の変更による位相リセットは `PulsePhaseRestarted` |
| ノート Arpeggio | `0, highNibble, lowNibble` の三制御フレーム周期を音色マクロへ加算 | 同左 | 同時和音へは展開しない |
| DutyMacro | enum 1〜4 を duty bits 0〜3 へ | モデルに DutyMacro がないため対象なし | NES の duty 3 の極性・位相は実機のパターンに従う |
| マクロの終端 / LoopIndex | 空は既定値、非ループは最終値保持、ループは指定位置へ戻る | 同左 | 値列・LoopIndex は保存せず、その演奏結果だけを保存する |
| Track.Muted | 当該トラックは停止状態で出力。DPCM 拒否判定からも除外する | routing を切り、当該トラックは停止 | 固定スナップショットなので途中解除はない。NES / GB に他声への変調依存はない |
| Track.Pan | 無視してモノラルにし、非 0 なら `PanReduced` | 後述の左右 routing に量子化 | 連続パンと既存ミキサーの左右別非線形処理は保存できない |
| SNES 音色・BRR・ADSR・FIR・EchoSend | 入力チップ不一致でエラー | 同左 | SPC / SNES VGM の代替として PCM を埋め込まない |

丸めだけの微差を無数に報告しない。`VolumeQuantized` は元の 0〜15 レベルとの差が `1e-9` より大きい場合、`PitchClamped` は連続音程の範囲制限にのみ付ける。実機周期への通常の量子化は制限として説明し、毎ノートの警告にはしない。

### NES レジスタへの変換

周期は既存 `PitchTable.ClampMidiNote` と同じ範囲へ制限した周波数 f から作る。Pulse は `round(1789773 / (16f) - 1)`、Triangle は分母を 32 とする。既存 GetRange の Triangle 上限は timer=8 相当なので、M3 も両方 **8〜2047**に制限する。実機が持つさらに高い Triangle 周波数へ勝手に拡張しない。

| 対象 | レジスタ変換・副作用の扱い |
|---|---|
| 初期化 | `$4015=0`、`$4010=0`、`$4011=0`、`$4001=$4005=$08`、`$4017=$C0`。DMC IRQ・frame IRQ と sweep を無効化する |
| Pulse On | 対象の `$4015` bit を有効化してから duty / halt / constant / volume、timer low、timer high + 非零 length をロードする。length index は 0（実カウンター値は 10）で固定し halt=1 で保持する |
| Pulse 継続 | `$4000/$4004 = (dutyBits << 6) + $30 + volume`。low は変更時、high は上位 3 bit が変わるときだけ書く。On では同値でも high を必ず書く |
| Pulse Off | `$4015` の対象 bit を落とす。書き込み値は共有 shadow から作り、他声を止めない |
| Triangle On | enable bit → `$4008=$FF` → `$400A` low → `$400B` high + length index 0 → `$4017=$C0`。linear counter をロードさせる |
| Triangle 継続 / Off | 継続は周期の差分のみ。Off は enable bit を落として length を 0 にする。停止は波形の保持値であり DAC の値を 0 にする操作ではない |
| Noise | `selection=roundToEven(clamp(変調後Midi,0,127))`、period index=`selection & 15`。`$400E=(Short ? $80 : 0)+index`、`$400C=$30+volume`。On だけ enable と `$400F=0` をロードし、Off は enable bit を落とす |
| DPCM | ミュートされていない Dpcm トラックにノートが一つでもあれば `UnsupportedDpcm` エラー。空トラック・未使用の予約音色は許可。DPCM の別音色への置換はしない |
| 終端 | `$4015=0`、Pulse / Noise の volume=0。Triangle の DC 保持を「停止後 PCM が厳密に 0」と解釈しない |

`$4001/$4005=0` は sweep の target overflow による消音を防げないので使わない。`$08` を使う根拠は [APU Sweep](https://www.nesdev.org/wiki/APU_Sweep)。Pulse の high 書き込みは duty sequencer を戻すが timer divider を戻さないため、[APU Pulse](https://www.nesdev.org/wiki/APU_Pulse)の副作用を持つ。

Triangle の位相、Noise の LFSR seed を NoteOn で任意値へ戻すレジスタはない。これを `limitations` に明記する。`$4017` の即時クロックにも数 CPU cycles の遅延があり、VGM の時刻が APU の物理的な発音端点まで保証するわけではない。[APU のゲート・Triangle・frame counter](https://www.nesdev.org/wiki/APU_Status)

### GB レジスタへの変換

| 対象 | レジスタ変換・副作用の扱い |
|---|---|
| 初期化 | `NR52=0` → `NR52=$80`、`NR50=$77`（VIN 無効）、`NR51=0`、`NR10=0`。全 length enable=0。開始後は他声を壊す NR52 の再リセットをしない |
| Pulse 周期・duty | f を既存範囲へ制限し `period=clamp(roundToEven(131072/f),1,2048)`、register=`2048-period`。NR11 / NR21 に duty bits、NR13 / NR23 に low、NR14 / NR24 に high |
| Pulse の目標音量 | E=`clamp(InitialVolume ± floor(frame/EnvelopeStepFrames),0,15)`。step=0 なら初期値保持。目標整数音量は `round(15 × V × E/15)` |
| Pulse / Noise 音量更新 | ハードウェア envelope pace=0。On または正の目標音量が変わるとき、routing を一時解除 → NRx2=0 で DAC off → `NRx2=volume<<4` → NRx4 の trigger → routing 復元。継続音の再トリガーは `EnvelopeRetriggered` |
| 音量 0 / Off | NRx2=0、routing を解除する。0 から正へ戻る場合は同じ On 手順。sweep / envelope の live 書き換えに伴う機種差に依存しない |
| Pulse の継続ピッチ | NR13 / NR23 と high の変更分だけを書き、trigger bit は 0。音量の再トリガーと同時なら新周期を先に設定する |
| Wave RAM | On のたびに NR30=0 → `$FF30–$FF3F` の 16 byte → NR32 / NR33 → NR30=$80 → NR34 の trigger。byte i は `wave[2i]<<4` と `wave[2i+1]` のビット OR。発音中に RAM を書かない |
| Wave 音量 | `target=V × OutputLevel/100` を `{0,0.25,0.5,1}` の最短絶対距離へ丸める。同距離は小さい値。NR32 の bits 6–5 は順に `{0,3,2,1}`。差があれば `WaveVolumeQuantized`。継続更新で RAM 書き換え・trigger は不要 |
| Wave 周期 | Pulse 式の 131072 を 65536 に置き換え NR33 / NR34 へ。Off は NR30=0 と routing 解除 |
| Noise 目標クロック | Core と同じ selection から `262144 / (((selection & 7)+1) × 2^floor((127-selection)/8))` を計算する |
| Noise 周期 | NR43 の divisor code=0〜7、shift=0〜13 を総当たりし、目標クロックとの対数比の絶対値が最小の組を選ぶ。同点は NR43 値が小さい方。code 0 の実分母は 0.5、他は code 値。shift 14 / 15 は停止するので選ばない。差があれば `NoiseRateQuantized` |
| Noise 幅・ゲート | NR43 bit 3 は LfsrWidth=7 のとき 1。On は NR42 の一定音量と NR44=$80。Noise の目標音量は `round(15×V)`。再トリガーによる LFSR 再初期化も `EnvelopeRetriggered` の詳細に含める |
| パン | Pan < -0.5 は左、Pan > 0.5 は右、その他は両側。声番号 0〜3 に対し NR51 の bit=右、bit+4=左。Pan が -1 / 0 / 1 以外なら `PanReduced`。NR50 を声別音量の補償に使わない |
| 終端 | NR51=0、NR12 / NR22 / NR42=0、NR30=0 |

GB の 60 Hz envelope を 64 Hz のハードウェア envelope へ近似せず、現状の音量変化時刻を保存する。その代償として継続音の再トリガー・DAC pop・Noise の状態リセットが生じる。これは入力によって発生する警告である。Pulse の duty 位相を再トリガーだけで任意値へ戻せるとは扱わない。Wave の右シフト音量と DAC は Core の符号付き浮動小数乗算と異なる。[Pan Docs 音源レジスタ](https://raw.githubusercontent.com/gbdev/pandocs/master/src/Audio_Registers.md)、[内部動作](https://raw.githubusercontent.com/gbdev/pandocs/master/src/Audio_details.md)

### MIDI → Song の時間・発音・コントローラー

既存 Song は整数の単一 TempoBpm と 48 ticks/beat を持つ。テンポマップを JSON に追加せず、**元の演奏時刻を固定テンポのノート位置へ焼き込む**。

| 入力・条件 | 変換規則 | 診断 |
|---|---|---|
| イベント統合 | `(絶対MIDI tick, SMFトラック番号, トラック内イベント番号)` で安定整列。channel の状態は全 MTrk で共有。発音の出自は元トラック・event 番号まで保持 | SMF トラックを音源チャンネルと誤認しない |
| 同時刻の状態 | 元の整列順で Program / CC / Note を評価し、NoteOn 時点の設定を保存する。その後、完成したノート区間を出力用に割り当てる | ファイル内で CC が On より後なら既に開始した音へ遡及しない |
| NoteOn / Off | velocity=0 の NoteOn は Off。同じ channel / pitch の未終了 On は FIFO で対応させる。別トラック由来でも同じ channel なら同じ待ち行列を使う | 対応しない Off は `UnmatchedNoteOff`、EOT まで残った On は曲入力終端で閉じ `UnclosedNote` |
| 同じ tick の On → Off | 同一時刻に開閉した音は長さ 0 として破棄する。量子化で短くなった正の長さの音とは区別する | `ZeroLengthNoteDropped` |
| CC64 sustain | 値 64 以上で保持。Off を受けた発音を pedal up まで延ばす。EOT で解除。同音再打鍵も別 On として FIFO 対応を維持する | チップへの割り当て時の競合は通常の和音規則で処理 |
| CC120 / CC123 | 120 は sustain を無視してその時刻に全音停止。123 は全キーの Off として sustain を適用 | 再現可能なため警告不要 |
| CC121 | 音量 7=100、expression 11=127、sustain off に戻す。Program は維持する | 発音中の音量変更は下記と同じ |
| ベロシティ・CC7・CC11 | On 時に `v=round(15 × velocity/127 × channelVolume/127 × expression/127)`。正の積は最低 1、積が 0 なら音を生成しない。初期 CC7=100、CC11=127 | 4 bit 化は通常仕様として説明。0 音量の省略数は統計へ。Triangle 割り当て時は 15 とし、異なれば `TriangleVolumeIgnored` |
| 発音中の CC7 / CC11 | 次の NoteOn から適用する。既存音の分割再発音はしない | 有効な値の変化が発音期間内にあれば `ControllerDuringNoteIgnored` |
| CC10 pan | 保存する Pan は全トラック 0。CC10 は変換しない | 中央 64 以外は `MidiPanIgnored`。複数 MIDI 声の合流で一つの Track.Pan を上書きしない |
| Program Change | On 時点の program で音色を選ぶ。進行中の音色は変えない。省略時 program=0 | 音色対応は次節。Bank Select が非 0 なら GM bank 0 に限定し `MidiBankIgnored` |
| Pitch Bend / 圧力 / CC1 / RPN / NRPN 等 | ノート効果への不確かな推定変換はしない。Pitch Bend は中央以外、圧力・CC1 は非 0、それ以外の非対応 CC は受信時に警告 | `MidiExpressionIgnored`。strict で拒否できる |
| テンポ | 全トラックの Tempo を採用。未指定区間は 500000 µs/quarter。tick 0 の最終有効テンポを丸めた整数 BPM を出力基準にする。`tempo` 指定時はそれを基準にする | 後の実効テンポ変化は `TempoMapFlattened`。基準の整数化は `TempoRounded` |
| テンポの競合 | 同じ tick で異なる値が複数あれば安定整列上の最後を採用。format 1 の track 0 以外の Tempo も採用する | `ConflictingTempo` / `NonConductorTempo` |
| 実時間の計算 | 各区間の `deltaMidiTick × microsecondsPerQuarter / PPQN` を足す。分母 PPQN の共通整数分子を checked 64 bit で持ち、区間ごとに秒へ丸めない | 上限超過・オーバーフローはエラー |
| tick 量子化 | 実時間 t 秒を `round(t × outputBpm × 48 / (60 × grid)) × grid` へ。grid は 48 の正の約数、既定 1。開始・終了の絶対時刻を別々に丸める | 変化時に `MidiTimingQuantized`、最大誤差を報告 |
| 正の長さが潰れる場合 | 終端を開始 + grid にする。延長後に和音割り当てを行い重なりを解決する | `ShortNoteExtended`。ゼロ長だった入力音は復活させない |
| 曲長・先頭無音 | `max(48, 量子化した入力EOT, 全出力ノート終端)`。小節境界には延ばさない。先頭無音を保持。LoopStartTick=0 | 全音が消えた場合は `NoPlayableNotes` エラー。単なる空 Song の新規作成にはしない |
| タイトル・拍子・調号等 | title 引数 → track 0 の最初の非空 Track Name → 拡張子を除いたファイル名。テキストは strict UTF-8、失敗時 Latin-1。拍子・調号・歌詞・マーカー・その他 meta は保存しない | `MidiTextDecoded` / `MidiMetadataIgnored`。拍子をノート時刻へ乗算しない |

基準 BPM は 1〜1000 に制限する。自動算出値が外なら端へ制限して `TempoRounded` を返し、実時間からの再配置でテンポ倍率の誤りを防ぐ。明示引数の範囲外はエラー。例として PPQN=480、tick 0 で 120 BPM、tick 480 で 60 BPM の入力は、基準 120 BPM では元 tick 0 / 480 / 960 が Song tick **0 / 48 / 144**になる。テンポ変更をまたぐノートも同じ実時間積分で伸びる。拍・小節の見た目は保持しない。

### MIDI チャンネル割り当て・和音・音域

| 対象 | 自動割り当て候補 |
|---|---|
| NES の旋律（ch 10 以外） | `[0,1,2]` = Pulse 1 / Pulse 2 / Triangle。Noise / Dpcm へ旋律を送らない |
| NES の ch 10 | `[3]` = Noise。Dpcm は常に空 |
| GB の旋律 | `[0,1,2]` = Pulse 1 / Pulse 2 / Wave |
| GB の ch 10 | `[3]` = Noise |
| SNES、ch 10 の有効な On あり | 旋律 `[0,1,2,3,4,5]`、ドラム `[6,7]` |
| SNES、ch 10 の有効な On なし | 旋律 `[0,1,2,3,4,5,6,7]` |

`channelMap` は任意の JSON オブジェクトとし、例は `{"1":[0,1],"2":[2],"10":[3]}`。キーは MIDI チャンネル 1〜16、値は候補出力トラック番号の配列。指定チャンネルだけ自動設定を上書きし、空配列は明示的な除外とする。候補内の重複・範囲外・NES Dpcm 指定・NES / GB の旋律と Noise の相互指定はエラー。複数チャンネルが同じ候補を共有することは許可し、その競合を以下で解決する。候補の配列順による優先順位は持たず、常に出力トラック番号で決定する。

| 状況 | 確定した処理 |
|---|---|
| 発音の割り当て順 | 量子化後の開始 tick 昇順。同時開始の旋律は変換前の実効音量の降順 → MIDI pitch 降順 → MIDI channel 昇順 → 元トラック・event 番号昇順。開始時刻で終了済みの声を先に解放する |
| 空きあり | 候補のうち最小の出力トラックへ置く。MIDI チャンネル一つを一つの出力トラックへ固定しない。同一チャンネルの和音も複数声へ分けられる |
| 空きなし・既定 `steal-oldest` | 候補中で先に開始した既存音を打ち切る。同開始なら音量の小さい方 → 出力トラック番号の小さい方。同じ開始 tick に採用した新音は奪わず、残りの新音を破棄する |
| 空きなし・`drop-new` | 既存音を保ち、新音を破棄する |
| 打ち切り / 破棄 | `NoteTruncated` / `PolyphonyReduced` に元音・出力音の位置と長さを残す。一度打ち切った音を、後で声が空いたときに再開しない。和音を自動アルペジオ化しない |
| 音域外 | 割り当て先の連続発音範囲内にある最小〜最大の整数 MIDI 音へ最寄りクランプする。オクターブ折り返しはしない。元音と結果を `MidiPitchClamped` に記録する |
| NES / GB 音域 | 既存 PitchTable のチャンネル別範囲を使い、下限は ceil、上限は floor とする。Noise は音階検証の対象外 |
| SNES 音域 | 選択したプリセットの SampleRate / RootMidiNote から `P=round(2^((note-root)/12) × sampleRate/32000 × 4096)` が 1〜16383 となる整数音域を求める。root だけの既存 RenderReport の近似を再利用しない |
| 出力の構成 | ChipLayout の全トラックを維持し、空トラックも保存。Muted=false、Pan=0。全 Note に InstrumentId を明示する。DefaultInstrumentId は null のまま。各トラックのノートは昇順・非重複にする |

例: NES で同時開始・同音量の C4 / E4 / G4 / B4 を自動割り当てすると、B4 → Pulse 1、G4 → Pulse 2、E4 → Triangle、C4 は破棄する。この結果を仕様として固定し、旋律・ベースの役割を確実に指定したい入力には channelMap を使う。

### MIDI 音色とドラム

NES / GB の旋律はそれぞれ既定 50% Pulse、既定 Triangle / Wave で作る。GB Pulse は InitialVolume=15・EnvelopeStepFrames=0、Wave は既存の既定 32 値・OutputLevel=100。GM の program が違ってもこれらの音色へまとめ、使用した `(channel,program)` ごとに `ProgramApproximated` を返す。

SNES は次の表で既存プリセットを選ぶ。program は **バイナリの 0 始まり**である。preset の推奨 ADSR / root / loop / sampleRate を使い、Pan=0、EchoSend=0、NoiseEnabled=false、PitchModulation=false、Song.SnesEcho.DelayMilliseconds=0 とする。原曲にない残響を追加しない。プリセット名が同じものは音色を共用し、最初に出力へ採用された順に ID 1 から採番する。元 MIDI の未使用音色は作らない。

| GM program | SNES preset | GM program | SNES preset |
|---|---|---|---|
| 0–7 | piano | 8–15 | bell |
| 16–23 | organ | 24–31 | pluck |
| 32–39 | bass | 40–51 | strings |
| 52–55 | choir | 56–63 | brass |
| 64–71 | lead | 72–79 | flute |
| 80–87 | lead | 88–95 | strings |
| 96–103 | bell | 104–111 | pluck |
| 112–119 | bell | 120–127 | lead |

SNES も `ProgramApproximated` を返す。preset による 250 Hz 周期素材の約 +21 cent のずれやワンショット終端は既存仕様を維持し、MIDI 取り込みのためにプリセットを補正しない。

| GM drum note | 分類・SNES preset | NES Noise selection | GB Noise selection | 基準ゲート長 |
|---|---|---:|---:|---:|
| 35,36 | kick | 15 | 72 | 300 ms |
| 37,38,39,40 | snare | 10 | 96 | 150 ms |
| 42,44 | hat | 1 | 120 | 40 ms |
| 46 | openhat | 2 | 112 | 200 ms |
| 41,43,45,47,48,50 | tom | 12 | 88 | 200 ms |
| 49,51,52,53,55,57,59 | crash | 3 | 104 | 800 ms |
| その他 | snare へフォールバック | 10 | 96 | 150 ms |

ch 10 の短い MIDI gate で打楽器が即切断されるのを防ぐため、元の Off / sustain は音長に使わず、On から表の実時間を量子化して終端を作る。元の gate と異なれば `DrumGateReplaced`。NES は Long、GB は 15 bit の Noise を使い、`values[k]=round(15×(1-k/N))`（k=0〜N、N=`ceil(基準秒数×60)`、ループなし）の VolumeMacro を生成する。音色は分類ごとに共有し、Note.Volume はベロシティ換算値を使う。

ch 10 の Off は元 gate の比較用にだけ対応付け、UnmatchedNoteOff / UnclosedNote の警告対象から除く。Off がなければ `DrumGateReplaced` を返す。CC120 は打楽器も即停止し、表の終端より早い停止を優先する。CC123 / sustain は打楽器の固定ゲートを変更しない。

SNES は対応プリセットを root=60 の原速で使い、表のゲート長でノートを作る。NoiseEnabled に置き換えない。NES / GB の `midiNote` は表の selection であり原 MIDI の打楽器番号ではない。Noise 化は `DrumApproximated`、表の「その他」は追加で `UnknownDrumMapped` を返す。

同時刻ドラムは kick → snare → tom → crash → openhat → hat の順、その中で実効音量降順・元イベント順とする。旋律と候補が交差する明示 map では同時刻のドラムを先に割り当てる。後発ドラムも `polyphony` の設定に従う。既定は古い発音を切るためリズムの新しい打撃を残す。同時刻の採用済み打撃は奪わない。ハイハット choke、GM2 の排他グループ、元ドラムの音程は再現しない。

### 診断・失敗時の契約

| 分類 | 内容・扱い |
|---|---|
| 入力ドキュメント不正 | 既存 SongValidator / JSON の失敗。CLI exit 2 |
| 操作エラー | 対象外形式 / チップ、DPCM、壊れた MIDI、設定・map 不正、容量・CPU 上限、NSF 時刻衝突、NoPlayableNotes。CLI exit 1。部分成功にしない |
| I/O | 読み取り・一時ファイル・保存先確定の失敗。CLI exit 3 |
| 変換警告 | 各変換表の近似・省略・打ち切り。通常は exit 0。strict=true なら保存前に exit 1 |
| 恒常的な制限 | 位相・LFSR・ミキサー・有限ループ・GM 音色近似の方式説明を `limitations` へ。strict の失敗理由にはしない。実際に program を置換した事実は別途警告する |
| 情報 | 採用ノート数、明示除外チャンネル数、音量 0 による省略数、実際の割り当て表、生成音色。無音トラックがあるだけでは警告にしない |

`ConversionReport` は `format / chip / durationSeconds / outputBytes / warnings / errors / limitations / statistics` を持つ。警告・エラーは `code / message / sourceTrack / sourceChannel / sourceEvent / sourceTick / outputTrack / outputTick / original / converted / occurrenceCount` とし、無関係の位置項目は null。書き出しでは sourceTrack / sourceTick を Song 内の位置、MIDI では SMF 内の位置として format で識別する。sourceChannel はユーザー表記の 1〜16 とする。

同じ原因・元ノートのマクロ更新は一件へ集約し、最大誤差と発生数を保持する。明細は warnings 最大 4096、errors 最大 4096。超過数と全体件数・code 別件数は別カウンターで保持し、strict は表示された明細件数ではなく全警告件数で判定する。警告の上限を理由に変換損失を成功として隠さない。

全候補の変換・検証・サイズ算定を終えてから出力する。新機能のファイル API は隣接一時ファイルへ保存し、成功時だけ保存先へ移動する。既定は既存ファイルを上書きしない。NSF / VGM の `overwrite=true` だけ明示置換を許可し、入力ファイルと同一パスへの出力は常に拒否する。Stream API は呼び出し側所有で閉じず、書き込み中の I/O 失敗による部分 Stream は戻せないことを API 契約に含める。

## 構成

### 依存と型の責務

```text
CLI / MCP / DAW ──→ Arpeggio.Formats ──→ Arpeggio.Core ──→ BCL
       └────────→ Arpeggio.Codecs ──→ Arpeggio.Codecs.Vorbis

Arpeggio.Core/Synthesis/
  VoiceModulation             既存計算の可視性を公開へ変更。音声と変換で共有

Arpeggio.Formats/
  ConversionReport / ConversionDiagnostic / ConversionLimits
  Export/
    ChipExportOptions / ChipExportPlan / ChipExportService
    ControlTimeline           シーケンサー・マクロをイベント境界で進める
    RegisterTimeline          書き込み列と終端時刻。作成後は不変
    NesRegisterCompiler       NES のレジスタ化・副作用・診断
    GameBoyRegisterCompiler   GB のレジスタ化・副作用・診断
    VgmWriter                 ヘッダ・待機・コマンド・GD3
    NsfFrameCompiler          PLAY 量子化・衝突検証
    NsfDataEncoder            書き込み / wait / end の符号化
    NsfDriverBuilder          固定 bank の 6502 コードとラベル・サイクル上限
    NsfWriter                 ヘッダ・bank 配置
  Midi/
    MidiReader                上限付き SMF バイナリ解析
    MidiTempoMap              整数分子による実時間積分
    MidiNoteCollector         channel 状態・On/Off 対応・sustain
    MidiVoiceAllocator        候補 map・和音・発音打ち切り
    MidiInstrumentMapper      GM family・ドラムから既存音色を生成
    MidiImportOptions / MidiImportResult / MidiImporter
    MidiSongFile              新規 JSON の安全な保存
```

これは責務一覧であり、複数主要型を一ファイルへ詰める指示ではない。各型を一ファイルとし、enum は None=0、公開メンバーは日本語 summary、ブロック namespace、Core の既存言語制約を継承する。NSF / VGM / MIDI 用 NuGet、汎用プラグイン機構、Clean Architecture のレイヤーは追加しない。

公開の入口は `ChipExportService.Prepare(Song, options)` → `ChipExportPlan`、`ChipExportService.Write(plan, path, overwrite)`、`MidiImporter.Import(Stream, options)` → `MidiImportResult(Song候補, report)` とする。Prepare / Import は呼び出し元 Song とファイルを変更しない。エラーがある plan / result は保存できない。`ChipExportPlan` が検証済みの列を所有するため、診断時と保存時に別の変換を実行して結果をずらさない。大きな配列は PCM ではなく上限付きのイベント・レジスタ列だけとする。

ControlTimeline はノートの開始・Delay 後開始・終了・周回・マクロ境界の和集合を時間順に走査する。各点で既存 TrackSequencer を問い合わせる。44100 回/秒の PCM Render、ReadSample、NoiseOscillator、SNES DSP を呼ばない。グローバルな 60 Hz 境界を維持する。NSF では同じ制御列を PLAY フレームへ量子化してから NES レジスタ化し、VGM では元の制御時刻でレジスタ化する。

MIDI の統合順は reader → channel 状態と元ノートの収集 → 音色・打楽器分類と実時間 gate の確定 → tick 量子化 → voice 割り当てと音域制限 → 採用音色 ID の確定 → SongValidator とする。MidiVoiceAllocator は確定した gate と音域情報を入力で受け、音色を自分で生成しない。E4 の単体テストではその入力値を直接与え、E5 / E6 で実際の音色変換と接続する。Stream 版 Import のタイトル用入力名は options の SourceName（既定 `midi`）で受け、CLI / MCP / DAW がファイル名を設定する。

### 既存の音・676 件のテストを保つ移行

1. **依頼者側で基準を固定する。** M3 前の既存 676 件の結果と、NES / GB / SNES の短い決定的な曲の PCM・RenderReport・JSON を採取する。本設計ランは実行していないため、676 件の成功を再確認済みとは扱わない。
2. `VoiceModulation` の型・必要メソッド・読み取りプロパティを public にし、日本語 summary と明示 public コンストラクターを追加する。計算式、フィールド、呼び出し順、ChannelSynthesizer の使い方を変更しない。外部呼び出し側は Start → Configure → SetDuration → AdvanceFrame の順を守る。既存 public API の削除・改名はしない。
3. Formats と ControlTimeline を追加し、同じ入力で制御値・発音境界・警告箇所を照合する。合成器にレジスタ shadow、イベント発火、ファイル I/O を入れない。WAV / OGG / DAW 再生は従来の SongRenderer のままとする。
4. NES / GB のレジスタ化と形式 writer を順に追加する。実機との差を既存 PCM の期待値変更で隠さない。既存 676 件・PCM の一致・Render / NoteOn / 音色交換の GC 0 byte を依頼者側で確認してからフロントエンドへ接続する。
5. MIDI は有効な Song を作る外側の処理とする。Song / Track / Note の永続項目と version は追加しない。既存の SNES ピッチ警告やプリセット自体の改善は同時に行わない。

676 件は依頼で示された基準件数である。M3 の追加テストにより総件数は増える。既存テストの削除・緩和で基準を通すことは禁止する。形式変換経路にはオフラインの確保を許可するが、音声ホットパスへは持ち込まない。

### CLI / MCP / DAW

| CLI コマンド | 引数・既定値 |
|---|---|
| `arpeggio export nsf <song> <output.nsf>` | `--loops 1`（1〜16）、`--author ""`、`--copyright ""`、`--strict`、`--dry-run`、`--overwrite`、`--json` |
| `arpeggio export vgm <song> <output.vgm>` | `--loops 1`、`--author ""`、`--strict`、`--dry-run`、`--overwrite`、`--json`。チップは Song から決める |
| `arpeggio import midi <input.mid> <output.arpeggio.json> --chip <nes\|gameboy\|snes>` | `--tempo <1〜1000>`（省略時 MIDI 基準）、`--quantize-ticks 1`、`--polyphony steal-oldest`（または drop-new）、`--channel-map <map.json>`、`--title <文字列>`、`--strict`、`--dry-run`、`--json` |

MIDI は新規ファイル作成だけとし、`--overwrite` は設けない。既存ソングへのマージは対象外。channel-map は UTF-8 JSON ファイルで、先述の channelMap と同じオブジェクトを読む。dry-run は全変換・検証と予定サイズの計算を行い、ファイル・履歴・セッションを変更しない。既存出力への上書き指定がなくても内容の事前診断は可能とし、`destinationExists` を別に返す。

通常 CLI は要約を stdout、警告を stderr に出す。`--json` は report を含む一つの JSON を stdout に返す。形式の制限も表示する。失敗時にも code・位置を含む report を返し、既存の exit 0 / 1 / 2 / 3 の意味を維持する。既存 export wav / ogg の引数・既定値は変更しない。

| MCP ツール | 引数 |
|---|---|
| `export_nsf` | `path, loops=1, author="", copyright="", strict=false, dryRun=false, overwrite=false` |
| `export_vgm` | `path, loops=1, author="", strict=false, dryRun=false, overwrite=false` |
| `import_midi` | `midiPath, path, chip, tempo=null, quantizeTicks=1, polyphony="steal-oldest", channelMap=null, title=null, strict=false, dryRun=false` |

MCP export の入力は開いている Song。channelMap は既存の instrument / operations と同じ **JSON 文字列**で渡す。戻り値も既存どおり JSON 文字列とし、エラーは `error / exitCode` に report を追加する。既存ツールを削除・改名しない。import_midi は曲を開いていなくても利用でき、成功時に新規保存するが現在の共有セッションは変更しない。人・AI が続けて `open_song(path)` を呼んで切り替える。これにより保存後の Open 失敗と取り込み成功を一つの失敗に混ぜない。

| DAW 操作 | 追加仕様 |
|---|---|
| 「書き出し」 / Ctrl+E | ピッカーに `.nsf` / `.vgm` を追加。NES は両方、GB は VGM、SNES は WAV / OGG を提示する。拡張子を手入力した場合もチップ適合性を検証する |
| 「チップ書き出し設定」 | loops、author、NSF の copyright、strict。末尾余白と sample rate は表示しない。形式を選んだ段階で「通常の再生とは位相・音量が異なる」等の制限を表示する |
| 書き出し診断 | ExportPresenter が開始時のスナップショットで Prepare。警告と変換後の長さを非モーダルに表示し「書き出す」で同じ plan を保存する。エラー・strict 警告時は保存不可。既存ファイルは OS ピッカーの上書き確定に従う |
| 「MIDI を取り込む」 | 専用 MidiImportPresenter / View。入力・新規保存先・チップ・基準テンポ・量子化・polyphony・任意 map ファイルを選び「変換を確認」で候補を作る |
| MIDI 候補確認 | 採用 / 破棄 / 打ち切り数と警告、実際の channel → 出力トラック対応を表示。「新規保存」で JSON 作成。「開く」は独立操作とする |
| 文書・非同期の境界 | 既存の未保存文書を勝手に保存・破棄しない。新規保存では現文書の履歴を維持し、開く段階で既存の競合・未保存保護を通す。作業中は二重開始を拒否し、終了時キャンセル・通知抑止・一時ファイル清掃を行う |

CLI / MCP / DAW は同じ Formats の変換規則を呼ぶ。変換後の独自音源プレビューや NSF / VGM ファイルを開いて編集する機能は M3 に追加しない。

## テスト方針

### 外部エミュレータなしで確かめる範囲

形式 writer の往復だけでは、writer と reader が同じ誤りを共有する。**手計算した固定値、独立パーサー、レジスタの副作用を読む小さなテスト音源、6502 の実行トレース**を組み合わせる。外部エミュレータ・ゲーム ROM・ネットワーク・GUI・実機を自動テストの必須依存にしない。

| 検証層 | 具体的な受け入れ条件 |
|---|---|
| 制御値 | MacroRunner と VoiceModulation の空・終端・LoopIndex、複合効果、Delay、フレーム途中 On、同時 Off / On、周回の再発音を固定値で検証。境界更新順も確認する |
| NES 固定値 | A4 / 50% / 音量15の Pulse は timer=253、volume/duty=`BF`。Triangle A4 は timer=126。sweep=`08`。timer high を変えない vibrato では high を書かず、On では同値でも書く |
| NES レジスタ副作用 | length enable・linear load・`$4015` shadow・Noise mode の両方・全停止を独立レジスタモデルで確認。特定トラック Off が他声を止めないこと、DPCM を一切開始しないこと |
| GB 固定値 | A4 Pulse は register=1750（`6D6`）、A4 Wave は1899（`76B`）。波形 `[0,1,2,3,…]` の先頭 bytes は `01 23`。NR32 の 0 / 25 / 50 / 100% は `00 / 60 / 40 / 20` |
| GB 副作用 | wave RAM 書き込み時に DAC off、On 時に周期を設定してから trigger、継続音量変更の明示再トリガー、音量 0 から復帰、Noise shift 14/15 不使用、左右 routing・他声維持 |
| VGM 独立パース | magic、256 byte header、BCD version、チップ欄、全相対 offset、`61` の待機総和、`66`、GD3 長さ・11 終端・日本語を検証。生成に使った定数・パーサーをテストへ流用しない |
| VGM 往復列 | ファイルをテストパーサーで戻した `(sample,address,value,order)` と writer 入力列が完全一致。待機 1 / 65535 / 65536、先頭無音、終端と同時の停止、空トラック、2 周を含む |
| NSF 独立ロード | header → bank mapping をテスト側で再構築し、INIT / PLAY が固定 bank に入ること、ROM 長・パディング・データ上限・ASCII 終端を検証 |
| NSF CPU 実行 | テスト専用の限定 6502 実行器で実際の生成 byte 列を実行する。使用する全 opcode の flags・stack・addressing・分岐追加 cycles を手書き短命令列で先に検証。未実装 opcode に当たれば失敗させる |
| NSF 再生トレース | INIT → PLAY を予定回数 + 2 回呼び、APU 書き込みを `(PLAY番号, 実行cycle, address, value)` として採取。PLAY番号・値・順序を NsfFrameCompiler の期待列と照合。終了後の追加発音がなく、INIT 再実行で先頭へ戻る |
| NSF バンク・CPU 上限 | 命令先頭 / オペランド途中 / WAIT 途中 / END 直前の 4 KiB 越え、bank 255 終端、容量超過、長待機を検証。測定 cycles が静的上限以下、受理ファイルが INIT 20000 / PLAY 8000 cycles 以下であること |
| レジスタ再合成 | NES / GB の生成命令サブセットだけを処理するテスト専用 RegisterTraceRenderer を作る。周期・duty sequencer・gate・LFSR・wave RAM・一定音量・routing を独立実装し、短い既知曲で周期、非無音、左右、停止を検証する |
| 比較の基準 | レジスタ値と整数時間は完全一致。再合成の周期波単音は理論周期に対しゼロクロス誤差 2% 以内、duty 比率は 1 周期単位で正確。Noise は周波数ではなく LFSR 遷移・周期設定・非無音を検証する。振幅はテスト音源内の同条件で単調性を検証し、Core PCM との混合 RMS 一致を要求しない |
| NSF / VGM 相互比較 | NSF 用に量子化した NES 列をテスト内で VGM 化し、独立 VGM パースと NSF の PLAY 書き込み列が同値になること。通常の VGM と NSF の時刻が同一だとは要求しない |
| 保存・所有権 | dry-run・変換失敗・strict・容量超過・保存競合・途中 I/O・キャンセルで既存出力と元 Song が不変。Stream を閉じない。ファイル API の失敗後に一時ファイルが残らない |

RegisterTraceRenderer はソフトウェア合成器へ NoteOn を送り直すだけのアダプターにしない。それではレジスタ packing や trigger 副作用を検証できない。NES Triangle の停止後は DAC 保持値を認め、DC を除去した交流成分で停止を判定する。アナログフィルターや全 CPU/APU サイクル互換をこのテスト音源に要求せず、検証した命令サブセットを列挙する。

### MIDI のテスト

| 対象 | 具体的な固定ケース |
|---|---|
| SMF パーサー | 手作り format 0 / 1、PPQN 1 / 480 / 32767、running status・1 byte message・meta / SysEx 後の status、4 byte VLQ、未知 chunk、最大許容量。切断位置を全 byte 境界で変える |
| 拒否 | format 2・SMPTE・RMID・UMP・port 非 0・不正 track count・5 byte VLQ・データ MSB・EOT 欠落/後続 byte・Tempo 0・過大宣言長・資源上限。成功扱いで空 Song を返さない |
| channel 状態 | 異なる MTrk の同じ ch、同音 FIFO、velocity 0、未対応 Off、EOT 補完、sustain と再打鍵、CC120/123/121、同 tick の CC と On の順序 |
| テンポ | PPQN480・120→60 BPM の tick 0/480/960 → Song 0/48/144。変更をまたぐ音、テンポなし、端数 BPM、非 conductor、同 tick 競合、先頭無音。長い曲も絶対位置の誤差が grid/2 を超えない（明示延長を除く） |
| 和音 | 前述 NES の四和音、同 pitch・同 velocity の決定性、steal-oldest / drop-new、打ち切り後の Off が新音を止めないこと、同時 On を奪わないこと、明示 map の候補競合 |
| 音域・音量 | NES / GB 各声の上下端、SNES の root と元レートを含む端点、velocity 1/64/127、CC7/11=0、Triangle の 15 固定、波形選択と音色参照 |
| ドラム | 全表・未知番号、短い入力 gate、同時 kick / hat、後発打撃、SNES 2 ボイス予約とドラムなし 8 ボイス、Noise selection を MIDI 音階としてクランプしないこと |
| 保存往復 | import → SongValidator → Serialize → Deserialize → 同じ JSON。全 Note が正の長さ、曲内、昇順・非重複、存在する互換音色を参照する |
| 統合 | 3 チップの小さな MIDI → JSON → 既存 SongRenderer の非無音。NES / GB はさらに VGM、NES は NSF。元 MIDI のテンポ・和音・音色へ無損失で逆変換できるとは扱わない |
| フロントエンド | CLI exit と JSON、MCP 未オープン import / セッション維持 / ツール名追加、DAW 候補確認・新規保存・別操作の Open・破棄時キャンセルをプロセス内のテストで検証する |

### 40 分ランへの分割

各行は Codex 1 ランとする。主要設計は本書で確定しているため、実装ランの既定プロファイルは **std**。依頼者または Claude Code がレビュー・コンパイル・テスト実行を担当する。表の受け入れ条件は「テストコードを書いた」だけで完了せず、担当者による実行確認までを指す。Codex は各ランで実装・静的確認と未実行項目を報告する。

| ラン | 前提 | そのランで追加する範囲 | 受け入れ条件 |
|---|---|---|---|
| M3-A1 | 基準採取 | Formats プロジェクト・共通 options/report/limits、VoiceModulation の公開 | Core に NuGet / 逆依存なし。676 件と既存 PCM・JSON が不変。共通診断の上限・strict 判定テスト |
| M3-A2 | A1 | ControlTimeline と不変な制御列 | Delay・global frame・短音・同時交代・有限 2 周が既存時間規則と一致。PCM を生成しない |
| M3-B1 | A2 | NES Pulse / Triangle と初期化・共有 gate | A4 固定値、低音 sweep 無効、high 差分と強制 On、Triangle 起動・他声維持のテスト |
| M3-B2 | B1 | NES Noise・DPCM 拒否・終端・変換診断 | 16 周期・長短 mode、ミュート、DPCM エラー、全効果と副作用・警告のケースが揃う |
| M3-C1 | B2 | VgmWriter・GD3・独立パーサーテスト | NES の header / wait / レジスタ列完全往復、日本語 GD3、65536 待機分割 |
| M3-C2 | A2 | GB Pulse / Wave コンパイラー | 周期・RAM packing・段階音量・DAC/trigger 順序・パン量子化の固定テスト |
| M3-C3 | C2,C1 | GB Noise・ソフトウェア envelope・VGM 接続 | 全 selection の最寄り値、shift 14/15 不使用、音量更新・再トリガー・他声維持、GB VGM 往復 |
| M3-D1 | B2 | NsfFrameCompiler / NsfDataEncoder | PLAY 時刻誤差、衝突拒否、マクロ集約、WAIT/END、容量見積もりの境界テスト |
| M3-D2 | D1 | NsfDriverBuilder、bank 読み出し、ラベル解決 | 文書化 opcode だけで固定 bank 4096 byte 内。再 INIT・待機・終端の命令列と静的サイクル上限を提示 |
| M3-D3 | D2 | 独立した限定 6502 テスト実行器 | 使用全命令の flags / stack / branch / cycles の単体テスト。生成 INIT / PLAY の停止・書き込みを実行して確認 |
| M3-D4 | D3 | NsfWriter・バンク配置・CPU 予算の拒否 | 実ファイルを独立ロードし期待トレースと一致。オペランド途中の bank 越え、最大 bank、終端後、予算超過を検証 |
| M3-E1 | A1 | SMF reader と不正入力テスト | format0/1・PPQN・全 message 長・running status・長さ/資源上限を受理/拒否表どおりに処理 |
| M3-E2 | E1 | MidiNoteCollector、channel 状態・sustain・CC | 同音 FIFO、MTrk 横断、CC120/123/121、未終了・不明 Off・同 tick 順の固定テスト |
| M3-E3 | E2 | MidiTempoMap と絶対 tick 量子化 | 0/48/144 の固定例、端数テンポ、長曲、短音延長・元ゼロ長破棄・EOT 曲長が一致 |
| M3-E4 | E3 | MidiVoiceAllocator・map・音域の規則 | チップ候補・四和音・両 polyphony モード・同時 On・打ち切り・明示除外・非重複のテスト |
| M3-E5 | E4 | MidiInstrumentMapper・ドラム変換 | GM 全 128 program、ドラム表と同時優先、Noise / SNES プリセット、ID の決定性を検証 |
| M3-E6 | E5 | MidiImporter・MidiSongFile・全体 report | 3 チップで有効 JSON 作成、保存往復、strict / dry-run / 新規保存競合、元データ不変 |
| M3-F1 | C3,D4 | ChipExportService のファイル保存、CLI NSF / VGM | 両形式の prepare/write、引数・チップ・exit・report・上書き保護を CLI 統合テストで確認 |
| M3-F2 | E6 | CLI import midi | 三チップ・map ファイル・基準テンポ・dry-run・strict・既存 JSON 保護。新規履歴に古い側車履歴を引き継がない |
| M3-F3 | F1,F2 | MCP の三ツールと共通 report 出力 | 既存全ツール維持、export は開いている曲、import はセッション不変、dry-run / error/exitCode の契約テスト |
| M3-G1 | F1 | DAW チップ書き出し設定・診断・保存 | 正しい拡張子候補、同じ plan の保存、警告・strict、二重開始・終了・上書き保護の Presenter テスト |
| M3-G2 | E6,G1 | DAW MIDI 候補確認・新規保存・Open | 現文書と履歴の保護、候補の一致、別操作 Open、入力エラー・キャンセルの Presenter テスト |
| M3-H1 | C3,D4 | NES の独立レジスタ再合成・相互比較 | PCM 逆算なしで gate / duty / timer / LFSR を検証。NSF と量子化 NES VGM の演奏列が一致 |
| M3-H2 | C3 | GB の独立レジスタ再合成 | Pulse / Wave / Noise・routing・再トリガー・DAC off を検証し、周波数・非無音・停止の条件を満たす |
| M3-H3 | F3,G2,H1,H2 | 全体回帰・README / 実装記録の追従 | MIDI→JSON→各出力の統合、既存 676 件を含む全件・GC 回帰、失敗時ファイル保持。既知制限と未検証実機条件を記録 |

25 ラン、上限を単純に合計すると 16 時間 40 分の実装枠になる。これは実装・テスト作成の分割見積もりであり、待機・レビュー・依頼者側の実行時間を含まない。NSF の実機経路と独立検証を「ヘッダを書くだけ」の一ランにまとめない。SPC を追加すると RAM 割り当て・SPC700 命令列・独立実行検証が別系列になり、M3 の出荷を遅らせるため採用しない。

各ランは開始後 30 分までに成果を保存する。30 分を過ぎたら残りをそのランの「未完了」節へ記録して止める。未完成機能をフロントエンドへ公開しない。依存先の受け入れが未確認なら、後続はその条件を通過したと報告しない。

## 未決事項

M3 の対応範囲・形式・変換・コマンド・実装分割に、実装開始を止める未決事項はない。SPC、SNES VGM、DPCM、無限ループ、MIDI ライブ入力は「後で考えて M3 に含める項目」ではなく、本書で決定した M3 対象外である。

実機 NSF プレイヤーでの互換性と聴取差は未検証である。出荷時に対応実績を記載するには、使用機種・プレイヤー版・NTSC 動作を記録し、先頭発音、3 分以上のテンポ、bank 越え、終了・再 INIT、Pulse high 境界、Triangle / Noise を実際に確認する。これは設計の未決ではなく検証実績の不足であり、外部エミュレータなしの自動テストに合格しただけで「実機検証済み」と表示してはならない。

本設計ランで未実行の確認事項は、コンパイル、既存 676 件の再実行、音声・ファイル生成、外部プレイヤー・実機・DAW の動作確認である。実装コードは追加していない。
