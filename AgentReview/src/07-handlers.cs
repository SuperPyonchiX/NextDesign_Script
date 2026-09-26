// ============================================================
//  Part 6 / コマンドハンドラ
// ============================================================

public partial class AgentReviewExtension
{
    public void StartAgentReview(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        SessionInfo session = null;
        try
        {
            var config = AgentConfig.Load();
            var profile = config.ActiveProfile();

            // 未表示エディタ配下でも最新値を取得できるようにする（バッチでは必須）
            context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

            var root = ResolveRoot(app);
            if (root == null)
            {
                app.Window.UI.ShowInformationDialog("プロジェクトが開かれていません。", category);
                return;
            }

            var project = app.Workspace.CurrentProject;
            var inputs = ReviewInputPicker.Show(project, root);
            if (inputs == null) return;
            var upperModels = ReviewInputPicker.ResolveModels(project, inputs);
            // 基点フォルダが未設定なら選ばせて設定に記憶する
            if (string.IsNullOrEmpty(config.WorkspaceRoot) || !Directory.Exists(config.WorkspaceRoot))
            {
                app.Window.UI.ShowInformationDialog(
                    "レビューセッションを作成する基点フォルダを選択してください。\n"
                    + "（設定に記憶され、次回からは選択不要になります）", category);
                var selected = app.Window.UI.ShowSelectFolderDialog("基点フォルダの選択");
                if (string.IsNullOrEmpty(selected)) return;
                config.WorkspaceRoot = selected;
                config.Save();
            }

            OutputPane.Show(app, category);
            app.Output.WriteLine(category, "=== レビュー開始 : " + root.Name + " (" + profile.DisplayName + ") ===");

            app.Output.WriteLine(category, "[1/3] ワークスペースを作成しています...");
            var skillsDir = SkillProvisioner.SourceDir(context.ExtensionInfo.ExtensionPath);
            SkillProvisioner.ValidateSource(skillsDir);
            session = WorkspaceBuilder.Build(config.WorkspaceRoot, root, config);
            session.Mode = "review";
            session.Phase = inputs.Phase;
            session.Save();
            app.Output.WriteLine(category, "[dir]   " + session.Folder);
            SkillProvisioner.LinkToSession(session.Folder, skillsDir);
            app.Output.WriteLine(category, "[info]  共通スキルへ接続（.agents/skills、.claude/skills → " + skillsDir + "）");

            app.Output.WriteLine(category, "[2/3] 設計情報と図をエクスポートしています...");
            var exporter = new MarkdownExporter(new MarkdownExportOptions(), session.DesignDir(),
                DiagramGroupRules.Load(config.DiagramGroupsRulesFile));
            WriteDesignArtifacts(app, category, exporter, root, session.DesignDir());

            WriteReviewInputs(app, config, project, root, session, inputs, upperModels, exporter);
            ReviewSnapshot.AppendInstructions(session.Folder, session.Mode);
            session.State = "ready";
            session.Save();

            app.Output.WriteLine(category, "[3/3] ターミナルで " + profile.DisplayName + " を起動しています...");
            TerminalLauncher.Launch(session.Folder, profile.BuildLaunchCommand(string.IsNullOrEmpty(config.InitialPrompt) ? "" : config.InitialPrompt
                + "。session.ini の phase はユーザーが確定済みです。工程を再質問せず、その工程でレビューしてください。"), config.Terminal);

            app.Output.WriteLine(category, "");
            app.Output.WriteLine(category, "=== 起動完了 ===");
            app.Output.WriteLine(category, string.IsNullOrEmpty(config.InitialPrompt)
                ? "ターミナルで「レビューして」と入力すると、指示書（" + profile.InstructionFileName + "）に従いレビューが始まります。"
                : "起動と同時に「" + config.InitialPrompt + "」が自動投入され、design-review スキルに従いレビューが始まります（選択した工程で開始します）。");
            app.Output.WriteLine(category, "指摘は review\\review.md、修正提案は review\\proposal.md に出力されます（リボンの「結果を開く」で参照）。");
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            if (session != null && session.State != "ready")
            {
                try { session.State = "failed"; session.Save(); }
                catch (Exception saveError) { app.Output.WriteLine(category, "[error] 失敗状態の保存: " + saveError.Message); }
            }
            app.Window.UI.ShowInformationDialog("レビュー開始に失敗しました。\n\n" + ex.Message, category);
        }
    }

