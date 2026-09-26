"""csproj の Compile 項目を記載順に列挙し、連結したソースを返す。

エクステンションは DLL 方式で、ソースは csproj に列挙したファイルそのもの。テストは
これまで生成した main.cs から目印で部分を切り出していたので、同じ並びの連結結果を
ここで作って渡す。ビルドに使うファイルとテストが読むファイルは常に一致する。

  python Tools/csproj_sources.py PlantUmlTool          # 列挙したファイルを表示
  python Tools/csproj_sources.py PlantUmlTool --joined  # 連結結果を表示

Include はファイルを1つずつ書く前提（ワイルドカードは使わない）。存在しないファイルは
例外にする。
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def project_file(project_dir):
    project_dir = Path(project_dir)
    if not project_dir.is_absolute():
        project_dir = ROOT / project_dir
    found = sorted(project_dir.glob("*.csproj"))
    if len(found) != 1:
        raise SystemExit("{} に .csproj が1つだけあること（{} 件）".format(project_dir, len(found)))
    return found[0]


def sources(project_dir):
    """Compile Include のファイルを記載順に返す。"""
    csproj = project_file(project_dir)
    result = []
    for item in ET.parse(csproj).getroot().iter("Compile"):
        include = item.get("Include")
        if not include:
            continue
        path = (csproj.parent / include.replace("\\", "/")).resolve()
        if not path.is_file():
            raise SystemExit("{} の Compile が指す {} が無い".format(csproj.name, include))
        result.append(path)
    return result


def joined(project_dir):
    """列挙したファイルを記載順に改行でつないだ文字列を返す。"""
    return "\n".join(p.read_text(encoding="utf-8-sig") for p in sources(project_dir))


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    if "--joined" in sys.argv[2:]:
        sys.stdout.reconfigure(encoding="utf-8")
        print(joined(sys.argv[1]))
    else:
        for p in sources(sys.argv[1]):
            print(p.relative_to(ROOT))
