// ============================================================
//  Part E / モデル編集 API（NdMcp 固有部。src/modeledit.cs）
//
//  UML 以外のモデル（フィールドの値、リッチテキスト、表の行＝所有フィールドの子モデル、
//  参照）を AI が編集するための窓口。すべて SDK のモデル操作（SetField / SetRichTextField /
//  AddNewModel / MoveTo / Relate / Delete）で行い、エディタの取込は使わないので、
//  保存していなくても Ctrl+Z で戻せる（シーケンス図の取込とは違う）。
//
//    GET  /model/schema?path=&id=   書き込めるフィールド・列挙値・追加できるクラス
//    POST /model/edit {operations:[...], dryRun?}
//         操作をまとめて 1 つのトランザクションで実行する。1 つでも失敗したら全部取り消す。
//         dryRun は実行して結果を読んだあと必ず取り消す。
//
//  操作（target / parent / to は {path} {id} {ref} のどれか。ref は同じ要求の add の "as"）:
//    {op:"set", target, fields:{名前: 値}}                  文字列・数値・真偽・列挙（リテラル名）
//    {op:"set_richtext", target, field, markdown|html}      リッチテキスト（Markdown は HTML に変換）
//    {op:"add", parent, field, class?, before?|after?|index?, fields?, richtext?:{名前: markdown}, as?}
//    {op:"delete", target}
//    {op:"move", target, parent?, field?, before?|after?|index?}
//    {op:"relate" | "unrelate", target, field, to}
//  シーケンス図などエディタの中でしか編集できないモデルは、製品が拒否する（その操作で失敗する）。
// ============================================================

public static class ModelEditApi
{
    static string S(ClassJsonNode n) { return n == null || n.Raw == null ? null : n.Raw.StartsWith("\"", StringComparison.Ordinal) ? n.StringValue() : n.Raw; }

    static IProject Project(IApplication app)
    {
        var p = app == null ? null : app.Workspace.CurrentProject;
        if (p == null) throw new NdMcpHttpError(409, "プロジェクトが開かれていません");
        return p;
    }

    static IField Field(IModel m, string name)
    {
        if (m.Metaclass == null) throw new InvalidOperationException("メタクラスを取得できません: " + ModelApi.PathOf(m));
        var f = m.Metaclass.GetFields().Cast<IField>().FirstOrDefault(x => x != null && x.Name == name);
        if (f == null)
        {
            var names = m.Metaclass.GetFields().Cast<IField>().Where(x => x != null && x.Name != null && !AgentText.IsSystemName(x.Name)).Select(x => x.Name);
            throw new InvalidOperationException("フィールド '" + name + "' がありません（" + m.ClassName + "）。フィールド: " + string.Join(", ", names));
        }
        return f;
    }

    static IEnumerable<IClass> Concrete(IClass declared)
    {
        var all = new List<IClass> { declared };
        try { all.AddRange(declared.GetAllSubClasses().Cast<IClass>()); } catch (Exception) { }
        return all.Where(c => c != null && !c.IsAbstract).GroupBy(c => c.Id).Select(g => g.First()).ToList();
    }

    static string Kind(IField f)
    {
        if (f.Type == "RichText") return "richtext";
        if (f.IsEmbedded && f.TypeClass != null) return "embedded";
        if (f.IsReference) return "reference";
        return "value";
    }

    // ---- GET /model/schema ----
    public static object Schema(IApplication app, string path, string id)
    {
        var m = ModelApi.ResolveModel(app, path, id);
        return ModelApi.Summary(m).Set("editable", m.IsEditable).Set("fields", FieldsOf(m.Metaclass, true));
    }

    // Each field's name, type and kind; for an owned field, the classes a row can be and (one level
    // down) the columns of such a row, so a table can be filled or edited without another lookup.
    static List<object> FieldsOf(IClass cls, bool withRows)
    {
        var fields = new List<object>();
        if (cls == null) return fields;
        foreach (var f in cls.GetFields().Cast<IField>())
        {
            if (f == null || f.Name == null || AgentText.IsSystemName(f.Name)) continue;
            var e = new JsonObject().Set("name", f.Name).Set("type", f.Type).Set("kind", Kind(f)).Set("multiple", f.UpperBound != 1);
            try { if (f.TypeEnum != null) e.Set("literals", f.TypeEnum.Literals.Select(l => (object)l.Name).ToList()); } catch (Exception) { }
            if (f.TypeClass != null)
            {
                e.Set("typeClass", f.TypeClass.FullName);
                if (f.IsEmbedded)
                    e.Set("addableClasses", Concrete(f.TypeClass).Select(c =>
                    {
                        var row = new JsonObject().Set("name", c.Name).Set("fullName", c.FullName);
                        if (withRows) row.Set("fields", FieldsOf(c, false));
                        return (object)row;
                    }).ToList());
            }
            fields.Add(e);
        }
        return fields;
    }

