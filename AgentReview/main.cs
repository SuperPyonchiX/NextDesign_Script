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
//      Part 7  PlantUML 出力エンジン（PlantUmlTool Part 0/7/8 の転記。末尾）
// ============================================================

using NextDesign.Core;
using NextDesign.Desktop;
using NextDesign.Extension;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    // （design\ の中身＝design.md と .puml はエクスポータが後から書く）
    public static SessionInfo Build(string workspaceRoot, IModel root, AgentConfig config)
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
        sb.Append("- `design/*.puml` : 図の PlantUML（シーケンス図・クラス図・状態遷移図。design.md の該当箇所に参照行がある）").Append(nl);
        sb.Append("- `design/_index.md` : 図一覧（図名・種別・ファイル・モデルパスの対応表）").Append(nl).Append(nl);
        sb.Append("design.md にはシーケンス図・状態遷移図の中身は含まれない。挙動は参照先の .puml を読むこと。").Append(nl).Append(nl);
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

    // 図の .puml 出力（null なら図は出力しない）
    private readonly string _diagramDir;
    private readonly HashSet<string> _seenEditors = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly PlantUmlOptions _seqOptions = new PlantUmlOptions();
    private readonly ClassPlantUmlOptions _classOptions = new ClassPlantUmlOptions();
    private readonly StatePlantUmlOptions _stateOptions = new StatePlantUmlOptions();

    public int ModelCount;
    public int DiagramCount;
    public int SkippedModelCount;   // 図の構成要素としてテキスト出力から除外したモデル数
    public List<string> Warnings = new List<string>();
    public List<string> IndexRows = new List<string>();   // _index.md 用「| 図名 | 種別 | ファイル | モデルパス |」

    public MarkdownExporter(MarkdownExportOptions options, string diagramDir)
    {
        _options = options ?? new MarkdownExportOptions();
        _diagramDir = diagramDir;
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

            // 図は .puml に出力して参照行を書く。シーケンス図・状態遷移図を持つ
            // モデルの配下は図の構成要素（メッセージ・実行仕様・状態など）なので、
            // テキストには出さず .puml 参照に委ねる
            var isBehaviorDiagram = WriteDiagrams(m);
            if (isBehaviorDiagram)
            {
                SkippedModelCount += CountSubtree(m);
                return;
            }

            WriteChildren(m, depth, myTrail);
        }
        catch (Exception ex)
        {
            // 1 モデルの失敗で全体を落とさない
            Warnings.Add(PathOf(m) + " : " + ex.Message);
        }
    }

    // モデルが持つ図を .puml に出力し、参照行を書く。
    // 戻り値: シーケンス図または状態遷移図を持っていたか（＝子モデルへの再帰を打ち切るか）
    private bool WriteDiagrams(IModel m)
    {
        if (_diagramDir == null) return false;
        var nl = _options.NewLine;
        var skipChildren = false;
        var refs = new List<string>();

        try
        {
            foreach (var editor in m.GetEditors())
            {
                if (editor == null) continue;
                try
                {
                    if (!_seenEditors.Add(editor.Id)) continue;

                    var seq = editor as ISequenceDiagram;
                    if (seq != null)
                    {
                        skipChildren = true;   // 空図でも配下は図要素なのでテキストに出さない
                        if (!seq.Lifelines.Cast<ILifelineShape>().Any()) continue;
                        var seqName = seq.Model != null && !string.IsNullOrEmpty(seq.Model.Name)
                            ? seq.Model.Name
                            : (string.IsNullOrEmpty(seq.ViewDefinitionName) ? "Sequence" : seq.ViewDefinitionName);
                        var uml = new SequencePlantUmlExporter(seq, _seqOptions).Export();
                        var file = SaveDiagram(seqName, "_seq", uml);
                        refs.Add("- 図: [" + seqName + "](" + file + ")（シーケンス図）");
                        AddIndexRow(seqName, "シーケンス図", file, m);
                        continue;
                    }

                    var diagram = editor as IDiagram;
                    if (diagram == null) continue;

                    var representation = editor as IRepresentation;
                    var diagramName = representation != null && representation.Model != null
                        && !string.IsNullOrEmpty(representation.Model.Name)
                        ? representation.Model.Name : (m.Name ?? "Diagram");

                    if (StateExportRunner.IsStateDiagram(diagram, _stateOptions))
                    {
                        skipChildren = true;
                        var exporter = new StatePlantUmlExporter(diagram, _stateOptions);
                        var uml = exporter.Export();
                        foreach (var warning in exporter.Warnings) Warnings.Add(warning);
                        var file = SaveDiagram(diagramName, "_state", uml);
                        refs.Add("- 図: [" + diagramName + "](" + file + ")（状態遷移図）");
                        AddIndexRow(diagramName, "状態遷移図", file, m);
                    }
                    else if (ClassExportRunner.IsClassDiagramEditor(editor))
                    {
                        // クラス図は図要素＝クラス設計そのものなので子の再帰は続ける
                        var exporter = new ClassPlantUmlExporter(diagram, _classOptions);
                        var uml = exporter.Export();
                        foreach (var warning in exporter.Warnings) Warnings.Add(warning);
                        var file = SaveDiagram(diagramName, "_class", uml);
                        refs.Add("- 図: [" + diagramName + "](" + file + ")（クラス図）");
                        AddIndexRow(diagramName, "クラス図", file, m);
                    }
                }
                catch (Exception ex)
                {
                    Warnings.Add(PathOf(m) + " : 図の出力に失敗 : " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Warnings.Add(PathOf(m) + " : エディタ一覧の取得に失敗 : " + ex.Message);
        }

        if (refs.Count > 0)
        {
            foreach (var line in refs) _sb.Append(line).Append(nl);
            _sb.Append(nl);
        }
        return skipChildren;
    }

    private string SaveDiagram(string name, string suffix, string uml)
    {
        var baseName = AgentText.SafeFileName(name);
        if (baseName.Length == 0) baseName = "diagram";
        var file = baseName + suffix + ".puml";
        var serial = 2;
        while (!_usedFileNames.Add(file))
        {
            file = baseName + suffix + "_" + serial + ".puml";
            serial++;
        }
        File.WriteAllText(Path.Combine(_diagramDir, file), uml, new UTF8Encoding(false));
        DiagramCount++;
        return file;
    }

    private void AddIndexRow(string name, string kind, string file, IModel owner)
    {
        IndexRows.Add("| " + name.Replace("|", "\\|") + " | " + kind + " | " + file
            + " | " + PathOf(owner).Replace("|", "\\|") + " |");
    }

    private static int CountSubtree(IModel m)
    {
        try { return m.GetAllChildren().Cast<IModel>().Count(); }
        catch (Exception) { return 0; }
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

        app.Output.WriteLine(category, "[1/3] ワークスペースを作成しています...");
        var session = WorkspaceBuilder.Build(config.WorkspaceRoot, root, config);
        app.Output.WriteLine(category, "[dir]   " + session.Folder);

        app.Output.WriteLine(category, "[2/3] 設計情報と図をエクスポートしています...");
        var exporter = new MarkdownExporter(new MarkdownExportOptions(), session.DesignDir());
        var markdown = exporter.Export(root);

        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(session.DesignDir(), "design.md"), markdown, utf8);
        if (exporter.IndexRows.Count > 0)
        {
            var index = new StringBuilder();
            index.Append("# 図一覧\n\n");
            index.Append("| 図名 | 種別 | ファイル | モデルパス |\n");
            index.Append("|---|---|---|---|\n");
            foreach (var row in exporter.IndexRows) index.Append(row).Append('\n');
            File.WriteAllText(Path.Combine(session.DesignDir(), "_index.md"), index.ToString(), utf8);
        }

        foreach (var warning in exporter.Warnings)
            app.Output.WriteLine(category, "[warn]  " + warning);
        app.Output.WriteLine(category, "[info]  モデル " + exporter.ModelCount + " 件を design\\design.md に出力");
        app.Output.WriteLine(category, "[info]  図 " + exporter.DiagramCount + " 件を design\\*.puml に出力"
            + (exporter.SkippedModelCount > 0
                ? "（図の構成要素 " + exporter.SkippedModelCount + " モデルはテキスト出力から除外）" : ""));

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

// ============================================================
//  Part 7 / PlantUML 出力エンジン（PlantUmlTool からの転記）
//
//    転記元: PlantUmlTool/main.cs（コミット 50ae431 時点の Part 0 / 7 / 8）。
//    修正はまず PlantUmlTool 側で実機検証してからこちらへ反映すること。
//    差分: OutputPane は AgentReview 側の同シグネチャ実装を使うため除外。
//          MetaMap は ModelOf のみ使用するため下のシムで代替。
//    ExportRunner / ClassExportRunner / StateExportRunner のダイアログを使う
//    メソッドは AgentReview のリボンからは呼ばれない（判定ヘルパのみ使用）。
// ============================================================

// MetaMap シム（転記元 Part 2 の小道具メソッドのみ）
public static class MetaMap
{
    public static IModel ModelOf(object shape)
    {
        var representation = shape as IRepresentation;
        return representation != null ? representation.Model : null;
    }
}

// ------------------------------------------------------------
//  出力オプション
// ------------------------------------------------------------
public class PlantUmlOptions
{
    public bool IncludeTitle = true;          // 図名を title として出力する
    public bool UseAutonumber = false;        // autonumber を出力する
    public string Theme = null;               // !theme <name> を出力する
    public bool EmitNotes = true;             // ノートを出力する
    public bool EmitActivation = true;        // activate / deactivate を出力する
    public bool UseTypeKeywords = true;       // 型名から actor / boundary などを出し分ける
    public bool UseCreateParticipant = true;  // 生成メッセージを create で表現する
    public bool EmitTimestamp = false;        // 出力日時を埋め込む（差分安定化のため既定 false）
    public string IndentUnit = "  ";          // 入れ子のインデント
    public string NewLine = "\n";             // 改行は LF 固定
    public string AliasStyle = "Name";        // "Name" | "Id"
    public double BoundaryEpsilon = 1.0;      // フラグメント下端の判定誤差
    public double ActivationSnapTolerance = 10.0;  // 実行仕様の端をメッセージに吸着させる許容距離
    public string RefBackgroundColor = "#EFEFEF";  // ref（相互作用の利用）の背景色。空なら既定のまま

    // 複合フラグメントのテキスト先頭語 → PlantUML の演算子
    public Dictionary<string, string> OperatorMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "alt", "alt" }, { "opt", "opt" }, { "loop", "loop" },
        { "par", "par" }, { "break", "break" }, { "critical", "critical" },
        { "代替", "alt" }, { "選択", "alt" }, { "分岐", "alt" },
        { "条件", "opt" }, { "オプション", "opt" }, { "任意", "opt" },
        { "繰り返し", "loop" }, { "ループ", "loop" }, { "反復", "loop" },
        { "並行", "par" }, { "並列", "par" },
        { "中断", "break" },
        { "限界領域", "critical" }, { "クリティカル", "critical" },
    };

    // ライフラインの型名 → PlantUML の participant キーワード
    public Dictionary<string, string> TypeKeywordMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Actor", "actor" }, { "アクター", "actor" }, { "利用者", "actor" }, { "ユーザ", "actor" },
        { "Boundary", "boundary" }, { "バウンダリ", "boundary" },
        { "Control", "control" }, { "コントロール", "control" },
        { "Entity", "entity" }, { "エンティティ", "entity" },
        { "Database", "database" }, { "データベース", "database" },
        { "Queue", "queue" }, { "キュー", "queue" },
    };
}

// ------------------------------------------------------------
//  文字列ユーティリティ
// ------------------------------------------------------------
public class PlantUmlText
{
    // 実行ごとに値が変わらないハッシュ（string.GetHashCode は使わない）
    public static string ShortHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            var t = s ?? "";
            for (var i = 0; i < t.Length; i++)
            {
                h ^= t[i];
                h *= 16777619;
            }
            return h.ToString("x8");
        }
    }

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

    // 改行を PlantUML のラベル用エスケープに変換する
    public static string Inline(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
    }

    public static string Quote(string s)
    {
        return "\"" + (s ?? "").Replace("\"", "'") + "\"";
    }

    // ASCII だけで別名を作る。作れない場合は空文字を返す
    public static string AsciiAlias(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? ""))
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                sb.Append(ch);
            else if (ch == '_' || ch == ' ' || ch == '-' || ch == '.')
                sb.Append('_');
        }
        var alias = sb.ToString().Trim('_');
        while (alias.Contains("__")) alias = alias.Replace("__", "_");
        if (alias.Length == 0) return "";
        if (alias[0] >= '0' && alias[0] <= '9') alias = "L" + alias;
        return alias;
    }

    // プロファイルが自動生成するシステム・匿名フィールド名（$ / ____ 始まり）か
    public static bool IsSystemName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.StartsWith("$", StringComparison.Ordinal)
            || s.StartsWith("____", StringComparison.Ordinal)
            || s.StartsWith("___", StringComparison.Ordinal);
    }

    public static string SafeFileName(string s)
    {
        var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());
        var sb = new StringBuilder();
        foreach (var ch in (s ?? ""))
            sb.Append(invalid.Contains(ch) || ch == ' ' ? '_' : ch);
        return sb.ToString().Trim('_', '.');
    }
}

// ------------------------------------------------------------
//  内部用：出力イベントと開いているフラグメント
// ------------------------------------------------------------
public class SeqEvent
{
    public double Y;
    public int Priority;
    public double X;
    public double Rank;          // フラグメントは面積の大きい順に並べるため負値を入れる
    public string Id = "";
    public string Kind = "";
    public string FragmentId = "";   // operand イベントが属するフラグメント
    public IMessageShape Message;
    public IFragmentShape Fragment;
    public IOperandShape Operand;
    public IExecutionSpecificationShape Execution;
    public IInteractionUseShape Use;
    public IDestructionShape Destruction;
    public INoteShape Note;
}

public class OpenFragment
{
    public string Id = "";
    public double Bottom;
}

// ------------------------------------------------------------
//  変換本体
// ------------------------------------------------------------
public class SequencePlantUmlExporter
{
    private readonly ISequenceDiagram _d;
    private readonly PlantUmlOptions _o;
    private readonly StringBuilder _sb = new StringBuilder();

    private readonly List<ILifelineShape> _lifelines = new List<ILifelineShape>();
    private readonly Dictionary<string, string> _alias = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _label = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _keyword = new Dictionary<string, string>();
    private readonly HashSet<string> _usedAlias = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _createdLater = new HashSet<string>();
    private readonly HashSet<string> _declared = new HashSet<string>();
    private readonly HashSet<string> _destroyed = new HashSet<string>();
    private readonly Dictionary<string, int> _activeCount = new Dictionary<string, int>();
    private readonly List<OpenFragment> _stack = new List<OpenFragment>();

    public SequencePlantUmlExporter(ISequenceDiagram diagram, PlantUmlOptions options)
    {
        _d = diagram;
        _o = options ?? new PlantUmlOptions();
    }

    public string Export()
    {
        PrepareLifelines();
        WriteHeader();
        WriteParticipants();
        WriteBody();
        DeactivateAll();
        CloseAllFragments();
        LineAt(0, "@enduml");
        return _sb.ToString();
    }

    public string DiagramName()
    {
        if (_d.Model != null && !string.IsNullOrEmpty(_d.Model.Name)) return _d.Model.Name;
        return string.IsNullOrEmpty(_d.ViewDefinitionName) ? "Sequence" : _d.ViewDefinitionName;
    }

    // ---------- 準備 ----------

    private void PrepareLifelines()
    {
        var ordered = _d.Lifelines.Cast<ILifelineShape>()
            .OrderBy(l => l.LocationX)
            .ThenBy(l => l.LocationY)
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var l in ordered)
        {
            _lifelines.Add(l);
            Register(l);
        }

        if (!_o.UseCreateParticipant) return;

