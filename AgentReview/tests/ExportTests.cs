// Fake SDK surface and engines. All model names and types are synthetic.
public class IField
{
    public string Name, Type;
    public bool IsEmbedded, IsReference;
    public Meta TypeClass;
}
public class Meta
{
    public string FullName;
    public List<IField> Fields = new List<IField>();
    public IEnumerable<IField> GetFields() { return Fields; }
}
public class IModel
{
    public string Id, Name, ClassName;
    public bool IsDeleted, IsProxy, ThrowOwner;
    public Meta Metaclass;
    private IModel _owner;
    public IModel Owner { get { if (ThrowOwner) throw new IOException("owner unavailable"); return _owner; } set { _owner = value; } }
    public string ModelPath { get { return "test/" + Id + "/" + Name; } }
    public List<IModel> Children = new List<IModel>();
    public List<Editor> Editors = new List<Editor>();
    public IEnumerable<IModel> GetChildren() { return Children; }
    public IEnumerable<IModel> GetAllChildren() { return Children.SelectMany(c => new[] { c }.Concat(c.GetAllChildren())); }
    public IEnumerable<Editor> GetEditors() { return Editors; }
    public IEnumerable<object> GetFieldValues(string field) { return new object[0]; }
    public string GetFieldString(string field) { return ""; }
    public string GetRichTextField(string field, string format) { return ""; }
}
public interface IRepresentation { IModel Model { get; } }
public class Editor { public string Id; }
public class IDiagram : Editor, IRepresentation
{
    public IModel Model { get; set; }
    public string Kind;
    public int Nodes = 1;
}
public class ILifelineShape { }
public class ISequenceDiagram : IDiagram
{
    public string ViewDefinitionName = "Sequence";
    public List<ILifelineShape> Lifelines = new List<ILifelineShape> { new ILifelineShape() };
}
public class PlantUmlOptions { }
public class ClassPlantUmlOptions
{
    public Dictionary<string, string> MemberKindMap = new Dictionary<string, string>();
    public Dictionary<string, string> LinkMap = new Dictionary<string, string>();
}
public class StatePlantUmlOptions { }
public class SequencePlantUmlExporter
{
    public SequencePlantUmlExporter(ISequenceDiagram d, PlantUmlOptions o) { }
    public string Export() { return "@startuml\nparticipant A\n@enduml\n"; }
}
public class ClassPlantUmlExporter
{
    public int NodeCount;
    public List<string> Warnings = new List<string>();
    public ClassPlantUmlExporter(IDiagram d, ClassPlantUmlOptions o) { NodeCount = d.Nodes; }
    public string Export() { return "@startuml\nclass A\n@enduml\n"; }
}
public class StatePlantUmlExporter
{
    public int NodeCount;
    public List<string> Warnings = new List<string>();
    public StatePlantUmlExporter(IDiagram d, StatePlantUmlOptions o) { NodeCount = d.Nodes; }
    public string Export() { return "@startuml\nstate A\n@enduml\n"; }
}
public static class StateExportRunner
{
    public static bool IsStateDiagram(IDiagram d, StatePlantUmlOptions o) { return d.Kind == "state"; }
}
public static class ClassExportRunner
{
    public static bool IsClassDiagramEditor(Editor e) { var d = e as IDiagram; return d != null && d.Kind == "class"; }
}
public static class HtmlToMarkdown { public static string Convert(string html) { return html; } }
public class IApplication { public TestOutput Output = new TestOutput(); public TestWindow Window = new TestWindow(); public TestWorkspace Workspace = new TestWorkspace(); }
public class TestWindow { public TestUI UI = new TestUI(); }
public class TestUI
{
    public List<string> Messages = new List<string>();
    public void ShowInformationDialog(string message, string category) { Messages.Add(message); }
    public bool Confirm = true;
    public Queue<bool> ConfirmAnswers = new Queue<bool>();
    public bool ShowConfirmDialog(string message, string category)
    {
        Messages.Add(message);
        return ConfirmAnswers.Count > 0 ? ConfirmAnswers.Dequeue() : Confirm;
    }
    public string ShowSelectFolderDialog(string message) { return null; }
}
public class ICommandContext
{
    public IApplication App = new IApplication();
    public TestContextOption ContextOption = new TestContextOption();
    public TestExtensionInfo ExtensionInfo = new TestExtensionInfo();
}
public class ICommandParams { }
public class TestOutput
{
    public List<string> Lines = new List<string>();
    public void WriteLine(string category, string message) { Lines.Add(message); }
}

