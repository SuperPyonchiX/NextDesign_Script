using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NextDesign.Core;
using NextDesign.Desktop;

public static class Tests
{
    private static int checks;
    private static string root;
    private static void Check(bool result, string label) { if (!result) throw new Exception(label); checks++; }
    private static void Reject(Action action) { bool rejected = false; try { action(); } catch (FormatException) { rejected = true; } Check(rejected, "invalid JSON accepted"); }
    private static Dictionary<string, string> Config()
    {
        return new Dictionary<string, string> { {"schemaVersion","1"}, {"caseId","S001"}, {"targetModelId","fictional-id"},
            {"fieldName","Description"}, {"newValue","SECRET_AFTER_VALUE"}, {"ndVersion","未確認"}, {"profile","未確認"}, {"profileVersion","未確認"} };
    }
    private static IApplication Setup()
    {
        var app = new IApplication();
        app.Workspace.CurrentProject = new Project(); app.Workspace.CurrentModel = new Model();
        ((Project)app.Workspace.CurrentProject).Models.Add(app.Workspace.CurrentModel.Id, app.Workspace.CurrentModel);
        app.Window.UI.Folder = root;
        ProbeHost.Prepare(app);
        Check(ProbeHost.Session != null, "prepare failed");
        Check(((Model)app.Workspace.CurrentModel).Writes == 0, "prepare mutated model");
        WriteConfig(Config());
        return app;
    }
    private static void WriteConfig(Dictionary<string, string> map) { File.WriteAllText(Path.Combine(ProbeHost.Session.DirectoryPath,"case.json"), ProbeJson.Write(map)); }
    public static void Main(string[] args)
    {
        root = args[0];
        var map = Config(); map["newValue"] = "日本語\n\t\"\\😀";
        Check(ProbeCase.Parse(ProbeJson.Write(map)).NewValue == map["newValue"], "JSON roundtrip");
        map["newValue"] = ""; Check(ProbeCase.Parse(ProbeJson.Write(map)).NewValue == "", "empty string");
        Reject(() => ProbeCase.Parse("{}"));
        Reject(() => ProbeJson.Parse("{\"a\":\"1\",\"a\":\"2\"}"));
        Reject(() => ProbeJson.Parse("{\"a\":\"1\",}"));
        Reject(() => ProbeJson.Parse("{\"a\":true}"));
        Reject(() => ProbeJson.Parse("{\"a\":null}"));
        Reject(() => ProbeJson.Parse("{\"a\":\"\\q\"}"));
        Reject(() => ProbeJson.Parse("{} trailing"));
        Reject(() => ProbeJson.Parse("{\"a\":\"line\nbreak\"}"));
        Reject(() => ProbeJson.Parse(new string(' ', 262145)));
        map = Config(); map["caseId"] = "名前"; Reject(() => ProbeCase.Parse(ProbeJson.Write(map)));
        map = Config(); map["schemaVersion"] = "2"; Reject(() => ProbeCase.Parse(ProbeJson.Write(map)));
        map = Config(); map["extra"] = "x"; Reject(() => ProbeCase.Parse(ProbeJson.Write(map)));

        var app = Setup(); var model = (Model)app.Workspace.CurrentModel;
        ProbeHost.Execute(app);
        Check(model.Writes == 1 && model.Value == "SECRET_AFTER_VALUE", "single mutation");
        Check(ProbeHost.Session.Run.Match("変更後") == "一致", "after mismatch");
        var summary = app.Window.UI.Last;
        foreach (var secret in new[] {model.Name, model.Id, "Description", "SECRET_BEFORE_VALUE", model.Value, root})
            Check(!summary.Contains(secret), "summary leaked " + secret);
        ProbeHost.Compare(app, "Undo後"); Check(ProbeHost.Session.Run.Match("Undo後") == "不一致", "false undo accepted");
        model.Value = "SECRET_BEFORE_VALUE"; ProbeHost.Compare(app, "Undo後");
        Check(ProbeHost.Session.Run.Match("Undo後") == "一致", "undo compare");
        model.Value = "SECRET_AFTER_VALUE"; ProbeHost.Compare(app, "Redo後");
        Check(ProbeHost.Session.Run.Match("Redo後") == "一致", "redo compare");
        Check(model.Writes == 1, "compare performed a mutation");
        ProbeHost.Execute(app); Check(model.Writes == 1, "repeated execute allowed");

        app = Setup(); model = (Model)app.Workspace.CurrentModel;
        app.Window.UI.Confirm = false; ProbeHost.Execute(app); Check(model.Writes == 0, "cancel mutated");
        app.Window.UI.Confirm = true; app.Window.UI.OnConfirm = () => model.Value = "external edit";
        ProbeHost.Execute(app); Check(model.Writes == 0, "stale before value overwritten");
        app.Window.UI.OnConfirm = null;

        foreach (var kind in new[] {"wrongId", "missing", "multi", "rich", "owner", "reference", "readonly", "proxy", "deleted", "project", "enum"})
        {
            app = Setup(); model = (Model)app.Workspace.CurrentModel; map = Config();
            var field = model.Metaclass.Fields[0];
            if (kind == "wrongId") map["targetModelId"] = "other";
            if (kind == "missing") map["fieldName"] = "missing";
            if (kind == "multi") field.UpperBound = -1;
            if (kind == "rich") field.Type = "RichText";
            if (kind == "owner") { field.IsEmbedded = true; field.TypeClass = new object(); }
            if (kind == "reference") field.IsReference = true;
            if (kind == "readonly") model.IsEditable = false;
            if (kind == "proxy") model.IsProxy = true;
            if (kind == "deleted") model.IsDeleted = true;
            if (kind == "project") app.Workspace.CurrentProject = new Project();
            if (kind == "enum") field.TypeEnum = new object();
            WriteConfig(map); ProbeHost.Execute(app); Check(model.Writes == 0, kind + " was mutated");
        }
        app = Setup(); model = (Model)app.Workspace.CurrentModel;
        model.ThrowAfterWrite = true; ProbeHost.Execute(app);
        Check(ProbeHost.Session.Run.Call == "例外" && ProbeHost.Session.Run.Match("変更後") == "一致", "partial failure not observed");
        Check(model.Writes == 1, "auto rollback occurred");
        model.FailRead = true; ProbeHost.Compare(app, "Undo後");
        Check(ProbeHost.Session.Run.Match("Undo後") == "未確認", "stale current on read failure");

        // Log failure after confirmation: move the session directory away. No SetField permitted.
        app = Setup(); model = (Model)app.Workspace.CurrentModel;
        var dir = ProbeHost.Session.DirectoryPath;
        app.Window.UI.OnConfirm = () => Directory.Move(dir, dir + "_moved");
        ProbeHost.Execute(app); Check(model.Writes == 0, "journal failure still mutated");
        Check(app.Window.UI.Last.Contains("保存失敗"), "log failure hidden");

        app = Setup(); model = (Model)app.Workspace.CurrentModel;
        dir = ProbeHost.Session.DirectoryPath;
        model.OnWrite = () => Directory.Move(dir, dir + "_after_write");
        ProbeHost.Execute(app);
        Check(model.Writes == 1 && ProbeHost.Session.Run.Invoked, "post-write journal failure altered invocation");
        Check(app.Window.UI.Last.Contains("保存失敗") && app.Window.UI.Last.Contains("正常終了"), "post-write failure hidden");

        app = Setup(); model = (Model)app.Workspace.CurrentModel;
        map = Config(); map["newValue"] = model.Value; WriteConfig(map);
        ProbeHost.Execute(app); Check(model.Writes == 0, "no-op case accepted");

        app = Setup(); model = (Model)app.Workspace.CurrentModel;
        model.OnWrite = () => model.FailRead = true;
        ProbeHost.Execute(app);
        Check(ProbeHost.Session.Run.Call == "正常終了" && ProbeHost.Session.Run.Match("変更後") == "未確認", "read failure treated as write failure");

        var run = new ProbeRun { Config = ProbeCase.Parse(ProbeJson.Write(Config())), BeforeKnown = true, Before = null };
        string value = null;
        ProbeCore.Perform(run, () => value, () => {}, v => value = v);
        Check(run.Before == null && run.CurrentKnown && run.Match("変更後") == "一致", "null before collapsed");
        var longA = new string('x', 150) + "A"; var longB = new string('x',150) + "B";
        run.Config.NewValue = longA; run.Current = longB;
        Check(run.Match("変更後") == "不一致", "truncated values compared");

        // The mock exercises independent stopped branches, not assumptions about SDK identity.
        foreach (var code in new[] {"C001", "C002", "C003", "C004", "C005"})
        {
            app = Setup(); model = (Model)app.Workspace.CurrentModel;
            if (code == "C001") ProbeHost.Session = null;
            if (code == "C002") app.Workspace.CurrentProject = null;
            if (code == "C003") app.Workspace.CurrentProject = new Project();
            if (code == "C004") model.IsDeleted = true;
            if (code == "C005") model.IsProxy = true;
            ProbeHost.Execute(app);
            Check(app.Window.UI.Last.Contains(code), "context reason not distinguished: " + code);
            Check(model.Writes == 0, "context check bypassed");
            Check(!app.Window.UI.Last.Contains(model.Name) && !app.Window.UI.Last.Contains(model.Id), "context summary leaked identity");
        }
        app = Setup(); model = (Model)app.Workspace.CurrentModel;
        model.Metaclass.Fields[0].Type = "RichText";
        ProbeHost.Prepare(app);
        Check(app.Window.UI.Last.Contains("更新対象なし") && !app.Window.UI.Last.Contains("準備完了"), "zero candidates reported ready");
        ProbeHost.Execute(app);
        Check(app.Window.UI.Last.Contains("F001") && model.Writes == 0, "zero-candidate execution permitted");
        app.Workspace.CurrentProject = new Project();
        ProbeHost.Fields(app);
        Check(app.Window.UI.Last.Contains("型名がRichText: 1") && app.Window.UI.Last.Contains("更新候補: 0"), "saved field summary unavailable");
        Check(!app.Window.UI.Last.Contains(model.Name) && !app.Window.UI.Last.Contains(model.Id), "field summary leaked identity");
        Check(model.Writes == 0, "field diagnosis mutated model");

        // An owned scalar remains eligible; owned model fields do not.
        app = Setup(); model = (Model)app.Workspace.CurrentModel;
        model.Metaclass.Fields[0].IsEmbedded = true;
        ProbeHost.Prepare(app); WriteConfig(Config());
        Check(ProbeHost.Session.CandidateCount == 1, "embedded scalar excluded");
        ProbeHost.Execute(app); Check(model.Writes == 1, "embedded scalar update blocked");

        foreach (var scenario in new[] {"success", "root", "lifeline", "removed", "deleted", "proxy", "readonly", "added", "changed", "unselected"})
        {
            app = Setup();
            var interaction = new Interaction(); interaction.Metaclass.Fields.Clear();
            var first = new Model {ModelId = "msg-a"};
            var second = new Model {ModelId = "msg-b"};
            var lifeline = new Model {ModelId = "line-a"};
            first.Metaclass.Fields[0].IsEmbedded = true;
            second.Metaclass.Fields[0].IsEmbedded = true;
            interaction.MessageList.Add(first); interaction.MessageList.Add(second);
            interaction.LifelineList.Add(lifeline);
            app.Workspace.CurrentModel = interaction;
            ((Project)app.Workspace.CurrentProject).Models[interaction.Id] = interaction;
            ProbeHost.PrepareSequence(app);
            Check(ProbeHost.Session != null && ProbeHost.Session.Sequence && ProbeHost.Session.CandidateCount == 2, "sequence prepare failed");
            Check(first.Writes + second.Writes + lifeline.Writes + interaction.Writes == 0, "sequence preparation mutated");
            Check(!app.Window.UI.Last.Contains(first.Name) && !app.Window.UI.Last.Contains(first.Id), "sequence summary leaked");
            Check(File.ReadAllText(Path.Combine(ProbeHost.Session.DirectoryPath,"targets.txt")).Contains("msg-b"), "target list missing message");
            map = Config(); map["targetModelId"] = second.Id;
            if (scenario == "root") map["targetModelId"] = interaction.Id;
            if (scenario == "lifeline") map["targetModelId"] = lifeline.Id;
            if (scenario == "removed") interaction.MessageList.Remove(second);
            if (scenario == "deleted") second.IsDeleted = true;
            if (scenario == "proxy") second.IsProxy = true;
            if (scenario == "readonly") second.IsEditable = false;
            if (scenario == "added") { var added = new Model {ModelId="msg-new"}; interaction.MessageList.Add(added); map["targetModelId"] = added.Id; }
            if (scenario == "changed") app.Window.UI.OnConfirm = () => interaction.MessageList.Remove(second);
            if (scenario != "unselected") WriteConfig(map);
            ProbeHost.Execute(app);
            Check(first.Writes + lifeline.Writes + interaction.Writes == 0, "non-target mutated");
            Check(second.Writes == (scenario == "success" ? 1 : 0), "sequence scenario " + scenario);
            if (scenario == "success")
            {
                Check(ProbeHost.Session.Run.Match("変更後") == "一致", "message readback mismatch");
                second.Value = "SECRET_BEFORE_VALUE"; ProbeHost.Compare(app, "Undo後");
                Check(ProbeHost.Session.Run.Match("Undo後") == "一致", "message undo compare");
                second.Value = "SECRET_AFTER_VALUE"; ProbeHost.Compare(app, "Redo後");
                Check(ProbeHost.Session.Run.Match("Redo後") == "一致" && second.Writes == 1, "message redo compare");
            }
        }
        app = Setup(); ProbeHost.PrepareSequence(app);
        Check(ProbeHost.Session == null && app.Window.UI.Last.Contains("S001"), "non-interaction accepted");

        foreach (var scenario in new[] {"wrapper", "path", "id", "missing", "proxy", "deleted", "unsaved", "confirmSwitch"})
        {
            app = Setup(); model = (Model)app.Workspace.CurrentModel;
            var original = (Project)app.Workspace.CurrentProject;
            var fresh = new Model();
            var replacement = new Project {ProjectId=original.Id, FilePath=original.Path};
            replacement.Models.Add(fresh.Id, fresh);
            if (scenario == "path") replacement.FilePath = @"C:\other\copy.nd";
            if (scenario == "id") replacement.ProjectId = "other-project";
            if (scenario == "missing") replacement.Models.Clear();
            if (scenario == "proxy") fresh.IsProxy = true;
            if (scenario == "deleted") fresh.IsDeleted = true;
            if (scenario == "unsaved") replacement.FilePath = null;
            if (scenario == "confirmSwitch") app.Window.UI.OnConfirm = () => replacement.FilePath = @"C:\other\copy.nd";
            app.Workspace.CurrentProject = replacement;
            ProbeHost.Execute(app);
            Check(model.Writes == 0, "stale model wrapper used");
            Check(fresh.Writes == (scenario == "wrapper" ? 1 : 0), "identity scenario " + scenario);
        }

        app = Setup();
        var oldSequence = new Interaction();
        var oldMessage = new Model {ModelId="sequence-message"}; oldSequence.MessageList.Add(oldMessage);
        app.Workspace.CurrentModel = oldSequence;
        var sequenceProject = (Project)app.Workspace.CurrentProject;
        sequenceProject.Models[oldSequence.Id] = oldSequence;
        ProbeHost.PrepareSequence(app);
        var newSequence = new Interaction();
        var newMessage = new Model {ModelId="sequence-message"}; newSequence.MessageList.Add(newMessage);
        var newProject = new Project {ProjectId=sequenceProject.Id, FilePath=sequenceProject.Path};
        newProject.Models.Add(newSequence.Id, newSequence);
        app.Workspace.CurrentProject = newProject;
        map = Config(); map["targetModelId"] = newMessage.Id; WriteConfig(map);
        ProbeHost.Execute(app);
        Check(oldMessage.Writes == 0 && newMessage.Writes == 1, "sequence reused stale wrappers");
        newMessage.Value = "SECRET_BEFORE_VALUE"; ProbeHost.Compare(app, "Undo後");
        Check(ProbeHost.Session.Run.Match("Undo後") == "一致", "fresh sequence undo readback");

        var collision = Path.Combine(root,"existing.txt"); File.WriteAllText(collision,"original");
        bool threw = false; try { ProbeCore.WriteNew(collision,"replacement"); } catch (IOException) { threw=true; }
        Check(threw && File.ReadAllText(collision)=="original", "existing evidence overwritten");
        Console.WriteLine("PASS: " + checks + " assertions (fake SDK; real ND untested)");
    }
}
