// ============================================================
//  Part 5 / プロセス起動
// ============================================================

public static class TerminalLauncher
{
    // ターミナルを開いてコマンドを対話実行する。完了は待たない
    // （UI スレッドで WaitForExit すると Next Design が固まる）。
    //  claude / codex は npm の .cmd シムなので必ず cmd.exe 経由で起動する
    public static void Launch(string workDir, string commandLine, string terminal)
    {
        var wt = FindWindowsTerminal();
        var useWt = wt != null
            && (terminal == "wt" || terminal == "auto")
            && !workDir.Contains(";")            // wt は ; を引数セパレータ扱いする
            && !commandLine.Contains(";");

        ProcessStartInfo psi;
        if (useWt)
        {
            psi = new ProcessStartInfo
            {
                FileName = wt,
                Arguments = "-d \"" + workDir + "\" cmd /k " + commandLine,
                UseShellExecute = true
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k " + commandLine,
                WorkingDirectory = workDir,
                UseShellExecute = true
            };
        }
        Process.Start(psi);
    }

    // ファイルを既定アプリで開く（完了は待たない）。フォルダには使わない
    public static void OpenWithShell(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    // フォルダをエクスプローラーで開く。フォルダパスを FileName に直接渡す方式は
    // 環境によって既定のエクスプローラー画面だけが開くため、explorer.exe に明示的に渡す
    public static bool OpenFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return false;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true
        });
        return true;
    }

    public static void OpenWithNotepad(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true
        });
    }

    private static string FindWindowsTerminal()
    {
        var candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "wt.exe");
        return File.Exists(candidate) ? candidate : null;
    }
}

// 「結果を開く」専用。VS Code 全体の設定や他のボタンの起動先は変更しない。
public static class ReviewResultViewer
{
    public const string WorkspaceFileName = "agentreview-results.code-workspace";

    public static List<string> ResultFiles(string sessionFolder)
    {
        // review.md を最後に渡し、指摘を先に確認しやすくする。
        var files = new[] { "coverage.md", "changes.md", "proposal.md", "review.md" }
            .Select(name => Path.Combine(sessionFolder, "review", name)).Where(File.Exists).ToList();
        if (files.Count == 0 && File.Exists(Path.Combine(sessionFolder, "probe.md")))
            files.Add(Path.Combine(sessionFolder, "probe.md"));
        return files;
    }

    public static IEnumerable<string> ExecutableCandidates(string localAppData, string programFiles,
        string programFilesX86, string pathVariable)
    {
        if (!string.IsNullOrEmpty(localAppData))
            yield return Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
        foreach (var root in new[] { programFiles, programFilesX86 })
            if (!string.IsNullOrEmpty(root)) yield return Path.Combine(root, "Microsoft VS Code", "Code.exe");
        foreach (var entry in (pathVariable ?? "").Split(';'))
        {
            string dir;
            try
            {
                dir = entry.Trim().Trim('"');
                if (dir.Length == 0 || !Path.IsPathRooted(dir)) continue;
                dir = Path.GetFullPath(dir);
            }
            catch (Exception) { continue; }
            yield return Path.Combine(dir, "Code.exe");
            // Windows の code コマンドは bin/code.cmd。cmd.exe を介さず本体を使う。
            if (File.Exists(Path.Combine(dir, "code.cmd")))
                yield return Path.GetFullPath(Path.Combine(dir, "..", "Code.exe"));
        }
    }

    public static string FindExecutable(string configured, IEnumerable<string> candidates)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = configured.Trim();
            if (!Path.IsPathRooted(path) || Path.GetPathRoot(path).Length < 3
                || !string.Equals(Path.GetFileName(path), "Code.exe", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path))
                throw new FileNotFoundException("「設定」のVS Code欄で、存在する Code.exe を参照ボタンから選んでください。");
            return Path.GetFullPath(path);
        }
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        throw new FileNotFoundException("VS Code が見つかりません。VS Code をインストールするか、「設定」画面の「VS Code」で参照ボタンから Code.exe を選んでください。");
    }

    // Windows の argv 規則で引用する。シェルの変数展開やメタ文字解釈を通さない。
    public static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var ch in value)
        {
            if (ch == '\\') { slashes++; continue; }
            if (ch == '"') result.Append('\\', slashes * 2 + 1);
            else result.Append('\\', slashes);
            result.Append(ch);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    public static ProcessStartInfo PrepareLaunch(string sessionFolder, string executable)
    {
        var folder = Path.GetFullPath(sessionFolder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("レビューセッションのフォルダがありません。");
        var files = ResultFiles(folder);
        if (files.Count == 0) throw new FileNotFoundException("レビュー結果がまだ生成されていません。");
        var workspace = Path.Combine(folder, WorkspaceFileName);
        // 専用生成物。相対パスなのでセッションを別の PC へ移しても参照が保たれる。
        var json = "{\n"
            + "  \"folders\": [{ \"path\": \".\" }],\n"
            + "  \"settings\": {\n"
            + "    \"workbench.editor.enablePreview\": false,\n"
            + "    \"workbench.editorAssociations\": {\n"
            + "      \"**/probe.md\": \"vscode.markdown.preview.editor\",\n"
            + "      \"**/review/review.md\": \"vscode.markdown.preview.editor\",\n"
            + "      \"**/review/proposal.md\": \"vscode.markdown.preview.editor\",\n"
            + "      \"**/review/coverage.md\": \"vscode.markdown.preview.editor\",\n"
            + "      \"**/review/changes.md\": \"vscode.markdown.preview.editor\"\n"
            + "    }\n"
            + "  }\n"
            + "}\n";
        File.WriteAllText(workspace, json, new UTF8Encoding(false));
        var info = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--new-window " + QuoteArgument(workspace) + " "
                + string.Join(" ", files.Select(QuoteArgument).ToArray()),
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Electron ベースの親プロセスから起動された場合でも VS Code の GUI として起動する。
        info.EnvironmentVariables.Remove("ELECTRON_RUN_AS_NODE");
        return info;
    }

    public static void Open(string sessionFolder, string configuredExecutable)
    {
        var executable = FindExecutable(configuredExecutable, ExecutableCandidates(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("PATH")));
        var info = PrepareLaunch(sessionFolder, executable);
        using (var process = Process.Start(info))
        {
            if (process == null) throw new IOException("VS Code を起動できませんでした。");
            // VS Code の終了やウィンドウ表示は待たない。
        }
    }
}

// CLI の存在とバージョンの診断。ここだけは短いタイムアウト付きで完了を待つ
public static class CliProbe
{
    public static string Run(string commandLine, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + commandLine,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(psi))
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch (Exception) { }
                    return "(タイムアウト)";
                }
                var text = (stdout + stderr).Trim();
                return text.Length > 0 ? text : "(出力なし / 終了コード " + process.ExitCode + ")";
            }
        }
        catch (Exception ex)
        {
            return "(実行失敗: " + ex.Message + ")";
        }
    }
}