    private void WriteReviewInputs(IApplication app, AgentConfig config, IProject project, IModel root,
        SessionInfo session, ReviewInputs inputs, List<IModel> upperModels, MarkdownExporter exporter)
    {
        var inventory = new StringBuilder("# レビュー入力\n\n");
        if (!string.IsNullOrEmpty(session.Phase)) inventory.Append("- レビュー工程（選択済み）: ").Append(ReviewInputPicker.PhaseLabel(session.Phase)).Append("\n");
        inventory.Append("- 種別: ").Append(session.Mode).Append("\n- 取得日時 (UTC): ")
            .Append(DateTime.UtcNow.ToString("o")).Append("\n- プロジェクト: ").Append(ReviewSnapshot.Cell(project.Path))
            .Append("\n- 対象: ").Append(ReviewSnapshot.Cell(root.ModelPath)).Append(" [")
            .Append(ReviewSnapshot.Cell(root.Id)).Append("]\n")
            .Append("- 版: 現在開いているモデル（未保存の編集を含む）。Gitコミット時点の出力ではない。\n")
            .Append("- 指定範囲の外にある要求・依存関係の網羅性は未確認。\n\n");
        AppendExportWarnings(inventory, "対象設計", exporter);
        inventory.Append("\n## 上位モデル\n\n");
        inventory.Append("- 上位文書の設定状態: ").Append(inputs == null ? "未設定" : inputs.UpstreamState).Append('\n');
        if (inputs != null && inputs.IntentionalNone)
        {
            if (string.IsNullOrEmpty(inputs.NoneConfirmedAt))
                throw new InvalidOperationException("今回の上位文書なしでの続行が確認されていません。");
            inventory.Append("- 指定しない理由: ").Append(ReviewSnapshot.Cell(inputs.NoneReason))
                .Append("\n- 今回の確認: 上位文書なしで続行するとユーザーが回答。\n- 今回の確認日時 (UTC): ")
                .Append(inputs.NoneConfirmedAt)
                .Append("\n- 上位整合: 未確認。保存された理由だけで適合・対象外と判定しない。\n");
        }
        else if (upperModels.Count == 0 && (inputs == null || inputs.Files.Count == 0))
            inventory.Append("- 上位文書未指定のため整合は未確認。対象外・適合として扱わない。\n"
                + "- 上位文書なしの続行: 開始前にユーザーが選択。\n");
        for (var i = 0; i < upperModels.Count; i++)
        {
            var model = upperModels[i];
            if (session.Mode == "change" && new[] { model }.Concat(model.GetAllChildren()).Any(m => m.IsProxy || m.IsDeleted))
                throw new IOException("上位文書に未ロード・削除済みモデルが含まれます。");
            var relative = "upstream/models/" + (i + 1).ToString("D3");
            var directory = Path.Combine(session.Folder, relative);
            Directory.CreateDirectory(directory);
            var upperExporter = new MarkdownExporter(new MarkdownExportOptions(), directory,
                DiagramGroupRules.Load(config.DiagramGroupsRulesFile));
            WriteDesignArtifacts(app, "AgentReview", upperExporter, model, directory);
            inventory.Append("- [").Append(ReviewSnapshot.Cell(model.ModelPath)).Append("](")
                .Append(relative).Append("/design.md) / ID: ").Append(ReviewSnapshot.Cell(model.Id)).Append('\n');
            AppendExportWarnings(inventory, model.ModelPath, upperExporter);
            if (session.Mode == "change" && upperExporter.Warnings.Count > 0) throw new IOException("上位文書の出力に警告があります。出力ログを確認してください。");
        }
        inventory.Append("\n## 固定コピーした資料\n\n| 出典 | コピー先 | SHA-256 |\n|---|---|---|\n");
        if (inputs != null)
        {
            for (var i = 0; i < inputs.Files.Count; i++)
            {
                var file = inputs.Files[i];
                var relative = "upstream/files/" + (i + 1).ToString("D3") + "/" + Path.GetFileName(file);
                string sha = null;
                if (session.Mode == "change") ChangeDialog.Work("上位資料を固定しています", token => sha = ReviewSnapshot.CopyFile(file, Path.Combine(session.Folder, relative), token));
                else sha = ReviewSnapshot.CopyFile(file, Path.Combine(session.Folder, relative));
                inventory.Append("| ").Append(ReviewSnapshot.Cell(file)).Append(" → ").Append(ReviewSnapshot.Cell(ReviewSnapshot.ResolvePath(file))).Append(" | ")
                    .Append(ReviewSnapshot.Cell(relative)).Append(" | ").Append(sha).Append(" |\n");
            }
        }
        if (!string.IsNullOrEmpty(project.Path))
        {
            var attachment = Path.Combine(Path.GetDirectoryName(project.Path), "Attachment");
            if (Directory.Exists(attachment)) {
                if (session.Mode == "change") ChangeDialog.Work("現在版の添付資料を固定しています", token => ReviewSnapshot.CopyTree(attachment, Path.Combine(session.DesignDir(), "Attachment"), inventory, "design/Attachment", token));
                else ReviewSnapshot.CopyTree(attachment, Path.Combine(session.DesignDir(), "Attachment"), inventory, "design/Attachment");
            }
            else inventory.Append("\nAttachment: フォルダなし。\n");
        }
        else inventory.Append("\nAttachment: プロジェクトの保存先が未確定のため取得なし。\n");
        File.WriteAllText(Path.Combine(session.Folder, "inputs.md"), inventory.ToString(), new UTF8Encoding(false));
    }

    private void AppendExportWarnings(StringBuilder inventory, string name, MarkdownExporter exporter)
    {
        foreach (var skipped in exporter.SkippedDiagrams)
            inventory.Append("- 図の未確認（").Append(ReviewSnapshot.Cell(name)).Append("）: ").Append(ReviewSnapshot.Cell(skipped)).Append("。図の内容の変更有無は判断できません。\n");
        foreach (var warning in exporter.Warnings)
            inventory.Append("- 出力警告（").Append(ReviewSnapshot.Cell(name)).Append("）: ")
                .Append(ReviewSnapshot.Cell(warning)).Append("。該当範囲は判断不能。\n");
    }

