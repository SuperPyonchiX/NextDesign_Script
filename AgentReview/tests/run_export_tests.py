"""Compile the actual exporter with a small fake Next Design model on Windows.

Requires the .NET Framework C# compiler; no Next Design or package downloads.
This verifies export orchestration, not the PlantUML engines or the real SDK.
"""
from pathlib import Path
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "main.cs").read_text(encoding="utf-8-sig")
parts = [
    source[source.index("public static class AgentText"):source.index("public static class OutputPane")],
    source[source.index("public class AgentConfig"):source.index("public class SessionInfo")],
    source[source.index("public class MarkdownExportOptions"):source.index("public class ExportProbe")],
    "public class ExportCommandsHarness {\n"
    + source[source.index("private void WriteDesignArtifacts"):source.index("// レビューセッションを作らず")]
    .replace("private void WriteDesignArtifacts", "public void WriteDesignArtifacts")
    + "\n}",
]
# Exercise production Save/Load without writing to the user's real profile.
parts[1] = parts[1].replace(
    "Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)",
    'Environment.GetEnvironmentVariable("AGENTREVIEW_TEST_HOME")',
)
compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
with tempfile.TemporaryDirectory(prefix="agentreview-tests-") as tmp:
    directory = Path(tmp)
    program = directory / "ExportTests.cs"
    executable = directory / "ExportTests.exe"
    program.write_text(
        "using System; using System.IO; using System.Text; using System.Linq; "
        "using System.Collections.Generic; using System.Text.RegularExpressions;\n"
        + "\n".join(parts)
        + (root / "tests/ExportTests.cs").read_text(encoding="utf-8"),
        encoding="utf-8-sig",
    )
    subprocess.run([str(compiler), "/nologo", "/warnaserror+", "/out:" + str(executable), str(program)], check=True)
    subprocess.run([str(executable), str(directory)], check=True,
                   env={**os.environ, "AGENTREVIEW_TEST_HOME": str(directory)})
