// ============================================================
//  AgentReview / Claude Code・Codex による設計レビュー支援
//
//    Next Design V3.x のスクリプト拡張。役割分担:
//      - 本拡張 : 設計情報のエクスポート / エージェント向け指示書の生成 /
//                 ターミナルでのエージェント起動 / 結果ファイルの表示
//      - 対話   : ターミナル上の claude / codex 本来の UI に委ねる
//        （V3.x の拡張 UI では対話画面を作れず、コマンドは UI スレッド
//          同期実行のため、CLI の完了を待つと Next Design が固まる）
//    Next Design のモデルへの書き戻しは行わない（V3.x は読み取り専用
//    要素が多いため。修正は review/ 配下への提案ファイル出力まで）。
//
//    Part 構成:
//      Part 0  共通ヘルパ   AgentText / OutputPane
//      Part 1  設定         AgentConfig（%USERPROFILE%\.nd-agent-review\config.ini）
//                           AgentProfile（claude / codex の差異吸収）
//      Part 2  セッション   SessionInfo / SessionLocator
//      Part 3  ワークスペース WorkspaceBuilder（フォルダ・指示書・session.ini）
//      Part 4  Markdown出力 MarkdownExportOptions / MarkdownExporter / HtmlToMarkdown
//                           （DesignExporter(46ac9c9) から図の埋め込みを外して移植。
//                             修正は転記元 PlantUmlTool 系と独立に本ファイルで完結。
//                             ドキュメント本文は RichText 型フィールドに格納されるため
//                             GetRichTextField(html) → Markdown 変換で出力する）
//      Part 5  プロセス起動 TerminalLauncher / CliProbe
//      Part 6  コマンドハンドラ
// ============================================================

