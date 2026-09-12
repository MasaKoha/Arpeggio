# Arpeggio

ファミコン・ゲームボーイ・スーパーファミコン風の曲や効果音を AI に作らせようとすると、すぐに壁に当たる。
AI は音を聞けない。鳴らした結果が意図どおりか、どの音符がどこに置かれたか、音量が飛び出していないかを、
人が毎回耳で確かめて言葉で返すしかなく、往復が終わらない。

Arpeggio は、この往復をテキストで閉じるために作ったチップチューン DAW である。
AI は曲データをコマンドで編集し、音を鳴らさずに結果を読める形で確かめる。
人はピアノロールで再生して微調整する。対象チップは NES・DMG・SPC700 風の 3 つ、
.NET 10 製で Unity には依存しない。

## 曲はこう見える

音を鳴らさずに、今どの音符がどこにあるかを確かめられる（`show`）。

```
Tick | [0] Pulse 1 | [1] Pulse 2 | [2] Triangle 1 | [3] Noise 1 | [4] Dpcm 1
0000 | C-5 12 01   | ---         | C-3 15 02      | C-4 08 03   | ---
0012 | ...         | ---         | ...            | ---         | ---
0024 | E-5 12 01   | ---         | ...            | C-4 08 03   | ---
0036 | ...         | ---         | ...            | ---         | ---
0048 | G-5 12 01   | ---         | G-2 15 02      | C-4 08 03   | ---
0060 | ...         | ---         | ...            | ---         | ---
0072 | C-6 12 01*  | ---         | ...            | C-4 08 03   | ---
0084 | ...         | ---         | ...            | ---         | ---
```

12 tick ごとに区切った表で、列がトラックにあたる。`C-5 12 01` は音名・音量・音色 ID の順。
`...` は前の音が続いていること、`*` はエフェクトが付いていること、`---` は無音を表す。

鳴らしたときにどうなるかも、レンダリングして音量・周波数・警告として読める（`analyze`）。
区間ごとに支配的な周波数と音名が出るので、意図した音が鳴っているかを目で確認できる。

```
長さ: 1.6000 s | 44100 Hz | 窓: 100 ms
RMS: -16.87 dBFS | ピーク: -7.12 dBFS | クリップ: 0
無音割合: 50.00 % | 左右差 L-R: 0.00 dB
帯域: 低 <200 Hz 59.50 % | 中 200–2000 Hz 34.61 % | 高 >2000 Hz 5.89 %

警告:
なし

開始 s | RMS dBFS | ピーク dBFS | 支配的 Hz | 音名 | 重心 Hz
0.0000 | -13.61 | -7.12 | 131.74 | C3 | 558.42
0.4000 | -13.74 | -7.12 | 97.59 | G2 | 719.56
```

## 何ができるか

**まとめて編集して、1 手で戻せる。** 複数の操作を JSON の配列で渡すと、全体が 1 つの履歴になる（`apply`）。
取り消しとやり直しは 1 コマンドで済む。

**3 つのチップを同じ書き方で扱える。** ファミコン、ゲームボーイ、スーパーファミコンで
チャンネル構成も使える音色も違うが、操作の語彙は共通である。
どのチップに何本のチャンネルがあり、どんな音色が使えるかは引ける（`chip-reference`）。

**書き出しは 4 形式ある。** 音声としての WAV と OGG、チップの演奏データとしての NSF と VGM。
MIDI の取り込みもできる。効果音はプリセットから作れる（`sfx`）。

**人が仕上げる画面がある。** Avalonia 製の DAW でピアノロールを開き、再生しながら微調整できる
（`arpeggio-daw`、macOS / Windows）。効果音と解析のパネルも備える。

部品は 4 つに分かれている。

