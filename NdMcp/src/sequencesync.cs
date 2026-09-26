// ============================================================
//  Part D / シーケンス図の PlantUML 同期 API（NdMcp 固有部。src/sequencesync.cs）
//
//  同期本体は PlantUmlTool/src/70〜74 を直接ビルドする（リボンの「PlantUMLで更新」
//  「PlantUMLから新規作成」と同じ処理）。ここは HTTP 要求と本体をつなぐ薄い層だけ。
//  ダイアログは出さない（本体のバッチ用の入口 Batch / BatchDiagram / BatchInput を使う）。
//  ref の参照先は名前が一致する相互作用が 1 つのときだけ結び付ける。
// ============================================================

public static class SequenceSyncApi
{
    public const int MaxPumlLength = 300000;

    static List<ISequenceDiagram> SequenceEditors(IModel model)
    {
        try { return model.GetEditors().Cast<object>().OfType<ISequenceDiagram>().ToList(); }
        catch (Exception ex) { throw new NdMcpHttpError(500, "エディタ一覧を取得できません: " + ex.Message); }
    }

    // path/id が指すモデル（シーケンス図のモデル＝相互作用）の図。editorId 指定があればそれ。
    static ISequenceDiagram RequireDiagram(IApplication app, string path, string id, string editorId, out IModel model)
    {
        model = ModelApi.ResolveModel(app, path, id);
        var editors = SequenceEditors(model);
        var found = string.IsNullOrEmpty(editorId) ? editors.FirstOrDefault() : editors.FirstOrDefault(e => e.Id == editorId);
        if (found == null)
            throw new NdMcpHttpError(404, "モデルにシーケンス図がありません" + (string.IsNullOrEmpty(editorId) ? "" : " (editor=" + editorId + ")")
                + ": " + ModelApi.PathOf(model) + "。nd_sequence_diagrams で図のモデルを探してください。");
        return found;
    }

    static JsonObject Describe(IModel owner, ISequenceDiagram d)
    {
        var model = d.Model;
        return new JsonObject()
            .Set("name", model != null ? model.Name : "")
            .Set("modelPath", model != null ? ModelApi.PathOf(model) : "").Set("modelId", model != null ? model.Id : "")
            .Set("editorId", d.Id).Set("viewDefinition", d.ViewDefinitionName ?? "")
            .Set("lifelines", d.Lifelines.Count()).Set("messages", d.Messages.Count());
    }

    // GET /sequence-sync/diagrams: 指定モデル配下（省略時はプロジェクト全体）のシーケンス図の一覧。
    public static object Diagrams(IApplication app, string path, string id, int limit)
    {
        IModel root = string.IsNullOrEmpty(path) && string.IsNullOrEmpty(id) ? app.Workspace.CurrentProject.DesignModel : ModelApi.ResolveModel(app, path, id);
        int skipped = 0;
        var entries = ExportRunner.Collect(root, false, ref skipped);
        return new JsonObject()
            .Set("root", ModelApi.PathOf(root)).Set("count", entries.Count)
            .Set("diagrams", entries.Take(limit).Select(e => (object)Describe(e.Owner, e.Diagram)).ToList())
            .Set("truncated", entries.Count > limit);
    }

    // GET /sequence-sync/current: 図を PlantUML（PlantUmlTool の出力と同じ書式）で返す。
    public static object Current(IApplication app, string path, string id, string editorId)
    {
        IModel model;
        var diagram = RequireDiagram(app, path, id, editorId, out model);
        var uml = new SequencePlantUmlExporter(diagram, new PlantUmlOptions()).Export();
        return Describe(model, diagram).Set("plantuml", uml);
    }

