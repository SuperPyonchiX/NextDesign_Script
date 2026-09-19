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
    source[source.index("public class SessionInfo"):source.index("public static class WorkspaceBuilder")],
    source[source.index("public static class ReviewResultViewer"):source.index("public static class CliProbe")],
    source[source.index("public static class WorkspaceBuilder"):source.index("//  ファイルシステムのリンク・コピー")],
    "public class ReviewCommandHarness {\n"
    + source[source.index("public void StartAgentReview"):source.index("// レビューセッションを作らず")]
    .replace("ReviewInputPicker.Show(context.ExtensionInfo.ExtensionPath, project, root, false)", "FakePicker.Show(context, project)")
    .replace("ReviewInputPicker.Show(context.ExtensionInfo.ExtensionPath, project, root, true)", "FakePicker.Show(context, project)")
    + "\nprivate IModel ResolveRoot(IApplication app) { return app.Workspace.CurrentModel ?? app.Workspace.CurrentProject; }\n}\n",
    "public class ResultCommandHarness {\n"
    + source[source.index("public void OpenReviewResult"):source.index("public void OpenWorkspaceFolder")]
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
        "using System.Collections.Generic; using System.Text.RegularExpressions; using System.Diagnostics;\n"
        + "\n".join(parts)
        + (root / "tests/ExportTests.cs").read_text(encoding="utf-8"),
        encoding="utf-8-sig",
    )
    with program.open("a", encoding="utf-8") as f:
        f.write((root / "tests/ViewerTests.cs").read_text(encoding="utf-8"))
        f.write((root / "tests/InputTests.cs").read_text(encoding="utf-8"))
    recorder = directory / "Recorder.cs"
    recorder.write_text('''using System;
using System.IO;
using System.Collections.Generic;
public static class Recorder {
    public static void Main(string[] args) {
        var lines = new List<string>(args);
        lines.Add(Environment.CurrentDirectory);
        lines.Add(Environment.GetEnvironmentVariable("ELECTRON_RUN_AS_NODE") ?? "<unset>");
        File.WriteAllLines("launch-capture.tmp", lines, System.Text.Encoding.UTF8);
        File.Move("launch-capture.tmp", "launch-capture.txt");
    }
}
''', encoding="utf-8")
    subprocess.run([str(compiler), "/nologo", "/out:" + str(directory / "Code.exe"), str(recorder)], check=True)
    subprocess.run([str(compiler), "/nologo", "/warnaserror+", "/out:" + str(executable), str(program)], check=True)
    subprocess.run([str(executable), str(directory)], check=True,
                   env={**os.environ, "AGENTREVIEW_TEST_HOME": str(directory)})
    # Parse the generated workspace as JSON, not just as string fragments.
    import json
    for workspace in directory.rglob("agentreview-results.code-workspace"):
        document = json.loads(workspace.read_text(encoding="utf-8"))
        assert document["folders"] == [{"path": "."}]
        assert document["settings"]["workbench.editorAssociations"] == {
            "**/review/review.md": "vscode.markdown.preview.editor",
            "**/review/proposal.md": "vscode.markdown.preview.editor",
            "**/review/coverage.md": "vscode.markdown.preview.editor",
            "**/review/changes.md": "vscode.markdown.preview.editor",
        }
    print("PASS: generated VS Code workspace JSON")
