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
