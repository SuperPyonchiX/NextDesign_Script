// ============================================================
//  Part 10 / シーケンス図同期のリボン側（PlantUMLで更新・PlantUMLから新規作成）
//
//    同期の本体（70〜73）は実験拡張 SequenceImportProbe で実機検証したものをそのまま
//    置いている。Probe は開発用のボタン（一括検証・全図チェック・調査）を持ち、同じ
//    ファイルを直接ビルドする。ここは PlantUmlTool のボタンから呼ぶ入口だけ。
//    ・反映: 未保存のプロジェクトでも確認せずに反映する（写しは SDK から組み立てる）
//    ・新規作成: 見本の図は要らない。開いているシーケンス図があれば同じ所有先に、
//      なければ開いている・選んでいるモデルの、シーケンス図を持てる欄に作る
// ============================================================

public static class SequenceCommands
{
    public const string Title = "PlantUML 連携 / シーケンス図";

    // Ribbon entry: update the open diagram to the chosen PlantUML file.
    public static void Apply(IApplication app)
    {
        SequenceExperiment.Title = Title;
        // On an unsaved project the user chooses: saving first keeps Ctrl+Z working, updating
        // without saving leaves Ctrl+Z putting the diagram's shapes back as last saved (the
        // product's undo of an editor import). MCP callers set UpdateWithoutSaving themselves.
        bool save = false;
        var project = app.Workspace.CurrentProject;
        if (project != null && app.Workspace.CurrentEditor is ISequenceDiagram && SequenceSyncRuntime.Unsaved(project))
            save = app.Window.UI.ShowConfirmDialog("プロジェクトに未保存の変更があります。\n\n"
                + "OK: 保存してから更新する（Ctrl+Z で更新を戻せます。作業中の他の変更も一緒に保存されます）\n"
                + "キャンセル: 保存せずに更新する（Ctrl+Z で戻すと、図は最後に保存した状態の図形に戻り、保存後に追加した図形は消えます）", Title);
        SequenceSyncRuntime.Plain = true;
        SequenceSyncRuntime.SaveBeforeUpdate = save;
        SequenceSyncRuntime.UpdateWithoutSaving = !save;
        try { SequenceSyncRuntime.Preview(app, true, true, true, true); }
        finally { SequenceSyncRuntime.Plain = false; SequenceSyncRuntime.UpdateWithoutSaving = false; SequenceSyncRuntime.SaveBeforeUpdate = false; }
    }

    // Ribbon entry: a new sequence diagram from the chosen PlantUML file.
    public static void Create(IApplication app)
    {
        SequenceExperiment.Title = Title;
        var log = new StringBuilder();
        string summary;
        try { summary = SequenceDiagramCreator.Run(app, log); }
        catch (Exception ex)
        {
            log.AppendLine(ex.ToString());
            summary = "シーケンス図を作れませんでした。プロジェクトは変更していません。\n" + ex.Message;
        }
        if (summary == null) return;
        string stem = SequenceDiagramCreator.SaveReport(log.ToString());
        if (stem != null) summary += "\n診断: " + stem + ".txt";
        app.Window.UI.ShowInformationDialog(summary, Title);
    }
}

public static class SequenceDiagramCreator
{
    static string Name(IModel m) { return m == null ? "" : (m.Name ?? ""); }
    static IEnumerable<IClass> Concrete(IClass declared)
    {
        var all = new List<IClass> { declared };
        try { all.AddRange(declared.GetAllSubClasses().Cast<IClass>()); } catch (Exception) { }
        return all.Where(c => c != null && !c.IsAbstract).GroupBy(c => c.Id).Select(g => g.First());
    }
    static IEnumerable<IEditorDef> SequenceDefinitions(IProject project, IClass c)
    {
        try { return project.Profile.ViewDefinitions.FindEditorDefByClass(c, null).Cast<IEditorDef>().Where(SequenceTypeSource.IsSequence).ToList(); }
        catch (Exception) { return new IEditorDef[0]; }
    }

    sealed class Place { public IModel Owner; public IField Field; public SequenceTypeSource Types; public string Where; }

    // Whether a sequence diagram can be made from where the user is (the ribbon's single
    // "新規作成" chooses between sequence and class diagrams with this).
    public static bool CanCreateHere(IApplication app, out string reason)
    {
        reason = null;
        try { Resolve(app, app.Workspace.CurrentProject, new StringBuilder()); return true; }
        catch (Exception ex) { reason = ex.Message; return false; }
    }

