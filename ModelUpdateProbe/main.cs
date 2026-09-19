// ModelUpdateProbe 0.3.0 — v3.x. Update success on the real runtime is unverified.
using NextDesign.Core;
using NextDesign.Desktop;
using NextDesign.Extension;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public void EditMessage(ICommandContext context, ICommandParams parameters) { ProbeHost.EditMessage(context.App); }
public void PrepareProbe(ICommandContext context, ICommandParams parameters) { ProbeHost.Prepare(context.App); }
public void PrepareSequenceProbe(ICommandContext context, ICommandParams parameters) { ProbeHost.PrepareSequence(context.App); }
public void OpenProbeTargets(ICommandContext context, ICommandParams parameters) { ProbeHost.OpenTargets(context.App); }
public void ExecuteProbe(ICommandContext context, ICommandParams parameters) { ProbeHost.Execute(context.App); }
public void OpenProbeConfig(ICommandContext context, ICommandParams parameters) { ProbeHost.OpenConfig(context.App); }
public void CompareAfter(ICommandContext context, ICommandParams parameters) { ProbeHost.Compare(context.App, "変更後"); }
public void CompareUndo(ICommandContext context, ICommandParams parameters) { ProbeHost.Compare(context.App, "Undo後"); }
public void CompareRedo(ICommandContext context, ICommandParams parameters) { ProbeHost.Compare(context.App, "Redo後"); }
public void ShowProbeResult(ICommandContext context, ICommandParams parameters) { ProbeHost.Show(context.App); }
public void ShowProbeDetails(ICommandContext context, ICommandParams parameters) { ProbeHost.Details(context.App); }
public void ShowProbeFields(ICommandContext context, ICommandParams parameters) { ProbeHost.Fields(context.App); }

public static class ProbeHost
{
    public const string Version = "0.3.0";
    public const string Title = "設計更新検証 / " + Version;
    public static ProbeSession Session;
    public static string LastSummary = "検証準備を実行してください。";
    public static string LastDetail = "";

    public static void Prepare(IApplication app)
    {
        try
        {
            Session = null;
            var project = app.Workspace.CurrentProject;
            var model = app.Workspace.CurrentModel;
            if (project == null || model == null) throw new InvalidOperationException("モデルを選択してください。");
            if (!app.Window.UI.ShowConfirmDialog("実プロジェクトのコピーを開いていますか？\n選択モデルだけを検証対象に登録します。\n自動保存・自動復元は行いません。", Title)) return;
            var folder = app.Window.UI.ShowSelectFolderDialog("会社PC内の検証結果の保存先");
            if (string.IsNullOrEmpty(folder)) return;
            var session = new ProbeSession(project, model, Path.Combine(folder, "probe_" + ProbeCore.NewId()));
            Directory.CreateDirectory(session.DirectoryPath);
            var fields = model.Metaclass.GetFields().Cast<IField>().ToList();
            var eligible = fields.Where(Eligible).ToList();
            session.CandidateCount = eligible.Count;
            session.FieldSummary = "準備時のフィールド診断（現在の再取得ではありません）\n"
                + "全フィールド数: " + fields.Count
                + "\n型名がString: " + fields.Count(f => f != null && f.Type == "String")
                + "\nうち上限多重度1: " + fields.Count(f => f != null && f.Type == "String" && f.UpperBound == 1)
                + "\n型名がRichText: " + fields.Count(f => f != null && f.Type == "RichText")
                + "\n更新候補: " + eligible.Count
                + "\nモデル名・フィールド名・値は表示していません。\n詳細は保存先のfields.txtにあります。";
            var report = new StringBuilder("ModelUpdateProbe " + Version + "\r\nモデルID: " + model.Id + "\r\nモデル名: " + model.Name + "\r\n");
            report.AppendLine("候補は単値のString属性のみ。APIでの書き込み可否は実行時に確認します。");
            foreach (var field in fields)
                report.AppendLine(field == null ? "<null field>" : field.Name + " | type=" + field.Type + " | upper=" + field.UpperBound
                    + " | embedded=" + field.IsEmbedded + " | reference=" + field.IsReference
                    + " | classType=" + (field.TypeClass != null) + " | enumType=" + (field.TypeEnum != null) + " | candidate=" + Eligible(field));
            ProbeCore.WriteNew(Path.Combine(session.DirectoryPath, "fields.txt"), report.ToString());
            var config = new Dictionary<string, string> {
                {"schemaVersion", "1"}, {"caseId", "S001"}, {"targetModelId", model.Id.ToString()},
                {"fieldName", eligible.Count == 0 ? "" : eligible[0].Name}, {"newValue", "PROBE_TEST_001"},
                {"ndVersion", "未確認（会社PCのヘルプで確認）"}, {"profile", "未確認"}, {"profileVersion", "未確認"}
            };
            ProbeCore.WriteNew(Path.Combine(session.DirectoryPath, "case.json"), ProbeJson.Write(config));
            Session = session;
            LastDetail = report + "\r\n検証ファイル: " + Path.Combine(session.DirectoryPath, "case.json");
            LastSummary = Title + "\n準備完了 / 対象1モデル\n文字列フィールド候補: " + eligible.Count + "\nモデル変更: なし\n実機版・プロファイル: case.json に手入力\ncase.json の fieldName と newValue を編集してください。\n詳細ボタンで保存先を確認できます。";
            if (eligible.Count == 0)
                LastSummary = Title + "\n診断完了 / 更新対象なし（F001）\n文字列フィールド候補: 0\nモデル変更: なし\n検証実行には進まず「フィールド診断」を押してください。\nこのモデルに属性がない、という判定ではありません。";
            Show(app);
        }
        catch (Exception ex) { Failure(app, "準備", ex); }
    }

