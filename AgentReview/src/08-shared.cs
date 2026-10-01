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
    public const string ManifestFile = "export-manifest.tsv", PathsFile = "paths.tsv";

    // design.md（大きければ model/ 配下のページ群）/ paths.tsv / diagrams\<種別>\<階層>\*.puml / _index.md を
    // outDir へ書き出し、警告と統計を Output に出す。戻り値は今回書いたファイル（outDir からの相対、/ 区切り）。
    public static List<string> Write(IApplication app, string category, MarkdownExporter exporter, IModel root, string outDir)
    {
        var pages = exporter.ExportPages(root, outDir);

        var utf8 = new UTF8Encoding(false);
        var written = new List<string>();
        var manifest = new StringBuilder("# AgentReview が出力した Markdown（再出力時の整理に使う）\n");
        foreach (var page in pages)
        {
            var path = Path.Combine(outDir, page.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, page.Content, utf8);
            written.Add(page.Path);
            manifest.Append(page.Path).Append('\t').Append(Sha256(page.Content)).Append('\n');
        }

        // 短い ID → Next Design のモデルパス。指摘の場所はここから引く。
        {
            var paths = new StringBuilder("id\tmodelId\tmodelPath\tfile\n");
            foreach (var model in exporter.Models)
                paths.Append(model.ShortId).Append('\t').Append(Tsv(model.ModelId)).Append('\t')
                     .Append(Tsv(model.Path)).Append('\t').Append(Tsv(model.File)).Append('\n');
            File.WriteAllText(Path.Combine(outDir, PathsFile), paths.ToString(), utf8);
            written.Add(PathsFile);
        }

        // 図が0件でも索引を更新し、前回の参照を残さない。
        {
            var index = new StringBuilder();
            index.Append("# 図一覧\n\n");
            index.Append("| 図名 | 種別 | ファイル | モデルパス |\n");
            index.Append("|---|---|---|---|\n");
            foreach (var row in exporter.IndexRows) index.Append(row).Append('\n');
            index.Append("\n[図の未確認一覧](unverified-diagrams.md)\n");
            File.WriteAllText(Path.Combine(outDir, "_index.md"), index.ToString(), utf8);
            written.Add("_index.md");
        }

        var omissions = new StringBuilder("# 図の未確認一覧\n\n取得できなかった図は空図・変更なし・問題なしとは判定していません。\n\n");
        foreach (var skipped in exporter.SkippedDiagrams) {
            omissions.Append("- ").Append(ReviewSnapshot.Cell(skipped)).Append('\n');
            app.Output.WriteLine(category, "[info] 図の未確認: " + skipped);
        }
        if (exporter.SkippedDiagrams.Count == 0) omissions.Append("スキップした図はありません。\n");
        File.WriteAllText(Path.Combine(outDir, "unverified-diagrams.md"), omissions.ToString(), utf8);
        written.Add("unverified-diagrams.md");
        written.AddRange(exporter.DiagramFiles);

        // 前回このフォルダへ出力したページのうち、今回なくなったものを消す（図の .puml は従来どおり残す）
        RemoveStalePages(app, category, outDir, pages.Select(p => p.Path).ToList());
        File.WriteAllText(Path.Combine(outDir, ManifestFile), manifest.ToString(), utf8);
        written.Add(ManifestFile);

        foreach (var warning in exporter.Warnings)
            app.Output.WriteLine(category, "[warn]  " + warning);
        app.Output.WriteLine(category, "[info]  モデル " + exporter.ModelCount + " 件を design.md"
            + (pages.Count > 1 ? " ほか " + (pages.Count - 1) + " ファイル（" + MarkdownExporter.PageFolder + "\\ 配下）" : "") + " に出力");
        app.Output.WriteLine(category, "[info]  図 " + exporter.DiagramCount + " 件を diagrams\\<種別>\\<階層>\\*.puml に出力"
            + (exporter.SkippedModelCount > 0
                ? "（図の構成要素 " + exporter.SkippedModelCount + " モデルはテキスト出力から除外）" : ""));
        return written;
    }

    // 前回の manifest にあり今回出力しなかったページを消す。手で編集されたもの（ハッシュ不一致）は残して知らせる。
    private static void RemoveStalePages(IApplication app, string category, string outDir, List<string> current)
    {
        var manifestPath = Path.Combine(outDir, ManifestFile);
        if (!File.Exists(manifestPath)) return;
        var keep = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(manifestPath, Encoding.UTF8))
        {
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            var parts = line.Split('\t');
            if (parts.Length != 2 || keep.Contains(parts[0])) continue;
            // 管理対象はページフォルダ配下だけ。.. や絶対パスは扱わない。
            if (!parts[0].StartsWith(MarkdownExporter.PageFolder + "/", StringComparison.Ordinal) || parts[0].Contains("..")
                || !parts[0].EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(outDir, parts[0].Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            if (Sha256(File.ReadAllText(path, Encoding.UTF8)) != parts[1])
            {
                app.Output.WriteLine(category, "[warn]  前回出力後に編集されたため残しました: " + parts[0]);
                continue;
            }
            File.Delete(path);
        }
        var pageRoot = Path.Combine(outDir, MarkdownExporter.PageFolder);
        if (!Directory.Exists(pageRoot)) return;
        foreach (var dir in Directory.GetDirectories(pageRoot, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        if (!Directory.EnumerateFileSystemEntries(pageRoot).Any()) Directory.Delete(pageRoot);
    }

    private static string Sha256(string text)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false).GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }

    private static string Tsv(string value)
    {
        return (value ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }
}
// ---- ここまで NdMcp と共有
