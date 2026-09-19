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
            if (kind == "owner") field.IsEmbedded = true;
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

        var collision = Path.Combine(root,"existing.txt"); File.WriteAllText(collision,"original");
        bool threw = false; try { ProbeCore.WriteNew(collision,"replacement"); } catch (IOException) { threw=true; }
        Check(threw && File.ReadAllText(collision)=="original", "existing evidence overwritten");
        Console.WriteLine("PASS: " + checks + " assertions (fake SDK; real ND untested)");
    }
}
