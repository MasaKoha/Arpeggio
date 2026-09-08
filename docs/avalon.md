# Avalon による DAW の操作・観測

Avalon のメールボックスから、OS の合成マウスイベントを経由せずに Avalonia の入力と状態観測を行う。組み込み対象は **Debug かつ隣接 Avalon プロジェクトが存在する場合だけ**。Release は Avalon を参照せず、操作サーバーを含めない。

## この worktree の対応範囲

2026-09-08 時点の実コードは**単一ノート選択、固定 1/16 スナップ**。矩形選択・複数ノート移動・右ドラッグ連続削除・コピペ・音量レーン・スナップ選択 UI は存在しない。今回の組み込みでは編集挙動を変更していない。

`pianoRoll.selectedNoteCount` は実在する選択ノートを数え、現状は 0 または 1。`selectedTicks` は未選択なら空文字、選択中ならその開始 tick。FL 式編集を取り込んだ後は、実際の選択集合へ取得元を接続し、20 件を超えたら昇順の先頭 20 件と総件数（例 `0,12,...,228 (count=25)`。`...` は説明用省略）を表示する対応が残る。

以下の FL 式操作例は**取り込み後の検証用**であり、現状へ送っても成立しない。Avalon 側の `ActionExecutor` / `RoutedInputSender` の実コードにはドラッグの `modifiers` / `button` が実装されていることを確認した。実行確認は未実施。古い Avalon を使う場合は Avalon 側の対応待ちとなる。

## 有効化と接続

ディレクトリを次のように配置する。参照先は DAW の csproj からの相対パスで決まり、シェルの作業ディレクトリには依存しない。

```text
pisuke-root/
  Arpeggio-av/src/Arpeggio.Daw/Arpeggio.Daw.csproj
  Avalon/src/Avalon/Avalon.csproj
```

依頼者側で Debug ビルドを行い、**アプリ起動前**に次を実行する。本ランではビルド・起動を行っていない。

```sh
cd /Users/masakoha/GitHub/pisuke-root/Arpeggio-av
mkdir -p DebugOutput/agent-mailbox
touch DebugOutput/agent-mailbox/.enabled
# 上の作業ディレクトリから、ビルド済み Debug 版を検証用ソングのパス付きで起動する。
# src/Arpeggio.Daw/bin/Debug/net10.0/arpeggio-daw /absolute/path/test.arpeggio.json

python3 ../Avalon/tools/avalon_client.py ping
python3 ../Avalon/tools/avalon_client.py observe '{"scope":"all"}'
python3 ../Avalon/tools/avalon_client.py logs '{"count":40}'
```

`.enabled` がなければ `onStarted` もホストも作動しない。起動後に作った場合は再起動する。起動済みホストは `.enabled` の削除中、新規要求を受け付けない。接続先を変える場合は、アプリ起動前に `AVALON_MAILBOX` へ絶対パスを設定し、そのディレクトリへ `.enabled` を置く。クライアントにも同じ環境変数、または `--mailbox` を指定する。

Avalon がない環境では通常の DAW としてビルドできる。`AVALON` を手動定義する必要はない。`.enabled` を置いても Release には接続できない。

## 公開状態

`observe` と `act` の応答本文の `state:` に JSON 値として出力される。`expect` の `kind: "state"` から同じキーを検証できる。取得関数は UI スレッドで Presenter を読むだけで、編集確定・保存・Poll・音声合成は行わない。座標情報だけは View の現在のレイアウトから取得する。

`onStarted` はウィンドウ生成前に呼ばれるため、インスタンスの解決は観測時まで遅延する。画面生成前の動的な値は `null`。操作前に `daw.isReady=true` と正の可視幅・高さを確認する。

