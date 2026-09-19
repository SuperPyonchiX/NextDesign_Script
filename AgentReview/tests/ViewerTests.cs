// Tests production viewer/configuration code with an argv-recording Code.exe.
// Does not install or launch a real VS Code instance.
public static class ViewerTests
{
    private static int _checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        _checks++;
    }
    private static void Throws(Action action, string contains)
    {
        try { action(); }
        catch (Exception ex) { Check(ex.Message.Contains(contains), "Unexpected error: " + ex.Message); return; }
        throw new Exception("Expected error: " + contains);
    }
    private static string FakeExe(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.GetFullPath(Path.Combine(folder, "Code.exe")); File.WriteAllText(path, "test only"); return path;
    }
    private static void WaitForCapture(string folder)
    {
        var path = Path.Combine(folder, "launch-capture.txt");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(20);
        Check(File.Exists(path), "Child process did not record launch arguments");
    }
    public static void Run(string temp)
    {
        // Only the test runner's isolated config directory is affected.
        File.Delete(AgentConfig.ConfigPath());
        Check(new AgentConfig().Agent == "codex" && AgentConfig.Load().ActiveProfile().Key == "codex", "First launch uses Codex");
        foreach (var text in new[] { "workspaceRoot=somewhere", "agent=", "agent=unknown" })
        {
            File.WriteAllText(AgentConfig.ConfigPath(), text);
            Check(AgentConfig.Load().Agent == "codex", "Missing/invalid agent uses Codex");
        }
        var config = new AgentConfig { Agent = "claude", VsCodeExecutable = Path.Combine(temp, "Code.exe") };
        config.Save();
        Check(AgentConfig.Load().Agent == "claude", "Existing Claude selection stays");
        Check(AgentConfig.Load().VsCodeExecutable == config.VsCodeExecutable, "VS Code setting round trip");
        config.Agent = "codex"; config.Save();
        Check(AgentConfig.Load().Agent == "codex" && AgentConfig.Load().VsCodeExecutable == config.VsCodeExecutable, "Switch preserves viewer setting");

        var local = Path.Combine(temp, "Local");
        var programs = Path.Combine(temp, "Program Files");
        var x86 = Path.Combine(temp, "Program Files x86");
        var standard = FakeExe(Path.Combine(local, "Programs/Microsoft VS Code"));
        var system = FakeExe(Path.Combine(programs, "Microsoft VS Code"));
        var systemX86 = FakeExe(Path.Combine(x86, "Microsoft VS Code"));
        var portable = FakeExe(Path.Combine(temp, "Portable Editor"));
        var bin = Path.Combine(Path.GetDirectoryName(portable), "bin"); Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "code.cmd"), "test only");
        var candidates = ReviewResultViewer.ExecutableCandidates(local, programs, x86, "relative;.;;\"" + bin + "\"").ToList();
        Check(ReviewResultViewer.FindExecutable("", candidates) == standard, "User install detection");
        Check(ReviewResultViewer.FindExecutable("", candidates.Skip(1)) == system, "System install detection");
        Check(ReviewResultViewer.FindExecutable("", candidates.Skip(2)) == systemX86, "x86 install detection");
        Check(ReviewResultViewer.FindExecutable("", ReviewResultViewer.ExecutableCandidates(null, null, null, bin)) == portable, "PATH code.cmd resolves to GUI executable");
        Check(ReviewResultViewer.FindExecutable("", ReviewResultViewer.ExecutableCandidates(null, null, null, Path.GetDirectoryName(portable))) == portable, "PATH direct executable");
        Check(ReviewResultViewer.FindExecutable(portable, candidates) == portable, "Explicit executable overrides detection");
        Throws(() => ReviewResultViewer.FindExecutable(Path.Combine(temp, "absent/Code.exe"), candidates), "参照ボタン");
        Throws(() => ReviewResultViewer.FindExecutable("Code.exe", candidates), "参照ボタン");
        Throws(() => ReviewResultViewer.FindExecutable("", new string[0]), "VS Code が見つかりません");

        var folder = Path.Combine(temp, "閲覧 Session (A) & %PATH% ! ^ #");
        var reviewDir = Path.Combine(folder, "review"); Directory.CreateDirectory(reviewDir);
        var executable = Path.Combine(temp, "Code.exe");
        Throws(() => ReviewResultViewer.PrepareLaunch(Path.Combine(temp, "absent"), executable), "フォルダがありません");
        Check(ReviewResultViewer.ResultFiles(folder).Count == 0, "No results");
        Throws(() => ReviewResultViewer.PrepareLaunch(folder, executable), "まだ生成されていません");
        Check(!File.Exists(Path.Combine(folder, ReviewResultViewer.WorkspaceFileName)), "No workspace for absent results");
        var proposal = Path.Combine(reviewDir, "proposal.md"); var review = Path.Combine(reviewDir, "review.md");
        File.WriteAllText(proposal, "# Proposal\n");
        Check(ReviewResultViewer.ResultFiles(folder).SequenceEqual(new[] { proposal }), "Proposal only");
        var proposalInfo = ReviewResultViewer.PrepareLaunch(folder, executable);
        Check(proposalInfo.Arguments.Contains("proposal.md") && !proposalInfo.Arguments.Contains("review.md"), "Only existing files passed");
        Check(!File.Exists(review), "Missing Markdown not created");
        File.Delete(proposal); File.WriteAllText(review, "# Review\n");
        Check(ReviewResultViewer.ResultFiles(folder).SequenceEqual(new[] { review }), "Review only");
        File.WriteAllText(proposal, "# Proposal\n");
        Check(ReviewResultViewer.ResultFiles(folder).SequenceEqual(new[] { proposal, review }), "Both files are passed together");
        var userSettings = Path.Combine(folder, ".vscode/settings.json"); Directory.CreateDirectory(Path.GetDirectoryName(userSettings));
        File.WriteAllText(userSettings, "{\"editor.fontSize\": 17}");
        Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", "1");
        var info = ReviewResultViewer.PrepareLaunch(folder, executable);
        Check(!info.UseShellExecute && info.CreateNoWindow && info.FileName == executable, "Direct executable without command shell");
        Check(info.Arguments.StartsWith("--new-window ") && !info.Arguments.Contains("--wait"), "Dedicated window without waiting");
        Check(!info.EnvironmentVariables.ContainsKey("ELECTRON_RUN_AS_NODE"), "Remove inherited Electron node mode");
        Check(File.ReadAllText(userSettings) == "{\"editor.fontSize\": 17}", "Do not change existing folder settings");
        var workspace = Path.Combine(folder, ReviewResultViewer.WorkspaceFileName);
        var json = File.ReadAllText(workspace);
        Check(!json.Contains(temp) && json.Contains("\"path\": \".\""), "Portable relative workspace");
        Check(json.Contains("**/review/review.md") && json.Contains("**/review/proposal.md") && !json.Contains("\"*.md\""), "Preview associations limited to result files");
        var bytes = File.ReadAllBytes(workspace);
        Check(bytes.Length > 3 && !(bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf), "Workspace is UTF-8 without BOM");
        using (var child = Process.Start(info))
            Check(child.WaitForExit(5000) && child.ExitCode == 0, "Launch argv recorder");
        var captured = File.ReadAllLines(Path.Combine(folder, "launch-capture.txt"));
        Check(captured.SequenceEqual(new[] { "--new-window", workspace, proposal, review, folder, "<unset>" }), "Special characters survive Windows argv and cwd intact");

        // Execute the actual button handler with fake UI and a real child process.
        var command = new ResultCommandHarness(); var context = new ICommandContext();
        var sessionBase = Path.Combine(temp, "sessions"); Directory.CreateDirectory(sessionBase);
        config.WorkspaceRoot = sessionBase; config.Save();
        command.OpenReviewResult(context, new ICommandParams());
        Check(context.App.Window.UI.Messages.Last().Contains("セッションが見つかりません"), "No session message");
        var sessionFolder = Path.Combine(sessionBase, "one"); Directory.CreateDirectory(Path.Combine(sessionFolder, "review"));
        new SessionInfo { Folder = sessionFolder, Agent = "claude" }.Save();
        Check(SessionInfo.LoadFrom(sessionFolder).Agent == "claude", "Saved session agent unchanged");
        command.OpenReviewResult(context, new ICommandParams());
        Check(context.App.Window.UI.Messages.Last().Contains("まだ生成されていません"), "No result message");
        File.WriteAllText(Path.Combine(sessionFolder, "review/review.md"), "# Result\n");
        config.VsCodeExecutable = Path.Combine(temp, "absent/Code.exe"); config.Save();
        command.OpenReviewResult(context, new ICommandParams());
        Check(context.App.Window.UI.Messages.Last().Contains("参照ボタン") && context.App.Window.UI.Messages.Last().Contains(sessionFolder), "Failure explains setting and session location");
        Check(!File.Exists(Path.Combine(sessionFolder, ReviewResultViewer.WorkspaceFileName)), "Failed discovery creates no workspace");
        config.VsCodeExecutable = executable; config.Save();
        var messages = context.App.Window.UI.Messages.Count;
        command.OpenReviewResult(context, new ICommandParams());
        WaitForCapture(sessionFolder);
        Check(context.App.Window.UI.Messages.Count == messages && context.App.Output.Lines.Last().Contains("表示を要求"), "Successful handler submits launch without claiming UI verification");
        Check(File.ReadAllLines(Path.Combine(sessionFolder, "launch-capture.txt"))[2].EndsWith("review.md"), "Handler sends existing result");
        Environment.SetEnvironmentVariable("ELECTRON_RUN_AS_NODE", null);
        Console.WriteLine("PASS: " + _checks + " viewer/config assertions (real VS Code preview still requires verification).");
    }
}