    public static bool Eligible(IField field)
    {
        return field != null && field.Type == "String" && field.UpperBound == 1
            && !field.IsReference && field.TypeClass == null && field.TypeEnum == null;
    }

    public static void PrepareSequence(IApplication app) { PrepareSequence(app, true); }
    public static void PrepareSequence(IApplication app, bool showSummary)
    {
        try
        {
            Session = null;
            var project = app.Workspace.CurrentProject;
            var interaction = app.Workspace.CurrentModel as IInteraction;
            if (project == null || interaction == null)
                throw new ProbeCheckException("S001", "シーケンス図のモデルをナビゲータで選択して再準備してください。\n選択モデルをIInteractionとして取得できませんでした。");
            if (interaction.IsDeleted || interaction.IsProxy)
                throw new ProbeCheckException("S002", "選択した相互作用モデルは削除済み、またはプロキシです。");
            if (!app.Window.UI.ShowConfirmDialog("実プロジェクトのコピーを開いていますか？\n図内のメッセージとライフラインの一覧を保存します。\nこの操作ではモデルを変更しません。", Title)) return;
            var folder = app.Window.UI.ShowSelectFolderDialog("会社PC内の検証結果の保存先");
            if (string.IsNullOrEmpty(folder)) return;
            var session = new ProbeSession(project, interaction, Path.Combine(folder, "probe_" + ProbeCore.NewId()));
            session.Sequence = true;
            var messages = interaction.Messages.Cast<IModel>().ToList();
            var lifelines = interaction.Lifelines.Cast<IModel>().ToList();
            var report = new StringBuilder("ModelUpdateProbe " + Version + "\r\nシーケンス内の更新候補\r\n");
            report.AppendLine("相互作用ID: " + interaction.Id);
            report.AppendLine("メッセージの並びは作成順。図の上からの順序ではありません。IDと名称・値で照合してください。");
            report.AppendLine("1件の候補からtargetModelIdとfieldNameをcase.jsonへ転記し、newValueを指定します。");
            report.AppendLine("文字列属性の変更が図のラベルに反映されるかは実機で別途確認します。");
            foreach (var message in messages)
            {
                report.AppendLine("\r\nメッセージ: " + ProbeJson.Quote(message.Name));
                report.AppendLine("targetModelId: " + ProbeJson.Quote(message.Id.ToString()));
                if (message.IsDeleted || message.IsProxy || !message.IsEditable)
                { report.AppendLine("候補外: 削除済み・プロキシ・編集不可のいずれか"); continue; }
                int count = 0;
                foreach (var field in message.Metaclass.GetFields().Cast<IField>())
                {
                    if (field == null) { report.AppendLine("<null field>"); continue; }
                    report.AppendLine("field: " + ProbeJson.Quote(field.Name) + " | type=" + field.Type
                        + " | upper=" + field.UpperBound + " | embedded=" + field.IsEmbedded
                        + " | reference=" + field.IsReference + " | candidate=" + Eligible(field));
                    if (!Eligible(field)) continue;
                    try
                    {
                        var value = message.GetField(field.Name);
                        if (value != null && !(value is string)) { report.AppendLine("読取値がStringでないため候補外"); continue; }
                        report.AppendLine("  fieldName: " + ProbeJson.Quote(field.Name) + " | 現在値: " + ProbeJson.Quote((string)value));
                        count++;
                    }
                    catch (Exception ex) { report.AppendLine("読取失敗: " + ex.GetType().FullName); }
                }
                if (count > 0) session.TargetIds.Add(message.Id.ToString());
                session.CandidateCount += count;
            }
            report.AppendLine("\r\nライフライン（今回の更新対象外・図の左からの順序）");
            foreach (var lifeline in lifelines)
                report.AppendLine(ProbeJson.Quote(lifeline.Id.ToString()) + " | " + ProbeJson.Quote(lifeline.Name));
            session.FieldSummary = "シーケンス準備時の診断\nメッセージ数: " + messages.Count
                + "\nライフライン数: " + lifelines.Count + "\n候補を持つメッセージ数: " + session.TargetIds.Count
                + "\n更新候補（属性数）: " + session.CandidateCount + "\nモデル変更: なし\n「対象一覧を開く」で1件を選んでください。";
            Directory.CreateDirectory(session.DirectoryPath);
            ProbeCore.WriteNew(Path.Combine(session.DirectoryPath, "targets.txt"), report.ToString());
            ProbeCore.WriteNew(Path.Combine(session.DirectoryPath, "case.json"), ProbeJson.Write(new Dictionary<string, string> {
                {"schemaVersion", "1"}, {"caseId", "SEQ001"}, {"targetModelId", ""}, {"fieldName", ""},
                {"newValue", "PROBE_TEST_001"}, {"ndVersion", "未確認"}, {"profile", "未確認"}, {"profileVersion", "未確認"}
            }));
            Session = session;
            LastDetail = report + "\r\n保存先: " + session.DirectoryPath;
            LastSummary = Title + "\n" + session.FieldSummary;
            if (session.CandidateCount == 0) LastSummary += "\n更新対象なし（F001）。検証実行には進まず、この画面を撮影してください。";
            if (showSummary) Show(app);
        }
        catch (Exception ex) { Failure(app, "シーケンス準備", ex); }
    }

