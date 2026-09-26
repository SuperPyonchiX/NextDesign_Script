// PlantUmlTool only: the class diagram probe uses MetaProbe from the legacy import part,
// so AgentReview and NdMcp do not transcribe this file.
// ------------------------------------------------------------
//  クラス図のメタモデル調査
//
//    メタクラス名・フィールド名はプロファイル依存で推測できないため、
//    実機の値をここでダンプし、ClassPlantUmlOptions の対応表を埋める
// ------------------------------------------------------------
public class ClassProbe
{
    public const string Category = "PlantUmlImport";

    public static void Run(IApplication app, IDiagram diagram)
    {
        var w = new Action<string>(text => app.Output.WriteLine(Category, text));

        var editor = diagram as IEditor;
        w("=== クラス図調査 ===");
        w("EditorType         : " + (editor != null ? editor.EditorType : "(不明)"));
        w("ViewDefinitionName : " + (editor != null ? editor.ViewDefinitionName : "(不明)"));

        var nodes = new List<INode>();
        try { foreach (var n in diagram.Nodes) { var node = n as INode; if (node != null) nodes.Add(node); } }
        catch (Exception ex) { w("ノードの取得に失敗 : " + ex.Message); }

        var connectors = new List<IConnector>();
        try { foreach (var c in diagram.Connectors) { var conn = c as IConnector; if (conn != null) connectors.Add(conn); } }
        catch (Exception ex) { w("コネクタの取得に失敗 : " + ex.Message); }

        w("ノード数           : " + nodes.Count);
        w("コネクタ数         : " + connectors.Count);
        w("");

        var models = new List<IModel>();
        foreach (var node in nodes)
        {
            var model = MetaMap.ModelOf(node);
            if (model != null) models.Add(model);
        }

        DumpModel(w, "ノード(1件目)", models.Count > 0 ? models[0] : null);
        DumpModel(w, "ノードの子(1件目)", FirstChild(models));

        DumpClassNames(w, "ノードのクラス名一覧", models);
        DumpClassNames(w, "子のクラス名一覧", AllChildren(models));

        DumpConnectors(w, connectors);
        DumpReferenceFields(w, models);

        w("=== 調査終了 ===");
    }

    private static IModel FirstChild(List<IModel> models)
    {
        foreach (var model in models)
        {
            try
            {
                foreach (var child in model.GetChildren().Cast<IModel>())
                    if (child != null && !child.IsDeleted) return child;
            }
            catch (Exception) { }
        }
        return null;
    }

    private static List<IModel> AllChildren(List<IModel> models)
    {
        var result = new List<IModel>();
        foreach (var model in models)
        {
            try
            {
                foreach (var child in model.GetChildren().Cast<IModel>())
                    if (child != null && !child.IsDeleted) result.Add(child);
            }
            catch (Exception) { }
        }
        return result;
    }

