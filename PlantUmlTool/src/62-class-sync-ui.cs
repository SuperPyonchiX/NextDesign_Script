// ============================================================
//  Part 9 / クラス図同期のリボン側（結果表示と診断ファイル）
//
//    同期本体（Part 9 の前半、60-class-sync.cs / 61-class-sync-runtime.cs）は
//    ClassImportProbe で実機検証したものをそのまま置いている。ここは本体が
//    参照する結果置き場（ClassExperiment）だけ。NdMcp では同名のクラスを
//    ダイアログ無しの版に差し替えて同じ本体を使う。
// ============================================================

public static class ClassExperiment
{
    public const string Version = "0.7.2";
    public const string Title = "PlantUML 連携 / クラス図同期 " + Version;
    public static string Summary = "クラス図を開き「差分を検証」または「PlantUMLを反映」を押してください。";
    public static string Details = "まだ実行していません。";
    public static void Show(IApplication app) { app.Window.UI.ShowInformationDialog(Summary, Title); }
    public static void Write(string path, string text)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
    }
    // 診断にはモデル名と ID が含まれる。この PC に残すだけで、リポジトリへは入れない。
    public static string SaveReport(string kind, string log, string reportJson, string currentPuml)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NextDesign.ClassSync", "reports");
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + kind + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Write(stem + ".txt", log);
            if (reportJson != null) Write(stem + ".json", reportJson);
            if (currentPuml != null) Write(stem + "_current.puml", currentPuml);
            return stem;
        }
        catch (Exception ex)
        {
            Summary += "\n診断ファイルを保存できませんでした: " + ex.Message;
            return null;
        }
    }
}