    public static void EditMessage(IApplication app)
    {
        try
        {
            PrepareSequence(app, false);
            var session = Session;
            if (session == null) return;
            CheckContext(app, session);
            var options = ((IInteraction)session.Model).Messages.Cast<IModel>()
                .Where(m => session.TargetIds.Contains(m.Id.ToString()) && m.Metaclass.GetFields().Cast<IField>()
                    .Count(f => f != null && f.Name == "Name" && Eligible(f)) == 1).ToList();
            if (options.Count == 0) throw new ProbeCheckException("U001", "名前を変更できるメッセージがありません。");
            var data = new Dictionary<string, string> {{"count", options.Count.ToString(CultureInfo.InvariantCulture)}};
            var ids = new List<string>();
            var before = new List<string>();
            foreach (var model in options)
            {
                var value = model.GetField("Name");
                if (value != null && !(value is string)) throw new InvalidOperationException("名前を文字列として取得できません。");
                data.Add("name" + ids.Count, (string)value ?? "");
                ids.Add(model.Id.ToString()); before.Add((string)value);
            }
            var input = Path.Combine(session.DirectoryPath, "dialog-input.json");
            var output = Path.Combine(session.DirectoryPath, "dialog-result.json");
            var script = Path.Combine(session.DirectoryPath, "message-dialog.ps1");
            ProbeCore.WriteNew(input, ProbeJson.Write(data));
            ProbeCore.WriteNew(script, ProbeDialog.Script);
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            var start = new ProcessStartInfo(executable, "-NoProfile -STA -File " + ProbeDialog.Argument(script)
                + " -InputPath " + ProbeDialog.Argument(input) + " -OutputPath " + ProbeDialog.Argument(output))
                {UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden};
            using (var process = Process.Start(start))
            {
                process.WaitForExit();
                if (process.ExitCode != 0) throw new ProbeCheckException("U002", "入力画面を起動できませんでした。会社PCのPowerShell実行制限などを確認してください。設定変更は不要です。この画面を渡してください。");
            }
            if (!File.Exists(output)) { LastSummary = Title + "\nキャンセルしました。モデル変更: なし"; Show(app); return; }
            if (new FileInfo(output).Length > 1048576) throw new FormatException("入力結果が大きすぎます。");
            var config = ProbeDialog.Selection(File.ReadAllText(output, Encoding.UTF8), ids);
            CheckContext(app, session);
            int index = ids.IndexOf(config["targetModelId"]);
            if (!string.Equals(Read(session, ProbeCase.Parse(ProbeJson.Write(config))), before[index], StringComparison.Ordinal))
                throw new ProbeCheckException("U003", "入力画面を開いている間に対象の名前が変わりました。もう一度選び直してください。");
            var configPath = Path.Combine(session.DirectoryPath, "dialog-case.json");
            ProbeCore.WriteNew(configPath, ProbeJson.Write(config));
            session.GuiCasePath = configPath;
            Execute(app);
        }
        catch (Exception ex) { Failure(app, "メッセージの変更", ex); }
    }

    public static IModel Target(ProbeSession session, ProbeCase config)
    {
        IModel model = session.Model;
        if (session.Sequence)
        {
            if (!session.TargetIds.Contains(config.TargetModelId))
                throw new ProbeCheckException("S003", "準備時のメッセージ候補にないIDです。");
            var matches = ((IInteraction)session.Model).Messages.Cast<IModel>()
                .Where(m => m.Id.ToString() == config.TargetModelId).ToList();
            if (matches.Count != 1) throw new ProbeCheckException("S004", "対象メッセージが元の相互作用内に一意に存在しません。再準備してください。");
            model = matches[0];
        }
        else if (model.Id.ToString() != config.TargetModelId)
            throw new InvalidOperationException("対象IDが準備時のモデルと一致しません。");
        if (model.IsDeleted || model.IsProxy || !model.IsEditable)
            throw new InvalidOperationException("対象は削除済み・プロキシ・編集不可のいずれかです。");
        return model;
    }

