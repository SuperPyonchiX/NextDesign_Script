// ============================================================
//  Part 3 / ワークスペースの構築
// ============================================================

public static class WorkspaceBuilder
{
    // セッションフォルダ一式を作り、SessionInfo を返す
    // （design\ の中身＝design.md と diagrams\ 配下の .puml はエクスポータが後から書く）
    public static SessionInfo Build(string workspaceRoot, IModel root, AgentConfig config)
    {
        var baseName = AgentText.SafeFileName(root.Name);
        if (baseName.Length == 0) baseName = "design";
        var folder = Path.Combine(workspaceRoot, baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));

        var session = new SessionInfo
        {
            Folder = folder,
            Agent = config.Agent,
            RootModel = root.Name,
            Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            State = "preparing"
        };

        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(session.DesignDir());
        Directory.CreateDirectory(session.ReviewDir());
        Directory.CreateDirectory(Path.Combine(session.ReviewDir(), "proposed"));

        var utf8 = new UTF8Encoding(false);

        // エージェントを後から切り替えても動くよう、指示書は両方の名前で置く
        var instructions = BuildInstructions(root.Name, config);
        File.WriteAllText(Path.Combine(folder, "CLAUDE.md"), instructions, utf8);
        File.WriteAllText(Path.Combine(folder, "AGENTS.md"), instructions, utf8);

        session.Save();
        return session;
    }

    private static string BuildInstructions(string rootName, AgentConfig config)
    {
        var nl = "\n";
        var perspectives = (config.Perspectives ?? "")
            .Split(',')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        var sb = new StringBuilder();
        sb.Append("# 設計レビュー指示書（Next Design AgentReview）").Append(nl).Append(nl);
        sb.Append("あなたはソフトウェア設計のレビュアーです。このフォルダは Next Design の").Append(nl);
        sb.Append("プロジェクト「").Append(rootName).Append("」からエクスポートされた設計レビュー用ワークスペースです。").Append(nl).Append(nl);

        sb.Append("## 入力（読み取り専用）").Append(nl).Append(nl);
        sb.Append("- `design/design.md` : Next Design からエクスポートした設計情報（モデル階層・フィールド・ドキュメント本文）").Append(nl);
        sb.Append("- `design/diagrams/<種別>/**/*.puml` : 図の PlantUML（種別フォルダ: クラス図 / シーケンス図 / 状態遷移図。design.md の該当箇所に参照行がある）").Append(nl);
        sb.Append("- `design/_index.md` : 図一覧（図名・種別・ファイル・モデルパスの対応表）").Append(nl);
        sb.Append("- `design/Attachment/` : 設計の別紙（Excel 等。存在する場合）。design.md に無い情報の参照先として活用すること").Append(nl).Append(nl);
        sb.Append("design.md にはシーケンス図・状態遷移図の中身は含まれない。挙動は参照先の .puml を読むこと。").Append(nl).Append(nl);
        sb.Append("**`design/` 配下のファイルを変更・削除してはならない。** 入力の原本である。").Append(nl);
        sb.Append("`design/Attachment/` はレビュー開始時に固定コピーした資料です。").Append(nl);
        sb.Append("固定した入力を変更するとレビューの再現性が失われる。読み取りのみとすること。").Append(nl).Append(nl);

        sb.Append("## 出力（このフォルダ規約に従うこと）").Append(nl).Append(nl);
        sb.Append("- `review/review.md` : レビュー指摘の一覧。次の表形式で書く。").Append(nl);
        sb.Append("  `| No | 重要度(高/中/低) | 工程 | 対象（モデルパスまたは図名） | 指摘 | 根拠（観点） | 修正方針 |`").Append(nl);
        sb.Append("- `review/proposal.md` : 修正提案。指摘 No と対応付け、修正後の設計を具体的に書く").Append(nl);
        sb.Append("- `review/proposed/<種別>/**/*.puml` : 修正後の図（図の変更を提案する場合）").Append(nl).Append(nl);
        sb.Append("修正後の図は入力の design/diagrams/ 以下の相対パスを保って review/proposed/ 以下に置く。").Append(nl);
        sb.Append("入力の図は _index.md または design.md の参照から選び、旧出力の残存ファイルを無差別に読まない。").Append(nl).Append(nl);
        sb.Append("指摘・提案の対象参照は Next Design のモデルパスと内容で示すこと。モデルパスは").Append(nl);
        sb.Append("design.md の各見出し直下の `<!-- modelpath: ... -->` コメントに記載がある").Append(nl);
        sb.Append("（図の指摘は `_index.md` のモデルパス＋図名）。ユーザーは Next Design 上でしか").Append(nl);
        sb.Append("指摘個所を辿れないため、design.md 等の変換後ファイルの行番号で参照を書いてはならない。").Append(nl).Append(nl);
        sb.Append("Next Design のモデルを直接編集することはできない。提案は必ず上記ファイルに書く。").Append(nl);
        sb.Append("修正提案はユーザーが Next Design 上で手作業で反映できる粒度（対象モデルパス・").Append(nl);
        sb.Append("フィールド名・変更前後の値）まで具体化すること。").Append(nl).Append(nl);

        sb.Append("## レビューの進め方").Append(nl).Append(nl);
        sb.Append("レビューは **design-review スキル**に従うこと。スキルとして認識できない環境では").Append(nl);
        sb.Append("`.agents/skills/design-review/SKILL.md` を読み、その手順に従うこと。").Append(nl);
        sb.Append("`.agents/skills/` と `.claude/skills/` は拡張機能のチーム共通スキルを直接参照している。").Append(nl);
        sb.Append("リンク先を含め、スキルのファイルを変更・削除してはならない。読み取りのみとすること。").Append(nl);
        sb.Append("要点: session.ini の phase は開始画面で確定済み。工程を再質問せず、").Append(nl);
        sb.Append("工程別の観点表（`.agents/skills/design-review/references/`）を適用してレビューする。工程情報のない旧セッションだけはユーザーに質問する。").Append(nl).Append(nl);

        if (perspectives.Count > 0)
        {
            sb.Append("## 追加のレビュー観点").Append(nl).Append(nl);
            sb.Append("工程別観点に加えて次も確認すること:").Append(nl);
            foreach (var p in perspectives)
                sb.Append("- ").Append(p).Append(nl);
            sb.Append(nl);
        }

        return sb.ToString();
    }
}