using NextDesign.Core;
using NextDesign.Desktop;
using NextDesign.Extension;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
    public string Agent = "claude";          // "claude" | "codex"
    public string WorkspaceRoot = "";        // レビューセッションの基点フォルダ
    public string Terminal = "auto";         // "auto" | "wt" | "cmd"
    public string ClaudeCommand = "claude";
    public string ClaudeArgs = "";           // 対話起動時の追加引数（--permission-mode など）
    public string CodexCommand = "codex";
    public string CodexArgs = "";
    public string Perspectives = "設計の整合性,網羅性（抜け漏れ）,インタフェース設計,命名の一貫性,保守性";

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
                case "perspectives": config.Perspectives = pair.Value; break;
            }
        }
        if (config.Agent != "claude" && config.Agent != "codex") config.Agent = "claude";
        return config;
    }

    public void Save()
    {
        var nl = "\r\n";   // メモ帳で編集するファイルなので CRLF
        var sb = new StringBuilder();
        sb.Append("# AgentReview 設定ファイル").Append(nl);
        sb.Append("# 保存すると次のボタン操作から反映されます（Next Design の再起動は不要）").Append(nl);
        sb.Append(nl);
        sb.Append("# 使用するエージェント: claude | codex").Append(nl);
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
        sb.Append("# レビュー観点（カンマ区切り。指示書に埋め込まれる）").Append(nl);
        sb.Append("perspectives=").Append(Perspectives).Append(nl);

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

    // 対話モードの起動コマンドライン。初期プロンプトは引数で渡さず、
    // 指示書（CLAUDE.md / AGENTS.md）の自動読み込みに任せる
    // （cmd / wt 経由の引用符の入れ子事故を避けるため）
    public string BuildLaunchCommand()
    {
        var line = Command;
        if (!string.IsNullOrEmpty(ExtraArgs)) line += " " + ExtraArgs;
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

// ============================================================
//  Part 2 / セッション
// ============================================================

public class SessionInfo
{
    public string Folder;        // セッションフォルダのフルパス
    public string Agent;         // 作成時に使ったエージェント
    public string RootModel;     // 起点モデル名
    public string Created;

    public string DesignDir() { return Path.Combine(Folder, "design"); }
    public string ReviewDir() { return Path.Combine(Folder, "review"); }
    public string SessionIniPath() { return Path.Combine(Folder, "session.ini"); }

    public void Save()
    {
        var nl = "\r\n";
        var sb = new StringBuilder();
        sb.Append("# AgentReview セッション情報（拡張機能が管理。編集不要）").Append(nl);
        sb.Append("agent=").Append(Agent).Append(nl);
        sb.Append("rootModel=").Append(RootModel).Append(nl);
        sb.Append("created=").Append(Created).Append(nl);
        File.WriteAllText(SessionIniPath(), sb.ToString(), new UTF8Encoding(false));
    }

    public static SessionInfo LoadFrom(string folder)
    {
        var path = Path.Combine(folder, "session.ini");
        if (!File.Exists(path)) return null;
        var info = new SessionInfo { Folder = folder };
        foreach (var pair in IniFile.Read(path))
        {
            switch (pair.Key)
            {
                case "agent": info.Agent = pair.Value; break;
                case "rootModel": info.RootModel = pair.Value; break;
                case "created": info.Created = pair.Value; break;
            }
        }
        return info;
    }
}

public static class SessionLocator
{
    // 基点フォルダ配下で最新のセッション（session.ini を持つフォルダ）を探す
    public static SessionInfo FindLatest(string workspaceRoot)
    {
        if (string.IsNullOrEmpty(workspaceRoot) || !Directory.Exists(workspaceRoot)) return null;
        return Directory.GetDirectories(workspaceRoot)
            .Where(d => File.Exists(Path.Combine(d, "session.ini")))
            .OrderByDescending(d => Directory.GetCreationTimeUtc(d))
            .Select(d => SessionInfo.LoadFrom(d))
            .FirstOrDefault(s => s != null);
    }
}

// ============================================================
//  Part 3 / ワークスペースの構築
// ============================================================

public static class WorkspaceBuilder
{
    // セッションフォルダ一式を作り、SessionInfo を返す
    public static SessionInfo Build(string workspaceRoot, IModel root, AgentConfig config, string designMarkdown)
    {
        var baseName = AgentText.SafeFileName(root.Name);
        if (baseName.Length == 0) baseName = "design";
        var folder = Path.Combine(workspaceRoot, baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));

        var session = new SessionInfo
        {
            Folder = folder,
            Agent = config.Agent,
            RootModel = root.Name,
            Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(session.DesignDir());
        Directory.CreateDirectory(session.ReviewDir());
        Directory.CreateDirectory(Path.Combine(session.ReviewDir(), "proposed"));

        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(session.DesignDir(), "design.md"), designMarkdown, utf8);

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
        sb.Append("- `design/design.md` : Next Design からエクスポートした設計情報（モデル階層とフィールド）").Append(nl);
        sb.Append("- `design/*.puml` : 図の PlantUML（存在する場合。クラス図・シーケンス図・状態遷移図）").Append(nl);
        sb.Append("- `design/_index.md` : 図とモデルパスの対応表（存在する場合）").Append(nl).Append(nl);
        sb.Append("**`design/` 配下のファイルを変更・削除してはならない。** 入力の原本である。").Append(nl).Append(nl);

        sb.Append("## 出力（このフォルダ規約に従うこと）").Append(nl).Append(nl);
        sb.Append("- `review/review.md` : レビュー指摘の一覧。次の表形式で書く。").Append(nl);
        sb.Append("  `| No | 重要度(高/中/低) | 対象（モデルパスまたは図名） | 指摘 | 根拠 | 修正方針 |`").Append(nl);
        sb.Append("- `review/proposal.md` : 修正提案。指摘 No と対応付け、修正後の設計を具体的に書く").Append(nl);
        sb.Append("- `review/proposed/*.puml` : 修正後の図（図の変更を提案する場合）").Append(nl).Append(nl);
        sb.Append("Next Design のモデルを直接編集することはできない。提案は必ず上記ファイルに書く。").Append(nl);
        sb.Append("修正提案はユーザーが Next Design 上で手作業で反映できる粒度（対象モデルパス・").Append(nl);
        sb.Append("フィールド名・変更前後の値）まで具体化すること。").Append(nl).Append(nl);

        if (perspectives.Count > 0)
        {
            sb.Append("## レビュー観点").Append(nl).Append(nl);
            foreach (var p in perspectives)
                sb.Append("- ").Append(p).Append(nl);
            sb.Append(nl);
        }

        sb.Append("## 進め方").Append(nl).Append(nl);
        sb.Append("1. まず `design/design.md`（と図があれば `design/*.puml`）を読み、設計の全体像を把握する").Append(nl);
        sb.Append("2. ユーザーが「レビューして」等と入力したらレビューを実施し、`review/review.md` に指摘を書き出す").Append(nl);
        sb.Append("3. 指摘の要約を会話で提示し、ユーザーと対話しながら深掘り・取捨選択する").Append(nl);
        sb.Append("4. ユーザーが合意した指摘について `review/proposal.md`（必要なら `review/proposed/*.puml`）に修正提案をまとめる").Append(nl);

        return sb.ToString();
    }
}

// ============================================================
//  Part 4 / 設計情報の Markdown 出力
//
//    DesignExporter（コミット 46ac9c9、後に revert）の MarkdownExporter を
//    図の PlantUML 埋め込み無しで自己完結化して移植。
//    revert の原因だった匿名参照フィールドのノイズは AgentText.IsSystemName
//    （$ / ___ 始まり）による除外で対策済み。
//
//    出力規約:
//      - リッチテキスト型フィールド（ドキュメントの本文）は
//        GetRichTextField(html) → HtmlToMarkdown で Markdown 化して出す
//      - 所有（クラス型）フィールドはフィールドとして出さず、
//        フィールド名の太字行 + 子セクション（見出し再帰）で出力する
//        （表の行モデルがどの表に属すかの文脈を保つため）
//      - Name / $・___ 始まりのシステムフィールド / 空値は出さない
//      - フィールド値はフェンスで囲まず箇条書き + インデント継続で出す
// ============================================================

public class MarkdownExportOptions
{
    public string NewLine = "\n";           // 改行は LF 固定
    public bool EmitTimestamp = true;       // 冒頭に出力日時を入れる
    public int MaxHeadingLevel = 6;         // Markdown 見出しの上限（# の最大数）
}

public class MarkdownExporter
{
    private readonly MarkdownExportOptions _options;
    private readonly HashSet<string> _visited = new HashSet<string>(StringComparer.Ordinal);
    private StringBuilder _sb;

    public int ModelCount;
    public List<string> Warnings = new List<string>();

    public MarkdownExporter(MarkdownExportOptions options)
    {
        _options = options ?? new MarkdownExportOptions();
    }

    public string Export(IModel root)
    {
        var nl = _options.NewLine;
        _sb = new StringBuilder();
        _visited.Clear();
        ModelCount = 0;
        Warnings.Clear();

        // 件数をプリアンブルに載せるため、本文を先に組み立てる
        WriteModel(root, 0, null);
        var body = _sb.ToString();

        var head = new StringBuilder();
        head.Append("<!-- Next Design 設計情報エクスポート (AgentReview) -->").Append(nl);
        head.Append(nl);
        head.Append("- 起点モデルパス: ").Append(PathOf(root)).Append(nl);
        head.Append("- モデル数: ").Append(ModelCount).Append(nl);
        if (_options.EmitTimestamp)
            head.Append("- 出力日時: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(nl);
        head.Append(nl);

        return head.ToString() + body;
    }

    // trail: 見出しレベルが上限に達した祖先（上限レベルのモデル）からの名前の連なり。
    //        上限未満の深さでは null
    private void WriteModel(IModel m, int depth, List<string> trail)
    {
        if (m == null || m.IsDeleted || m.IsProxy) return;
        if (!_visited.Add(m.Id)) return;   // 再訪ガード（循環・重複列挙の保険）

        try
        {
            ModelCount++;
            var nl = _options.NewLine;
            var level = Math.Min(depth + 1, _options.MaxHeadingLevel);
            var name = AgentText.Normalize(m.Name);
            if (name.Length == 0) name = "(無名)";

            // 上限を超えた深さは、上限レベルの祖先からの相対パスを見出しにして階層を保つ
            var capped = depth + 1 >= _options.MaxHeadingLevel;
            List<string> myTrail = null;
            var heading = name;
            if (capped)
            {
                myTrail = trail != null ? new List<string>(trail) : new List<string>();
                myTrail.Add(name);
                heading = string.Join(" / ", myTrail.ToArray());
            }

            // メタクラスは短縮名を見出しに付記するだけに留める
            // （完全修飾名とパスの引用ブロックはノイズが大きく実機で不評だった）
            _sb.Append(new string('#', level)).Append(' ').Append(heading);
            var shortCls = ShortClassName(m);
            if (shortCls.Length > 0) _sb.Append("（").Append(shortCls).Append("）");
            _sb.Append(nl);
            _sb.Append(nl);

            WriteFields(m);
            WriteChildren(m, depth, myTrail);
        }
        catch (Exception ex)
        {
            // 1 モデルの失敗で全体を落とさない
            Warnings.Add(PathOf(m) + " : " + ex.Message);
        }
    }

    // 子モデルの出力。所有フィールド単位で列挙し、フィールド名の小見出しで
    // 表・区画の文脈を保つ（GetChildren は全所有フィールドを平坦化して返し、
    // どのフィールドに属すかが失われるため）
    private void WriteChildren(IModel m, int depth, List<string> myTrail)
    {
        var nl = _options.NewLine;
        var cls = m.Metaclass;
        if (cls != null)
        {
            List<IField> fields;
            try { fields = cls.GetFields().Cast<IField>().ToList(); }
            catch (Exception) { fields = new List<IField>(); }

            foreach (var f in fields)
            {
                try
                {
                    if (f == null || !f.IsEmbedded || f.TypeClass == null) continue;

                    var children = new List<IModel>();
                    foreach (var v in m.GetFieldValues(f.Name))
                    {
                        var child = v as IModel;
                        if (child == null || child.IsDeleted || child.IsProxy) continue;
                        if (_visited.Contains(child.Id)) continue;
                        children.Add(child);
                    }
                    if (children.Count == 0) continue;

                    // システム・匿名フィールドは名前を出さず配下だけ出力する
                    if (!AgentText.IsSystemName(f.Name))
                        _sb.Append("**").Append(f.Name).Append("**").Append(nl).Append(nl);

                    foreach (var child in children)
                        WriteModel(child, depth + 1, myTrail);
                }
                catch (Exception ex)
                {
                    Warnings.Add(PathOf(m) + " / " + f.Name + " : 子モデルの列挙に失敗 : " + ex.Message);
                }
            }
        }

        // 安全網: フィールド列挙から漏れた所有子を GetChildren で拾う
        try
        {
            foreach (var child in m.GetChildren().Cast<IModel>().ToList())
            {
                if (child == null || _visited.Contains(child.Id)) continue;
                WriteModel(child, depth + 1, myTrail);
            }
        }
        catch (Exception ex)
        {
            Warnings.Add(PathOf(m) + " : 子モデルの取得に失敗 : " + ex.Message);
        }
    }

    private void WriteFields(IModel m)
    {
        var cls = m.Metaclass;
        if (cls == null) return;

        var nl = _options.NewLine;
        List<IField> fields;
        try { fields = cls.GetFields().Cast<IField>().ToList(); }
        catch (Exception ex)
        {
            Warnings.Add(PathOf(m) + " : フィールド一覧の取得に失敗 : " + ex.Message);
            return;
        }

        var wrote = false;
        foreach (var f in fields)
        {
            try
            {
                if (f == null || IsSystemField(f)) continue;

                // ドキュメントエディタの本文はリッチテキスト型フィールドに
                // 格納されており GetFieldString では取得できない
                if (f.Type == "RichText")
                {
                    if (WriteRichTextField(m, f)) wrote = true;
                    continue;
                }

                if (f.IsEmbedded) continue;   // 所有（クラス型）は子セクションで出す（二重化回避）

                if (f.IsReference)
                {
                    var names = new List<string>();
                    foreach (var v in m.GetFieldValues(f.Name))
                    {
                        var target = v as IModel;
                        if (target == null) continue;
                        var refName = AgentText.Normalize(target.Name);
                        names.Add(refName.Length > 0 ? refName : "(無名)");
                    }
                    if (names.Count == 0) continue;
                    _sb.Append("- ").Append(f.Name).Append(" (参照): ")
                       .Append(string.Join(", ", names.ToArray())).Append(nl);
                    wrote = true;
                }
                else
                {
                    string value = null;
                    try { value = m.GetFieldString(f.Name); }
                    catch (Exception) { }
                    if (string.IsNullOrEmpty(value) || value.Trim().Length == 0) continue;

                    var lines = value.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                    if (lines.Length == 1)
                    {
                        _sb.Append("- ").Append(f.Name).Append(": ").Append(lines[0]).Append(nl);
                    }
                    else
                    {
                        // 複数行はインデント継続で崩さず出す
                        _sb.Append("- ").Append(f.Name).Append(":").Append(nl);
                        foreach (var line in lines)
                            _sb.Append("  ").Append(line).Append(nl);
                    }
                    wrote = true;
                }
            }
            catch (Exception ex)
            {
                Warnings.Add(PathOf(m) + " / " + f.Name + " : " + ex.Message);
            }
        }
        if (wrote) _sb.Append(nl);
    }

    // リッチテキストは html で取得して Markdown 化する。失敗時は text にフォールバック
    private bool WriteRichTextField(IModel m, IField f)
    {
        var nl = _options.NewLine;
        string text = null;
        try
        {
            var html = m.GetRichTextField(f.Name, "html");
            if (!string.IsNullOrEmpty(html)) text = HtmlToMarkdown.Convert(html);
        }
        catch (Exception ex)
        {
            Warnings.Add(PathOf(m) + " / " + f.Name + " : リッチテキストの変換に失敗 : " + ex.Message);
        }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
        {
            try { text = m.GetRichTextField(f.Name, "text"); }
            catch (Exception) { }
        }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return false;

        _sb.Append("**").Append(f.Name).Append("**:").Append(nl).Append(nl);
        _sb.Append(text.Replace("\r\n", "\n").Replace("\r", "\n").Trim('\n')).Append(nl);
        _sb.Append(nl);
        return true;
    }

    private static string ShortClassName(IModel m)
    {
        string full = null;
        try
        {
            var cls = m.Metaclass;
            full = cls != null ? cls.FullName : m.ClassName;
        }
        catch (Exception) { }
        if (string.IsNullOrEmpty(full)) return "";
        var dot = full.LastIndexOf('.');
        return dot >= 0 ? full.Substring(dot + 1) : full;
    }

    private static bool IsSystemField(IField f)
    {
        var name = f.Name ?? "";
        if (name == "Name") return true;   // 見出しと重複するため出さない
        return AgentText.IsSystemName(name);
    }

    private static string PathOf(IModel m)
    {
        if (m == null) return "";
        string path = null;
        try { path = m.ModelPath; }
        catch (Exception) { }
        return string.IsNullOrEmpty(path) ? (m.Name ?? "") : path;
    }
}

// ------------------------------------------------------------
//  リッチテキスト(HTML)の簡易 Markdown 変換
//    Next Design のリッチテキストフィールドが返す HTML を、
//    生成 AI が読みやすい Markdown に落とす。表は Markdown 表に、
//    ブロック要素は改行に変換し、その他のタグは除去する
// ------------------------------------------------------------
public static class HtmlToMarkdown
{
    private static readonly Regex TableRe = new Regex("<table[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex RowRe = new Regex("<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex CellRe = new Regex("<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex TagRe = new Regex("<[^>]+>", RegexOptions.Singleline);
    private static readonly Regex StyleRe = new Regex("<(style|script)[^>]*>.*?</\\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public static string Convert(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = html.Replace("\r\n", "\n").Replace("\r", "\n");
        s = StyleRe.Replace(s, "");

        // 表を先に Markdown 化して退避する（後段のタグ除去で壊さないため）
        var tables = new List<string>();
        s = TableRe.Replace(s, match =>
        {
            tables.Add(ConvertTable(match.Groups[1].Value));
            return "\n[[TABLE" + (tables.Count - 1) + "]]\n";
        });

        s = Regex.Replace(s, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<h[1-6][^>]*>", "\n**", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "</h[1-6]>", "**\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "</(p|div|li|ul|ol)>", "\n", RegexOptions.IgnoreCase);
        s = TagRe.Replace(s, "");
        s = DecodeEntities(s);

        for (var i = 0; i < tables.Count; i++)
            s = s.Replace("[[TABLE" + i + "]]", tables[i]);

        // 行末空白と連続する空行を整理する
        var sb = new StringBuilder();
        var blank = 0;
        foreach (var raw in s.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                blank++;
                if (blank >= 2) continue;
            }
            else blank = 0;
            sb.Append(line).Append('\n');
        }
        return sb.ToString().Trim('\n');
    }

    private static string ConvertTable(string inner)
    {
        var rows = new List<List<string>>();
        foreach (Match row in RowRe.Matches(inner))
        {
            var cells = new List<string>();
            foreach (Match cell in CellRe.Matches(row.Groups[1].Value))
                cells.Add(CellText(cell.Groups[1].Value));
            if (cells.Count > 0) rows.Add(cells);
        }
        if (rows.Count == 0) return "";

        var width = rows.Max(r => r.Count);
        var sb = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            sb.Append('|');
            for (var c = 0; c < width; c++)
                sb.Append(' ').Append(c < row.Count ? row[c] : "").Append(" |");
            sb.Append('\n');
            if (i == 0)   // 1 行目をヘッダとして区切り行を入れる
            {
                sb.Append('|');
                for (var c = 0; c < width; c++) sb.Append("---|");
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    // セル内は改行を <br> 表記にし、| をエスケープして 1 行に潰す
    private static string CellText(string inner)
    {
        var s = Regex.Replace(inner, "<br\\s*/?>", "[[BR]]", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "</(p|div|li)>", "[[BR]]", RegexOptions.IgnoreCase);
        s = TagRe.Replace(s, "");
        s = DecodeEntities(s);
        s = s.Replace("\n", " ").Replace("|", "\\|");
        s = AgentText.Normalize(s);
        var text = s.Replace("[[BR]]", "<br>").Trim();
        while (text.EndsWith("<br>", StringComparison.Ordinal))
            text = text.Substring(0, text.Length - 4).TrimEnd();
        return text;
    }

    private static string DecodeEntities(string s)
    {
        s = s.Replace("&nbsp;", " ").Replace("&quot;", "\"").Replace("&#39;", "'")
             .Replace("&lt;", "<").Replace("&gt;", ">");
        s = Regex.Replace(s, "&#(\\d+);", m =>
        {
            try { return char.ConvertFromUtf32(int.Parse(m.Groups[1].Value)); }
            catch (Exception) { return ""; }
        });
        s = Regex.Replace(s, "&#x([0-9a-fA-F]+);", m =>
        {
            try { return char.ConvertFromUtf32(System.Convert.ToInt32(m.Groups[1].Value, 16)); }
            catch (Exception) { return ""; }
        });
        return s.Replace("&amp;", "&");
    }
}

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

    // エクスプローラーや既定アプリで開く（完了は待たない）
    public static void OpenWithShell(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
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

// ============================================================
//  Part 6 / コマンドハンドラ
// ============================================================

public void StartAgentReview(ICommandContext context, ICommandParams commandParams)
{
    var category = "AgentReview";
    var app = context.App;
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

        var message = "「" + root.Name + "」配下の設計情報をエクスポートし、"
            + profile.DisplayName + " によるレビューを開始します。\n\n"
            + "エージェント: " + profile.DisplayName + "（コマンド: " + profile.Command + "）\n"
            + "作成先: " + config.WorkspaceRoot + "\n\n"
            + "ターミナルが開いたら「レビューして」と入力してください。続行しますか？";
        if (!app.Window.UI.ShowConfirmDialog(message, category)) return;

        OutputPane.Show(app, category);
        app.Output.WriteLine(category, "=== レビュー開始 : " + root.Name + " (" + profile.DisplayName + ") ===");

        app.Output.WriteLine(category, "[1/3] 設計情報をエクスポートしています...");
        var exporter = new MarkdownExporter(new MarkdownExportOptions());
        var markdown = exporter.Export(root);
        foreach (var warning in exporter.Warnings)
            app.Output.WriteLine(category, "[warn]  " + warning);

        app.Output.WriteLine(category, "[2/3] ワークスペースを作成しています...");
        var session = WorkspaceBuilder.Build(config.WorkspaceRoot, root, config, markdown);
        app.Output.WriteLine(category, "[dir]   " + session.Folder);
        app.Output.WriteLine(category, "[info]  モデル " + exporter.ModelCount + " 件を design\\design.md に出力");
        app.Output.WriteLine(category, "[hint]  図も渡す場合は PlantUmlTool の一括出力で design\\ に .puml を置いてから対話を始めてください");

        app.Output.WriteLine(category, "[3/3] ターミナルで " + profile.DisplayName + " を起動しています...");
        TerminalLauncher.Launch(session.Folder, profile.BuildLaunchCommand(), config.Terminal);

        app.Output.WriteLine(category, "");
        app.Output.WriteLine(category, "=== 起動完了 ===");
        app.Output.WriteLine(category, "ターミナルで「レビューして」と入力すると、指示書（" + profile.InstructionFileName + "）に従いレビューが始まります。");
        app.Output.WriteLine(category, "指摘は review\\review.md、修正提案は review\\proposal.md に出力されます（リボンの「結果を開く」で参照）。");
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] " + ex.ToString());
        app.Window.UI.ShowInformationDialog("レビュー開始に失敗しました。\n\n" + ex.Message, category);
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

        var reviewPath = Path.Combine(session.ReviewDir(), "review.md");
        var proposalPath = Path.Combine(session.ReviewDir(), "proposal.md");
        var opened = 0;
        if (File.Exists(reviewPath)) { TerminalLauncher.OpenWithNotepad(reviewPath); opened++; }
        if (File.Exists(proposalPath)) { TerminalLauncher.OpenWithNotepad(proposalPath); opened++; }

        if (opened == 0)
        {
            app.Window.UI.ShowInformationDialog(
                "レビュー結果がまだ生成されていません。\n\n"
                + "エージェントがターミナルで review\\review.md を書き出すと開けるようになります。\n"
                + "セッション: " + session.Folder, category);
        }
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] " + ex.ToString());
        app.Window.UI.ShowInformationDialog("結果を開けませんでした。\n\n" + ex.Message, category);
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
        if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
        {
            app.Window.UI.ShowInformationDialog(
                "開くフォルダがありません。\n先に「レビュー開始」を実行してください。", category);
            return;
        }
        TerminalLauncher.OpenWithShell(target);
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

public void OpenConfig(ICommandContext context, ICommandParams commandParams)
{
    var category = "AgentReview";
    var app = context.App;
    try
    {
        if (!File.Exists(AgentConfig.ConfigPath()))
            new AgentConfig().Save();   // 既定値 + コメント付きで生成
        TerminalLauncher.OpenWithNotepad(AgentConfig.ConfigPath());
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

// 選択モデル 1 件のフィールド構成・子モデル・エディタを出力ウィンドウにダンプする。
// design.md に出ない情報がある場合の切り分け用（プロファイル依存の実測）
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
        app.Output.WriteLine(category, "ClassName : " + root.ClassName);
        var cls = root.Metaclass;
        app.Output.WriteLine(category, "Metaclass : " + (cls != null ? cls.FullName : "(null)"));
        string modelPath = null;
        try { modelPath = root.ModelPath; } catch (Exception) { }
        app.Output.WriteLine(category, "ModelPath : " + (modelPath ?? ""));
        app.Output.WriteLine(category, "");

        app.Output.WriteLine(category, "--- フィールド ---");
        if (cls != null)
        {
            foreach (var f in cls.GetFields().Cast<IField>())
            {
                if (f == null) continue;
                app.Output.WriteLine(category, f.Name + " : Type=" + f.Type
                    + " Embedded=" + f.IsEmbedded + " Reference=" + f.IsReference
                    + " 多重度=" + f.LowerBound + ".." + f.UpperBound);
                if (f.Type == "RichText")
                {
                    try
                    {
                        var html = root.GetRichTextField(f.Name, "html");
                        var preview = html == null ? "(null)" : html.Replace("\r", "").Replace("\n", " ");
                        if (preview.Length > 200) preview = preview.Substring(0, 200) + "...";
                        app.Output.WriteLine(category, "    [richtext html] " + preview);
                    }
                    catch (Exception ex)
                    {
                        app.Output.WriteLine(category, "    [richtext html] 取得失敗: " + ex.Message);
                    }
                }
                else if (!f.IsEmbedded && !f.IsReference)
                {
                    try
                    {
                        var value = root.GetFieldString(f.Name) ?? "";
                        value = value.Replace("\r", "").Replace("\n", " ");
                        if (value.Length > 80) value = value.Substring(0, 80) + "...";
                        if (value.Trim().Length > 0)
                            app.Output.WriteLine(category, "    [value] " + value);
                    }
                    catch (Exception) { }
                }
            }
        }

        app.Output.WriteLine(category, "");
        app.Output.WriteLine(category, "--- 子モデル (GetChildren) ---");
        try
        {
            var children = root.GetChildren().Cast<IModel>().ToList();
            app.Output.WriteLine(category, "件数: " + children.Count);
            var shown = 0;
            foreach (var child in children)
            {
                if (child == null) continue;
                app.Output.WriteLine(category, "  " + (child.Name ?? "(無名)") + " (" + child.ClassName + ")");
                if (++shown >= 50)
                {
                    app.Output.WriteLine(category, "  ...（以降省略）");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "取得失敗: " + ex.Message);
        }

        app.Output.WriteLine(category, "");
        app.Output.WriteLine(category, "--- エディタ (GetEditors) ---");
        try
        {
            foreach (var editor in root.GetEditors())
            {
                if (editor == null) continue;
                var defName = "";
                try
                {
                    var def = editor.EditorDefinition;
                    if (def != null) defName = def.DisplayName ?? def.Name ?? "";
                }
                catch (Exception) { }
                app.Output.WriteLine(category, "  EditorType=" + editor.EditorType
                    + (defName.Length > 0 ? " 定義=" + defName : ""));
            }
        }
        catch (Exception ex)
        {
            app.Output.WriteLine(category, "取得失敗: " + ex.Message);
        }

        app.Output.WriteLine(category, "=== 診断完了 ===");
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