        // 生成メッセージで作られるライフラインは create 宣言に回す
        foreach (var m in _d.Messages.Cast<IMessageShape>())
        {
            if (KindOf(m) != "create") continue;
            if (m.Receiver == null) continue;
            _createdLater.Add(m.Receiver.Id);
        }
    }

    private void Register(ILifelineShape l)
    {
        if (_alias.ContainsKey(l.Id)) return;

        var label = PlantUmlText.Normalize(l.Text);
        if (label.Length == 0 && l.TypeModel != null) label = PlantUmlText.Normalize(l.TypeModel.Name);
        if (label.Length == 0) label = "(unnamed)";
        _label[l.Id] = label;

        var alias = _o.AliasStyle == "Id" ? "" : PlantUmlText.AsciiAlias(label);
        if (alias.Length == 0) alias = "L" + PlantUmlText.ShortHash(l.Id);
        if (!_usedAlias.Add(alias))
        {
            alias = alias + "_" + PlantUmlText.ShortHash(l.Id);
            _usedAlias.Add(alias);
        }
        _alias[l.Id] = alias;
        _keyword[l.Id] = KeywordOf(l);
    }

    private string KeywordOf(ILifelineShape l)
    {
        if (!_o.UseTypeKeywords || l.TypeModel == null) return "participant";

        string keyword;
        var typeName = l.TypeModel.Name;
        if (!string.IsNullOrEmpty(typeName) && _o.TypeKeywordMap.TryGetValue(typeName, out keyword))
            return keyword;

        var className = l.TypeModel.ClassName;
        if (!string.IsNullOrEmpty(className) && _o.TypeKeywordMap.TryGetValue(className, out keyword))
            return keyword;

        return "participant";
    }

    private string AliasOf(ILifelineShape l)
    {
        if (l == null) return null;
        Register(l);
        return _alias[l.Id];
    }

    private string DeclarationOf(ILifelineShape l)
    {
        return _keyword[l.Id] + " " + PlantUmlText.Quote(_label[l.Id]) + " as " + _alias[l.Id];
    }

    private void EnsureDeclared(ILifelineShape l)
    {
        if (l == null) return;
        AliasOf(l);
        if (_declared.Add(l.Id)) Line(DeclarationOf(l));
    }

    // ---------- ヘッダとライフライン宣言 ----------

    private void WriteHeader()
    {
        LineAt(0, "@startuml");
        if (!string.IsNullOrEmpty(_o.Theme)) LineAt(0, "!theme " + _o.Theme);
        LineAt(0, "skinparam sequenceMessageAlign left");
        LineAt(0, "skinparam maxMessageSize 200");
        if (!string.IsNullOrEmpty(_o.RefBackgroundColor))
            LineAt(0, "skinparam sequenceReferenceBackgroundColor " + _o.RefBackgroundColor);
        if (_o.IncludeTitle) LineAt(0, "title " + PlantUmlText.Inline(DiagramName()));
        if (_o.EmitTimestamp) LineAt(0, "' exported at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (_o.UseAutonumber) LineAt(0, "autonumber");
        Blank();
    }

    private void WriteParticipants()
    {
        var any = false;
        foreach (var l in _lifelines)
        {
            if (_createdLater.Contains(l.Id)) continue;
            Line(DeclarationOf(l));
            _declared.Add(l.Id);
            any = true;
        }
        if (any) Blank();
    }

    // ---------- 本体 ----------

    private void WriteBody()
    {
        var events = new List<SeqEvent>();

        foreach (var m in _d.Messages.Cast<IMessageShape>())
        {
            events.Add(new SeqEvent
            {
                Y = m.SourceY, Priority = 50, X = MessageX(m),
                Id = m.Id, Kind = "message", Message = m
            });
        }

        foreach (var f in _d.Fragments.Cast<IFragmentShape>())
        {
            events.Add(new SeqEvent
            {
                Y = f.LocationY, Priority = 10, X = f.LocationX,
                Rank = -((double)f.Width * (double)f.Height),
                Id = f.Id, Kind = "fragment", Fragment = f
            });

            var operands = OperandsOf(f);
            var operandYs = OperandYs(f, operands);
            for (var i = 1; i < operands.Count; i++)   // 先頭のガードはヘッダ行に出す
            {
                events.Add(new SeqEvent
                {
                    Y = operandYs[i], Priority = 20, X = f.LocationX,
                    Id = operands[i].Id, Kind = "operand", Operand = operands[i],
                    FragmentId = f.Id
                });
            }
        }

        if (_o.EmitActivation)
        {
            var messages = _d.Messages.Cast<IMessageShape>().ToList();
            var executions = _d.ExecutionSpecifications.Cast<IExecutionSpecificationShape>().ToList();

            foreach (var e in executions)
            {
                var lifelineId = e.Lifeline != null ? e.Lifeline.Id : null;
                var top = (double)e.LocationY;
                var bottom = top + e.Length;

                // PlantUML の入れ子は「トリガのメッセージ行の直後に activate」で決まるが、
                // 図形上はバー上端とメッセージの Y が数ピクセルずれうる。
                // 最寄りのメッセージに吸着させてから並べる
                var activateY = top;
                var activatePriority = 60;   // 受信メッセージ(50)の直後
                var trigger = NearestMessage(messages, lifelineId, top, true);
                if (trigger != null)
                {
                    activateY = trigger.SourceY;

                    // セルフメッセージでは送信元の外側バーと受信で立つ内側バーの
                    // 両方が上端一致する。他のバーに包含されない最外殻のバーは
                    // 送信元なので、activate をメッセージより前に出す
                    var isSelf = trigger.Sender != null && trigger.Receiver != null
                              && trigger.Sender.Id == trigger.Receiver.Id;
                    if (isSelf && !executions.Any(o => ContainsExecution(o, e)))
                        activatePriority = 45;
                }
                else
                {
                    // 受信で立たないバーは送信メッセージが起点。activate をその送信より先に出す
                    var origin = NearestMessage(messages, lifelineId, top, false);
                    if (origin != null) { activateY = origin.SourceY; activatePriority = 45; }
                }

                // 下端は戻りメッセージ(50)の直後・次の activate(60) より前
                var deactivateY = bottom;
                var closer = NearestMessage(messages, lifelineId, bottom, false);
                if (closer != null) deactivateY = closer.SourceY;

                events.Add(new SeqEvent
                {
                    Y = activateY, Priority = activatePriority, X = e.LocationX,
                    Id = e.Id, Kind = "activate", Execution = e
                });
                events.Add(new SeqEvent
                {
                    Y = deactivateY, Priority = 55, X = e.LocationX,
                    Id = e.Id, Kind = "deactivate", Execution = e
                });
            }
        }

        foreach (var u in _d.InteractionUses.Cast<IInteractionUseShape>())
        {
            events.Add(new SeqEvent
            {
                Y = u.LocationY, Priority = 30, X = u.LocationX,
                Id = u.Id, Kind = "use", Use = u
            });
        }

        foreach (var x in _d.Destructions.Cast<IDestructionShape>())
        {
            events.Add(new SeqEvent
            {
                Y = x.LocationY, Priority = 70, X = x.LocationX,
                Id = x.Id, Kind = "destruction", Destruction = x
            });
        }

        if (_o.EmitNotes)
        {
            foreach (var n in _d.Notes.Cast<INoteShape>())
            {
                events.Add(new SeqEvent
                {
                    Y = n.LocationY, Priority = 40, X = n.LocationX,
                    Id = n.Id, Kind = "note", Note = n
                });
            }
        }

        // Y → 種類 → X → 面積の大きい順 → Id の完全順序
        var ordered = events
            .OrderBy(e => e.Y)
            .ThenBy(e => e.Priority)
            .ThenBy(e => e.X)
            .ThenBy(e => e.Rank)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        // PlantUML では activate/deactivate が「直前のメッセージ行」に束縛される。
        // 同じメッセージに deactivate 済みの参加者を、新しいメッセージを挟まずに
        // 再度 activate すると "Activate/Deactivate already done" になるため、
        // そうなる activate だけ次のメッセージの直後まで先送りする
        var deactivatedSinceMessage = new HashSet<string>(StringComparer.Ordinal);
        var pendingActivates = new List<SeqEvent>();

        foreach (var ev in ordered)
        {
            CloseFragmentsAbove(ev.Y);

            if (ev.Kind == "fragment") OnFragment(ev.Fragment);
            else if (ev.Kind == "operand") OnOperand(ev.Operand, ev.FragmentId);
            else if (ev.Kind == "message")
            {
                OnMessage(ev.Message);
                deactivatedSinceMessage.Clear();
                foreach (var pending in pendingActivates) OnActivate(pending.Execution);
                pendingActivates.Clear();
            }
            else if (ev.Kind == "activate")
            {
                var lifeline = ev.Execution.Lifeline;
                var alias = lifeline != null ? AliasOf(lifeline) : null;
                if (alias != null && deactivatedSinceMessage.Contains(alias))
                    pendingActivates.Add(ev);
                else
                    OnActivate(ev.Execution);
            }
            else if (ev.Kind == "deactivate")
            {
                // メッセージを 1 つも挟めなかったバーは activate/deactivate を対で捨てる
                var pendingIndex = pendingActivates.FindIndex(p => p.Id == ev.Id);
                if (pendingIndex >= 0)
                {
                    pendingActivates.RemoveAt(pendingIndex);
                }
                else if (OnDeactivate(ev.Execution))
                {
                    var lifeline = ev.Execution.Lifeline;
                    if (lifeline != null) deactivatedSinceMessage.Add(AliasOf(lifeline));
                }
            }
            else if (ev.Kind == "use") OnInteractionUse(ev.Use);
            else if (ev.Kind == "destruction") OnDestruction(ev.Destruction);
            else if (ev.Kind == "note") OnNote(ev.Note);
        }
        // 最後までメッセージが来なかった先送り分は出力しない
        // （対応する deactivate は _activeCount のガードで自然にスキップ済み）
    }

    private List<IOperandShape> OperandsOf(IFragmentShape f)
    {
        return f.Operands.Cast<IOperandShape>()
            .OrderBy(o => o.Position)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();
    }

    // Operand.Position は環境によって絶対 Y とフラグメント上端からの相対の両方が
    // ありうるため、フラグメントの範囲に収まるかどうかで判別して絶対 Y に揃える。
    // さらにフラグメント範囲内へクランプし、else 行が自分の枠から漏れないようにする
    private List<double> OperandYs(IFragmentShape f, List<IOperandShape> operands)
    {
        var top = (double)f.LocationY;
        var bottom = top + f.Height;
        var eps = _o.BoundaryEpsilon;

        var absolute = operands.Count > 0
            && operands.All(o => o.Position >= top - eps && o.Position <= bottom + eps);

        var result = new List<double>();
        foreach (var o in operands)
        {
            var y = absolute ? (double)o.Position : top + o.Position;
            if (y < top) y = top;
            if (y > bottom - 2 * eps) y = bottom - 2 * eps;
            result.Add(y);
        }
        return result;
    }

    private double MessageX(IMessageShape m)
    {
        var send = m.SendPort as ISequenceNodeShape;
        if (send != null) return send.LocationX;
        var receive = m.ReceivePort as ISequenceNodeShape;
        return receive != null ? receive.LocationX : 0;
    }

    // 指定 Y に最も近い、指定ライフラインが受信（wantReceiver=true）または
    // 送信するメッセージを許容誤差内で探す
    private IMessageShape NearestMessage(List<IMessageShape> messages, string lifelineId,
                                         double y, bool wantReceiver)
    {
        if (lifelineId == null) return null;

        IMessageShape best = null;
        var bestDistance = _o.ActivationSnapTolerance + 1e-9;
        foreach (var m in messages)
        {
            var end = wantReceiver ? m.Receiver : m.Sender;
            if (end == null || end.Id != lifelineId) continue;

            var distance = Math.Abs(m.SourceY - y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = m;
            }
        }
        return best;
    }

    // 同一ライフライン上で outer が inner を包含するか。
    // スパンがほぼ同一の場合は X の小さい方を外側とみなす
    private bool ContainsExecution(IExecutionSpecificationShape outer, IExecutionSpecificationShape inner)
    {
        if (outer == null || inner == null || outer.Id == inner.Id) return false;
        if (outer.Lifeline == null || inner.Lifeline == null) return false;
        if (outer.Lifeline.Id != inner.Lifeline.Id) return false;

        var eps = _o.BoundaryEpsilon;
        var outerTop = (double)outer.LocationY;
        var outerBottom = outerTop + outer.Length;
        var innerTop = (double)inner.LocationY;
        var innerBottom = innerTop + inner.Length;

        if (outerTop > innerTop + eps || outerBottom < innerBottom - eps) return false;

        var sameSpan = Math.Abs(outerTop - innerTop) <= eps && Math.Abs(outerBottom - innerBottom) <= eps;
        if (sameSpan) return outer.LocationX < inner.LocationX;
        return true;
    }

    // ---------- フラグメント ----------

    private void OnFragment(IFragmentShape f)
    {
        var op = OperatorOf(f);
        string header;
        if (op == "group")
        {
            var text = PlantUmlText.Inline(PlantUmlText.Normalize(f.Text));
            header = text.Length > 0 ? "group " + text : "group";
        }
        else
        {
            var guard = FirstGuardOf(f);
            header = guard.Length > 0 ? op + " " + guard : op;
        }
        Line(header);
        _stack.Add(new OpenFragment { Id = f.Id, Bottom = f.LocationY + f.Height });
    }

    private void OnOperand(IOperandShape o, string fragmentId)
    {
        var guard = PlantUmlText.Inline(PlantUmlText.Normalize(o.Guard));

        // 自分のフラグメントが開いていない位置で else を出すと構文エラーになる
        var index = _stack.FindLastIndex(s => s.Id == fragmentId);
        if (index < 0)
        {
            Line("' [warn] 分岐 '" + guard + "' の位置を特定できなかったため出力しません");
            return;
        }

        // 前の分岐の中で開いたままの内側フラグメントを閉じてから else を出す
        while (_stack.Count - 1 > index)
        {
            _stack.RemoveAt(_stack.Count - 1);
            Line("end");
        }

        // ガードが「else」そのものの分岐は素の else にする（"else else" を避ける）
        var line = guard.Length == 0 || string.Equals(guard, "else", StringComparison.OrdinalIgnoreCase)
                 ? "else" : "else " + guard;
        LineAt(_stack.Count - 1, line);
    }

    private string OperatorOf(IFragmentShape f)
    {
        var text = PlantUmlText.Normalize(f.Text);
        if (text.Length == 0) return "group";

        string op;
        if (_o.OperatorMap.TryGetValue(text, out op)) return op;

        var head = text.Split(new[] { ' ', '[', '(', '\u3000' }, StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault();
        if (!string.IsNullOrEmpty(head) && _o.OperatorMap.TryGetValue(head, out op)) return op;

        return "group";
    }

    private string FirstGuardOf(IFragmentShape f)
    {
        var first = OperandsOf(f).FirstOrDefault();
        if (first == null) return "";
        return PlantUmlText.Inline(PlantUmlText.Normalize(first.Guard));
    }

    private void CloseFragmentsAbove(double y)
    {
        while (_stack.Count > 0 && y > _stack[_stack.Count - 1].Bottom - _o.BoundaryEpsilon)
        {
            _stack.RemoveAt(_stack.Count - 1);
            Line("end");
        }
    }

    private void CloseAllFragments()
    {
        while (_stack.Count > 0)
        {
            _stack.RemoveAt(_stack.Count - 1);
            Line("end");
        }
    }

    // ---------- メッセージ ----------

    private void OnMessage(IMessageShape m)
    {
        var kind = KindOf(m);
        var sender = m.Sender;
        var receiver = m.Receiver;

        if (kind == "create" && receiver != null && _o.UseCreateParticipant && !_declared.Contains(receiver.Id))
        {
            AliasOf(receiver);
            Line("create " + DeclarationOf(receiver));
            _declared.Add(receiver.Id);
        }
        EnsureDeclared(sender);
        EnsureDeclared(receiver);

        var arrow = ArrowOf(kind);
        var label = PlantUmlText.Inline(PlantUmlText.Normalize(m.Text));
        var tail = label.Length > 0 ? " : " + label : "";

        if (sender == null && receiver != null)
            Line("[" + arrow + " " + AliasOf(receiver) + tail);              // 出現メッセージ
        else if (sender != null && receiver == null)
            Line(AliasOf(sender) + " " + arrow + "]" + tail);                // 消失メッセージ
        else if (sender != null && receiver != null)
            Line(AliasOf(sender) + " " + arrow + " " + AliasOf(receiver) + tail);
        else
            Line("' message : " + label);

        if (kind == "destroy" && receiver != null && _destroyed.Add(receiver.Id))
            Line("destroy " + AliasOf(receiver));
    }

    private string KindOf(IMessageShape m)
    {
        var model = m.Model as IMessage;
        if (model == null) return "sync";
        var kind = model.Kind;
        return string.IsNullOrEmpty(kind) ? "sync" : kind.ToLowerInvariant();
    }

    private static string ArrowOf(string kind)
    {
        if (kind == "async") return "->>";
        if (kind == "reply") return "-->";
        return "->";
    }

    // ---------- 実行仕様・破棄 ----------

    private void OnActivate(IExecutionSpecificationShape e)
    {
        var l = e.Lifeline;
        if (l == null) return;
        EnsureDeclared(l);

        var alias = AliasOf(l);
        int count;
        _activeCount.TryGetValue(alias, out count);
        _activeCount[alias] = count + 1;
        Line("activate " + alias);
    }

    // 戻り値: deactivate 行を実際に出力したか
    private bool OnDeactivate(IExecutionSpecificationShape e)
    {
        var l = e.Lifeline;
        if (l == null) return false;

        var alias = AliasOf(l);
        int count;
        if (!_activeCount.TryGetValue(alias, out count) || count <= 0) return false;
        _activeCount[alias] = count - 1;
        Line("deactivate " + alias);
        return true;
    }

    private void DeactivateAll()
    {
        foreach (var entry in _activeCount.OrderBy(k => k.Key, StringComparer.Ordinal).ToList())
        {
            for (var i = 0; i < entry.Value; i++) Line("deactivate " + entry.Key);
            _activeCount[entry.Key] = 0;
        }
    }

    private void OnDestruction(IDestructionShape x)
    {
        var l = x.Lifeline;
        if (l == null) return;
        if (_destroyed.Add(l.Id)) Line("destroy " + AliasOf(l));
    }

    // ---------- 相互作用の利用・ノート ----------

    private void OnInteractionUse(IInteractionUseShape u)
    {
        var text = PlantUmlText.Inline(PlantUmlText.Normalize(u.Text));
        if (text.Length == 0) text = "ref";

        var aliases = u.Lifelines.Cast<ILifelineShape>()
            .OrderBy(l => l.LocationX)
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .Select(l => AliasOf(l))
            .Where(a => a != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (aliases.Count == 0)
        {
            var nearest = NearestAlias(u.LocationX + u.Width / 2.0);
            if (nearest != null) aliases.Add(nearest);
        }
        if (aliases.Count == 0)
        {
            Line("' ref : " + text);
            return;
        }
        Line("ref over " + string.Join(", ", aliases) + " : " + text);
    }

    private void OnNote(INoteShape n)
    {
        if (PlantUmlText.Normalize(n.Text).Length == 0) return;

        var target = AnchoredLifelineOf(n);
        var alias = target != null ? AliasOf(target) : NearestAlias(n.LocationX + n.Width / 2.0);
        if (alias == null)
        {
            Line("' note : " + PlantUmlText.Inline(PlantUmlText.Normalize(n.Text)));
            return;
        }

        Line("note over " + alias);
        foreach (var raw in n.Text.Replace("\r\n", "\n").Split('\n'))
            LineAt(_stack.Count + 1, raw.TrimEnd());
        Line("end note");
    }

    private ILifelineShape AnchoredLifelineOf(INoteShape n)
    {
        foreach (var anchor in n.NoteAnchors.Cast<INoteAnchorShape>()
                                            .OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var other = IsSame(anchor.Source, n) ? anchor.Target : anchor.Source;

            var lifeline = other as ILifelineShape;
            if (lifeline != null) return lifeline;

            var execution = other as IExecutionSpecificationShape;
            if (execution != null && execution.Lifeline != null) return execution.Lifeline;

            var message = other as IMessageShape;
            if (message != null) return message.Sender ?? message.Receiver;
        }
        return null;
    }

    private static bool IsSame(ISequenceShape a, ISequenceShape b)
    {
        return a != null && b != null && a.Id == b.Id;
    }

    private string NearestAlias(double x)
    {
        ILifelineShape best = null;
        var bestDistance = double.MaxValue;
        foreach (var l in _lifelines)
        {
            var distance = Math.Abs(l.LocationX + l.Width / 2.0 - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = l;
            }
        }
        return best == null ? null : _alias[best.Id];
    }

    // ---------- 出力 ----------

    private void Line(string text)
    {
        LineAt(_stack.Count, text);
    }

    private void LineAt(int depth, string text)
    {
        if (!string.IsNullOrEmpty(text))
            for (var i = 0; i < depth; i++) _sb.Append(_o.IndentUnit);
        _sb.Append(text);
        _sb.Append(_o.NewLine);
    }

    private void Blank()
    {
        _sb.Append(_o.NewLine);
    }
}

// ============================================================
//  実行部：対象の決定、ダイアログ、ファイル出力、ログ
// ============================================================

// ------------------------------------------------------------
//  出力対象（図とその所有モデルのペア）
// ------------------------------------------------------------
public class DiagramEntry
{
    public IModel Owner;
    public ISequenceDiagram Diagram;

    public string OwnerPath
    {
        get
        {
            if (Owner == null) return "";
            var path = Owner.ModelPath;
            return string.IsNullOrEmpty(path) ? Owner.Name : path;
        }
    }

    public string Name
    {
        get
        {
            if (Diagram.Model != null && !string.IsNullOrEmpty(Diagram.Model.Name)) return Diagram.Model.Name;
            return string.IsNullOrEmpty(Diagram.ViewDefinitionName) ? "Sequence" : Diagram.ViewDefinitionName;
        }
    }

    public string Label
    {
        get { return OwnerPath + " / " + Name; }
    }
}

// ------------------------------------------------------------
//  実行時の設定
// ------------------------------------------------------------
public class ExportSettings
{
    public bool SaveToFile = true;          // false なら出力ウィンドウへの表示のみ
    public bool OneFilePerDiagram = true;   // false なら 1 ファイルに連結
    public bool WriteIndexFile = true;      // 出力フォルダに _index.md を作る
    public bool SkipEmptyDiagram = true;    // ライフラインが 0 本の図はスキップ
    public bool Confirm = true;             // 件数を確認ダイアログで確認する
}

// ------------------------------------------------------------
//  実行本体
// ------------------------------------------------------------
public class ExportRunner
{
    public const string Category = "PlantUML";

    // ==================== 1 枚を出力 ====================

    public static void ExportCurrent(IApplication app, PlantUmlOptions options, ExportSettings settings)
    {
        options = options ?? new PlantUmlOptions();
        settings = settings ?? new ExportSettings();

        var ui = app.Window.UI;

        var editor = app.Workspace.CurrentEditor;
        if (editor == null)
        {
            ui.ShowInformationDialog(
                "エディタが開かれていません。シーケンス図を開いてから実行してください。", Category);
            return;
        }

        var diagram = editor as ISequenceDiagram;
        if (diagram == null)
        {
            ui.ShowInformationDialog(
                "アクティブなエディタはシーケンス図ではありません。（EditorType = "
                + editor.EditorType + "）", Category);
            return;
        }

        var exporter = new SequencePlantUmlExporter(diagram, options);
        var uml = exporter.Export();

        ShowPane(app);
        foreach (var line in uml.Replace("\r\n", "\n").Split('\n'))
            app.Output.WriteLine(Category, line);

        if (!settings.SaveToFile) return;

        var baseName = PlantUmlText.SafeFileName(exporter.DiagramName());
        if (baseName.Length == 0) baseName = "sequence";

        var path = ui.ShowSaveFileDialog(
            "PlantUML ファイルの保存",
            "PlantUML (*.puml)|*.puml|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            baseName + ".puml");
        if (string.IsNullOrEmpty(path)) return;

        SaveText(path, uml);
        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "[saved] " + path);
    }

    // ==================== 配下をまとめて出力 ====================

    public static void ExportAll(IApplication app, IContext context,
                                 PlantUmlOptions options, ExportSettings settings)
    {
        options = options ?? new PlantUmlOptions();
        settings = settings ?? new ExportSettings();

        var ui = app.Window.UI;

        // 未表示エディタの詳細も取得できるようにする（バッチでは必須）
        context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

        var root = ResolveRoot(app);
        if (root == null)
        {
            ui.ShowInformationDialog("プロジェクトが開かれていません。", Category);
            return;
        }

        var skipCount = 0;
        var targets = Collect(root, settings.SkipEmptyDiagram, ref skipCount);

        if (targets.Count == 0)
        {
            ui.ShowInformationDialog(
                "「" + root.Name + "」配下に出力対象のシーケンス図が見つかりませんでした。"
                + (skipCount > 0 ? "（空の図 " + skipCount + " 件をスキップ）" : ""), Category);
            return;
        }

        if (settings.Confirm)
        {
            var message = "「" + root.Name + "」配下のシーケンス図 " + targets.Count
                        + " 件を PlantUML に変換します。"
                        + (skipCount > 0 ? "\n（ライフラインなしの " + skipCount + " 件はスキップ）" : "")
                        + "\n\n続行しますか？";
            if (!ui.ShowConfirmDialog(message, Category)) return;
        }

        string folder = null;
        string singlePath = null;

        if (settings.OneFilePerDiagram)
        {
            folder = ui.ShowSelectFolderDialog("PlantUML の出力先フォルダを選択してください");
            if (string.IsNullOrEmpty(folder)) return;
        }
        else
        {
            var rootName = PlantUmlText.SafeFileName(root.Name);
            if (rootName.Length == 0) rootName = "sequences";
            singlePath = ui.ShowSaveFileDialog(
                "PlantUML ファイルの保存",
                "PlantUML (*.puml)|*.puml|すべてのファイル (*.*)|*.*",
                rootName + ".puml");
            if (string.IsNullOrEmpty(singlePath)) return;
        }

        // ファイル名は出現順に依存しない形で先に確定させる
        var fileNames = BuildFileNames(targets);

        ShowPane(app);
        app.Output.WriteLine(Category, "=== PlantUML Export : " + root.Name + " ===");
        app.Output.WriteLine(Category, "対象 " + targets.Count + " 件");
        app.Output.WriteLine(Category, "");

        var joined = new StringBuilder();
        var indexRows = new List<string>();
        var okCount = 0;
        var errorCount = 0;

        for (var i = 0; i < targets.Count; i++)
        {
            var entry = targets[i];
            try
            {
                var uml = new SequencePlantUmlExporter(entry.Diagram, options).Export();

                if (settings.OneFilePerDiagram)
                {
                    var fileName = fileNames[entry.Diagram.Id];
                    SaveText(System.IO.Path.Combine(folder, fileName), uml);
                    indexRows.Add("| " + (i + 1) + " | " + entry.OwnerPath + " | " + entry.Name
                                  + " | [" + fileName + "](" + fileName + ") |");
                }
                else
                {
                    joined.Append("' ======== ").Append(entry.Label).Append(" ========").Append(options.NewLine);
                    joined.Append(uml).Append(options.NewLine);
                    indexRows.Add("| " + (i + 1) + " | " + entry.OwnerPath + " | " + entry.Name
                                  + " | (連結出力) |");
                }

                okCount++;
                app.Output.WriteLine(Category, "[ok]    " + entry.Label);
            }
            catch (Exception ex)
            {
                errorCount++;
                app.Output.WriteLine(Category, "[error] " + entry.Label + " : " + ex.Message);
            }
        }

        if (!settings.OneFilePerDiagram && joined.Length > 0)
        {
            SaveText(singlePath, joined.ToString());
            app.Output.WriteLine(Category, "");
            app.Output.WriteLine(Category, "[saved] " + singlePath);
            folder = System.IO.Path.GetDirectoryName(singlePath);
        }

        if (settings.WriteIndexFile && !string.IsNullOrEmpty(folder) && indexRows.Count > 0)
        {
            var indexPath = System.IO.Path.Combine(folder, "_index.md");
            SaveText(indexPath, BuildIndex(root, okCount, targets.Count, indexRows, options));
            app.Output.WriteLine(Category, "[saved] " + indexPath);
        }

        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "=== 完了 : 成功 " + okCount
                             + " / スキップ " + skipCount + " / エラー " + errorCount + " ===");

        ui.ShowInformationDialog(
            "PlantUML 出力が完了しました。\n\n"
            + "成功: " + okCount + " 件\n"
            + "スキップ: " + skipCount + " 件\n"
            + "エラー: " + errorCount + " 件\n\n"
            + "出力先: " + (folder ?? singlePath), Category);
    }

    // ==================== 対象の決定 ====================

    // ナビゲータの選択 → CurrentModel → プロジェクト の順に起点を決める
    public static IModel ResolveRoot(IApplication app)
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

    public static List<DiagramEntry> Collect(IModel root, bool skipEmpty, ref int skipCount)
    {
        var entries = new List<DiagramEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var models = new List<IModel>();
        models.Add(root);
        models.AddRange(root.GetAllChildren().Cast<IModel>());

        foreach (var model in models)
        {
            if (model == null || model.IsDeleted || model.IsProxy) continue;

            foreach (var editor in model.GetEditors())
            {
                if (editor.EditorType != "SequenceDiagram") continue;

                var diagram = editor as ISequenceDiagram;
                if (diagram == null) continue;
                if (!seen.Add(diagram.Id)) continue;

                if (skipEmpty && !diagram.Lifelines.Cast<ILifelineShape>().Any())
                {
                    skipCount++;
                    continue;
                }
                entries.Add(new DiagramEntry { Owner = model, Diagram = diagram });
            }
        }

        return entries
            .OrderBy(e => e.OwnerPath, StringComparer.Ordinal)
            .ThenBy(e => e.Diagram.ViewDefinitionName, StringComparer.Ordinal)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.Diagram.Id, StringComparer.Ordinal)
            .ToList();
    }

    // ==================== ファイル名 ====================

    // 重複した基本名はグループ全員にハッシュを付ける（追加・削除で他の名前が動かない）
    public static Dictionary<string, string> BuildFileNames(List<DiagramEntry> entries)
    {
        var baseNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            // ファイル名は図名のみ。モデルパスとの対応は _index.md で追跡する
            var baseName = PlantUmlText.SafeFileName(entry.Name);
            if (baseName.Length == 0) baseName = "sequence";
            if (baseName.Length > 100) baseName = baseName.Substring(0, 100);
            baseNames[entry.Diagram.Id] = baseName;
        }

        var duplicated = new HashSet<string>(
            baseNames.Values
                     .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1)
                     .Select(g => g.Key),
            StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in baseNames)
        {
            result[pair.Key] = duplicated.Contains(pair.Value)
                ? pair.Value + "_" + PlantUmlText.ShortHash(pair.Key) + ".puml"
                : pair.Value + ".puml";
        }
        return result;
    }

    // ==================== 出力ユーティリティ ====================

    private static string BuildIndex(IModel root, int okCount, int total,
                                     List<string> rows, PlantUmlOptions options)
    {
        var sb = new StringBuilder();
        sb.Append("# PlantUML Export : ").Append(root.Name).Append(options.NewLine);
        sb.Append(options.NewLine);
        sb.Append("- 起点モデル: ").Append(root.Name).Append(options.NewLine);
        sb.Append("- 出力件数: ").Append(okCount).Append(" / ").Append(total).Append(options.NewLine);
        if (options.EmitTimestamp)
            sb.Append("- 出力日時: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(options.NewLine);
        sb.Append(options.NewLine);
        sb.Append("| # | モデルパス | 図名 | ファイル |").Append(options.NewLine);
        sb.Append("|---|---|---|---|").Append(options.NewLine);
        foreach (var row in rows) sb.Append(row).Append(options.NewLine);
        return sb.ToString();
    }

    private static void ShowPane(IApplication app)
    {
        OutputPane.Show(app, Category);
    }

    private static void SaveText(string path, string text)
    {
        System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}

// ============================================================
//  Part 7 / クラス図の PlantUML 出力
//
//    クラス図は EditorType が "ERDiagram"（プロジェクトによっては
//    "TreeDiagram"）のエディタで、ISequenceDiagram のような型付き
//    アクセサが無い。IDiagram が持つのは Nodes / Connectors だけなので、
//    クラス・属性・操作・関連の意味はすべてモデル側から取る。
//
//    メタクラス名とフィールド名はプロファイル依存で、API リファレンス
//    には載っていない。そのため ClassPlantUmlOptions の対応表で解釈し、
//    未登録のものは既定の扱いにして警告を出す（黙って落とさない）。
//    実機の値は「クラス図調査」（ClassProbe）で確認する。
// ============================================================

// ------------------------------------------------------------
//  クラス図の出力オプション
// ------------------------------------------------------------
public class ClassPlantUmlOptions
{
    public bool IncludeTitle = true;          // 図名を title として出力する
    public string Theme = null;               // !theme <name> を出力する
    public bool HideEmptyMembers = true;      // hide empty members を出力する
    public bool EmitMembers = true;           // 属性・操作を出力する
    public bool EmitStereotypes = true;       // <<...>> を出力する
    public bool EmitUnknownStereotype = true; // 対応表に無いクラス名もそのまま <<...>> に出す
    public bool EmitPackages = true;          // オーナーを package でまとめる
    public bool EmitEmbedded = false;         // 所有関連も線にする（属性の親子まで線になるため既定 false）
    public bool EmitRoleNames = true;         // リンクのラベルにフィールド名を出す
    public bool EmitMultiplicity = true;      // 多重度を出す
    public bool MergeBidirectional = true;    // 双方向の関連を 1 本にまとめる
    public bool EmitTimestamp = false;        // 出力日時を埋め込む（差分安定化のため既定 false）
    public string IndentUnit = "  ";          // 入れ子のインデント
    public string NewLine = "\n";             // 改行は LF 固定
    public string DefaultLink = "-->";        // 種別が判別できない参照関連
    public string EmbeddedLink = "*--";       // 所有関連
    public string FallbackLink = "--";        // コネクタはあるがモデル側で辿れないとき

    // メタクラス名 → PlantUML のキーワード
    public Dictionary<string, string> KeywordMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Class", "class" }, { "クラス", "class" },
        { "Interface", "interface" }, { "インタフェース", "interface" }, { "インターフェース", "interface" },
        { "Enumeration", "enum" }, { "Enum", "enum" }, { "列挙", "enum" }, { "列挙型", "enum" },
        { "AbstractClass", "abstract class" }, { "抽象クラス", "abstract class" },
        { "Entity", "entity" }, { "エンティティ", "entity" },
        { "Struct", "struct" }, { "構造体", "struct" },
        { "Package", "package" }, { "パッケージ", "package" },
        { "Component", "component" }, { "コンポーネント", "component" },
        { "Block", "class" }, { "ブロック", "class" },
    };

    // メタクラス名 → ステレオタイプ表記（キーワードで表せないものだけ）
    public Dictionary<string, string> StereotypeMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Class", "" }, { "クラス", "" },   // 空文字はステレオタイプなし
    };

    // 子モデルのメタクラス名 → "attribute" | "operation" | "literal" | "skip"
    public Dictionary<string, string> MemberKindMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Attribute", "attribute" }, { "属性", "attribute" },
        { "Property", "attribute" }, { "プロパティ", "attribute" },
        { "Field", "attribute" }, { "フィールド", "attribute" },
        { "Operation", "operation" }, { "操作", "operation" },
        { "Method", "operation" }, { "メソッド", "operation" },
        { "Function", "operation" }, { "関数", "operation" },
        { "EnumLiteral", "literal" }, { "Literal", "literal" }, { "列挙リテラル", "literal" },
        { "Parameter", "skip" }, { "引数", "skip" }, { "パラメータ", "skip" },
        { "Port", "skip" }, { "ポート", "skip" },
    };

    // 参照フィールド名 → PlantUML の矢印
    public Dictionary<string, string> LinkMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Generalization", "--|>" }, { "SuperClass", "--|>" }, { "Super", "--|>" },
        { "Inheritance", "--|>" }, { "Extends", "--|>" }, { "Parent", "--|>" },
        { "汎化", "--|>" }, { "継承", "--|>" }, { "親クラス", "--|>" }, { "スーパークラス", "--|>" },

        { "Realization", "..|>" }, { "Implements", "..|>" }, { "InterfaceRealization", "..|>" },
        { "実現", "..|>" }, { "実装", "..|>" },

        { "Dependency", "..>" }, { "Depends", "..>" }, { "Use", "..>" }, { "Uses", "..>" },
        { "依存", "..>" }, { "利用", "..>" },

        { "Aggregation", "o--" }, { "集約", "o--" },
        { "Composition", "*--" }, { "合成", "*--" }, { "コンポジション", "*--" },

        { "Association", "-->" }, { "関連", "-->" },
    };

    // 可視性の値 → 記号
    public Dictionary<string, string> VisibilityMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // 比較が OrdinalIgnoreCase のため大文字小文字違いを重複登録しないこと
        // （"public" と "Public" を両方書くと初期化時に例外になる）
        { "public", "+" }, { "公開", "+" }, { "+", "+" },
        { "private", "-" }, { "非公開", "-" }, { "-", "-" },
        { "protected", "#" }, { "限定公開", "#" }, { "#", "#" },
        { "package", "~" }, { "internal", "~" }, { "パッケージ", "~" }, { "~", "~" },
    };

    // 値／参照フィールドを名前で探すときの候補
    public List<string> TypeFieldNames =
        new List<string> { "Type", "DataType", "AttributeType", "PropertyType", "型", "データ型", "属性型" };
    public List<string> ReturnTypeFieldNames =
        new List<string> { "ReturnType", "Return", "ResultType", "戻り値", "戻り値型", "返り値" };
    public List<string> MultiplicityFieldNames =
        new List<string> { "Multiplicity", "Cardinality", "多重度" };
    public List<string> VisibilityFieldNames =
        new List<string> { "Visibility", "Accessibility", "AccessModifier", "可視性", "公開範囲" };
    public List<string> DefaultValueFieldNames =
        new List<string> { "DefaultValue", "Default", "InitialValue", "既定値", "初期値" };
    public List<string> ParameterFieldNames =
        new List<string> { "Parameters", "Parameter", "Arguments", "引数", "パラメータ" };
    public List<string> StaticFieldNames =
        new List<string> { "IsStatic", "Static", "静的", "クラスメンバ" };
    public List<string> AbstractFieldNames =
        new List<string> { "IsAbstract", "Abstract", "抽象" };
}

