"""Compile PlantUmlTool/main.cs against the official V3.1.3 SDK and the .NET 6 references.

  python PlantUmlTool/tests/compile_sdk.py --sdk-root work/sequence-api-research

Top-level command handlers (public void X(ICommandContext, ...)) are wrapped in one class so the
script compiles as a library. This checks types and signatures only; it is not a real-machine test.
"""
import argparse
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def wrap_handlers(source: str) -> str:
    out, inside, depth = [], False, 0
    for line in source.split("\n"):
        if not inside and re.match(r"^public void \w+\(ICommandContext", line):
            inside, depth = True, 0
            out.append("public partial class Handlers {")
        out.append(line)
        if inside:
            depth += line.count("{") - line.count("}")
            if depth == 0 and "{" in line or depth == 0 and line.strip() == "}":
                # The handler's braces are balanced (single-line body or closing line).
                out.append("}")
                inside = False
    return "\n".join(out)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--sdk-root", type=Path, required=True)
    args = parser.parse_args()
    sdk = args.sdk_root.resolve()
    refs = list((sdk / "net6-ref/ref/net6.0").glob("*.dll"))
    if not refs:
        raise SystemExit("obtain Microsoft.NETCore.App.Ref 6.0.36 in sdk-root/net6-ref first")
    refs += [sdk / "core/lib/netstandard2.0/NextDesign.Core.dll", sdk / "desktop/lib/net6.0-windows7.0/NextDesign.Desktop.dll"]
    work = Path(tempfile.mkdtemp(prefix="plantumltool-", dir=ROOT.parent / "work"))
    production = work / "Production.cs"
    production.write_text(wrap_handlers((ROOT / "main.cs").read_text(encoding="utf-8")), encoding="utf-8-sig")
    commands = ["/nologo", "/target:library", "/warnaserror+", "/out:" + str(work / "PlantUmlTool.dll"), str(production)]
    commands += ["/r:" + str(p) for p in refs]
    rsp = work / "compile.rsp"
    rsp.write_text("\n".join('"' + v + '"' for v in commands))
    sdks = subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()
    version, location = sdks[-1].split(" [", 1)
    csc = Path(location.rstrip("]")) / version / "Roslyn/bincore/csc.dll"
    result = subprocess.run(["dotnet", str(csc), "@" + str(rsp)])
    if result.returncode != 0:
        return result.returncode
    print("PASS: PlantUmlTool/main.cs compiled against official V3.1.3 SDK and .NET 6 references")
    return 0


if __name__ == "__main__":
    sys.exit(main())
