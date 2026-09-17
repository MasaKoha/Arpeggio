#!/usr/bin/env python3
"""DAW の「この内容で直してもらう」依頼ファイルを見張り、Codex（gpt-6-astra / high）へ修正を委ねる。

使い方:
    python3 tools/fix_daemon.py <song.arpeggio.json> [--poll-seconds N] [--repo-dir PATH]

<song>.fix-request.txt の mtime が変化したら、内容を読み取って codex_run.sh 経由で Codex を実行し、
既存の arpeggio CLI（note/apply/analyze 等）だけで曲を直させる。DAW 側は曲ファイルの外部変更検知
（DawDocument.HasExternalChange）が既にあるため、この daemon はファイルを書き換えるだけでよい。
DAW を開き直す・再読み込みするのは人間が「R」キーで行う（誤って未保存編集を上書きしないため）。

**なぜ claude -p ではなく Codex か**: このマシンではヘッドレスの `claude -p` が従量課金防止のため
グローバル設定でブロックされている。このリポジトリの実装委譲は元々 Codex（`-p top` ＋
`model_reasoning_effort=high`）が既定であり、`karakuri/tools/codex_run.sh` がハング・使用量上限を
検知して安全に起動できる。

**song_path は repo_dir の配下にあること。** Codex のサンドボックス（workspace-write）は
`-C <repo_dir>` の外への書き込みを許可しない。

このスクリプト自体は Core/CLI の一部ではなく、リポジトリを触らない運用ツール。
"""

from __future__ import annotations

import argparse
import subprocess
import sys
import os
import tempfile
import time
from pathlib import Path

POLL_SECONDS_DEFAULT = 3.0

# Codex 起動ラッパーの場所は環境ごとに違う。ARPEGGIO_CODEX_RUN_SH で上書きできるようにし、
# 既定はこのリポジトリと同じ親ディレクトリに karakuri を置いた構成を想定する。
CODEX_RUN_SH = Path(
    os.environ.get(
        "ARPEGGIO_CODEX_RUN_SH",
        Path(__file__).resolve().parents[3] / "karakuri" / "tools" / "codex_run.sh",
    )
)

INSTRUCTION_TEMPLATE = """# Arpeggio 修正依頼: {song_name}

プロファイル: top / 理由: リポジトリ規定 + 創造的判断を伴う CLI 操作（fix_daemon.py からの自動委譲）

作業ディレクトリ: `{repo_dir}`（ここ以外を触らない）

## このランでやること

既存の Song `{song_path}` を、以下のユーザーの依頼どおりに **既存の arpeggio CLI だけ**で直す。
**新しい C# コードは書かない。ソースコード（`src/` `docs/`）は変更しない。git コマンドは使わない。
依頼に関係しない変更はしない。**

**このランは非対話で実行される。確認を求めても誰も答えられない。** 判断に迷う点があれば、
最も妥当な解釈を自分で選んで進め、その判断理由を完了報告に書く。確認待ちで未適用のまま止まらないこと。

## ユーザーの依頼（そのまま）

{request_text}

## 使える CLI（詳細は docs/cli-reference.md）

- `dotnet {arpeggio_dll} info {song_path} --json`
- `dotnet {arpeggio_dll} show {song_path} --from-tick N --to-tick M`
- `dotnet {arpeggio_dll} note add/remove/move/resize {song_path} ...`
- `dotnet {arpeggio_dll} apply {song_path} --operations ops.json`（AddNote/RemoveNote/MoveNote/ResizeNote/UpdateNote/AddInstrument/UpdateInstrument/SetTempo の配列。1操作でも不正なら全体を適用しない）
- `dotnet {arpeggio_dll} instrument set {song_path} --id N ...`
- `dotnet {arpeggio_dll} analyze {song_path} [--json]`（無音・クリップ・支配周波数・警告を数値で返す）
- `dotnet {arpeggio_dll} undo {song_path}` / `dotnet {arpeggio_dll} redo {song_path}`

## 手順

1. `info`/`show`/`analyze` で現状を把握する
2. 依頼の内容に関係する範囲だけを直す
3. `analyze` で新たなクリップ・無音悪化が無いか確認する
4. 完了したら、変更前後の該当区間の `show` 出力の要点と、依頼にどう応えたかを実装記録相当のメモへ書く

## 動かさない変数・触らない箇所

| 項目 | 値 |
|---|---|
| `src/` `docs/` 配下のソースコード・設計書 | 変更しない |
| `{song_path}` 以外の**ユーザーのファイル** | 変更しない |
| `{song_path}.history/` `{song_path}.*.tmp` 等、arpeggio CLI が編集の都度自動更新する側車ファイル | **CLI が書くのは正常な挙動なので許可する。確認を求めず進めてよい** |
| git コマンド | 使わない |

## 完了時に報告すること

- 修正前後の該当区間の `show` 出力の要点
- 最終的な `analyze` の結果（クリップ・無音割合）
- 依頼のうち反映できなかった項目があれば理由
"""


