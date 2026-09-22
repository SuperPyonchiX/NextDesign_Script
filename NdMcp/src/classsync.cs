// ============================================================
//  Part C / クラス図の PlantUML 同期 API（NdMcp 固有部。src/classsync.cs）
//
//  同期本体は PlantUmlTool/src/60-class-sync.cs と 61-class-sync-runtime.cs を
//  tools/build_main.py が転記する。ここには HTTP 要求と同期本体をつなぐ薄い層と、
//  同期本体が参照する ClassExperiment（リボン版では結果ダイアログ）の代替だけを置く。
//  MCP 経由ではダイアログを出せないため、結果はすべて JSON 応答と診断ファイルに載せる。
// ============================================================

// PlantUmlTool/src/62-class-sync-ui.cs の ClassExperiment と同じ名前・同じメンバ。ダイアログは出さず出力ウィンドウへ書く。
public static class ClassExperiment
{
    public const string Version = "0.7.2";
    public const string Title = "クラス図同期 (NdMcp) / " + Version;
    public static string Summary = "";
    public static string Details = "";
    public static void Show(IApplication app)
    {
        try { app.Output.WriteLine("NdMcp", Title + ": " + Summary); } catch (Exception) { }
    }
    public static void Write(string path, string text)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
    }
    // 診断にはモデル名と ID が含まれる。リボン版と同じフォルダに残し、リポジトリへは入れない。
    public static string SaveReport(string kind, string log, string reportJson, string currentPuml)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextDesign.ClassSync", "reports");
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + kind + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Write(stem + ".txt", log);
            if (reportJson != null) Write(stem + ".json", reportJson);
            if (currentPuml != null) Write(stem + "_current.puml", currentPuml);
            return stem;
        }
        catch (Exception ex)
        {
            Summary += "\n診断ファイルを保存できませんでした: " + ex.Message;
            return null;
        }
    }
}

// HTTP 要求（UI スレッドのコマンド内）からクラス図同期を呼ぶ。
public static class ClassSyncApi
{
    public const int MaxPumlLength = 300000;

    // 対象モデルに紐づくクラス図エディタを選ぶ。editorId 指定があればそれ、無ければ最初のクラス図。
    public static IEditor FindEditor(IApplication app, IModel model, string editorId, out List<object> candidates)
    {
        candidates = new List<object>();
        IEditor found = null;
        IEnumerable<IEditor> editors;
        try { editors = model.GetEditors().Cast<IEditor>().ToList(); }
        catch (Exception ex) { throw new NdMcpHttpError(500, "エディタ一覧を取得できません: " + ex.Message); }
        foreach (var editor in editors)
        {
            string reject = ClassDiagramKind.Reject(editor);
            candidates.Add(new JsonObject()
                .Set("editorId", editor.Id).Set("editorType", editor.EditorType ?? "")
                .Set("viewDefinition", editor.ViewDefinitionName ?? "")
                .Set("classDiagram", reject == null).Set("reason", reject));
            if (reject != null) continue;
            if (!string.IsNullOrEmpty(editorId) ? editor.Id == editorId : found == null) found = editor;
        }
        return found;
    }

    static IEditor RequireEditor(IApplication app, string path, string id, string editorId, out IModel model)
    {
        model = ModelApi.ResolveModel(app, path, id);
        List<object> candidates;
        var editor = FindEditor(app, model, editorId, out candidates);
        if (editor == null)
            throw new NdMcpHttpError(404, "モデルにクラス図がありません" + (string.IsNullOrEmpty(editorId) ? "" : " (editor=" + editorId + ")")
                + ": " + ModelApi.PathOf(model) + " / 図 " + candidates.Count + " 件");
        return editor;
    }

    // GET /class-sync/current: 図の現在の内容を PlantUML（PlantUmlTool のクラス図出力と同じ書式）で返す。
    public static object Current(IApplication app, string path, string id, string editorId)
    {
        IModel model;
        var editor = RequireEditor(app, path, id, editorId, out model);
        var log = new StringBuilder();
        var snapshot = ClassDiagramSnapshot.Read((IDiagram)editor, new ClassSyncOptions(), log);
        var editorModel = ClassDiagramKind.ModelOf(editor);
        return new JsonObject()
            .Set("modelPath", ModelApi.PathOf(model)).Set("modelId", model.Id)
            .Set("editorId", editor.Id).Set("diagramName", editorModel != null ? editorModel.Name : "")
            .Set("viewDefinition", editor.ViewDefinitionName ?? "")
            .Set("plantuml", ClassPumlWriter.Write(snapshot.Document))
            .Set("limitations", snapshot.Limitations.Cast<object>().ToList());
    }

    // GET /class-sync/editors: モデルに紐づく図の一覧と、クラス図として扱えるかどうか。
    public static object Editors(IApplication app, string path, string id)
    {
        var model = ModelApi.ResolveModel(app, path, id);
        List<object> candidates;
        FindEditor(app, model, "", out candidates);
        return new JsonObject().Set("modelPath", ModelApi.PathOf(model)).Set("modelId", model.Id).Set("editors", candidates);
    }

    // POST /class-sync/{preview|trial|apply}: PlantUML と図を比較し、mode に応じて反映する。
    //   preview … 比較のみ。trial … 一時適用して照合し、必ず取り消す。apply … 確定する。
    public static object Sync(IApplication app, string path, string id, string editorId, string plantuml, string mode)
    {
        if (plantuml == null || plantuml.Trim().Length == 0) throw new NdMcpHttpError(400, "plantuml が空です");
        if (plantuml.Length > MaxPumlLength) throw new NdMcpHttpError(400, "plantuml は 300KB 以下にしてください");
        bool trial = mode == "trial" || mode == "apply";
        bool retain = mode == "apply";
        IModel model;
        var editor = RequireEditor(app, path, id, editorId, out model);
        var outcome = ClassSyncRuntime.Run(app, editor, plantuml, "mcp:" + mode, trial, retain, true, message => true);
        string stem = ClassExperiment.SaveReport("mcp-" + mode, outcome.Log, outcome.ReportJson, outcome.CurrentPuml);
        var result = new JsonObject()
            .Set("mode", mode).Set("ok", outcome.Succeeded)
            .Set("modelPath", ModelApi.PathOf(model)).Set("modelId", model.Id).Set("editorId", editor.Id)
            .Set("changes", outcome.Changes).Set("limitations", outcome.Limitations).Set("stopReasons", outcome.StopReasons)
            .Set("applied", outcome.Applied).Set("committed", outcome.Committed)
            .Set("summary", outcome.Summary).Set("details", outcome.Details)
            .Set("error", outcome.ErrorMessage)
            .Set("reportFile", stem == null ? null : stem + ".txt");
        if (mode == "preview") result.Set("currentPlantuml", outcome.CurrentPuml);
        return result;
    }
}