    public static void CheckContext(IApplication app, ProbeSession session)
    {
        if (session == null) throw new ProbeCheckException("C001", "準備状態がありません。未準備または状態が失われています。");
        var project = app.Workspace.CurrentProject;
        if (project == null) throw new ProbeCheckException("C002", "現在開いているプロジェクトがありません。");
        if (!string.Equals(project.Id.ToString(), session.ProjectId, StringComparison.Ordinal)
            || !string.Equals(ProjectPath(project), session.ProjectPath, StringComparison.OrdinalIgnoreCase))
            throw new ProbeCheckException("C003", "プロジェクトのIDまたは保存先が準備時と一致しません。再準備してください。");
        var model = project.GetModelById(session.RootModelId);
        if (model == null || model.IsDeleted) throw new ProbeCheckException("C004", "対象モデルが見つからないか、削除済みです。");
        if (model.IsProxy) throw new ProbeCheckException("C005", "対象モデルがプロキシです。");
        if (model.Id.ToString() != session.RootModelId || (session.Sequence && !(model is IInteraction)))
            throw new ProbeCheckException("C007", "再取得した対象のIDまたは種類が一致しません。");
        session.Model = model; // Only the newly resolved root is used by this command.
    }

    public static string ProjectPath(IProject project)
    {
        var path = project.Path;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            throw new ProbeCheckException("C006", "保存先を確認できません。コピーしたプロジェクトを保存して再準備してください。");
        return Path.GetFullPath(path);
    }

    public static string Read(ProbeSession session, ProbeCase config)
    {
        var model = Target(session, config);
        var fields = model.Metaclass.GetFields().Cast<IField>().Where(f => f != null && f.Name == config.FieldName).ToList();
        if (fields.Count != 1 || !Eligible(fields[0]))
            throw new InvalidOperationException("単値String属性を一意に特定できません。");
        var value = model.GetField(config.FieldName);
        if (value != null && !(value is string)) throw new InvalidOperationException("読戻し値が文字列ではありません。");
        return (string)value;
    }

    public static void Execute(IApplication app)
    {
        ProbeRun run = null;
        try
        {
            var session = Session;
            CheckContext(app, session);
            if (session.CandidateCount == 0)
                throw new ProbeCheckException("F001", "準備時に更新候補がありません。「フィールド診断」を確認してください。");
            if (session.Run != null && session.Run.Invoked)
                throw new InvalidOperationException("このセッションは実行済みです。Undo/Redoの照合を終え、検証準備をやり直してください。");
            run = new ProbeRun();
            session.Run = run;
            var casePath = session.GuiCasePath ?? Path.Combine(session.DirectoryPath, "case.json");
            if (new FileInfo(casePath).Length > 1048576) throw new FormatException("検証ファイルは1MB以内です。");
            run.Config = ProbeCase.Parse(File.ReadAllText(casePath, Encoding.UTF8));
            run.Before = Read(session, run.Config);
            run.BeforeKnown = true;
            if (string.Equals(run.Before, run.Config.NewValue, StringComparison.Ordinal))
                throw new InvalidOperationException("変更前と変更後が同じです。別の値を指定してください。");
            run.Precheck = "合格";
            var confirmation = "IModel.SetField を1回実行します。\n対象: " + Target(session, run.Config).Name
                + "\nフィールド: " + run.Config.FieldName + "\n変更前: " + ProbeCore.Preview(run.Before)
                + "\n変更後: " + ProbeCore.Preview(run.Config.NewValue) + "\n\n続行しますか？";
            if (!app.Window.UI.ShowConfirmDialog(confirmation, Title))
            {
                run.Call = "キャンセル（未呼出）";
                SaveAndShow(app, session, run, "キャンセル");
                return;
            }
            // Write-ahead record is mandatory. Failure here must never invoke SetField.
            ProbeCore.Perform(run,
                delegate { CheckContext(app, session); return Read(session, run.Config); },
                delegate { Save(session, run, "呼出前"); },
                delegate(string value) { Target(session, run.Config).SetField(run.Config.FieldName, value); });
            SaveAndShow(app, session, run, "変更後");
        }
        catch (Exception ex)
        {
            if (run != null && Session != null)
            {
                run.Error = ex.GetType().FullName;
                run.ErrorMessage = ex.Message;
                var check = ex as ProbeCheckException;
                if (check != null) run.CheckCode = check.Code;
                if (!run.Invoked) run.Call = "未呼出";
                SaveAndShow(app, Session, run, "停止");
            }
            else Failure(app, "実行前", ex);
        }
    }

