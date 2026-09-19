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
public static class FakePicker
{
    public static bool None;
    public static ReviewInputs Show(ICommandContext context, IProject project)
    {
        if (!context.App.Window.UI.Confirm) return null;
        if (None) return new ReviewInputs { Phase = "detailed", IntentionalNone = true,
            NoneReason = "開始画面で今回は上位文書なしを選択", NoneConfirmedAt = DateTime.UtcNow.ToString("o") };
        var input = ReviewInputs.Load(ReviewInputs.SettingsPath(project.Path), project.Path);
        input.Phase = "detailed"; input.ValidateUpstream();
        return input;
    }
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
        FakePicker.None = true;
        var saved = File.ReadAllText(setting);
        command.StartAgentReview(context, new ICommandParams());
        session = SessionLocator.FindLatest(workspace);
        Check(TerminalLauncher.Launches == count + 1, "Explicit none launches");
        Check(session.Phase == "detailed", "Phase survives session save/load");
        Check(File.ReadAllText(Path.Combine(session.Folder, "inputs.md")).Contains("詳細設計"), "Phase in inventory");
        Check(File.ReadAllText(Path.Combine(session.Folder, "inputs.md")).Contains("上位整合: 未確認"), "No upstream marked unverified");
        Check(File.ReadAllText(setting) == saved, "None does not overwrite prior choices");
        Check(File.ReadAllText(Path.Combine(session.Folder, "AGENTS.md")).Contains("工程を再質問せず"), "No repeated phase instruction");
        context.App.Window.UI.Confirm = false;
        var before = Directory.GetDirectories(workspace).Length;
        command.StartAgentReview(context, new ICommandParams());
        Check(Directory.GetDirectories(workspace).Length == before && TerminalLauncher.Launches == count + 1, "Picker cancel stops all work");
        context.App.Window.UI.Confirm = true;
        project.Path = null;
        command.StartAgentReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == count + 2, "Unsaved project review allowed");
        Check(ReviewInputPicker.SettingsFile(null) == null, "Unsaved project has no stored choices");
        project.Path = projectFile;
        File.WriteAllText(setting, "upstream.model.1=upper\nprobe.project=keep.nproj");
        saved = File.ReadAllText(setting);
        var request = ReviewInputPicker.Request(project, root, false);
        Check(request.SelectNodes("/request/settings/phase/model").Count == 3, "Legacy choices offered for each phase");
        var response = new System.Xml.XmlDocument();
        response.LoadXml("<result action='accept' phase='detailed'><selection><model>upper</model><model>target</model></selection><settings lastPhase='detailed'><phase key='detailed'><model>upper</model></phase><phase key='architecture'><model>project</model></phase></settings></result>");
        var selected = ReviewInputPicker.Result(response, project);
        Check(selected.Phase == "detailed" && selected.ModelIds.Count == 2, "XML result");
        ReviewInputPicker.SaveSelection(project.Path, response);
        request = ReviewInputPicker.Request(project, root, false);
        Check(request.SelectSingleNode("/request/settings/phase[@key='architecture']/model").InnerText == "project", "Independent phase memory");
        Check(File.ReadAllText(setting) == saved, "Legacy probe config retained");
        selected.ModelIds.Add("project");
        Check(ReviewInputPicker.ResolveModels(project, selected).Count == 1, "Parent/child deduplication");
        response.DocumentElement.SetAttribute("phase", "unknown");
        Throws(() => ReviewInputPicker.Result(response, project), "Unknown phase rejected");
        response.DocumentElement.SetAttribute("phase", "detailed");
        upper.IsProxy = true;
        Throws(() => ReviewInputPicker.Result(response, project), "Unloaded model rejected");
        upper.IsProxy = false;
        response.SelectSingleNode("/result/selection/model").InnerText = "deleted";
        Throws(() => ReviewInputPicker.Result(response, project), "Deleted model rejected");
        response.DocumentElement.SetAttribute("action", "cancel");
        Check(ReviewInputPicker.Result(response, project) == null, "XML cancel");
        response.DocumentElement.SetAttribute("action", "none");
        Check(ReviewInputPicker.Result(response, project).IntentionalNone, "XML none does not export stale choices");
        NativePickerTests.Run();
        Console.WriteLine("PASS: " + checks + " input/command assertions (real Next Design still requires verification).");
    }
}
