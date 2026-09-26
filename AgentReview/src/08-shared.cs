// ============================================================
//  NdMcp と共有する部品
//  NdMcp.csproj も 01-common.cs / 05-markdown-export.cs と一緒にこのファイルをビルドする。
// ============================================================

public static partial class ReviewSnapshot
{
    public static string Cell(string value)
    {
        return (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("|", "&#124;").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
    }
}

// 共通エクスポータ（05-markdown-export.cs）が参照する比較レコード
public sealed class ChangeRecord
{
    public string Key = "", Parent = "", Name = "", Kind = "", Path = "", File = "", Content = "";
}

public static class DesignArtifactWriter
{
    // design.md / diagrams\<種別>\<階層>\*.puml / _index.md を outDir へ書き出し、警告と統計を Output に出す
    public static void Write(IApplication app, string category, MarkdownExporter exporter, IModel root, string outDir)
    {
        var markdown = exporter.Export(root);

        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(outDir, "design.md"), markdown, utf8);
        // 図が0件でも索引を更新し、前回の参照を残さない。
        {
            var index = new StringBuilder();
            index.Append("# 図一覧\n\n");
            index.Append("| 図名 | 種別 | ファイル | モデルパス |\n");
            index.Append("|---|---|---|---|\n");
            foreach (var row in exporter.IndexRows) index.Append(row).Append('\n');
            File.WriteAllText(Path.Combine(outDir, "_index.md"), index.ToString(), utf8);
        }

        var omissions = new StringBuilder("# 図の未確認一覧\n\n取得できなかった図は空図・変更なし・問題なしとは判定していません。\n\n");
        foreach (var skipped in exporter.SkippedDiagrams) {
            omissions.Append("- ").Append(ReviewSnapshot.Cell(skipped)).Append('\n');
            app.Output.WriteLine(category, "[info] 図の未確認: " + skipped);
        }
        if (exporter.SkippedDiagrams.Count == 0) omissions.Append("スキップした図はありません。\n");
        File.WriteAllText(Path.Combine(outDir, "unverified-diagrams.md"), omissions.ToString(), utf8);
        File.AppendAllText(Path.Combine(outDir, "_index.md"), "\n[図の未確認一覧](unverified-diagrams.md)\n", utf8);
        foreach (var warning in exporter.Warnings)
            app.Output.WriteLine(category, "[warn]  " + warning);
        app.Output.WriteLine(category, "[info]  モデル " + exporter.ModelCount + " 件を design.md に出力");
        app.Output.WriteLine(category, "[info]  図 " + exporter.DiagramCount + " 件を diagrams\\<種別>\\<階層>\\*.puml に出力"
            + (exporter.SkippedModelCount > 0
                ? "（図の構成要素 " + exporter.SkippedModelCount + " モデルはテキスト出力から除外）" : ""));
    }
}
// ---- ここまで NdMcp と共有