| キー | 意味・単位 |
|---|---|
| `daw.isReady` | Presenter が生成され、メインウィンドウが表示されているか |
| `song.path` | 開いている正本の絶対パス |
| `song.title` | 曲名 |
| `song.chip` | `Nes` / `GameBoy` / `Snes` |
| `song.isDirty` | 正本保存時から内容が変わっているか |
| `song.trackCount` | 現在のトラック数 |
| `song.noteCount` | 全トラックのノート数。ゴースト表示のトラックも含む |
| `pianoRoll.selectedTrack` | 選択トラックの番号。0 始まり |
| `pianoRoll.selectedNoteCount` | 選択中の実在ノート数。現状 0 / 1 |
| `pianoRoll.selectedTicks` | 選択ノートの開始 tick の文字列。現状は単一値または空文字 |
| `pianoRoll.snap` | 通常のスナップ間隔。現状 12 tick = 1/16。Alt 一時解除は含めない |
| `transport.isPlaying` | 再生状態 |
| `transport.positionTick` | 音声出力へ供給済みの位置。実際に聞こえる音より出力バッファ分先行しうる |
| `track.<n>.noteCount` | 0 始まりのトラック別ノート数。曲切替で不要になったキーは削除する |
| `pianoRoll.pixelsPerTick` | ズーム後の横幅。DIP / tick |
| `pianoRoll.noteHeight` | 半音の行高。18 DIP |
| `pianoRoll.topPitch` | スクロール前の全体最上段の MIDI 音高。127。可視最上段の音高ではない |
| `pianoRoll.originX` / `pianoRoll.originY` | 可視領域左上のウィンドウ内 DIP 座標 |
| `pianoRoll.scrollOffsetX` / `pianoRoll.scrollOffsetY` | 全体左上からのスクロール量。DIP |
| `pianoRoll.viewportWidth` / `pianoRoll.viewportHeight` | スクロールバーを除いた可視領域の幅・高さ。DIP |

`song.isDirty` は既存の正規 JSON 比較を利用するため、大きいソングの高頻度観測にはその比較コストが掛かる。

## 操作対象の名前とログ

| 名前 | 操作対象 |
|---|---|
| `DawWindow` | タイトルが曲名で変わっても固定指定できるメインウィンドウ |
| `PianoRoll` | 独自描画のピアノロール。ノートごとの UI 要素はない |
| `Keyboard` / `Ruler` | 鍵盤・時間ルーラー |
| `RollScroll` | ピアノロールの縦横スクロール |
| `Tracks` / `TracksScroll` | トラック一覧とスクロール領域 |
| `TrackRow<n>` | 動的な行コンテナ。例 `TrackRow0`。レイアウト要素のため観測本文では省略されうる |
| `TrackSelect<n>` / `TrackMute<n>` | トラックの選択ボタン・ミュート。例 `TrackSelect0` / `TrackMute0` |
| `SnapLabel` | 現状の固定スナップ説明。選択 UI ではない |
| `EditTab` / `AnalysisTab` / `SfxTab` | 右ペインのタブ |
| `EditScroll` / `TransportScroll` | 編集パネル・トランスポートのスクロール領域 |
| `SnesEchoPanel` | SNES エコー Flyout の内容 |
| `PlayButton` / `StopButton` / `LoopButton` | 再生・停止・ループ |
| `ExportButton` / `RevealExportButton` | 書き出し・最後の出力を Finder / Explorer で表示 |

既存の `x:Name` は維持している。音量レーンとスナップ選択は要素自体が未実装のため、名前で操作できない。その他の既存入力名は `observe {"scope":"all"}` / `find` で確認する。

ログには観測開始時の正本パス、ファイル読み込み成功（再読込を含む）、明示保存成功、書き出しの開始・完了・失敗を残す。拒否・失敗した保存を成功扱いしない。同じパスへの再保存も記録する。書き出し完了後の同一表示通知は重複記録しない。保存は既存の `Ctrl+S`、書き出しは既存の OS ピッカーを使う。ネイティブピッカーは Avalon の Avalonia ツリー外となる場合がある。