// ------------------------------------------------------------
//  中間表現：図上の 1 ノード
// ------------------------------------------------------------
public class ClassNodeInfo
{
    public IModel Model;
    public INode Node;
    public string ModelId = "";
    public string Name = "";
    public string Alias = "";
    public string Keyword = "class";
    public string Stereotype = "";
    public List<string> Attributes = new List<string>();
    public List<string> Operations = new List<string>();
    public List<string> PackagePath = new List<string>();

    // 図上ノード同士の所有関係（package の入れ子出力に使う）
    public ClassNodeInfo Parent;
    public List<ClassNodeInfo> Children = new List<ClassNodeInfo>();

    // package / component は PlantUML 上、中に書けるのが要素宣言だけ
    //（属性のようなテキスト行は構文エラーになる）
    public bool IsContainer
    {
        get
        {
            return string.Equals(Keyword, "package", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Keyword, "component", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string PackageKey
    {
        get { return string.Join("/", PackagePath.ToArray()); }
    }
}

// ------------------------------------------------------------
//  中間表現：1 本のリンク
// ------------------------------------------------------------
public class ClassLink
{
    public string FromId = "";
    public string ToId = "";
    public string FromAlias = "";
    public string ToAlias = "";
    public string Arrow = "-->";
    public string Label = "";
    public string FromMultiplicity = "";
    public string ToMultiplicity = "";
    public string FieldName = "";

    // 出力順を決めるキー（同じ図なら必ず同じ順になるようにする）
    public string SortKey
    {
        get { return FromAlias + "" + ToAlias + "" + Arrow + "" + FieldName + "" + Label; }
    }

    public string PairKey
    {
        get
        {
            return string.CompareOrdinal(FromId, ToId) <= 0
                ? FromId + "" + ToId
                : ToId + "" + FromId;
        }
    }
}

// ------------------------------------------------------------
//  収集：IDiagram からノードとリンクを組み立てる
// ------------------------------------------------------------
public class ClassDiagramCollector
{
    private readonly IDiagram _d;
    private readonly ClassPlantUmlOptions _o;

    public readonly List<ClassNodeInfo> Nodes = new List<ClassNodeInfo>();
    public readonly List<ClassLink> Links = new List<ClassLink>();
    public readonly List<string> Warnings = new List<string>();

    private readonly Dictionary<string, ClassNodeInfo> _byModelId =
        new Dictionary<string, ClassNodeInfo>(StringComparer.Ordinal);
    private readonly HashSet<string> _usedAlias = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownMember = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unknownLink = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ClassDiagramCollector(IDiagram diagram, ClassPlantUmlOptions options)
    {
        _d = diagram;
        _o = options ?? new ClassPlantUmlOptions();
    }

    public void Collect()
    {
        CollectNodes();
        CollectLinksFromFields();
        CollectLinksFromConnectors();
        MergeBidirectional();
        SortLinks();
    }

    // ---------- ノード ----------

    private void CollectNodes()
    {
        var shapes = new List<INode>();
        try
        {
            foreach (var s in _d.Nodes)
            {
                var node = s as INode;
                if (node != null) shapes.Add(node);
            }
        }
        catch (Exception ex) { Warnings.Add("ノードの取得に失敗しました : " + ex.Message); }

        var ordered = shapes
            .OrderBy(n => SafeY(n))
            .ThenBy(n => SafeX(n))
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

        // パス1: まず全ノードを登録する（親子解決とメンバ収集で全ノードの索引が要る）
        foreach (var node in ordered)
        {
            var model = MetaMap.ModelOf(node);
            if (model == null || model.IsDeleted) continue;
            if (_byModelId.ContainsKey(model.Id)) continue;   // 同じモデルの重複シェイプ

            var info = new ClassNodeInfo
            {
                Model = model,
                Node = node,
                ModelId = model.Id,
                Name = NameOf(model),
            };
            info.Alias = MakeAlias(info.Name, model.Id);
            info.Keyword = KeywordOf(model);
            info.Stereotype = StereotypeOf(model, info.Keyword);

            Nodes.Add(info);
            _byModelId[model.Id] = info;
        }

        // パス2: 親子解決・メンバ収集・パッケージパス
        foreach (var info in Nodes)
        {
            // 最も近い「図上ノードでもあるオーナー」を親にする
            var owner = info.Model.Owner;
            var guard = 0;
            while (owner != null && guard++ < 32)
            {
                ClassNodeInfo parent;
                if (_byModelId.TryGetValue(owner.Id, out parent))
                {
                    info.Parent = parent;
                    parent.Children.Add(info);
                    break;
                }
                owner = owner.Owner;
            }

            // package/component の中に書けるのは要素宣言だけなのでメンバは集めない
            if (_o.EmitMembers && !info.IsContainer) CollectMembers(info);

            // パッケージパスは最上位ノードだけに付ける（子は親の中に入れ子で出す）
            if (_o.EmitPackages && info.Parent == null) info.PackagePath = PackagePathOf(info.Model);
        }

        if (Nodes.Count == 0) Warnings.Add("図上にモデルと対応するノードがありません。");
    }

    private static double SafeY(INode n)
    {
        try { return n.LocationY; } catch (Exception) { return 0; }
    }

    private static double SafeX(INode n)
    {
        try { return n.LocationX; } catch (Exception) { return 0; }
    }

    private static string NameOf(IModel m)
    {
        var name = PlantUmlText.Normalize(m.Name);
        if (name.Length > 0) return name;
        return "(unnamed)";
    }

    private string MakeAlias(string label, string modelId)
    {
        var alias = PlantUmlText.AsciiAlias(label);
        if (alias.Length == 0) alias = "C" + PlantUmlText.ShortHash(modelId);
        if (!_usedAlias.Add(alias))
        {
            alias = alias + "_" + PlantUmlText.ShortHash(modelId);
            _usedAlias.Add(alias);
        }
        return alias;
    }

    private string KeywordOf(IModel m)
    {
        string keyword;
        if (!string.IsNullOrEmpty(m.ClassName) && _o.KeywordMap.TryGetValue(m.ClassName, out keyword))
            return keyword;

        // 親クラスをたどる（プロファイルが Class を継承した派生クラスを使っている場合）
        var cls = m.Metaclass;
        if (cls != null)
        {
            try
            {
                foreach (var s in cls.GetAllSuperClasses().Cast<IClass>())
                    if (_o.KeywordMap.TryGetValue(s.Name, out keyword)) return keyword;
            }
            catch (Exception) { }
        }

        // 抽象フラグが立っていれば abstract class
        if (BoolField(m, _o.AbstractFieldNames)) return "abstract class";
        return "class";
    }

    private string StereotypeOf(IModel m, string keyword)
    {
        if (!_o.EmitStereotypes) return "";

        string stereotype;
        if (!string.IsNullOrEmpty(m.ClassName) && _o.StereotypeMap.TryGetValue(m.ClassName, out stereotype))
            return PlantUmlText.Normalize(stereotype);

        // キーワードで既に表現できているものは重ねて出さない
        if (!string.Equals(keyword, "class", StringComparison.OrdinalIgnoreCase)) return "";
        if (!_o.EmitUnknownStereotype) return "";
        if (string.IsNullOrEmpty(m.ClassName)) return "";
        return PlantUmlText.Normalize(m.ClassName);
    }

    private List<string> PackagePathOf(IModel m)
    {
        var path = new List<string>();
        var owner = m.Owner;
        var guard = 0;
        while (owner != null && guard++ < 32)
        {
            // オーナー自身が図に載っているならパッケージにしない
            if (_byModelId.ContainsKey(owner.Id)) break;
            var name = PlantUmlText.Normalize(owner.Name);
            if (name.Length > 0) path.Insert(0, name);
            owner = owner.Owner;
        }
        return path;
    }

    // ---------- 属性・操作 ----------

    private void CollectMembers(ClassNodeInfo info)
    {
        IEnumerable<IModel> children;
        try { children = info.Model.GetChildren().Cast<IModel>().ToList(); }
        catch (Exception ex)
        {
            Warnings.Add(info.Name + " : 子モデルの取得に失敗しました : " + ex.Message);
            return;
        }

        foreach (var child in children)
        {
            if (child == null || child.IsDeleted) continue;
            // それ自体が図上のノードである子は、独立した要素として出すのでメンバにしない
            if (_byModelId.ContainsKey(child.Id)) continue;

            var kind = MemberKindOf(child);
            if (kind == "skip") continue;
            if (kind == "operation") info.Operations.Add(RenderOperation(child));
            else if (kind == "literal") info.Attributes.Add(PlantUmlText.Inline(NameOf(child)));
            else info.Attributes.Add(RenderAttribute(child));
        }
    }

    private string MemberKindOf(IModel child)
    {
        string kind;
        if (!string.IsNullOrEmpty(child.ClassName) && _o.MemberKindMap.TryGetValue(child.ClassName, out kind))
            return kind;

        var cls = child.Metaclass;
        if (cls != null)
        {
            try
            {
                foreach (var s in cls.GetAllSuperClasses().Cast<IClass>())
                    if (_o.MemberKindMap.TryGetValue(s.Name, out kind)) return kind;
            }
            catch (Exception) { }
        }

        // 対応表に無いものは属性として出し、1 クラス名につき 1 回だけ警告する
        if (!string.IsNullOrEmpty(child.ClassName) && _unknownMember.Add(child.ClassName))
            Warnings.Add("メンバの種別が不明なため属性として出力しました : ClassName='"
                         + child.ClassName + "'（ClassPlantUmlOptions.MemberKindMap に追加してください）");
        return "attribute";
    }

    private string RenderAttribute(IModel m)
    {
        var sb = new StringBuilder();

        var visibility = VisibilityOf(m);
        if (visibility.Length > 0) sb.Append(visibility);
        if (BoolField(m, _o.StaticFieldNames)) sb.Append("{static} ");

        sb.Append(PlantUmlText.Inline(NameOf(m)));

        var type = TextOf(m, _o.TypeFieldNames);
        if (type.Length > 0) sb.Append(" : ").Append(PlantUmlText.Inline(type));

        var mult = TextOf(m, _o.MultiplicityFieldNames);
        if (_o.EmitMultiplicity && mult.Length > 0) sb.Append(" [").Append(PlantUmlText.Inline(mult)).Append("]");

        var def = TextOf(m, _o.DefaultValueFieldNames);
        if (def.Length > 0) sb.Append(" = ").Append(PlantUmlText.Inline(def));

        return sb.ToString();
    }

    private string RenderOperation(IModel m)
    {
        var sb = new StringBuilder();

        var visibility = VisibilityOf(m);
        if (visibility.Length > 0) sb.Append(visibility);
        if (BoolField(m, _o.StaticFieldNames)) sb.Append("{static} ");
        if (BoolField(m, _o.AbstractFieldNames)) sb.Append("{abstract} ");

        sb.Append(PlantUmlText.Inline(NameOf(m))).Append("(");
        sb.Append(PlantUmlText.Inline(ParametersOf(m)));
        sb.Append(")");

        var ret = TextOf(m, _o.ReturnTypeFieldNames);
        if (ret.Length > 0) sb.Append(" : ").Append(PlantUmlText.Inline(ret));

        return sb.ToString();
    }

    // 引数は「値フィールドの文字列」と「子モデルの並び」の両方に対応する
    private string ParametersOf(IModel m)
    {
        var text = TextOf(m, _o.ParameterFieldNames);
        if (text.Length > 0) return text;

        var parts = new List<string>();
        try
        {
            foreach (var child in m.GetChildren().Cast<IModel>())
            {
                if (child == null || child.IsDeleted) continue;
                var name = PlantUmlText.Normalize(child.Name);
                var type = TextOf(child, _o.TypeFieldNames);
                if (name.Length == 0 && type.Length == 0) continue;
                parts.Add(type.Length > 0 ? name + " : " + type : name);
            }
        }
        catch (Exception) { }
        return string.Join(", ", parts.ToArray());
    }

    private string VisibilityOf(IModel m)
    {
        var raw = TextOf(m, _o.VisibilityFieldNames);
        if (raw.Length == 0) return "";
        string symbol;
        if (_o.VisibilityMap.TryGetValue(raw, out symbol)) return symbol + " ";
        return "";
    }

    // 名前候補のフィールドを順に探す。値フィールドは文字列、参照/所有フィールドは
    // 参照先の名前を返す。見つからなければ空文字
    // （Part 8 の状態遷移図出力からも使うため public）
    public static string TextOf(IModel m, List<string> candidates)
    {
        var cls = m.Metaclass;
        if (cls == null) return "";

        List<IField> fields;
        try { fields = cls.GetFields().Cast<IField>().ToList(); }
        catch (Exception) { return ""; }

        foreach (var candidate in candidates)
        {
            foreach (var f in fields)
            {
                if (!string.Equals(f.Name, candidate, StringComparison.OrdinalIgnoreCase)) continue;

                if (f.IsEmbedded || f.IsReference)
                {
                    try
                    {
                        var names = new List<string>();
                        foreach (var v in m.GetFieldValues(f.Name))
                        {
                            var target = v as IModel;
                            if (target == null) continue;
                            var name = PlantUmlText.Normalize(target.Name);
                            if (name.Length > 0) names.Add(name);
                        }
                        if (names.Count > 0) return string.Join(", ", names.ToArray());
                    }
                    catch (Exception) { }

                    // 所有フィールドでも値が文字列のことがある（例: DeSIDE の State.Entry は
                    // kind=所有 type=String）。モデルとして読めなければ文字列として読む
                    try
                    {
                        var text = PlantUmlText.Normalize(m.GetFieldString(f.Name));
                        if (text.Length > 0) return text;
                    }
                    catch (Exception) { }
                }
                else
                {
                    try
                    {
                        var value = PlantUmlText.Normalize(m.GetFieldString(f.Name));
                        if (value.Length > 0) return value;
                    }
                    catch (Exception) { }
                }
            }
        }
        return "";
    }

    public static bool BoolField(IModel m, List<string> candidates)
    {
        var value = TextOf(m, candidates);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "True", StringComparison.Ordinal)
            || value == "1";
    }

    // ---------- リンク：モデルのフィールドから ----------

    // 図に載っているモデルどうしの参照関連を走査する。
    // 方向・フィールド名・多重度がフィールド定義から確実に取れるので、これを主とする
    private void CollectLinksFromFields()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var info in Nodes)
        {
            var cls = info.Model.Metaclass;
            if (cls == null) continue;

            List<IField> fields;
            try { fields = cls.GetFields().Cast<IField>().ToList(); }
            catch (Exception) { continue; }

            foreach (var f in fields)
            {
                if (f.IsReference) { }
                else if (f.IsEmbedded && _o.EmitEmbedded) { }
                else continue;

                List<IModel> targets;
                try
                {
                    targets = new List<IModel>();
                    foreach (var v in info.Model.GetFieldValues(f.Name))
                    {
                        var target = v as IModel;
                        if (target != null && !target.IsDeleted) targets.Add(target);
                    }
                }
                catch (Exception) { continue; }

                foreach (var target in targets)
                {
                    ClassNodeInfo other;
                    if (!_byModelId.TryGetValue(target.Id, out other)) continue;   // 図に載っていない相手は出さない
                    if (other.ModelId == info.ModelId) continue;                   // 自己参照は線にしない

                    var key = info.ModelId + "" + f.Name + "" + other.ModelId;
                    if (!seen.Add(key)) continue;

                    Links.Add(new ClassLink
                    {
                        FromId = info.ModelId,
                        ToId = other.ModelId,
                        FromAlias = info.Alias,
                        ToAlias = other.Alias,
                        Arrow = ArrowOf(f),
                        FieldName = f.Name,
                        // 自動生成の匿名フィールド名（____anonymous____... 等）はラベルに出さない
                        Label = _o.EmitRoleNames && !PlantUmlText.IsSystemName(f.Name)
                                ? PlantUmlText.Inline(f.Name) : "",
                        ToMultiplicity = _o.EmitMultiplicity ? Multiplicity(f) : "",
                    });
                }
            }
        }
    }

