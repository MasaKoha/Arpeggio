# Arpeggio

ファミコン（NES）・ゲームボーイ（DMG）・スーパーファミコン（SPC700 風）の曲と効果音を、AI と人が一緒に作るチップチューン DAW。

- `Arpeggio.Core` — 曲データモデル・3 チップの合成エンジン・WAV 書き出し（.NET 10、Unity 非依存）
- `arpeggio` — CLI。AI が曲データ（`.arpeggio.json`）を編集・書き出しする
- `arpeggio-mcp` — stdio MCP サーバー。Claude Code / Codex から直接叩く
- `arpeggio-daw` — Avalonia 製 DAW。ピアノロールで人が再生・微調整する（macOS / Windows）

設計の正本は [docs/design.md](docs/design.md)。マイルストーンと未決事項もそこに置く。

## 状態

M1 実装中（Core → CLI/MCP → DAW の順）。

## ビルド

```sh
dotnet build Arpeggio.slnx -nologo -v q -clp:ErrorsOnly
dotnet test Arpeggio.slnx -nologo -v q
```

## CLI

ビルド済みの `arpeggio` 実行ファイルを PATH に置いて使用する。

```sh
arpeggio new melody.arpeggio.json --chip nes --tempo 150 --length-beats 16 --title melody
arpeggio note add melody.arpeggio.json --track 0 --tick 0 --duration 24 --note C5
arpeggio note add melody.arpeggio.json --track 0 --tick 24 --duration 24 --note E5
arpeggio note add melody.arpeggio.json --track 0 --tick 48 --duration 24 --note G5
arpeggio note add melody.arpeggio.json --track 0 --tick 72 --duration 24 --note C6
arpeggio show melody.arpeggio.json --from-tick 0 --to-tick 96
arpeggio info melody.arpeggio.json --json
arpeggio export wav melody.arpeggio.json melody.wav --loops 1 --sample-rate 44100 --tail 0.5
arpeggio chip-reference nes
```

`--chip` は `nes / gameboy / snes`。ノート名は `C5 / C#5 / Db5 / 60`（MIDI 60 = C4）。トラックは 0 始まり、四分音符は 48 tick。`note move` は `--to-tick` と任意の `--note`、`note resize` は `--duration`、`note remove` は `--track` と `--tick` を指定する。

`show` は 12 tick ごとの表で、開始音を `C-5 15 01`、継続を `...`、無音を `---`、効果付きを末尾 `*` で示す。行の途中で開始する音は `@実tick`、同じ行の複数音は `;` で併記する。範囲は `[from-tick,to-tick)`、行は from-tick 起点。`--track` で絞り込み、`--json` で正確なノート値と表示テキストを取得する。

```sh
arpeggio instrument list melody.arpeggio.json --json
arpeggio instrument add melody.arpeggio.json --kind NesPulse --name lead --duty 25 --volume-macro "15,14,12/2"
arpeggio instrument set melody.arpeggio.json --id 2 --arpeggio-macro "0,4,7/0" --pitch-macro "null"
arpeggio note add melody.arpeggio.json --track 1 --tick 0 --duration 48 --note C4 --instrument 2 --effect PitchSlide=-4 Vibrato=20
arpeggio undo melody.arpeggio.json
arpeggio redo melody.arpeggio.json
```

音色追加の ID は自動採番して標準出力へ返す。`instrument set` は指定項目だけを更新する。マクロは `値,値,.../loopIndex`（省略時 -1）、`null` で解除する。不適合な kind のオプションは拒否する。

| 音色のオプション | 入力 |
|---|---|
| `--duty` | 12.5 / 25 / 50 / 75 |
| `--volume-macro` / `--arpeggio-macro` / `--pitch-macro` / `--duty-macro` | 整数列。単位は `chip-reference` を参照 |
| `--noise-mode` | NES Noise: Long / Short |
| `--initial-volume` / `--envelope-increasing` / `--envelope-step-frames` | GB Pulse: 0〜15 / true・false / 60 Hz のフレーム数 |
| `--waveform` / `--output-level` | GB Wave: 32 個の 0〜15 のカンマ区切り / 0・25・50・100 |
| `--lfsr-width` | GB Noise: 7 / 15 |
| `--waveform` / `--loop` | SNES: Sine・Square・Saw・Triangle・Pulse・Noise / true・false |
| `--adsr` | SNES: attack秒,decay秒,sustain(0〜1),release秒 |
| `--echo-send` / `--pan` | SNES: 0〜1 / -1〜1 |

