#!/usr/bin/env python3
"""NdMcp/main.cs を組み立てる。

Next Design のスクリプト拡張はエントリポイントが1ファイルに限られるため、
  src/header.cs（ファイルヘッダと using）
  AgentReview/main.cs の Part 0 / 4 / 7 / 8（共通ヘルパ・Markdown 出力・PlantUML 出力）
  src/server.cs（HTTP サーバー本体）
  PlantUmlTool/src/61-class-sync-runtime.cs, 60-class-sync.cs（クラス図の PlantUML 同期。転記元）
  src/classsync.cs（同期 API の窓口と ClassExperiment の代替）
をこの順に連結して main.cs を生成する。転記元は AgentReview 側で実機検証済みのコードなので、
エクスポータの修正は AgentReview で行い、本スクリプトで再生成する。

使い方:
  python NdMcp/tools/build_main.py            # 生成
  python NdMcp/tools/build_main.py --check    # 生成結果が main.cs と一致するか検査（差分があれば終了コード 1）
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REPO = ROOT.parent
AGENT_REVIEW = REPO / "AgentReview" / "main.cs"
CLASS_SYNC = [REPO / "PlantUmlTool" / "src" / "61-class-sync-runtime.cs", REPO / "PlantUmlTool" / "src" / "60-class-sync.cs"]
OUT = ROOT / "main.cs"

# 転記する Part（ヘッダ行の「Part N /」で切り出す）。順序は転記順
PARTS = [0, 4, 7, 8]
PART_HEADER = re.compile(r"^//  Part (\d+) /")
SEPARATOR = "// " + "=" * 60


def split_parts(text: str) -> dict:
    """AgentReview/main.cs を Part 番号ごとに分ける。Part 7 のように同じ番号が複数ある場合は連結する。"""
    lines = text.splitlines()
    starts = []
    for i, line in enumerate(lines):
        m = PART_HEADER.match(line)
        if not m:
            continue
        # ヘッダ直前の区切り線を探す（Part 8 のように空行が挟まることがある）
        for back in (1, 2):
            if i - back >= 0 and lines[i - back].startswith(SEPARATOR):
                starts.append((i - back, int(m.group(1))))
                break
    parts: dict[int, list[str]] = {}
    for idx, (start, number) in enumerate(starts):
        end = starts[idx + 1][0] if idx + 1 < len(starts) else len(lines)
        parts.setdefault(number, []).extend(lines[start:end])
    return parts


def build() -> str:
    header = (ROOT / "src" / "header.cs").read_text(encoding="utf-8")
    server = (ROOT / "src" / "server.cs").read_text(encoding="utf-8")
    agent_source = AGENT_REVIEW.read_text(encoding="utf-8")
    parts = split_parts(agent_source)
    missing = [n for n in PARTS if n not in parts]
    if missing:
        raise SystemExit(f"AgentReview/main.cs に Part {missing} が見つからない")

    chunks = [header.rstrip("\n"), ""]
    # 共通エクスポータが参照する比較レコードと表示用整形だけを転記する。
    # レビュー開始やファイル固定処理はMCPサーバーには持ち込まない。
    start = agent_source.index("public sealed class ChangeRecord")
    end = agent_source.index("public static class ChangeDiff", start)
    chunks.append(agent_source[start:end].strip())
    start = agent_source.index("    public static string Cell(string value)")
    end = agent_source.index("\n    }", start) + len("\n    }")
    chunks.append("public static class ReviewSnapshot\n{\n" + agent_source[start:end] + "\n}")
    chunks.append(SEPARATOR)
    chunks.append("//  ここから AgentReview/main.cs の Part 0 / 4 / 7 / 8 の転記（tools/build_main.py が生成）")
    chunks.append(SEPARATOR)
    for n in PARTS:
        chunks.append("")
        chunks.append("\n".join(parts[n]).rstrip("\n"))
    # AgentReview の出力メソッドもそのまま使い、索引やログの挙動を揃える。
    start = agent_source.index("private void WriteDesignArtifacts(")
    end = agent_source.index("// レビューセッションを作らず", start)
    writer = agent_source[start:end].strip().replace(
        "private void WriteDesignArtifacts(", "public static void Write(", 1
    )
    chunks.append("\npublic static class DesignArtifactWriter\n{\n" + writer + "\n}")
    chunks.append("")
    chunks.append(server.rstrip("\n"))
    # クラス図同期は PlantUmlTool/src の実機検証済みコードをそのまま転記する。修正は PlantUmlTool 側で行う。
    chunks.append("")
    chunks.append(SEPARATOR)
    chunks.append("//  ここから PlantUmlTool/src の転記（tools/build_main.py が生成）")
    chunks.append(SEPARATOR)
    for source in CLASS_SYNC:
        chunks.append("")
        chunks.append("// BEGIN TRANSCRIBED " + source.name)
        chunks.append(source.read_text(encoding="utf-8-sig").rstrip("\n"))
        chunks.append("// END TRANSCRIBED " + source.name)
    chunks.append("")
    chunks.append((ROOT / "src" / "classsync.cs").read_text(encoding="utf-8").rstrip("\n"))
    chunks.append("")
    return "\n".join(chunks)


def main() -> int:
    content = build()
    if "--check" in sys.argv:
        current = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if current != content:
            print("main.cs が src/ と転記元から生成した内容と一致しない。python NdMcp/tools/build_main.py で再生成する")
            return 1
        print("main.cs は最新")
        return 0
    OUT.write_text(content, encoding="utf-8", newline="\n")
    print(f"生成: {OUT} ({content.count(chr(10))} 行)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
