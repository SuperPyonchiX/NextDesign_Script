// ============================================================
//  Part F / モデル編集の確認ボタン（NdMcp 固有部。src/editcheck.cs）
//
//  MCP サーバーを使わずに、Next Design の中からモデル編集 API（ModelEditApi）を試す。
//  コマンドプロンプトや HTTP を使えない PC での確認用。
//    ・書ける項目を出力 … 選んでいるモデルの /model と /model/schema の内容を JSON に書き、
//                          表（所有フィールド）があれば行を 1 つ足す編集 JSON のひな形も作る
//    ・編集 JSON を実行 … 選んだ JSON（{operations:[...], dryRun}）を /model/edit と同じ処理で実行する
//  出力は %USERPROFILE%\.nd-mcp\edit-check\ に置く（モデル名と ID を含むのでリポジトリへは入れない）。
// ============================================================

public partial class NdMcpExtension
{
    const string EditCheckTitle = "NdMcp / モデル編集の確認";

    static string EditCheckDir()
    {
        var dir = Path.Combine(NdMcpConfig.ConfigDir(), "edit-check");
        Directory.CreateDirectory(dir);
        return dir;
    }

    static IModel SelectedModel(IApplication app)
    {
        try
        {
            var page = app.Window.EditorPage;
            if (page != null && page.CurrentNavigator != null)
            {
                var selected = page.CurrentNavigator.SelectedItems.OfType<IModel>().FirstOrDefault();
                if (selected != null) return selected;
            }
        }
        catch (Exception) { }
        return app.Workspace.CurrentModel;
    }

    public void WriteEditSchema(ICommandContext context, ICommandParams parameters)
    {
        var app = context.App;
        try
        {
            var m = SelectedModel(app);
            if (m == null) { app.Window.UI.ShowInformationDialog("モデルナビゲータで、編集したいモデル（例: 改訂履歴一覧）を選んでから押してください。", EditCheckTitle); return; }
            var model = ModelApi.Model(app, "", m.Id);
            var schema = ModelEditApi.Schema(app, "", m.Id);
            string stem = Path.Combine(EditCheckDir(), DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + AgentText.SafeFileName(m.Name ?? "model"));
            File.WriteAllText(stem + "_model.json", Pretty(Json.Write(model)), new UTF8Encoding(false));
            File.WriteAllText(stem + "_schema.json", Pretty(Json.Write(schema)), new UTF8Encoding(false));
            string template = Template(app, m);
            if (template != null) File.WriteAllText(stem + "_edit.json", template, new UTF8Encoding(false));
            app.Window.UI.ShowInformationDialog("「" + m.Name + "」の内容を書き出しました。\n\n"
                + stem + "_model.json（今の値・子モデルの id）\n" + stem + "_schema.json（書ける項目）\n"
                + (template != null ? stem + "_edit.json（行を 1 つ足す編集のひな形。値を書き換えて「編集 JSON を実行」で選ぶ）" : "（表＝所有フィールドが無いので編集のひな形は作っていません）"), EditCheckTitle);
        }
        catch (Exception ex) { app.Window.UI.ShowInformationDialog("書き出せませんでした: " + ex.Message, EditCheckTitle); }
    }

    // An "add a row" edit for the first owned field of the model: the columns come from an existing
    // row's fields (values as they are, rich text as Markdown), so only the values need changing.
    static string Template(IApplication app, IModel m)
    {
        if (m.Metaclass == null) return null;
        var field = m.Metaclass.GetFields().Cast<IField>().FirstOrDefault(f => f != null && f.IsEmbedded && f.TypeClass != null && f.Name != null && !AgentText.IsSystemName(f.Name));
        if (field == null) return null;
        var rows = m.GetFieldValues(field.Name).Cast<object>().OfType<IModel>().Where(r => !r.IsDeleted).ToList();
        var values = new JsonObject(); var rich = new JsonObject();
        if (rows.Count > 0)
            foreach (JsonObject f in ModelApi.Fields(rows[rows.Count - 1]).OfType<JsonObject>())
            {
                var kind = f["kind"] as string; var name = f["name"] as string;
                if (kind == "value") values.Set(name, f["value"] ?? "");
                else if (kind == "richtext") rich.Set(name, f["value"] ?? "");
            }
        var add = new JsonObject().Set("op", "add").Set("parent", new JsonObject().Set("id", m.Id)).Set("field", field.Name);
        if (values.Count > 0) add.Set("fields", values);
        if (rich.Count > 0) add.Set("richtext", rich);
        add.Set("as", "newRow");
        var body = new JsonObject().Set("dryRun", true).Set("operations", new List<object> { add });
        return Pretty(Json.Write(body));
    }

    public void RunEditJson(ICommandContext context, ICommandParams parameters)
    {
        var app = context.App;
        string path = app.Window.UI.ShowOpenFileDialog("実行する編集 JSON（{\"operations\":[...],\"dryRun\":true}）", "JSON (*.json)|*.json");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var body = ClassJsonNode.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
            if (body == null || body.Properties == null) throw new InvalidOperationException("JSON オブジェクトではありません");
            bool dry = body["dryRun"] != null && body["dryRun"].Raw == "true";
            if (!dry && !app.Window.UI.ShowConfirmDialog("dryRun が true ではないので、モデルを書き換えて確定します（Ctrl+Z で戻せます。保存はしません）。\n\nOK: 実行する\nキャンセル: 中止する", EditCheckTitle)) return;
            var result = (JsonObject)ModelEditApi.Edit(app, body);
            string outPath = Path.Combine(EditCheckDir(), DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_result.json");
            File.WriteAllText(outPath, Pretty(Json.Write(result)), new UTF8Encoding(false));
            try { app.Window.EditorPage.UpdateEditors(); } catch (Exception) { }
            bool ok = result["ok"] is bool && (bool)result["ok"];
            app.Window.UI.ShowInformationDialog((ok ? (dry ? "試行しました（dryRun のため取り消し済み）。" : "編集を確定しました。Ctrl+Z で戻せます。")
                : "失敗しました。何も変わっていません。\n" + result["failedIndex"] + " 番目の操作: " + result["error"])
                + "\n\n結果: " + outPath, EditCheckTitle);
        }
        catch (NdMcpHttpError ex) { app.Window.UI.ShowInformationDialog("実行できませんでした: " + ex.Message, EditCheckTitle); }
        catch (Exception ex) { app.Window.UI.ShowInformationDialog("実行できませんでした: " + ex.Message, EditCheckTitle); }
    }

    // Indents compact JSON for reading in an editor.
    static string Pretty(string json)
    {
        var sb = new StringBuilder(); int depth = 0; bool inString = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];
            if (inString) { sb.Append(c); if (c == '\\' && i + 1 < json.Length) sb.Append(json[++i]); else if (c == '"') inString = false; continue; }
            switch (c)
            {
                case '"': inString = true; sb.Append(c); break;
                case '{': case '[': sb.Append(c).Append('\n').Append(' ', ++depth * 2); break;
                case '}': case ']': sb.Append('\n').Append(' ', --depth * 2).Append(c); break;
                case ',': sb.Append(c).Append('\n').Append(' ', depth * 2); break;
                case ':': sb.Append(": "); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
