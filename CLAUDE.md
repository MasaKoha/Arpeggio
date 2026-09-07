# CLAUDE.md — Arpeggio

ファミコン・ゲームボーイ・スーファミ風の曲と効果音を AI と人が作るチップチューン DAW。`Arpeggio.Core`（.NET 10 ライブラリ）＋ CLI `arpeggio` ＋ MCP サーバー `arpeggio-mcp` ＋ Avalonia DAW `arpeggio-daw`。

- 設計の正本: `docs/design.md`。仕様変更はまずここを直す
- ビルド: `dotnet build Arpeggio.slnx -nologo -v q -clp:ErrorsOnly`
- テスト: `dotnet test Arpeggio.slnx -nologo -v q`
- C# 規約は `~/.claude/rules/coding-principles.md`。Unity ではないので `unity-csharp.md` の Unity 固有項目（Prefab / GetComponent / MonoBehaviour）は対象外。命名・ブレース・`None = 0` enum・省略形禁止は適用する
- 1 ファイル 1 型。ファイルスコープ namespace は使わない（`Arpeggio.Core` を Unity ランタイムへ流用するため）
- 合成のホットパス（`Render(Span<float>)`）ではアロケーション・LINQ 禁止。`// perf:` で意図を残す
- 先行例は `../colors`（同じ Core + CLI + MCP + Avalonia 構成）。迷ったらそちらの流儀に合わせる
