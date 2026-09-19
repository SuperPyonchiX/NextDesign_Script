// Command boundaries use a fake SDK; the filesystem and generated inputs are real.
public class IProject : IModel { public string Path; }
public enum EditorAccessMode { GetInactiveValue }
public class TestContextOption { public EditorAccessMode EditorAccessMode; }
public class TestExtensionInfo { public string ExtensionPath = "test-extension"; }
public class TestWorkspace
{
    public IProject CurrentProject, Historical;
    public IModel CurrentModel;
    public bool Opened, Closed, ThrowOpen;
    public IProject OpenProject(string path, bool current, bool exclude)
    {
        if (current || exclude) throw new Exception("Unsafe OpenProject options");
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        Opened = true;
        if (ThrowOpen) throw new IOException("fake load failure");
        return Historical;
    }
    public void CloseProject(IProject project)
    {
        if (object.ReferenceEquals(project, CurrentProject)) throw new Exception("Closed current project");
        Closed = true;
    }
}
public static class OutputPane { public static void Show(IApplication app, string category) { } }
public static class SkillProvisioner
{
    public static string SourceDir(string path) { return path; }
    public static void ValidateSource(string path) { }
    public static void LinkToSession(string folder, string source) { }
}
public static class TerminalLauncher
{
    public static int Launches;
    public static void Launch(string folder, string command, string terminal) { Launches++; }
    public static void OpenWithNotepad(string path) { }
}
public static class InputTests
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        checks++;
    }
    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (IOException) { checks++; return; }
        catch (InvalidDataException) { checks++; return; }
        throw new Exception(message);
    }
    private static IProject Project(string path, string id)
    {
        return new IProject { Path = path, Id = id, Name = id, Metaclass = new Meta { FullName = "Example.Project" } };
    }
    public static void Run(string temp)
    {
        var area = Path.Combine(temp, "input-tests");
        var projectDir = Path.Combine(area, "project");
        Directory.CreateDirectory(projectDir);
        var projectFile = Path.Combine(projectDir, "sample.nproj");
        File.WriteAllText(projectFile, "synthetic");
        var doc = Path.Combine(projectDir, "requirement.md");
        File.WriteAllText(doc, "# 上位\n応答時間: 100ms");
        var setting = ReviewInputs.SettingsPath(projectFile);
        ReviewInputs.CreateTemplate(setting);
        Check(ReviewInputs.SettingsPath(projectFile.ToUpperInvariant()) == setting, "Case-insensitive project config");
        Check(ReviewInputs.SettingsPath(Path.Combine(area, "other.nproj")) != setting, "Separate config per project");
        File.WriteAllText(setting, "upstream.file.1=requirement.md\nupstream.model.1=upper\n");
        var inputs = ReviewInputs.Load(setting, projectFile);
        inputs.ValidateUpstream();
        Check(inputs.Files.Single() == doc && inputs.ModelIds.Single() == "upper", "Resolve relative path and model ID");
        ReviewInputs.CreateTemplate(setting);
        Check(File.ReadAllText(setting).Contains("=upper"), "Keep edited settings");
        File.WriteAllText(setting, "upstream.modle.1=upper");
        Throws(() => ReviewInputs.Load(setting, projectFile), "Reject typo");
        File.WriteAllText(setting, "upstream.model.1=x\nupstream.model.1=y");
        Throws(() => ReviewInputs.Load(setting, projectFile), "Reject duplicate keys");
        File.WriteAllText(setting, "upstream.model.1");
        Throws(() => ReviewInputs.Load(setting, projectFile), "Reject malformed line");
        new ReviewInputs().ValidateUpstream(); // 未指定は許可し、結果で未確認とする。
        inputs.Files.Add(Path.Combine(projectDir, "missing.xlsx"));
        Throws(() => inputs.ValidateUpstream(), "Reject missing file");

        var copy = Path.Combine(area, "copy.md");
        var sha = ReviewSnapshot.CopyFile(doc, copy);
        File.WriteAllText(doc, "changed");
        Check(File.ReadAllText(copy).Contains("100ms") && sha.Length == 64, "Snapshot survives source edit");
        Throws(() => ReviewSnapshot.CopyFile(doc, copy), "Do not overwrite snapshot");
        Throws(() => ReviewSnapshot.CopyTree(projectDir, Path.Combine(projectDir, "nested"), new StringBuilder(), "nested"),
            "Reject recursive copy");
        Check(!Directory.Exists(Path.Combine(projectDir, "nested")), "No nested destination created");
        Check(ReviewSnapshot.Cell("a|b\n<script>").Contains("&#124;b &lt;script&gt;"), "Escape inventory cells");

        var workspace = Path.Combine(area, "sessions");
        Directory.CreateDirectory(workspace);
        var config = new AgentConfig { WorkspaceRoot = workspace }; config.Save();
        var project = Project(projectFile, "project");
        var root = new IModel { Id = "target", Name = "Design", Owner = project, Metaclass = new Meta { FullName = "Example.Design" } };
        var upper = new IModel { Id = "upper", Name = "Requirement", Owner = project, Metaclass = new Meta { FullName = "Example.Requirement" } };
        project.Children.Add(root); project.Children.Add(upper);
        var context = new ICommandContext();
        context.App.Workspace.CurrentProject = project; context.App.Workspace.CurrentModel = root;
        var command = new ReviewCommandHarness();
        File.WriteAllText(setting, "upstream.model.1=upper\nupstream.file.1=requirement.md");
        var attachment = Path.Combine(projectDir, "Attachment");
        Directory.CreateDirectory(attachment); File.WriteAllText(Path.Combine(attachment, "table.csv"), "original");
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == 1, "Launch after upstream inputs");
        var session = SessionLocator.FindLatest(workspace);
        Check(session != null && session.Mode == "review" && session.State == "ready", "Ready upstream session");
        Check(File.ReadAllText(Path.Combine(session.Folder, "upstream/models/001/design.md")).Contains("Requirement"), "Export upper model separately");
        Check(File.Exists(Path.Combine(session.Folder, "upstream/files/001/requirement.md")), "Copy external upper document");
        Check(File.ReadAllText(Path.Combine(session.Folder, "inputs.md")).Contains("SHA-256"), "Input inventory");
        Check(File.ReadAllText(Path.Combine(session.Folder, "AGENTS.md")).Contains("review/coverage.md"), "Coverage contract");
        File.WriteAllText(Path.Combine(attachment, "table.csv"), "later");
        Check(File.ReadAllText(Path.Combine(session.DesignDir(), "Attachment/table.csv")) == "original", "Attachment frozen");
        Check((File.GetAttributes(Path.Combine(session.DesignDir(), "Attachment")) & FileAttributes.ReparsePoint) == 0, "No source junction");
        File.WriteAllText(Path.Combine(session.ReviewDir(), "coverage.md"), "coverage");
        Check(ReviewResultViewer.ResultFiles(session.Folder).Single().EndsWith("coverage.md"), "Coverage-only result opens");
        var launches = TerminalLauncher.Launches;
        File.WriteAllText(setting, "upstream.model.1=missing");
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches && Directory.GetDirectories(workspace).Length == 1, "Invalid model stops before session creation");
        File.WriteAllText(setting, "upstream.file.1=missing.pdf");
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches, "Missing source prevents launch");
        File.WriteAllText(setting, "upstream.model.1=upper");
        context.App.Window.UI.Confirm = false;
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches && Directory.GetDirectories(workspace).Length == 1, "Cancel creates no session");
        context.App.Window.UI.Confirm = true;
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches + 1, "Single review preserved");
        session = SessionLocator.FindLatest(workspace);
        Check(session.Mode == "review" && Directory.Exists(Path.Combine(session.Folder, "upstream")), "Review always includes configured upstream");
        var incomplete = Path.Combine(workspace, "failed");
        Directory.CreateDirectory(incomplete);
        new SessionInfo { Folder = incomplete, State = "failed" }.Save();
        Check(SessionLocator.FindLatest(workspace).Folder != incomplete, "Failed session is not resumable");
        var lastReady = SessionLocator.FindLatest(workspace).Folder;
        using (File.Open(Path.Combine(attachment, "table.csv"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches + 1, "Snapshot failure prevents AI launch");
        Check(SessionLocator.FindLatest(workspace).Folder == lastReady, "Partial export is not selected for resume");
        Check(Directory.GetDirectories(workspace).Select(SessionInfo.LoadFrom).Any(s => s != null && s.State == "failed" && s.Folder != incomplete),
            "Failed input generation persisted");

        var pastDir = Path.Combine(area, "past");
        Directory.CreateDirectory(pastDir);
        File.WriteAllText(Path.Combine(pastDir, "sample.nproj"), "past");
        File.WriteAllText(setting, "probe.folder=" + pastDir + "\nprobe.project=sample.nproj");
        var past = Project(Path.Combine(pastDir, "sample.nproj"), "past");
        context.App.Workspace.Historical = past;
        command.ProbeHistoricalExport(context, new ICommandParams());
        Check(context.App.Workspace.Opened && context.App.Workspace.Closed, "Historical model opened and released");
        Check(context.App.Workspace.CurrentProject == project && File.ReadAllText(projectFile) == "synthetic", "Current project retained");
        Check(Directory.GetFiles(workspace, "probe.md", SearchOption.AllDirectories).Length == 1, "Probe report written");
        Check(TerminalLauncher.Launches == launches + 1, "Probe launches no AI");
        command.StartChangeReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches + 1 && context.App.Window.UI.Messages.Last().Contains("未提供"), "Change review remains gated");
        context.App.Workspace.ThrowOpen = true;
        command.ProbeHistoricalExport(context, new ICommandParams());
        Check(Directory.GetFiles(workspace, "failure.txt", SearchOption.AllDirectories).Length == 1, "Load failure recorded");
        Check(context.App.Workspace.CurrentProject == project, "Failure does not replace current project");
        var count = TerminalLauncher.Launches;
        File.Delete(setting);
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count && File.Exists(setting), "Configure choice opens settings without review");
        var sessionsBeforeCancel = Directory.GetDirectories(workspace).Length;
        context.App.Window.UI.ConfirmAnswers.Enqueue(false);
        context.App.Window.UI.ConfirmAnswers.Enqueue(false);
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count && Directory.GetDirectories(workspace).Length == sessionsBeforeCancel,
            "Declining no-upstream review cancels without session");
        File.Delete(setting);
        context.App.Window.UI.ConfirmAnswers.Enqueue(false);
        context.App.Window.UI.ConfirmAnswers.Enqueue(true);
        command.StartAgentReview(context, new ICommandParams());
        session = SessionLocator.FindLatest(workspace);
        Check(TerminalLauncher.Launches == count + 1, "Missing config allows review");
        Check(File.ReadAllText(Path.Combine(session.Folder, "inputs.md")).Contains("上位文書未指定のため整合は未確認"),
            "No upstream recorded explicitly");
        Check(File.ReadAllText(Path.Combine(session.Folder, "inputs.md")).Contains("開始前にユーザーが選択"),
            "Explicit choice recorded");
        Check(File.ReadAllText(Path.Combine(session.Folder, "AGENTS.md")).Contains("工程別の単体観点と上位要求との整合を両方確認"),
            "Both review perspectives always instructed");
        ReviewInputs.CreateTemplate(setting);
        context.App.Window.UI.ConfirmAnswers.Enqueue(false);
        context.App.Window.UI.ConfirmAnswers.Enqueue(true);
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 2, "Empty upstream settings allow review");
        project.Path = null;
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 2 && context.App.Window.UI.Messages.Last().Contains("保存済み"),
            "Unsaved project explains settings requirement without starting review");
        context.App.Window.UI.ConfirmAnswers.Enqueue(false);
        context.App.Window.UI.ConfirmAnswers.Enqueue(true);
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 3, "Unsaved project can be reviewed with no upstream");
        project.Path = projectFile;
        File.WriteAllText(setting, "upstream.file.1=requirement.md");
        var messageStart = context.App.Window.UI.Messages.Count;
        command.StartAgentReview(context, new ICommandParams());
        session = SessionLocator.FindLatest(workspace);
        Check(TerminalLauncher.Launches == count + 4
            && File.Exists(Path.Combine(session.Folder, "upstream/files/001/requirement.md")), "External-only upstream included");
        Check(!File.ReadAllText(Path.Combine(session.Folder, "inputs.md")).Contains("上位文書未指定"),
            "External-only input is not marked missing");
        Check(!context.App.Window.UI.Messages.Skip(messageStart).Any(m => m.Contains("上位文書を設定してから")),
            "Configured external document does not ask missing-input question");
        File.WriteAllText(setting, "upstream.modle.1=upper");
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 4, "Malformed config cannot silently omit upstream");
        File.WriteAllText(setting, "upstream.intentionalNone=true\nupstream.noneReason=このプロジェクトが最上位の要求を定義するため");
        messageStart = context.App.Window.UI.Messages.Count;
        command.StartAgentReview(context, new ICommandParams());
        session = SessionLocator.FindLatest(workspace);
        Check(TerminalLauncher.Launches == count + 5, "Explicit none starts review");
        var manifest = File.ReadAllText(Path.Combine(session.Folder, "inputs.md"));
        Check(manifest.Contains("設定状態: 意図的になし") && manifest.Contains("最上位の要求を定義するため"),
            "Intentional none and reason captured in session");
        Check(manifest.Contains("今回の確認: 上位文書なしで続行するとユーザーが回答")
            && manifest.Contains("今回の確認日時 (UTC):"), "Current answer and timestamp recorded separately");
        Check(ReviewInputs.Load(setting, projectFile).NoneConfirmedAt == "", "Confirmation is never restored from saved config");
        Check(!context.App.Window.UI.Messages.Skip(messageStart).Any(m => m.Contains("上位文書を設定してから")),
            "Intentional none bypasses unset question");
        Check(context.App.Window.UI.Messages.Last().Contains("最上位の要求を定義するため"),
            "Reason visible in review-start confirmation");
        Check(context.App.Window.UI.Messages.Skip(messageStart).Count(m => m.Contains("今回も上位文書なしでレビューしますか")) == 1,
            "Explicit none asks for this review");
        messageStart = context.App.Window.UI.Messages.Count;
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 6, "Intentional none persists for next review");
        Check(context.App.Window.UI.Messages.Skip(messageStart).Count(m => m.Contains("今回も上位文書なしでレビューしますか")) == 1,
            "Next review asks again despite saved reason");
        sessionsBeforeCancel = Directory.GetDirectories(workspace).Length;
        var savedIntent = File.ReadAllText(setting);
        context.App.Window.UI.ConfirmAnswers.Enqueue(false);
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 6 && Directory.GetDirectories(workspace).Length == sessionsBeforeCancel,
            "Declining intentional none does not create session or launch AI");
        Check(File.ReadAllText(setting) == savedIntent, "Declining does not overwrite saved intent");
        File.WriteAllText(setting, "upstream.intentionalNone=true\n");
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 6 && context.App.Window.UI.Messages.Last().Contains("理由"),
            "Intentional none without reason rejected");
        File.WriteAllText(setting, "upstream.intentionalNone=true\nupstream.noneReason=理由\nupstream.model.1=upper");
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 6 && context.App.Window.UI.Messages.Last().Contains("併用"),
            "Conflicting none and model rejected");
        File.WriteAllText(setting, "upstream.intentionalNone=true\nupstream.noneReason=理由\nupstream.file.1=requirement.md");
        Throws(() => ReviewInputs.Load(setting, projectFile).ValidateUpstream(), "Conflicting none and file rejected");
        File.WriteAllText(setting, "upstream.intentionalNone=maybe");
        Throws(() => ReviewInputs.Load(setting, projectFile), "Invalid intent rejected");
        File.WriteAllText(setting, "upstream.noneReason=stale");
        Throws(() => ReviewInputs.Load(setting, projectFile).ValidateUpstream(), "Stale reason rejected");
        File.WriteAllText(setting, "upstream.intentionalNone=false\nupstream.noneReason=");
        context.App.Window.UI.ConfirmAnswers.Enqueue(false);
        context.App.Window.UI.ConfirmAnswers.Enqueue(false);
        messageStart = context.App.Window.UI.Messages.Count;
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 6
            && context.App.Window.UI.Messages.Skip(messageStart).Any(m => m.Contains("上位文書を設定してから")),
            "Clearing intentional none restores unset question");
        Console.WriteLine("PASS: " + checks + " input/command assertions (real Next Design still requires verification).");
    }
}