    private string ArrowOf(IField f)
    {
        string arrow;
        if (!string.IsNullOrEmpty(f.Name) && _o.LinkMap.TryGetValue(f.Name, out arrow)) return arrow;

        if (f.IsEmbedded) return _o.EmbeddedLink;

        // 自動生成の匿名フィールドは対応表に載りようがないため警告しない
        if (!string.IsNullOrEmpty(f.Name) && !PlantUmlText.IsSystemName(f.Name) && _unknownLink.Add(f.Name))
            Warnings.Add("関連の種別が不明なため既定の矢印で出力しました : フィールド名='"
                         + f.Name + "'（ClassPlantUmlOptions.LinkMap に追加してください）");
        return _o.DefaultLink;
    }

    private static string Multiplicity(IField f)
    {
        int lower, upper;
        try { lower = f.LowerBound; upper = f.UpperBound; }
        catch (Exception) { return ""; }

        var upperText = upper < 0 ? "*" : upper.ToString(CultureInfo.InvariantCulture);
        if (lower == 1 && upper == 1) return "1";
        if (lower == 0 && upper == 1) return "0..1";
        if (lower == upper) return upperText;
        return lower.ToString(CultureInfo.InvariantCulture) + ".." + upperText;
    }

    // ---------- リンク：コネクタから（フィールド走査で拾えなかった分） ----------