    public void StartChangeReview(ICommandContext context, ICommandParams commandParams)
    {
        var app = context.App; SessionInfo session = null; IProject historical = null;
        bool owns = false; string copiedProject = null; string originalPath = null; string originalId = null;
        int changes = 0; bool prepared = false;
        try {
            context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;
            var current = app.Workspace.CurrentProject; var target = ResolveRoot(app);
            if (current == null || string.IsNullOrWhiteSpace(current.Path)) throw new InvalidOperationException("保存済みのGit管理プロジェクトを開いてください。");
            if (target == null || target.Id == current.Id || target.IsDeleted || target.IsProxy) throw new InvalidOperationException("比較する工程成果物のモデルを選択してください。");
            originalPath = ProbeProjectPath(current); originalId = current.Id;
            GitChange git = null;
            ChangeDialog.Work("Gitリポジトリを確認", token => git = new GitChange(Path.GetDirectoryName(originalPath), token));
            var prefix = Path.GetFullPath(git.Root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!originalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("プロジェクトがGitリポジトリ内にありません。");
            var relativeProject = originalPath.Substring(prefix.Length).Replace('\\', '/');
            // A saved but untracked current project is not a reliable comparison entry point.
            git.Text("ls-files", "--error-unmatch", "--", relativeProject);
            var inputs = ReviewInputPicker.Show(current, target); if (inputs == null) return;
            var upperModels = ReviewInputPicker.ResolveModels(current, inputs);
            var commit = ChangeDialog.Commit(git); if (commit == null) return;
            var config = AgentConfig.Load();
            if (string.IsNullOrWhiteSpace(config.WorkspaceRoot) || !Directory.Exists(config.WorkspaceRoot)) {
                var selected = app.Window.UI.ShowSelectFolderDialog("レビュー保存先を選択"); if (string.IsNullOrWhiteSpace(selected)) return;
                config.WorkspaceRoot = selected; config.Save();
            }
            OutputPane.Show(app, "AgentReview");
            session = WorkspaceBuilder.Build(config.WorkspaceRoot, target, config);
            session.Mode = "change"; session.State = "preparing"; session.Phase = inputs.Phase;
            session.BaselineCommit = commit; session.CurrentTargetId = target.Id; session.TargetMapping = "id"; session.Stage = "現在版の固定"; session.Save();
            SkillProvisioner.ValidateSource(SkillProvisioner.SourceDir(context.ExtensionInfo.ExtensionPath));
            SkillProvisioner.LinkToSession(session.Folder, SkillProvisioner.SourceDir(context.ExtensionInfo.ExtensionPath));
            app.Output.WriteLine("AgentReview", "[1/4] 現在版を固定しています: " + session.Folder);
            var after = ExportChangeTarget(app, config, target, session.DesignDir());
            WriteReviewInputs(app, config, current, target, session, inputs, upperModels, after);
            ChangeDiff.Attachments(Path.Combine(session.DesignDir(), "Attachment"), after.Comparison);
            ChangeDiff.SaveIndex(session.DesignDir(), after.Comparison);
            var baseline = Path.Combine(session.Folder, "baseline"); var bundle = Path.Combine(baseline, "project");
            session.Stage = "過去版の取得"; session.Save();
            app.Output.WriteLine("AgentReview", "[2/4] 過去コミットを取得しています: " + commit);
            ChangeDialog.Work("過去版を取得しています", token => git.Extract(commit, bundle, token));
            copiedProject = Path.Combine(bundle, relativeProject.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(copiedProject)) {
                var candidates = Directory.GetFiles(bundle, "*", SearchOption.AllDirectories)
                    .Where(f => new[] { ".nproj", ".iproj" }.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f => f).ToList();
                if (candidates.Count == 0) throw new IOException("過去版にプロジェクトファイルがありません。");
                int selected = ChangeDialog.Choose("過去版のプロジェクトを選択", candidates.Select(f => f.Substring(bundle.Length + 1)).ToList());
                if (selected < 0) throw new OperationCanceledException(); copiedProject = candidates[selected];
            }
            copiedProject = ReviewSnapshot.ResolvePath(copiedProject);
            session.Stage = "過去版の読込・対象選択"; session.Save();
            historical = app.Workspace.OpenProject(copiedProject, false, false);
            owns = historical != null && string.Equals(ProbeProjectPath(historical), copiedProject, StringComparison.OrdinalIgnoreCase);
            if (!owns) throw new IOException("過去版を独立したプロジェクトとして取得できませんでした。");
            if (!ProbeMatchesProject(app.Workspace.CurrentProject, originalPath, originalId)) throw new IOException("カレントプロジェクトが変化したため停止しました。");
            var all = historical.GetAllChildren().ToList(); var matches = all.Where(m => m.Id == target.Id).ToList(); IModel oldTarget = null;
            if (matches.Count == 1 && !matches[0].IsDeleted && !matches[0].IsProxy) oldTarget = matches[0];
            else if (matches.Count > 0) throw new IOException("対応モデルが重複、削除済み、または未ロードです。");
            else {
                var available = all.Where(m => !m.IsDeleted && !m.IsProxy).OrderBy(m => m.ModelPath, StringComparer.Ordinal).ToList();
                var labels = new List<string> { "過去版にはない新規成果物" }; labels.AddRange(available.Select(m => m.ModelPath + "  [" + m.Id + "]"));
                var byId = new Dictionary<string, int>();
                for (int i = 0; i < available.Count; i++) { if (byId.ContainsKey(available[i].Id)) throw new IOException("過去版のモデルIDが重複しています。"); byId.Add(available[i].Id, i + 1); }
                var parents = new List<int> { -1 };
                foreach (var model in available) { int parent; parents.Add(model.Owner != null && byId.TryGetValue(model.Owner.Id, out parent) ? parent : -1); }
                int selected = ChangeDialog.ChooseModel("過去版の対応成果物を選択（現在: " + target.ModelPath + "）", labels, parents);
                if (selected < 0) throw new OperationCanceledException();
                if (selected == 0) session.TargetMapping = "new";
                else { oldTarget = available[selected - 1]; session.TargetMapping = "manual"; }
            }
            var oldDesign = Path.Combine(baseline, "design"); Directory.CreateDirectory(oldDesign);
            var before = new List<ChangeRecord>();
            session.Stage = "過去版の出力"; session.Save();
            app.Output.WriteLine("AgentReview", "[3/4] 過去版の選択成果物を出力しています。");
            if (oldTarget != null) {
                session.BaselineTargetId = oldTarget.Id;
                before = ExportChangeTarget(app, config, oldTarget, oldDesign).Comparison;
                var attachment = Path.Combine(Path.GetDirectoryName(copiedProject), "Attachment");
                if (Directory.Exists(attachment)) ChangeDialog.Work("過去版の添付資料を固定しています", token => ReviewSnapshot.CopyTree(attachment, Path.Combine(oldDesign, "Attachment"), new StringBuilder(), "baseline/design/Attachment", token));
                ChangeDiff.Attachments(Path.Combine(oldDesign, "Attachment"), before);
            } else File.WriteAllText(Path.Combine(oldDesign, "design.md"), "# 過去版\nユーザーが、過去版にはない新規成果物として指定しました。\n", new UTF8Encoding(false));
            ChangeDiff.SaveIndex(oldDesign, before);
            session.Save();
            File.AppendAllText(Path.Combine(session.Folder, "inputs.md"), "\n## 変化点比較\n\n- 比較元コミット: " + commit
                + "\n- リポジトリ: " + ReviewSnapshot.Cell(git.Root) + "\n- 取得したプロジェクト: " + ReviewSnapshot.Cell(copiedProject.Substring(bundle.Length + 1))
                + "\n- 現在版対象ID: " + ReviewSnapshot.Cell(target.Id)
                + "\n- 過去版対象ID: " + ReviewSnapshot.Cell(session.BaselineTargetId) + "\n- 対応方法: " + session.TargetMapping
                + "\n- 過去版対象: " + (oldTarget == null ? "新規成果物" : ReviewSnapshot.Cell(oldTarget.ModelPath))
                + "\n- 手動対応時、配下の異なるIDは追加／除外として扱います。全体を読み合わせて変更意図を確認してください。\n"
                + "- 同一リポジトリ外の依存関係・解釈できない添付資料の整合は未確認です。\n", new UTF8Encoding(false));
            if (!ProbeMatchesProject(app.Workspace.CurrentProject, originalPath, originalId)) throw new IOException("出力中にカレントが変化しました。");
            session.Stage = "差分生成"; session.Save();
            app.Output.WriteLine("AgentReview", "[4/4] 差分を作成しています。");
            ChangeDialog.Work("差分を作成しています", token => { token.ThrowIfCancellationRequested(); changes = ChangeDiff.Build(session.Folder, before, after.Comparison, token); token.ThrowIfCancellationRequested(); });
            prepared = true;
        }
        catch (Exception ex) { ChangeFailure(app, session, ex); }
        finally {
            if (owns) {
                try {
                    if (prepared) { session.Stage = "過去版の解放"; session.Save(); }
                    if (app.Workspace.CurrentProject != null && string.Equals(ProbeProjectPath(app.Workspace.CurrentProject), copiedProject, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("過去版がカレントになったため自動では閉じません。");
                    app.Workspace.CloseProject(historical);
                    if (!ProbeMatchesProject(app.Workspace.CurrentProject, originalPath, originalId)) throw new IOException("過去版解放後のカレントが一致しません。");
                } catch (Exception ex) { prepared = false; ChangeFailure(app, session, ex); }
            }
        }
        if (!prepared) return;
        try {
            ReviewSnapshot.AppendInstructions(session.Folder, "change");
            var instructions = "\n## 変化点レビュー\n\n`session.ini` の mode=change です。最初に `diff/changes.md` と `inputs.md` を読み、"
                + "`baseline/design/` と `design/` の前後を比較してください。`baseline/` と `diff/` は固定入力で変更禁止です。"
                + "両版および上位文書の unverified-diagrams.md を確認し、取得できない図を空図・変更なし・問題なしと扱わず、未確認として結果へ記載してください。変更項目と変更されていない関連設計を確認し、現在版の上位文書との整合・波及影響をレビューしてください。"
                + "工程は確定済みで再質問しません。既存問題と変更起因の問題を区別し、前後の根拠を示してください。"
                + "`review/changes.md` に変更概要・影響範囲・未確認範囲を、通常の指摘・対応表・提案と併せて出力してください。\n";
            foreach (var file in new[] { "AGENTS.md", "CLAUDE.md" }) File.AppendAllText(Path.Combine(session.Folder, file), instructions, new UTF8Encoding(false));
            session.State = "ready"; session.Stage = "レビュー入力完成"; session.Save();
            var config = AgentConfig.Load();
            if (changes == 0) {
                File.WriteAllText(Path.Combine(session.ReviewDir(), "changes.md"), "# 差分なし\n\n比較元: " + session.BaselineCommit
                    + "\n比較先: 現在開いている選択成果物（未保存編集を含む固定出力）\n\n取得できた範囲に差分はありません。AIは起動していません。上位整合・入力範囲外の依存関係は未確認です。両版および上位文書の unverified-diagrams.md にある図の内容の変更有無は判断できません。\n", new UTF8Encoding(false));
                app.Window.UI.ShowInformationDialog("取得できた範囲に差分はありません。AIは起動していません。図の未確認一覧も確認してください。\n保存先: " + session.Folder + "\n「結果を開く」で確認できます。", "AgentReview");
            } else {
                TerminalLauncher.Launch(session.Folder, config.ActiveProfile().BuildLaunchCommand("変化点レビューを開始してください。session.ini の工程は確定済みです。diff/changes.md と inputs.md から確認してください。"), config.Terminal);
                app.Window.UI.ShowInformationDialog("変化点レビューを開始しました。変更項目: " + changes + "\n保存先: " + session.Folder
                    + "\nターミナルで進行し、「結果を開く」でレビュー結果を確認できます。", "AgentReview");
            }
        } catch (Exception ex) { ChangeFailure(app, session, ex); }
    }
    private MarkdownExporter ExportChangeTarget(IApplication app, AgentConfig config, IModel target, string directory)
    {
        if (new[] { target }.Concat(target.GetAllChildren()).Any(m => m.IsDeleted || m.IsProxy)) throw new IOException("対象に削除済み・未ロードモデルが含まれます。");
        Directory.CreateDirectory(directory);
        var exporter = new MarkdownExporter(new MarkdownExportOptions(), directory, DiagramGroupRules.Load(config.DiagramGroupsRulesFile));
        WriteDesignArtifacts(app, "AgentReview", exporter, target, directory);
        if (exporter.Warnings.Count > 0) throw new IOException("出力警告があるため、不完全な差分レビューを停止しました。\n" + string.Join("\n", exporter.Warnings));
        return exporter;
    }
    private void ChangeFailure(IApplication app, SessionInfo session, Exception ex)
    {
        var cancelled = ex is OperationCanceledException;
        string path = "";
        if (session != null) {
            session.State = cancelled ? "cancelled" : "failed";
            try { session.Save(); path = Path.Combine(session.Folder, "failure.txt"); File.WriteAllText(path, ex.ToString(), new UTF8Encoding(false)); }
            catch (Exception writeError) { app.Output.WriteLine("AgentReview", "[error] 診断記録失敗: " + writeError.Message); }
        }
        if (session == null && !cancelled) {
            try {
                var directory = Path.Combine(AgentConfig.ConfigDir(), "diagnostics"); Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "change-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(path, ex.ToString(), new UTF8Encoding(false));
            } catch (Exception writeError) { path = ""; app.Output.WriteLine("AgentReview", "[error] 診断記録失敗: " + writeError.Message); }
        }
        app.Output.WriteLine("AgentReview", "[error] " + ex);
        app.Window.UI.ShowInformationDialog((cancelled ? "変化点レビューをキャンセルしました。" : "変化点レビューを開始できませんでした。\n" + (session == null ? "入力選択" : session.Stage) + "\n" + ex.Message)
            + (path.Length == 0 ? "" : "\n診断ログ: " + path), "AgentReview");
    }

    // OpenProject(path, false, false): カレントにせず、モデルを含めて読み込む。
    // https://docs.nextdesign.app/extension/v3.x/api/NextDesign.Desktop/IWorkspace/methods/OpenProject-1
    public void ProbeHistoricalExport(ICommandContext context, ICommandParams commandParams)
    {
        var app = context.App;
        IProject historical = null;
        var current = app.Workspace.CurrentProject;
        string outDir = null;
        string copiedProject = null;
        string currentPath = null;
        string currentId = null;
        bool ownsHistorical = false;
        bool exportCompleted = false;
        try
        {
            if (current == null) throw new InvalidOperationException("プロジェクトを開いてください。");
            var selectedTarget = ResolveRoot(app);
            if (selectedTarget == null || selectedTarget.Id == current.Id || selectedTarget.IsDeleted || selectedTarget.IsProxy)
                throw new InvalidOperationException("出力する工程成果物のモデルを選択してから実行してください。プロジェクト全体は出力しません。");
            var targetId = selectedTarget.Id;
            var targetPath = selectedTarget.ModelPath;
            currentPath = ProbeProjectPath(current);
            currentId = current.Id;
            var folder = app.Window.UI.ShowSelectFolderDialog("過去版一式のフォルダを選択（参照ファイルも含む）");
            if (string.IsNullOrWhiteSpace(folder)) return;
            var selected = app.Window.UI.ShowOpenFileDialog("過去版フォルダ内のプロジェクトファイルを選択: " + folder,
                "Next Design プロジェクト (*.nproj;*.iproj)|*.nproj;*.iproj|すべてのファイル (*.*)|*.*");
            if (string.IsNullOrWhiteSpace(selected)) return;
            var source = ReviewSnapshot.ResolvePath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var sourceProject = ReviewSnapshot.ResolvePath(selected);
            if (!sourceProject.StartsWith(source, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("選択した過去版フォルダ内のプロジェクトファイルを選んでください。");
            var relativeProject = sourceProject.Substring(source.Length);
            ReviewSnapshot.CheckFile(sourceProject);
            if (string.Equals(sourceProject, currentPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("現在開いているプロジェクトではなく、別途取り出した過去版を指定してください。");
            var config = AgentConfig.Load();
            var baseDir = config.WorkspaceRoot;
            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                baseDir = app.Window.UI.ShowSelectFolderDialog("検証結果の保存先");
            if (string.IsNullOrWhiteSpace(baseDir)) return;
            if (!app.Window.UI.ShowConfirmDialog("過去版一式を専用領域にコピーし、カレントにせず読み込んで出力します。\n"
                + sourceProject + "\n検証用のコピー以外は保存・切り替えしません。続行しますか？", "AgentReview")) return;
            outDir = Path.Combine(baseDir, "historical-probe-" + Guid.NewGuid().ToString("N"));
            var copyDir = Path.Combine(outDir, "project");
            var report = new StringBuilder("# 過去版出力の実機検証\n\n自動判定は参考。図の内容と現在の編集状態は実機で確認してください。\n\n");
            report.Append("## 確認するファイル\n\n- [設計本文](design/design.md)\n- [図の一覧](design/_index.md)\n- 図のPlantUML: `design/diagrams/`\n- 読み込み用の過去版コピー: `project/`\n\n図の一覧から各図を開き、過去版の内容・件数と照合してください。現在版の未保存編集・選択・表示も確認してください。\n\n## コピー記録\n\n");
            report.Append("| コピー元 | コピー先 | SHA-256 |\n|---|---|---|\n");
            ReviewSnapshot.CopyTree(source, copyDir, report, "project");
            context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;
            copiedProject = ReviewSnapshot.ResolvePath(Path.Combine(copyDir, relativeProject));
            historical = app.Workspace.OpenProject(copiedProject, false, false);
            ownsHistorical = historical != null && string.Equals(ProbeProjectPath(historical), copiedProject, StringComparison.OrdinalIgnoreCase);
            if (!ownsHistorical)
                throw new InvalidOperationException("過去版を独立したプロジェクトとして取得できませんでした。");
            if (!ProbeMatchesProject(app.Workspace.CurrentProject, currentPath, currentId))
                throw new InvalidOperationException("カレントプロジェクトが変化しました。検証を中断します。");
            var targetMatches = historical.GetAllChildren().Where(m => m.Id == targetId).ToList();
            if (targetMatches.Count != 1 || targetMatches[0].IsDeleted || targetMatches[0].IsProxy)
                throw new InvalidOperationException("選択した工程成果物に対応するモデルを過去版で特定できませんでした。\n"
                    + targetPath + "\n同じモデルIDを持つ過去版が必要です。プロジェクト全体への切り替えは行いません。");
            var historicalTarget = targetMatches[0];
            report.Append("\n## 出力対象\n\n- 現在版の選択: ").Append(ReviewSnapshot.Cell(targetPath))
                .Append("\n- 過去版の対象: ").Append(ReviewSnapshot.Cell(historicalTarget.ModelPath))
                .Append("\n- 対応モデルID: ").Append(ReviewSnapshot.Cell(targetId))
                .Append("\n- 出力範囲: 上記モデルとその配下のみ\n");
            var designDir = Path.Combine(outDir, "design");
            Directory.CreateDirectory(designDir);
            var exporter = new MarkdownExporter(new MarkdownExportOptions(), designDir,
                DiagramGroupRules.Load(config.DiagramGroupsRulesFile));
            WriteDesignArtifacts(app, "AgentReview", exporter, historicalTarget, designDir);
            if (!ProbeMatchesProject(app.Workspace.CurrentProject, currentPath, currentId))
                throw new InvalidOperationException("出力後にカレントプロジェクトが変化しました。検証を中断します。");
            report.Append("\n## 出力結果\n\n- モデル: ").Append(exporter.ModelCount).Append("\n- 図: ")
                .Append(exporter.DiagramCount).Append("\n- カレント維持: 確認\n");
            AppendExportWarnings(report, "過去版", exporter);
            File.WriteAllText(Path.Combine(outDir, "probe.md"), report.ToString(), new UTF8Encoding(false));
            app.Output.WriteLine("AgentReview", "[info] 過去版出力: " + outDir + "（図の内容・件数・編集状態は未判定）");
            exportCompleted = true;
        }
        catch (Exception ex)
        {
            app.Output.WriteLine("AgentReview", "[error] " + ex);
            if (outDir != null && Directory.Exists(outDir))
            {
                try { File.WriteAllText(Path.Combine(outDir, "failure.txt"), ex.ToString(), new UTF8Encoding(false)); }
                catch (Exception writeError) { app.Output.WriteLine("AgentReview", "[error] " + writeError.Message); }
            }
            app.Window.UI.ShowInformationDialog("過去版出力の検証に失敗しました。\n" + ex.Message, "AgentReview");
        }
        finally
        {
            if (ownsHistorical)
            {
                try
                {
                    if (app.Workspace.CurrentProject != null && string.Equals(ProbeProjectPath(app.Workspace.CurrentProject), copiedProject, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("検証用プロジェクトがカレントになったため、自動で閉じません。編集状態を確認してください。");
                    app.Workspace.CloseProject(historical);
                    if (!ProbeMatchesProject(app.Workspace.CurrentProject, currentPath, currentId))
                        throw new InvalidOperationException("過去版解放後のカレントプロジェクトが一致しません。");
                    if (outDir != null && File.Exists(Path.Combine(outDir, "probe.md")))
                        File.AppendAllText(Path.Combine(outDir, "probe.md"), "- 過去版の解放: API呼び出し正常終了\n", new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    app.Output.WriteLine("AgentReview", "[error] 過去版の解放失敗: " + ex);
                    exportCompleted = false;
                    if (outDir != null && Directory.Exists(outDir))
                    {
                        try { File.WriteAllText(Path.Combine(outDir, "failure.txt"), "過去版の解放失敗\n" + ex, new UTF8Encoding(false)); }
                        catch (Exception writeError) { app.Output.WriteLine("AgentReview", "[error] " + writeError.Message); }
                    }
                    app.Window.UI.ShowInformationDialog("過去版の解放に失敗しました。出力ログを確認してください。", "AgentReview");
                }
            }
        }
        if (exportCompleted)
        {
            if (!app.Window.UI.ShowConfirmDialog("過去版の出力が完了しました。図の内容は確認が必要です。\n\n保存先:\n" + outDir
                + "\n\n検証レポート: probe.md\n設計本文: design/design.md\n図の一覧: design/_index.md\n図のPlantUML: design/diagrams/"
                + "\n\nVS Codeでフォルダと検証レポートを開きますか？", "AgentReview")) return;
            try { ReviewResultViewer.Open(outDir, AgentConfig.Load().VsCodeExecutable); }
            catch (Exception ex) {
                app.Window.UI.ShowInformationDialog("出力は完了していますが、VS Codeを開けませんでした。\n" + ex.Message
                    + "\n\n保存先:\n" + outDir + "\n検証レポート: probe.md", "AgentReview");
            }
        }
    }

    private static string ProbeProjectPath(IProject project)
    {
        return string.IsNullOrWhiteSpace(project.Path) ? "" : ReviewSnapshot.ResolvePath(project.Path);
    }

    private static bool ProbeMatchesProject(IProject project, string path, string id)
    {
        return project != null && project.Id == id
            && string.Equals(ProbeProjectPath(project), path, StringComparison.OrdinalIgnoreCase);
    }

    // design.md / diagrams\<種別>\<階層>\*.puml / _index.md を outDir へ書き出し、警告と統計を Output に出す
    // （StartAgentReview とレビューなし単体出力 ExportDesignInfo の共通部）
    private void WriteDesignArtifacts(IApplication app, string category, MarkdownExporter exporter, IModel root, string outDir)
    {
        DesignArtifactWriter.Write(app, category, exporter, root, outDir);
    }

    // レビューセッションを作らず、設計情報（design.md + diagrams\<種別>\<階層>\*.puml + _index.md）だけを任意のフォルダへ出力する
    public void ExportDesignInfo(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        try
        {
            // 未表示エディタ配下でも最新値を取得できるようにする（バッチでは必須）
            context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

            var root = ResolveRoot(app);
            if (root == null)
            {
                app.Window.UI.ShowInformationDialog("プロジェクトが開かれていません。", category);
                return;
            }

            var outDir = app.Window.UI.ShowSelectFolderDialog("設計情報の出力先フォルダを選択してください");
            if (string.IsNullOrEmpty(outDir)) return;

            if (!app.Window.UI.ShowConfirmDialog(
                "「" + root.Name + "」配下の設計情報と図を出力します。\n\n"
                + "出力先: " + outDir + "\n\n続行しますか？", category)) return;

            OutputPane.Show(app, category);
            app.Output.WriteLine(category, "=== 設計情報の出力 : " + root.Name + " ===");

            var config = AgentConfig.Load();
            var exporter = new MarkdownExporter(new MarkdownExportOptions(), outDir,
                DiagramGroupRules.Load(config.DiagramGroupsRulesFile));
            WriteDesignArtifacts(app, category, exporter, root, outDir);

            app.Output.WriteLine(category, "=== 出力完了 : " + outDir + " ===");
            app.Window.UI.ShowInformationDialog(
                "設計情報を出力しました。\n\n"
                + "出力先: " + outDir + "\n"
                + "モデル " + exporter.ModelCount + " 件 / 図 " + exporter.DiagramCount + " 件"
                + (exporter.SkippedModelCount > 0
                    ? "（図の構成要素 " + exporter.SkippedModelCount + " モデルはテキスト出力から除外）" : "")
                + (exporter.Warnings.Count > 0
                    ? "\n警告 " + exporter.Warnings.Count + " 件（出力ウィンドウを確認してください）" : ""), category);
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("設計情報の出力に失敗しました。\n\n" + ex.Message, category);
        }
    }

    public void ResumeAgentSession(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        try
        {
            var config = AgentConfig.Load();
            var session = SessionLocator.FindLatest(config.WorkspaceRoot);
            if (session == null)
            {
                app.Window.UI.ShowInformationDialog(
                    "再開できるセッションが見つかりません。\n先に「レビュー開始」を実行してください。", category);
                return;
            }

            // セッション作成時のエージェントで再開する（会話履歴は CLI 側がフォルダ単位で持つ）
            var savedAgent = config.Agent;
            config.Agent = session.Agent == "codex" ? "codex" : "claude";
            var profile = config.ActiveProfile();
            config.Agent = savedAgent;

            OutputPane.Show(app, category);
            app.Output.WriteLine(category, "=== セッション再開 : " + session.Folder + " (" + profile.DisplayName + ") ===");
            TerminalLauncher.Launch(session.Folder, profile.BuildResumeCommand(), config.Terminal);
            app.Output.WriteLine(category, "ターミナルを開きました。前回の対話の続きから再開します。");
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("セッション再開に失敗しました。\n\n" + ex.Message, category);
        }
    }

    public void OpenReviewResult(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        string sessionFolder = null;
        try
        {
            var config = AgentConfig.Load();
            var session = SessionLocator.FindLatest(config.WorkspaceRoot);
            if (session == null)
            {
                app.Window.UI.ShowInformationDialog(
                    "セッションが見つかりません。\n先に「レビュー開始」を実行してください。", category);
                return;
            }

            sessionFolder = session.Folder;
            if (ReviewResultViewer.ResultFiles(sessionFolder).Count == 0)
            {
                app.Window.UI.ShowInformationDialog(
                    "レビュー結果がまだ生成されていません。\n\n"
                    + "エージェントがターミナルで review\\review.md を書き出すと開けるようになります。\n"
                    + "セッション: " + session.Folder, category);
                return;
            }
            ReviewResultViewer.Open(sessionFolder, config.VsCodeExecutable);
            app.Output.WriteLine(category, "VS Code に結果表示を要求しました: " + sessionFolder);
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("VS Code で結果を開けませんでした。\n\n" + ex.Message
                + (sessionFolder == null ? "" : "\n\nセッション: " + sessionFolder), category);
        }
    }

    public void OpenWorkspaceFolder(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        try
        {
            var config = AgentConfig.Load();
            var session = SessionLocator.FindLatest(config.WorkspaceRoot);
            var target = session != null ? session.Folder : config.WorkspaceRoot;
            if (!TerminalLauncher.OpenFolder(target))
            {
                app.Window.UI.ShowInformationDialog(
                    "開くフォルダがありません。\n先に「レビュー開始」を実行してください。", category);
                return;
            }
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("フォルダを開けませんでした。\n\n" + ex.Message, category);
        }
    }

    public void SwitchAgent(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        try
        {
            var config = AgentConfig.Load();
            config.Agent = config.Agent == "claude" ? "codex" : "claude";
            config.Save();
            var profile = config.ActiveProfile();
            app.Window.UI.ShowInformationDialog(
                "使用するエージェントを切り替えました。\n\n"
                + "現在: " + profile.DisplayName + "（コマンド: " + profile.Command + "）\n\n"
                + "次回の「レビュー開始」から有効です。", category);
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("切替に失敗しました。\n\n" + ex.Message, category);
        }
    }

    public void OpenSkillFolder(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        try
        {
            var skillsDir = SkillProvisioner.SourceDir(context.ExtensionInfo.ExtensionPath);
            SkillProvisioner.ValidateSource(skillsDir);
            var reviewSkillDir = Path.Combine(skillsDir, "design-review");
            if (!TerminalLauncher.OpenFolder(reviewSkillDir))
            {
                app.Window.UI.ShowInformationDialog(
                    "スキルフォルダを開けませんでした。\n\n" + reviewSkillDir, category);
                return;
            }
            app.Output.WriteLine(category, "design-review 共通スキル: " + reviewSkillDir);
            app.Output.WriteLine(category, "チーム共通の原本です。変更は拡張機能一式として配布してください。既存セッションも更新後の内容を参照します。");
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("スキルフォルダを開けませんでした。\n\n" + ex.Message, category);
        }
    }

    public void OpenConfig(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        try
        {
            AgentSettingsDialog.Show(AgentConfig.Load());
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("設定を開けませんでした。\n\n" + ex.Message, category);
        }
    }

    public void CheckCliEnvironment(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        try
        {
            var config = AgentConfig.Load();
            OutputPane.Show(app, category);
            app.Output.WriteLine(category, "=== 環境診断 ===");
            app.Output.WriteLine(category, "現在のエージェント : " + config.ActiveProfile().DisplayName);
            app.Output.WriteLine(category, "基点フォルダ       : " + (string.IsNullOrEmpty(config.WorkspaceRoot) ? "(未設定)" : config.WorkspaceRoot));
            app.Output.WriteLine(category, "設定ファイル       : " + AgentConfig.ConfigPath()
                + (File.Exists(AgentConfig.ConfigPath()) ? "" : " (未作成。既定値で動作)"));
            var skillsDir = SkillProvisioner.SourceDir(context.ExtensionInfo.ExtensionPath);
            app.Output.WriteLine(category, "共通スキル         : " + Path.Combine(skillsDir, "design-review"));
            try
            {
                SkillProvisioner.ValidateSource(skillsDir);
                app.Output.WriteLine(category, "同梱スキル         : OK（セッションからジャンクションで参照）");
            }
            catch (Exception ex)
            {
                app.Output.WriteLine(category, "[error] " + ex.Message);
            }
            app.Output.WriteLine(category, "");

            app.Output.WriteLine(category, "[claude] where   : " + CliProbe.Run("where " + config.ClaudeCommand, 5000));
            app.Output.WriteLine(category, "[claude] version : " + CliProbe.Run(config.ClaudeCommand + " --version", 15000));
            app.Output.WriteLine(category, "[codex]  where   : " + CliProbe.Run("where " + config.CodexCommand, 5000));
            app.Output.WriteLine(category, "[codex]  version : " + CliProbe.Run(config.CodexCommand + " --version", 15000));
            app.Output.WriteLine(category, "");
            app.Output.WriteLine(category, "CLI が見つからない場合: インストール後に Next Design を再起動すると PATH が反映されます。");
            app.Output.WriteLine(category, "=== 診断完了 ===");
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("環境診断に失敗しました。\n\n" + ex.Message, category);
        }
    }

    // 選択モデル配下を再帰的にダンプする（モデルごとの全フィールドの型・値・RichText・
    // GetFieldValues の実行時型まで）。design.md に出ない情報がある場合の切り分け用。
    // 長大になるため出力ウィンドウには要約のみ出し、全文はファイルに保存する
    public void ProbeExportTarget(ICommandContext context, ICommandParams commandParams)
    {
        var category = "AgentReview";
        var app = context.App;
        try
        {
            context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

            var root = ResolveRoot(app);
            if (root == null)
            {
                app.Window.UI.ShowInformationDialog("プロジェクトが開かれていません。", category);
                return;
            }

            OutputPane.Show(app, category);
            app.Output.WriteLine(category, "=== エクスポート診断 : " + (root.Name ?? "(無名)") + " ===");

            var probe = new ExportProbe();
            probe.Dump(root);

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nd-agent-review");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "probe_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
            File.WriteAllText(file, probe.Text(), new UTF8Encoding(false));

            app.Output.WriteLine(category, "モデル " + probe.ModelCount + " 件をダンプしました"
                + (probe.Truncated ? "（上限 " + ExportProbe.MaxModels + " 件で打ち切り。より深い階層は対象モデルを選び直して実行）" : ""));
            app.Output.WriteLine(category, "保存先: " + file);
            app.Output.WriteLine(category, "=== 診断完了 ===");

            TerminalLauncher.OpenWithNotepad(file);
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "[error] " + ex.ToString());
            app.Window.UI.ShowInformationDialog("エクスポート診断に失敗しました。\n\n" + ex.Message, category);
        }
    }

    // ==================== 対象の決定 ====================

    // ナビゲータの選択 → CurrentModel → プロジェクト の順に起点を決める
    // （PlantUmlTool の ExportRunner.ResolveRoot と同じ規則）
    private IModel ResolveRoot(IApplication app)
    {
        var page = app.Window.EditorPage;
        if (page != null && page.CurrentNavigator != null)
        {
            var selected = page.CurrentNavigator.SelectedItems
                .OfType<IModel>()
                .OrderBy(m => m.ModelPath, StringComparer.Ordinal)
                .ThenBy(m => m.Id, StringComparer.Ordinal)
                .ToList();
            if (selected.Count > 0) return selected[0];
        }
        if (app.Workspace.CurrentModel != null) return app.Workspace.CurrentModel;
        return app.Workspace.CurrentProject;
    }
}
