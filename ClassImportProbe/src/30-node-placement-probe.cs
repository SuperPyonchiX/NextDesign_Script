// ------------------------------------------------------------
//  新規作成の箱置き調査（開発用）
//
//    PlantUmlTool の「PlantUMLから新規作成」（クラス図）は、新しい図に Domain の箱を
//    AddNodeShape で置こうとして「ビュー定義 … を用いて … シェイプを追加できません」で
//    止まる（K067）。SDK の説明では、マッピング対象がクラスのシェイプ（手動）は追加でき、
//    フィールドのシェイプ（自動）は非表示の既存ノードを表示するだけ。どちらなのか、
//    自動ならどのフィールドかを、開いている既存のクラス図から調べる。
//    図のモデルを仮に作って試す。同じコマンドで置けなければ仮の図を開いたままにし、2回目を
//    別のコマンドとして試して、最後に仮の図を削除する（保存しない）。
// ------------------------------------------------------------
public static class NodePlacementProbe
{
    public const string Category = "ClassImportProbe";

    static string Props(object o)
    {
        if (o == null) return "(null)";
        var rows = new List<string> { "型=" + o.GetType().FullName };
        foreach (var p in o.GetType().GetProperties())
        {
            if (p.GetIndexParameters().Length > 0) continue;
            object v;
            try { v = p.GetValue(o); } catch (Exception ex) { v = "!" + ex.GetType().Name; }
            string text;
            if (v == null) text = "null";
            else if (v is string || v.GetType().IsPrimitive || v is Enum) text = v.ToString();
            else if (v is IModel) text = "IModel " + ((IModel)v).ClassName + " '" + ((IModel)v).Name + "'";
            else if (v is IClass) text = "IClass " + ((IClass)v).FullName;
            else if (v is IField) text = "IField " + ((IField)v).Name + ":" + ((IField)v).Type;
            else if (v is System.Collections.IEnumerable)
            {
                var items = ((System.Collections.IEnumerable)v).Cast<object>().Take(8).Select(x => x == null ? "null" : x is IField ? "IField " + ((IField)x).Name : x is IClass ? "IClass " + ((IClass)x).FullName : x.GetType().Name + ":" + x).ToArray();
                text = "[" + string.Join(", ", items) + "]";
            }
            else text = v.GetType().Name + ":" + v;
            rows.Add(p.Name + "=" + text);
        }
        return string.Join("\n      ", rows);
    }

    const string TrialName = "箱置き調査（削除します）";

    // What to try placing: models shown as outer boxes on the open diagram, else the Domains the
    // owner holds (for the second press, where the open diagram is the empty trial one).
    static IEnumerable<IModel> OuterModels(IModel owner, IEnumerable<IModel> shown)
    {
        var list = shown.ToList();
        if (list.Count > 0) return list;
        try { return owner.GetChildren().Cast<IModel>().Where(m => m != null && !m.IsDeleted && m.ClassName.StartsWith("Domain", StringComparison.Ordinal)).ToList(); }
        catch (Exception) { return new IModel[0]; }
    }

