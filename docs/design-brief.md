# Arpeggio 作曲指示書（Composition Brief）設計書

AI（Codex 等）に曲を作らせるための構造化した指示書を作る機能を設計する。本書は作曲指示書機能の追加仕様の正本とする。[design.md](design.md) の短いソング・保存・履歴の規則を継承するが、**Song の合成・チップ仕様には一切踏み込まない**（[design-sfx.md](design-sfx.md) と異なり、この機能は Song を生成しない。人／AI が読む指示文書を1つ作るだけ）。

## 決定事項

| 項目 | 決定 | 根拠 |
|---|---|---|
| 対象読者 | **AI に曲を作らせる指示書**。人間の作曲家向け仕様書ではない | ユーザー指示（2026-09-12）。CLI/MCP から文字列として取り出し、AI へのプロンプトにそのまま貼れることを最優先にする |
| 作成方法 | **空のテンプレートへ手入力**。既存 Song からの自動生成は行わない | ユーザー指示。今回のランでは自動生成ロジックを作らない（将来の拡張候補として設計との差に残す） |
| データ構造 | Song とは無関係の独立文書。`ChipKind`・BPM・拍子・構成・声の役割・雰囲気・参考・制約・自由メモを持つレコード | 作曲指示は「これから作る曲」の仕様であり、既存 Song のフィールドを流用すると無関係な必須項目（Tracks 等）を抱え込む |
| 保存形式 | 独立 JSON ファイル（`*.brief.json`）。`version: 1` を持つ | Song の `.arpeggio.json` と衝突しないよう別拡張子にする。将来のスキーマ変更に備え version を持つ（Song の規約に合わせる） |
| 出力形態 | **JSON**（構造化データ）と**プレーンテキスト**（AI プロンプト用に整形した1つの文字列）の両方を持つ | UI 上で編集した内容を CLI/MCP から文字列として取り出せるようにするユーザー要求に応える |
| CLI/MCP | SFX と同じ構成に倣う: `brief create` / `brief tweak` / `brief show`（JSON） / `brief text`（プレーンテキスト） | 既存の SFX CLI/MCP パターン（`SfxParameterOptions` 相当の個別オプション、`--json`）を踏襲し、一貫した操作感にする |
| UI | DAW に新規タブ「作曲指示書」を追加する。既存 SFX タブと同じ並びに配置し、フォームは全項目が空でも保存できる | ユーザー指示（同じ UI 上に載せる）。既存の `SfxCreationView` のレイアウト規約（`Themes/ArpeggioTheme.axaml` トークン使用）を踏襲する |
| コピー操作 | UI に「指示書テキストをコピー」ボタンを1つ置く。クリップボードへ `brief text` 相当の文字列をコピーする | 文字列で渡す運用を UI からもワンクリックで行えるようにする |
| Song との連携 | このランでは**連携しない**。指示書から Song を自動生成する機能・Song から指示書を書き戻す機能は作らない | スコープを固定し、往復変換の仕様判断（構成メモをどう Song の長さへ変換するか等）を先送りする。将来の拡張候補として設計との差に残す |

## 用語

- **作曲指示書（Composition Brief）**: この機能が扱う独立文書。1ファイルが1曲分の指示に対応する
- **指示書テキスト（Brief Text）**: 作曲指示書の内容を人間／AI が読める1つのプレーンテキストへ整形したもの

## データ構造

`Arpeggio.Core` に新設する `Brief/` フォルダ（`~/.claude/rules/coding-principles.md` §2.5 のフォルダ構成規約に従い最初からサブフォルダを切る）。

```
src/Arpeggio.Core/Brief/
  CompositionBrief.cs         本体レコード（下記フィールド）
  CompositionBriefFile.cs     保存・読み込み（JSON シリアライズ、原子的書き込み）
  CompositionBriefTextRenderer.cs   指示書テキストへの整形
  CompositionBriefValidator.cs      入力検証（文字数上限・BPM範囲等）
```

### `CompositionBrief`（record、すべて optional。`Title` 以外は null／空を許容する）

| フィールド | 型 | 説明 |
|---|---|---|
| `Title` | `string` | 曲名・仮題。必須（空文字は不可） |
| `Chip` | `ChipKind?` | 対象チップ。null は「AI に一任」を意味する |
| `TempoBpm` | `int?` | 目安テンポ。null は「AI に一任」 |
| `Mood` | `string?` | 雰囲気・ジャンル・キーワードの自由記述（複数行可） |
| `Structure` | `string?` | 構成メモ（イントロ・メイン・アウトロ等）の自由記述 |
| `Instrumentation` | `string?` | 声ごとの役割・使い方の自由記述 |
| `References` | `string?` | 参考曲・スタイルの自由記述 |
| `Constraints` | `string?` | 制約（ループ長・避けたい表現等）の自由記述 |
| `Notes` | `string?` | その他自由記述 |

自由記述フィールドはすべて長さ上限 2000 文字（`CompositionBriefValidator` で検証。超過は `InvalidParameter`）。`Title` は 200 文字上限。

### 保存形式（`*.brief.json`）

```json
{
  "version": 1,
  "title": "廃墟の朝",
  "chip": "Nes",
  "tempoBpm": 96,
  "mood": "寂しいが希望が残る。アンビエント寄り",
  "structure": "イントロ8小節→メイン16小節×2→アウトロ4小節でループ",
  "instrumentation": "Pulse1: 主旋律 / Pulse2: ハーモニー / Triangle: ベース / Noise: 使わない",
  "references": "Undertale の寂寥感、ただしテンポはもう少し速く",
  "constraints": "ループ後半で唐突に切れない。DPCMは使わない",
  "notes": ""
}
```