public static class ExportTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    private static IModel Model(string id, string name, IModel parent, string type)
    {
        var model = new IModel { Id = id, Name = name, Owner = parent, Metaclass = new Meta { FullName = type } };
        if (parent != null) parent.Children.Add(model);
        return model;
    }
    private static IModel Diagram(string id, string name, IModel parent, string kind)
    {
        var model = Model(id, name, parent, "Example." + kind + ".Diagram");
        IDiagram d = kind == "sequence" ? new ISequenceDiagram() : new IDiagram();
        d.Model = model; d.Id = "editor-" + id; d.Kind = kind;
        model.Editors.Add(d);
        return model;
    }
    private static string[] Links(string text)
    {
        var references = string.Join("\n", text.Split('\n').Where(l => l.StartsWith("- 図:") || l.StartsWith("| ")).ToArray());
        return Regex.Matches(references, @"(?<!\\)\]\((diagrams/[^)]+)\)").Cast<Match>().Select(m => Uri.UnescapeDataString(m.Groups[1].Value)).ToArray();
    }
    private static void Exists(string dir, string path) { Check(File.Exists(Path.Combine(dir, path)), "Missing " + path); }
    private static void OwnsDiagram(IModel group, IModel diagram)
    {
        group.Metaclass.Fields.Add(new IField { Name = "OwnedViews", IsEmbedded = true,
            TypeClass = new Meta { FullName = diagram.Metaclass.FullName } });
    }
    public static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex) { Console.Error.WriteLine("FAIL after " + _checks + " assertions: " + ex.Message); Environment.Exit(1); }
    }
    private static void Run(string[] args)
    {
        var temp = args[0];
        var rulesPath = Path.Combine(temp, "rules.ini");
        File.WriteAllText(rulesPath, "sequence=Example.SequenceGroup\nclass=Example.ClassGroup\nstate=Example.StateGroup\n", Encoding.UTF8);
        var rules = DiagramGroupRules.Load(rulesPath);
        var config = new AgentConfig { DiagramGroupsRulesFile = rulesPath, Agent = "codex", Perspectives = "keep" };
        config.Save();
        var loaded = AgentConfig.Load();
        Check(loaded.DiagramGroupsRulesFile == rulesPath && loaded.Agent == "codex" && loaded.Perspectives == "keep", "Config round trip");
        loaded.Agent = "claude"; loaded.Save();
        Check(AgentConfig.Load().DiagramGroupsRulesFile == rulesPath, "Agent switch preserves rules");
        Check(rules.Warnings.Count == 0, "Valid rules");
        Check(rules.Matches("sequence", "Example.SequenceGroup"), "Exact type");
        Check(!rules.Matches("sequence", "Other.SequenceGroup") && !rules.Matches("class", "Example.SequenceGroup"), "Do not infer type");
        Check(DiagramGroupRules.Load("").Warnings.Count == 0, "No configuration is required");
        Check(DiagramGroupRules.Load("relative.ini").Warnings.Count == 1, "Relative rules path");
        Check(DiagramGroupRules.Load(Path.Combine(temp, "missing.ini")).Warnings.Count == 1, "Missing rules file");
        foreach (var invalid in new[] { "broken", "class=", "unknown=Example.X", "class=Example.X\nstate=Example.X", "class=Example.X\nclass=Example.Y", "class=ShortName", "" })
        {
            var file = Path.Combine(temp, "bad.ini"); File.WriteAllText(file, invalid);
            var bad = DiagramGroupRules.Load(file);
            Check(bad.Warnings.Count == 1 && !bad.Matches("class", "Example.X"), "Invalid rules must be atomic");
        }
        File.WriteAllText(Path.Combine(temp, "multi.ini"), "sequence=Example.SequenceGroup; Example.AlternateGroup");
        Check(DiagramGroupRules.Load(Path.Combine(temp, "multi.ini")).Matches("sequence", "Example.AlternateGroup"), "Multiple types");

        // Regression: a fresh installation must not require a private rules file.
        var autoRoot = Model("auto-root", "DesignDocument", null, "Example.Document");
        var implementation = Model("auto-impl", "Implementation", autoRoot, "Example.Implementation");
        var behavior = Model("auto-behavior", "Behavior", implementation, "Example.Behavior");
        var autoGroup = Model("auto-group", "Transport", behavior, "Example.UnnamedContainer");
        var autoSeq = Diagram("auto-seq", "Notify(Channel)", autoGroup, "sequence");
        OwnsDiagram(autoGroup, autoSeq);
        // References to a diagram and owning a different diagram type are not groups for this diagram.
        autoRoot.Metaclass.Fields.Add(new IField { Name = "Reference", IsReference = true, TypeClass = autoSeq.Metaclass });
        var autoClassGroup = Model("auto-classes", "Structure", implementation, "Example.StructureContainer");
        var autoClass = Diagram("auto-class", "Types", autoClassGroup, "class"); OwnsDiagram(autoClassGroup, autoClass);
        var autoStateGroup = Model("auto-states", "StateDesign", behavior, "Example.StateContainer");
        var autoState = Diagram("auto-state", "Lifecycle", autoStateGroup, "state"); OwnsDiagram(autoStateGroup, autoState);
        var autoDir = Path.Combine(temp, "automatic");
        var automatic = new MarkdownExporter(new MarkdownExportOptions(), autoDir);
        var autoBody = automatic.Export(autoRoot);
        Check(automatic.Warnings.Count == 0 && automatic.DiagramCount == 3, "Default export recognizes all three group kinds");
        Exists(autoDir, "diagrams/シーケンス図/Transport/Notify(Channel)_seq.puml");
        Exists(autoDir, "diagrams/クラス図/Structure/Types_class.puml");
        Exists(autoDir, "diagrams/状態遷移図/StateDesign/Lifecycle_state.puml");
        Check(Links(autoBody).All(p => !p.Contains("DesignDocument") && !p.Contains("Implementation") && !p.Contains("Behavior")), "Default output omits all ancestors above groups");
        var autoPath = Links(automatic.Export(autoSeq)).Single();
        Check(autoPath == "diagrams/シーケンス図/Transport/Notify(Channel)_seq.puml", "Default diagram-only selection retains group");
        Check(Links(automatic.Export(autoGroup)).Single() == autoPath, "Selection does not change diagram path");
        var autoNested = Model("auto-nested", "Nested", autoGroup, "Example.OtherContainer"); OwnsDiagram(autoNested, autoSeq);
        var autoChild = Model("auto-child", "Child", autoNested, "Example.Child");
        var autoDeep = Diagram("auto-deep", "Deep", autoChild, "sequence");
        Check(Links(automatic.Export(autoDeep)).Single() == "diagrams/シーケンス図/Transport/Nested/Child/Deep_seq.puml", "Automatic nested groups retain child hierarchy");
        var missingRules = DiagramGroupRules.Load(Path.Combine(temp, "not-deployed.ini"));
        var missingExporter = new MarkdownExporter(new MarkdownExportOptions(), autoDir, missingRules);
        Check(Links(missingExporter.Export(autoSeq)).Single() == autoPath && missingExporter.Warnings.Count == 1, "Missing optional file still uses automatic groups");
        var overrideFile = Path.Combine(temp, "override.ini"); File.WriteAllText(overrideFile, "sequence=Example.Behavior");
        var overrideExporter = new MarkdownExporter(new MarkdownExportOptions(), autoDir, DiagramGroupRules.Load(overrideFile));
        Check(Links(overrideExporter.Export(autoSeq)).Single() == "diagrams/シーケンス図/Behavior/Transport/Notify(Channel)_seq.puml", "Explicit override remains supported");

        var root = Model("root", "Project", null, "Example.Root");
        var upper = Model("upper", "Document", root, "Example.Document");
        var a = Model("a", "Start", upper, "Example.SequenceGroup");
        var b = Model("b", "Communication", upper, "Example.SequenceGroup");
        var first = Diagram("first", "Init", a, "sequence");
        Diagram("second", "Init", b, "sequence");
        var nested = Model("nested", "Nested", a, "Example.SequenceGroup");
        var middle = Model("middle", "Child", nested, "Example.Container");
        Diagram("deep", "Run", middle, "sequence");
        var cg = Model("cg", "Classes", upper, "Example.ClassGroup");
        Diagram("class", "Structure", cg, "class");
        var sg = Model("sg", "States", upper, "Example.StateGroup");
        Diagram("state", "Operation", sg, "state");
        var dir = Path.Combine(temp, "normal");
        var exporter = new MarkdownExporter(new MarkdownExportOptions { EmitTimestamp = false }, dir, rules);
        var body = exporter.Export(root);
        Check(exporter.DiagramCount == 5 && exporter.IndexRows.Count == 5 && exporter.Warnings.Count == 0, "All three kinds");
        Exists(dir, "diagrams/シーケンス図/Start/Init_seq.puml");
        Exists(dir, "diagrams/シーケンス図/Communication/Init_seq.puml");
        Exists(dir, "diagrams/シーケンス図/Start/Nested/Child/Run_seq.puml");
        Exists(dir, "diagrams/クラス図/Classes/Structure_class.puml");
        Exists(dir, "diagrams/状態遷移図/States/Operation_state.puml");
        Check(Links(body).OrderBy(s => s).SequenceEqual(Links(string.Join("\n", exporter.IndexRows.ToArray())).OrderBy(s => s)), "Index and body agree");
        Check(exporter.IndexRows.Any(s => s.Contains("[diagrams/シーケンス図/Start/Init_seq.puml]")), "Index displays relative path");
        foreach (var path in Links(body)) Exists(dir, path);
        var modelCount = exporter.ModelCount;
        Check(body == exporter.Export(root) && exporter.ModelCount == modelCount && exporter.DiagramCount == 5, "Reusable exporter");
        var one = new MarkdownExporter(new MarkdownExportOptions(), Path.Combine(temp, "single"), rules);
        Check(Links(one.Export(first)).Single() == "diagrams/シーケンス図/Start/Init_seq.puml", "Single diagram keeps group");
        var groupExport = new MarkdownExporter(new MarkdownExportOptions(), Path.Combine(temp, "subgroup"), rules);
        Check(Links(groupExport.Export(nested)).Single() == "diagrams/シーケンス図/Start/Nested/Child/Run_seq.puml", "Nested selection keeps outer group");

        // Duplicate group names under different documents, including sanitization collisions.
        var other = Model("other", "OtherDocument", root, "Example.Document");
        var collision = Model("collision", "Start", other, "Example.SequenceGroup");
        Diagram("third", "Init", collision, "sequence");
        var slash = Model("slash", "A/B", upper, "Example.SequenceGroup");
        var backslash = Model("backslash", "A\\B", upper, "Example.SequenceGroup");
        Diagram("slash-diagram", "Same", slash, "sequence");
        Diagram("backslash-diagram", "Same", backslash, "sequence");
        Diagram("duplicate", "Init", a, "sequence");
        var multi = new ISequenceDiagram { Id = "another-view", Model = first }; first.Editors.Add(multi);
        body = exporter.Export(root);
        var paths = Links(body);
        Check(paths.Length == 10 && paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 10, "No colliding output");
        Check(paths.Count(s => s.Contains("/Start_")) == 5 && paths.Count(s => s.Contains("/A_B_")) == 2, "Disambiguated directories");
        foreach (var path in paths) Exists(dir, path);
        root.Children.Reverse(); upper.Children.Reverse(); a.Children.Reverse(); first.Editors.Reverse();
        Check(paths.OrderBy(s => s).SequenceEqual(Links(exporter.Export(root)).OrderBy(s => s)), "Enumeration-independent allocation");

        Check(DiagramPaths.Segment("CON") == "_CON" && DiagramPaths.Segment("lpt1.txt") == "_lpt1.txt", "Reserved names");
        Check(DiagramPaths.Segment("...") == "unnamed" && DiagramPaths.Segment("end. ") == "end", "Empty and trailing names");
        Check(DiagramPaths.Segment("A/B\\C") == "A_B_C", "Do not inject hierarchy");
        var pathRoot = new DiagramPathNode { Assigned = "diagrams" };
        var directoryCollision = DiagramPaths.Directory(pathRoot, "dir-id", "same_seq.puml");
        var fileCollision = new DiagramPathNode { Id = "file-id", Name = "SAME", Suffix = "_seq.puml", IsFile = true, Parent = pathRoot };
        pathRoot.Children.Add(fileCollision);
        var reservedCandidate = "SAME_" + DiagramPaths.Hash("file:file-id").Substring(0, 8) + "_seq.puml";
        DiagramPaths.Directory(pathRoot, "reserved-id", reservedCandidate);
        DiagramPaths.Allocate(pathRoot);
        Check(pathRoot.Children.Select(n => n.Assigned).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3, "Files and folders share collision namespace");
        Check(fileCollision.Assigned != reservedCandidate && directoryCollision.Assigned != "same_seq.puml", "Hash candidates cannot overwrite literal names");
        var special = Diagram("special", "[日本語](x)#%", b, "sequence");
        var specialBody = one.Export(special);
        Check(!specialBody.Contains("ND_DIAGRAM_") && specialBody.Contains("%23%25") && specialBody.Contains("\\[日本語\\]"), "Encoded links and escaped labels");
        Exists(Path.Combine(temp, "single"), Links(specialBody).Single());

        var fallback = new MarkdownExporter(new MarkdownExportOptions(), Path.Combine(temp, "fallback"));
        Check(Links(fallback.Export(b.Children.First(c => c.Id == "second"))).Single() == "diagrams/シーケンス図/Communication/Init_seq.puml" && fallback.Warnings.Count > 0, "No-group single fallback");
        Check(Links(fallback.Export(b)).All(s => s.StartsWith("diagrams/シーケンス図/Communication/")), "No-group selected-root fallback");
        var warnings = new List<string>();
        var cycle = Model("cycle", "Cycle", null, "Example.Other"); cycle.Owner = cycle;
        var broken = Diagram("broken", "Broken", cycle, "sequence");
        Check(rules.Directories(broken, "sequence", warnings).Count == 1 && warnings.Count > 0, "Cyclic ancestry fallback");
        warnings.Clear(); broken.ThrowOwner = true;
        Check(rules.Directories(broken, "sequence", warnings).Count == 0 && warnings.Count > 0, "Owner failure fallback");

        // Disk failure for one group leaves other diagrams and removes only the failed references.
        var failureDir = Path.Combine(temp, "failure");
        Directory.CreateDirectory(Path.Combine(failureDir, "diagrams/シーケンス図"));
        File.WriteAllText(Path.Combine(failureDir, "diagrams/シーケンス図/Communication"), "block directory creation");
        var failed = new MarkdownExporter(new MarkdownExportOptions(), failureDir, rules);
        var failedBody = failed.Export(root);
        Check(failed.Warnings.Any(w => w.Contains("書込みに失敗")) && failed.DiagramCount > 0, "Continue after write failure");
        Check(!failedBody.Contains("ND_DIAGRAM_") && failed.IndexRows.Count == failed.DiagramCount && Links(failedBody).Length == failed.DiagramCount, "No dangling failed references");
        foreach (var path in Links(failedBody)) Exists(failureDir, path);
        var longName = Diagram("long", new string('x', 300), b, "sequence");
        var longBody = one.Export(longName);
        Check(one.DiagramCount == 0 && !longBody.Contains("- 図:") && one.Warnings.Count > 0, "Overlong path warns without dangling link");
        var empty = Diagram("empty", "Empty", b, "sequence"); ((ISequenceDiagram)empty.Editors[0]).Lifelines.Clear();
        Check(!one.Export(empty).Contains("- 図:") && one.IndexRows.Count == 0 && one.DiagramCount == 0, "Empty diagram and reset");
        var noNodes = Diagram("zero", "Zero", sg, "state"); ((IDiagram)noNodes.Editors[0]).Nodes = 0;
        Check(!one.Export(noNodes).Contains("- 図:") && one.DiagramCount == 0, "Unsupported state diagram");

        var app = new IApplication();
        var commands = new ExportCommandsHarness();
        var artifactsDir = Path.Combine(temp, "single");
        commands.WriteDesignArtifacts(app, "test", one, special, artifactsDir);
        Check(File.Exists(Path.Combine(artifactsDir, "design.md")) && File.ReadAllText(Path.Combine(artifactsDir, "_index.md")).Contains(".puml"), "Written design and index artifacts");
        commands.WriteDesignArtifacts(app, "test", one, empty, artifactsDir);
        Check(!File.ReadAllText(Path.Combine(artifactsDir, "_index.md")).Contains(".puml"), "Zero diagrams replaces stale index");
        Check(!File.ReadAllText(Path.Combine(artifactsDir, "design.md")).Contains("- 図:"), "Zero diagrams replaces old body");
        Check(File.Exists(Path.Combine(artifactsDir, "diagrams/シーケンス図/Communication/[日本語](x)#%_seq.puml")), "Existing files are not deleted");

        Console.WriteLine("PASS: " + _checks + " export assertions (fake SDK; real runtime still requires verification).");
        ViewerTests.Run(temp);
        InputTests.Run(temp);
    }
}