```sh
python3 ../Avalon/tools/avalon_client.py act '{"action":{"window":true,"name":"DawWindow"}}'
python3 ../Avalon/tools/avalon_client.py act '{"action":{"click":"TrackSelect0"},"expect":[{"kind":"state","key":"pianoRoll.selectedTrack","op":"eq","value":0}]}'
python3 ../Avalon/tools/avalon_client.py act '{"action":{"key":"Ctrl+S"},"expect":[{"kind":"state","key":"song.isDirty","op":"eq","value":false}]}'
python3 ../Avalon/tools/avalon_client.py logs '{"count":20}'
```

## tick・MIDI 音高から座標を計算する

すべて **DIP（論理ピクセル）**。画面上の絶対座標や PNG の物理ピクセルではない。Retina 倍率を掛けない。

```text
横座標 = originX + tick * pixelsPerTick - scrollOffsetX
行上端 = originY + (topPitch - midiPitch) * noteHeight - scrollOffsetY
行中央 = 行上端 + noteHeight / 2
```

`origin` は `PianoRoll` のローカル点 `(scrollOffsetX, scrollOffsetY)` を `TranslatePoint` でウィンドウ座標へ変換している。したがってスクロール量を二重に引かない。ノート開始点をクリックするなら上の横座標、ノート本体をドラッグするなら開始 tick より内側（以下では +6 tick）を使い、右端のリサイズ領域を避ける。

座標は `originX <= 横座標 < originX + viewportWidth`、`originY <= 行中央 < originY + viewportHeight` に入れる。範囲外なら `RollScroll` をスクロールしてから再観測・再計算する。ウィンドウサイズ・ズーム・曲切替後も再計算する。`PianoRoll` 全体の中心は画面外になりうるため、ノート編集は要素中心クリックに頼らず座標で指定する。

例として次の観測値を仮定する（固定座標をそのまま別ウィンドウへ流用しない）。

```json
{"originX":270,"originY":74,"pixelsPerTick":3,"noteHeight":18,"topPitch":127,"scrollOffsetX":0,"scrollOffsetY":774,"viewportWidth":700,"viewportHeight":500}
```

tick 24・C5（MIDI **72**）の行中央は `(342,299)`。そのノートの内側 tick 30 は `(360,299)`。tick 72・E5（MIDI 76）は `(486,227)`。MIDI 60 は C4。

## FL 式編集の検証例（実装取り込み待ち）

**現状では実行しない。** DAW 側に FL 式編集を取り込み、複数選択の状態公開を接続した後の検証手順。検証用ソングのトラック 0 を空にし、長さを 384 tick 以上、他トラックも空にして開始する。以下は上の座標条件を仮定する。

まず通常クリックで、tick 24・MIDI 72 と tick 72・MIDI 76 に長さ 24 tick のノートを各 1 個置く。最後に使ったノート長が 24 であることも前提。

```sh
python3 ../Avalon/tools/avalon_client.py act '{"steps":[{"click":"TrackSelect0"},{"pointer":true,"x":342,"y":299,"button":"left"},{"pointer":true,"x":486,"y":227,"button":"left"}],"expect":[{"kind":"state","key":"track.0.noteCount","op":"eq","value":2},{"kind":"state","key":"song.isDirty","op":"eq","value":true}]}'
```

### 矩形選択

tick 12〜108、MIDI 77〜71 の領域を Ctrl＋左ドラッグする。両ノートが選択され、曲のノート総数は変化しない。

```sh
python3 ../Avalon/tools/avalon_client.py act '{"action":{"drag":true,"fromX":306,"fromY":200,"toX":594,"toY":326,"milliseconds":300,"modifiers":"Ctrl","button":"left"},"expect":[{"kind":"state","key":"pianoRoll.selectedNoteCount","op":"eq","value":2},{"kind":"state","key":"pianoRoll.selectedTicks","op":"eq","value":"24,72"},{"kind":"state","key":"song.noteCount","op":"eq","value":2}]}'
```

