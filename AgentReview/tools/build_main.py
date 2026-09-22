#!/usr/bin/env python3
"""AgentReview/main.cs を組み立てる。

  src/00-agentreview.cs                    この拡張固有の Part 0〜6
  PlantUmlTool/src/shims/metamap.cs        MetaMap シム（出力エンジンが使う ModelOf だけ）
  PlantUmlTool/src/10-sequence-export.cs   シーケンス図の出力
  PlantUmlTool/src/40-class-export.cs      クラス図の出力
  PlantUmlTool/src/50-state-export.cs      状態遷移図の出力
  PlantUmlTool/src/60-class-sync.cs        クラス図の PlantUML 文書と書出し（出力が使う）
  PlantUmlTool/src/61-class-snapshot.cs    クラス図の読取り（出力が使う）
をこの順に連結する。PlantUML 出力の修正は PlantUmlTool 側で行い、ここでは再生成するだけ。

使い方:
  python AgentReview/tools/build_main.py            # 生成
  python AgentReview/tools/build_main.py --check    # 生成結果が main.cs と一致するか検査
"""
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REPO = ROOT.parent
PUT = REPO / "PlantUmlTool" / "src"
OUT = ROOT / "main.cs"
CRLF = b"\r\n"

NOTE = """// ============================================================
//  Part 7 / PlantUML 出力エンジン（PlantUmlTool/src からの転記。tools/build_main.py が生成）
//
//    転記元: PlantUmlTool/src の 10 / 40 / 50 / 60 / 61。修正はまず PlantUmlTool 側で
//    実機検証してから、このスクリプトで再生成する。
//    差分: OutputPane は AgentReview 側の同シグネチャ実装を使う（05 は転記しない）。
//          MetaMap は ModelOf のみ使用するため shims/metamap.cs で代替。
//          ClassProbe（45）と書き戻し（62 / 63）は転記しない。
//    ExportRunner / ClassExportRunner / StateExportRunner のダイアログを使う
//    メソッドは AgentReview のリボンからは呼ばれない（判定ヘルパのみ使用）。
// ============================================================
"""

TRANSCRIBED = [
    PUT / "shims" / "metamap.cs",
    PUT / "10-sequence-export.cs",
    PUT / "40-class-export.cs",
    PUT / "50-state-export.cs",
    PUT / "60-class-sync.cs",
    PUT / "61-class-snapshot.cs",
]


def crlf(data: bytes) -> bytes:
    data = data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    return data if data.endswith(CRLF) else data + CRLF


def build() -> bytes:
    chunks = [(ROOT / "src" / "00-agentreview.cs").read_bytes(), CRLF, crlf(NOTE.encode("utf-8")), CRLF]
    for path in TRANSCRIBED:
        chunks.append(crlf(path.read_bytes().lstrip(b"\xef\xbb\xbf")))
        chunks.append(CRLF)
    return b"".join(chunks)


def main() -> int:
    content = build()
    if "--check" in sys.argv:
        current = OUT.read_bytes() if OUT.exists() else b""
        if current != content:
            print("main.cs が src/ と転記元から生成した内容と一致しない。python AgentReview/tools/build_main.py で再生成する")
            return 1
        print("main.cs は最新")
        return 0
    OUT.write_bytes(content)
    print("生成: %s (%d 行)" % (OUT, content.count(b"\n")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
