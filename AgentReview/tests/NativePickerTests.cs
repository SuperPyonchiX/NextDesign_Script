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
    private static void Exercise()
    {
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