    // ---- POST /model/edit ----
    public static object Edit(IApplication app, ClassJsonNode body)
    {
        var project = Project(app);
        var ops = body["operations"];
        if (ops == null || ops.Items == null || ops.Items.Count == 0) throw new NdMcpHttpError(400, "operations に操作の配列を指定してください");
        if (ops.Items.Count > 500) throw new NdMcpHttpError(400, "operations は 500 件以下にしてください");
        bool dryRun = body["dryRun"] != null && body["dryRun"].Raw == "true";
        var refs = new Dictionary<string, IModel>(StringComparer.Ordinal);
        var results = new List<object>();
        var touched = new List<IModel>();
        var transaction = project.BeginUndoTransaction(false);
        if (transaction == null) throw new NdMcpHttpError(500, "トランザクションを開始できません");
        int index = 0;
        string failure = null;
        try
        {
            for (; index < ops.Items.Count; index++)
            {
                var op = ops.Items[index];
                var r = Apply(app, op, refs, touched);
                results.Add(r.Set("index", index));
            }
        }
        catch (Exception ex) { failure = ex.Message; }
        // Read back before completing, so a dry run still shows what the edit made.
        var after = touched.Where(m => m != null && !m.IsDeleted).GroupBy(m => m.Id).Select(g => g.First())
            .Select(m => (object)ModelApi.Summary(m).Set("fields", ModelApi.Fields(m))).ToList();
        bool committed = false;
        if (failure == null && !dryRun) { transaction.Commit(); committed = true; }
        else { try { transaction.Rollback(); } catch (Exception ex) { failure = (failure ?? "") + " / 取消に失敗: " + ex.Message; } }
        var result = new JsonObject().Set("ok", failure == null).Set("dryRun", dryRun).Set("committed", committed)
            .Set("results", results).Set("models", after);
        if (failure != null) result.Set("failedIndex", index).Set("error", failure)
            .Set("note", "1 つでも失敗したときは全部取り消した。プロジェクトは変わっていない。");
        else if (committed) result.Set("undo", "Next Design の Ctrl+Z で、この編集全体を 1 回で戻せる。保存はしていない。");
        return result;
    }

    static IModel Target(IApplication app, ClassJsonNode spec, Dictionary<string, IModel> refs, string what)
    {
        if (spec == null) throw new InvalidOperationException(what + " を {path} / {id} / {ref} で指定してください");
        var r = S(spec["ref"]);
        if (!string.IsNullOrEmpty(r))
        {
            IModel m;
            if (!refs.TryGetValue(r, out m)) throw new InvalidOperationException("ref '" + r + "' は、この要求の前の add の as にありません");
            return m;
        }
        // Both empty would resolve to the project itself; an edit must name its model.
        if (string.IsNullOrEmpty(S(spec["path"])) && string.IsNullOrEmpty(S(spec["id"])))
            throw new InvalidOperationException(what + " の path / id / ref が空です");
        try { return ModelApi.ResolveModel(app, S(spec["path"]) ?? "", S(spec["id"]) ?? ""); }
        catch (NdMcpHttpError e) { throw new InvalidOperationException(e.Message); }
    }

