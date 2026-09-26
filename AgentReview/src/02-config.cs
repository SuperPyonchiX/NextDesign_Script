// ============================================================
//  Part 1 / 設定
//
//    JSON パーサ（Newtonsoft 等）が V3.x スクリプトで使える保証が
//    ないため、設定は key=value 形式の .ini で持つ。
//    ハンドラ呼び出しのたびに読み直すので、編集の反映に
//    Next Design の再起動は不要。
// ============================================================

public class AgentConfig
{
    public string Agent = "codex";          // "claude" | "codex"
    public string WorkspaceRoot = "";        // レビューセッションの基点フォルダ
    public string Terminal = "auto";         // "auto" | "wt" | "cmd"
    public string ClaudeCommand = "claude";
    public string ClaudeArgs = "";           // 対話起動時の追加引数（--permission-mode など）
    public string CodexCommand = "codex";
    public string CodexArgs = "";
    public string InitialPrompt = "レビューを開始してください";   // 起動時に自動投入。空なら手入力
    public string Perspectives = "";   // 追加観点のみ。工程別観点は design-review スキル側で定義
    public string DiagramGroupsRulesFile = "";
    public string VsCodeExecutable = "";     // 空なら通常のインストール先と PATH から自動検出

    public static string ConfigDir()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nd-agent-review");
    }

    public static string ConfigPath()
    {
        return Path.Combine(ConfigDir(), "config.ini");
    }

    public static AgentConfig Load()
    {
        var config = new AgentConfig();
        var path = ConfigPath();
        if (!File.Exists(path)) return config;

        foreach (var pair in IniFile.Read(path))
        {
            switch (pair.Key)
            {
                case "agent": config.Agent = pair.Value; break;
                case "workspaceRoot": config.WorkspaceRoot = pair.Value; break;
                case "terminal": config.Terminal = pair.Value; break;
                case "claude.command": config.ClaudeCommand = pair.Value; break;
                case "claude.args": config.ClaudeArgs = pair.Value; break;
                case "codex.command": config.CodexCommand = pair.Value; break;
                case "codex.args": config.CodexArgs = pair.Value; break;
                case "initialPrompt": config.InitialPrompt = pair.Value; break;
                case "perspectives": config.Perspectives = pair.Value; break;
                case "diagramGroups.rulesFile": config.DiagramGroupsRulesFile = pair.Value; break;
                case "vscode.executable": config.VsCodeExecutable = pair.Value; break;
            }
        }
        if (config.Agent != "claude" && config.Agent != "codex") config.Agent = "codex";
        return config;
    }

    public void Save()
    {
        var nl = "\r\n";   // メモ帳で編集するファイルなので CRLF
        var sb = new StringBuilder();
        sb.Append("# AgentReview 設定ファイル").Append(nl);
        sb.Append("# 保存すると次のボタン操作から反映されます（Next Design の再起動は不要）").Append(nl);
        sb.Append(nl);
        sb.Append("# 使用するエージェント: codex（既定） | claude").Append(nl);
        sb.Append("agent=").Append(Agent).Append(nl);
        sb.Append(nl);
        sb.Append("# レビューセッションを作成する基点フォルダ").Append(nl);
        sb.Append("workspaceRoot=").Append(WorkspaceRoot).Append(nl);
        sb.Append(nl);
        sb.Append("# ターミナル: auto（Windows Terminal があれば使う） | wt | cmd").Append(nl);
        sb.Append("terminal=").Append(Terminal).Append(nl);
        sb.Append(nl);
        sb.Append("# CLI コマンド名と対話起動時の追加引数").Append(nl);
        sb.Append("# 例: claude.args=--permission-mode acceptEdits").Append(nl);
        sb.Append("#     codex.args=--sandbox workspace-write").Append(nl);
        sb.Append("claude.command=").Append(ClaudeCommand).Append(nl);
        sb.Append("claude.args=").Append(ClaudeArgs).Append(nl);
        sb.Append("codex.command=").Append(CodexCommand).Append(nl);
        sb.Append("codex.args=").Append(CodexArgs).Append(nl);
        sb.Append(nl);
        sb.Append("# レビュー開始時にエージェントへ自動投入する最初のプロンプト（空なら手入力）").Append(nl);
        sb.Append("initialPrompt=").Append(InitialPrompt).Append(nl);
        sb.Append(nl);
        sb.Append("# 追加のレビュー観点（カンマ区切り）。工程別の観点表は").Append(nl);
        sb.Append("# 拡張機能に同梱した skills/design-review/ でチーム共通管理する").Append(nl);
        sb.Append("perspectives=").Append(Perspectives).Append(nl);
        sb.Append(nl);
        sb.Append("# 任意の図グループ対応表（UTF-8 INI）の絶対パス。空なら所有フィールドから自動判別").Append(nl);
        sb.Append("diagramGroups.rulesFile=").Append(DiagramGroupsRulesFile).Append(nl);
        sb.Append(nl);
        sb.Append("# 結果表示用 Code.exe の絶対パス。空なら VS Code を自動検出（引用符不要）").Append(nl);
        sb.Append("vscode.executable=").Append(VsCodeExecutable).Append(nl);

        Directory.CreateDirectory(ConfigDir());
        File.WriteAllText(ConfigPath(), sb.ToString(), new UTF8Encoding(false));
    }

    public AgentProfile ActiveProfile()
    {
        if (Agent == "codex")
            return new AgentProfile
            {
                Key = "codex",
                DisplayName = "Codex",
                Command = string.IsNullOrEmpty(CodexCommand) ? "codex" : CodexCommand,
                ExtraArgs = CodexArgs,
                InstructionFileName = "AGENTS.md",
                ResumeArgs = "resume --last"
            };
        return new AgentProfile
        {
            Key = "claude",
            DisplayName = "Claude Code",
            Command = string.IsNullOrEmpty(ClaudeCommand) ? "claude" : ClaudeCommand,
            ExtraArgs = ClaudeArgs,
            InstructionFileName = "CLAUDE.md",
            ResumeArgs = "--continue"
        };
    }
}

// claude / codex の CLI 差異の吸収
public class AgentProfile
{
    public string Key;
    public string DisplayName;
    public string Command;
    public string ExtraArgs;
    public string InstructionFileName;
    public string ResumeArgs;

    // 対話モードの起動コマンドライン。初期プロンプトを位置引数で渡すと
    // 対話セッションの最初の入力として自動投入される（claude / codex 共通）。
    // 引用符の入れ子事故を避けるため、プロンプト内の " は ' に置換する
    public string BuildLaunchCommand(string initialPrompt)
    {
        var line = Command;
        if (!string.IsNullOrEmpty(ExtraArgs)) line += " " + ExtraArgs;
        if (!string.IsNullOrEmpty(initialPrompt))
            line += " \"" + initialPrompt.Replace("\"", "'") + "\"";
        return line;
    }

    public string BuildResumeCommand()
    {
        var line = Command + " " + ResumeArgs;
        if (!string.IsNullOrEmpty(ExtraArgs)) line += " " + ExtraArgs;
        return line;
    }
}

// key=value 形式の読み書き（# 始まりと空行は無視）
public static class IniFile
{
    public static List<KeyValuePair<string, string>> Read(string path)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result.Add(new KeyValuePair<string, string>(
                line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
        }
        return result;
    }
}
