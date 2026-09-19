public static class FakeChangeDialog
{
    public static string CommitId, CancelStage;
    public static Queue<int> Choices = new Queue<int>();
    public static string Commit(GitChange git) { return CommitId; }
    public static int Choose(string title, List<string> labels) { return Choices.Count == 0 ? -1 : Choices.Dequeue(); }
    public static int ChooseModel(string title, List<string> labels, List<int> parents) { return Choose(title, labels); }
    public static void Work(string title, Action<System.Threading.CancellationToken> work) {
        if (title == CancelStage) throw new OperationCanceledException();
        work(System.Threading.CancellationToken.None);
    }
}
public static class ChangeTests
{
    private static int checks;
    private static void Check(bool value, string description) { if (!value) throw new Exception(description); checks++; }
    private static string Run(string dir, params string[] args) { return Encoding.UTF8.GetString(GitChange.Run(dir, args, null)).Trim(); }
    private static void Reject(Action action, string message) { try { action(); } catch (IOException) { checks++; return; } throw new Exception(message); }
    public static void Run(string temp) {
        var dir = Path.Combine(temp, "履歴 repository"); Directory.CreateDirectory(dir);
        Run(dir, "init"); Run(dir, "config", "user.name", "Fixture"); Run(dir, "config", "user.email", "fixture@example.invalid");
        var projectFile = Path.Combine(dir, "sample.nproj"); File.WriteAllText(projectFile, "old project");
        File.WriteAllText(Path.Combine(dir, "日本語 file.txt"), "past");
        File.WriteAllText(Path.Combine(dir, ".gitattributes"), "*.txt export-ignore\n");
        Run(dir, "add", "."); Run(dir, "commit", "-m", "最初の版");
        var git = new GitChange(dir); var commit = git.Resolve("HEAD");
        Run(dir, "tag", "v1"); Check(git.Resolve("v1") == commit, "Tag resolves to fixed commit");
        File.WriteAllText(Path.Combine(dir, "日本語 file.txt"), "unsaved working-tree value");
        File.WriteAllText(Path.Combine(dir, "untracked.txt"), "not committed");
        var status = Run(dir, "status", "--porcelain"); var head = git.Resolve("HEAD");
        var copy = Path.Combine(temp, "取得結果"); git.Extract(commit, copy, System.Threading.CancellationToken.None);
        Check(File.ReadAllText(Path.Combine(copy, "日本語 file.txt")) == "past", "Exact blob ignores working tree and export-ignore");
        Check(!File.Exists(Path.Combine(copy, "untracked.txt")), "Untracked file excluded");
        Check(git.Resolve("HEAD") == head && Run(dir, "status", "--porcelain") == status, "Working tree and HEAD unchanged");
        using (var cancel = new System.Threading.CancellationTokenSource()) {
            cancel.Cancel(); bool stopped = false;
            try { git.Extract(commit, Path.Combine(temp, "cancel-copy"), cancel.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped && !Directory.Exists(Path.Combine(temp, "cancel-copy")), "Cancelled acquisition creates no output");
        }
        File.WriteAllText(Path.Combine(dir, "large.dat"), "version https://git-lfs.github.com/spec/v1\noid sha256:000\nsize 999\n");
        Run(dir, "add", "large.dat"); Run(dir, "commit", "-m", "pointer");
        Reject(() => git.Extract(git.Resolve("HEAD"), Path.Combine(temp, "lfs-copy"), System.Threading.CancellationToken.None), "LFS accepted");
        var before = new List<ChangeRecord> { new ChangeRecord { Key = "model:a", Name = "before", Parent = "p1", Content = "text\r\n" }, new ChangeRecord { Key = "model:removed" } };
        var after = new List<ChangeRecord> { new ChangeRecord { Key = "model:a", Name = "after", Parent = "p2", Content = "text\n" }, new ChangeRecord { Key = "diagram:b", Content = "@startuml\n@enduml" } };
        var diffDir = Path.Combine(temp, "diff-fixture");
        Check(ChangeDiff.Build(diffDir, before, after) == 3, "Rename/move/add/remove entries");
        var diff = File.ReadAllText(Path.Combine(diffDir, "diff/changes.md"));
        Check(diff.Contains("名称変更 / 移動") && diff.Contains("範囲からの除外") && diff.Contains("範囲への追加"), "Change types shown");
        Check(ChangeDiff.Build(Path.Combine(temp, "equal"), new List<ChangeRecord> { new ChangeRecord { Key = "a", Content = "x\r\n" } }, new List<ChangeRecord> { new ChangeRecord { Key = "a", Content = "x\n" } }) == 0, "Line endings normalized");
        Check(ChangeDiff.Build(Path.Combine(temp, "spaces"), new List<ChangeRecord> { new ChangeRecord { Key = "a", Content = "x " } }, new List<ChangeRecord> { new ChangeRecord { Key = "a", Content = "x" } }) == 1, "Meaningful spaces retained");
        var outputs = Path.Combine(temp, "change-sessions"); Directory.CreateDirectory(outputs);
        new AgentConfig { WorkspaceRoot = outputs }.Save();
        var project = new IProject { Path = projectFile, Id = "project", Name = "project" };
        var target = new IModel { Id = "deliverable", Name = "current-unsaved", Owner = project, Metaclass = new Meta { FullName = "Test.Design" } }; project.Children.Add(target);
        var historical = new IProject { Id = project.Id, Name = "past" };
        var past = new IModel { Id = target.Id, Name = "old", Owner = historical, Metaclass = target.Metaclass }; historical.Children.Add(past);
        historical.Children.Add(new IModel { Id = "other-phase", Name = "Excluded", Owner = historical, Metaclass = target.Metaclass });
        var context = new ICommandContext(); context.App.Workspace.CurrentProject = project; context.App.Workspace.CurrentModel = target; context.App.Workspace.Historical = historical;
        FakePicker.None = true; FakeChangeDialog.CommitId = commit;
        var command = new ReviewCommandHarness(); int launches = TerminalLauncher.Launches;
        command.StartChangeReview(context, new ICommandParams());
        var session = SessionLocator.FindLatest(outputs);
        Check(session != null && session.Mode == "change" && session.BaselineCommit == commit && session.CurrentTargetId == target.Id, "Change metadata persisted");
        Check(TerminalLauncher.Launches == launches + 1 && context.App.Workspace.Closed, "AI starts after past released");
        Check(File.ReadAllText(Path.Combine(session.DesignDir(), "design.md")).Contains("current-unsaved"), "Current in-memory target captured");
        Check(!File.ReadAllText(Path.Combine(session.Folder, "baseline/design/design.md")).Contains("Excluded"), "Other phase excluded");
        Check(File.ReadAllText(Path.Combine(session.Folder, "AGENTS.md")).Contains("変更起因"), "Change review instructions");
        Check(git.Resolve("HEAD") != commit && Run(dir, "status", "--porcelain") == status, "Old commit comparison leaves dirty current tree untouched");
        past.Name = target.Name;
        command.StartChangeReview(context, new ICommandParams());
        session = SessionLocator.FindLatest(outputs);
        Check(TerminalLauncher.Launches == launches + 1 && File.ReadAllText(Path.Combine(session.ReviewDir(), "changes.md")).Contains("差分なし"), "No changes does not launch AI");
        historical.Children.Remove(past); FakeChangeDialog.Choices.Enqueue(0);
        command.StartChangeReview(context, new ICommandParams()); session = SessionLocator.FindLatest(outputs);
        Check(session.TargetMapping == "new" && TerminalLauncher.Launches == launches + 2, "Explicit new deliverable");
        past.Id = "old-id"; historical.Children.Add(past);
        // Available models sorted by synthetic ModelPath: old-id follows other-phase? lexical old < other.
        FakeChangeDialog.Choices.Enqueue(1);
        command.StartChangeReview(context, new ICommandParams()); session = SessionLocator.FindLatest(outputs);
        Check(session.TargetMapping == "manual" && session.BaselineTargetId == past.Id, "Manual mapping retained");
        launches = TerminalLauncher.Launches;
        FakeChangeDialog.CancelStage = "過去版を取得しています";
        command.StartChangeReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches && Directory.GetDirectories(outputs).Select(SessionInfo.LoadFrom).Any(s => s.State == "cancelled"), "Cancellation blocks AI");
        FakeChangeDialog.CancelStage = null; past.Id = target.Id; past.Name = "changed again"; context.App.Workspace.ThrowClose = true;
        command.StartChangeReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches && context.App.Window.UI.Messages.Last().Contains("fake close failure"), "Close failure blocks AI");
        context.App.Workspace.ThrowClose = false; past.IsProxy = true;
        command.StartChangeReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches && context.App.Window.UI.Messages.Last().Contains("未ロード"), "Incomplete old model blocked");
        Check(git.Resolve("HEAD") != commit && Run(dir, "status", "--porcelain") == status, "Failure leaves source untouched");
        past.IsProxy = false;
        context.App.Workspace.ThrowOpen = true;
        command.StartChangeReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches && context.App.Window.UI.Messages.Last().Contains("fake load failure"), "Open failure blocks AI");
        context.App.Workspace.ThrowOpen = false;
        target.Editors.Add(new IDiagram { Id = "empty-class", Kind = "class", Model = target, Nodes = 0 });
        command.StartChangeReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches && context.App.Window.UI.Messages.Last().Contains("出力警告"), "Export warning blocks incomplete comparison");
        target.Editors.Clear();
        var binary = Path.Combine(temp, "attachment-fixture"); Directory.CreateDirectory(binary);
        File.WriteAllBytes(Path.Combine(binary, "table.bin"), new byte[] { 0, 255, 1 });
        var firstFiles = new List<ChangeRecord>(); ChangeDiff.Attachments(binary, firstFiles);
        File.WriteAllBytes(Path.Combine(binary, "table.bin"), new byte[] { 0, 255, 2 });
        var nextFiles = new List<ChangeRecord>(); ChangeDiff.Attachments(binary, nextFiles);
        Check(ChangeDiff.Build(Path.Combine(temp, "attachment-diff"), firstFiles, nextFiles) == 1, "Binary attachment changes detected");
        foreach (var kind in new[] { "sequence", "class", "state" }) {
            Check(ChangeDiff.Build(Path.Combine(temp, "diagram-diff-" + kind),
                new List<ChangeRecord> { new ChangeRecord { Key = "diagram:a", Kind = kind, Content = "@startuml\nA -> B\n@enduml" } },
                new List<ChangeRecord> { new ChangeRecord { Key = "diagram:a", Kind = kind, Content = "@startuml\nB -> A\n@enduml" } }) == 1, "Diagram content compared: " + kind);
        }
        Run(dir, "mv", "sample.nproj", "renamed.nproj"); Run(dir, "commit", "-m", "project rename");
        project.Path = Path.Combine(dir, "renamed.nproj"); FakeChangeDialog.Choices.Enqueue(0);
        command.StartChangeReview(context, new ICommandParams());
        Check(TerminalLauncher.Launches == launches + 1 && SessionLocator.FindLatest(outputs).BaselineCommit == commit, "Moved project selected from past bundle");
        var countBeforeCancel = Directory.GetDirectories(outputs).Length; FakeChangeDialog.CommitId = null;
        command.StartChangeReview(context, new ICommandParams());
        Check(Directory.GetDirectories(outputs).Length == countBeforeCancel, "Commit picker cancel creates no session");
        FakeChangeDialog.CommitId = commit;
        Run(dir, "update-index", "--add", "--cacheinfo", "160000," + commit + ",submodule"); Run(dir, "commit", "-m", "submodule fixture");
        Reject(() => git.Extract(git.Resolve("HEAD"), Path.Combine(temp, "submodule-copy"), System.Threading.CancellationToken.None), "Submodule accepted");
        FakePicker.None = false;
        Console.WriteLine("PASS: " + checks + " change review assertions (real Git, fake SDK).");
    }
}