    static bool Place(IProject project, IViewDefinitions views, IDiagram target, List<IModel> models, string when, Action<string> say)
    {
        bool any = false;
        var editorDef = ((IEditor)target).EditorDefinition;
        foreach (var m in models)
        {
            bool can = false;
            try { can = target.CanAddNodeShape(m); } catch (Exception ex) { say("CanAddNodeShape: " + ex.Message); }
            IElementDef def = null;
            try { def = views.FindElementDefByClass(editorDef, m.Metaclass, null).Cast<IElementDef>().FirstOrDefault(); } catch (Exception) { }
            var tx = project.BeginUndoTransaction(false);
            try
            {
                var added = target.AddNodeShape(m, def);
                tx.Commit();
                say("[" + when + "] '" + m.Name + "' " + m.ClassName + ": CanAdd=" + can + " AddNodeShape 成功 " + (added == null ? "(null)" : "visible=" + ((INode)added).IsVisible));
                any = true;
            }
            catch (Exception ex)
            {
                try { tx.Rollback(); } catch (Exception) { }
                say("[" + when + "] '" + m.Name + "' " + m.ClassName + ": CanAdd=" + can + " " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        return any;
    }

    static void Remove(IProject project, IModel trial, Action<string> say)
    {
        var tx = project.BeginUndoTransaction(false);
        try { trial.Delete(); tx.Commit(); say("仮の図を削除しました"); }
        catch (Exception ex) { try { tx.Rollback(); } catch (Exception) { } say("仮の図を削除できません。手で削除してください: " + ex.Message); }
    }

    public static void Run(IApplication app)
    {
        var w = new Action<string>(text => app.Output.WriteLine(Category, text));
        OutputPane.Show(app, Category);
        var log = new StringBuilder();
        Action<string> say = text => { w(text); log.AppendLine(text); };
        try
        {
            var project = app.Workspace.CurrentProject;
            var editor = app.Workspace.CurrentEditor;
            var diagram = editor as IDiagram;
            if (project == null || diagram == null || ClassDiagramKind.Reject(editor) != null)
            { app.Window.UI.ShowInformationDialog("箱のある既存のクラス図を開いてから実行してください。", Category); return; }
            var current = ClassDiagramKind.ModelOf(editor);
            if (current != null && current.Name == TrialName)
            {
                say("=== 新規作成の箱置き調査（2回目） ===");
                var views2 = project.Profile.ViewDefinitions;
                var targets2 = OuterModels(current.Owner, new IModel[0]).Take(3).ToList();
                Place(project, views2, diagram, targets2, "別のコマンド", say);
                Remove(project, current, say);
                say("=== 調査終了 ===");
                return;
            }
            if (!app.Window.UI.ShowConfirmDialog("【コピーのプロジェクトで実行してください】\n開いているクラス図の箱のビュー定義を調べ、同じ所有先に図のモデルを仮に作って箱を置けるか試します。置けなければ仮の図を開いたままにするので、もう一度押してください。最後に仮の図を削除します（保存しません）。\n\nOK: 実行 / キャンセル: 中止", Category)) return;

            var model = ClassDiagramKind.ModelOf(editor);
            say("=== 新規作成の箱置き調査 ===");
            say("図: " + editor.EditorType + " / " + editor.ViewDefinitionName + " / モデル " + (model == null ? "?" : model.ClassName + " '" + model.Name + "'"));
            var editorDef = editor.EditorDefinition;
            say("エディタ定義: " + Props(editorDef));

            var nodes = diagram.Nodes.Cast<object>().OfType<INode>().ToList();
            var inner = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in nodes) foreach (var c in diagram.GetChildNodes(n).Cast<object>().OfType<INode>()) inner.Add(c.Id);
            var outer = nodes.Where(n => !inner.Contains(n.Id)).ToList();
            say("ノード " + nodes.Count + " / 外側 " + outer.Count);

            // 1. The definitions the existing outer boxes use.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in outer.Take(12))
            {
                var m = ClassDiagramKind.ModelOf(n);
                var vd = ((IRepresentation)n).ViewDefinition;
                string key = (m == null ? "" : m.ClassName) + "|" + (vd == null ? "" : vd.GetHashCode().ToString());
                say("外側の箱 '" + (m == null ? "?" : m.Name) + "' " + (m == null ? "" : m.ClassName) + " visible=" + n.IsVisible
                    + " 所有先=" + (m == null || m.Owner == null ? "?" : m.Owner.ClassName + " '" + m.Owner.Name + "'"));
                if (seen.Add(m == null ? "" : m.ClassName)) say("    その箱の定義: " + Props(vd));
            }

            // 2. What FindElementDefByClass gives for those classes.
            var views = project.Profile.ViewDefinitions;
            foreach (var cls in outer.Select(n => ClassDiagramKind.ModelOf(n)).Where(m => m != null && m.Metaclass != null).GroupBy(m => m.Metaclass.Id).Select(g => g.First().Metaclass))
            {
                var defs = new List<object>();
                try { defs = views.FindElementDefByClass(editorDef, cls, null).Cast<object>().ToList(); } catch (Exception ex) { say("FindElementDefByClass " + cls.Name + ": " + ex.Message); }
                say("FindElementDefByClass(" + cls.Name + "): " + defs.Count + " 件");
                foreach (var d in defs.Take(3)) say("    " + Props(d));
            }

            // 3. A trial diagram model in the same owner. The same-command attempts come first; the
            //    diagram is then left open so a second press can try from a command of its own.
            IField field = null;
            try { field = model.GetOwnerField(); } catch (Exception) { }
            if (model == null || model.Owner == null || field == null) { say("所有先を取得できないので試行は省略"); return; }
            var targets = OuterModels(model.Owner, outer.Select(n => ClassDiagramKind.ModelOf(n)).Where(m => m != null)).Take(3).ToList();
            IModel fresh = null;
            var make = project.BeginUndoTransaction(false);
            try
            {
                fresh = model.Owner.AddNewModel(field, model.Metaclass);
                fresh.SetField("Name", TrialName);
                make.Commit();
                say("仮の図のモデルを作って確定: " + fresh.Id);
            }
            catch (Exception ex) { try { make.Rollback(); } catch (Exception) { } say("仮の図のモデルを作れません: " + ex.Message); return; }
            var created = fresh.GetEditors().Cast<object>().OfType<IEditor>().Where(e => ClassDiagramKind.Reject(e) == null).OfType<IDiagram>().FirstOrDefault();
            bool placed = created != null && Place(project, views, created, targets, "確定後（同じコマンド）", say);
            if (!placed)
            {
                try { app.Workspace.State.SetCurrentModel(fresh); say("仮の図を開きました（SetCurrentModel）: 現在のエディタ=" + (app.Workspace.CurrentEditor == null ? "なし" : app.Workspace.CurrentEditor.ViewDefinitionName + " / " + (ClassDiagramKind.ModelOf(app.Workspace.CurrentEditor) == null ? "?" : ClassDiagramKind.ModelOf(app.Workspace.CurrentEditor).Name))); }
                catch (Exception ex) { say("SetCurrentModel: " + ex.Message); }
                var opened = app.Workspace.CurrentEditor as IDiagram;
                if (opened != null && ClassDiagramKind.ModelOf((IEditor)opened) != null && ClassDiagramKind.ModelOf((IEditor)opened).Id == fresh.Id)
                    placed = Place(project, views, opened, targets, "開いた後（同じコマンド）", say);
            }
            if (placed) { Remove(project, fresh, say); }
            else say("同じコマンドの中では置けませんでした。仮の図「" + TrialName + "」を開いたまま、もう一度「新規作成の箱置き調査」を押してください（2回目は別のコマンドとして試し、最後に仮の図を削除します）。");
            say("=== 調査終了 ===");
        }
        catch (Exception ex) { say("[error] " + ex); }
        finally
        {
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextDesign.ClassSync", "reports");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_node-probe.txt");
                File.WriteAllText(path, log.ToString(), new UTF8Encoding(false));
                w("保存先: " + path);
                app.Window.UI.ShowInformationDialog("調査が終わりました。結果: " + path, Category);
            }
            catch (Exception) { }
        }
    }
}