    public static void Compare(IApplication app, string phase)
    {
        try
        {
            var session = Session;
            CheckContext(app, session);
            if (session.Run == null || !session.Run.Invoked) throw new InvalidOperationException("照合する呼出記録がありません。");
            var run = session.Run;
            run.ObservationError = "";
            run.ObservationMessage = "";
            run.CurrentKnown = false;
            try { run.Current = Read(session, run.Config); run.CurrentKnown = true; }
            catch (Exception ex) { run.ObservationError = ex.GetType().FullName; run.ObservationMessage = ex.Message; }
            SaveAndShow(app, session, run, phase);
        }
        catch (Exception ex) { Failure(app, phase, ex); }
    }

    public static void Save(ProbeSession session, ProbeRun run, string phase)
    {
        var record = run.Record(phase);
        record.Add("extensionVersion", Version);
        record.Add("sessionId", Path.GetFileName(session.DirectoryPath));
        record.Add("scope", session.Sequence ? "interaction message" : "selected model");
        record.Add("rootModelId", session.RootModelId);
        record.Add("projectId", session.ProjectId);
        record.Add("projectPath", session.ProjectPath);
        ProbeCore.WriteNew(Path.Combine(session.DirectoryPath, run.Id + "_" + ProbeCore.NewId() + ".json"), ProbeJson.Write(record));
    }

    public static void SaveAndShow(IApplication app, ProbeSession session, ProbeRun run, string phase)
    {
        run.LogError = "";
        try { Save(session, run, phase); }
        catch (Exception ex) { run.LogError = ex.GetType().FullName; }
        LastSummary = Title + "\n" + run.Summary(phase);
        LastDetail = ProbeJson.Write(run.Record(phase)) + "\r\n保存先: " + session.DirectoryPath;
        Show(app);
    }

    public static void Show(IApplication app) { app.Window.UI.ShowInformationDialog(LastSummary, Title); }
    public static void Fields(IApplication app)
    {
        // This reads the saved diagnostic, even when the current SDK context differs.
        // Never use this command to bypass CheckContext for model access or mutation.
        if (Session == null) { Failure(app, "フィールド診断", new ProbeCheckException("C001", "準備状態がありません。")); return; }
        app.Window.UI.ShowInformationDialog(Title + "\n" + Session.FieldSummary, Title);
    }
    public static void OpenConfig(IApplication app)
    {
        try
        {
            if (Session == null) throw new ProbeCheckException("C001", "準備状態がありません。");
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Path.Combine(Session.DirectoryPath, "case.json") + "\"") { UseShellExecute = false });
        }
        catch (Exception ex) { Failure(app, "検証ファイル", ex); }
    }
    public static void OpenTargets(IApplication app)
    {
        try
        {
            if (Session == null) throw new ProbeCheckException("C001", "準備状態がありません。");
            var name = Session.Sequence ? "targets.txt" : "fields.txt";
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Path.Combine(Session.DirectoryPath, name) + "\"") { UseShellExecute = false });
        }
        catch (Exception ex) { Failure(app, "対象一覧", ex); }
    }
    public static void Details(IApplication app)
    {
        try
        {
            if (Session == null) { app.Window.UI.ShowInformationDialog(LastDetail, Title + " 詳細"); return; }
            var file = Path.Combine(Session.DirectoryPath, "detail_" + ProbeCore.NewId() + ".txt");
            ProbeCore.WriteNew(file, LastDetail);
            Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = false });
        }
        catch (Exception ex) { Failure(app, "詳細表示", ex); }
    }
    public static void Failure(IApplication app, string phase, Exception ex)
    {
        LastSummary = Title + "\n段階: " + phase + "\n処理停止\n例外型: " + ex.GetType().FullName + "\n詳細ボタンで理由を確認してください。";
        var check = ex as ProbeCheckException;
        if (check != null)
            LastSummary = Title + "\n段階: " + phase + "\n処理停止 / 診断コード: " + check.Code + "\n" + check.Message
                + "\nこのコマンドによる更新API呼出: なし\nこの画面を撮影してください。";
        LastDetail = ex.ToString();
        Show(app);
    }
}

public class ProbeSession
{
    public readonly string ProjectId, ProjectPath, RootModelId;
    public IModel Model;
    public readonly string DirectoryPath;
    public ProbeRun Run;
    public int CandidateCount;
    public string FieldSummary;
    public bool Sequence;
    public string GuiCasePath;
    public readonly HashSet<string> TargetIds = new HashSet<string>(StringComparer.Ordinal);
    public ProbeSession(IProject project, IModel model, string directory)
    {
        ProjectId = project.Id.ToString(); ProjectPath = ProbeHost.ProjectPath(project);
        RootModelId = model.Id.ToString(); Model = model; DirectoryPath = directory;
    }
}