    // With a sequence diagram open: beside it, of the same kind. Otherwise the open or selected
    // model: the embedded field whose element type has a sequence editor in the profile,
    // preferring the one the model's existing sequence diagrams already use.
    // at: a model given by a caller (the MCP API) in place of what is open: a sequence diagram's
    // model (the new one goes beside it) or the model to hold it.
    static Place Resolve(IApplication app, IProject project, StringBuilder log, IModel at = null)
    {
        var open = at == null ? app.Workspace.CurrentEditor as ISequenceDiagram
            : at.GetEditors().Cast<object>().OfType<ISequenceDiagram>().FirstOrDefault();
        if (open != null && open.Model != null)
        {
            var model = open.Model;
            IField field = null;
            try { field = model.GetOwnerField(); } catch (Exception) { }
            if (model.Owner == null || field == null) throw new InvalidOperationException("E102: 開いている図の所有先を取得できません。");
            return new Place { Owner = model.Owner, Field = field, Types = SequenceTypeSource.Of(open), Where = "開いている図と同じ「" + Name(model.Owner) + "」の下" };
        }
        IModel parent = at;
        var editor = at == null ? app.Workspace.CurrentEditor : null;
        try { if (parent == null && editor != null) parent = editor.Model; } catch (Exception) { }
        if (parent == null && at == null) { try { parent = app.Window.EditorPage.CurrentModel; } catch (Exception) { } }
        if (parent == null || parent.Metaclass == null)
            throw new InvalidOperationException("E102: シーケンス図を追加するモデルを開くか選んでから実行してください。");
        var candidates = new List<Place>();
        foreach (var f in parent.Metaclass.GetFields().Cast<IField>().Where(f => f.IsEmbedded && f.TypeClass != null))
            foreach (var c in Concrete(f.TypeClass))
                foreach (var d in SequenceDefinitions(project, c))
                    candidates.Add(new Place { Owner = parent, Field = f, Types = SequenceTypeSource.Blank(d, c), Where = "「" + Name(parent) + "」の下" });
        log.AppendLine("place candidates: " + string.Join(", ", candidates.Select(p => p.Field.Name + "/" + p.Types.Interaction.FullName + "/" + p.Types.Definition.Type + ":" + p.Types.Definition.Id)));
        if (candidates.Count > 1)
        {
            // Sequence diagrams already in the model show which field, class and editor to use.
            var used = new HashSet<string>();
            foreach (var child in parent.GetChildren().Cast<IModel>())
            {
                IField own = null;
                try { own = child.GetOwnerField(); } catch (Exception) { }
                if (own == null || child.Metaclass == null) continue;
                foreach (var d in child.GetEditors().OfType<ISequenceDiagram>())
                    if (d.EditorDefinition != null) used.Add(own.Name + "\t" + child.Metaclass.Id + "\t" + d.EditorDefinition.Id);
            }
            var preferred = candidates.Where(p => used.Contains(p.Field.Name + "\t" + p.Types.Interaction.Id + "\t" + p.Types.Definition.Id)).ToList();
            if (preferred.Count == 1) candidates = preferred;
        }
        if (candidates.Count == 0)
            throw new InvalidOperationException("E102: 「" + Name(parent) + "」（" + parent.ClassName + "）にはシーケンス図を追加できません。シーケンス図を置くモデルを開くか選んでから実行してください。");
        if (candidates.Count > 1)
            throw new InvalidOperationException("E102: 「" + Name(parent) + "」に追加できるシーケンス図の種類が複数あり、決められません: "
                + string.Join(", ", candidates.Select(p => p.Field.Name + "/" + p.Types.Interaction.Name)));
        return candidates[0];
    }

    // The schema the project file states; the public sample's when it cannot be read.
    static string Schema(IProject project, StringBuilder log)
    {
        string schema = "13.0";
        try
        {
            if (!string.IsNullOrEmpty(project.Path) && File.Exists(project.Path))
                using (var reader = new StreamReader(project.Path, Encoding.UTF8, true))
                {
                    char[] header = new char[4096]; int n = reader.Read(header, 0, header.Length);
                    var match = Regex.Match(new string(header, 0, n), "\"SchemaVersion\"\\s*:\\s*\"([0-9]+\\.[0-9]+)\"");
                    if (match.Success) { log.AppendLine("schema: " + match.Groups[1].Value + " (project header)"); return match.Groups[1].Value; }
                }
        }
        catch (Exception ex) { log.AppendLine("schema read: " + ex.Message); }
        log.AppendLine("schema: " + schema + " (default)");
        return schema;
    }

    // What a creation made, for a caller that reports it (the MCP API).
    public sealed class Created { public string Summary, ModelId, EditorId, Name, Where; }