    private void CollectLinksFromConnectors()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in Links) covered.Add(link.PairKey);

        List<IConnector> connectors;
        try
        {
            connectors = new List<IConnector>();
            foreach (var c in _d.Connectors)
            {
                var connector = c as IConnector;
                if (connector != null) connectors.Add(connector);
            }
        }
        catch (Exception ex)
        {
            Warnings.Add("コネクタの取得に失敗しました : " + ex.Message);
            return;
        }

        foreach (var connector in connectors)
        {
            var from = NodeInfoOf(connector.StartPoint);
            var to = NodeInfoOf(connector.EndPoint);
            if (from == null || to == null)
            {
                Warnings.Add("両端のどちらかが図上のクラスではないコネクタを読み飛ばしました。");
                continue;
            }
            if (from.ModelId == to.ModelId) continue;

            var pair = string.CompareOrdinal(from.ModelId, to.ModelId) <= 0
                ? from.ModelId + "" + to.ModelId
                : to.ModelId + "" + from.ModelId;
            if (!covered.Add(pair)) continue;   // フィールド走査で既に出している

            var label = "";
            var model = MetaMap.ModelOf(connector);
            if (model != null) label = PlantUmlText.Inline(PlantUmlText.Normalize(model.Name));

            Links.Add(new ClassLink
            {
                FromId = from.ModelId,
                ToId = to.ModelId,
                FromAlias = from.Alias,
                ToAlias = to.Alias,
                Arrow = _o.FallbackLink,
                FieldName = "",
                Label = label,
            });

            Warnings.Add("モデル側で種別を判別できないコネクタを既定の線で出力しました : "
                         + from.Name + " - " + to.Name);
        }
    }

    private ClassNodeInfo NodeInfoOf(INode node)
    {
        if (node == null) return null;
        var model = MetaMap.ModelOf(node);
        if (model == null) return null;

        ClassNodeInfo info;
        if (_byModelId.TryGetValue(model.Id, out info)) return info;

        // 複合ノード（クラスの中の区画）の場合は親をたどる
        var owner = model.Owner;
        var guard = 0;
        while (owner != null && guard++ < 8)
        {
            if (_byModelId.TryGetValue(owner.Id, out info)) return info;
            owner = owner.Owner;
        }
        return null;
    }

    // ---------- 双方向の統合 ----------

    // A→B と B→A が両方あるときは 1 本にまとめ、両端に多重度とロールを出す
    private void MergeBidirectional()
    {
        if (!_o.MergeBidirectional) return;

        var result = new List<ClassLink>();
        var consumed = new HashSet<int>();

        for (var i = 0; i < Links.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            var a = Links[i];

            var partner = -1;
            for (var j = i + 1; j < Links.Count; j++)
            {
                if (consumed.Contains(j)) continue;
                var b = Links[j];
                if (b.FromId != a.ToId || b.ToId != a.FromId) continue;
                if (b.Arrow != a.Arrow) continue;          // 汎化と関連が対になることはない
                if (a.Arrow == "--|>" || a.Arrow == "..|>") continue;  // 汎化・実現は統合しない
                partner = j;
                break;
            }

            if (partner < 0) { result.Add(a); continue; }

            var other = Links[partner];
            consumed.Add(partner);

            a.FromMultiplicity = other.ToMultiplicity;
            a.Arrow = ToUndirected(a.Arrow);
            if (_o.EmitRoleNames && other.Label.Length > 0 && other.Label != a.Label)
                a.Label = a.Label + " / " + other.Label;
            result.Add(a);
        }

        Links.Clear();
        Links.AddRange(result);
    }

    private static string ToUndirected(string arrow)
    {
        if (arrow == "-->") return "--";
        if (arrow == "..>") return "..";
        return arrow;
    }

    private void SortLinks()
    {
        var sorted = Links.OrderBy(l => l.SortKey, StringComparer.Ordinal).ToList();
        Links.Clear();
        Links.AddRange(sorted);
    }
}

// ------------------------------------------------------------
//  出力：PlantUML テキストの組み立て
// ------------------------------------------------------------
public class ClassPlantUmlExporter
{
    private readonly IDiagram _d;
    private readonly ClassPlantUmlOptions _o;
    private readonly StringBuilder _sb = new StringBuilder();
    private ClassDiagramCollector _c;

    public ClassPlantUmlExporter(IDiagram diagram, ClassPlantUmlOptions options)
    {
        _d = diagram;
        _o = options ?? new ClassPlantUmlOptions();
    }

    public List<string> Warnings
    {
        get { return _c != null ? _c.Warnings : new List<string>(); }
    }

    public int NodeCount { get { return _c != null ? _c.Nodes.Count : 0; } }
    public int LinkCount { get { return _c != null ? _c.Links.Count : 0; } }

    public string DiagramName()
    {
        var editor = _d as IEditor;
        var representation = _d as IRepresentation;
        if (representation != null && representation.Model != null
            && !string.IsNullOrEmpty(representation.Model.Name))
            return representation.Model.Name;
        if (editor != null && !string.IsNullOrEmpty(editor.ViewDefinitionName))
            return editor.ViewDefinitionName;
        return "Class";
    }

    public string Export()
    {
        _c = new ClassDiagramCollector(_d, _o);
        _c.Collect();

        WriteHeader();
        WriteNodes();
        WriteLinks();
        LineAt(0, "@enduml");
        return _sb.ToString();
    }

    private void WriteHeader()
    {
        LineAt(0, "@startuml");
        if (!string.IsNullOrEmpty(_o.Theme)) LineAt(0, "!theme " + _o.Theme);
        if (_o.IncludeTitle)
        {
            var name = PlantUmlText.Inline(PlantUmlText.Normalize(DiagramName()));
            if (name.Length > 0) LineAt(0, "title " + name);
        }
        if (_o.EmitTimestamp)
            LineAt(0, "' generated at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (_o.HideEmptyMembers) LineAt(0, "hide empty members");
        LineAt(0, "");
    }

    // パッケージごとにまとめる。パッケージの並びはノードの並び順（＝図の並び）で決まる
    private void WriteNodes()
    {
        // 図上ノード同士の入れ子は WriteNode の再帰で出すため、ここは最上位ノードだけを回す
        var roots = _c.Nodes.Where(n => n.Parent == null).ToList();

        var groups = new List<string>();
        var byPackage = new Dictionary<string, List<ClassNodeInfo>>(StringComparer.Ordinal);

        foreach (var info in roots)
        {
            var key = _o.EmitPackages ? info.PackageKey : "";
            if (!byPackage.ContainsKey(key))
            {
                byPackage[key] = new List<ClassNodeInfo>();
                groups.Add(key);
            }
            byPackage[key].Add(info);
        }

        foreach (var key in groups)
        {
            var members = byPackage[key];
            var depth = 0;

            if (key.Length > 0)
            {
                var path = members[0].PackagePath;
                for (var i = 0; i < path.Count; i++)
                    LineAt(i, "package " + PlantUmlText.Quote(path[i]) + " {");
                depth = path.Count;
            }

            foreach (var info in members) WriteNode(info, depth);

            for (var i = depth - 1; i >= 0; i--) LineAt(i, "}");
            LineAt(0, "");
        }
    }

    private void WriteNode(ClassNodeInfo info, int depth)
    {
        var head = new StringBuilder();
        head.Append(info.Keyword).Append(" ").Append(PlantUmlText.Quote(info.Name));
        head.Append(" as ").Append(info.Alias);
        if (info.Stereotype.Length > 0)
            head.Append(" <<").Append(PlantUmlText.Inline(info.Stereotype)).Append(">>");

        if (info.IsContainer)
        {
            // package / component の中に書けるのは要素宣言だけ。
            // 属性行は出さず、図上の子ノードを入れ子で出す
            if (info.Children.Count > 0)
            {
                LineAt(depth, head.ToString() + " {");
                foreach (var child in info.Children) WriteNode(child, depth + 1);
                LineAt(depth, "}");
            }
            else
            {
                LineAt(depth, head.ToString());
            }
            return;
        }

        var hasBody = info.Attributes.Count > 0 || info.Operations.Count > 0;
        if (!hasBody)
        {
            LineAt(depth, head.ToString());
        }
        else
        {
            LineAt(depth, head.ToString() + " {");
            foreach (var attribute in info.Attributes) LineAt(depth + 1, attribute);
            if (info.Attributes.Count > 0 && info.Operations.Count > 0) LineAt(depth + 1, "--");
            foreach (var operation in info.Operations) LineAt(depth + 1, operation);
            LineAt(depth, "}");
        }

        // クラスの中にクラスは書けないため、クラス系ノードの子ノードは同じ深さで続けて出す
        foreach (var child in info.Children) WriteNode(child, depth);
    }

    private void WriteLinks()
    {
        foreach (var link in _c.Links)
        {
            var sb = new StringBuilder();
            sb.Append(link.FromAlias);
            if (_o.EmitMultiplicity && link.FromMultiplicity.Length > 0)
                sb.Append(" ").Append(PlantUmlText.Quote(link.FromMultiplicity));
            sb.Append(" ").Append(link.Arrow);
            if (_o.EmitMultiplicity && link.ToMultiplicity.Length > 0)
                sb.Append(" ").Append(PlantUmlText.Quote(link.ToMultiplicity));
            sb.Append(" ").Append(link.ToAlias);
            if (link.Label.Length > 0) sb.Append(" : ").Append(link.Label);
            LineAt(0, sb.ToString());
        }
        if (_c.Links.Count > 0) LineAt(0, "");
    }

    private void LineAt(int depth, string text)
    {
        for (var i = 0; i < depth; i++) _sb.Append(_o.IndentUnit);
        _sb.Append(text).Append(_o.NewLine);
    }
}

// ------------------------------------------------------------
//  出力対象（クラス図とその所有モデルのペア）
// ------------------------------------------------------------
public class ClassDiagramEntry
{
    public IModel Owner;
    public IDiagram Diagram;
    public string EditorId = "";
    public string EditorType = "";
    public string ViewDefinitionName = "";
    public string DiagramName = "";

    public string OwnerPath
    {
        get
        {
            if (Owner == null) return "";
            var path = Owner.ModelPath;
            return string.IsNullOrEmpty(path) ? Owner.Name : path;
        }
    }

    public string Name
    {
        get
        {
            if (!string.IsNullOrEmpty(DiagramName)) return DiagramName;
            return string.IsNullOrEmpty(ViewDefinitionName) ? "Class" : ViewDefinitionName;
        }
    }

    public string Label
    {
        get { return OwnerPath + " / " + Name; }
    }
}

// ------------------------------------------------------------
//  クラス図出力の実行
// ------------------------------------------------------------
public class ClassExportRunner
{
    public const string Category = "PlantUML";

    // クラス図として扱うエディタ種別。ND V3.x に "ClassDiagram" は存在しない
    public static bool IsClassDiagramEditor(IEditor editor)
    {
        if (editor == null) return false;
        if (editor is ISequenceDiagram) return false;
        var type = editor.EditorType;
        return type == "ERDiagram" || type == "TreeDiagram";
    }

    // ==================== 1 枚を出力 ====================

    public static void ExportCurrent(IApplication app, ClassPlantUmlOptions options, ExportSettings settings)
    {
        options = options ?? new ClassPlantUmlOptions();
        settings = settings ?? new ExportSettings();

        var ui = app.Window.UI;
        var diagram = app.Workspace.CurrentEditor as IDiagram;
        if (diagram == null)
        {
            ui.ShowInformationDialog("アクティブなエディタはクラス図ではありません。", Category);
            return;
        }

        var exporter = new ClassPlantUmlExporter(diagram, options);
        var uml = exporter.Export();

        ShowPane(app);
        foreach (var line in uml.Replace("\r\n", "\n").Split('\n'))
            app.Output.WriteLine(Category, line);
        WriteWarnings(app, exporter.Warnings);

        if (!settings.SaveToFile) return;

        var baseName = PlantUmlText.SafeFileName(exporter.DiagramName());
        if (baseName.Length == 0) baseName = "class";

        var path = ui.ShowSaveFileDialog(
            "PlantUML ファイルの保存",
            "PlantUML (*.puml)|*.puml|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            baseName + "_class.puml");
        if (string.IsNullOrEmpty(path)) return;

        SaveText(path, uml);
        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "[saved] " + path);
    }

    // ==================== 配下をまとめて出力 ====================

