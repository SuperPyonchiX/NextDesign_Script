// ------------------------------------------------------------
//  出力ペインの表示
// ------------------------------------------------------------
public static class OutputPane
{
    public static void Show(IApplication app, string category)
    {
        // CurrentOutputCategory は未登録のカテゴリを渡すと
        // 「値域外の値」例外になるため、先に 1 行書いて登録してから切り替える
        app.Output.WriteLine(category, "");
        app.Output.Clear(category);
        app.Window.IsInformationPaneVisible = true;
        app.Window.ActiveInfoWindow = "Output";
        try { app.Window.CurrentOutputCategory = category; }
        catch (Exception) { }   // カテゴリ切替に失敗しても処理は続行できる
    }
}