| | 役割 |
|---|---|
| `Arpeggio.Core` | 曲データモデル、3 チップの合成エンジン、WAV 書き出し |
| `arpeggio` | CLI。AI が曲データ（`.arpeggio.json`）を編集・書き出しする |
| `arpeggio-mcp` | stdio の MCP サーバー。Claude Code / Codex から直接叩く |
| `arpeggio-daw` | Avalonia 製 DAW。人が再生・微調整する |

## 状態

| 段階 | 実装済みの内容 |
|---|---|
| M1 | Core / CLI / MCP / DAW |
| M2 | 効果音プリセット、音声解析、OGG 書き出し、SNES への WAV 取り込み、DAW の効果音／解析パネル |
| M3 | NSF / VGM 書き出し、MIDI 取り込み。CLI / MCP / DAW のすべてから利用可能 |

テストは 3006 件、ビルド警告はゼロ（2026-09-12 時点）。

## 試す

```sh
dotnet build Arpeggio.slnx
```

上のトラッカー表示に載せた曲は、次のコマンド列で作ったものである。

```sh
arpeggio new demo.arpeggio.json --chip nes --tempo 150 --length-beats 4 --title "デモ"
arpeggio instrument add demo.arpeggio.json --kind NesTriangle --name "ベース"
arpeggio note add demo.arpeggio.json --track 0 --tick 0 --duration 24 --note C5 --volume 12
arpeggio note add demo.arpeggio.json --track 0 --tick 72 --duration 24 --note C6 --volume 12 --effect Vibrato=20
arpeggio show demo.arpeggio.json
arpeggio export wav demo.arpeggio.json demo.wav
```

コマンドの全一覧と引数は `arpeggio --help`、および [docs/cli-reference.md](docs/cli-reference.md) にある。

## AI エージェントから使う

stdio の MCP サーバー（`arpeggio-mcp`）を登録すると、Claude Code や Codex から直接呼べる。
CLI で行える編集・確認・書き出しは、MCP からも同じ語彙で使える。

進め方は人が使うときと変わらない。まずチャンネル構成と使える音色を確かめ（`chip-reference`）、
曲を編集し、配置をトラッカー表示で確かめ（`show`）、音量と周波数を読み（`analyze`）、
問題がなければ書き出す。複数の編集をまとめたいときは操作配列を渡す（`apply`）。

曲データ（`.arpeggio.json`）はテキストなので、差分がそのまま読める。
AI が書いた曲を人がレビューし、DAW で直す、という往復ができる。

## 気をつけること

**NSF と VGM は実機のプレイヤーで検証していない。** 自動テストでは、独立に書いたパーサーと
限定的な 6502 実行器で検証している。対応範囲の正本は [docs/design-m3.md](docs/design-m3.md) にある。

**トラックごとに使える音色の種類が決まっている。** 合う音色が無いとノートを追加できない。
その場合は先に足す（`instrument add --kind`。Triangle トラックなら `NesTriangle`）。

**スーパーファミコンは内蔵音色バンクから選ぶ。** `orchestral` / `band` / `chip` の 3 つがある。

**配布ビルドは未署名である。** macOS では初回起動に追加の操作が要る。
手順は [docs/distribution.md](docs/distribution.md) にある。

## もっと詳しく

| ページ | 内容 |
|---|---|
| [docs/cli-reference.md](docs/cli-reference.md) | CLI・DAW・MCP の全コマンドと引数、SNES 内蔵音色バンク |
| [docs/distribution.md](docs/distribution.md) | 配布ビルドと署名まわり |
| [docs/design.md](docs/design.md) | 設計の正本。マイルストーンと未決事項 |
| [docs/design-m3.md](docs/design-m3.md) | NSF / VGM と MIDI の対応範囲の正本 |
| [docs/design-sfx.md](docs/design-sfx.md) | 効果音の設計 |
| [docs/implementation.md](docs/implementation.md) | 実装記録と未検証事項 |

開発時のビルドとテストは次のコマンドで行う。

```sh
dotnet build Arpeggio.slnx
dotnet test Arpeggio.slnx
```

MIT License