// ------------------------------------------------------------
//  ファイルシステムのリンク・コピー
// ------------------------------------------------------------
public static class FsLink
{
    // ジャンクションは管理者権限なしで作れる（シンボリックリンクは要権限のため使わない）
    public static bool TryCreateJunction(string link, string target)
    {
        string error;
        return TryCreateJunction(link, target, out error);
    }

    public static bool TryCreateJunction(string link, string target, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /v:off /c mklink /J \"%ND_FS_LINK%\" \"%ND_FS_TARGET%\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // パス中の % や ! を cmd の変数として再展開させない。
            psi.EnvironmentVariables["ND_FS_LINK"] = link;
            psi.EnvironmentVariables["ND_FS_TARGET"] = target;
            using (var process = Process.Start(psi))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(3000))
                {
                    try { process.Kill(); } catch (Exception) { }
                    error = "ジャンクション作成がタイムアウトしました。";
                    return false;
                }
                if (process.ExitCode == 0 && Directory.Exists(link)) return true;
                error = "mklink 終了コード: " + process.ExitCode
                    + "\n" + stderr.Result.Trim() + "\n" + stdout.Result.Trim();
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
        foreach (var sub in Directory.GetDirectories(src))
            CopyDirectory(sub, Path.Combine(dst, Path.GetFileName(sub)));
    }
}

// ------------------------------------------------------------
//  同梱 skills の参照（ユーザー用の複製や埋め込みは持たない）
// ------------------------------------------------------------
public static class SkillProvisioner
{
    public static string SourceDir(string extensionPath)
    {
        if (string.IsNullOrWhiteSpace(extensionPath) || !Path.IsPathRooted(extensionPath))
            throw new InvalidOperationException("拡張機能の配置パスを取得できませんでした。");
        return Path.Combine(extensionPath, "skills");
    }

    public static void ValidateSource(string skillsDir)
    {
        var reviewDir = Path.Combine(skillsDir, "design-review");
        var required = new[] {
            Path.Combine(reviewDir, "SKILL.md"),
            Path.Combine(reviewDir, "references", "requirements-review.md"),
            Path.Combine(reviewDir, "references", "architecture-review.md"),
            Path.Combine(reviewDir, "references", "detailed-design-review.md"),
            Path.Combine(reviewDir, "references", "upstream-review.md")
        };
        foreach (var path in required)
            if (!File.Exists(path))
                throw new FileNotFoundException("同梱スキルが不足しています。拡張機能一式を再配置してください。\n" + path, path);
    }

    public static void LinkToSession(string sessionFolder, string skillsDir)
    {
        ValidateSource(skillsDir);
        foreach (var agentDir in new[] { ".agents", ".claude" })
        {
            var parent = Path.Combine(sessionFolder, agentDir);
            Directory.CreateDirectory(parent);
            var link = Path.Combine(parent, "skills");
            string error;
            if (!FsLink.TryCreateJunction(link, skillsDir, out error))
                throw new IOException("共通スキルへのジャンクションを作成できませんでした。"
                    + "\nリンク元: " + link + "\nリンク先: " + skillsDir + "\n" + error);
        }
    }
}