    // POST /sequence-sync/{preview|trial|apply}
    //   preview … 比較のみ。trial … 一時適用して照合し、必ず取り消す。apply … 確定する。
    //   save … 未保存のプロジェクトを先に保存する（Ctrl+Z で戻せるようにする）。既定は保存しない。
    public static object Sync(IApplication app, string path, string id, string editorId, string plantuml, string mode, bool save)
    {
        if (plantuml == null || plantuml.Trim().Length == 0) throw new NdMcpHttpError(400, "plantuml が空です");
        if (plantuml.Length > MaxPumlLength) throw new NdMcpHttpError(400, "plantuml は 300KB 以下にしてください");
        IModel model;
        var diagram = RequireDiagram(app, path, id, editorId, out model);
        var project = app.Workspace.CurrentProject;
        bool unsaved = SequenceSyncRuntime.Unsaved(project);
        string file = Path.Combine(Path.GetTempPath(), "NdMcp-sequence-" + Guid.NewGuid().ToString("N") + ".puml");
        File.WriteAllText(file, plantuml, new UTF8Encoding(false));
        try
        {
            SequenceExperiment.Title = "シーケンス図同期 (NdMcp)";
            SequenceSyncRuntime.Batch = true; SequenceSyncRuntime.BatchDiagram = diagram; SequenceSyncRuntime.BatchInput = file;
            SequenceSyncRuntime.Plain = mode == "apply";
            SequenceSyncRuntime.SaveBeforeUpdate = save; SequenceSyncRuntime.UpdateWithoutSaving = !save;
            if (mode == "preview") SequenceSyncRuntime.Preview(app);
            else if (mode == "trial") SequenceSyncRuntime.Preview(app, true, true);
            else SequenceSyncRuntime.Preview(app, true, true, true, true);
        }
        finally
        {
            SequenceSyncRuntime.Batch = false; SequenceSyncRuntime.BatchDiagram = null; SequenceSyncRuntime.BatchInput = null;
            SequenceSyncRuntime.Plain = false; SequenceSyncRuntime.SaveBeforeUpdate = false; SequenceSyncRuntime.UpdateWithoutSaving = false;
            try { File.Delete(file); } catch (Exception) { }
        }
        int changes = SequenceSyncRuntime.LastChanges;
        bool committed = SequenceSyncRuntime.LastCommitted;
        bool ok = changes >= 0 && (mode != "apply" || committed || changes == 0);
        var result = Describe(model, diagram)
            .Set("mode", mode).Set("ok", ok).Set("changes", changes).Set("committed", committed)
            .Set("stopReasons", SequenceSyncRuntime.LastReasons ?? "")
            .Set("summary", SequenceExperiment.Summary).Set("details", SequenceExperiment.Details)
            .Set("reportFile", SequenceSyncRuntime.LastReportFile);
        if (mode == "apply" && committed)
            result.Set("undo", unsaved && !save
                ? "未保存のまま更新した。Ctrl+Z で戻すと図は最後に保存した状態の図形に戻り、保存後に追加した図形は消える。"
                : "Ctrl+Z で戻せる（メッセージ・フラグメント・Note・ref を追加した更新は、Undo で製品が停止する既知の不具合がある）。");
        return result;
    }

    // POST /sequence-sync/create: PlantUML から新しいシーケンス図を作る。
    //   path/id はシーケンス図のモデル（その隣に作る）か、図を置くモデル。
    public static object Create(IApplication app, string path, string id, string plantuml)
    {
        if (plantuml == null || plantuml.Trim().Length == 0) throw new NdMcpHttpError(400, "plantuml が空です");
        if (plantuml.Length > MaxPumlLength) throw new NdMcpHttpError(400, "plantuml は 300KB 以下にしてください");
        var at = ModelApi.ResolveModel(app, path, id);
        var log = new StringBuilder();
        SequenceDiagramCreator.Created made = null;
        string error = null;
        try { made = SequenceDiagramCreator.Create(app, at, plantuml, log); }
        catch (Exception ex) { error = ex.Message; log.AppendLine(ex.ToString()); }
        string stem = SequenceDiagramCreator.SaveReport(log.ToString());
        var result = new JsonObject().Set("ok", made != null).Set("error", error).Set("reportFile", stem == null ? null : stem + ".txt");
        if (made != null)
        {
            var model = app.Workspace.CurrentProject.GetModelById(made.ModelId);
            result.Set("name", made.Name).Set("where", made.Where).Set("modelId", made.ModelId)
                .Set("modelPath", model != null ? ModelApi.PathOf(model) : "").Set("editorId", made.EditorId).Set("summary", made.Summary);
        }
        return result;
    }
}
