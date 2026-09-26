// ============================================================
//  Part 0 / 共通ヘルパ
// ============================================================

public static class AgentText
{
    // 連続する空白を 1 つに畳んで前後を除去する
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        var space = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!space && sb.Length > 0) sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(ch);
                space = false;
            }
        }
        return sb.ToString().Trim();
    }

    // プロファイルが自動生成するシステム・匿名フィールド名（$ / ___ 始まり）か
    public static bool IsSystemName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.StartsWith("$", StringComparison.Ordinal)
            || s.StartsWith("___", StringComparison.Ordinal);
    }

    public static string SafeFileName(string s)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        var sb = new StringBuilder();
        foreach (var ch in (s ?? ""))
            sb.Append(invalid.Contains(ch) || ch == ' ' ? '_' : ch);
        return sb.ToString().Trim('_', '.');
    }
}

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