    static JsonObject Apply(IApplication app, ClassJsonNode op, Dictionary<string, IModel> refs, List<IModel> touched)
    {
        var kind = S(op["op"]) ?? "";
        switch (kind)
        {
            case "set":
            {
                var m = Target(app, op["target"], refs, "target");
                var fields = op["fields"];
                if (fields == null || fields.Properties == null) throw new InvalidOperationException("set には fields を指定してください");
                foreach (var pair in fields.Properties) SetValue(m, pair.Key, pair.Value);
                touched.Add(m);
                return ModelApi.Summary(m).Set("op", kind);
            }
            case "set_richtext":
            {
                var m = Target(app, op["target"], refs, "target");
                var field = S(op["field"]);
                if (string.IsNullOrEmpty(field)) throw new InvalidOperationException("set_richtext には field を指定してください");
                SetRichText(m, field, S(op["markdown"]), S(op["html"]));
                touched.Add(m);
                return ModelApi.Summary(m).Set("op", kind);
            }
            case "add":
            {
                var parent = Target(app, op["parent"], refs, "parent");
                var fieldName = S(op["field"]);
                if (string.IsNullOrEmpty(fieldName)) throw new InvalidOperationException("add には field（子を持つ所有フィールド）を指定してください");
                var f = Field(parent, fieldName);
                if (!f.IsEmbedded || f.TypeClass == null) throw new InvalidOperationException("'" + fieldName + "' は子モデルを持つフィールドではありません（" + Kind(f) + "）");
                var cls = ChooseClass(f, S(op["class"]));
                IModel made;
                var anchor = Position(app, parent, fieldName, op, refs);
                if (anchor != null) made = parent.AddNewModelAt(f, cls, anchor.Item1, anchor.Item2);
                else made = parent.AddNewModel(f, cls);
                if (made == null) throw new InvalidOperationException("モデルを追加できませんでした: " + fieldName);
                var fields = op["fields"];
                if (fields != null && fields.Properties != null) foreach (var pair in fields.Properties) SetValue(made, pair.Key, pair.Value);
                var rich = op["richtext"];
                if (rich != null && rich.Properties != null) foreach (var pair in rich.Properties) SetRichText(made, pair.Key, S(pair.Value), null);
                var name = S(op["as"]);
                if (!string.IsNullOrEmpty(name)) refs[name] = made;
                touched.Add(made); touched.Add(parent);
                return ModelApi.Summary(made).Set("op", kind).Set("class", cls.Name);
            }
            case "delete":
            {
                var m = Target(app, op["target"], refs, "target");
                var summary = ModelApi.Summary(m);
                var owner = m.Owner;
                m.Delete();
                if (owner != null) touched.Add(owner);
                return summary.Set("op", kind);
            }
            case "move":
            {
                var m = Target(app, op["target"], refs, "target");
                var parent = op["parent"] != null ? Target(app, op["parent"], refs, "parent") : m.Owner;
                string fieldName = S(op["field"]);
                if (string.IsNullOrEmpty(fieldName)) { IField own = null; try { own = m.GetOwnerField(); } catch (Exception) { } fieldName = own == null ? null : own.Name; }
                if (parent == null || string.IsNullOrEmpty(fieldName)) throw new InvalidOperationException("move の移動先（parent / field）を決められません");
                var anchor = Position(app, parent, fieldName, op, refs);
                if (anchor != null) m.MoveTo(parent, fieldName, anchor.Item1, anchor.Item2);
                else m.MoveTo(parent, fieldName, "last", 0);
                touched.Add(m); touched.Add(parent);
                return ModelApi.Summary(m).Set("op", kind);
            }
            case "relate":
            case "unrelate":
            {
                var m = Target(app, op["target"], refs, "target");
                var field = S(op["field"]);
                if (string.IsNullOrEmpty(field)) throw new InvalidOperationException(kind + " には field（参照フィールド）を指定してください");
                var f = Field(m, field);
                if (!f.IsReference) throw new InvalidOperationException("'" + field + "' は参照フィールドではありません（" + Kind(f) + "）");
                var to = Target(app, op["to"], refs, "to");
                if (kind == "relate") m.Relate(field, to); else m.UnRelate(field, to);
                touched.Add(m);
                return ModelApi.Summary(m).Set("op", kind).Set("to", ModelApi.Summary(to));
            }
            default:
                throw new InvalidOperationException("不明な op: '" + kind + "'（set / set_richtext / add / delete / move / relate / unrelate）");
        }
    }

    // before / after（同じフィールドの兄弟モデル）か index（0 始まり）。無ければ null（末尾）。
    static Tuple<string, int> Position(IApplication app, IModel parent, string field, ClassJsonNode op, Dictionary<string, IModel> refs)
    {
        foreach (var direction in new[] { "before", "after" })
        {
            if (op[direction] == null) continue;
            var sibling = Target(app, op[direction], refs, direction);
            var list = parent.GetFieldValues(field).Cast<object>().OfType<IModel>().ToList();
            int at = list.FindIndex(x => x.Id == sibling.Id);
            if (at < 0) throw new InvalidOperationException(direction + " のモデルが '" + field + "' の中にありません: " + ModelApi.PathOf(sibling));
            return Tuple.Create(direction, at);
        }
        var index = S(op["index"]);
        int i;
        if (!string.IsNullOrEmpty(index) && int.TryParse(index, out i))
        {
            var count = parent.GetFieldValues(field).Cast<object>().Count();
            if (i <= 0) return count == 0 ? null : Tuple.Create("before", 0);
            if (i >= count) return null;
            return Tuple.Create("before", i);
        }
        return null;
    }