    // folder が指定されていればダイアログを出さずにそこへ書く（シーケンス出力との連続実行用）
    public static int ExportAll(IApplication app, IContext context,
                                ClassPlantUmlOptions options, ExportSettings settings,
                                string folder, bool quiet)
    {
        settings = settings ?? new ExportSettings();
        context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

        var root = ExportRunner.ResolveRoot(app);
        if (root == null)
        {
            if (!quiet) app.Window.UI.ShowInformationDialog("プロジェクトが開かれていません。", Category);
            return 0;
        }

        var skipCount = 0;
        var targets = Collect(root, settings.SkipEmptyDiagram, ref skipCount);
        return ExportAll(app, context, options, settings, folder, quiet, root, targets, skipCount);
    }

    // 収集済みの対象リストを受ける版（Part 8 の一括出力と分類を共有するため）
    public static int ExportAll(IApplication app, IContext context,
                                ClassPlantUmlOptions options, ExportSettings settings,
                                string folder, bool quiet,
                                IModel root, List<ClassDiagramEntry> targets, int skipCount)
    {
        options = options ?? new ClassPlantUmlOptions();
        settings = settings ?? new ExportSettings();

        var ui = app.Window.UI;
        context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

        if (targets.Count == 0)
        {
            if (!quiet)
                ui.ShowInformationDialog(
                    "「" + root.Name + "」配下に出力対象のクラス図が見つかりませんでした。"
                    + (skipCount > 0 ? "（空の図 " + skipCount + " 件をスキップ）" : ""), Category);
            else
                app.Output.WriteLine(Category, "クラス図: 対象なし"
                    + (skipCount > 0 ? "（空の図 " + skipCount + " 件をスキップ）" : ""));
            return 0;
        }

        if (string.IsNullOrEmpty(folder))
        {
            if (settings.Confirm)
            {
                var message = "「" + root.Name + "」配下のクラス図 " + targets.Count
                            + " 件を PlantUML に変換します。\n\n続行しますか？";
                if (!ui.ShowConfirmDialog(message, Category)) return 0;
            }
            folder = ui.ShowSelectFolderDialog("PlantUML の出力先フォルダを選択してください");
            if (string.IsNullOrEmpty(folder)) return 0;
        }

        var fileNames = BuildFileNames(targets);

        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "=== クラス図 : " + targets.Count + " 件 ===");

        var okCount = 0;
        var errorCount = 0;

        foreach (var entry in targets)
        {
            try
            {
                var exporter = new ClassPlantUmlExporter(entry.Diagram, options);
                var uml = exporter.Export();
                SaveText(System.IO.Path.Combine(folder, fileNames[entry.EditorId]), uml);
                okCount++;
                app.Output.WriteLine(Category, "[ok]    " + entry.Label
                                     + "  (クラス " + exporter.NodeCount
                                     + " / 線 " + exporter.LinkCount + ")");
                WriteWarnings(app, exporter.Warnings);
            }
            catch (Exception ex)
            {
                errorCount++;
                app.Output.WriteLine(Category, "[error] " + entry.Label + " : " + ex.Message);
            }
        }

        app.Output.WriteLine(Category, "=== クラス図 完了 : 成功 " + okCount
                             + " / スキップ " + skipCount + " / エラー " + errorCount + " ===");

        if (!quiet)
            ui.ShowInformationDialog(
                "クラス図の PlantUML 出力が完了しました。\n\n"
                + "成功: " + okCount + " 件\n"
                + "スキップ: " + skipCount + " 件\n"
                + "エラー: " + errorCount + " 件\n\n"
                + "出力先: " + folder, Category);

        return okCount;
    }

    // ==================== 対象の決定 ====================

    public static List<ClassDiagramEntry> Collect(IModel root, bool skipEmpty, ref int skipCount)
    {
        var entries = new List<ClassDiagramEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var models = new List<IModel>();
        models.Add(root);
        models.AddRange(root.GetAllChildren().Cast<IModel>());

        foreach (var model in models)
        {
            if (model == null || model.IsDeleted || model.IsProxy) continue;

            foreach (var editor in model.GetEditors())
            {
                if (!IsClassDiagramEditor(editor)) continue;

                var diagram = editor as IDiagram;
                if (diagram == null) continue;
                if (!seen.Add(editor.Id)) continue;

                var nodeCount = 0;
                try { foreach (var n in diagram.Nodes) if (n != null) nodeCount++; }
                catch (Exception) { }

                if (skipEmpty && nodeCount == 0)
                {
                    skipCount++;
                    continue;
                }

                var representation = editor as IRepresentation;
                entries.Add(new ClassDiagramEntry
                {
                    Owner = model,
                    Diagram = diagram,
                    EditorId = editor.Id,
                    EditorType = editor.EditorType,
                    ViewDefinitionName = editor.ViewDefinitionName,
                    DiagramName = representation != null && representation.Model != null
                                  ? representation.Model.Name : model.Name,
                });
            }
        }

        return entries
            .OrderBy(e => e.OwnerPath, StringComparer.Ordinal)
            .ThenBy(e => e.ViewDefinitionName, StringComparer.Ordinal)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.EditorId, StringComparer.Ordinal)
            .ToList();
    }

    // シーケンス図と同じフォルダに出しても衝突しないよう _class を付ける
    public static Dictionary<string, string> BuildFileNames(List<ClassDiagramEntry> entries)
    {
        var baseNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            // ファイル名は図名のみ。モデルパスとの対応は _index.md で追跡する
            var baseName = PlantUmlText.SafeFileName(entry.Name);
            if (baseName.Length == 0) baseName = "class";
            if (baseName.Length > 100) baseName = baseName.Substring(0, 100);
            baseNames[entry.EditorId] = baseName;
        }

        var duplicated = new HashSet<string>(
            baseNames.Values
                     .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1)
                     .Select(g => g.Key),
            StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in baseNames)
        {
            result[pair.Key] = duplicated.Contains(pair.Value)
                ? pair.Value + "_" + PlantUmlText.ShortHash(pair.Key) + "_class.puml"
                : pair.Value + "_class.puml";
        }
        return result;
    }

    // ==================== ユーティリティ ====================

    private static void WriteWarnings(IApplication app, List<string> warnings)
    {
        if (warnings == null || warnings.Count == 0) return;
        foreach (var warning in warnings)
            app.Output.WriteLine(Category, "[warn]  " + warning);
    }

    private static void ShowPane(IApplication app)
    {
        OutputPane.Show(app, Category);
    }

    private static void SaveText(string path, string text)
    {
        System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}

// ------------------------------------------------------------
//  クラス図のメタモデル調査
//
//    メタクラス名・フィールド名はプロファイル依存で推測できないため、
//    実機の値をここでダンプし、ClassPlantUmlOptions の対応表を埋める
// ------------------------------------------------------------
public class ClassProbe
{
    public const string Category = "PlantUmlImport";

    public static void Run(IApplication app, IDiagram diagram)
    {
        var w = new Action<string>(text => app.Output.WriteLine(Category, text));

        var editor = diagram as IEditor;
        w("=== クラス図調査 ===");
        w("EditorType         : " + (editor != null ? editor.EditorType : "(不明)"));
        w("ViewDefinitionName : " + (editor != null ? editor.ViewDefinitionName : "(不明)"));

        var nodes = new List<INode>();
        try { foreach (var n in diagram.Nodes) { var node = n as INode; if (node != null) nodes.Add(node); } }
        catch (Exception ex) { w("ノードの取得に失敗 : " + ex.Message); }

        var connectors = new List<IConnector>();
        try { foreach (var c in diagram.Connectors) { var conn = c as IConnector; if (conn != null) connectors.Add(conn); } }
        catch (Exception ex) { w("コネクタの取得に失敗 : " + ex.Message); }

        w("ノード数           : " + nodes.Count);
        w("コネクタ数         : " + connectors.Count);
        w("");

        var models = new List<IModel>();
        foreach (var node in nodes)
        {
            var model = MetaMap.ModelOf(node);
            if (model != null) models.Add(model);
        }

        // 転記時変更: MetaProbe（シーケンス図調査、未転記）による詳細ダンプは省略。
        // フィールド構成は AgentReview の「エクスポート診断」で代替する
        w("ノード(1件目)      : " + Describe(models.Count > 0 ? models[0] : null));
        w("ノードの子(1件目)  : " + Describe(FirstChild(models)));

        DumpClassNames(w, "ノードのクラス名一覧", models);
        DumpClassNames(w, "子のクラス名一覧", AllChildren(models));

        DumpConnectors(w, connectors);
        DumpReferenceFields(w, models);

        w("=== 調査終了 ===");
    }

    private static string Describe(IModel model)
    {
        if (model == null) return "(なし)";
        var cls = model.Metaclass;
        return (model.Name ?? "(無名)") + " : " + (cls != null ? cls.FullName : model.ClassName);
    }

    private static IModel FirstChild(List<IModel> models)
    {
        foreach (var model in models)
        {
            try
            {
                foreach (var child in model.GetChildren().Cast<IModel>())
                    if (child != null && !child.IsDeleted) return child;
            }
            catch (Exception) { }
        }
        return null;
    }

    private static List<IModel> AllChildren(List<IModel> models)
    {
        var result = new List<IModel>();
        foreach (var model in models)
        {
            try
            {
                foreach (var child in model.GetChildren().Cast<IModel>())
                    if (child != null && !child.IsDeleted) result.Add(child);
            }
            catch (Exception) { }
        }
        return result;
    }

    private static void DumpClassNames(Action<string> w, string title, List<IModel> models)
    {
        w("---- " + title + " ----");
        if (models.Count == 0) { w("  (なし)"); w(""); return; }

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var model in models)
        {
            var name = model.ClassName ?? "(null)";
            if (!counts.ContainsKey(name)) counts[name] = 0;
            counts[name]++;
        }
        foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
            w("  " + pair.Key + " : " + pair.Value + " 件");
        w("");
    }

    private static void DumpConnectors(Action<string> w, List<IConnector> connectors)
    {
        w("---- コネクタ ----");
        if (connectors.Count == 0) { w("  (なし)"); w(""); return; }

        foreach (var connector in connectors)
        {
            var from = MetaMap.ModelOf(connector.StartPoint);
            var to = MetaMap.ModelOf(connector.EndPoint);
            var label = (from != null ? from.Name : "?") + " -> " + (to != null ? to.Name : "?");

            var own = MetaMap.ModelOf(connector);
            w("  " + label
              + " | LineType=" + SafeLineType(connector)
              + " | コネクタのモデル=" + (own != null ? own.ClassName + " '" + own.Name + "'" : "(なし)"));

            if (from == null || to == null) continue;
            try
            {
                var any = false;
                foreach (var r in from.GetRelationsOf(to).Cast<IRelationship>())
                {
                    any = true;
                    w("      IsEmbedded=" + r.IsEmbedded
                      + " IsReference=" + r.IsReference
                      + " IsTwoWay=" + r.IsTwoWay
                      + " SourceField=" + FieldName(r.SourceField)
                      + " TargetField=" + FieldName(r.TargetField));
                }
                if (!any) w("      (GetRelationsOf で関連を取得できません)");
            }
            catch (Exception ex) { w("      GetRelationsOf に失敗 : " + ex.Message); }
        }
        w("");
    }

    private static string SafeLineType(IConnector c)
    {
        try { return c.LineType; } catch (Exception) { return "(不明)"; }
    }

    private static string FieldName(IField f)
    {
        if (f == null) return "(なし)";
        return f.Name + "[" + f.LowerBound + ".."
             + (f.UpperBound < 0 ? "*" : f.UpperBound.ToString(CultureInfo.InvariantCulture)) + "]";
    }

    // 図上のノードどうしを結ぶ参照フィールドを一覧にする（LinkMap を埋めるための材料）
    private static void DumpReferenceFields(Action<string> w, List<IModel> models)
    {
        w("---- 図上のノードを結ぶ参照フィールド ----");

        var ids = new HashSet<string>(models.Select(m => m.Id), StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var model in models)
        {
            var cls = model.Metaclass;
            if (cls == null) continue;

            List<IField> fields;
            try { fields = cls.GetFields().Cast<IField>().ToList(); }
            catch (Exception) { continue; }

            foreach (var f in fields)
            {
                if (!f.IsReference && !f.IsEmbedded) continue;
                try
                {
                    foreach (var v in model.GetFieldValues(f.Name))
                    {
                        var target = v as IModel;
                        if (target == null || !ids.Contains(target.Id)) continue;
                        var key = (f.IsEmbedded ? "所有 " : "参照 ") + f.Name
                                + " [" + f.LowerBound + ".."
                                + (f.UpperBound < 0 ? "*" : f.UpperBound.ToString(CultureInfo.InvariantCulture)) + "]";
                        if (!counts.ContainsKey(key)) counts[key] = 0;
                        counts[key]++;
                    }
                }
                catch (Exception) { }
            }
        }

        if (counts.Count == 0) w("  (なし)");
        else
            foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
                w("  " + pair.Key + " : " + pair.Value + " 件");
        w("");
    }
}

// ============================================================

//  Part 8 / 状態遷移図（ステートマシン図）の PlantUML 出力
//
//    V3.x の拡張 API に状態遷移図専用のインタフェースは無い（公式 docs 確認済み）。
//    クラス図と同じく汎用 IDiagram の Nodes / Connectors を使い、
//    状態・擬似状態・遷移の意味はモデル側のメタクラス名とフィールドから取る。
//
//    メタクラス名・フィールド名はプロファイル依存。StatePlantUmlOptions の
//    対応表で解釈し、未登録のものは state 扱いにして警告を出す。
//    実機の値は状態遷移図を開いて「クラス図調査」（ClassProbe）で確認し、
//    StateKindMap / 各 FieldNames に追記して育てる。
//
//    図の種類の判別（クラス図か状態遷移図か）は EditorType では確定できない
//    （実機では状態遷移図もクラス図と同じ "ERDiagram"）ため、
//    ViewDefinitionName の完全一致 → ノードのメタクラス名（StateClassNames と
//    完全一致）の順で行う。誤判定時は StateViewDefinitionNames /
//    NonStateViewDefinitionNames / StateClassNames を編集して救済する。
// ============================================================

// ------------------------------------------------------------
//  状態遷移図の出力オプション
// ------------------------------------------------------------
public class StatePlantUmlOptions
{
    public bool IncludeTitle = true;            // 図名を title として出力する
    public string Theme = null;                 // !theme <name> を出力する
    public bool HideEmptyDescription = true;    // hide empty description を出力する
    public bool EmitInternalActions = true;     // entry / exit / do を出力する
    public bool EmitTimestamp = false;          // 出力日時を埋め込む（差分安定化のため既定 false）
    public string IndentUnit = "  ";            // 入れ子のインデント
    public string NewLine = "\n";               // 改行は LF 固定
    public string DefaultArrow = "-->";         // 遷移の矢印

    // ---- 図種の判別 ----
    //
    // 実機で確認した事実（DeSIDE UML/SysML プロファイル・2026-08）:
    //   状態遷移図の EditorType はクラス図と同じ "ERDiagram" で、
    //   ViewDefinitionName（"ステートマシン図"）とノードのメタクラス
    //   （State、親クラス Vertex）でしか区別できない

    // EditorType による明示指定（最優先の逃げ道。実機で判明したら追記する）
    public List<string> StateEditorTypes = new List<string>();      // 例: "StateMachineDiagram"
    public List<string> NonStateEditorTypes = new List<string>();   // 状態遷移図として扱わない EditorType

    // ViewDefinitionName による判別（完全一致・大文字小文字無視）。
    // EditorType がクラス図と同じでもビュー定義名は図種ごとに異なるため、これを優先する
    public List<string> StateViewDefinitionNames = new List<string>
        { "ステートマシン図", "状態遷移図", "StateMachineDiagram", "StateMachine" };
    public List<string> NonStateViewDefinitionNames = new List<string>
        { "クラス図", "ClassDiagram" };

    // ノードのメタクラス名（ClassName / 親クラス名）との完全一致で状態遷移図と判定する。
    // 部分一致にすると "〜State〜" を含む無関係なメタクラスで誤判定するため完全一致に限る
    public List<string> StateClassNames = new List<string>
    {
        "Vertex", "State", "StateMachine", "Pseudostate", "PseudoState",
        "InitialState", "FinalState", "HistoryState", "ControlState",
        "EntryPoint", "ExitPoint",
        "状態", "擬似状態", "疑似状態", "履歴状態",
    };

    // ---- ノードの種別 ----