// No ND types below this line: these components are tested without the real SDK.
public class ProbeCheckException : InvalidOperationException
{
    public readonly string Code;
    // Only fixed, non-sensitive messages are passed to this exception.
    public ProbeCheckException(string code, string message) : base(message) { Code = code; }
}

public static class ProbeCore
{
    public static string NewId() { return DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8); }
    public static void WriteNew(string path, string text)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(true))) { writer.Write(text); }
    }
    public static string Preview(string value)
    {
        if (value == null) return "<null>";
        var escaped = value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return (escaped.Length > 100 ? escaped.Substring(0, 100) + "…（表示省略）" : escaped) + " [UTF-16文字数=" + value.Length + "]";
    }
    public static void Perform(ProbeRun run, Func<string> read, Action journal, Action<string> write)
    {
        if (!run.BeforeKnown || run.Config == null) throw new InvalidOperationException("事前検査が未完了です。");
        journal();
        // Re-read immediately before the only mutating call, after dialog and journal I/O.
        if (!string.Equals(read(), run.Before, StringComparison.Ordinal)) throw new InvalidOperationException("確認後に値が変わりました。再準備してください。");
        run.Invoked = true;
        try { write(run.Config.NewValue); run.Call = "正常終了"; }
        catch (Exception ex) { run.Call = "例外"; run.Error = ex.GetType().FullName; run.ErrorMessage = ex.Message; }
        try { run.Current = read(); run.CurrentKnown = true; }
        catch (Exception ex) { run.ObservationError = ex.GetType().FullName; run.ObservationMessage = ex.Message; }
    }
}

public class ProbeCase
{
    public string CaseId, TargetModelId, FieldName, NewValue, NdVersion, Profile, ProfileVersion;
    public static ProbeCase Parse(string text)
    {
        var map = ProbeJson.Parse(text);
        string[] keys = {"schemaVersion", "caseId", "targetModelId", "fieldName", "newValue", "ndVersion", "profile", "profileVersion"};
        if (map.Count != keys.Length || keys.Any(k => !map.ContainsKey(k) || map[k] == null)) throw new FormatException("検証ファイルの項目不足・未知の項目・nullがあります。");
        if (map["schemaVersion"] != "1") throw new FormatException("schemaVersionは文字列の1のみ対応します。");
        if (!Regex.IsMatch(map["caseId"], @"\A[A-Za-z0-9_-]{1,32}\z")) throw new FormatException("caseIdは英数字、ハイフン、アンダースコアの1〜32文字です。");
        if (string.IsNullOrWhiteSpace(map["targetModelId"]) || string.IsNullOrWhiteSpace(map["fieldName"])) throw new FormatException("対象IDとフィールド名は必須です。");
        return new ProbeCase {CaseId=map["caseId"], TargetModelId=map["targetModelId"], FieldName=map["fieldName"], NewValue=map["newValue"], NdVersion=map["ndVersion"], Profile=map["profile"], ProfileVersion=map["profileVersion"]};
    }
}

public class ProbeRun
{
    public readonly string Id = ProbeCore.NewId();
    public ProbeCase Config;
    public string Before, Current;
    public bool BeforeKnown, CurrentKnown, Invoked;
    public string Precheck = "未完了", Call = "未呼出", Error = "", ErrorMessage = "", ObservationError = "", ObservationMessage = "", LogError = "";
    public string CheckCode = "";
    public string Match(string phase)
    {
        if (!CurrentKnown || !BeforeKnown || Config == null) return "未確認";
        return string.Equals(Current, phase == "Undo後" ? Before : Config.NewValue, StringComparison.Ordinal) ? "一致" : "不一致";
    }
    public string Summary(string phase)
    {
        return "実行: " + Id + "\nケース: " + (Config == null ? "未読込" : Config.CaseId)
            + " / 段階: " + phase + "\n日時: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")
            + "\nAPI: IModel.SetField / 事前検査: " + Precheck + "\n呼出: " + Call
            + " / 期待値との照合: " + Match(phase)
            + "\n変更前との差: " + (!CurrentKnown || !BeforeKnown ? "未確認" : string.Equals(Before, Current, StringComparison.Ordinal) ? "なし" : "あり")
            + "\n例外型: " + (Error.Length == 0 ? "なし" : Error)
            + (CheckCode.Length == 0 ? "" : " / 診断: " + CheckCode)
            + "\n読戻し例外: " + (ObservationError.Length == 0 ? "なし" : ObservationError)
            + "\n記録: " + (LogError.Length == 0 ? "保存済み" : "保存失敗 " + LogError)
            + "\n対象フィールドのみ照合。Undo/Redo実行はユーザー申告。\nモデル名・パス・値・環境情報は詳細に保存。";
    }
    public Dictionary<string, string> Record(string phase)
    {
        var map = new Dictionary<string, string> {
            {"runId", Id}, {"timestamp", DateTimeOffset.Now.ToString("o")}, {"phase", phase},
            {"api", "IModel.SetField"}, {"precheck", Precheck}, {"invoked", Invoked.ToString()}, {"call", Call}, {"checkCode", CheckCode},
            {"beforeKnown", BeforeKnown.ToString()}, {"before", Before}, {"currentKnown", CurrentKnown.ToString()}, {"current", Current},
            {"match", Match(phase)}, {"exceptionType", Error}, {"exceptionMessage", ErrorMessage},
            {"readExceptionType", ObservationError}, {"readExceptionMessage", ObservationMessage}, {"logError", LogError},
            {"observationScope", "target field only; phase selected by user; no undo/redo invocation detection"}
        };
        if (Config != null)
        {
            map.Add("caseId", Config.CaseId); map.Add("targetModelId", Config.TargetModelId); map.Add("fieldName", Config.FieldName);
            map.Add("newValue", Config.NewValue); map.Add("ndVersionUserReported", Config.NdVersion);
            map.Add("profileUserReported", Config.Profile); map.Add("profileVersionUserReported", Config.ProfileVersion);
        }
        return map;
    }
}

