"""Compile the shipped script with a fake SDK and exercise mutation boundaries.

Windows/.NET Framework only. This is NOT validation against the real ND runtime.
No production project or user settings are touched.
"""
from pathlib import Path
import json
import os
import subprocess
import tempfile
import sys
import shutil

root = Path(__file__).resolve().parents[1]
source = (root / "main.cs").read_text(encoding="utf-8-sig")
start = source.index("public void EditMessage")
end = source.index("public static class ProbeHost")
code = source[:start] + "public class Handlers {\n" + source[start:end] + "}\n" + source[end:]
compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
with tempfile.TemporaryDirectory(prefix="model-update-probe-tests-") as tmp:
    folder = Path(tmp)
    (folder / "Production.cs").write_text(code, encoding="utf-8-sig")
    exe = folder / "Tests.exe"
    subprocess.run([str(compiler), "/nologo", "/warnaserror+", "/out:" + str(exe),
                    str(folder / "Production.cs"), str(root / "tests/FakeSdk.cs"),
                    str(root / "tests/Tests.cs")], check=True)
    subprocess.run([str(exe), str(folder)], check=True)
    if len(sys.argv) == 3 and sys.argv[1] == "--preview":
        shutil.copyfile(folder / "native-dialog.png", sys.argv[2])
    records = list(folder.rglob("*.json"))
    for path in records:
        json.loads(path.read_text(encoding="utf-8-sig"))
    assert records, "no evidence was produced"
    print(f"PASS: {len(records)} emitted JSON files parsed by Python")
