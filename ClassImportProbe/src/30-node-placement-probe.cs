// ------------------------------------------------------------
//  新規作成の箱置き調査（開発用）
//
//    PlantUmlTool の「PlantUMLから新規作成」（クラス図）は、新しい図に Domain の箱を
//    AddNodeShape で置こうとして「ビュー定義 … を用いて … シェイプを追加できません」で
//    止まる（K067）。SDK の説明では、マッピング対象がクラスのシェイプ（手動）は追加でき、
//    フィールドのシェイプ（自動）は非表示の既存ノードを表示するだけ。どちらなのか、
//    自動ならどのフィールドかを、開いている既存のクラス図から調べる。
//    図のモデルを仮に作って試し、最後に取り消す（保存しない）。
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
            if (!app.Window.UI.ShowConfirmDialog("【コピーのプロジェクトで実行してください】\n開いているクラス図の箱のビュー定義を調べ、同じ所有先に図のモデルを仮に作って箱を置けるか試します。最後に取り消します（保存しません）。\n\nOK: 実行 / キャンセル: 中止", Category)) return;

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

            // 3. A diagram model made for the trial, then taken back.
            IField field = null;
            try { field = model.GetOwnerField(); } catch (Exception) { }
            if (model == null || model.Owner == null || field == null) { say("所有先を取得できないので試行は省略"); return; }
            var targets = outer.Select(n => new { Node = n, Model = ClassDiagramKind.ModelOf(n) }).Where(t => t.Model != null).Take(3).ToList();
            var transaction = project.BeginUndoTransaction(false);
            try
            {
                var fresh = model.Owner.AddNewModel(field, model.Metaclass);
                fresh.SetField("Name", "箱置き調査（取り消します）");
                var newDiagram = fresh.GetEditors().Cast<object>().OfType<IEditor>().Where(e => ClassDiagramKind.Reject(e) == null).OfType<IDiagram>().FirstOrDefault();
                say("仮の図: " + (newDiagram == null ? "クラス図エディタなし" : "ノード " + newDiagram.Nodes.Cast<object>().Count() + " / 表示中 " + newDiagram.DisplayedShapes.Cast<object>().Count()));
                if (newDiagram != null)
                {
                    foreach (var n in newDiagram.Nodes.Cast<object>().OfType<INode>().Take(12))
                    {
                        var m = ClassDiagramKind.ModelOf(n);
                        say("    仮の図のノード '" + (m == null ? "?" : m.Name) + "' visible=" + n.IsVisible);
                    }
                    foreach (var t in targets)
                    {
                        bool can = false;
                        try { can = newDiagram.CanAddNodeShape(t.Model); } catch (Exception ex) { say("CanAddNodeShape: " + ex.Message); }
                        int shapes = 0;
                        try { shapes = newDiagram.GetShapesByModel(t.Model).Cast<object>().Count(); } catch (Exception) { }
                        say("'" + t.Model.Name + "': CanAddNodeShape=" + can + " 既存シェイプ=" + shapes);
                        foreach (var attempt in new[] { "既存の箱の定義", "FindElementDefByClass の定義", "定義なし(null)" })
                        {
                            IElementDef def = null;
                            if (attempt == "既存の箱の定義") def = ((IRepresentation)t.Node).ViewDefinition as IElementDef;
                            else if (attempt == "FindElementDefByClass の定義") { try { def = views.FindElementDefByClass(editorDef, t.Model.Metaclass, null).Cast<IElementDef>().FirstOrDefault(); } catch (Exception) { } }
                            try
                            {
                                var added = newDiagram.AddNodeShape(t.Model, def);
                                say("    AddNodeShape（" + attempt + "）: 成功 " + (added == null ? "(null)" : "visible=" + ((INode)added).IsVisible));
                                break;
                            }
                            catch (Exception ex) { say("    AddNodeShape（" + attempt + "）: " + ex.GetType().Name + ": " + ex.Message); }
                        }
                    }
                }
            }
            finally
            {
                try { transaction.Rollback(); say("試行は取り消しました"); } catch (Exception ex) { say("取消に失敗: " + ex.Message + "（保存せずに開き直してください）"); }
            }
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
