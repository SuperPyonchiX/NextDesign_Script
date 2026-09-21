#!/usr/bin/env python3
"""PlantUmlTool/main.cs を src/ から組み立てる。

Next Design のスクリプト拡張はエントリポイントが 1 ファイルに限られるため、
src/ の *.cs をファイル名順に連結して main.cs を生成する。改行や文字コードは
src の内容をそのまま（バイト単位で）つなぐ。編集は src/ 側で行い、main.cs は直接編集しない。

使い方:
  python PlantUmlTool/tools/build_main.py            # 生成
  python PlantUmlTool/tools/build_main.py --check    # main.cs が src/ と一致するか検査（差分があれば終了コード 1）
"""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
OUT = ROOT / "main.cs"


def sources() -> list:
    files = sorted(p for p in SRC.glob("*.cs") if p.is_file())
    if not files:
        raise SystemExit(f"{SRC} に .cs がない")
    return files


def build() -> bytes:
    chunks = []
    for path in sources():
        data = path.read_bytes()
        if data and not data.endswith(b"\n"):
            data += b"\r\n"
        chunks.append(data)
    return b"".join(chunks)


def main() -> int:
    content = build()
    if "--check" in sys.argv:
        current = OUT.read_bytes() if OUT.exists() else b""
        if current != content:
            print("main.cs が src/ から生成した内容と一致しない。python PlantUmlTool/tools/build_main.py で再生成する")
            return 1
        print("main.cs は最新")
        return 0
    OUT.write_bytes(content)
    line_count = content.count(b"\n")
    print(f"生成: {OUT} ({line_count} 行, {len(sources())} ファイル)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
