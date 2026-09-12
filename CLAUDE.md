# CLAUDE.md — Arpeggio

ファミコン・ゲームボーイ・スーファミ風の曲と効果音を AI と人が作るチップチューン DAW。`Arpeggio.Core`（.NET 10 ライブラリ）＋ CLI `arpeggio` ＋ MCP サーバー `arpeggio-mcp` ＋ Avalonia DAW `arpeggio-daw`。

## Codex の使い方（2026-09-08 ユーザー指示）

- **実装ランは当分すべて `-p top`（gpt-6-astra）＋ `-c model_reasoning_effort=high`**。`~/.claude/rules/ai-operations.md` の
  「top を選ぶ基準」より優先する。std へ落とさない。`docs/design-m3.md` の分割表に「既定 std」と書いてあるものも top で投げる
- **仕様の判断も Codex に任せてよい**。設計ラン（実装を書かせず設計書だけ書かせる）→ メインがレビュー → 実装ランの順で回す
- **「何を作るか」も Codex に提案させる**。実装ランの指示書に `## 提案（任意・このランでは実装しない）` 節を入れ、
  実装記録の末尾に気づきを 3 件まで書かせる。メインが 1 件ずつ採否を判定し、採用は次のランへ回す。
  **提案した Codex にそのまま実装させない**。却下にも理由を書く。提案だけの独立ランは作らない
- 起動は `karakuri/tools/codex_run.sh` 経由。並行するときは `git worktree` で作業ディレクトリを分ける

## フォルダ構成

`~/.claude/rules/coding-principles.md` の §2.5 に従う（**原則 8・上限 10**、値型だけなら 12）。
2026-09-12 に下記の木へ移行済み（PR #40〜#42）。新規ファイルは最初からこの木に沿って置く。

```
src/Arpeggio.Core/
  Document/       ソング・トラック・ノート・シリアライズ・検証
    Chips/        ChipKind・ChannelKind・ChipLayout・ChipReference
  Instruments/    音色の基底とマクロ
    Nes/ GameBoy/ Snes/    チップ別の音色
  Sequencing/     時間・tick・イベント展開
  Synthesis/      合成器の基底・ピッチ・マクロ実行
    Nes/ GameBoy/ Snes/    チップ別の合成器とミキサー
  Render/         レンダラー・WAV 読み書き
  Analysis/       FFT・音声解析・レポート
  Sfx/            効果音（直下は入口の型のみ）
    Parameters/   パラメータ型・Catalog・Validator・patch
    Curves/       包絡・ピッチ・デューティ・ノイズの曲線生成
    Compile/      Song 組み立て（チップ別）
    Presets/      プリセットとランダム化
    Storage/      保存形式・hash・同期状態
  Brief/          作曲指示書（Composition Brief）。Song 非依存の独立文書
  Session/        編集セッション・履歴・バッチ操作
src/Arpeggio.Formats/
  Export/         共通の診断・制御列・レジスタ列
    Control/      制御イベント・タイムライン
    Nes/ GameBoy/     チップ別のレジスタ変換
    Nsf/ Vgm/         ファイル書き出し
  Midi/           SMF 読み込み（直下は生の読み込み）
    Import/       取り込みの入口・音色マップ
      Tempo/      テンポ変換
      Voice/      声割り当て
src/Arpeggio.Daw/
  Presenters/     画面ごとに分ける（直下は MainWindowPresenter 等の入口のみ）
    PianoRoll/    Selection/ に選択・クリップボード
    Instrument/ Sfx/ Transport/ Export/ Midi/ Analysis/ Brief/
  Views/          同上
  Audio/ Editing/ Themes/ Watch/ Platform/
```

- **テストのフォルダは実装と 1 対 1 で対応させる**（`tests/Arpeggio.Core.Tests/Sfx/Curves/` のように）
- **Codex への実装指示では、新規ファイルの置き場所をこの木で明示する**

- 設計の正本: `docs/design.md`。仕様変更はまずここを直す
- ビルド: `dotnet build Arpeggio.slnx -nologo -v q -clp:ErrorsOnly`
- テスト: `dotnet test Arpeggio.slnx -nologo -v q`
- C# 規約は `~/.claude/rules/coding-principles.md`。Unity ではないので `unity-csharp.md` の Unity 固有項目（Prefab / GetComponent / MonoBehaviour）は対象外。命名・ブレース・`None = 0` enum・省略形禁止は適用する
- 1 ファイル 1 型。ファイルスコープ namespace は使わない（`Arpeggio.Core` を Unity ランタイムへ流用するため）
- 合成のホットパス（`Render(Span<float>)`）ではアロケーション・LINQ 禁止。`// perf:` で意図を残す
- 先行例は `../Colors`（同じ Core + CLI + MCP + Avalonia 構成）。迷ったらそちらの流儀に合わせる

## このリポジトリ固有の罠

- **View に `Presenter` という名前のプロパティを作らない。** Avalonia の `ContentControl.Presenter` と衝突する（3 回踏んだ）
- **`TextBox.Watermark` は obsolete。** `PlaceholderText` を使う
- **xunit のアナライザは警告＝エラーになる**（`TreatWarningsAsErrors`）。`Assert.Single(x.Where(f))` ではなく `Assert.Single(x, f)`
- **`option.Errors` は `IEnumerable`。** `.Count` はメソッドなので `Count()` と書く
- **`.app` は Release ビルドなので `AVALON` が入らない。** Avalon で操作・観測するときは Debug 実行ファイルを使う（メールボックス方式なのでフォーカス不要）
