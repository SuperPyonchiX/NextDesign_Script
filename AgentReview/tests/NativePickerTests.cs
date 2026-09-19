public static class NativePickerTests
{
    private static int checks;
    private static void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks++; }
    private static int Count(object target, string collection) { return (int)ReviewNativeDialog.Get(ReviewNativeDialog.Get(target, collection), "Count"); }
    public static void Run()
    {
        Exception failure = null;
        var thread = new System.Threading.Thread(delegate() {
            try { Exercise(); } catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw new Exception("Native UI regression", failure);
        Console.WriteLine("PASS: " + checks + " native Forms assertions (no PowerShell or external UI process).");
    }
    private static void RenderChange(ChangeDialog dialog, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AGENTREVIEW_CHANGE_RENDER");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        ReviewNativeDialog.Set(dialog.Form, "Opacity", 0d); ReviewNativeDialog.Set(dialog.Form, "ShowInTaskbar", false);
        ReviewNativeDialog.Call(dialog.Form, "Show");
        var drawing = System.Reflection.Assembly.Load("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
        var width = (int)ReviewNativeDialog.Get(dialog.Form, "Width"); var height = (int)ReviewNativeDialog.Get(dialog.Form, "Height");
        var bitmap = Activator.CreateInstance(drawing.GetType("System.Drawing.Bitmap"), new object[] { width, height });
        try {
            var rect = Activator.CreateInstance(drawing.GetType("System.Drawing.Rectangle"), new object[] { 0, 0, width, height });
            ReviewNativeDialog.Call(dialog.Form, "DrawToBitmap", bitmap, rect);
            ReviewNativeDialog.Call(bitmap, "Save", Path.Combine(directory, name));
        } finally { ((IDisposable)bitmap).Dispose(); }
    }
    private static void Exercise()
    {
        using (var dialog = new ChangeDialog("対象選択", new List<string> { "新規成果物", "親", "親/詳細設計", "別工程" }, new List<int> { -1, -1, 1, -1 })) {
            Check(Count(dialog.List, "Nodes") == 3, "Historical picker displays hierarchy");
            RenderChange(dialog, "model-tree.png");
            ReviewNativeDialog.Set(dialog.Search, "Text", "詳細設計");
            Check(Count(dialog.List, "Nodes") == 1, "Tree search retains ancestor");
            var roots = ReviewNativeDialog.Get(dialog.List, "Nodes");
            var parent = roots.GetType().GetProperty("Item", new[] { typeof(int) }).GetValue(roots, new object[] { 0 });
            Check(Count(parent, "Nodes") == 1, "Matching child retained");
            var children = ReviewNativeDialog.Get(parent, "Nodes");
            var child = children.GetType().GetProperty("Item", new[] { typeof(int) }).GetValue(children, new object[] { 0 });
            ReviewNativeDialog.Set(dialog.List, "SelectedNode", child);
            dialog.Accept.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(dialog.Accept, new object[] { EventArgs.Empty });
            Check(dialog.Selected == 2, "Tree selection returns original model index");
        }
        using (var dialog = new ChangeDialog("履歴", new List<string> { "2026-09-01 abc123 初版", "2026-09-02 def456 改訂" })) {
            RenderChange(dialog, "commits.png");
            ReviewNativeDialog.Set(dialog.Search, "Text", "def456");
            Check(Count(dialog.List, "Items") == 1, "Commit search filters displayed items");
            ReviewNativeDialog.Set(dialog.List, "SelectedIndex", 0);
            dialog.Accept.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(dialog.Accept, new object[] { EventArgs.Empty });
            Check(dialog.Selected == 1, "Filtered commit index preserved");
        }
        var configPath = AgentConfig.ConfigPath();
        var original = File.ReadAllText(configPath);
        var existing = AgentConfig.Load();
        using (var settings = new AgentSettingsDialog(existing)) {
            var settingsRender = Environment.GetEnvironmentVariable("AGENTREVIEW_SETTINGS_RENDER");
            if (!string.IsNullOrEmpty(settingsRender)) {
                ReviewNativeDialog.Set(settings.Form, "Opacity", 0d);
                ReviewNativeDialog.Set(settings.Form, "ShowInTaskbar", false);
                ReviewNativeDialog.Call(settings.Form, "Show");
                var drawing = System.Reflection.Assembly.Load("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
                var bitmap = Activator.CreateInstance(drawing.GetType("System.Drawing.Bitmap"), new object[] { (int)ReviewNativeDialog.Get(settings.Form, "Width"), (int)ReviewNativeDialog.Get(settings.Form, "Height") });
                try {
                    var rect = Activator.CreateInstance(drawing.GetType("System.Drawing.Rectangle"), new object[] { 0, 0, (int)ReviewNativeDialog.Get(settings.Form, "Width"), (int)ReviewNativeDialog.Get(settings.Form, "Height") });
                    ReviewNativeDialog.Call(settings.Form, "DrawToBitmap", bitmap, rect);
                    ReviewNativeDialog.Call(bitmap, "Save", settingsRender);
                } finally { ((IDisposable)bitmap).Dispose(); }
            }
            Check((int)ReviewNativeDialog.Get(settings.FieldControl("Agent"), "SelectedIndex") == (existing.Agent == "claude" ? 1 : 0), "Existing agent restored");
            ReviewNativeDialog.Set(settings.FieldControl("Perspectives"), "Text", "保存しない変更");
            Check(File.ReadAllText(configPath) == original && settings.Result == null, "Editing settings does not save");
        }
        Check(File.ReadAllText(configPath) == original, "Closing settings without save preserves file");
        using (var settings = new AgentSettingsDialog(existing)) {
            ReviewNativeDialog.Set(settings.FieldControl("WorkspaceRoot"), "Text", "relative-missing");
            settings.SaveButton.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(settings.SaveButton, new object[] { EventArgs.Empty });
            Check(settings.Result == null && ((string)ReviewNativeDialog.Get(settings.ErrorLabel, "Text")).Contains("保存先"), "Invalid folder shown inline");
            Check(File.ReadAllText(configPath) == original, "Invalid settings do not overwrite file");
            ReviewNativeDialog.Set(settings.FieldControl("WorkspaceRoot"), "Text", existing.WorkspaceRoot);
            ReviewNativeDialog.Set(settings.FieldControl("Agent"), "SelectedIndex", 1);
            ReviewNativeDialog.Set(settings.FieldControl("Terminal"), "SelectedIndex", 2);
            ReviewNativeDialog.Set(settings.FieldControl("Perspectives"), "Text", "境界値,排他制御");
            var candidate = settings.ReadValues();
            Check(candidate.Agent == "claude" && candidate.Terminal == "cmd" && candidate.Perspectives == "境界値,排他制御", "Controls map to config");
            Check(candidate.CodexCommand == existing.CodexCommand && candidate.ClaudeArgs == existing.ClaudeArgs && candidate.InitialPrompt == existing.InitialPrompt, "Advanced settings retained");
            settings.SaveButton.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(settings.SaveButton, new object[] { EventArgs.Empty });
            Check(settings.Result != null && AgentConfig.Load().Perspectives == "境界値,排他制御", "Save persists and accepts");
        }
        File.WriteAllText(configPath, original);
        var request = new System.Xml.XmlDocument();
        request.LoadXml("<request><target>設計/日本語 &amp; target</target><choices>"
            + "<model id='root' parent='' name='プロジェクト' path='プロジェクト' available='true'/>"
            + "<model id='a' parent='root' name='同名' path='プロジェクト/A/同名' available='true'/>"
            + "<model id='b' parent='root' name='同名' path='プロジェクト/B/同名' available='true'/>"
            + "<model id='proxy' parent='root' name='未ロード' path='未ロード' available='false'/>"
            + "</choices><settings><phase key='detailed'><model>a</model></phase>"
            + "<phase key='architecture'><model>b</model></phase></settings></request>");
        foreach (var action in new[] { "accept", "none", "cancel" }) {
            using (var dialog = new ReviewNativeDialog(request)) {
                Check(!(bool)ReviewNativeDialog.Get(dialog.AcceptButton, "Enabled"), "Phase required");
                ReviewNativeDialog.Set(dialog.Combo, "SelectedIndex", 2);
                Check((bool)ReviewNativeDialog.Get(dialog.AcceptButton, "Enabled") && Count(dialog.Selection, "Items") == 1, "Detailed restored");
                var render = Environment.GetEnvironmentVariable("AGENTREVIEW_RENDER");
                if (action == "accept" && !string.IsNullOrEmpty(render)) {
                    ReviewNativeDialog.Set(dialog.Form, "Opacity", 0d);
                    ReviewNativeDialog.Set(dialog.Form, "ShowInTaskbar", false);
                    ReviewNativeDialog.Call(dialog.Form, "Show");
                    var drawing = System.Reflection.Assembly.Load("System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
                    int width = (int)ReviewNativeDialog.Get(dialog.Form, "Width"), height = (int)ReviewNativeDialog.Get(dialog.Form, "Height");
                    var bitmap = Activator.CreateInstance(drawing.GetType("System.Drawing.Bitmap"), new object[] { width, height });
                    try {
                        var rectangle = Activator.CreateInstance(drawing.GetType("System.Drawing.Rectangle"), new object[] { 0, 0, width, height });
                        ReviewNativeDialog.Call(dialog.Form, "DrawToBitmap", bitmap, rectangle);
                        ReviewNativeDialog.Call(bitmap, "Save", render);
                    } finally { ((IDisposable)bitmap).Dispose(); ReviewNativeDialog.Call(dialog.Form, "Hide"); }
                }

                ReviewNativeDialog.Set(dialog.Combo, "SelectedIndex", 1);
                var first = ((System.Collections.IEnumerable)ReviewNativeDialog.Get(dialog.Selection, "Items")).Cast<object>().First();
                Check((string)ReviewNativeDialog.Get(first, "Tag") == "m:b", "Architecture restored");
                ReviewNativeDialog.Set(dialog.Combo, "SelectedIndex", 2);
                ReviewNativeDialog.Set(dialog.SearchBox, "Text", "B/同名");
                Check(Count(dialog.Tree, "Nodes") == 1, "Search retains parent");
                var parent = ((System.Collections.IEnumerable)ReviewNativeDialog.Get(dialog.Tree, "Nodes")).Cast<object>().First();
                Check(Count(parent, "Nodes") == 1, "Search filters other branches");
                var child = ((System.Collections.IEnumerable)ReviewNativeDialog.Get(parent, "Nodes")).Cast<object>().First();
                ReviewNativeDialog.Set(child, "Checked", true);
                Check(Count(dialog.Selection, "Items") == 2, "Native AfterCheck event adds selection");
                ReviewNativeDialog.Set(dialog.SearchBox, "Text", "");
                ReviewNativeDialog.Set(dialog.Combo, "SelectedIndex", 1); ReviewNativeDialog.Set(dialog.Combo, "SelectedIndex", 2);
                Check(Count(dialog.Selection, "Items") == 2, "Search and phase switch preserve edits");
                if (action == "cancel") { Check(dialog.Result == null, "Close/cancel has no result"); continue; }
                dialog.Accept(action == "none");
                Check(dialog.Result.DocumentElement.GetAttribute("phase") == "detailed", "Confirmed phase retained");
                Check(dialog.Result.SelectNodes("/result/selection/model").Count == (action == "none" ? 0 : 2), "Correct export selection");
                Check(dialog.Result.SelectNodes("/result/settings/phase[@key='detailed']/model").Count == (action == "none" ? 1 : 2), "None preserves prior settings");
            }
        }
        var detailed = request.SelectSingleNode("/request/settings/phase[@key='detailed']/model");
        detailed.InnerText = "deleted";
        using (var dialog = new ReviewNativeDialog(request)) {
            ReviewNativeDialog.Set(dialog.Combo, "SelectedIndex", 2);
            Check(!(bool)ReviewNativeDialog.Get(dialog.AcceptButton, "Enabled"), "Deleted selection blocks accept");
            dialog.Accept(false); Check(dialog.Result == null, "Invalid selection not accepted");
        }
        detailed.InnerText = "proxy";
        using (var dialog = new ReviewNativeDialog(request)) {
            ReviewNativeDialog.Set(dialog.Combo, "SelectedIndex", 2);
            Check(!(bool)ReviewNativeDialog.Get(dialog.AcceptButton, "Enabled"), "Unloaded selection blocks accept");
        }
        detailed.InnerText = "a";
        var file = request.CreateElement("file"); file.InnerText = "Z:\\missing-agentreview-file.pdf";
        detailed.ParentNode.AppendChild(file);
        using (var dialog = new ReviewNativeDialog(request)) {
            ReviewNativeDialog.Set(dialog.Combo, "SelectedIndex", 2);
            Check(!(bool)ReviewNativeDialog.Get(dialog.AcceptButton, "Enabled"), "Missing file blocks accept");
        }
    }
}