// Strict, bounded flat JSON object. The configuration contract uses strings only.
// No dependency on a JSON assembly that the ND script host may not reference.
public static class ProbeJson
{
    public static string Write(IDictionary<string, string> map)
    {
        return "{\r\n" + string.Join(",\r\n", map.Select(kv => "  " + Quote(kv.Key) + ": " + Quote(kv.Value))) + "\r\n}\r\n";
    }
    public static string Quote(string value)
    {
        if (value == null) return "null";
        var b = new StringBuilder("\"");
        foreach (char c in value)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < 32 || char.IsSurrogate(c)) b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
    public static Dictionary<string, string> Parse(string text)
    {
        if (text == null || text.Length > 262144) throw new FormatException("検証ファイルは256K文字以内です。");
        var p = new ProbeJsonReader(text);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        p.Require('{');
        if (!p.Take('}'))
        {
            do
            {
                var key = p.String(); p.Require(':'); var value = p.String();
                if (result.ContainsKey(key)) throw new FormatException("JSONのキーが重複しています。");
                result.Add(key, value);
            } while (p.Take(','));
            p.Require('}');
        }
        if (!p.End()) throw new FormatException("JSONの末尾に余分な内容があります。");
        return result;
    }
}
public class ProbeJsonReader
{
    private readonly string text;
    private int index;
    public ProbeJsonReader(string value) { text = value; }
    private void Space() { while (index < text.Length && " \t\r\n".IndexOf(text[index]) >= 0) index++; }
    public bool End() { Space(); return index == text.Length; }
    public bool Take(char c) { Space(); if (index < text.Length && text[index] == c) { index++; return true; } return false; }
    public void Require(char c) { if (!Take(c)) throw new FormatException("JSON構文エラー（位置 " + index + "）。"); }
    public string String()
    {
        Require('"'); var b = new StringBuilder();
        while (index < text.Length)
        {
            char c = text[index++];
            if (c == '"') return b.ToString();
            if (c < 32) throw new FormatException("JSON文字列内の改行等はエスケープしてください。");
            if (c != '\\') { b.Append(c); continue; }
            if (index == text.Length) break;
            c = text[index++];
            if (c == '"' || c == '\\' || c == '/') b.Append(c);
            else if (c == 'n') b.Append('\n'); else if (c == 'r') b.Append('\r'); else if (c == 't') b.Append('\t');
            else if (c == 'b') b.Append('\b'); else if (c == 'f') b.Append('\f');
            else if (c == 'u')
            {
                int code;
                if (index + 4 > text.Length || !int.TryParse(text.Substring(index, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw new FormatException("Unicodeエスケープが不正です。");
                b.Append((char)code); index += 4;
            }
            else throw new FormatException("JSONエスケープが不正です。");
        }
        throw new FormatException("JSON文字列が閉じていません。");
    }
}

public static class ProbeDialog
{
    public static string Argument(string path)
    {
        if (path.IndexOf('"') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0) throw new FormatException("Invalid path");
        return "\"" + path + "\"";
    }
    public static Dictionary<string, string> Selection(string text, IList<string> ids)
    {
        var result = ProbeJson.Parse(text);
        int index;
        if (result.Count != 2 || !result.ContainsKey("index") || !result.ContainsKey("value")
            || !int.TryParse(result["index"], NumberStyles.None, CultureInfo.InvariantCulture, out index)
            || index < 0 || index >= ids.Count || result["value"].Length > 4000)
            throw new FormatException("入力結果が不正です。");
        return new Dictionary<string, string> {
            {"schemaVersion", "1"}, {"caseId", "SEQ001"}, {"targetModelId", ids[index]},
            {"fieldName", "Name"}, {"newValue", result["value"]}, {"ndVersion", "未確認"},
            {"profile", "未確認"}, {"profileVersion", "未確認"}
        };
    }
    // Generated from tools/message-dialog.ps1; never interpolate model values into this script.
    public const string Script = @"param([Parameter(Mandatory=$true)][string]$InputPath, [Parameter(Mandatory=$true)][string]$OutputPath, [switch]$SelfTest)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$data = Get-Content -LiteralPath $InputPath -Raw -Encoding UTF8 | ConvertFrom-Json
$form = New-Object System.Windows.Forms.Form
$form.Text = 'メッセージの名前を変更'
$form.ClientSize = New-Object System.Drawing.Size(760, 550)
$form.MinimumSize = New-Object System.Drawing.Size(640, 500)
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Yu Gothic UI', 11)
$form.AutoScaleMode = 'Dpi'
$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = 'Fill'; $layout.Padding = 16; $layout.ColumnCount = 1; $layout.RowCount = 6
foreach ($height in @(32, 0, 30, 76, 54, 46)) {
 $style = New-Object System.Windows.Forms.RowStyle
 if ($height -eq 0) { $style.SizeType = 'Percent'; $style.Height = 100 } else { $style.SizeType = 'Absolute'; $style.Height = $height }
 [void]$layout.RowStyles.Add($style)
}
$form.Controls.Add($layout)
$label = New-Object System.Windows.Forms.Label
$label.Text = '1. 変更するメッセージを選ぶ'; $label.Dock = 'Fill'
$layout.Controls.Add($label,0,0)
$list = New-Object System.Windows.Forms.ListBox
$list.Dock = 'Fill'; $list.IntegralHeight = $false; $list.HorizontalScrollbar = $true
for ($i=0; $i -lt [int]$data.count; $i++) { [void]$list.Items.Add(('{0}. {1}' -f ($i+1), [string]$data.""name$i"")) }
$layout.Controls.Add($list,0,1)
$label2 = New-Object System.Windows.Forms.Label
$label2.Text = '2. 変更後の名前を入力する'; $label2.Dock = 'Fill'
$layout.Controls.Add($label2,0,2)
$value = New-Object System.Windows.Forms.TextBox
$value.Dock = 'Fill'; $value.Multiline = $true; $value.ScrollBars = 'Vertical'; $value.MaxLength = 4000
$layout.Controls.Add($value,0,3)
$hint = New-Object System.Windows.Forms.Label
$hint.Text = '名前を変更した後、図上の表示も確認してください。次の確認画面でOKを押すまでは変更されません。'
$hint.Dock = 'Fill'; $layout.Controls.Add($hint,0,4)
$buttons = New-Object System.Windows.Forms.FlowLayoutPanel
$buttons.Dock = 'Fill'; $buttons.FlowDirection = 'RightToLeft'
$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'キャンセル'; $cancel.AutoSize = $true; $cancel.DialogResult = 'Cancel'
$ok = New-Object System.Windows.Forms.Button
$ok.Text = '変更内容を確認'; $ok.AutoSize = $true; $ok.Enabled = $false
$buttons.Controls.Add($cancel); $buttons.Controls.Add($ok); $layout.Controls.Add($buttons,0,5)
$form.CancelButton = $cancel
$list.Add_SelectedIndexChanged({
 if ($list.SelectedIndex -ge 0) { $value.Text = [string]$data.('name'+$list.SelectedIndex); $ok.Enabled = $false }
})
$value.Add_TextChanged({
 $ok.Enabled = $list.SelectedIndex -ge 0 -and $value.Text -cne [string]$data.('name'+$list.SelectedIndex)
})
$accept = {
 if (-not $ok.Enabled) { return }
 $result = @{ index=[string]$list.SelectedIndex; value=$value.Text } | ConvertTo-Json
 $bytes = [System.Text.UTF8Encoding]::new($true).GetBytes($result)
 $stream = [System.IO.File]::Open($OutputPath, 'CreateNew', 'Write', 'None')
 try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
 $form.DialogResult = 'OK'; $form.Close()
}
$ok.Add_Click($accept)
try {
 if ($SelfTest) {
  if ($ok.Enabled -or $list.SelectedIndex -ne -1) { throw 'Initial selection must be empty' }
  $list.SelectedIndex = 1
  if ($ok.Enabled) { throw 'Unchanged name accepted' }
  $value.Text = 'Changed ""message""'
  if (-not $ok.Enabled) { throw 'Changed name rejected' }
  $form.StartPosition = ""Manual""; $form.Location = New-Object System.Drawing.Point(-30000,-30000)
  $form.Show(); [System.Windows.Forms.Application]::DoEvents()
  $bitmap = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
  try { $form.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0,0,$form.Width,$form.Height))); $bitmap.Save($OutputPath+'.png') } finally { $bitmap.Dispose() }
  & $accept
 } else { [void]$form.ShowDialog() }
} finally { $form.Dispose() }
";
}