`instrument remove <path> --id N` は未参照の音色だけを削除する。`instrument set --kind` は ID・名前と両 kind で共通する項目を保ち、専用項目を新 kind の既定値へ切り替える。

### バッチ

`arpeggio apply <path> --operations operations.json` で次の JSON 配列を適用する。`--operations -` は標準入力。`--track N` は各操作の track 省略時だけ使う。1 バッチは 1 回の undo 単位で、途中の不正操作では曲・履歴・ファイルを変更しない。

```json
[
  { "kind": "AddInstrument", "instrument": { "id": 2, "name": "lead", "kind": "NesPulse", "duty": "Percent25" } },
  { "kind": "AddNote", "track": 0, "tick": 0, "durationTicks": 24, "midiNote": 60, "volume": 15, "instrumentId": 2, "effects": [] },
  { "kind": "SetTempo", "tempoBpm": 180 }
]
```

| kind | 必須項目と任意項目 |
|---|---|
| AddNote | track・tick・durationTicks・midiNote。volume=15・instrumentId=1・effects=[] は省略可 |
| RemoveNote | track・tick |
| MoveNote | track・tick・toTick。midiNote は任意 |
| ResizeNote | track・tick・durationTicks |
| UpdateNote | track・tick。toTick・durationTicks・midiNote・volume・instrumentId・effects の指定項目だけを更新。effects=[] で解除 |
| AddInstrument / UpdateInstrument | instrument（保存形式と同じオブジェクト。Update は全置換） |
| RemoveInstrument | instrumentId |
| SetTempo / SetLength / SetLoopStart | tempoBpm / lengthTicks / loopStartTick |
| SetTrackMuted / SetTrackPan | track と muted / pan |

各操作直後にソング制約を検証するため、音色追加はその音色を使うノート追加より先に指定する。音色削除は参照ノート削除より後に指定する。

終了コードは成功 0、引数・操作エラー 1、ドキュメント不正 2、I/O エラー 3。`export wav` の警告は stderr に出すが成功コードは 0。`--json` 指定時は結果の `warnings` と `droppedWarningCount` に含める。

CLI 履歴は `<path>.history/state.json` に最大 50 件保存する。外部更新後は古い履歴を無効にする。履歴ファイルが破損している場合は `<path>.history` を退避して再実行する。

## MCP

復元・ビルド済みの `arpeggio-mcp` 実行ファイルを MCP クライアントの stdio サーバーとして登録する。

```json
{
  "mcpServers": {
    "arpeggio": {
      "command": "/absolute/path/to/arpeggio-mcp",
      "args": []
    }
  }
}
```

ツールは `new_song` / `open_song` / `save_song` / `song_info` / `show_song` / `add_note` / `remove_note` / `update_note` / `apply_operations` / `add_instrument` / `update_instrument` / `remove_instrument` / `export_wav` / `undo` / `redo` / `chip_reference`。

`new_song` と編集ツールは成功時に自動保存する。`open_song` は現在の曲とセッション履歴を差し替える。MCP の履歴はセッション内に保持し、CLI の履歴とは共有しない。`add_instrument` / `update_instrument` の `instrument` と `apply_operations` の `operations` は JSON **文字列**で渡す。音色更新はオブジェクト全体の置き換え。ノートの `effects` も JSON 配列文字列、省略で既存値を保持し `[]` で解除する。

戻り値は JSON 文字列。ただし `show_song` の既定と `chip_reference` はテキストをそのまま返す。入力・操作の失敗は `{"error":"説明","exitCode":1}` などの JSON 文字列を返す。
