"""Compile the shipped script with a fake SDK and exercise mutation boundaries.

Windows/.NET Framework only. This is NOT validation against the real ND runtime.
No production project or user settings are touched.
"""
from pathlib import Path
import json
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "main.cs").read_text(encoding="utf-8-sig")
start = source.index("public void EditMessage")
end = source.index("public static class ProbeHost")
code = source[:start] + "public class Handlers {\n" + source[start:end] + "}\n" + source[end:]
script = (root / "tools/message-dialog.ps1").read_text(encoding="utf-8-sig")
assert ('public const string Script = @"' + script.replace('"', '""') + '";') in source
compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
with tempfile.TemporaryDirectory(prefix="model-update-probe-tests-") as tmp:
    folder = Path(tmp)
    (folder / "Production.cs").write_text(code, encoding="utf-8-sig")
    exe = folder / "Tests.exe"
    subprocess.run([str(compiler), "/nologo", "/warnaserror+", "/out:" + str(exe),
                    str(folder / "Production.cs"), str(root / "tests/FakeSdk.cs"),
                    str(root / "tests/Tests.cs")], check=True)
    subprocess.run([str(exe), str(folder)], check=True)
    records = list(folder.rglob("*.json"))
    for path in records:
        json.loads(path.read_text(encoding="utf-8-sig"))
    assert records, "no evidence was produced"
    print(f"PASS: {len(records)} emitted JSON files parsed by Python")

with tempfile.TemporaryDirectory(prefix="model-update-dialog-") as tmp:
    folder = Path(tmp)
    input_path, output_path = folder / "input.json", folder / "output.json"
    input_path.write_text(json.dumps({"count":"2", "name0":"First", "name1":"Second"}), encoding="utf-8-sig")
    subprocess.run(["powershell", "-NoProfile", "-STA", "-File", str(root / "tools/message-dialog.ps1"),
                    "-InputPath", str(input_path), "-OutputPath", str(output_path), "-SelfTest"], check=True)
    result = json.loads(output_path.read_text(encoding="utf-8-sig"))
    assert result == {"index":"1", "value":'Changed "message"'}
    print("PASS: dialog selection, no-op prevention, text input and JSON output (standalone Windows Forms)")