    // Returns the summary to show, or null when the user cancelled.
    public static string Run(IApplication app, StringBuilder log)
    {
        var project = app.Workspace.CurrentProject;
        if (project == null) throw new InvalidOperationException("E101: プロジェクトを開いてください。");
        var place = Resolve(app, project, log);
        string path = app.Window.UI.ShowOpenFileDialog("新しいシーケンス図にするPlantUML", "PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
        if (string.IsNullOrEmpty(path)) return null;
        if (new FileInfo(path).Length > 300000) throw new InvalidOperationException("E120: 入力は300KB以下にしてください。");
        string text = File.ReadAllText(path, new UTF8Encoding(false, true));
        log.AppendLine("PlantUML file: " + path);
        var made = Make(app, project, place, text, log, true);
        return made == null ? null : made.Summary;
    }

    // Without dialogs: at is where (see Resolve), refs are linked only where one interaction fits.
    public static Created Create(IApplication app, IModel at, string text, StringBuilder log)
    {
        var project = app.Workspace.CurrentProject;
        if (project == null) throw new InvalidOperationException("E101: プロジェクトを開いてください。");
        if (at == null) throw new InvalidOperationException("E102: 作成先のモデルを指定してください。");
        return Make(app, project, Resolve(app, project, log, at), text, log, false);
    }

    static Created Make(IApplication app, IProject project, Place place, string text, StringBuilder log, bool interactive)
    {
        if (!place.Owner.IsEditable || place.Owner.IsDeleted || place.Owner.IsProxy)
            throw new InvalidOperationException("E102: 「" + Name(place.Owner) + "」は編集できません。");
        log.AppendLine("place: " + place.Where + " " + place.Owner.ClassName + "." + place.Field.Name + " as " + place.Types.Interaction.FullName
            + " editor=" + place.Types.Definition.Type + " " + place.Types.Definition.Id);
        var plan = PumlPlan.Parse(text);
        SequenceDocument.Parse(text);

        var sources = PumlRuntime.BaseTypes(place.Types);
        log.AppendLine("base types: " + string.Join(", ", sources.Select(c => c.FullName)));
        var relationIds = new HashSet<string>(sources.SelectMany(c => c.GetFields().Cast<IField>())
            .Where(f => f.RelationshipClass != null).Select(f => f.RelationshipClass.Id));
        foreach (string id in SequencePayload.RelationTypes)
            if (!relationIds.Contains(SequencePayload.Prefix + id))
                throw new InvalidOperationException("E106: 標準の構造関連が見つかりません: " + id);
        if (!sources[6].GetFields().Cast<IField>().Any(f => f.Name == "MessageSort"))
            throw new InvalidOperationException("E107: メッセージ種別フィールドが未対応です。");
        string schema = Schema(project, log);
        var profile = PumlRuntime.Profile(place.Types, sources, plan, project);
        if (profile.Relations.ContainsKey("RefersTo"))
            SequenceSyncRuntime.ResolveReferences(app, project, place.Owner, plan.All().Where(n => n.Kind == "ref"), profile.References, log, interactive);
        foreach (string row in profile.Resolved) log.AppendLine("type: " + row);
        var payload = PumlBuild.Build(plan, profile, place.Types.Definition.Id, schema, null);
        foreach (string id in payload.Ids)
            if (project.GetModelById(id) != null) throw new InvalidOperationException("E111: 生成IDが既存モデルと衝突しました。");

        if (interactive && !app.Window.UI.ShowConfirmDialog(place.Where + "に新しいシーケンス図「" + payload.Name + "」を作ります。\n" + plan.Summary()
            + "\n\nプロジェクトは保存しません。作成の Undo は確かめていません。取り消すときは保存せずに開き直してください。\n\nOK: 作成する\nキャンセル: 中止する", SequenceCommands.Title))
            return null;

        var transaction = project.BeginUndoTransaction(false);
        if (transaction == null) throw new InvalidOperationException("E117: トランザクションを開始できませんでした。");
        bool done = false;
        try
        {
            var result = project.ImportUnitFromJson(payload.Json, place.Owner, place.Field.Name);
            if (result == null) throw new InvalidOperationException("E112: インポート結果がありません。");
            log.AppendLine("import: " + result.State);
            foreach (var error in result.Errors) log.AppendLine(error.Kind + ": " + error.Message);
            if (result.State != "success" || result.Errors.Any(e => e.Kind != UnitImportErrorKind.Info))
                throw new InvalidOperationException("E113: インポートが失敗または警告を返しました。");
            PumlRuntime.Verify(project, result, payload, place.Owner.Id);
            transaction.Commit(); done = true;
        }
        finally
        {
            // Explicit completion only; Dispose may attempt a second rollback.
            if (!done)
            {
                try { transaction.Rollback(); log.AppendLine("rolled back"); }
                catch (Exception rollback) { log.AppendLine("ROLLBACK: " + rollback); }
            }
        }
        int refs = plan.All().Count(n => n.Kind == "ref");
        int linked = profile.Relations.ContainsKey("RefersTo") ? profile.References.Values.Count(v => !string.IsNullOrEmpty(v)) : 0;
        var root = project.GetModelById(payload.Ids[0]);
        var made = root == null ? null : root.GetEditors().Cast<object>().OfType<ISequenceDiagram>().FirstOrDefault();
        return new Created { Name = payload.Name, Where = place.Where, ModelId = payload.Ids[0], EditorId = made == null ? null : made.Id,
            Summary = "新しいシーケンス図「" + payload.Name + "」を作りました（" + place.Where + "）。\n" + plan.Summary()
            + (refs > 0 ? "\nref の参照先: " + linked + " / " + refs + " 件を結び付けました（名前が一致する相互作用がないものは参照先なし）。" : "")
            + "\nプロジェクトは保存していません。" };
    }

    // The log names models and paths; it stays on this PC.
    public static string SaveReport(string log)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextDesign.SequenceSync", "reports");
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_create_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            File.WriteAllText(stem + ".txt", log, new UTF8Encoding(false));
            return stem;
        }
        catch (Exception) { return null; }
    }
}