    static IClass ChooseClass(IField f, string wanted)
    {
        var candidates = Concrete(f.TypeClass).ToList();
        if (string.IsNullOrEmpty(wanted))
        {
            if (candidates.Count == 1) return candidates[0];
            throw new InvalidOperationException("'" + f.Name + "' に追加できるクラスが複数あります。class で指定してください: " + string.Join(", ", candidates.Select(c => c.Name)));
        }
        var hit = candidates.Where(c => c.Name == wanted || c.FullName == wanted).ToList();
        if (hit.Count == 1) return hit[0];
        throw new InvalidOperationException("class '" + wanted + "' は '" + f.Name + "' に追加できません。候補: " + string.Join(", ", candidates.Select(c => c.Name)));
    }

    static void SetValue(IModel m, string name, ClassJsonNode value)
    {
        var f = Field(m, name);
        var kind = Kind(f);
        if (kind == "richtext") { SetRichText(m, name, S(value), null); return; }
        if (kind != "value") throw new InvalidOperationException("'" + name + "' は " + kind + " フィールドなので set では変えられません（" + (kind == "embedded" ? "add / delete" : "relate / unrelate") + " を使う）");
        if (value != null && value.Items != null) throw new InvalidOperationException("'" + name + "' に配列は設定できません（複数値のフィールドの一括設定は未対応）");
        string raw = value == null ? null : value.Raw;
        object v;
        string type = (f.Type ?? "").ToLowerInvariant();
        if (raw == null || raw == "null") v = null;
        else if (f.TypeEnum != null)
        {
            var text = S(value);
            var literal = f.TypeEnum.Literals.FirstOrDefault(l => string.Equals(l.Name, text, StringComparison.Ordinal))
                ?? f.TypeEnum.Literals.FirstOrDefault(l => string.Equals(l.Name, text, StringComparison.OrdinalIgnoreCase));
            if (literal == null) throw new InvalidOperationException("'" + name + "' の値 '" + text + "' は列挙にありません: " + string.Join(", ", f.TypeEnum.Literals.Select(l => l.Name)));
            v = literal.Name;
        }
        else if (type == "boolean" || type == "bool")
        {
            var text = S(value);
            bool b;
            if (!bool.TryParse(text, out b)) throw new InvalidOperationException("'" + name + "' は真偽値です: " + text);
            v = b;
        }
        else if (type.Contains("int") || type == "long" || type == "short")
        {
            long n;
            if (!long.TryParse(S(value), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n)) throw new InvalidOperationException("'" + name + "' は整数です: " + raw);
            v = type.Contains("64") || type == "long" ? (object)n : (int)n;
        }
        else if (type.Contains("double") || type.Contains("float") || type.Contains("decimal") || type == "number")
        {
            double d;
            if (!double.TryParse(S(value), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) throw new InvalidOperationException("'" + name + "' は数値です: " + raw);
            v = d;
        }
        else v = S(value);
        m.SetField(name, v);
    }

    static void SetRichText(IModel m, string name, string markdown, string html)
    {
        var f = Field(m, name);
        if (f.Type != "RichText") throw new InvalidOperationException("'" + name + "' はリッチテキストではありません（" + f.Type + "）");
        if (html == null) html = MarkdownHtml.Convert(markdown ?? "");
        m.SetRichTextField(name, html, MarkdownHtml.PlainText(html), "html");
    }
}

// AI が書く Markdown を、リッチテキストに入れる HTML にする。見出し・段落・箇条書き（入れ子）・
// 番号付き・表・引用・コードブロック・太字・斜体・コード・リンクを扱う。それ以外は文字のまま残す。
public static class MarkdownHtml
{
    static string Esc(string s) { return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }

    public static string Inline(string text)
    {
        var s = Esc(text);
        s = Regex.Replace(s, @"`([^`]+)`", "<code>$1</code>");
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        s = Regex.Replace(s, @"(?<![\*\w])\*(?!\s)(.+?)(?<!\s)\*(?!\*)", "<em>$1</em>");
        s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)\s]+)\)", "<a href=\"$2\">$1</a>");
        return s;
    }

    static string[] Cells(string row)
    {
        var t = row.Trim();
        if (t.StartsWith("|")) t = t.Substring(1);
        if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
        return t.Split('|').Select(c => c.Trim()).ToArray();
    }

    public static string Convert(string markdown)
    {
        var lines = (markdown ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var html = new StringBuilder();
        var para = new List<string>();
        Action flush = () => { if (para.Count > 0) { html.Append("<p>").Append(string.Join("<br/>", para.Select(Inline))).Append("</p>"); para.Clear(); } };
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0) { flush(); continue; }
            if (trimmed.StartsWith("```"))
            {
                flush();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```"); i++) code.Add(lines[i]);
                html.Append("<pre><code>").Append(Esc(string.Join("\n", code))).Append("</code></pre>");
                continue;
            }
            var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.*)$");
            if (heading.Success) { flush(); int n = heading.Groups[1].Length; html.Append("<h" + n + ">").Append(Inline(heading.Groups[2].Value)).Append("</h" + n + ">"); continue; }
            if (trimmed.StartsWith("|") && i + 1 < lines.Length && Regex.IsMatch(lines[i + 1].Trim(), @"^\|?\s*:?-{2,}"))
            {
                flush();
                html.Append("<table><tr>");
                foreach (var c in Cells(trimmed)) html.Append("<th>").Append(Inline(c)).Append("</th>");
                html.Append("</tr>");
                for (i += 2; i < lines.Length && lines[i].Trim().StartsWith("|"); i++)
                {
                    html.Append("<tr>");
                    foreach (var c in Cells(lines[i])) html.Append("<td>").Append(Inline(c)).Append("</td>");
                    html.Append("</tr>");
                }
                i--;
                html.Append("</table>");
                continue;
            }
            if (trimmed.StartsWith(">"))
            {
                flush();
                var quote = new List<string>();
                for (; i < lines.Length && lines[i].Trim().StartsWith(">"); i++) quote.Add(lines[i].Trim().Substring(1).TrimStart());
                i--;
                html.Append("<blockquote>").Append(Convert(string.Join("\n", quote))).Append("</blockquote>");
                continue;
            }
            if (Regex.IsMatch(line, @"^\s*([-*+]|\d+[.)])\s+"))
            {
                flush();
                int end = i;
                while (end < lines.Length && lines[end].Trim().Length > 0 && (Regex.IsMatch(lines[end], @"^\s*([-*+]|\d+[.)])\s+") || lines[end].StartsWith("  "))) end++;
                html.Append(List(lines.Skip(i).Take(end - i).ToList()));
                i = end - 1;
                continue;
            }
            para.Add(trimmed);
        }
        flush();
        return html.ToString();
    }

    static int Indent(string s) { int n = 0; foreach (var c in s) { if (c == ' ') n++; else if (c == '\t') n += 4; else break; } return n; }

    // Items at the first line's indent; deeper lines belong to the item above them.
    static string List(List<string> lines)
    {
        if (lines.Count == 0) return "";
        int baseIndent = Indent(lines[0]);
        bool ordered = Regex.IsMatch(lines[0], @"^\s*\d+[.)]\s+");
        var sb = new StringBuilder(ordered ? "<ol>" : "<ul>");
        for (int i = 0; i < lines.Count; i++)
        {
            var m = Regex.Match(lines[i], @"^\s*(?:[-*+]|\d+[.)])\s+(.*)$");
            var text = m.Success ? m.Groups[1].Value : lines[i].Trim();
            var nested = new List<string>();
            while (i + 1 < lines.Count && Indent(lines[i + 1]) > baseIndent) nested.Add(lines[++i]);
            sb.Append("<li>").Append(Inline(text));
            if (nested.Count > 0)
            {
                if (Regex.IsMatch(nested[0], @"^\s*([-*+]|\d+[.)])\s+")) sb.Append(List(nested));
                else sb.Append("<br/>").Append(string.Join("<br/>", nested.Select(x => Inline(x.Trim()))));
            }
            sb.Append("</li>");
        }
        sb.Append(ordered ? "</ol>" : "</ul>");
        return sb.ToString();
    }

    // The text value stored beside the HTML (search and plain display use it).
    public static string PlainText(string html)
    {
        var s = Regex.Replace(html ?? "", @"<br\s*/?>|</p>|</li>|<ul>|<ol>|</tr>|</h\d>|</pre>|</blockquote>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"</t[dh]>", "\t", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
        return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
    }
}