    // メタクラス名 → 種別
    //   state / initial / final / choice / fork / join / history / deephistory /
    //   entrypoint / exitpoint / skip
    public Dictionary<string, string> StateKindMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "State", "state" }, { "SimpleState", "state" }, { "CompositeState", "state" },
        { "StateMachine", "state" }, { "SubmachineState", "state" },
        { "状態", "state" }, { "複合状態", "state" }, { "ステートマシン", "state" },

        { "InitialState", "initial" }, { "Initial", "initial" }, { "InitialPseudostate", "initial" },
        { "InitialNode", "initial" },
        { "初期状態", "initial" }, { "開始状態", "initial" }, { "開始擬似状態", "initial" },

        { "FinalState", "final" }, { "Final", "final" }, { "FinalNode", "final" },
        { "Terminate", "final" },
        { "終了状態", "final" }, { "最終状態", "final" }, { "停止", "final" },

        { "Choice", "choice" }, { "ChoicePseudostate", "choice" },
        { "選択", "choice" }, { "分岐", "choice" },

        { "Junction", "choice" }, { "ジャンクション", "choice" },

        { "Fork", "fork" }, { "フォーク", "fork" },
        { "Join", "join" }, { "ジョイン", "join" },

        { "ShallowHistory", "history" }, { "History", "history" }, { "HistoryState", "history" },
        { "履歴", "history" }, { "浅い履歴", "history" }, { "履歴状態", "history" },
        { "DeepHistory", "deephistory" }, { "深い履歴", "deephistory" },

        { "ControlState", "choice" },   // DeSIDE プロファイルの判断ノード

        { "EntryPoint", "entrypoint" }, { "入場点", "entrypoint" },
        { "ExitPoint", "exitpoint" }, { "退場点", "exitpoint" },

        { "Region", "skip" }, { "領域", "skip" },   // 図上に領域ノードが出る場合の保険
    };

    // メタクラスが汎用の Pseudostate で、種別がフィールド値に入っている場合の候補
    public List<string> PseudostateKindFieldNames =
        new List<string> { "Kind", "PseudostateKind", "StateKind", "種類", "種別" };

    // ---- 遷移ラベル（イベント [ガード] / アクション）----

    public List<string> TriggerFieldNames =
        new List<string> { "Trigger", "Event", "トリガ", "トリガー", "イベント", "契機", "事象" };
    public List<string> GuardFieldNames =
        new List<string> { "Guard", "GuardCondition", "Condition", "ガード", "ガード条件", "条件" };
    public List<string> ActionFieldNames =
        new List<string> { "Action", "Effect", "Behavior", "アクション", "効果", "動作", "振る舞い", "処理" };

    // ---- 状態の内部アクション（entry / exit / do）----

    // 第一経路: 状態モデル自身のフィールド値
    public List<string> EntryFieldNames =
        new List<string> { "Entry", "EntryAction", "EntryActivity", "EntryBehavior", "入場", "入場時", "入場アクション", "エントリ" };
    public List<string> ExitFieldNames =
        new List<string> { "Exit", "ExitAction", "ExitActivity", "ExitBehavior", "退場", "退場時", "退場アクション" };
    public List<string> DoFieldNames =
        new List<string> { "Do", "DoActivity", "DoAction", "DoBehavior", "実行", "実行時", "アクティビティ" };

    // 第二経路: 子モデルがアクションの場合。メタクラス名 → "entry" | "exit" | "do" | "internal" | "skip"
    public Dictionary<string, string> StateMemberKindMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "EntryAction", "entry" }, { "EntryActivity", "entry" }, { "入場アクション", "entry" },
        { "ExitAction", "exit" }, { "ExitActivity", "exit" }, { "退場アクション", "exit" },
        { "DoActivity", "do" }, { "DoAction", "do" }, { "実行アクティビティ", "do" },
        { "InternalTransition", "internal" }, { "内部遷移", "internal" },
        { "Region", "skip" }, { "領域", "skip" },
    };
}

// ------------------------------------------------------------
//  中間表現：図上の 1 状態（または擬似状態）
// ------------------------------------------------------------
public class StateNodeInfo
{
    public IModel Model;
    public INode Node;
    public string ModelId = "";
    public string Name = "";
    public string Alias = "";
    public string Kind = "state";
    public bool HasName;                        // 無名の擬似状態は表示名を出さない

    // 図上ノード同士の所有関係（複合状態の入れ子出力に使う）
    public StateNodeInfo Parent;
    public List<StateNodeInfo> Children = new List<StateNodeInfo>();

    // "entry / 〜" などの内部アクション行（別名 : テキスト 形式で出す）
    public List<string> Descriptions = new List<string>();

    // initial / final は宣言せず遷移の端点 [*] としてだけ現れる
    public bool IsAnonymousEndpoint
    {
        get { return Kind == "initial" || Kind == "final"; }
    }
}

// ------------------------------------------------------------
//  中間表現：1 本の遷移
// ------------------------------------------------------------
public class StateTransition
{
    public StateNodeInfo From;
    public StateNodeInfo To;
    public string Label = "";
    public string UniqueId = "";               // 決定的ソート用（遷移モデルの Id か連番）

    // [*] 端点を含む遷移は、その擬似状態の親ブロック内に出す必要がある。
    // null ならトップレベルに出す
    public StateNodeInfo Scope;

    public string SortKey
    {
        get
        {
            return (From != null ? From.Alias : "") + ""
                 + (To != null ? To.Alias : "") + ""
                 + Label + "" + UniqueId;
        }
    }
}

// ------------------------------------------------------------
//  収集：IDiagram から状態と遷移を組み立てる
// ------------------------------------------------------------
public class StateDiagramCollector
{
    private readonly IDiagram _d;
    private readonly StatePlantUmlOptions _o;

    public readonly List<StateNodeInfo> Nodes = new List<StateNodeInfo>();
    public readonly List<StateTransition> Transitions = new List<StateTransition>();
    public readonly List<string> Warnings = new List<string>();

    private readonly Dictionary<string, StateNodeInfo> _byModelId =
        new Dictionary<string, StateNodeInfo>(StringComparer.Ordinal);
    private readonly HashSet<string> _usedAlias = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownKind = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _unknownMember = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public StateDiagramCollector(IDiagram diagram, StatePlantUmlOptions options)
    {
        _d = diagram;
        _o = options ?? new StatePlantUmlOptions();
    }

    public void Collect()
    {
        CollectNodes();
        CollectTransitions();
        DropUnlabeledDuplicates();
        SortTransitions();
    }

    // 遷移が「ラベル付きの線」と「ラベル無しの線」の 2 系統のコネクタで
    // 二重に描かれるプロファイルがある（ラベル無し側は参照関係の線などで、
    // モデルが別なので Id の重複除去では消えない）。
    // 同じ端点間にラベル付きの遷移が 1 本でもあれば、ラベル無しの遷移は落とす。
    // ラベル無ししか無い端点間はそのまま残す（正当な無ラベル遷移を消さない）
    private void DropUnlabeledDuplicates()
    {
        var labeledPairs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in Transitions)
            if (t.Label.Length > 0)
                labeledPairs.Add(t.From.ModelId + "" + t.To.ModelId);

        if (labeledPairs.Count == 0) return;

        var kept = Transitions
            .Where(t => t.Label.Length > 0
                     || !labeledPairs.Contains(t.From.ModelId + "" + t.To.ModelId))
            .ToList();
        Transitions.Clear();
        Transitions.AddRange(kept);
    }

    // ---------- ノード ----------

    private void CollectNodes()
    {
        var shapes = new List<INode>();
        try
        {
            foreach (var s in _d.Nodes)
            {
                var node = s as INode;
                if (node != null) shapes.Add(node);
            }
        }
        catch (Exception ex) { Warnings.Add("ノードの取得に失敗しました : " + ex.Message); }

        var ordered = shapes
            .OrderBy(n => SafeY(n))
            .ThenBy(n => SafeX(n))
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

        // パス1: まず全ノードを登録する（親子解決と端点解決で全ノードの索引が要る）
        foreach (var node in ordered)
        {
            var model = MetaMap.ModelOf(node);
            if (model == null || model.IsDeleted) continue;
            if (_byModelId.ContainsKey(model.Id)) continue;   // 同じモデルの重複シェイプ

            var kind = KindOf(model);
            if (kind == "skip") continue;

            var rawName = PlantUmlText.Normalize(model.Name);
            var info = new StateNodeInfo
            {
                Model = model,
                Node = node,
                ModelId = model.Id,
                Kind = kind,
                HasName = rawName.Length > 0,
                Name = rawName.Length > 0 ? rawName : KindLabel(kind),
            };
            info.Alias = MakeAlias(info.Name, model.Id);

            Nodes.Add(info);
            _byModelId[model.Id] = info;
        }

        // パス2: 親子解決（最も近い「図上ノードでもあるオーナー」を親にする。
        //        UML の Region モデルが間に挟まっていても自動的に飛ばされる）
        foreach (var info in Nodes)
        {
            var owner = info.Model.Owner;
            var guard = 0;
            while (owner != null && guard++ < 32)
            {
                StateNodeInfo parent;
                if (_byModelId.TryGetValue(owner.Id, out parent))
                {
                    info.Parent = parent;
                    parent.Children.Add(info);
                    break;
                }
                owner = owner.Owner;
            }

            if (_o.EmitInternalActions && info.Kind == "state") CollectDescriptions(info);
        }

        if (Nodes.Count == 0) Warnings.Add("図上にモデルと対応するノードがありません。");
    }

    private static double SafeY(INode n)
    {
        try { return n.LocationY; } catch (Exception) { return 0; }
    }

    private static double SafeX(INode n)
    {
        try { return n.LocationX; } catch (Exception) { return 0; }
    }

    private static string KindLabel(string kind)
    {
        if (kind == "choice") return "choice";
        if (kind == "fork") return "fork";
        if (kind == "join") return "join";
        if (kind == "history" || kind == "deephistory") return "H";
        return "(unnamed)";
    }

    private string MakeAlias(string label, string modelId)
    {
        var alias = PlantUmlText.AsciiAlias(label);
        if (alias.Length == 0) alias = "S" + PlantUmlText.ShortHash(modelId);
        if (!_usedAlias.Add(alias))
        {
            alias = alias + "_" + PlantUmlText.ShortHash(modelId);
            _usedAlias.Add(alias);
        }
        return alias;
    }

    private string KindOf(IModel m)
    {
        string kind;
        if (!string.IsNullOrEmpty(m.ClassName) && _o.StateKindMap.TryGetValue(m.ClassName, out kind))
            return kind;

        // 親クラスをたどる（プロファイルが State を継承した派生クラスを使っている場合）
        var cls = m.Metaclass;
        if (cls != null)
        {
            try
            {
                foreach (var s in cls.GetAllSuperClasses().Cast<IClass>())
                    if (_o.StateKindMap.TryGetValue(s.Name, out kind)) return kind;
            }
            catch (Exception) { }
        }

        // 汎用 Pseudostate で種別がフィールド値の場合（値も StateKindMap で引く）
        var kindText = ClassDiagramCollector.TextOf(m, _o.PseudostateKindFieldNames);
        if (kindText.Length > 0 && _o.StateKindMap.TryGetValue(kindText, out kind)) return kind;

        // 対応表に無いものは state として出し、1 クラス名につき 1 回だけ警告する
        if (!string.IsNullOrEmpty(m.ClassName) && _unknownKind.Add(m.ClassName))
            Warnings.Add("状態の種別が不明なため state として出力しました : ClassName='"
                         + m.ClassName + "'（StatePlantUmlOptions.StateKindMap に追加してください）");
        return "state";
    }

    // ---------- entry / exit / do ----------

    private void CollectDescriptions(StateNodeInfo info)
    {
        // 第一経路: 状態モデル自身のフィールド値
        AddDescription(info, "entry", ClassDiagramCollector.TextOf(info.Model, _o.EntryFieldNames));
        AddDescription(info, "exit", ClassDiagramCollector.TextOf(info.Model, _o.ExitFieldNames));
        AddDescription(info, "do", ClassDiagramCollector.TextOf(info.Model, _o.DoFieldNames));

        // 第二経路: 子モデルがアクションの場合
        IEnumerable<IModel> children;
        try { children = info.Model.GetChildren().Cast<IModel>().ToList(); }
        catch (Exception ex)
        {
            Warnings.Add(info.Name + " : 子モデルの取得に失敗しました : " + ex.Message);
            return;
        }

        foreach (var child in children)
        {
            if (child == null || child.IsDeleted) continue;
            // それ自体が図上のノードである子はサブ状態として出すので、ここでは扱わない
            if (_byModelId.ContainsKey(child.Id)) continue;

            var kind = StateMemberKindOf(child);
            if (kind == "skip") continue;
            if (kind == "internal")
            {
                var label = BuildTransitionLabel(child);
                if (label.Length > 0) info.Descriptions.Add(label);
            }
            else
            {
                AddDescription(info, kind, PlantUmlText.Normalize(child.Name));
            }
        }
    }

    private static void AddDescription(StateNodeInfo info, string keyword, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        info.Descriptions.Add(keyword + " / " + PlantUmlText.Inline(text));
    }

    private string StateMemberKindOf(IModel child)
    {
        string kind;
        if (!string.IsNullOrEmpty(child.ClassName) && _o.StateMemberKindMap.TryGetValue(child.ClassName, out kind))
            return kind;

        var cls = child.Metaclass;
        if (cls != null)
        {
            try
            {
                foreach (var s in cls.GetAllSuperClasses().Cast<IClass>())
                    if (_o.StateMemberKindMap.TryGetValue(s.Name, out kind)) return kind;
            }
            catch (Exception) { }
        }

        // 不明な子はサブ状態や領域の可能性があるため、誤ってテキスト行にせず読み飛ばす
        if (!string.IsNullOrEmpty(child.ClassName) && _unknownMember.Add(child.ClassName))
            Warnings.Add("状態の子モデルの種別が不明なため読み飛ばしました : ClassName='"
                         + child.ClassName + "'（StatePlantUmlOptions.StateMemberKindMap に追加してください）");
        return "skip";
    }

    // ---------- 遷移 ----------

    private void CollectTransitions()
    {
        List<IConnector> connectors;
        try
        {
            connectors = new List<IConnector>();
            foreach (var c in _d.Connectors)
            {
                var connector = c as IConnector;
                if (connector != null) connectors.Add(connector);
            }
        }
        catch (Exception ex)
        {
            Warnings.Add("コネクタの取得に失敗しました : " + ex.Message);
            return;
        }

        // 1 本の遷移が複数のコネクタ図形（線分・ラベル図形など）で構成される
        // プロファイルがある（実機ではノード 6 件に対しコネクタ 44 件）。
        // 同じ遷移モデルを指すコネクタは 1 本にまとめる
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var noModelWarned = false;
        foreach (var connector in connectors)
        {
            var from = NodeInfoOf(connector.StartPoint);
            var to = NodeInfoOf(connector.EndPoint);
            if (from == null || to == null)
            {
                Warnings.Add("両端のどちらかが図上の状態ではないコネクタを読み飛ばしました。");
                continue;
            }

            var label = "";
            string uniqueId;
            var model = MetaMap.ModelOf(connector);
            if (model != null)
            {
                // 遷移モデルの Id で重複除去する。平行遷移（同じ状態間の複数遷移）は
                // 別モデルなので消えない。自己遷移もそのまま出す
                label = BuildTransitionLabel(model);
                uniqueId = model.Id;
            }
            else
            {
                // モデルが取れないコネクタは 端点 + ラベル の組で重複除去する
                uniqueId = "c" + from.ModelId + "" + to.ModelId + "" + label;
                if (!noModelWarned)
                {
                    noModelWarned = true;
                    Warnings.Add("モデルが取得できないコネクタをラベルなしの遷移として出力しました。");
                }
            }
            if (!seen.Add(uniqueId)) continue;

            Transitions.Add(new StateTransition
            {
                From = from,
                To = to,
                Label = label,
                UniqueId = uniqueId,
                Scope = ScopeOf(from, to),
            });
        }
    }

    // [*] はブロックスコープで解決されるため、initial / final を端点に持つ遷移は
    // その擬似状態の親ブロック内に出す
    private static StateNodeInfo ScopeOf(StateNodeInfo from, StateNodeInfo to)
    {
        if (from.IsAnonymousEndpoint) return from.Parent;
        if (to.IsAnonymousEndpoint) return to.Parent;
        return null;
    }

    // トリガ [ガード] / アクション（空要素は省略。全部空ならモデル名）
    private string BuildTransitionLabel(IModel m)
    {
        var trigger = ClassDiagramCollector.TextOf(m, _o.TriggerFieldNames);
        var guard = ClassDiagramCollector.TextOf(m, _o.GuardFieldNames);
        var action = ClassDiagramCollector.TextOf(m, _o.ActionFieldNames);

        var sb = new StringBuilder();
        if (trigger.Length > 0) sb.Append(trigger);
        if (guard.Length > 0)
        {
            if (sb.Length > 0) sb.Append(" ");
            sb.Append("[").Append(guard).Append("]");
        }
        if (action.Length > 0)
        {
            if (sb.Length > 0) sb.Append(" ");
            sb.Append("/ ").Append(action);
        }
        if (sb.Length == 0) return PlantUmlText.Inline(PlantUmlText.Normalize(m.Name));
        return PlantUmlText.Inline(sb.ToString());
    }

    private StateNodeInfo NodeInfoOf(INode node)
    {
        if (node == null) return null;
        var model = MetaMap.ModelOf(node);
        if (model == null) return null;

        StateNodeInfo info;
        if (_byModelId.TryGetValue(model.Id, out info)) return info;

        // 複合ノード（状態の中の区画など）の場合は親をたどる
        var owner = model.Owner;
        var guard = 0;
        while (owner != null && guard++ < 8)
        {
            if (_byModelId.TryGetValue(owner.Id, out info)) return info;
            owner = owner.Owner;
        }
        return null;
    }

    private void SortTransitions()
    {
        var sorted = Transitions.OrderBy(t => t.SortKey, StringComparer.Ordinal).ToList();
        Transitions.Clear();
        Transitions.AddRange(sorted);
    }
}