### まとめて移動

選択済みの最初のノート内側から右へ 24 tick・上へ 2 半音ドラッグする。開始 tick は `48,96`、MIDI 音高は `74,78` になる想定。

```sh
python3 ../Avalon/tools/avalon_client.py act '{"action":{"drag":true,"fromX":360,"fromY":299,"toX":432,"toY":263,"milliseconds":300,"button":"left"},"expect":[{"kind":"state","key":"pianoRoll.selectedNoteCount","op":"eq","value":2},{"kind":"state","key":"pianoRoll.selectedTicks","op":"eq","value":"48,96"},{"kind":"state","key":"track.0.noteCount","op":"eq","value":2}]}'
```

開始 tick と選択数は state で検証できる。音高そのものの変更は今回の必須キーに含まれないため、撮影または正本を保存してノート値を照合する。`Ctrl+Z` 一回で両ノートが戻ること、`Ctrl+Shift+Z` 一回で再移動することも確認する（Undo 後に選択解除する版では選択数の保持を条件にしない）。

### 右ドラッグ削除

上の移動直後から、二つのノートの内側を通る直線を右ドラッグする。両ノートが削除される想定。

```sh
python3 ../Avalon/tools/avalon_client.py act '{"action":{"drag":true,"fromX":432,"fromY":263,"toX":576,"toY":191,"milliseconds":300,"button":"right"},"expect":[{"kind":"state","key":"track.0.noteCount","op":"eq","value":0},{"kind":"state","key":"pianoRoll.selectedNoteCount","op":"eq","value":0},{"kind":"state","key":"pianoRoll.selectedTicks","op":"eq","value":""}]}'
```

`Ctrl+Z` 一回で二つとも復元されることを確認する。現状の DAW は押下位置の単一削除しかないため、この例を通過しない。

### コピペ

削除例とは別に、まとめて移動した直後の二つが選択された状態から開始する。**貼付開始位置をマウスの tick 144 とする FL 版を想定した例**。現在の worktree に貼付処理がないため、実装取り込み時に貼付位置の決定規則を照合し、必要なら hover をその位置指定操作へ差し替える。

```sh
python3 ../Avalon/tools/avalon_client.py act '{"steps":[{"focus":"PianoRoll"},{"key":"Ctrl+C"},{"hover":true,"x":702,"y":263},{"key":"Ctrl+V"}],"expect":[{"kind":"state","key":"track.0.noteCount","op":"eq","value":4},{"kind":"state","key":"pianoRoll.selectedNoteCount","op":"eq","value":2},{"kind":"state","key":"pianoRoll.selectedTicks","op":"eq","value":"144,192"}]}'
```

貼付前後で元の二つが残ること、貼付が一回の Undo で取り消されることを確認する。失敗やクライアントのタイムアウト後は、同じ変更を重複送信せず `observe` で実行済みか確認する。

## 依頼者側での確認

- Debug＋Avalon あり、Debug＋Avalon なし、Release の各ビルド。警告ゼロと既存・追加テストの全件成功。
- Avalon なしの条件は、隣接リポジトリを移動せず `-p:AvalonProjectPath=/absolute/nonexistent/Avalon.csproj` でも確認できる。
- `.enabled` なしではホストが動かず、ありでは `ping` / `observe` / `act` / `logs` が応答すること。
- NES / GB / SNES 切替後のトラックキー増減、選択解除、スクロール・ズーム・サイズ変更後の座標、読み込み・保存・書き出しログと終了時の解放。
- FL 式編集取り込み後に複数選択、20 件超の tick 表示、音量レーン・スナップ選択の名前を接続し、上の検証例を実行すること。

Avalon の語彙の正本は隣接リポジトリの `docs/getting-started.md`、`docs/ops-reference.md` と `src/Avalon/`。