    private static void DumpClassNames(Action<string> w, string title, List<IModel> models)
    {
        w("---- " + title + " ----");
        if (models.Count == 0) { w("  (なし)"); w(""); return; }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            var name = model.ClassName ?? "(null)";
            if (!counts.ContainsKey(name)) counts[name] = 0;
            counts[name]++;
        }
        foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
            w("  " + pair.Key + " : " + pair.Value + " 件");
        w("");
    }

    private static void DumpConnectors(Action<string> w, List<IConnector> connectors)
    {
        w("---- コネクタ ----");
        if (connectors.Count == 0) { w("  (なし)"); w(""); return; }

        foreach (var connector in connectors)
        {
            var from = MetaMap.ModelOf(connector.StartPoint);
            var to = MetaMap.ModelOf(connector.EndPoint);
            var label = (from != null ? from.Name : "?") + " -> " + (to != null ? to.Name : "?");

            var own = MetaMap.ModelOf(connector);
            w("  " + label
              + " | LineType=" + SafeLineType(connector)
              + " | コネクタのモデル=" + (own != null ? own.ClassName + " '" + own.Name + "'" : "(なし)"));

            if (from == null || to == null) continue;
            try
            {
                var any = false;
                foreach (var r in from.GetRelationsOf(to).Cast<IRelationship>())
                {
                    any = true;
                    w("      IsEmbedded=" + r.IsEmbedded
                      + " IsReference=" + r.IsReference
                      + " IsTwoWay=" + r.IsTwoWay
                      + " SourceField=" + FieldName(r.SourceField)
                      + " TargetField=" + FieldName(r.TargetField));
                }
                if (!any) w("      (GetRelationsOf で関連を取得できません)");
            }
            catch (Exception ex) { w("      GetRelationsOf に失敗 : " + ex.Message); }
        }
        w("");
    }

    private static string SafeLineType(IConnector c)
    {
        try { return c.LineType; } catch (Exception) { return "(不明)"; }
    }

    private static string FieldName(IField f)
    {
        if (f == null) return "(なし)";
        return f.Name + "[" + f.LowerBound + ".."
             + (f.UpperBound < 0 ? "*" : f.UpperBound.ToString(CultureInfo.InvariantCulture)) + "]";
    }

    // 図上のノードどうしを結ぶ参照フィールドを一覧にする（LinkMap を埋めるための材料）
    private static void DumpReferenceFields(Action<string> w, List<IModel> models)
    {
        w("---- 図上のノードを結ぶ参照フィールド ----");

        var ids = new HashSet<string>(models.Select(m => m.Id), StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var model in models)
        {
            var cls = model.Metaclass;
            if (cls == null) continue;

            List<IField> fields;
            try { fields = cls.GetFields().Cast<IField>().ToList(); }
            catch (Exception) { continue; }

            foreach (var f in fields)
            {
                if (!f.IsReference && !f.IsEmbedded) continue;
                try
                {
                    foreach (var v in model.GetFieldValues(f.Name))
                    {
                        var target = v as IModel;
                        if (target == null || !ids.Contains(target.Id)) continue;
                        var key = (f.IsEmbedded ? "所有 " : "参照 ") + f.Name
                                + " [" + f.LowerBound + ".."
                                + (f.UpperBound < 0 ? "*" : f.UpperBound.ToString(CultureInfo.InvariantCulture)) + "]";
                        if (!counts.ContainsKey(key)) counts[key] = 0;
                        counts[key]++;
                    }
                }
                catch (Exception) { }
            }
        }

        if (counts.Count == 0) w("  (なし)");
        else
            foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
                w("  " + pair.Key + " : " + pair.Value + " 件");
        w("");
    }
    // メタモデル調査（SequenceImportProbe の MetaProbe）と同じ書式。
    public static void DumpModel(Action<string> w, string title, IModel m)
    {
        w("---- " + title + " ----");
        if (m == null)
        {
            w("  (見本なし)");
            w("");
            return;
        }

        w("  ClassName  : " + m.ClassName);
        w("  Name       : " + m.Name);

        var cls = m.Metaclass;
        if (cls != null)
        {
            w("  FullName   : " + cls.FullName);
            w("  IsAbstract : " + cls.IsAbstract);
            var supers = cls.GetAllSuperClasses().Cast<IClass>().Select(c => c.Name).ToList();
            w("  SuperClass : " + (supers.Count > 0 ? string.Join(", ", supers.ToArray()) : "(なし)"));
        }

        try
        {
            var ownerField = m.GetOwnerField();
            w("  OwnerField : " + (ownerField != null ? ownerField.Name : "(不明)"));
        }
        catch (Exception) { w("  OwnerField : (取得できません)"); }

        w("  Owner      : " + (m.Owner != null ? m.Owner.ClassName + " / " + m.Owner.Name : "(なし)"));

        if (cls != null)
        {
            w("  Fields:");
            foreach (var f in cls.GetFields().Cast<IField>())
            {
                var sb = new StringBuilder();
                sb.Append("    ").Append(Pad(f.Name, 30));
                sb.Append(" kind=").Append(f.IsEmbedded ? "所有" : (f.IsReference ? "参照" : "値  "));
                sb.Append(" type=").Append(Pad(f.Type, 24));
                sb.Append(" mult=").Append(f.LowerBound).Append("..")
                  .Append(f.UpperBound < 0 ? "*" : f.UpperBound.ToString());

                if (!f.IsEmbedded && !f.IsReference)
                {
                    string value = null;
                    try { value = m.GetFieldString(f.Name); }
                    catch (Exception) { }
                    if (!string.IsNullOrEmpty(value)) sb.Append(" value='").Append(Shorten(value)).Append("'");
                }
                w(sb.ToString());
            }
        }
        w("");
    }
    private static string Pad(string s, int width)
    {
        var t = s ?? "";
        return t.Length >= width ? t : t + new string(' ', width - t.Length);
    }

    private static string Shorten(string s)
    {
        var t = PlantUmlText.Inline(PlantUmlText.Normalize(s));
        return t.Length <= 40 ? t : t.Substring(0, 40) + "…";
    }
}