// ------------------------------------------------------------
//  出力：PlantUML 状態図テキストの組み立て
// ------------------------------------------------------------
public class StatePlantUmlExporter
{
    private readonly IDiagram _d;
    private readonly StatePlantUmlOptions _o;
    private readonly StringBuilder _sb = new StringBuilder();
    private StateDiagramCollector _c;

    public StatePlantUmlExporter(IDiagram diagram, StatePlantUmlOptions options)
    {
        _d = diagram;
        _o = options ?? new StatePlantUmlOptions();
    }

    public List<string> Warnings
    {
        get { return _c != null ? _c.Warnings : new List<string>(); }
    }

    public int NodeCount { get { return _c != null ? _c.Nodes.Count : 0; } }
    public int TransitionCount { get { return _c != null ? _c.Transitions.Count : 0; } }

    public string DiagramName()
    {
        var editor = _d as IEditor;
        var representation = _d as IRepresentation;
        if (representation != null && representation.Model != null
            && !string.IsNullOrEmpty(representation.Model.Name))
            return representation.Model.Name;
        if (editor != null && !string.IsNullOrEmpty(editor.ViewDefinitionName))
            return editor.ViewDefinitionName;
        return "StateMachine";
    }

    public string Export()
    {
        _c = new StateDiagramCollector(_d, _o);
        _c.Collect();

        WriteHeader();

        // 複合状態の入れ子は WriteNode の再帰で出すため、ここは最上位ノードだけを回す
        foreach (var info in _c.Nodes.Where(n => n.Parent == null))
            WriteNode(info, 0);
        LineAt(0, "");

        // [*] 端点を含まない遷移はトップレベルにまとめて出す（別名はグローバルに解決される）
        var any = false;
        foreach (var t in _c.Transitions.Where(x => x.Scope == null))
        {
            WriteTransition(t, 0);
            any = true;
        }
        if (any) LineAt(0, "");

        LineAt(0, "@enduml");
        return _sb.ToString();
    }

    private void WriteHeader()
    {
        LineAt(0, "@startuml");
        if (!string.IsNullOrEmpty(_o.Theme)) LineAt(0, "!theme " + _o.Theme);
        if (_o.IncludeTitle)
        {
            var name = PlantUmlText.Inline(PlantUmlText.Normalize(DiagramName()));
            if (name.Length > 0) LineAt(0, "title " + name);
        }
        if (_o.EmitTimestamp)
            LineAt(0, "' generated at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (_o.HideEmptyDescription) LineAt(0, "hide empty description");
        LineAt(0, "");
    }

    private void WriteNode(StateNodeInfo info, int depth)
    {
        // initial / final は宣言せず [*] としてだけ現れる
        if (info.IsAnonymousEndpoint) return;

        // 履歴は親ブロックがあれば sParent[H] 表記だけで済む（宣言不要）
        if (info.Kind == "history" || info.Kind == "deephistory")
        {
            if (info.Parent == null)
            {
                // トップレベルの履歴は表現手段が無いためステレオタイプ付き状態に退避する
                LineAt(depth, "state " + PlantUmlText.Quote(info.Name) + " as " + info.Alias + " <<history>>");
                _c.Warnings.Add("親の無い履歴擬似状態をステレオタイプ付き状態として出力しました : " + info.Name);
            }
            return;
        }

        var head = new StringBuilder();
        head.Append("state ");
        if (info.HasName) head.Append(PlantUmlText.Quote(info.Name)).Append(" as ");
        head.Append(info.Alias);
        var stereotype = StereotypeOf(info.Kind);
        if (stereotype.Length > 0) head.Append(" ").Append(stereotype);

        // ブロックが必要なのは、図上の子ノードか、ブロック内に出すべき [*] 遷移があるとき
        var scoped = _c.Transitions.Where(t => t.Scope == info).ToList();
        var childRenderables = info.Children.Where(NeedsRendering).ToList();

        if (childRenderables.Count > 0 || scoped.Count > 0)
        {
            LineAt(depth, head.ToString() + " {");
            foreach (var child in info.Children) WriteNode(child, depth + 1);
            foreach (var t in scoped) WriteTransition(t, depth + 1);
            LineAt(depth, "}");
        }
        else
        {
            LineAt(depth, head.ToString());
        }

        // 内部アクションは別名参照形式（ネスト位置に依存しない）
        foreach (var description in info.Descriptions)
            LineAt(depth, info.Alias + " : " + description);
    }

    private static bool NeedsRendering(StateNodeInfo info)
    {
        if (info.IsAnonymousEndpoint) return false;
        if ((info.Kind == "history" || info.Kind == "deephistory") && info.Parent != null) return false;
        return true;
    }

    private static string StereotypeOf(string kind)
    {
        if (kind == "choice") return "<<choice>>";
        if (kind == "fork") return "<<fork>>";
        if (kind == "join") return "<<join>>";
        if (kind == "entrypoint") return "<<entryPoint>>";
        if (kind == "exitpoint") return "<<exitPoint>>";
        return "";
    }

    private void WriteTransition(StateTransition t, int depth)
    {
        var sb = new StringBuilder();
        sb.Append(RenderEndpoint(t.From)).Append(" ").Append(_o.DefaultArrow)
          .Append(" ").Append(RenderEndpoint(t.To));
        if (t.Label.Length > 0) sb.Append(" : ").Append(t.Label);
        LineAt(depth, sb.ToString());
    }

    private static string RenderEndpoint(StateNodeInfo info)
    {
        if (info.IsAnonymousEndpoint) return "[*]";
        if (info.Kind == "history" && info.Parent != null) return info.Parent.Alias + "[H]";
        if (info.Kind == "deephistory" && info.Parent != null) return info.Parent.Alias + "[H*]";
        return info.Alias;
    }

    private void LineAt(int depth, string text)
    {
        for (var i = 0; i < depth; i++) _sb.Append(_o.IndentUnit);
        _sb.Append(text).Append(_o.NewLine);
    }
}

// ------------------------------------------------------------
//  状態遷移図出力の実行と図種の判別
// ------------------------------------------------------------
public class StateExportRunner
{
    public const string Category = "PlantUML";

    // 図種の判別に使うクラス図側の対応表（既定値で十分なため共有インスタンス）
    private static readonly ClassPlantUmlOptions ClassDefaults = new ClassPlantUmlOptions();

    // ==================== 図種の判別 ====================

    public static bool IsStateDiagram(IDiagram diagram, StatePlantUmlOptions options)
    {
        if (diagram == null || diagram is ISequenceDiagram) return false;
        options = options ?? new StatePlantUmlOptions();

        // 1. EditorType の明示指定が最優先（実機で判明した値の追記先）
        var editor = diagram as IEditor;
        var editorType = editor != null ? (editor.EditorType ?? "") : "";
        if (ContainsIgnoreCase(options.StateEditorTypes, editorType)) return true;
        if (ContainsIgnoreCase(options.NonStateEditorTypes, editorType)) return false;

        // 2. ViewDefinitionName による判別。
        //    実機確認では状態遷移図も EditorType が "ERDiagram"（クラス図と同一）で、
        //    ビュー定義名（"ステートマシン図"）が最も確実な判別材料だった
        var viewName = editor != null ? (editor.ViewDefinitionName ?? "") : "";
        if (ContainsIgnoreCase(options.StateViewDefinitionNames, viewName)) return true;
        if (ContainsIgnoreCase(options.NonStateViewDefinitionNames, viewName)) return false;

        // 3. 内容判定: ノードのメタクラス名（ClassName / 全親クラス名）を完全一致で突き合わせる。
        //    状態系がクラス系以上に多ければ状態遷移図とみなす
        var stateHits = 0;
        var classHits = 0;
        var examined = 0;
        try
        {
            foreach (var s in diagram.Nodes)
            {
                if (examined >= 50) break;
                var node = s as INode;
                if (node == null) continue;
                var model = MetaMap.ModelOf(node);
                if (model == null || model.IsDeleted) continue;
                examined++;

                var names = new List<string>();
                if (!string.IsNullOrEmpty(model.ClassName)) names.Add(model.ClassName);
                var cls = model.Metaclass;
                if (cls != null)
                {
                    try
                    {
                        foreach (var sup in cls.GetAllSuperClasses().Cast<IClass>())
                            if (!string.IsNullOrEmpty(sup.Name)) names.Add(sup.Name);
                    }
                    catch (Exception) { }
                }

                if (names.Any(n => ContainsIgnoreCase(options.StateClassNames, n))) stateHits++;
                else if (names.Any(n => ClassDefaults.KeywordMap.ContainsKey(n))) classHits++;
            }
        }
        catch (Exception) { return false; }

        return stateHits > 0 && stateHits >= classHits;
    }

    private static bool ContainsIgnoreCase(List<string> list, string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var item in list)
            if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // ==================== 対象の決定（クラス図との振り分け）====================

    // 非シーケンスの図エディタを走査し、状態遷移図とクラス図に分類する。
    // クラス図側の対象範囲（ERDiagram / TreeDiagram）は従来から変えない
    public static void CollectSplit(IModel root, bool skipEmpty, StatePlantUmlOptions stateOptions,
                                    ref int skipCount,
                                    out List<ClassDiagramEntry> classTargets,
                                    out List<ClassDiagramEntry> stateTargets)
    {
        classTargets = new List<ClassDiagramEntry>();
        stateTargets = new List<ClassDiagramEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var models = new List<IModel>();
        models.Add(root);
        models.AddRange(root.GetAllChildren().Cast<IModel>());

        foreach (var model in models)
        {
            if (model == null || model.IsDeleted || model.IsProxy) continue;

            foreach (var editor in model.GetEditors())
            {
                if (editor is ISequenceDiagram) continue;
                var diagram = editor as IDiagram;
                if (diagram == null) continue;
                if (!seen.Add(editor.Id)) continue;

                var nodeCount = 0;
                try { foreach (var n in diagram.Nodes) if (n != null) nodeCount++; }
                catch (Exception) { }

                if (skipEmpty && nodeCount == 0)
                {
                    skipCount++;
                    continue;
                }

                var isState = IsStateDiagram(diagram, stateOptions);
                if (!isState && !ClassExportRunner.IsClassDiagramEditor(editor)) continue;

                var representation = editor as IRepresentation;
                var entry = new ClassDiagramEntry
                {
                    Owner = model,
                    Diagram = diagram,
                    EditorId = editor.Id,
                    EditorType = editor.EditorType,
                    ViewDefinitionName = editor.ViewDefinitionName,
                    DiagramName = representation != null && representation.Model != null
                                  ? representation.Model.Name : model.Name,
                };

                if (isState) stateTargets.Add(entry);
                else classTargets.Add(entry);
            }
        }

        classTargets = SortEntries(classTargets);
        stateTargets = SortEntries(stateTargets);
    }

    private static List<ClassDiagramEntry> SortEntries(List<ClassDiagramEntry> entries)
    {
        return entries
            .OrderBy(e => e.OwnerPath, StringComparer.Ordinal)
            .ThenBy(e => e.ViewDefinitionName, StringComparer.Ordinal)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.EditorId, StringComparer.Ordinal)
            .ToList();
    }

    // ==================== 1 枚を出力 ====================

    public static void ExportCurrent(IApplication app, StatePlantUmlOptions options, ExportSettings settings)
    {
        options = options ?? new StatePlantUmlOptions();
        settings = settings ?? new ExportSettings();

        var ui = app.Window.UI;
        var diagram = app.Workspace.CurrentEditor as IDiagram;
        if (diagram == null)
        {
            ui.ShowInformationDialog("アクティブなエディタは状態遷移図ではありません。", Category);
            return;
        }

        var exporter = new StatePlantUmlExporter(diagram, options);
        var uml = exporter.Export();

        OutputPane.Show(app, Category);
        foreach (var line in uml.Replace("\r\n", "\n").Split('\n'))
            app.Output.WriteLine(Category, line);
        WriteWarnings(app, exporter.Warnings);

        if (!settings.SaveToFile) return;

        var baseName = PlantUmlText.SafeFileName(exporter.DiagramName());
        if (baseName.Length == 0) baseName = "state";

        var path = ui.ShowSaveFileDialog(
            "PlantUML ファイルの保存",
            "PlantUML (*.puml)|*.puml|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
            baseName + "_state.puml");
        if (string.IsNullOrEmpty(path)) return;

        SaveText(path, uml);
        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "[saved] " + path);
    }

    // ==================== 配下をまとめて出力 ====================

    // folder が指定されていればダイアログを出さずにそこへ書く（他図種との連続実行用）
    public static int ExportAll(IApplication app, IContext context,
                                StatePlantUmlOptions options, ExportSettings settings,
                                string folder, bool quiet,
                                IModel root, List<ClassDiagramEntry> targets, int skipCount)
    {
        options = options ?? new StatePlantUmlOptions();
        settings = settings ?? new ExportSettings();

        var ui = app.Window.UI;
        context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

        if (targets.Count == 0)
        {
            if (!quiet)
                ui.ShowInformationDialog(
                    "「" + root.Name + "」配下に出力対象の状態遷移図が見つかりませんでした。", Category);
            else
                app.Output.WriteLine(Category, "状態遷移図: 対象なし");
            return 0;
        }

        if (string.IsNullOrEmpty(folder))
        {
            if (settings.Confirm)
            {
                var message = "「" + root.Name + "」配下の状態遷移図 " + targets.Count
                            + " 件を PlantUML に変換します。\n\n続行しますか？";
                if (!ui.ShowConfirmDialog(message, Category)) return 0;
            }
            folder = ui.ShowSelectFolderDialog("PlantUML の出力先フォルダを選択してください");
            if (string.IsNullOrEmpty(folder)) return 0;
        }

        var fileNames = BuildFileNames(targets);

        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "=== 状態遷移図 : " + targets.Count + " 件 ===");

        var okCount = 0;
        var errorCount = 0;

        foreach (var entry in targets)
        {
            try
            {
                var exporter = new StatePlantUmlExporter(entry.Diagram, options);
                var uml = exporter.Export();
                SaveText(System.IO.Path.Combine(folder, fileNames[entry.EditorId]), uml);
                okCount++;
                app.Output.WriteLine(Category, "[ok]    " + entry.Label
                                     + "  (状態 " + exporter.NodeCount
                                     + " / 遷移 " + exporter.TransitionCount + ")");
                WriteWarnings(app, exporter.Warnings);
            }
            catch (Exception ex)
            {
                errorCount++;
                app.Output.WriteLine(Category, "[error] " + entry.Label + " : " + ex.Message);
            }
        }

        app.Output.WriteLine(Category, "=== 状態遷移図 完了 : 成功 " + okCount
                             + " / スキップ " + skipCount + " / エラー " + errorCount + " ===");

        if (!quiet)
            ui.ShowInformationDialog(
                "状態遷移図の PlantUML 出力が完了しました。\n\n"
                + "成功: " + okCount + " 件\n"
                + "エラー: " + errorCount + " 件\n\n"
                + "出力先: " + folder, Category);

        return okCount;
    }

    // ==================== ファイル名 ====================

    // 他の図種と同じフォルダに出しても衝突しないよう _state を付ける
    public static Dictionary<string, string> BuildFileNames(List<ClassDiagramEntry> entries)
    {
        var baseNames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var baseName = PlantUmlText.SafeFileName(entry.Name);
            if (baseName.Length == 0) baseName = "state";
            if (baseName.Length > 100) baseName = baseName.Substring(0, 100);
            baseNames[entry.EditorId] = baseName;
        }

        var duplicated = new HashSet<string>(
            baseNames.Values
                     .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1)
                     .Select(g => g.Key),
            StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in baseNames)
        {
            result[pair.Key] = duplicated.Contains(pair.Value)
                ? pair.Value + "_" + PlantUmlText.ShortHash(pair.Key) + "_state.puml"
                : pair.Value + "_state.puml";
        }
        return result;
    }

    // ==================== ユーティリティ ====================

    private static void WriteWarnings(IApplication app, List<string> warnings)
    {
        if (warnings == null || warnings.Count == 0) return;
        foreach (var warning in warnings)
            app.Output.WriteLine(Category, "[warn]  " + warning);
    }

    private static void SaveText(string path, string text)
    {
        System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}