def read_request(request_path: Path) -> str:
    text = request_path.read_text(encoding="utf-8").strip()
    # 「[yyyy-MM-dd HH:mm] 本文」の先頭タイムスタンプは無くても構わないが、あれば読みやすさのため残す。
    return text


def run_codex_fix(song_path: Path, request_text: str, repo_dir: Path, arpeggio_dll: Path) -> None:
    instruction = INSTRUCTION_TEMPLATE.format(
        song_name=song_path.name,
        song_path=song_path,
        repo_dir=repo_dir,
        arpeggio_dll=arpeggio_dll,
        request_text=request_text,
    )
    instruction_path = Path(tempfile.mkstemp(prefix="arpeggio-fix-", suffix=".md")[1])
    instruction_path.write_text(instruction, encoding="utf-8")
    command = [
        str(CODEX_RUN_SH),
        str(instruction_path),
        "-p",
        "top",
        "-c",
        "model_reasoning_effort=high",
        "--sandbox",
        "workspace-write",
        "-C",
        str(repo_dir),
    ]
    print(f"[fix_daemon] Codex を起動します: {song_path}")
    result = subprocess.run(command)
    if result.returncode != 0:
        print(f"[fix_daemon] codex_run.sh が終了コード {result.returncode} で終了しました"
              "（42=ハング 43=使用量上限 44=タイムアウト）", file=sys.stderr)
    else:
        print(f"[fix_daemon] 修正が完了しました: {song_path}")


def watch(song_path: Path, repo_dir: Path, arpeggio_dll: Path, poll_seconds: float) -> None:
    request_path = Path(str(song_path) + ".fix-request.txt")
    last_mtime = request_path.stat().st_mtime if request_path.exists() else None
    print(f"[fix_daemon] 監視中: {request_path}")
    while True:
        time.sleep(poll_seconds)
        if not request_path.exists():
            continue
        mtime = request_path.stat().st_mtime
        if mtime == last_mtime:
            continue
        last_mtime = mtime
        request_text = read_request(request_path)
        if not request_text:
            continue
        run_codex_fix(song_path, request_text, repo_dir, arpeggio_dll)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("song_path", type=Path, help="監視する .arpeggio.json のパス")
    parser.add_argument("--poll-seconds", type=float, default=POLL_SECONDS_DEFAULT)
    parser.add_argument("--repo-dir", type=Path, default=Path(__file__).resolve().parent.parent,
                         help="Codex サンドボックスの作業ディレクトリ（既定: このスクリプトが属するリポジトリ）")
    arguments = parser.parse_args()

    song_path = arguments.song_path.resolve()
    if not song_path.exists():
        parser.error(f"曲ファイルが見つかりません: {song_path}")
    repo_dir = arguments.repo_dir.resolve()
    if repo_dir not in song_path.parents:
        parser.error(f"曲ファイルは --repo-dir の配下にある必要があります"
                     f"（Codex サンドボックスがその外への書き込みを許可しないため）: {song_path} not under {repo_dir}")
    arpeggio_dll = repo_dir / "src" / "Arpeggio.Cli" / "bin" / "Debug" / "net10.0" / "arpeggio.dll"
    if not arpeggio_dll.exists():
        parser.error(f"arpeggio.dll が見つかりません（先に dotnet build してください）: {arpeggio_dll}")
    if not CODEX_RUN_SH.exists():
        parser.error(f"codex_run.sh が見つかりません: {CODEX_RUN_SH}")

    try:
        watch(song_path, repo_dir, arpeggio_dll, arguments.poll_seconds)
    except KeyboardInterrupt:
        print("\n[fix_daemon] 終了します")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