未指定フィールドは `null`（自由記述は空文字と null を区別しない。読み込み時に空文字へ正規化する）。

### 指示書テキストへの整形（`CompositionBriefTextRenderer`）

日本語の見出し付きプレーンテキストへ整形する。値が無い項目は行ごと省略する（空欄を見せない）。例:

```
# 作曲指示書: 廃墟の朝

チップ: NES
テンポ目安: 96 BPM

## 雰囲気
寂しいが希望が残る。アンビエント寄り

## 構成
イントロ8小節→メイン16小節×2→アウトロ4小節でループ

## 声の役割
Pulse1: 主旋律 / Pulse2: ハーモニー / Triangle: ベース / Noise: 使わない

## 参考
Undertale の寂寥感、ただしテンポはもう少し速く

## 制約
ループ後半で唐突に切れない。DPCMは使わない
```

`Chip` が null なら「チップ: 指定なし（AIに一任）」、`TempoBpm` が null なら「テンポ目安: 指定なし（AIに一任）」と明示する（省略すると AI が「指定が無いから守らなくてよい」と読める曖昧さを避ける）。

## CLI (`arpeggio brief`)

既存の `arpeggio sfx` コマンド群（`src/Arpeggio.Cli/Sfx/`）と同じ構成に倣う。新設 `src/Arpeggio.Cli/Brief/`。

| コマンド | 内容 |
|---|---|
| `arpeggio brief create <path> [--title T] [--chip nes\|gameboy\|snes]` | 新規作成。`--title` 省略時は `"無題"` |
| `arpeggio brief tweak <path> --title T \| --chip C \| --tempo N \| --mood M \| --structure S \| --instrumentation I \| --references R \| --constraints C2 \| --notes N2 \| --clear-chip \| --clear-tempo` | 部分編集。1回の呼び出しで複数オプション指定可（SFX の `tweak` と同様、各オプションは `ExactlyOne` の値オプション）。`--clear-chip`/`--clear-tempo` は該当フィールドを null に戻す（フラグ、値なし） |
| `arpeggio brief show <path> [--json]` | 現在値を表示。`--json` 無しは整形テキスト、有りは JSON |
| `arpeggio brief text <path>` | 指示書テキストだけを stdout へ出力する（`show` と分ける理由: `text` はパイプで直接 AI へ渡す用途に特化し、他の出力を混ぜない） |

エラー処理は SFX の `CliSfxExecution` と同じ規約（exitCode 0/1/2/3、`--json` 時は `{operation, error, exitCode, code, parameterPath}`）を流用する。**SFX-E12 で見つかった System.CommandLine のオプション貪欲消費バグ対策（`CliArgumentGuard.RejectFlagLikeToken`）を新設オプションにも適用する**（値未指定のオプションが後続の `--json` 等を飲み込む事故を防ぐ）。

## MCP (`arpeggio-mcp`)

`src/Arpeggio.Mcp/Brief/` を新設し、CLI と同じ4操作をツールとして公開する（`brief_create` / `brief_tweak` / `brief_show` / `brief_text`）。既存の SFX MCP ツール（`src/Arpeggio.Mcp/Sfx/`）と同じ実行境界（`McpSfxExecution` 相当）を流用し、直列実行・現セッション維持を保証する。

## DAW UI

既存の SFX タブ（`Views/Sfx/SfxCreationView`）と同じ並びに新規タブ「作曲指示書」を追加する。新設 `src/Arpeggio.Daw/Presenters/Brief/` `src/Arpeggio.Daw/Views/Brief/`。

- タイトル・チップ選択（ドロップダウン、「指定なし」を含む）・テンポ（数値入力、空欄可）を上部に配置
- 雰囲気・構成・声の役割・参考・制約・メモの6項目は複数行テキストボックス
- 下部に「保存」「開く」「指示書テキストをコピー」の3ボタン
- 「指示書テキストをコピー」は `CompositionBriefTextRenderer` の出力をクリップボードへコピーする（Avalonia の `Clipboard` API）。コピー後は一時的に「コピーしました」を表示する（既存の保存成功表示と同じ一時メッセージパターンに揃える）
- 保存は 1 操作 1 履歴の対象にしない（SFX の Undo/Redo とは無関係の単純な文書。編集の都度バリデーションのみ行い、保存ボタンで書き込む）
- 空のフォームでも保存できる（`Title` だけ既定値 `"無題"` を埋める）

## テスト方針

- `CompositionBriefFile`: 保存・読み込みの往復一致、不正 JSON・欠損 version の拒否
- `CompositionBriefValidator`: 文字数上限超過・空 Title の拒否
- `CompositionBriefTextRenderer`: 値の有無による行の出現・省略、chip/tempo 未指定時の明示文言
- CLI: `create`/`tweak`/`show`/`text` の正常系・異常系（SFX の `SfxCommandFailureTests` と同型）
- MCP: 4ツールの正常系・直列実行
- DAW: Presenter の空値保存・複数行テキスト保持・クリップボードコピー（Avalonia を起動しない Presenter テスト）

## 実装ランの分割

| ラン | 範囲 |
|---|---|
| Brief-A | Core（`CompositionBrief` 一式）+ CLI + MCP |
| Brief-B | DAW（Presenter + View + タブ追加） |

## 設計との差（実装時に記録する）

このランでは仕様変更を行わない。矛盾や不足を実装時に見つけた場合はここに追記せず、各ランの実装記録（`docs/implementation.md`）の「設計との差」節に書く。

## 未決事項

- 指示書から Song を自動生成する機能（将来の拡張候補。今回は対象外）
- Song 側に指示書への参照を持たせるか（今回は無関係な独立文書とする）
- 指示書テキストの言語（今回は日本語固定。多言語対応は対象外）
