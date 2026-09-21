// ============================================================
//  NdMcp / main.cs  (Next Design V3.x スクリプト拡張)
//
//  ★ このファイルは tools/build_main.py が生成する。直接編集しない。
//     編集対象: src/header.cs / src/server.cs（サーバー本体）/ src/classsync.cs（クラス図同期の窓口）
//               AgentReview/main.cs の Part 0 / 4 / 7 / 8（エクスポータ。転記元）
//               ClassImportProbe/sync/ClassSync.cs, ClassSyncRuntime.cs（クラス図同期。転記元）
//
//  Next Design のモデルを MCP（Model Context Protocol）クライアントから
//  読めるようにするための、Next Design 側のサーバー。
//
//    Claude Code ── stdio(MCP) ── Python ブリッジ(bridge/) ── HTTP ── この拡張
//
//  構成:
//    - リボン「NdMcp」タブの「サーバー開始」で 127.0.0.1:3560 に HttpListener を立てる
//      （ハンドラは UI スレッドで同期実行されるため、受付だけ仕掛けて即 return する）
//    - リクエストはスレッドプールで受け、ND API を触る処理は
//      SynchronizationContext.Send() で UI スレッドへ戻してから実行する
//    - 応答は JSON（手書きの Json ライタ。GET + クエリ文字列のみなので JSON パーサは不要）
//    - モデル読み出しは読み取り専用。書き込みは /class-sync/trial と /class-sync/apply だけ
//
//  API（すべて GET。path= はモデルパス、id= はモデル ID。両方空ならプロジェクト）:
//    /ping                        生存確認（ND API 非依存）
//    /thread                      スレッド診断
//    /project                     プロジェクト概要と直下モデル
//    /tree?path=&id=&depth=2      モデルツリー
//    /model?path=&id=             モデル詳細（全フィールド）
//    /search?q=&metaclass=&limit= 名前の部分一致検索
//    /markdown?path=&id=          サブツリーを design.md 形式の Markdown で返す
//    /export?path=&id=&out=       design.md + diagrams\*.puml + _index.md をフォルダへ書き出す
//
//  クラス図同期（ClassImportProbe の同期本体を転記。この API だけがモデルへ書き込む）:
//    GET  /class-sync/editors?path=&id=          モデルに紐づく図の一覧
//    GET  /class-sync/current?path=&id=&editor=  クラス図を PlantUML（PlantUmlTool 互換の書式）で返す
//    POST /class-sync/preview  {path|id, editor?, plantuml|file}  比較のみ
//    POST /class-sync/trial    {path|id, editor?, plantuml|file}  一時適用して照合し、必ず取り消す
//    POST /class-sync/apply    {path|id, editor?, plantuml|file}  確定する（Undo 可）
//
//  設定: %USERPROFILE%\.nd-mcp\config.ini（port= / exportDir=）
//  ログ: %USERPROFILE%\.nd-mcp\server.log
// ============================================================

using NextDesign.Core;
using NextDesign.Desktop;
using NextDesign.Extension;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

public sealed class ChangeRecord
{
    public string Key = "", Parent = "", Name = "", Kind = "", Path = "", File = "", Content = "";
}
public static class ReviewSnapshot
{
    public static string Cell(string value)
    {
        return (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("|", "&#124;").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
    }
}
// ============================================================
//  ここから AgentReview/main.cs の Part 0 / 4 / 7 / 8 の転記（tools/build_main.py が生成）
// ============================================================

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

// 所有フィールドの型から図グループを判別する。明示的な対応表があれば優先する。
public class DiagramGroupRules
{
    private readonly Dictionary<string, HashSet<string>> _types = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    public readonly List<string> Warnings = new List<string>();

    public static DiagramGroupRules Load(string file)
    {
        var rules = new DiagramGroupRules();
        if (string.IsNullOrWhiteSpace(file)) return rules;
        try
        {
            if (!Path.IsPathRooted(file) || !string.Equals(Path.GetFullPath(file), file, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("対応表には正規化した絶対パスを指定してください。");
            var assigned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var eq = line.IndexOf('=');
                if (eq < 1) throw new InvalidDataException("対応表は key=value 形式で指定してください。");
                var key = line.Substring(0, eq).Trim();
                if (key != "sequence" && key != "class" && key != "state")
                    throw new InvalidDataException("対応表の種別が不正です: " + key);
                if (rules._types.ContainsKey(key)) throw new InvalidDataException("対応表の種別が重複しています: " + key);
                var types = new HashSet<string>(StringComparer.Ordinal);
                foreach (var value in line.Substring(eq + 1).Split(';'))
                {
                    var type = value.Trim();
                    if (type.Length == 0 || type.IndexOf('.') < 1 || type.EndsWith(".", StringComparison.Ordinal))
                        throw new InvalidDataException("空でないメタクラス完全名を指定してください。");
                    if (!assigned.Add(type)) throw new InvalidDataException("対応表のメタクラスが重複しています。");
                    types.Add(type);
                }
                rules._types.Add(key, types);
            }
            if (rules._types.Count == 0) throw new InvalidDataException("対応表が空です。");
        }
        catch (Exception ex)
        {
            rules._types.Clear();
            rules.Warnings.Add("図グループ対応表: " + ex.Message + " 所有フィールドから自動判別します。");
        }
        return rules;
    }

    public bool Matches(string kind, string fullName)
    {
        HashSet<string> types;
        return fullName != null && _types.TryGetValue(kind, out types) && types.Contains(fullName);
    }

    public List<IModel> Directories(IModel model, string kind, List<string> warnings)
    {
        var chain = new List<IModel>(); // 図の親から上へ。図モデル自身はファイル名に使う。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            seen.Add(model.Id);
            for (var owner = model.Owner; owner != null; owner = owner.Owner)
            {
                if (chain.Count >= 1024 || !seen.Add(owner.Id))
                    throw new InvalidDataException("所有関係の循環または階層上限を検出しました。");
                chain.Add(owner);
            }
            var groupIndex = -1;
            for (var i = 0; i < chain.Count; i++)
                if (chain[i].Metaclass != null && Matches(kind, chain[i].Metaclass.FullName)) groupIndex = i;
            if (groupIndex < 0 && model.Metaclass != null)
            {
                // グループは図のメタクラスを所有フィールドの型として宣言している。
                // 表示名・型名の接尾辞には依存しない。参照フィールドは対象外。
                var diagramType = model.Metaclass.FullName;
                if (!string.IsNullOrEmpty(diagramType))
                    for (var i = 0; i < chain.Count; i++)
                    {
                        var cls = chain[i].Metaclass;
                        if (cls == null) continue;
                        try
                        {
                            if (cls.GetFields().Cast<IField>().Any(f => f != null && f.IsEmbedded
                                && !f.IsReference && f.TypeClass != null
                                && string.Equals(f.TypeClass.FullName, diagramType, StringComparison.Ordinal)))
                                groupIndex = i;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add("モデル「" + chain[i].Name + "」: グループ判別用フィールドの取得に失敗: " + ex.Message);
                        }
                    }
            }
            if (groupIndex >= 0)
            {
                var result = chain.Take(groupIndex + 1).ToList();
                result.Reverse();
                return result;
            }
        }
        catch (Exception ex)
        {
            warnings.Add("図「" + model.Name + "」: 祖先の取得に失敗: " + ex.Message);
        }
        // 判別できなくても選択モデルからの長い階層には戻さない。
        warnings.Add("図「" + model.Name + "」: グループを特定できません。"
            + (chain.Count > 0 ? "図の直接の親だけを保存先に使用します。" : "種別フォルダ直下に出力します。"));
        return chain.Take(1).ToList();
    }
}

// OS に書き込む前に、ディレクトリとファイルを同じ名前空間で割り当てる。
public class DiagramPathNode
{
    public string Id, Name, Suffix, Assigned;
    public bool IsFile;
    public DiagramPathNode Parent;
    public readonly List<DiagramPathNode> Children = new List<DiagramPathNode>();
    public string RelativePath()
    {
        return Parent == null ? Assigned : Parent.RelativePath() + "/" + Assigned;
    }
}

public static class DiagramPaths
{
    public static string Segment(string name)
    {
        var result = AgentText.SafeFileName(name).TrimEnd(' ', '.');
        if (result.Length == 0) result = "unnamed";
        if (Regex.IsMatch(result, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase))
            result = "_" + result;
        return result;
    }

    public static string Hash(string id)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(id))).Replace("-", "").ToLowerInvariant();
    }

    public static DiagramPathNode Directory(DiagramPathNode parent, string id, string name)
    {
        var node = parent.Children.FirstOrDefault(n => !n.IsFile && n.Id == id);
        if (node == null)
        {
            node = new DiagramPathNode { Id = id, Name = Segment(name), Suffix = "", Parent = parent };
            parent.Children.Add(node);
        }
        return node;
    }

    public static void Allocate(DiagramPathNode parent)
    {
        var counts = parent.Children.GroupBy(n => n.Name + n.Suffix, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(counts.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var node in parent.Children.OrderBy(n => n.Id, StringComparer.Ordinal).ThenBy(n => n.IsFile))
        {
            var original = node.Name + node.Suffix;
            node.Assigned = original;
            if (counts[original] > 1)
            {
                var hash = Hash((node.IsFile ? "file:" : "dir:") + node.Id);
                var length = 8;
                while (true)
                {
                    var candidate = node.Name + "_" + hash.Substring(0, length) + node.Suffix;
                    if (used.Add(candidate)) { node.Assigned = candidate; break; }
                    if (length == hash.Length) throw new InvalidDataException("図の保存先を一意に割り当てられません。");
                    length = Math.Min(length + 4, hash.Length);
                }
            }
            Allocate(node);
        }
    }

    public static string Link(string relativePath)
    {
        // .NET Framework の URI 設定によっては括弧が残るため、Markdown 用に明示処理する。
        return string.Join("/", relativePath.Split('/').Select(s => Uri.EscapeDataString(s)
            .Replace("(", "%28").Replace(")", "%29").Replace("'", "%27").Replace("*", "%2A").Replace("!", "%21")).ToArray());
    }

    public static string Label(string text)
    {
        return (text ?? "").Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]")
            .Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }
}

public class PendingDiagram
{
    public string Token, Name, Uml;
    public ChangeRecord Comparison;
    public DiagramPathNode Node;
}

public class MarkdownExporter
{
    private readonly MarkdownExportOptions _options;
    private readonly HashSet<string> _visited = new HashSet<string>(StringComparer.Ordinal);
    private StringBuilder _sb;

    // 図の .puml 出力（null なら図は出力しない）
    private readonly string _diagramDir;
    private readonly HashSet<string> _seenEditors = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<PendingDiagram> _pending = new List<PendingDiagram>();
    private readonly DiagramGroupRules _groupRules;
    private DiagramPathNode _pathRoot;
    private readonly HashSet<string> _seenDiagramWarnings = new HashSet<string>(StringComparer.Ordinal);
    private readonly PlantUmlOptions _seqOptions = new PlantUmlOptions();
    private readonly ClassPlantUmlOptions _classOptions = new ClassPlantUmlOptions();
    private readonly StatePlantUmlOptions _stateOptions = new StatePlantUmlOptions();

    public int ModelCount;
    public int DiagramCount;
    public int SkippedModelCount;   // 図の構成要素としてテキスト出力から除外したモデル数
    public List<string> Warnings = new List<string>();
    public List<ChangeRecord> Comparison = new List<ChangeRecord>();
    public readonly List<string> SkippedDiagrams = new List<string>();
    public List<string> IndexRows = new List<string>();   // _index.md 用「| 図名 | 種別 | ファイル | モデルパス |」

    public MarkdownExporter(MarkdownExportOptions options, string diagramDir, DiagramGroupRules groupRules = null)
    {
        _options = options ?? new MarkdownExportOptions();
        _diagramDir = diagramDir;
        _groupRules = groupRules ?? DiagramGroupRules.Load("");
        RegisterDesideMaps(_classOptions);
    }

    // DeSIDE プロファイル向けの対応表（既定表は Part 7 側なので触らず、ここで追記する）。
    // 実機の warn（対応表に無いメタクラス・フィールド名）から採録
    private static void RegisterDesideMaps(ClassPlantUmlOptions o)
    {
        // 型定義の構成要素は属性として出力する（従来の既定動作を明示して警告を止める）
        var attrs = new[] { "StructureType", "PointerType", "NumericalType", "ArrayType",
            "EnumeratorType", "StringType", "ImplementationDataType", "BooleanType", "VoidType" };
        foreach (var name in attrs)
            if (!o.MemberKindMap.ContainsKey(name)) o.MemberKindMap[name] = "attribute";

        // 双方向フィールドは逆側を逆向き矢印にする（フィールド名からの推定。実図と違えば要調整）
        var links = new Dictionary<string, string>
        {
            { "SuperClasses", "--|>" }, { "SubClasses", "<|--" },
            { "Whole", "--*" }, { "Parts", "*--" },
            { "Related", "-->" }, { "RelateFrom", "<--" },
            { "Children", "o--" },
        };
        foreach (var pair in links)
            if (!o.LinkMap.ContainsKey(pair.Key)) o.LinkMap[pair.Key] = pair.Value;
    }

    public string Export(IModel root)
    {
        var nl = _options.NewLine;
        _sb = new StringBuilder();
        _visited.Clear();
        _seenEditors.Clear();
        _seenDiagramWarnings.Clear();
        _pending.Clear();
        IndexRows.Clear();
        Comparison.Clear();
        SkippedDiagrams.Clear();
        DiagramCount = 0;
        SkippedModelCount = 0;
        _pathRoot = new DiagramPathNode { Id = "diagrams", Assigned = "diagrams" };
        ModelCount = 0;
        Warnings.Clear();
        if (_diagramDir != null) Warnings.AddRange(_groupRules.Warnings);

        // 件数をプリアンブルに載せるため、本文を先に組み立てる
        WriteModel(root, 0, null);
        var body = WriteDiagramFiles(_sb.ToString());

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
            // モデルパスは HTML コメントで埋め込む。レンダリング表示には出ないため
            // ノイズにならず、レビューエージェントが指摘の対象参照
            // （Next Design のモデルパス）として引用できる
            _sb.Append(new string('#', level)).Append(' ').Append(heading);
            var shortCls = ShortClassName(m);
            if (shortCls.Length > 0) _sb.Append("（").Append(shortCls).Append("）");
            _sb.Append(nl);
            _sb.Append("<!-- modelpath: ").Append(PathOf(m)).Append(" -->").Append(nl);
            _sb.Append(nl);

            var fieldStart = _sb.Length;
            WriteFields(m);
            Comparison.Add(new ChangeRecord { Key = "model:" + m.Id, Parent = m.Owner == null ? "" : m.Owner.Id,
                Name = m.Name, Kind = m.Metaclass == null ? "" : m.Metaclass.FullName, Path = PathOf(m),
                Content = _sb.ToString(fieldStart, _sb.Length - fieldStart) });

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
                        if (!seq.Lifelines.Cast<ILifelineShape>().Any()) { RecordSkippedDiagram(seq.Model ?? m, editor.Id, (seq.Model ?? m).Name, "ライフラインが取得できないため図の内容は未確認"); continue; }
                        var seqName = seq.Model != null && !string.IsNullOrEmpty(seq.Model.Name)
                            ? seq.Model.Name
                            : (string.IsNullOrEmpty(seq.ViewDefinitionName) ? "Sequence" : seq.ViewDefinitionName);
                        var uml = new SequencePlantUmlExporter(seq, _seqOptions).Export();
                        var owner = seq.Model ?? m;
                        var file = SaveDiagram(seqName, "_seq", "シーケンス図", "sequence", uml, owner, editor.Id);
                        refs.Add("- 図: [" + DiagramPaths.Label(seqName) + "](" + file + ")（シーケンス図）");
                        AddIndexRow(seqName, "シーケンス図", file, owner);
                        continue;
                    }

                    var diagram = editor as IDiagram;
                    if (diagram == null) continue;

                    var representation = editor as IRepresentation;
                    var diagramOwner = representation != null && representation.Model != null ? representation.Model : m;
                    var diagramName = representation != null && representation.Model != null
                        && !string.IsNullOrEmpty(representation.Model.Name)
                        ? representation.Model.Name : (m.Name ?? "Diagram");

                    if (StateExportRunner.IsStateDiagram(diagram, _stateOptions))
                    {
                        skipChildren = true;
                        var exporter = new StatePlantUmlExporter(diagram, _stateOptions);
                        var uml = exporter.Export();
                        AddDiagramWarnings(diagramName, exporter.Warnings);
                        if (exporter.NodeCount == 0)
                        {
                            RecordSkippedDiagram(diagramOwner, editor.Id, diagramName, "対応可能なノードが取得できないため図の内容は未確認");
                            continue;
                        }
                        var file = SaveDiagram(diagramName, "_state", "状態遷移図", "state", uml, diagramOwner, editor.Id);
                        refs.Add("- 図: [" + DiagramPaths.Label(diagramName) + "](" + file + ")（状態遷移図）");
                        AddIndexRow(diagramName, "状態遷移図", file, diagramOwner);
                    }
                    else if (ClassExportRunner.IsClassDiagramEditor(editor))
                    {
                        // クラス図は図要素＝クラス設計そのものなので子の再帰は続ける
                        var exporter = new ClassPlantUmlExporter(diagram, _classOptions);
                        var uml = exporter.Export();
                        AddDiagramWarnings(diagramName, exporter.Warnings);
                        if (exporter.NodeCount == 0)
                        {
                            RecordSkippedDiagram(diagramOwner, editor.Id, diagramName, "対応可能なノードが取得できないため図の内容は未確認");
                            continue;
                        }
                        var file = SaveDiagram(diagramName, "_class", "クラス図", "class", uml, diagramOwner, editor.Id);
                        refs.Add("- 図: [" + DiagramPaths.Label(diagramName) + "](" + file + ")（クラス図）");
                        AddIndexRow(diagramName, "クラス図", file, diagramOwner);
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

    private void RecordSkippedDiagram(IModel owner, string editorId, string name, string reason)
    {
        SkippedDiagrams.Add("図「" + name + "」 / " + PathOf(owner) + " : " + reason);
        Comparison.Add(new ChangeRecord { Key = "diagram:" + owner.Id + ":" + editorId, Parent = owner.Id,
            Name = name, Kind = "unverified-diagram", Path = PathOf(owner), Content = reason });
    }

    // 出力予定を集めてからパスを確定する。本文と索引の仮参照は書込み成功後に置換する。
    private string SaveDiagram(string name, string suffix, string kindFolder, string kind, string uml, IModel owner, string editorId)
    {
        var parent = DiagramPaths.Directory(_pathRoot, kind, kindFolder);
        foreach (var model in _groupRules.Directories(owner, kind, Warnings))
            parent = DiagramPaths.Directory(parent, model.Id, model.Name);
        var node = new DiagramPathNode { Id = editorId, Name = DiagramPaths.Segment(name),
            Suffix = suffix + ".puml", IsFile = true, Parent = parent };
        parent.Children.Add(node);
        var comparison = new ChangeRecord { Key = "diagram:" + owner.Id + ":" + editorId, Parent = owner.Id,
            Name = name, Kind = kind, Path = PathOf(owner), Content = uml };
        Comparison.Add(comparison);
        var token = "ND_DIAGRAM_" + Guid.NewGuid().ToString("N");
        _pending.Add(new PendingDiagram { Token = token, Name = name, Uml = uml, Node = node, Comparison = comparison });
        return token;
    }

    private string WriteDiagramFiles(string body)
    {
        DiagramPaths.Allocate(_pathRoot);
        foreach (var diagram in _pending)
        {
            try
            {
                var relative = diagram.Node.RelativePath();
                diagram.Comparison.File = relative;
                var path = Path.Combine(_diagramDir, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, diagram.Uml, new UTF8Encoding(false));
                var link = DiagramPaths.Link(relative);
                body = body.Replace(diagram.Token, link);
                for (var i = 0; i < IndexRows.Count; i++)
                    IndexRows[i] = IndexRows[i].Replace(diagram.Token + "_LABEL", DiagramPaths.Label(relative))
                        .Replace(diagram.Token, link);
                DiagramCount++;
            }
            catch (Exception ex)
            {
                Warnings.Add("図「" + diagram.Name + "」: " + diagram.Node.RelativePath() + " の書込みに失敗: " + ex.Message);
                body = Regex.Replace(body, @"(?m)^- 図: [^\r\n]*" + diagram.Token + @"[^\r\n]*(?:\r?\n|$)", "");
                IndexRows.RemoveAll(row => row.Contains(diagram.Token));
            }
        }
        return body;
    }

    // エクスポータの警告に図名を付けて写す。同一内容の繰り返しは初出だけ残す
    // （対応表に無いメタクラス等の警告は図の枚数分だけ重複するため）
    private void AddDiagramWarnings(string diagramName, List<string> warnings)
    {
        foreach (var warning in warnings)
        {
            // ノード0件の図はスキップ行で報告するため、エクスポータ側の同旨の警告は写さない
            if (warning == "図上にモデルと対応するノードがありません。") continue;
            if (!_seenDiagramWarnings.Add(warning)) continue;
            Warnings.Add("図「" + diagramName + "」: " + warning);
        }
    }

    private void AddIndexRow(string name, string kind, string file, IModel owner)
    {
        IndexRows.Add("| " + DiagramPaths.Label(name) + " | " + kind + " | [" + file + "_LABEL](" + file + ")"
            + " | " + DiagramPaths.Label(PathOf(owner)) + " |");
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

                // 所有（クラス型）は子セクションで出す（二重化回避）。
                // String 等のプリミティブにも IsEmbedded が立つプロファイルがあるため
                // クラス型（TypeClass あり）に限定してスキップする
                if (f.IsEmbedded && f.TypeClass != null) continue;

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
                    // 多値プリミティブ等で GetFieldString が空になるフィールドの保険
                    if (string.IsNullOrEmpty(value) || value.Trim().Length == 0)
                        value = JoinScalarValues(m, f.Name);
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
        // RichText として取得できない環境・フィールドの保険（文字列取得に落とす）
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
        {
            try { text = m.GetFieldString(f.Name); }
            catch (Exception) { }
        }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
            text = JoinScalarValues(m, f.Name);
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) return false;

        _sb.Append("**").Append(f.Name).Append("**:").Append(nl).Append(nl);
        _sb.Append(text.Replace("\r\n", "\n").Replace("\r", "\n").Trim('\n')).Append(nl);
        _sb.Append(nl);
        return true;
    }

    // GetFieldValues を列挙し、モデル以外のスカラー値を ToString で連結する
    // （GetFieldString が空を返す多値・特殊型フィールドの最終フォールバック）
    private static string JoinScalarValues(IModel m, string fieldName)
    {
        var values = new List<string>();
        try
        {
            foreach (var v in m.GetFieldValues(fieldName))
            {
                if (v == null || v is IModel) continue;
                var s = v.ToString();
                if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0) values.Add(s);
            }
        }
        catch (Exception) { }
        return values.Count > 0 ? string.Join(", ", values.ToArray()) : null;
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

// 選択モデル配下の実フィールド構成・値の所在を再帰ダンプする診断ヘルパ（ProbeExportTarget 用）。
// design.md に出ない情報がどの取得経路（GetFieldString / GetRichTextField / GetFieldValues）に
// あるのかをプロファイル依存で実測する
public class ExportProbe
{
    public const int MaxModels = 200;
    public int ModelCount;
    public bool Truncated;
    private readonly StringBuilder _sb = new StringBuilder();
    private readonly HashSet<string> _visited = new HashSet<string>(StringComparer.Ordinal);

    public string Text() { return _sb.ToString(); }

    public void Dump(IModel root)
    {
        _sb.Append("Next Design エクスポート診断 ")
           .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n').Append('\n');
        DumpModel(root, 0);
    }

    private void DumpModel(IModel m, int depth)
    {
        if (m == null || Truncated) return;
        if (!_visited.Add(m.Id)) return;
        if (ModelCount >= MaxModels)
        {
            Truncated = true;
            _sb.Append("...（上限 ").Append(MaxModels).Append(" 件で打ち切り。より深い階層は対象モデルを選び直して実行）\n");
            return;
        }
        ModelCount++;

        var indent = new string(' ', depth * 2);
        var cls = m.Metaclass;
        _sb.Append(indent).Append("■ ").Append(string.IsNullOrEmpty(m.Name) ? "(無名)" : m.Name)
           .Append("  [Class=").Append(m.ClassName ?? "")
           .Append(" Meta=").Append(cls != null ? cls.FullName : "(null)").Append("]\n");

        if (cls != null)
        {
            List<IField> fields;
            try { fields = cls.GetFields().Cast<IField>().ToList(); }
            catch (Exception ex)
            {
                fields = new List<IField>();
                _sb.Append(indent).Append("  フィールド一覧の取得失敗: ").Append(ex.Message).Append('\n');
            }
            foreach (var f in fields)
            {
                if (f == null) continue;
                try { DumpField(m, f, indent); }
                catch (Exception ex)
                {
                    _sb.Append(indent).Append("  - ").Append(f.Name)
                       .Append(" : ダンプ失敗 ").Append(ex.Message).Append('\n');
                }
            }
        }

        try
        {
            foreach (var editor in m.GetEditors())
            {
                if (editor == null) continue;
                var defName = "";
                try
                {
                    var def = editor.EditorDefinition;
                    if (def != null) defName = def.DisplayName ?? def.Name ?? "";
                }
                catch (Exception) { }
                _sb.Append(indent).Append("  [editor] ").Append(editor.EditorType)
                   .Append(defName.Length > 0 ? " 定義=" + defName : "").Append('\n');
            }
        }
        catch (Exception) { }

        try
        {
            foreach (var child in m.GetChildren().Cast<IModel>().ToList())
                DumpModel(child, depth + 1);
        }
        catch (Exception ex)
        {
            _sb.Append(indent).Append("  子モデルの取得失敗: ").Append(ex.Message).Append('\n');
        }
    }

    private void DumpField(IModel m, IField f, string indent)
    {
        _sb.Append(indent).Append("  - ").Append(f.Name)
           .Append(" : Type=").Append(f.Type)
           .Append(" Embedded=").Append(f.IsEmbedded)
           .Append(" Reference=").Append(f.IsReference)
           .Append(" 多重度=").Append(f.LowerBound).Append("..").Append(f.UpperBound).Append('\n');

        // 所有クラス型の中身は子モデルの行として出る。埋め込みスカラーは値をダンプする
        if (f.IsEmbedded && f.TypeClass != null) return;

        var got = false;
        try
        {
            var s = m.GetFieldString(f.Name);
            if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0)
            {
                _sb.Append(indent).Append("      [string] ").Append(Clip(s, 80)).Append('\n');
                got = true;
            }
        }
        catch (Exception ex)
        {
            _sb.Append(indent).Append("      [string] 取得失敗: ").Append(ex.Message).Append('\n');
        }

        if (f.Type == "RichText")
        {
            try
            {
                var html = m.GetRichTextField(f.Name, "html");
                _sb.Append(indent).Append("      [richtext html] ")
                   .Append(string.IsNullOrEmpty(html) ? "(空)" : Clip(html, 120)).Append('\n');
                if (!string.IsNullOrEmpty(html)) got = true;
            }
            catch (Exception ex)
            {
                _sb.Append(indent).Append("      [richtext html] 取得失敗: ").Append(ex.Message).Append('\n');
            }
            try
            {
                var text = m.GetRichTextField(f.Name, "text");
                _sb.Append(indent).Append("      [richtext text] ")
                   .Append(string.IsNullOrEmpty(text) ? "(空)" : Clip(text, 120)).Append('\n');
                if (!string.IsNullOrEmpty(text)) got = true;
            }
            catch (Exception ex)
            {
                _sb.Append(indent).Append("      [richtext text] 取得失敗: ").Append(ex.Message).Append('\n');
            }
        }

        if (!got)
        {
            try
            {
                foreach (var v in m.GetFieldValues(f.Name))
                {
                    if (v == null) continue;
                    var model = v as IModel;
                    _sb.Append(indent).Append("      [value ").Append(v.GetType().Name).Append("] ")
                       .Append(model != null
                            ? (string.IsNullOrEmpty(model.Name) ? "(無名)" : model.Name)
                            : Clip(v.ToString(), 80)).Append('\n');
                }
            }
            catch (Exception ex)
            {
                _sb.Append(indent).Append("      [values] 取得失敗: ").Append(ex.Message).Append('\n');
            }
        }
    }

    private static string Clip(string s, int max)
    {
        s = (s ?? "").Replace("\r", "").Replace("\n", " ");
        return s.Length > max ? s.Substring(0, max) + "..." : s;
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

        if (kind == "destroy" && receiver != null) DestroyLifeline(receiver);
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
        if (l == null || _destroyed.Contains(l.Id)) return;
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
        if (l == null || _destroyed.Contains(l.Id)) return false;

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
        DestroyLifeline(l);
    }

    private void DestroyLifeline(ILifelineShape l)
    {
        if (!_destroyed.Add(l.Id)) return;
        var alias = AliasOf(l);
        // destroy が実行バーを終了する。後続イベントや末尾処理で再終了しない。
        _activeCount.Remove(alias);
        Line("destroy " + alias);
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

public static class DesignArtifactWriter
{
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

// ============================================================
//  Part S / MCP ブリッジ用 HTTP サーバー（NdMcp 固有部。src/server.cs）
// ============================================================

// ------------------------------------------------------------
//  コマンドハンドラ（UI スレッドで同期実行される）
// ------------------------------------------------------------

public void StartNdMcpServer(ICommandContext context, ICommandParams parameters)
{
    var category = "NdMcp";
    var app = context.App;
    try
    {
        if (NdMcpServer.IsRunning)
        {
            app.Output.WriteLine(category, "[info] サーバーは既に稼働中です: " + NdMcpServer.BaseUrl());
            return;
        }

        // UI スレッド（＝このハンドラのスレッド）の情報を捕獲する
        NdMcpServer.UiThreadId = Thread.CurrentThread.ManagedThreadId;
        NdMcpServer.SyncContext = SynchronizationContext.Current;
        NdMcpServer.App = app;
        if (NdMcpServer.SyncContext == null)
        {
            app.Output.WriteLine(category, "[error] SynchronizationContext.Current が null のため、UI スレッドへ戻せません。サーバーは開始しません。");
            app.Window.UI.ShowInformationDialog(
                "SynchronizationContext が取得できないため、この環境ではサーバーを開始できません。\n"
                + "（出力ウィンドウの NdMcp カテゴリを添えて報告してください）", category);
            return;
        }

        var config = NdMcpConfig.Load();
        NdMcpServer.Port = config.Port;
        NdMcpServer.ExportDir = config.ExportDir;
        NdMcpServer.Start();

        OutputPane.Show(app, category);
        app.Output.WriteLine(category, "=== NdMcp サーバー開始 ===");
        app.Output.WriteLine(category, "[info] URL      : " + NdMcpServer.BaseUrl());
        app.Output.WriteLine(category, "[info] UI thread: " + NdMcpServer.UiThreadId + " / " + NdMcpServer.SyncContext.GetType().FullName);
        app.Output.WriteLine(category, "[info] 出力先   : " + NdMcpServer.ExportDir);
        app.Output.WriteLine(category, "[info] ログ     : " + NdMcpServer.LogPath());
        app.Output.WriteLine(category, "[info] 確認     : curl " + NdMcpServer.BaseUrl() + "/ping");
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] サーバー開始に失敗: " + ex.ToString());
        app.Window.UI.ShowInformationDialog("サーバー開始に失敗しました。\n\n" + ex.Message, category);
    }
}

// HTTP から UI スレッドへ戻した後、正式なコマンドとして呼び出す。
// エディタ取得設定の有効期間内に、モデル取得からファイル出力まで完了させる。
public void ExecuteNdMcpRequest(ICommandContext context, ICommandParams parameters)
{
    var request = parameters[0] as NdMcpCommandRequest;
    if (request == null) throw new ArgumentException("NdMcp の要求がありません。");
    try
    {
        context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;
        request.Result = request.Work(context.App);
    }
    catch (Exception ex) { request.Error = ex; }
    finally { request.Completed = true; }
}

public class NdMcpCommandRequest
{
    public Func<IApplication, object> Work;
    public object Result;
    public Exception Error;
    public bool Completed;
}

public void StopNdMcpServer(ICommandContext context, ICommandParams parameters)
{
    var category = "NdMcp";
    var app = context.App;
    try
    {
        if (!NdMcpServer.IsRunning)
        {
            app.Output.WriteLine(category, "[info] サーバーは稼働していません。");
            return;
        }
        NdMcpServer.Stop();
        app.Output.WriteLine(category, "[info] サーバーを停止しました（累計 " + NdMcpServer.RequestCount + " 件受信）。");
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] 停止に失敗: " + ex.ToString());
    }
}

public void ShowNdMcpStatus(ICommandContext context, ICommandParams parameters)
{
    var category = "NdMcp";
    var app = context.App;
    try
    {
        OutputPane.Show(app, category);
        app.Output.WriteLine(category, "=== NdMcp 状態 ===");
        app.Output.WriteLine(category, "稼働    : " + (NdMcpServer.IsRunning ? "稼働中 " + NdMcpServer.BaseUrl() : "停止"));
        app.Output.WriteLine(category, "受信数  : " + NdMcpServer.RequestCount);
        app.Output.WriteLine(category, "UI thread: " + NdMcpServer.UiThreadId + " / "
            + (NdMcpServer.SyncContext != null ? NdMcpServer.SyncContext.GetType().FullName : "(null)"));
        app.Output.WriteLine(category, "設定    : " + NdMcpConfig.ConfigPath());
        foreach (var line in NdMcpServer.RecentLog())
            app.Output.WriteLine(category, "[log] " + line);
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] 状態確認に失敗: " + ex.ToString());
    }
}

public void OpenNdMcpConfig(ICommandContext context, ICommandParams parameters)
{
    var category = "NdMcp";
    var app = context.App;
    try
    {
        var path = NdMcpConfig.EnsureFile();
        Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = "\"" + path + "\"", UseShellExecute = true });
        app.Output.WriteLine(category, "[info] 設定ファイルを開きました: " + path + "（変更はサーバー再開始で反映）");
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] 設定を開けません: " + ex.ToString());
    }
}

// ------------------------------------------------------------
//  設定（%USERPROFILE%\.nd-mcp\config.ini、key=value 形式）
// ------------------------------------------------------------

public class NdMcpConfig
{
    public int Port = 3560;
    public string ExportDir;

    public static string ConfigDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nd-mcp");
    }

    public static string ConfigPath() { return Path.Combine(ConfigDir(), "config.ini"); }

    public static NdMcpConfig Load()
    {
        var config = new NdMcpConfig();
        config.ExportDir = Path.Combine(ConfigDir(), "export");
        var path = ConfigPath();
        if (!File.Exists(path)) return config;
        foreach (var pair in IniFile.Read(path))
        {
            switch (pair.Key)
            {
                case "port":
                    int port;
                    if (int.TryParse(pair.Value, out port) && port > 0 && port < 65536) config.Port = port;
                    break;
                case "exportDir":
                    if (pair.Value.Length > 0) config.ExportDir = pair.Value;
                    break;
            }
        }
        return config;
    }

    // 設定ファイルが無ければ既定値入りで作る
    public static string EnsureFile()
    {
        Directory.CreateDirectory(ConfigDir());
        var path = ConfigPath();
        if (!File.Exists(path))
        {
            var nl = "\r\n";
            var sb = new StringBuilder();
            sb.Append("# NdMcp 設定（変更後は「サーバー停止」→「サーバー開始」で反映）").Append(nl);
            sb.Append("# 待ち受けポート。Python ブリッジ側の ND_MCP_URL と合わせる").Append(nl);
            sb.Append("port=3560").Append(nl);
            sb.Append("# /export の既定出力先").Append(nl);
            sb.Append("exportDir=" + Path.Combine(ConfigDir(), "export")).Append(nl);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        return path;
    }
}

// key=value 形式の読み込み（# 始まりと空行は無視）
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

// ------------------------------------------------------------
//  JSON ライタ（string / bool / 数値 / null / IDictionary / IEnumerable のみ）
// ------------------------------------------------------------

public static class Json
{
    public static string Write(object value)
    {
        var sb = new StringBuilder();
        WriteValue(sb, value);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, object value)
    {
        if (value == null) { sb.Append("null"); return; }
        var s = value as string;
        if (s != null) { WriteString(sb, s); return; }
        if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
        if (value is int || value is long || value is short || value is byte)
        { sb.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture)); return; }
        if (value is double || value is float || value is decimal)
        { sb.Append(Convert.ToDouble(value).ToString("R", CultureInfo.InvariantCulture)); return; }
        var dict = value as IDictionary;
        if (dict != null)
        {
            sb.Append('{');
            var first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, entry.Key.ToString());
                sb.Append(':');
                WriteValue(sb, entry.Value);
            }
            sb.Append('}');
            return;
        }
        var list = value as IEnumerable;
        if (list != null)
        {
            sb.Append('[');
            var first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
            return;
        }
        WriteString(sb, value.ToString());
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }
}

// 順序を保つ JSON オブジェクト（Dictionary は列挙順が保証されないため）
public class JsonObject : IDictionary
{
    private readonly List<string> _keys = new List<string>();
    private readonly Dictionary<string, object> _map = new Dictionary<string, object>(StringComparer.Ordinal);

    public JsonObject Set(string key, object value)
    {
        if (!_map.ContainsKey(key)) _keys.Add(key);
        _map[key] = value;
        return this;
    }

    public object this[object key]
    {
        get { object v; return _map.TryGetValue((string)key, out v) ? v : null; }
        set { Set((string)key, value); }
    }
    public void Add(object key, object value) { Set((string)key, value); }
    public bool Contains(object key) { return _map.ContainsKey((string)key); }
    public void Remove(object key) { var k = (string)key; if (_map.Remove(k)) _keys.Remove(k); }
    public void Clear() { _keys.Clear(); _map.Clear(); }
    public ICollection Keys { get { return _keys; } }
    public ICollection Values { get { return _keys.Select(k => _map[k]).ToList(); } }
    public bool IsReadOnly { get { return false; } }
    public bool IsFixedSize { get { return false; } }
    public int Count { get { return _keys.Count; } }
    public object SyncRoot { get { return this; } }
    public bool IsSynchronized { get { return false; } }
    public void CopyTo(Array array, int index) { throw new NotSupportedException(); }
    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    public IDictionaryEnumerator GetEnumerator()
    {
        var ordered = new System.Collections.Specialized.OrderedDictionary();
        foreach (var k in _keys) ordered.Add(k, _map[k]);
        return ordered.GetEnumerator();
    }
}

// ------------------------------------------------------------
//  HTTP サーバー
// ------------------------------------------------------------

public class NdMcpHttpError : Exception
{
    public int Status;
    public NdMcpHttpError(int status, string message) : base(message) { Status = status; }
}

public static class NdMcpServer
{
    public const string Version = "0.2.0";

    public static int Port = 3560;
    public static string ExportDir;
    public static int UiThreadId = -1;
    public static SynchronizationContext SyncContext;
    public static IApplication App;
    public static int RequestCount;

    private static HttpListener _listener;
    private static readonly object _lock = new object();
    private static readonly List<string> _log = new List<string>();

    public static bool IsRunning
    {
        get { return _listener != null && _listener.IsListening; }
    }

    public static string BaseUrl() { return "http://127.0.0.1:" + Port; }

    public static void Start()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(BaseUrl() + "/");
        listener.Start();
        _listener = listener;
        Log("start " + BaseUrl());
        // 非同期受付を1件仕掛けて即戻る（UI スレッドをブロックしない）
        listener.BeginGetContext(OnRequest, listener);
    }

    public static void Stop()
    {
        var listener = _listener;
        _listener = null;
        if (listener != null)
        {
            try { listener.Stop(); } catch (Exception) { }
            try { listener.Close(); } catch (Exception) { }
        }
        Log("stop");
    }

    // 受付コールバック（スレッドプールのスレッドで実行される）
    private static void OnRequest(IAsyncResult ar)
    {
        var listener = (HttpListener)ar.AsyncState;
        HttpListenerContext ctx;
        try { ctx = listener.EndGetContext(ar); }
        catch (Exception) { return; }   // Stop() 後の ObjectDisposedException 等。終了する

        // 次のリクエストの受付を先に仕掛ける
        try { if (listener.IsListening) listener.BeginGetContext(OnRequest, listener); }
        catch (Exception e) { Log("re-arm failed: " + e.Message); }

        Interlocked.Increment(ref RequestCount);
        var status = 200;
        object body;
        var sw = Stopwatch.StartNew();
        try
        {
            body = Route(ctx.Request);
        }
        catch (NdMcpHttpError e)
        {
            status = e.Status;
            body = new JsonObject().Set("error", e.Message);
        }
        catch (Exception e)
        {
            status = 500;
            body = new JsonObject().Set("error", e.Message).Set("detail", e.ToString());
        }
        Log(ctx.Request.Url.PathAndQuery + " -> " + status + " (" + sw.ElapsedMilliseconds + "ms)");

        try
        {
            var bytes = Encoding.UTF8.GetBytes(Json.Write(body));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception e)
        {
            Log("response write failed: " + e.Message);
        }
    }

    private static object Route(HttpListenerRequest request)
    {
        var path = request.Url.AbsolutePath;
        var q = request.QueryString;
        if (path.StartsWith("/class-sync/", StringComparison.Ordinal)) return RouteClassSync(request, path, q);
        if (request.HttpMethod != "GET") throw new NdMcpHttpError(405, "GET のみ対応しています");

        if (path == "/ping")
        {
            return new JsonObject()
                .Set("ok", true).Set("server", "NdMcp").Set("version", Version)
                .Set("time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Set("requests", RequestCount)
                .Set("uiThreadId", UiThreadId)
                .Set("syncContext", SyncContext != null ? SyncContext.GetType().FullName : null);
        }
        if (path == "/thread")
        {
            var tid = Thread.CurrentThread.ManagedThreadId;
            return new JsonObject()
                .Set("thread", tid).Set("isUiThread", tid == UiThreadId)
                .Set("isThreadPool", Thread.CurrentThread.IsThreadPoolThread)
                .Set("uiThreadId", UiThreadId);
        }
        if (path == "/") return Usage();

        // ND API は必ずコマンド内で呼ぶ。診断用の直呼びも許可しない。
        if (q["direct"] == "1") throw new NdMcpHttpError(400, "direct=1 は廃止しました。通常のコマンド経由で実行してください。");
        var modelPath = q["path"] ?? "";
        var modelId = q["id"] ?? "";

        Func<IApplication, object> work = null;
        switch (path)
        {
            case "/project": work = app => ModelApi.Project(app); break;
            case "/tree": work = app => ModelApi.Tree(app, modelPath, modelId, ParseInt(q["depth"], 2, 0, 20)); break;
            case "/model": work = app => ModelApi.Model(app, modelPath, modelId); break;
            case "/search": work = app => ModelApi.Search(app, q["q"] ?? "", q["metaclass"] ?? "", ParseInt(q["limit"], 50, 1, 1000)); break;
            case "/markdown": work = app => ModelApi.Markdown(app, modelPath, modelId); break;
            case "/export": work = app => ModelApi.Export(app, modelPath, modelId, q["out"] ?? "", ExportDir); break;
            default: throw new NdMcpHttpError(404, "不明なパス: " + path);
        }
        return OnUiThread(work);
    }

    // クラス図同期。読み出しは GET、比較・反映は JSON 本文（path / id / editor / plantuml）の POST。
    private static object RouteClassSync(HttpListenerRequest request, string path, NameValueCollection q)
    {
        Func<IApplication, object> work;
        if (path == "/class-sync/current" || path == "/class-sync/editors")
        {
            if (request.HttpMethod != "GET") throw new NdMcpHttpError(405, path + " は GET のみ対応しています");
            var modelPath = q["path"] ?? "";
            var modelId = q["id"] ?? "";
            if (path == "/class-sync/editors") work = app => ClassSyncApi.Editors(app, modelPath, modelId);
            else { var editorId = q["editor"] ?? ""; work = app => ClassSyncApi.Current(app, modelPath, modelId, editorId); }
            return OnUiThread(work);
        }
        var mode = path.Substring("/class-sync/".Length);
        if (mode != "preview" && mode != "trial" && mode != "apply") throw new NdMcpHttpError(404, "不明なパス: " + path);
        if (request.HttpMethod != "POST") throw new NdMcpHttpError(405, path + " は POST のみ対応しています");
        var body = ReadBody(request);
        ClassJsonNode json;
        try { json = ClassJsonNode.Parse(body); }
        catch (Exception e) { throw new NdMcpHttpError(400, "本文が JSON として読めません: " + e.Message); }
        if (json == null || json.Properties == null) throw new NdMcpHttpError(400, "本文は JSON オブジェクトにしてください");
        var bodyPath = ClassJsonNode.Value(json, "path") ?? "";
        var bodyId = ClassJsonNode.Value(json, "id") ?? "";
        var bodyEditor = ClassJsonNode.Value(json, "editor") ?? "";
        var plantuml = ClassJsonNode.Value(json, "plantuml") ?? "";
        if (plantuml.Length == 0)
        {
            // 大きな図はファイルで渡せる（このPC上のパス）。
            var file = ClassJsonNode.Value(json, "file") ?? "";
            if (file.Length == 0) throw new NdMcpHttpError(400, "plantuml（本文）か file（このPC上の .puml パス）を指定してください");
            try
            {
                if (new FileInfo(file).Length > ClassSyncApi.MaxPumlLength) throw new NdMcpHttpError(400, "file は 300KB 以下にしてください");
                plantuml = File.ReadAllText(file, new UTF8Encoding(false, true));
            }
            catch (NdMcpHttpError) { throw; }
            catch (Exception e) { throw new NdMcpHttpError(400, "file を読めません: " + e.Message); }
        }
        work = app => ClassSyncApi.Sync(app, bodyPath, bodyId, bodyEditor, plantuml, mode);
        return OnUiThread(work);
    }

    private static string ReadBody(HttpListenerRequest request)
    {
        if (!request.HasEntityBody) return "";
        if (request.ContentLength64 > 2L * 1024 * 1024) throw new NdMcpHttpError(413, "本文は 2MB 以下にしてください");
        using (var reader = new StreamReader(request.InputStream, new UTF8Encoding(false, true)))
            return reader.ReadToEnd();
    }

    private static object OnUiThread(Func<IApplication, object> work)
    {
        var context = SyncContext;
        if (context == null) throw new NdMcpHttpError(503, "SynchronizationContext が捕獲できていません");
        object result = null;
        Exception error = null;
        context.Send(delegate(object state)
        {
            try
            {
                var request = new NdMcpCommandRequest { Work = work };
                var parameters = App.CreateCommandParams();
                parameters.AddParam(request);
                App.ExecuteCommand("NdMcp.Command.ExecuteRequest", parameters);
                if (!request.Completed)
                    throw new NdMcpHttpError(500, "NdMcp の要求コマンドが完了しませんでした。manifest.json と main.cs を一緒に更新してください。");
                if (request.Error != null) throw request.Error;
                result = request.Result;
            }
            catch (Exception e) { error = e; }
        }, null);
        if (error != null) throw error;
        return result;
    }

    private static int ParseInt(string s, int fallback, int min, int max)
    {
        int v;
        if (string.IsNullOrEmpty(s) || !int.TryParse(s, out v)) return fallback;
        return Math.Max(min, Math.Min(max, v));
    }

    private static object Usage()
    {
        return new JsonObject()
            .Set("server", "NdMcp").Set("version", Version)
            .Set("endpoints", new List<object>
            {
                "GET /ping", "GET /thread", "GET /project",
                "GET /tree?path=&id=&depth=2", "GET /model?path=&id=",
                "GET /search?q=&metaclass=&limit=50", "GET /markdown?path=&id=",
                "GET /export?path=&id=&out=",
                "GET /class-sync/editors?path=&id=", "GET /class-sync/current?path=&id=&editor=",
                "POST /class-sync/preview {path|id, editor?, plantuml|file}",
                "POST /class-sync/trial {path|id, editor?, plantuml|file}",
                "POST /class-sync/apply {path|id, editor?, plantuml|file}",
            });
    }

    // ---- ログ（バックグラウンドスレッドからは Output を使わずここへ書く）----

    public static string LogPath() { return Path.Combine(NdMcpConfig.ConfigDir(), "server.log"); }

    private static void Log(string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [t" + Thread.CurrentThread.ManagedThreadId + "] " + message;
        lock (_lock)
        {
            _log.Add(line);
            if (_log.Count > 50) _log.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(NdMcpConfig.ConfigDir());
                File.AppendAllText(LogPath(), line + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }

    public static List<string> RecentLog()
    {
        lock (_lock) { return new List<string>(_log); }
    }
}

// ------------------------------------------------------------
//  モデル読み出し API（UI スレッドで実行される前提）
//  使う ND API は AgentReview の MarkdownExporter で実績のあるものに限る
// ------------------------------------------------------------

public static class ModelApi
{
    public static object Project(IApplication app)
    {
        var project = RequireProject(app);
        var result = Summary(project);
        result.Set("path", project.Path);
        result.Set("children", ChildrenOf(project).Select(c => (object)Summary(c)).ToList());
        return result;
    }

    public static object Tree(IApplication app, string path, string id, int depth)
    {
        var root = Resolve(app, path, id);
        return TreeNode(root, depth);
    }

    public static object Model(IApplication app, string path, string id)
    {
        var m = Resolve(app, path, id);
        var result = Summary(m);
        result.Set("fields", Fields(m));
        result.Set("children", ChildrenOf(m).Select(c => (object)Summary(c)).ToList());
        return result;
    }

    public static object Search(IApplication app, string query, string metaclass, int limit)
    {
        var project = RequireProject(app);
        var hits = new List<object>();
        var total = 0;
        foreach (var m in AllModels(project))
        {
            if (m.IsDeleted || m.IsProxy) continue;
            if (query.Length > 0)
            {
                var name = m.Name ?? "";
                if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            }
            if (metaclass.Length > 0
                && !string.Equals(ShortClassName(m), metaclass, StringComparison.OrdinalIgnoreCase)) continue;
            total++;
            if (hits.Count < limit) hits.Add(Summary(m));
        }
        return new JsonObject().Set("query", query).Set("metaclass", metaclass)
            .Set("total", total).Set("returned", hits.Count).Set("models", hits);
    }

    public static object Markdown(IApplication app, string path, string id)
    {
        var root = Resolve(app, path, id);
        var exporter = new MarkdownExporter(new MarkdownExportOptions(), null);
        var markdown = exporter.Export(root);
        return new JsonObject()
            .Set("modelPath", PathOf(root)).Set("modelCount", exporter.ModelCount)
            .Set("warnings", exporter.Warnings.Cast<object>().ToList())
            .Set("markdown", markdown);
    }

    // design.md + diagrams\<種別>\*.puml + _index.md を書き出す（AgentReview の WriteDesignArtifacts 相当）
    public static object Export(IApplication app, string path, string id, string outDir, string defaultRoot)
    {
        var root = Resolve(app, path, id);
        if (string.IsNullOrEmpty(outDir))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var folder = AgentText.SafeFileName(root.Name ?? "");
            outDir = Path.Combine(defaultRoot, stamp + (folder.Length > 0 ? "_" + folder : ""));
        }
        Directory.CreateDirectory(outDir);

        // AgentReview と同じ対応表・エクスポータ・ファイル出力メソッドを使う。
        var exporter = new MarkdownExporter(new MarkdownExportOptions(), outDir, LoadDiagramGroupRules());
        DesignArtifactWriter.Write(app, "NdMcp", exporter, root, outDir);
        var files = new List<object>();
        files.Add(Path.Combine(outDir, "design.md"));
        files.Add(Path.Combine(outDir, "_index.md"));
        var diagramsDir = Path.Combine(outDir, "diagrams");
        if (Directory.Exists(diagramsDir))
            foreach (var f in Directory.GetFiles(diagramsDir, "*.puml", SearchOption.AllDirectories))
                files.Add(f);

        return new JsonObject()
            .Set("modelPath", PathOf(root)).Set("dir", outDir)
            .Set("modelCount", exporter.ModelCount).Set("diagramCount", exporter.DiagramCount)
            .Set("skippedModelCount", exporter.SkippedModelCount)
            .Set("warnings", exporter.Warnings.Cast<object>().ToList())
            .Set("files", files);
    }

    // ---- 内部 ----

    private static DiagramGroupRules LoadDiagramGroupRules()
    {
        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nd-agent-review", "config.ini");
        var rulesFile = "";
        if (File.Exists(config))
            foreach (var pair in IniFile.Read(config))
                if (pair.Key == "diagramGroups.rulesFile") rulesFile = pair.Value;
        return DiagramGroupRules.Load(rulesFile);
    }

    private static IProject RequireProject(IApplication app)
    {
        if (app == null) throw new NdMcpHttpError(503, "IApplication が捕獲できていません（サーバーを開始し直してください）");
        var project = app.Workspace.CurrentProject;
        if (project == null) throw new NdMcpHttpError(409, "プロジェクトが開かれていません");
        return project;
    }

    // path / id からモデルを引く。両方空ならプロジェクト。id 優先
    // クラス図同期 API からも使う（同じ解決規則）。
    public static IModel ResolveModel(IApplication app, string path, string id) { return Resolve(app, path, id); }

    private static IModel Resolve(IApplication app, string path, string id)
    {
        var project = RequireProject(app);
        if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(id)) return project;
        foreach (var m in AllModels(project))
        {
            if (m.IsDeleted || m.IsProxy) continue;
            if (!string.IsNullOrEmpty(id) && m.Id == id) return m;
            if (!string.IsNullOrEmpty(path) && string.IsNullOrEmpty(id) && PathOf(m) == path) return m;
        }
        throw new NdMcpHttpError(404, "モデルが見つかりません: " + (string.IsNullOrEmpty(id) ? "path=" + path : "id=" + id));
    }

    private static IEnumerable<IModel> AllModels(IProject project)
    {
        yield return project;
        IEnumerable<IModel> all;
        try { all = project.GetAllChildren().Cast<IModel>().ToList(); }
        catch (Exception) { all = new List<IModel>(); }
        foreach (var m in all) if (m != null) yield return m;
    }

    private static List<IModel> ChildrenOf(IModel m)
    {
        try
        {
            return m.GetChildren().Cast<IModel>()
                .Where(c => c != null && !c.IsDeleted && !c.IsProxy).ToList();
        }
        catch (Exception) { return new List<IModel>(); }
    }

    private static JsonObject Summary(IModel m)
    {
        return new JsonObject()
            .Set("name", m.Name).Set("id", m.Id)
            .Set("modelPath", PathOf(m)).Set("metaclass", ShortClassName(m));
    }

    private static object TreeNode(IModel m, int depth)
    {
        var node = Summary(m);
        var children = ChildrenOf(m);
        node.Set("childCount", children.Count);
        if (depth > 0 && children.Count > 0)
            node.Set("children", children.Select(c => TreeNode(c, depth - 1)).ToList());
        return node;
    }

    // 全フィールドを種別つきで返す。判定順は MarkdownExporter.WriteFields と同じ
    private static List<object> Fields(IModel m)
    {
        var result = new List<object>();
        var cls = m.Metaclass;
        if (cls == null) return result;
        List<IField> fields;
        try { fields = cls.GetFields().Cast<IField>().ToList(); }
        catch (Exception) { return result; }

        foreach (var f in fields)
        {
            if (f == null || f.Name == null || AgentText.IsSystemName(f.Name)) continue;
            var entry = new JsonObject().Set("name", f.Name).Set("type", f.Type);
            try
            {
                // 多重度上限（-1 は無制限）。IField.UpperBound は V3.x ドキュメントで確認済み
                entry.Set("multiple", f.UpperBound != 1);
                if (f.Type == "RichText")
                {
                    entry.Set("kind", "richtext");
                    entry.Set("value", RichTextMarkdown(m, f));
                }
                else if (f.IsEmbedded && f.TypeClass != null)
                {
                    entry.Set("kind", "embedded").Set("typeClass", f.TypeClass.FullName);
                    entry.Set("children", m.GetFieldValues(f.Name).Cast<object>()
                        .OfType<IModel>().Where(c => !c.IsDeleted && !c.IsProxy)
                        .Select(c => (object)Summary(c)).ToList());
                }
                else if (f.IsReference)
                {
                    entry.Set("kind", "reference");
                    if (f.TypeClass != null) entry.Set("typeClass", f.TypeClass.FullName);
                    entry.Set("targets", m.GetFieldValues(f.Name).Cast<object>()
                        .OfType<IModel>().Select(c => (object)Summary(c)).ToList());
                }
                else
                {
                    entry.Set("kind", "value");
                    string value = null;
                    try { value = m.GetFieldString(f.Name); } catch (Exception) { }
                    if (string.IsNullOrEmpty(value) || value.Trim().Length == 0) value = JoinScalarValues(m, f.Name);
                    entry.Set("value", value ?? "");
                }
            }
            catch (Exception ex)
            {
                entry.Set("kind", "error").Set("error", ex.Message);
            }
            result.Add(entry);
        }
        return result;
    }

    private static string RichTextMarkdown(IModel m, IField f)
    {
        string text = null;
        try
        {
            var html = m.GetRichTextField(f.Name, "html");
            if (!string.IsNullOrEmpty(html)) text = HtmlToMarkdown.Convert(html);
        }
        catch (Exception) { }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
        {
            try { text = m.GetRichTextField(f.Name, "text"); } catch (Exception) { }
        }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
        {
            try { text = m.GetFieldString(f.Name); } catch (Exception) { }
        }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) text = JoinScalarValues(m, f.Name);
        return text ?? "";
    }

    private static string JoinScalarValues(IModel m, string fieldName)
    {
        var values = new List<string>();
        try
        {
            foreach (var v in m.GetFieldValues(fieldName))
            {
                if (v == null || v is IModel) continue;
                var s = v.ToString();
                if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0) values.Add(s);
            }
        }
        catch (Exception) { }
        return values.Count > 0 ? string.Join(", ", values.ToArray()) : null;
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

    public static string PathOf(IModel m)
    {
        if (m == null) return "";
        string path = null;
        try { path = m.ModelPath; }
        catch (Exception) { }
        return string.IsNullOrEmpty(path) ? (m.Name ?? "") : path;
    }
}

// ============================================================
//  ここから PlantUmlTool/src の転記（tools/build_main.py が生成）
// ============================================================

// BEGIN TRANSCRIBED 61-class-sync-runtime.cs
// SDK-facing runtime: read the active class diagram, probe its metamodel, compare with
// PlantUML. Nothing here writes to the project.
public static class ClassDiagramKind
{
    public static IModel ModelOf(object shape)
    {
        var representation=shape as IRepresentation;
        return representation!=null?representation.Model:null;
    }
    // ND V3.x has no ClassDiagram editor type. Class diagrams and state machine diagrams
    // both report ERDiagram, so the view definition name separates them.
    public static string Reject(IEditor editor)
    {
        if(editor==null)return "C110: クラス図をメインエディタに開いてください。";
        if(editor is ISequenceDiagram)return "C110: 開いているのはシーケンス図です。クラス図を開いてください。";
        if(!(editor is IDiagram))return "C110: 開いているエディタは図ではありません。";
        string type=editor.EditorType??"";
        if(!ClassSyncOptions.ClassEditorTypes.Contains(type))return "C110: クラス図ではないエディタ種別です: "+type;
        string view=editor.ViewDefinitionName??"";
        if(ClassSyncOptions.StateViewDefinitionNames.Any(n=>string.Equals(n,view,StringComparison.OrdinalIgnoreCase)))
            return "C110: 開いているのはステートマシン図です: "+view;
        return null;
    }
}

public sealed class ClassDiagramSnapshot
{
    public ClassDocument Document;
    public List<string> Limitations=new List<string>();
    public Dictionary<string,string> ModelIds=new Dictionary<string,string>(StringComparer.Ordinal);
    public Dictionary<string,double[]> Geometry=new Dictionary<string,double[]>(StringComparer.Ordinal);
    class NodeInfo { public IModel Model; public INode Node; public ClassElement Element; public NodeInfo Parent; public List<NodeInfo> Children=new List<NodeInfo>(); }
    ClassSyncOptions o;ClassDocument doc;int order;
    Dictionary<string,NodeInfo> byModelId=new Dictionary<string,NodeInfo>(StringComparer.Ordinal);
    HashSet<string> usedAlias=new HashSet<string>(StringComparer.Ordinal);
    HashSet<string> unknownMember=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    HashSet<string> unknownLink=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static double Safe(Func<double> read) { try { return read(); } catch(Exception) { return 0; } }
    public static ClassDiagramSnapshot Read(IDiagram diagram,ClassSyncOptions options,StringBuilder log)
    {
        var snapshot=new ClassDiagramSnapshot{o=options??new ClassSyncOptions()};
        snapshot.doc=new ClassDocument();
        var editor=diagram as IEditor;var model=ClassDiagramKind.ModelOf(diagram);
        string title=model!=null && !string.IsNullOrEmpty(model.Name)?model.Name:(editor!=null && !string.IsNullOrEmpty(editor.ViewDefinitionName)?editor.ViewDefinitionName:"Class");
        snapshot.doc.HasTitle=ClassText.Normalize(title).Length>0;
        snapshot.doc.Elements.Add(new ClassElement{Id="root",Kind="diagram",Text=ClassText.Inline(ClassText.Normalize(title))});
        snapshot.CollectNodes(diagram,log);
        snapshot.CollectLinksFromFields();
        snapshot.CollectLinksFromConnectors(diagram);
        snapshot.doc.NormalizeLiterals();
        snapshot.doc.Validate();
        snapshot.Document=snapshot.doc;
        return snapshot;
    }
    void CollectNodes(IDiagram diagram,StringBuilder log)
    {
        var shapes=new List<INode>();
        try { foreach(var s in diagram.Nodes) { var node=s as INode;if(node!=null)shapes.Add(node); } }
        catch(Exception ex) { Limitations.Add("ノードの取得に失敗: "+ex.Message); }
        var ordered=shapes.OrderBy(n=>Safe(()=>n.LocationY)).ThenBy(n=>Safe(()=>n.LocationX)).ThenBy(n=>n.Id,StringComparer.Ordinal).ToList();
        var infos=new List<NodeInfo>();int withoutModel=0,duplicates=0;
        foreach(var node in ordered)
        {
            var model=ClassDiagramKind.ModelOf(node);
            if(model==null || model.IsDeleted) { withoutModel++;continue; }
            if(byModelId.ContainsKey(model.Id)) { duplicates++;continue; }
            string name=NameOf(model);
            string keyword=KeywordOf(model);
            var element=new ClassElement{Kind="class",Text=ClassText.Inline(name),Order=order++};
            element.Attributes["keyword"]=keyword;
            element.Attributes["stereotype"]=StereotypeOf(model,keyword);
            element.Attributes["alias"]=MakeAlias(name,model.Id);
            element.Id="c:"+element.Attributes["alias"];
            var info=new NodeInfo{Model=model,Node=node,Element=element};
            infos.Add(info);byModelId[model.Id]=info;
            ModelIds[element.Id]=model.Id;
            Geometry[element.Id]=new[]{Safe(()=>node.LocationX),Safe(()=>node.LocationY),Safe(()=>node.Width),Safe(()=>node.Height)};
        }
        if(withoutModel>0)Limitations.Add("モデルのないノード: "+withoutModel+"件（比較対象外）");
        if(duplicates>0)Limitations.Add("同じモデルの重複シェイプ: "+duplicates+"件（1件だけ比較）");
        // Parents and packages need the full node index, so resolve them in a second pass.
        foreach(var info in infos)
        {
            NodeInfo parent=null;var owner=info.Model.Owner;int guard=0;
            while(owner!=null && guard++<32) { if(byModelId.TryGetValue(owner.Id,out parent))break;owner=owner.Owner; }
            if(parent!=null) { info.Parent=parent;parent.Children.Add(info); }
        }
        // The exporter walks roots in position order and each root's children right after it.
        // A class-like node cannot contain a class in PlantUML, so its children are written at
        // the same depth inside the nearest container (package/component node) or package block.
        // The document takes that flattened shape so text and diagram agree on ownership.
        Action<NodeInfo,string> place=null;
        place=(info,container)=>{
            info.Element.Parent=container;info.Element.Order=order++;
            doc.Elements.Add(info.Element);
            string inner=ClassDocument.IsContainerKeyword(info.Element.Attr("keyword"))?info.Element.Id:container;
            foreach(var child in info.Children)place(child,inner);
        };
        foreach(var root in infos.Where(i=>i.Parent==null))place(root,PackageOf(root.Model));
        foreach(var info in infos)
            if(!ClassDocument.IsContainerKeyword(info.Element.Attr("keyword")))CollectMembers(info);
        if(infos.Count==0)Limitations.Add("図上にモデルと対応するノードがありません。");
        log.AppendLine("Snapshot nodes="+infos.Count+" packages="+doc.Elements.Count(e=>e.Kind=="package"));
    }
    string PackageOf(IModel m)
    {
        var path=new List<string>();var owner=m.Owner;int guard=0;
        while(owner!=null && guard++<32)
        {
            if(byModelId.ContainsKey(owner.Id))break;
            var name=ClassText.Normalize(owner.Name);
            if(name.Length>0)path.Insert(0,name);
            owner=owner.Owner;
        }
        string parent="root";
        foreach(var name in path)
        {
            var existing=doc.Elements.FirstOrDefault(e=>e.Kind=="package" && e.Parent==parent && e.Text==name);
            if(existing==null) { existing=new ClassElement{Id="pkg"+doc.Elements.Count,Kind="package",Parent=parent,Text=name,Order=order++};doc.Elements.Add(existing); }
            parent=existing.Id;
        }
        return parent;
    }
    static string NameOf(IModel m) { var name=ClassText.Normalize(m.Name);return name.Length>0?name:"(unnamed)"; }
    string MakeAlias(string label,string modelId)
    {
        var alias=ClassText.AsciiAlias(label);
        if(alias.Length==0)alias="C"+ClassText.ShortHash(modelId);
        if(!usedAlias.Add(alias)) { alias=alias+"_"+ClassText.ShortHash(modelId);usedAlias.Add(alias); }
        return alias;
    }
    string KeywordOf(IModel m)
    {
        string keyword;
        if(!string.IsNullOrEmpty(m.ClassName) && o.KeywordMap.TryGetValue(m.ClassName,out keyword))return keyword;
        var cls=m.Metaclass;
        if(cls!=null)
        {
            try { foreach(var s in cls.GetAllSuperClasses().Cast<IClass>())if(o.KeywordMap.TryGetValue(s.Name,out keyword))return keyword; }
            catch(Exception) { }
        }
        if(BoolField(m,o.AbstractFieldNames))return "abstract class";
        return "class";
    }
    string StereotypeOf(IModel m,string keyword)
    {
        if(!o.EmitStereotypes)return "";
        string stereotype;
        if(!string.IsNullOrEmpty(m.ClassName) && o.StereotypeMap.TryGetValue(m.ClassName,out stereotype))return ClassText.Normalize(stereotype);
        if(!string.Equals(keyword,"class",StringComparison.OrdinalIgnoreCase))return "";
        if(!o.EmitUnknownStereotype || string.IsNullOrEmpty(m.ClassName))return "";
        return ClassText.Normalize(m.ClassName);
    }
    void CollectMembers(NodeInfo info)
    {
        List<IModel> children;
        try { children=info.Model.GetChildren().Cast<IModel>().ToList(); }
        catch(Exception ex) { Limitations.Add(info.Element.Text+": 子モデルの取得に失敗: "+ex.Message);return; }
        var attributes=new List<ClassElement>();var operations=new List<ClassElement>();
        foreach(var child in children)
        {
            if(child==null || child.IsDeleted || byModelId.ContainsKey(child.Id))continue;
            string kind=MemberKindOf(child);
            if(kind=="skip")continue;
            var e=new ClassElement{Parent=info.Element.Id,Text=ClassText.Inline(NameOf(child))};
            if(kind=="operation")
            {
                e.Kind="operation";
                e.Attributes["visibility"]=VisibilityOf(child);e.Attributes["static"]=BoolField(child,o.StaticFieldNames)?"true":"";
                e.Attributes["abstract"]=BoolField(child,o.AbstractFieldNames)?"true":"";
                e.Attributes["parameters"]=ClassText.Inline(ParametersOf(child));
                // The exporter never prints a return type on this profile; it lives in the
                // operation's Type reference (K010). Read it so an input that states one can
                // be compared; the comparison ignores it when the input is silent.
                string returnType=ClassText.Inline(TextOf(child,o.ReturnTypeFieldNames));
                if(returnType.Length==0)returnType=ClassText.Inline(TextOf(child,o.TypeFieldNames));
                e.Attributes["returnType"]=returnType;
                operations.Add(e);
            }
            else if(kind=="literal") { e.Kind="literal";attributes.Add(e); }
            else
            {
                e.Kind="attribute";
                e.Attributes["visibility"]=VisibilityOf(child);e.Attributes["static"]=BoolField(child,o.StaticFieldNames)?"true":"";
                e.Attributes["type"]=ClassText.Inline(TextOf(child,o.TypeFieldNames));
                string multiplicity=ClassText.Inline(TextOf(child,o.MultiplicityFieldNames));
                if(multiplicity.Length==0)multiplicity=BoundsOf(child);
                e.Attributes["multiplicity"]=multiplicity;
                e.Attributes["default"]=ClassText.Inline(TextOf(child,o.DefaultValueFieldNames));
                attributes.Add(e);
            }
            e.Id="m"+doc.Elements.Count+"_"+attributes.Count+"_"+operations.Count;
            ModelIds[e.Id]=child.Id;
            doc.Elements.Add(e);
        }
        // The exporter prints attributes before operations regardless of child order.
        foreach(var e in attributes)e.Order=order++;
        foreach(var e in operations)e.Order=order++;
    }
    string MemberKindOf(IModel child)
    {
        string kind;
        if(!string.IsNullOrEmpty(child.ClassName) && o.MemberKindMap.TryGetValue(child.ClassName,out kind))return kind;
        var cls=child.Metaclass;
        if(cls!=null)
        {
            try { foreach(var s in cls.GetAllSuperClasses().Cast<IClass>())if(o.MemberKindMap.TryGetValue(s.Name,out kind))return kind; }
            catch(Exception) { }
        }
        if(!string.IsNullOrEmpty(child.ClassName) && unknownMember.Add(child.ClassName))
            Limitations.Add("メンバ種別が対応表にないため属性として読みました: ClassName="+child.ClassName);
        return "attribute";
    }
    string ParametersOf(IModel m)
    {
        var text=TextOf(m,o.ParameterFieldNames);
        if(text.Length>0)return text;
        var parts=new List<string>();
        try
        {
            foreach(var child in m.GetChildren().Cast<IModel>())
            {
                if(child==null || child.IsDeleted)continue;
                var name=ClassText.Normalize(child.Name);var type=TextOf(child,o.TypeFieldNames);
                if(name.Length==0 && type.Length==0)continue;
                parts.Add(type.Length>0?name+" : "+type:name);
            }
        }
        catch(Exception) { }
        return string.Join(", ",parts.ToArray());
    }
    string VisibilityOf(IModel m)
    {
        var raw=TextOf(m,o.VisibilityFieldNames);
        if(raw.Length==0)return "";
        string symbol;
        return o.VisibilityMap.TryGetValue(raw,out symbol)?symbol:"";
    }
    // LowerBound / UpperBound (K009) as "a..b"; "*" for an unbounded upper; "" when unset.
    public static string BoundsOf(IModel m)
    {
        string lower=TextOf(m,new List<string>{"LowerBound"}),upper=TextOf(m,new List<string>{"UpperBound"});
        if(lower.Length==0 && upper.Length==0)return "";
        if(upper=="-1")upper="*";
        if(lower.Length==0)lower="0";
        if(upper.Length==0)upper="*";
        return lower==upper?lower:lower+".."+upper;
    }
    public static string TextOf(IModel m,List<string> candidates)
    {
        var cls=m.Metaclass;if(cls==null)return "";
        List<IField> fields;
        try { fields=cls.GetFields().Cast<IField>().ToList(); } catch(Exception) { return ""; }
        foreach(var candidate in candidates)
        {
            foreach(var f in fields)
            {
                if(!string.Equals(f.Name,candidate,StringComparison.OrdinalIgnoreCase))continue;
                if(f.IsEmbedded || f.IsReference)
                {
                    try
                    {
                        var names=new List<string>();
                        foreach(var v in m.GetFieldValues(f.Name)) { var target=v as IModel;if(target==null)continue;var name=ClassText.Normalize(target.Name);if(name.Length>0)names.Add(name); }
                        if(names.Count>0)return string.Join(", ",names.ToArray());
                    }
                    catch(Exception) { }
                    try { var text=ClassText.Normalize(m.GetFieldString(f.Name));if(text.Length>0)return text; } catch(Exception) { }
                }
                else
                {
                    try { var value=ClassText.Normalize(m.GetFieldString(f.Name));if(value.Length>0)return value; } catch(Exception) { }
                }
            }
        }
        return "";
    }
    public static bool BoolField(IModel m,List<string> candidates)
    {
        var value=TextOf(m,candidates);
        return string.Equals(value,"true",StringComparison.OrdinalIgnoreCase) || value=="1";
    }
    void CollectLinksFromFields()
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);int selfReferences=0;
        foreach(var info in byModelId.Values.OrderBy(i=>i.Element.Order))
        {
            var cls=info.Model.Metaclass;if(cls==null)continue;
            List<IField> fields;
            try { fields=cls.GetFields().Cast<IField>().ToList(); } catch(Exception) { continue; }
            foreach(var f in fields)
            {
                if(!(f.IsReference || (f.IsEmbedded && o.EmitEmbedded)))continue;
                List<IModel> targets;
                try { targets=new List<IModel>();foreach(var v in info.Model.GetFieldValues(f.Name)) { var target=v as IModel;if(target!=null && !target.IsDeleted)targets.Add(target); } }
                catch(Exception) { continue; }
                foreach(var target in targets)
                {
                    NodeInfo other;
                    if(!byModelId.TryGetValue(target.Id,out other))continue;
                    if(other.Model.Id==info.Model.Id) { selfReferences++;continue; }
                    if(!seen.Add(info.Model.Id+"|"+f.Name+"|"+other.Model.Id))continue;
                    string label=o.EmitRoleNames && !ClassText.IsSystemName(f.Name)?ClassText.Inline(f.Name):"";
                    AddLink(info,other,ArrowOf(f),label,f.Name,o.EmitMultiplicity?Multiplicity(f):"");
                }
            }
        }
        if(selfReferences>0)Limitations.Add("自己参照: "+selfReferences+"件（出力側と同様に線にしない）");
    }
    void AddLink(NodeInfo from,NodeInfo to,string arrow,string label,string field,string toMult)
    {
        var e=new ClassElement{Id="l"+doc.Elements.Count,Kind="link",Parent="root",Text=label,Order=order++};
        e.Attributes["arrow"]=arrow;e.Attributes["field"]=field;e.Attributes["toMultiplicity"]=toMult;
        e.Links["from"]=new[]{from.Element.Id};e.Links["to"]=new[]{to.Element.Id};
        doc.Elements.Add(e);
    }
    string ArrowOf(IField f)
    {
        string arrow;
        if(!string.IsNullOrEmpty(f.Name) && o.LinkMap.TryGetValue(f.Name,out arrow))return arrow;
        if(f.IsEmbedded)return o.EmbeddedLink;
        if(!string.IsNullOrEmpty(f.Name) && !ClassText.IsSystemName(f.Name) && unknownLink.Add(f.Name))
            Limitations.Add("関連の種別が対応表にないため既定の矢印で読みました: フィールド="+f.Name);
        return o.DefaultLink;
    }
    public static string Multiplicity(IField f)
    {
        int lower,upper;
        try { lower=f.LowerBound;upper=f.UpperBound; } catch(Exception) { return ""; }
        var upperText=upper<0?"*":upper.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(lower==1 && upper==1)return "1";
        if(lower==0 && upper==1)return "0..1";
        if(lower==upper)return upperText;
        return lower.ToString(System.Globalization.CultureInfo.InvariantCulture)+".."+upperText;
    }
    void CollectLinksFromConnectors(IDiagram diagram)
    {
        var covered=new HashSet<string>(StringComparer.Ordinal);
        foreach(var l in doc.Elements.Where(e=>e.Kind=="link"))covered.Add(PairKey(l.Link("from"),l.Link("to")));
        List<IConnector> connectors;
        try { connectors=new List<IConnector>();foreach(var c in diagram.Connectors) { var connector=c as IConnector;if(connector!=null)connectors.Add(connector); } }
        catch(Exception ex) { Limitations.Add("コネクタの取得に失敗: "+ex.Message);return; }
        int skipped=0,fallback=0;
        foreach(var connector in connectors)
        {
            var from=NodeInfoOf(connector.StartPoint);var to=NodeInfoOf(connector.EndPoint);
            if(from==null || to==null) { skipped++;continue; }
            if(from.Model.Id==to.Model.Id)continue;
            if(!covered.Add(PairKey(from.Element.Id,to.Element.Id)))continue;
            string label="";var model=ClassDiagramKind.ModelOf(connector);
            if(model!=null)label=ClassText.Inline(ClassText.Normalize(model.Name));
            AddLink(from,to,o.FallbackLink,label,"","");fallback++;
        }
        if(skipped>0)Limitations.Add("両端が図上のクラスではないコネクタ: "+skipped+"件（読み飛ばし）");
        if(fallback>0)Limitations.Add("モデル側で種別を判別できないコネクタ: "+fallback+"件（既定の線として比較）");
    }
    static string PairKey(string a,string b) { return string.CompareOrdinal(a,b)<=0?a+"|"+b:b+"|"+a; }
    NodeInfo NodeInfoOf(INode node)
    {
        if(node==null)return null;
        var model=ClassDiagramKind.ModelOf(node);if(model==null)return null;
        NodeInfo info;
        if(byModelId.TryGetValue(model.Id,out info))return info;
        var owner=model.Owner;int guard=0;
        while(owner!=null && guard++<8) { if(byModelId.TryGetValue(owner.Id,out info))return info;owner=owner.Owner; }
        return null;
    }
}

// Exports the diagram's unit through the public SDK and cuts out this editor's JSON.
// Read-only observation of the persisted shape structure for later write-back design.
public static class ClassEditorCapture
{
    public sealed class Unit { public string Schema; public ClassJsonNode Editor; }
    // Editor node plus the unit's schema version, for building an Editors-only re-import.
    public static Unit ReadUnit(IProject project,IModel model,IEditor diagram,StringBuilder log)
    {
        var raw=ClassJsonNode.Parse(Read(project,model,diagram,log,true));
        return new Unit{Schema=ClassJsonNode.Value(raw,"SchemaVersion"),Editor=raw["Editor"]};
    }
    public static string Read(IProject project,IModel model,IEditor diagram,StringBuilder log,bool withSchema=false)
    {
        string directory=Path.Combine(Path.GetTempPath(),"ClassEditor-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"snapshot.nmdl");
        try
        {
            if(model.ModelUnit==null)throw new InvalidOperationException("C180: 図のモデルユニットを取得できません。");
            log.AppendLine("Editor export: unit type="+model.ModelUnit.Type);
            project.UnitManager.ExportModelUnit(model.ModelUnit,path);
            if(!File.Exists(path) || new FileInfo(path).Length>100000000)throw new InvalidOperationException("C180: 図のエクスポートを取得できないか100MBを超えています。");
            var exported=ClassJsonNode.Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
            var editors=exported["Editors"];
            if(editors==null || editors.Items==null)throw new InvalidOperationException("C180: エクスポートにEditorsがありません。");
            var mine=editors.Items.FirstOrDefault(e=>ClassJsonNode.Value(e,"Id")==diagram.Id);
            if(mine==null)throw new InvalidOperationException("C180: エクスポートに現在の図のEditorがありません（図が別ユニットにある可能性）。");
            var nodes=mine["Nodes"];var connectors=mine["Connectors"];
            log.AppendLine("Editor JSON: ViewType="+(ClassJsonNode.Value(mine,"ViewType")??"?")+" Nodes="+(nodes!=null && nodes.Items!=null?nodes.Items.Count:0)
                +" Connectors="+(connectors!=null && connectors.Items!=null?connectors.Items.Count:0)+" keys="+string.Join(",",mine.Properties.Keys));
            if(withSchema)return "{\"SchemaVersion\":"+ClassJson.Q(ClassJsonNode.Value(exported,"SchemaVersion")??"")+",\"Editor\":"+mine.ToJsonString()+"}";
            return mine.ToJsonString();
        }
        finally
        {
            try { if(File.Exists(path))File.Delete(path);if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);else log.AppendLine("Additional export files remain in: "+directory); }
            catch(Exception ex){log.AppendLine("Temporary export cleanup failed: "+ex.Message);}
        }
    }
}

// Metamodel probe. Everything profile-specific is observed here and saved locally;
// the tables in ClassSyncOptions get filled from this output, never guessed.
public static class ClassDiagramProbe
{
    public const string Category="ClassImportProbe";
    static string Pad(string s,int width) { int length=0;foreach(var ch in s??"")length+=ch<128?1:2;return (s??"")+new string(' ',Math.Max(0,width-length)); }
    static string Shorten(string s) { if(string.IsNullOrEmpty(s))return "";s=s.Replace("\r"," ").Replace("\n"," ");return s.Length<=60?s:s.Substring(0,60)+"…"; }
    static string Bounds(IField f) { try { return "["+f.LowerBound+".."+(f.UpperBound<0?"*":f.UpperBound.ToString(System.Globalization.CultureInfo.InvariantCulture))+"]"; } catch(Exception) { return "[?]"; } }
    static void DumpModel(Action<string> w,string title,IModel m)
    {
        w("---- "+title+" ----");
        if(m==null) { w("  (なし)");w("");return; }
        w("  ClassName  : "+m.ClassName);
        w("  Name       : "+Shorten(m.Name));
        w("  Id         : "+m.Id);
        var cls=m.Metaclass;
        if(cls!=null)
        {
            w("  FullName   : "+cls.FullName);
            w("  IsAbstract : "+cls.IsAbstract);
            try { var supers=cls.GetAllSuperClasses().Cast<IClass>().Select(c=>c.Name).ToList();w("  SuperClass : "+(supers.Count>0?string.Join(", ",supers.ToArray()):"(なし)")); }
            catch(Exception ex) { w("  SuperClass : (取得失敗 "+ex.Message+")"); }
        }
        try { var ownerField=m.GetOwnerField();w("  OwnerField : "+(ownerField!=null?ownerField.Name:"(不明)")); } catch(Exception) { w("  OwnerField : (取得できません)"); }
        w("  Owner      : "+(m.Owner!=null?m.Owner.ClassName+" / "+Shorten(m.Owner.Name):"(なし)"));
        if(cls!=null)
        {
            w("  Fields:");
            List<IField> fields;
            try { fields=cls.GetFields().Cast<IField>().ToList(); } catch(Exception ex) { fields=new List<IField>();w("    (取得失敗 "+ex.Message+")"); }
            foreach(var f in fields)
            {
                var sb=new StringBuilder();
                sb.Append("    ").Append(Pad(f.Name,30));
                sb.Append(" kind=").Append(f.IsEmbedded?"所有":(f.IsReference?"参照":"値  "));
                sb.Append(" type=").Append(Pad(f.Type,24)).Append(" mult=").Append(Bounds(f));
                if(f.IsEmbedded || f.IsReference)
                {
                    try { var targets=m.GetFieldValues(f.Name).Cast<object>().OfType<IModel>().ToList();if(targets.Count>0)sb.Append(" targets=").Append(targets.Count).Append(" first=").Append(targets[0].ClassName).Append("'").Append(Shorten(targets[0].Name)).Append("'"); }
                    catch(Exception) { }
                }
                else
                {
                    string value=null;
                    try { value=m.GetFieldString(f.Name); } catch(Exception) { }
                    if(!string.IsNullOrEmpty(value))sb.Append(" value='").Append(Shorten(value)).Append("'");
                }
                w(sb.ToString());
            }
        }
        w("");
    }
    static void DumpClassNames(Action<string> w,string title,List<IModel> models)
    {
        w("---- "+title+" ----");
        if(models.Count==0) { w("  (なし)");w("");return; }
        var counts=new Dictionary<string,int>(StringComparer.Ordinal);
        foreach(var model in models) { var name=model.ClassName??"(null)";int c;counts.TryGetValue(name,out c);counts[name]=c+1; }
        foreach(var pair in counts.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key,StringComparer.Ordinal))w("  "+pair.Key+" : "+pair.Value+" 件");
        w("");
    }
    static List<IModel> ChildrenOf(IEnumerable<IModel> models)
    {
        var result=new List<IModel>();
        foreach(var model in models)
        {
            try { foreach(var child in model.GetChildren().Cast<IModel>())if(child!=null && !child.IsDeleted)result.Add(child); }
            catch(Exception) { }
        }
        return result;
    }
    static void DumpEach(Action<string> w,string title,List<IModel> models)
    {
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var m in models)if(seen.Add(m.ClassName??"(null)"))DumpModel(w,title+" ClassName="+m.ClassName,m);
    }
    public static void Run(IApplication app)
    {
        var log=new StringBuilder();var pages=new StringBuilder();string editorJson=null;string stem=null;
        Action<string> w=text=>{log.AppendLine(text);try { app.Output.WriteLine(Category,text); } catch(Exception) { }};
        try
        {
            var editor=app.Workspace.CurrentEditor;
            string reject=ClassDiagramKind.Reject(editor);
            if(reject!=null)throw new InvalidOperationException(reject);
            var diagram=(IDiagram)editor;var project=app.Workspace.CurrentProject;
            var model=ClassDiagramKind.ModelOf(diagram);
            w("=== クラス図調査 "+ClassExperiment.Title+" ===");
            w("EditorType         : "+editor.EditorType);
            w("ViewDefinitionName : "+editor.ViewDefinitionName);
            w("Editor Id          : "+editor.Id);
            w("Editor ModelId     : "+editor.ModelId);
            pages.Append("EditorType: ").Append(editor.EditorType).Append("\nViewDefinitionName: ").Append(editor.ViewDefinitionName).Append('\n');
            DumpModel(w,"図のモデル",model);
            if(model!=null)
            {
                var chain=new List<string>();var at=model.Owner;int guard=0;
                while(at!=null && guard++<16) { chain.Add(at.ClassName+"("+(at.Metaclass!=null?at.Metaclass.FullName:"?")+")");at=at.Owner; }
                w("  Owner chain: "+string.Join(" <- ",chain.ToArray()));
                pages.Append("図モデル: ").Append(model.ClassName).Append(" / owner chain: ").Append(string.Join(" <- ",chain.Select(c=>c.Substring(0,c.IndexOf('('))).ToArray())).Append('\n');
            }
            var nodes=new List<INode>();var connectors=new List<IConnector>();
            try { foreach(var n in diagram.Nodes) { var node=n as INode;if(node!=null)nodes.Add(node); } } catch(Exception ex) { w("ノードの取得に失敗 : "+ex.Message); }
            try { foreach(var c in diagram.Connectors) { var conn=c as IConnector;if(conn!=null)connectors.Add(conn); } } catch(Exception ex) { w("コネクタの取得に失敗 : "+ex.Message); }
            w("ノード数           : "+nodes.Count);
            w("コネクタ数         : "+connectors.Count);
            pages.Append("ノード ").Append(nodes.Count).Append(" / コネクタ ").Append(connectors.Count).Append('\n');
            w("");
            w("---- ノード一覧（座標順） ----");
            var models=new List<IModel>();var onDiagram=new HashSet<string>(StringComparer.Ordinal);
            foreach(var node in nodes) { var m=ClassDiagramKind.ModelOf(node);if(m!=null)onDiagram.Add(m.Id); }
            foreach(var node in nodes.OrderBy(n=>n.LocationY).ThenBy(n=>n.LocationX))
            {
                var m=ClassDiagramKind.ModelOf(node);
                string parentOnDiagram="";
                if(m!=null) { var owner=m.Owner;int guard=0;while(owner!=null && guard++<32) { if(onDiagram.Contains(owner.Id)) { parentOnDiagram=" parentNode="+Shorten(owner.Name);break; }owner=owner.Owner; } }
                w("  node="+node.Id+" xywh="+node.LocationX.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+node.LocationY.ToString("R",System.Globalization.CultureInfo.InvariantCulture)
                    +","+node.Width.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+node.Height.ToString("R",System.Globalization.CultureInfo.InvariantCulture)
                    +" model="+(m==null?"(なし)":m.ClassName+" '"+Shorten(m.Name)+"' id="+m.Id+" children="+m.GetChildren().Cast<IModel>().Count(c=>!c.IsDeleted))+parentOnDiagram);
                if(m!=null && !m.IsDeleted)models.Add(m);
            }
            w("");
            var children=ChildrenOf(models);var grandchildren=ChildrenOf(children);
            DumpClassNames(w,"ノードのクラス名一覧",models);
            DumpClassNames(w,"子のクラス名一覧",children);
            DumpClassNames(w,"孫のクラス名一覧",grandchildren);
            pages.Append("ノードClassName: ").Append(string.Join(", ",models.Select(m=>m.ClassName).Distinct().ToArray())).Append('\n');
            pages.Append("子ClassName: ").Append(string.Join(", ",children.Select(m=>m.ClassName).Distinct().ToArray())).Append('\n');
            pages.Append("孫ClassName: ").Append(string.Join(", ",grandchildren.Select(m=>m.ClassName).Distinct().ToArray())).Append('\n');
            DumpEach(w,"ノード",models);
            DumpEach(w,"子",children);
            DumpEach(w,"孫",grandchildren);
            w("---- コネクタ ----");
            if(connectors.Count==0)w("  (なし)");
            foreach(var connector in connectors)
            {
                var from=ClassDiagramKind.ModelOf(connector.StartPoint);var to=ClassDiagramKind.ModelOf(connector.EndPoint);
                var own=ClassDiagramKind.ModelOf(connector);
                string lineType;try { lineType=connector.LineType; } catch(Exception) { lineType="(不明)"; }
                w("  "+connector.Id+" "+(from!=null?Shorten(from.Name):"?")+" -> "+(to!=null?Shorten(to.Name):"?")+" | LineType="+lineType
                    +" | コネクタのモデル="+(own!=null?own.ClassName+" '"+Shorten(own.Name)+"' id="+own.Id:"(なし)"));
                if(from==null || to==null)continue;
                try
                {
                    bool any=false;
                    foreach(var r in from.GetRelationsOf(to).Cast<IRelationship>())
                    {
                        any=true;
                        w("      rel="+r.Id+" IsEmbedded="+r.IsEmbedded+" IsReference="+r.IsReference+" IsTwoWay="+r.IsTwoWay
                            +" SourceField="+(r.SourceField!=null?r.SourceField.Name+Bounds(r.SourceField):"(なし)")+" TargetField="+(r.TargetField!=null?r.TargetField.Name+Bounds(r.TargetField):"(なし)")
                            +" SourceIndex="+r.SourceIndex+" TargetIndex="+r.TargetIndex);
                    }
                    if(!any)w("      (GetRelationsOf で関連を取得できません)");
                }
                catch(Exception ex) { w("      GetRelationsOf に失敗 : "+ex.Message); }
            }
            w("");
            w("---- 図上のノードを結ぶ参照・所有フィールド ----");
            var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(var m in models)
            {
                var cls=m.Metaclass;if(cls==null)continue;
                List<IField> fields;try { fields=cls.GetFields().Cast<IField>().ToList(); } catch(Exception) { continue; }
                foreach(var f in fields)
                {
                    if(!f.IsReference && !f.IsEmbedded)continue;
                    try
                    {
                        foreach(var v in m.GetFieldValues(f.Name))
                        {
                            var target=v as IModel;if(target==null || !onDiagram.Contains(target.Id))continue;
                            var key=(f.IsEmbedded?"所有 ":"参照 ")+f.Name+" "+Bounds(f)+" on "+m.ClassName;int c;counts.TryGetValue(key,out c);counts[key]=c+1;
                        }
                    }
                    catch(Exception) { }
                }
            }
            if(counts.Count==0)w("  (なし)");
            foreach(var pair in counts.OrderByDescending(p=>p.Value).ThenBy(p=>p.Key,StringComparer.Ordinal))w("  "+pair.Key+" : "+pair.Value+" 件");
            pages.Append("結合フィールド: ").Append(string.Join(", ",counts.Keys.ToArray())).Append('\n');
            w("");
            w("---- 読取り結果（ClassSyncOptions の既定表で解釈） ----");
            var snapshot=ClassDiagramSnapshot.Read(diagram,new ClassSyncOptions(),log);
            w("  classes="+snapshot.Document.Elements.Count(e=>e.Kind=="class")+" attributes="+snapshot.Document.Elements.Count(e=>e.Kind=="attribute")
                +" operations="+snapshot.Document.Elements.Count(e=>e.Kind=="operation")+" literals="+snapshot.Document.Elements.Count(e=>e.Kind=="literal")
                +" links="+snapshot.Document.Elements.Count(e=>e.Kind=="link")+" packages="+snapshot.Document.Elements.Count(e=>e.Kind=="package"));
            foreach(var limitation in snapshot.Limitations)w("  要照合: "+limitation);
            pages.Append("要照合: ").Append(snapshot.Limitations.Count).Append("件\n");
            foreach(var limitation in snapshot.Limitations)pages.Append("  ").Append(limitation).Append('\n');
            w("");
            w("---- Editor JSON の退避 ----");
            if(project==null || model==null)w("  プロジェクトまたは図モデルが取得できないため省略");
            else if(project.HasUnsavedChanges())w("  未保存の変更があるため省略（保存してから再実行すると取得できます）。この操作は自動保存しません。");
            else if(string.IsNullOrEmpty(project.Path))w("  未保存のプロジェクトのため省略");
            else
            {
                try { editorJson=ClassEditorCapture.Read(project,model,editor,log);w("  取得済み（レポートと同名の _editor.json）"); }
                catch(Exception ex) { w("  取得失敗: "+ex.Message); }
            }
            pages.Append("Editor JSON: ").Append(editorJson!=null?"取得済み":"未取得（診断ファイル参照）").Append('\n');
            w("=== 調査終了 ===");
            ClassExperiment.Summary="クラス図調査: 完了\nノード "+nodes.Count+" / コネクタ "+connectors.Count+" / 要照合 "+snapshot.Limitations.Count+"件\n出力ウィンドウ（"+Category+"）と診断ファイルに全文を保存しました。";
        }
        catch(Exception ex) { ClassExperiment.Summary="クラス図調査を完了できませんでした。\n"+ex.Message;log.AppendLine(ex.ToString()); }
        stem=ClassExperiment.SaveReport("probe",log.ToString(),null,null);
        if(stem!=null && editorJson!=null) { try { ClassExperiment.Write(stem+"_editor.json",editorJson); } catch(Exception ex) { ClassExperiment.Summary+="\nEditor JSONの保存失敗: "+ex.Message; } }
        if(stem!=null)ClassExperiment.Summary+="\n診断保存先: "+stem+".txt";
        ClassExperiment.Details=pages.Length>0?"クラス図調査の要約（名前・IDは診断ファイルのみ）\n"+pages.ToString():log.ToString();
        ClassExperiment.Show(app);
    }
}

public static class ClassSyncRuntime
{
    // The diagram a run works on. Set by Run(); null means the ribbon's active editor.
    // Re-reading through the model keeps the reference valid after undo or re-import.
    [ThreadStatic] static string targetModelId, targetEditorId;
    static IEditor Current(IApplication app)
    {
        if(targetEditorId==null)return app.Workspace.CurrentEditor;
        var active=app.Workspace.CurrentEditor;
        if(active!=null && active.Id==targetEditorId)return active;
        var project=app.Workspace.CurrentProject;var model=project==null?null:project.GetModelById(targetModelId);
        if(model==null)return null;
        return model.GetEditors().Cast<IEditor>().FirstOrDefault(e=>e.Id==targetEditorId);
    }
    static void Refresh(IApplication app,StringBuilder log)
    {
        try {app.Window.EditorPage.UpdateEditors();log.AppendLine("editors refreshed");}
        catch(Exception ex){log.AppendLine("editor refresh failed: "+ex.Message);}
    }
    static bool Matches(Action verify,StringBuilder log) { try {verify();return true;} catch(Exception ex){log.AppendLine(ex.ToString());return false;} }
    // Re-read the diagram through the SDK and compare it with the input. Never re-export.
    static void VerifyAgainst(IApplication app,string editorId,ClassDocument desired,string stage,StringBuilder log,bool tolerateMemberOrder=false)
    {
        var editor=Current(app);
        if(editor==null || editor.Id!=editorId)throw new InvalidOperationException("C230: "+stage+": 対象の図が表示されていません。");
        var after=ClassDiagramSnapshot.Read((IDiagram)editor,new ClassSyncOptions(),log).Document;
        var residual=ClassSyncPlan.Build(after,desired,()=>Guid.NewGuid().ToString());
        foreach(var c in residual.Changes)log.AppendLine(stage+" residual: "+c.Action+" "+c.Kind+" line="+c.Line+" detail="+c.Detail);
        // A member appended at the end differs from the input only in order; that is the
        // product's placement, not a missing edit, and is accepted when tolerance is on.
        if(tolerateMemberOrder && residual.Changes.Count>0 && residual.Changes.All(c=>c.Action=="move" && c.Detail=="order" && ClassDocument.MemberKinds.Contains(c.Kind)))
        {
            log.AppendLine(stage+": "+residual.Changes.Count+" member order residual(s) tolerated (appended members)");
            residual.Changes.Clear();
        }
        if(residual.Changes.Count>0)
        {
            // Link residuals are shown as PlantUML lines: a paired field on the other side
            // appears or disappears with its partner, so the input must list both lines.
            var lines=residual.Changes.Where(c=>c.Kind=="link").Take(6).Select(c=>(c.Action=="add"?"入力にあり図にない: ":"図にあり入力にない: ")+c.Detail).ToArray();
            throw new InvalidOperationException("C230: "+stage+": 読戻しで残差 "+residual.Changes.Count+"件（診断ファイル参照）"+(lines.Length>0?"\n"+string.Join("\n",lines):""));
        }
        log.AppendLine(stage+": SDK read-back matches the input");
    }
    static void VerifyRestored(IApplication app,string editorId,string originalJson,StringBuilder log)
    {
        var editor=Current(app);
        if(editor==null || editor.Id!=editorId)throw new InvalidOperationException("C230: 取消後: 対象の図が表示されていません。");
        var after=ClassDiagramSnapshot.Read((IDiagram)editor,new ClassSyncOptions(),log).Document;
        if(after.ToJson()!=originalJson)throw new InvalidOperationException("C230: 取消後: 図が処理前の状態に戻っていません。");
        log.AppendLine("restored: SDK read-back equals the pre-trial state");
    }
    // One resolved edit: the member model plus, for a type change, the old and new type models.
    class ResolvedEdit { public IModel Model; public ClassMemberEdit Edit; public IModel OldType; public TypeTarget NewType, ReturnType; public string VisibilityValue; public ArgumentPlan Arguments; }
    // A type to reference: an existing model, or one to create under the owner class's
    // type-definition field on first use. Created models are shared by name within a run.
    class TypeTarget
    {
        public IModel Existing, Owner; public IField Field; public IClass Class; public string Name;
        public IModel Model; public bool Created;
        public IModel Materialize(StringBuilder log)
        {
            if(Model!=null)return Model;
            if(Existing!=null) { Model=Existing;return Model; }
            var created=Owner.AddNewModel(Field,Class);
            if(created==null)throw new InvalidOperationException("C230: 型 '"+Name+"' を作成できませんでした。");
            created.SetField("Name",Name);
            if(ClassText.Inline(ClassText.Normalize(created.Name))!=Name)throw new InvalidOperationException("C230: 作成した型の名前の読戻しが一致しません。");
            log.AppendLine("created type "+created.ClassName+" id="+created.Id+" name='"+Name+"' under "+Owner.ClassName+" '"+Owner.Name+"' field="+Field.Name);
            Model=created;Created=true;return Model;
        }
    }
    static Dictionary<string,TypeTarget> typeTargets=new Dictionary<string,TypeTarget>(StringComparer.Ordinal);
    // Find a type model by name (preferring ones owned near the class), or plan to create it.
    static TypeTarget ResolveType(IProject project,IModel ownerClass,string typeName,string typeKind,string typeClassName,ClassSyncOptions options,ref List<IModel> everything,StringBuilder log)
    {
        if(everything==null)everything=Tree(project.DesignModel).ToList();
        var candidates=everything.Where(m=>!m.IsProxy && !m.IsDeleted && IsA(m,typeClassName) && ClassText.Inline(ClassText.Normalize(m.Name))==typeName).ToList();
        if(candidates.Count>1)
        {
            var owners=new List<string>();var at=ownerClass;int guard=0;
            while(at!=null && guard++<32) { owners.Add(at.Id);at=at.Owner; }
            foreach(string ownerId in owners)
            {
                var near=candidates.Where(m=>{var o=m.Owner;int g=0;while(o!=null && g++<32){if(o.Id==ownerId)return true;o=o.Owner;}return false;}).ToList();
                if(near.Count>0) { candidates=near;break; }
            }
        }
        if(candidates.Count>1)
        {
            // Several same-named definitions (a primitive like "int" defined per class, K053).
            // Prefer one a sibling class in the same owner already references, then the one
            // owned highest in the tree; only stop when even that is ambiguous.
            var referenced=candidates.Where(m=>m.GetRelationsWhere((rel,f)=>rel.Target!=null && rel.Target.Id==m.Id && rel.IsReference).Cast<IRelationship>()
                .Any(rel=>{var src=rel.Source;int g=0;while(src!=null && g++<32){if(src.Owner!=null && ownerClass.Owner!=null && src.Owner.Id==ownerClass.Owner.Id)return true;src=src.Owner;}return false;})).ToList();
            if(referenced.Count>0)candidates=referenced;
            if(candidates.Count>1)
            {
                Func<IModel,int> depth=m=>{int d=0;var o=m.Owner;while(o!=null && d<64){d++;o=o.Owner;}return d;};
                int shallowest=candidates.Min(depth);
                var top=candidates.Where(m=>depth(m)==shallowest).ToList();
                if(top.Count==1)candidates=top;
                else { log.AppendLine("type '"+typeName+"': "+candidates.Count+" candidates at the same depth; taking the first by owner name"); candidates=top.OrderBy(m=>m.Owner==null?"":m.Owner.Name,StringComparer.Ordinal).ThenBy(m=>m.Id,StringComparer.Ordinal).Take(1).ToList(); }
            }
            log.AppendLine("type '"+typeName+"' resolved among several: "+candidates[0].Id+" owner="+(candidates[0].Owner==null?"":candidates[0].Owner.ClassName+" '"+candidates[0].Owner.Name+"'"));
        }
        if(candidates.Count==1)return new TypeTarget{Existing=candidates[0],Name=typeName};
        string key=ownerClass.Id+"|"+typeName;
        TypeTarget planned;
        if(typeTargets.TryGetValue(key,out planned))return planned;
        string kind=typeKind.Length>0?typeKind:options.DefaultTypeKind;
        var field=FieldOf(ownerClass,kind);
        if(field==null || !field.IsEmbedded || field.TypeClass==null)
        {
            var available=ownerClass.Metaclass.GetFields().Cast<IField>().Where(f=>f.IsEmbedded && f.TypeClass!=null && f.Type.EndsWith("Type",StringComparison.Ordinal)).Select(f=>f.Name).ToArray();
            throw new InvalidOperationException("C220: 型 '"+typeName+"' はプロジェクトに無く、"+ownerClass.ClassName+" に型定義フィールド '"+kind+"' もありません。使えるフィールド: "+string.Join(", ",available));
        }
        // Reuse the metaclass of an existing definition in that field when there is one.
        var sibling=ownerClass.GetFieldValues(field.Name).Cast<object>().OfType<IModel>().FirstOrDefault(m=>!m.IsDeleted);
        planned=new TypeTarget{Owner=ownerClass,Field=field,Class=sibling!=null?sibling.Metaclass:field.TypeClass,Name=typeName};
        typeTargets[key]=planned;
        log.AppendLine("type '"+typeName+"' not found: will create "+planned.Class.FullName+" in "+ownerClass.ClassName+" '"+ownerClass.Name+"'."+field.Name);
        return planned;
    }
    // How to make an operation's Parameter children match a list of names: the metaclass to
    // create (from an existing argument anywhere in the project, else the field's type class)
    // and the owning field. Types on arguments are resolved by name like attribute types.
    class ArgumentPlan { public IField Field; public IClass ArgumentClass; public string[] Names, Types; public TypeTarget[] TypeModels; }
    static ArgumentPlan PlanArguments(IProject project,IModel operation,IModel ownerClass,string parameters,ClassSyncOptions options,ref List<IModel> everything,StringBuilder log)
    {
        var names=ClassTextPreflight.ParameterNames(parameters);var types=ClassTextPreflight.ParameterTypes(parameters);var kinds=ClassTextPreflight.ParameterTypeKinds(parameters);
        var plan=new ArgumentPlan{Names=names,Types=types,TypeModels=new TypeTarget[names.Length]};
        if(names.Length==0)return plan;
        var field=options.ParameterFieldNames.Select(n=>FieldOf(operation,n)).FirstOrDefault(f=>f!=null && f.IsEmbedded);
        if(field==null)throw new InvalidOperationException("C220: 操作に引数の所有フィールドがありません。");
        plan.Field=field;
        var sibling=operation.GetFieldValues(field.Name).Cast<object>().OfType<IModel>().FirstOrDefault(m=>!m.IsDeleted);
        if(sibling==null)
        {
            // Another operation of the same class, then any operation of this metaclass in the project.
            var owners=new List<IModel>();owners.AddRange(ownerClass.GetChildren().Cast<IModel>().Where(m=>!m.IsDeleted && m.Metaclass!=null && m.Metaclass.Id==operation.Metaclass.Id));
            foreach(var o in owners) { sibling=o.GetFieldValues(field.Name).Cast<object>().OfType<IModel>().FirstOrDefault(m=>!m.IsDeleted);if(sibling!=null)break; }
        }
        if(sibling==null)
        {
            if(everything==null)everything=Tree(project.DesignModel).ToList();
            sibling=everything.Where(m=>!m.IsProxy && m.Owner!=null && m.Metaclass!=null && m.Owner.Metaclass!=null && m.Owner.Metaclass.Id==operation.Metaclass.Id).FirstOrDefault(m=>{IField f;try { f=m.GetOwnerField(); } catch(Exception) { return false; }return f!=null && f.Name==field.Name;});
        }
        plan.ArgumentClass=sibling!=null?sibling.Metaclass:field.TypeClass;
        if(plan.ArgumentClass==null)throw new InvalidOperationException("C220: 引数のメタクラスを特定できません。");
        for(int i=0;i<names.Length;i++)
        {
            if(types[i].Length==0)continue;
            if(everything==null)everything=Tree(project.DesignModel).ToList();
            var typeField=sibling!=null?options.TypeFieldNames.Select(n=>FieldOf(sibling,n)).FirstOrDefault(f=>f!=null && f.IsReference):null;
            string typeClass=typeField!=null?typeField.Type:"Type";
            plan.TypeModels[i]=ResolveType(project,ownerClass,types[i],kinds[i],typeClass,options,ref everything,log);
        }
        log.AppendLine("arguments: field="+field.Name+" class="+plan.ArgumentClass.FullName+" names=["+string.Join(", ",names)+"]");
        return plan;
    }
    // Make the Parameter children equal to plan.Names in order: rename in place when the count
    // is unchanged (the exporter carries names only), otherwise delete the extras and append
    // the missing ones, then re-read and compare.
    static void ApplyArguments(IModel operation,ArgumentPlan plan,ClassSyncOptions options,StringBuilder log)
    {
        if(plan.Field==null)
        {
            // No parameters wanted: delete whatever is there.
            var field0=options.ParameterFieldNames.Select(n=>FieldOf(operation,n)).FirstOrDefault(f=>f!=null && f.IsEmbedded);
            if(field0==null)return;
            foreach(var a in operation.GetFieldValues(field0.Name).Cast<object>().OfType<IModel>().Where(m=>!m.IsDeleted).ToList())a.Delete();
            return;
        }
        var existing=operation.GetFieldValues(plan.Field.Name).Cast<object>().OfType<IModel>().Where(m=>!m.IsDeleted).ToList();
        if(existing.Count==plan.Names.Length)
        {
            for(int i=0;i<existing.Count;i++)if(ClassText.Inline(ClassText.Normalize(existing[i].Name))!=plan.Names[i])existing[i].SetField("Name",plan.Names[i]);
        }
        else
        {
            var wanted=new HashSet<string>(plan.Names,StringComparer.Ordinal);
            foreach(var a in existing.Where(m=>!wanted.Contains(ClassText.Inline(ClassText.Normalize(m.Name)))).ToList())a.Delete();
            var present=new HashSet<string>(operation.GetFieldValues(plan.Field.Name).Cast<object>().OfType<IModel>().Where(m=>!m.IsDeleted).Select(m=>ClassText.Inline(ClassText.Normalize(m.Name))),StringComparer.Ordinal);
            for(int i=0;i<plan.Names.Length;i++)
            {
                if(present.Contains(plan.Names[i]))continue;
                IModel created=null;
                var now=operation.GetFieldValues(plan.Field.Name).Cast<object>().OfType<IModel>().Where(m=>!m.IsDeleted).ToList();
                if(i<now.Count) { try { created=operation.AddNewModelAt(plan.Field,plan.ArgumentClass,"before",i); } catch(Exception ex) { log.AppendLine("AddNewModelAt for argument failed, appending: "+ex.Message);created=null; } }
                if(created==null)created=operation.AddNewModel(plan.Field,plan.ArgumentClass);
                if(created==null)throw new InvalidOperationException("C230: 引数を作成できませんでした。");
                created.SetField("Name",plan.Names[i]);
                if(plan.TypeModels[i]!=null)
                {
                    var tf=options.TypeFieldNames.Select(n=>FieldOf(created,n)).FirstOrDefault(f=>f!=null && f.IsReference);
                    if(tf!=null)created.Relate(tf.Name,plan.TypeModels[i].Materialize(log));
                }
                log.AppendLine("created argument "+created.ClassName+" '"+plan.Names[i]+"' at "+i);
            }
        }
        var after=operation.GetFieldValues(plan.Field.Name).Cast<object>().OfType<IModel>().Where(m=>!m.IsDeleted).Select(m=>ClassText.Inline(ClassText.Normalize(m.Name))).ToArray();
        if(!after.SequenceEqual(plan.Names))throw new InvalidOperationException("C230: 引数の読戻しが一致しません: ["+string.Join(", ",after)+"]");
    }
    class ResolvedLink { public IModel From, To; public ClassLinkChange Change; public string RelationId="", PartnerField=""; }
    // A class to create (owner and owning field taken from its sibling, node placed next to
    // the sibling's node) or to delete.
    class ResolvedClass { public ClassChangeItem Change; public IModel Owner, Sibling, Model; public IField OwningField; public IClass Class; public INode SiblingNode, Node; }
    class ResolvedMember { public IModel Owner, Member, InsertBefore; public TypeTarget TypeTarget, ReturnType; public ClassMemberChange Change; public string Field, ClassName, VisibilityValue, TypeField; public IField OwningField; public IClass MemberClass; public string Parameters; }
    static IEnumerable<IModel> Tree(IModel root)
    {
        var stack=new Stack<IModel>();stack.Push(root);
        while(stack.Count>0)
        {
            var m=stack.Pop();if(m==null || m.IsDeleted)continue;
            yield return m;
            IEnumerable<IModel> children;
            try { children=m.GetChildren().Cast<IModel>().ToList(); } catch(Exception) { continue; }
            foreach(var c in children)stack.Push(c);
        }
    }
    static bool IsA(IModel m,string className)
    {
        var cls=m.Metaclass;if(cls==null)return false;
        if(cls.Name==className)return true;
        try { return cls.GetAllSuperClasses().Cast<IClass>().Any(c=>c.Name==className); } catch(Exception) { return false; }
    }
    static ClassElement ClassByAlias(ClassDocument doc,string alias) { return doc.Elements.FirstOrDefault(e=>e.Kind=="class" && e.Attr("alias")==alias); }
    // A type definition created under a class is a child the exporter prints as a bare
    // attribute line (K019/K048). The expected document gets that line so the read-back
    // matches; it lands after the class's other members, like the export order.
    static void AddCreatedTypeLines(ClassDocument doc,ClassDiagramSnapshot snapshot,StringBuilder log)
    {
        foreach(var target in typeTargets.Values.Where(x=>x.Created && x.Owner!=null))
        {
            var ownerElement=snapshot.ModelIds.Where(p=>p.Value==target.Owner.Id).Select(p=>p.Key).FirstOrDefault();
            if(ownerElement==null) { log.AppendLine("created type owner is not on the diagram; no implied line: "+target.Name);continue; }
            var owner=doc.Elements.FirstOrDefault(e=>e.Kind=="class" && e.Id!=null && snapshotOwnerMatches(e,ownerElement,doc,snapshot));
            if(owner==null) { log.AppendLine("created type owner class not found in the expected input: "+target.Name);continue; }
            if(doc.Elements.Any(e=>ClassDocument.MemberKinds.Contains(e.Kind) && e.Parent==owner.Id && e.Text==target.Name && e.Kind!="operation"))continue;
            int order=doc.Elements.Where(e=>e.Parent==owner.Id).Select(e=>e.Order).DefaultIfEmpty(owner.Order).Max()+1;
            foreach(var e in doc.Elements.Where(e=>e.Order>=order))e.Order++;
            var line=new ClassElement{Id="impliedtype"+doc.Elements.Count,Kind="attribute",Parent=owner.Id,Text=ClassText.Inline(target.Name),Order=order};
            foreach(var key in new[]{"visibility","static","type","multiplicity","default"})line.Attributes[key]="";
            doc.Elements.Add(line);
            log.AppendLine("implied type line added to the expected input: "+owner.Text+" :: "+target.Name);
        }
    }
    // The expected document is the parsed input, whose class ids are "c:<alias>"; the
    // snapshot uses the same alias scheme, so the owner is found by alias.
    static bool snapshotOwnerMatches(ClassElement candidate,string snapshotElementId,ClassDocument doc,ClassDiagramSnapshot snapshot)
    {
        var snap=snapshot.Document.Elements.FirstOrDefault(e=>e.Id==snapshotElementId);
        return snap!=null && candidate.Attr("alias")==snap.Attr("alias");
    }
    static bool RemovePartnerLine(ClassDocument doc,string fromAlias,string toAlias,string field)
    {
        var from=ClassByAlias(doc,fromAlias);var to=ClassByAlias(doc,toAlias);
        if(from==null || to==null)return false;
        var line=doc.Elements.FirstOrDefault(e=>e.Kind=="link" && e.Link("from")==from.Id && e.Link("to")==to.Id && e.Text==field);
        if(line==null)return false;
        doc.Elements.Remove(line);return true;
    }
    // A class created under a package gets anonymous back-references from the product
    // (K054); the exporter prints them as unlabeled lines. Lines from system-named fields
    // that touch a new class and are absent from the input are added to the expected document.
    static void AddSystemLinesForNewClasses(IApplication app,ClassDocument doc,List<ResolvedClass> newClasses,ClassSyncOptions options,StringBuilder log)
    {
        if(newClasses.Count==0)return;
        var d=Current(app) as IDiagram;if(d==null)return;
        var after=ClassDiagramSnapshot.Read(d,options,new StringBuilder());
        var newModelIds=new HashSet<string>(newClasses.Select(c=>c.Model.Id),StringComparer.Ordinal);
        var newElementIds=new HashSet<string>(after.ModelIds.Where(p=>newModelIds.Contains(p.Value)).Select(p=>p.Key),StringComparer.Ordinal);
        var afterIndex=after.Document.Elements.ToDictionary(e=>e.Id);
        foreach(var line in after.Document.Elements.Where(e=>e.Kind=="link" && ClassText.IsSystemName(e.Attr("field")) && (newElementIds.Contains(e.Link("from")) || newElementIds.Contains(e.Link("to")))))
        {
            string fromAlias=afterIndex[line.Link("from")].Attr("alias"),toAlias=afterIndex[line.Link("to")].Attr("alias");
            var from=ClassByAlias(doc,fromAlias);var to=ClassByAlias(doc,toAlias);
            if(from==null || to==null)continue;
            if(doc.Elements.Any(e=>e.Kind=="link" && e.Link("from")==from.Id && e.Link("to")==to.Id && e.Text==line.Text))continue;
            var e2=new ClassElement{Id="impliedsys"+doc.Elements.Count,Kind="link",Parent="root",Text=line.Text,Order=doc.Elements.Count};
            e2.Attributes["arrow"]=line.Attr("arrow");e2.Attributes["field"]=line.Attr("field");e2.Attributes["toMultiplicity"]=line.Attr("toMultiplicity");
            e2.Links["from"]=new[]{from.Id};e2.Links["to"]=new[]{to.Id};
            doc.Elements.Add(e2);
            log.AppendLine("implied system line added to the expected input: "+fromAlias+" -> "+toAlias+" field="+line.Attr("field"));
        }
    }
    static bool AddPartnerLine(ClassDocument doc,string fromAlias,string toAlias,IField field,ClassSyncOptions options)
    {
        var from=ClassByAlias(doc,fromAlias);var to=ClassByAlias(doc,toAlias);
        if(from==null || to==null)return false;
        if(doc.Elements.Any(e=>e.Kind=="link" && e.Link("from")==from.Id && e.Link("to")==to.Id && e.Text==field.Name))return false;
        string arrow;if(!options.LinkMap.TryGetValue(field.Name,out arrow))arrow=options.DefaultLink;
        var e=new ClassElement{Id="implied"+doc.Elements.Count,Kind="link",Parent="root",Text=ClassText.IsSystemName(field.Name)?"":ClassText.Inline(field.Name),Order=doc.Elements.Count};
        e.Attributes["arrow"]=arrow;e.Attributes["field"]=field.Name;e.Attributes["toMultiplicity"]=ClassDiagramSnapshot.Multiplicity(field);
        e.Links["from"]=new[]{from.Id};e.Links["to"]=new[]{to.Id};
        doc.Elements.Add(e);return true;
    }
    // Build the captured editor plus one entry per connector that appeared during this run,
    // cloned from an existing connector (same DefinitionId, Style and Labels) with the new
    // connector's own Id, model and ends and IsVisible=true, then re-apply Editors only.
    static void ReapplyEditorWithVisibleConnectors(IApplication app,IProject project,ClassEditorCapture.Unit unit,HashSet<string> before,StringBuilder log,List<ResolvedClass> newClasses)
    {
        var d=Current(app) as IDiagram;if(d==null)throw new InvalidOperationException("C230: 図が表示されていません。");
        var connectors=unit.Editor["Connectors"];
        int added=0;
        // New classes: a node cloned from the sibling's node entry (same DefinitionId, Style,
        // Title, Category, Compartments) with the created model, placed to the right of the
        // sibling. When the API already made a node, its entry is written with the same Id so
        // the re-import keeps it and makes it visible.
        var nodes=unit.Editor["Nodes"];
        int nodesBefore=d.Nodes.Cast<object>().Count();
        foreach(var c in newClasses)
        {
            if(nodes==null || nodes.Items==null)throw new InvalidOperationException("C230: Editor JSON に Nodes がありません。");
            var siblingEntry=nodes.Items.FirstOrDefault(n=>ClassJsonNode.Value(n,"Id")==c.SiblingNode.Id);
            if(siblingEntry==null)throw new InvalidOperationException("C230: 隣のクラスのノードが Editor JSON にありません。");
            var clone=ClassJsonNode.Parse(siblingEntry.ToJsonString());
            string nodeId=c.Node!=null?c.Node.Id:Guid.NewGuid().ToString();
            clone.Properties["Id"]=new ClassJsonNode{Raw=ClassJson.Q(nodeId)};
            clone.Properties["ModelId"]=new ClassJsonNode{Raw=ClassJson.Q(c.Model.Id)};
            clone.Properties["LocationX"]=new ClassJsonNode{Raw=(c.SiblingNode.LocationX+c.SiblingNode.Width+40).ToString("R",System.Globalization.CultureInfo.InvariantCulture)};
            clone.Properties["LocationY"]=new ClassJsonNode{Raw=c.SiblingNode.LocationY.ToString("R",System.Globalization.CultureInfo.InvariantCulture)};
            clone.Properties["Visible"]=new ClassJsonNode{Raw="true"};
            clone.Properties["IsVisible"]=new ClassJsonNode{Raw="true"};
            nodes.Items.Add(clone);added++;
            log.AppendLine("node entry for re-import: id="+nodeId+" model="+c.Model.Id+" (template node "+c.SiblingNode.Id+", api node "+(c.Node!=null?"yes":"no")+")");
        }
        if(connectors==null || connectors.Items==null || connectors.Items.Count==0)
        {
            if(added==0) { log.AppendLine("no connector entries to re-apply");return; }
        }
        var template=connectors!=null && connectors.Items!=null && connectors.Items.Count>0?connectors.Items[0]:null;
        foreach(var c in d.Connectors.Cast<object>().ToList())
        {
            var shape=c as IConnector;if(shape==null || before.Contains(shape.Id))continue;
            if(template==null)throw new InvalidOperationException("C230: 図に既存の線がないため、線の雛形を取れません。");
            var own=ClassDiagramKind.ModelOf(shape);
            if(own==null || shape.StartPoint==null || shape.EndPoint==null)throw new InvalidOperationException("C230: 追加されたコネクタのモデルまたは両端を取得できません。");
            var clone=ClassJsonNode.Parse(template.ToJsonString());
            clone.Properties["Id"]=new ClassJsonNode{Raw=ClassJson.Q(shape.Id)};
            clone.Properties["ModelId"]=new ClassJsonNode{Raw=ClassJson.Q(own.Id)};
            clone.Properties["SourceId"]=new ClassJsonNode{Raw=ClassJson.Q(shape.StartPoint.Id)};
            clone.Properties["TargetId"]=new ClassJsonNode{Raw=ClassJson.Q(shape.EndPoint.Id)};
            clone.Properties["Visible"]=new ClassJsonNode{Raw="true"};
            clone.Properties["IsVisible"]=new ClassJsonNode{Raw="true"};
            clone.Properties.Remove("Bends");
            connectors.Items.Add(clone);added++;
            log.AppendLine("connector entry for re-import: id="+shape.Id+" model="+own.ClassName+" "+shape.StartPoint.Id+" -> "+shape.EndPoint.Id+" (template "+ClassJsonNode.Value(template,"Id")+")");
        }
        if(added==0) { log.AppendLine("no new connector to re-apply");return; }
        int countBefore=CountConnectors(app);
        string json="{\"Type\":\"Model\",\"SchemaVersion\":"+ClassJson.Q(unit.Schema)+",\"TopElementId\":"+ClassJson.Q(ClassJsonNode.Value(unit.Editor,"ModelId")??"")
            +",\"Entities\":[],\"Relations\":[],\"Editors\":["+unit.Editor.ToJsonString()+"]}";
        var result=project.ImportUnitFromJson(json,null,null);
        if(result==null)throw new InvalidOperationException("C230: エディタ再反映の結果がありません。");
        log.AppendLine("editor re-import: "+result.State);
        foreach(var e in result.Errors)log.AppendLine(e.Kind+": "+e.Message);
        if(result.State!="success" || result.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))throw new InvalidOperationException("C230: エディタ再反映が失敗または警告を返しました。");
        int countAfter=CountConnectors(app);
        log.AppendLine("connectors after re-import: "+countBefore+" -> "+countAfter);
        if(countAfter!=countBefore)throw new InvalidOperationException("C230: エディタ再反映でコネクタ数が変わりました（"+countBefore+" -> "+countAfter+"）。同じIDで上書きされていません。");
        int nodesAfter=d.Nodes.Cast<object>().Count();
        int expectedNodes=nodesBefore+newClasses.Count(x=>x.Node==null);
        log.AppendLine("nodes after re-import: "+nodesBefore+" -> "+nodesAfter+" (expected "+expectedNodes+")");
        if(nodesAfter!=expectedNodes)throw new InvalidOperationException("C230: エディタ再反映でノード数が想定と違います（"+nodesBefore+" -> "+nodesAfter+"、想定 "+expectedNodes+"）。");
        DescribeNewConnectors(app,before,log);
    }
    static void ShowNewConnectors(IApplication app,HashSet<string> before,StringBuilder log)
    {
        var d=Current(app) as IDiagram;if(d==null)return;
        int shown=0;
        foreach(var c in d.Connectors.Cast<object>().ToList())
        {
            var shape=c as IConnector;if(shape==null || before.Contains(shape.Id))continue;
            // SetVisible(true) reads back true but the saved editor keeps IsVisible=false and
            // nothing is drawn (K032). Go through the diagram's own show operation instead,
            // and record every flag the SDK exposes before and after for the next comparison.
            bool visible;try { visible=shape.IsVisible; } catch(Exception) { visible=true; }
            log.AppendLine("connector "+shape.Id+" before: IsVisible="+visible);
            try { d.ShowShape(shape); } catch(Exception ex) { log.AppendLine("ShowShape failed: "+ex.Message); }
            bool after;try { after=shape.IsVisible; } catch(Exception) { after=false; }
            log.AppendLine("connector "+shape.Id+" ShowShape: IsVisible="+after);
            if(!after)
            {
                shape.SetVisible(true);
                try { after=shape.IsVisible; } catch(Exception) { after=false; }
                log.AppendLine("connector "+shape.Id+" SetVisible(true): IsVisible="+after);
            }
            if(!after)throw new InvalidOperationException("C230: 追加した関連のコネクタを表示にできません。");
            shown++;
        }
        if(shown>0)log.AppendLine("connectors shown: "+shown);
    }
    // "a..b" / "a" / "*" into LowerBound / UpperBound (-1 for *), verified by BoundsOf.
    static void WriteBounds(IModel model,string multiplicity,StringBuilder log)
    {
        string lower,upper;int dots=multiplicity.IndexOf("..",StringComparison.Ordinal);
        if(dots>=0) { lower=multiplicity.Substring(0,dots);upper=multiplicity.Substring(dots+2); } else { lower=multiplicity;upper=multiplicity; }
        int lo=lower=="*"?0:int.Parse(lower,System.Globalization.CultureInfo.InvariantCulture);
        int hi=upper=="*"?-1:int.Parse(upper,System.Globalization.CultureInfo.InvariantCulture);
        model.SetField("LowerBound",lo);model.SetField("UpperBound",hi);
        string readBack=ClassDiagramSnapshot.BoundsOf(model);
        log.AppendLine("bounds written: "+lo+".."+hi+" read-back='"+readBack+"'");
        if(readBack!=multiplicity)throw new InvalidOperationException("C230: 多重度の読戻しが一致しません: '"+readBack+"'");
    }
    static int CountConnectors(IApplication app)
    {
        try { var d=Current(app) as IDiagram;return d==null?-1:d.Connectors.Cast<object>().Count(); } catch(Exception) { return -1; }
    }
    static HashSet<string> ConnectorIds(IApplication app)
    {
        var ids=new HashSet<string>(StringComparer.Ordinal);
        try { var d=Current(app) as IDiagram;if(d!=null)foreach(var c in d.Connectors) { var s=c as IShape;if(s!=null)ids.Add(s.Id); } } catch(Exception) { }
        return ids;
    }
    // What the product drew for a connector that appeared during this run: both ends, their
    // positions and the shape flags, so an invisible line can be told from a missing one.
    static void DescribeNewConnectors(IApplication app,HashSet<string> before,StringBuilder log)
    {
        try
        {
            var d=Current(app) as IDiagram;if(d==null)return;
            foreach(var c in d.Connectors)
            {
                var connector=c as IConnector;if(connector==null || before.Contains(connector.Id))continue;
                var from=connector.StartPoint;var to=connector.EndPoint;
                var fromModel=ClassDiagramKind.ModelOf(from);var toModel=ClassDiagramKind.ModelOf(to);
                var own=ClassDiagramKind.ModelOf(connector);
                string lineType;try { lineType=connector.LineType; } catch(Exception) { lineType="?"; }
                bool visible;try { visible=connector.IsVisible; } catch(Exception) { visible=false; }
                log.AppendLine("new connector "+connector.Id+": "+(fromModel==null?"?":fromModel.Name)+" -> "+(toModel==null?"?":toModel.Name)
                    +" model="+(own==null?"(none)":own.ClassName)+" lineType="+lineType+" visible="+visible
                    +" from@("+(from==null?"?":from.LocationX+","+from.LocationY)+") to@("+(to==null?"?":to.LocationX+","+to.LocationY)+")"
                    +" bends="+(connector.GetBends()==null?0:connector.GetBends().Cast<object>().Count()));
            }
        }
        catch(Exception ex) { log.AppendLine("new connector description failed: "+ex.Message); }
    }
    static IField FieldOf(IModel m,string name) { return m.Metaclass.GetFields().Cast<IField>().FirstOrDefault(f=>f.Name==name); }
    // Text update: member name, visibility and (attributes) type. A type is a reference to an
    // existing type model, resolved by name before anything is written; nothing is created.
    // Trial always rolls back; commit keeps the change only after the same verification succeeds.
    static string RunTextUpdate(IApplication app,IProject project,IEditor editor,ClassDiagramSnapshot snapshot,ClassDocument desired,ClassTextPreflight preflight,bool retain,StringBuilder log,Func<string,bool> confirm)
    {
        string editorId=editor.Id;string originalJson=snapshot.Document.ToJson();
        var options=new ClassSyncOptions();typeTargets.Clear();
        var idMap=new Dictionary<string,string>(snapshot.ModelIds,StringComparer.Ordinal);
        var targets=new List<ResolvedEdit>();
        List<IModel> everything=null;
        foreach(var edit in preflight.Edits)
        {
            string modelId;
            if(!idMap.TryGetValue(edit.CurrentId,out modelId))throw new InvalidOperationException("C220: 更新対象のモデルIDを特定できません。");
            var model=project.GetModelById(modelId);
            if(model==null || model.IsDeleted || model.IsProxy || !model.IsEditable)throw new InvalidOperationException("C220: 更新対象に編集不可のモデルがあります。");
            string live=ClassText.Inline(ClassText.Normalize(model.Name));
            if(live!=edit.OldText)throw new InvalidOperationException("C220: 更新対象の現在の名前が読取りと一致しません。");
            var resolved=new ResolvedEdit{Model=model,Edit=edit};
            if(edit.NameChanged)
            {
                var nameField=FieldOf(model,"Name");
                if(nameField==null || nameField.IsReference || nameField.Type!="String")throw new InvalidOperationException("C220: Name が文字列フィールドではありません。");
            }
            if(edit.VisibilityChanged)
            {
                var field=options.VisibilityFieldNames.Select(n=>FieldOf(model,n)).FirstOrDefault(f=>f!=null && !f.IsReference);
                if(field==null)throw new InvalidOperationException("C220: 可視性のフィールドが見つかりません。");
                string current=ClassDiagramSnapshot.TextOf(model,new List<string>{field.Name});string symbol;
                if(!options.VisibilityMap.TryGetValue(current,out symbol) || symbol!=edit.OldVisibility)throw new InvalidOperationException("C220: 可視性の現在値 '"+current+"' が読取りと一致しません。");
                if(!options.VisibilityValues.TryGetValue(edit.NewVisibility,out resolved.VisibilityValue))throw new InvalidOperationException("C220: 可視性の記号 '"+edit.NewVisibility+"' に対応する値がありません。");
                log.AppendLine("visibility field="+field.Name+" type="+field.Type+" current='"+current+"' -> '"+resolved.VisibilityValue+"'");
            }
            if(edit.TypeChanged)
            {
                var field=options.TypeFieldNames.Select(n=>FieldOf(model,n)).FirstOrDefault(f=>f!=null && f.IsReference);
                if(field==null)throw new InvalidOperationException("C220: 型の参照フィールドが見つかりません。");
                var currentTargets=model.GetFieldValues(field.Name).Cast<object>().OfType<IModel>().ToList();
                if(currentTargets.Count>1)throw new InvalidOperationException("C220: 型の参照が複数あります。");
                resolved.OldType=currentTargets.FirstOrDefault();
                string currentName=resolved.OldType==null?"":ClassText.Inline(ClassText.Normalize(resolved.OldType.Name));
                if(currentName!=edit.OldType)throw new InvalidOperationException("C220: 型の現在値 '"+currentName+"' が読取りと一致しません。");
                var ownerClass=model.Owner;
                if(ownerClass==null)throw new InvalidOperationException("C220: メンバの所有先を取得できません。");
                resolved.NewType=ResolveType(project,ownerClass,edit.NewType,edit.TypeKind,field.Type,options,ref everything,log);
                log.AppendLine("type field="+field.Name+" ("+field.Type+") old="+(resolved.OldType==null?"(none)":resolved.OldType.Id+" "+resolved.OldType.ClassName)+" new="+(resolved.NewType.Existing!=null?resolved.NewType.Existing.Id+" "+resolved.NewType.Existing.ClassName:"(create) "+resolved.NewType.Class.Name));
            }
            if(edit.ParametersChanged)
            {
                var ownerClass=model.Owner;
                if(ownerClass==null)throw new InvalidOperationException("C220: 操作の所有先を取得できません。");
                resolved.Arguments=PlanArguments(project,model,ownerClass,edit.NewParameters,options,ref everything,log);
            }
            if(edit.ReturnTypeChanged)
            {
                var ownerClass=model.Owner;
                var field=options.ReturnTypeFieldNames.Concat(options.TypeFieldNames).Select(n=>FieldOf(model,n)).FirstOrDefault(f=>f!=null && f.IsReference);
                if(field==null || ownerClass==null)throw new InvalidOperationException("C220: 操作に戻り値の参照フィールドがありません。");
                resolved.ReturnType=ResolveType(project,ownerClass,edit.NewReturnType,edit.ReturnTypeKind,field.Type,options,ref everything,log);
            }
            if(edit.MultiplicityChanged && (FieldOf(model,"LowerBound")==null || FieldOf(model,"UpperBound")==null))throw new InvalidOperationException("C220: 属性に LowerBound / UpperBound がありません。");
            if(edit.DefaultChanged && options.DefaultValueFieldNames.Select(n=>FieldOf(model,n)).All(f=>f==null || f.IsReference))throw new InvalidOperationException("C220: 属性に既定値のフィールドがありません。");
            log.AppendLine("edit target: model="+modelId+" class="+model.ClassName+" "+edit.Describe());
            targets.Add(resolved);
        }
        // Links: resolve both end models and the reference field on the source class. A delete
        // also looks up the relationship to learn the paired field on the other side, so the
        // input can be checked for the partner line before anything is written.
        // Members: the owner class and, for adds, the owning field and the metaclass to create
        // (Property under Attribute, Method under Operation as observed on this profile, K008)
        // plus the type model when a type is written. Deletes need the live member model.
        // idMap: input document ids to live model ids. Classes created during the apply register
        // here so members and links under them resolve on a second pass.
        var classes=new List<ResolvedClass>();
        var diagramNow=(IDiagram)editor;
        foreach(var change in preflight.Classes)
        {
            var resolved=new ResolvedClass{Change=change};
            if(change.Action=="add")
            {
                string siblingId;
                if(!idMap.TryGetValue(change.SiblingId,out siblingId))throw new InvalidOperationException("C220: 追加するクラスの隣のクラスを特定できません。");
                var sibling=project.GetModelById(siblingId);
                if(sibling==null || sibling.IsDeleted || sibling.IsProxy)throw new InvalidOperationException("C220: 追加するクラスの隣のクラスが取得できません。");
                var owner=sibling.Owner;
                if(owner==null || !owner.IsEditable)throw new InvalidOperationException("C220: 追加するクラスの所有先が編集できません。");
                IField ownerField=null;try { ownerField=sibling.GetOwnerField(); } catch(Exception) { }
                if(ownerField==null || !ownerField.IsEmbedded)throw new InvalidOperationException("C220: 追加するクラスの所有フィールドを特定できません。");
                if(owner.GetFieldValues(ownerField.Name).Cast<object>().OfType<IModel>().Any(m=>!m.IsDeleted && ClassText.Inline(ClassText.Normalize(m.Name))==change.Text))
                    throw new InvalidOperationException("C220: 同じ所有先に同じ名前のクラス '"+change.Text+"' が既にあります。");
                var siblingNode=diagramNow.Nodes.Cast<object>().OfType<INode>().FirstOrDefault(n=>{var m=ClassDiagramKind.ModelOf(n);return m!=null && m.Id==sibling.Id;});
                if(siblingNode==null)throw new InvalidOperationException("C220: 隣のクラスのノードが図にありません。");
                resolved.Owner=owner;resolved.Sibling=sibling;resolved.OwningField=ownerField;resolved.Class=sibling.Metaclass;resolved.SiblingNode=siblingNode;
                if(!diagramNow.CanAddNodeShape(sibling))log.AppendLine("CanAddNodeShape(sibling)=false: the view may not accept a new node for this metaclass");
                log.AppendLine("class target: add '"+change.Text+"' as "+resolved.Class.FullName+" under "+owner.ClassName+" '"+owner.Name+"'."+ownerField.Name+" next to '"+sibling.Name+"' node@("+siblingNode.LocationX+","+siblingNode.LocationY+" "+siblingNode.Width+"x"+siblingNode.Height+")");
            }
            else
            {
                string modelId;
                if(!idMap.TryGetValue(change.CurrentId,out modelId))throw new InvalidOperationException("C220: 削除するクラスのモデルIDを特定できません。");
                var model=project.GetModelById(modelId);
                if(model==null || model.IsDeleted || model.IsProxy || !model.IsEditable)throw new InvalidOperationException("C220: 削除するクラスが編集できません。");
                if(ClassText.Inline(ClassText.Normalize(model.Name))!=change.Text)throw new InvalidOperationException("C220: 削除するクラスの名前が読取りと一致しません。");
                // References from outside the class and outside the diagram's classes (a sequence
                // lifeline, another diagram) keep it alive; links from diagram classes go with it.
                var onDiagram=new HashSet<string>(snapshot.ModelIds.Values,StringComparer.Ordinal);
                var subtree=new HashSet<string>(Tree(model).Select(m=>m.Id),StringComparer.Ordinal);
                var outside=new List<string>();
                foreach(var member in Tree(model))
                {
                    foreach(var rel in member.GetRelationsWhere((x,f)=>x.Target!=null && x.Target.Id==member.Id && x.IsReference).Cast<IRelationship>())
                    {
                        var src=rel.Source;if(src==null)continue;
                        var top=src;int g=0;while(top.Owner!=null && g++<32 && !onDiagram.Contains(top.Id))top=top.Owner;
                        if(subtree.Contains(src.Id) || onDiagram.Contains(top.Id) || onDiagram.Contains(src.Id))continue;
                        outside.Add(src.ClassName+" '"+ClassText.Normalize(src.Name)+"'."+(rel.SourceField==null?"?":rel.SourceField.Name)+(src.Owner==null?"":" in "+src.Owner.ClassName+" '"+ClassText.Normalize(src.Owner.Name)+"'"));
                        if(outside.Count>=5)break;
                    }
                    if(outside.Count>=5)break;
                }
                if(outside.Count>0)throw new InvalidOperationException("C220: クラス '"+change.Text+"' は図の外から参照されているため削除しません。\n参照元: "+string.Join(" / ",outside.ToArray()));
                resolved.Model=model;
                log.AppendLine("class target: delete '"+change.Text+"' model="+modelId+" class="+model.ClassName+" children="+Tree(model).Count());
            }
            classes.Add(resolved);
        }
        var pendingClassIds=new HashSet<string>(preflight.Classes.Where(c=>c.Action=="add").Select(c=>c.ExpectedId),StringComparer.Ordinal);
        var members=new List<ResolvedMember>();
        var deferredMembers=new List<ClassMemberChange>();
        Action<ClassMemberChange> resolveMember=null;
        resolveMember=delegate(ClassMemberChange change) {
            string ownerId;
            if(!idMap.TryGetValue(change.OwnerId,out ownerId))throw new InvalidOperationException("C220: メンバの所有先のモデルIDを特定できません。");
            var owner=project.GetModelById(ownerId);
            if(owner==null || owner.IsDeleted || owner.IsProxy || !owner.IsEditable)throw new InvalidOperationException("C220: メンバの所有先が編集できません。");
            var resolved=new ResolvedMember{Owner=owner,Change=change};
            if(change.Action=="add")
            {
                string fieldName=change.Kind=="attribute"?"Attribute":"Operation";
                var field=FieldOf(owner,fieldName);
                if(field==null || !field.IsEmbedded)throw new InvalidOperationException("C220: "+owner.ClassName+" に所有フィールド '"+fieldName+"' がありません。");
                // Reuse the metaclass of an existing sibling of the same kind so the profile's
                // concrete class (Property / Method) is not guessed; fall back to the field type.
                var sibling=owner.GetFieldValues(fieldName).Cast<object>().OfType<IModel>().FirstOrDefault(m=>!m.IsDeleted);
                // A new class has no members yet: borrow the metaclass from the class it was
                // created next to (Method rather than the field's Operation, K054).
                if(sibling==null)
                {
                    var created=classes.FirstOrDefault(c=>c.Model!=null && c.Model.Id==owner.Id);
                    if(created!=null)sibling=created.Sibling.GetFieldValues(fieldName).Cast<object>().OfType<IModel>().FirstOrDefault(m=>!m.IsDeleted);
                }
                // The short ClassName is not accepted by AddNewModel(string,string) (K038); pass the
                // metaclass object from a sibling, or the field's declared type class when the
                // class has no member of this kind yet.
                resolved.Field=fieldName;resolved.OwningField=field;
                resolved.MemberClass=sibling!=null?sibling.Metaclass:field.TypeClass;
                if(resolved.MemberClass==null)throw new InvalidOperationException("C220: '"+fieldName+"' に作るメタクラスを特定できません。");
                resolved.ClassName=resolved.MemberClass.FullName;
                if(owner.GetFieldValues(fieldName).Cast<object>().OfType<IModel>().Any(m=>!m.IsDeleted && ClassText.Inline(ClassText.Normalize(m.Name))==change.Text))
                    throw new InvalidOperationException("C220: 同じ名前のメンバ '"+change.Text+"' が既にあります。");
                if(change.Visibility.Length>0 && !options.VisibilityValues.TryGetValue(change.Visibility,out resolved.VisibilityValue))throw new InvalidOperationException("C220: 可視性の記号 '"+change.Visibility+"' に対応する値がありません。");
                if(change.Kind=="operation" && change.ReturnType.Length>0)
                {
                    var typeField=sibling!=null?options.ReturnTypeFieldNames.Concat(options.TypeFieldNames).Select(n=>FieldOf(sibling,n)).FirstOrDefault(f=>f!=null && f.IsReference):null;
                    string typeClass=typeField!=null?typeField.Type:"Type";
                    resolved.ReturnType=ResolveType(project,owner,change.ReturnType,change.ReturnTypeKind,typeClass,options,ref everything,log);
                }
                if(change.Kind=="attribute" && change.Type.Length>0)
                {
                    if(everything==null)everything=Tree(project.DesignModel).ToList();
                    var typeField=sibling!=null?options.TypeFieldNames.Select(n=>FieldOf(sibling,n)).FirstOrDefault(f=>f!=null && f.IsReference):null;
                    string typeClass=typeField!=null?typeField.Type:"Type";
                    resolved.TypeTarget=ResolveType(project,owner,change.Type,change.TypeKind,typeClass,options,ref everything,log);resolved.TypeField=typeField!=null?typeField.Name:options.TypeFieldNames[0];
                }
                if(change.InsertBeforeId!=null)
                {
                    string beforeId;
                    if(idMap.TryGetValue(change.InsertBeforeId,out beforeId))resolved.InsertBefore=project.GetModelById(beforeId);
                    if(resolved.InsertBefore==null || resolved.InsertBefore.IsDeleted)log.AppendLine("insert position: following member not resolved, appending at the end");
                }
                resolved.Parameters=change.Parameters;
                log.AppendLine("member target: add "+change.Kind+" '"+change.Text+"' under "+owner.ClassName+" '"+owner.Name+"' field="+fieldName+" class="+resolved.ClassName+(resolved.TypeTarget!=null?" type="+(resolved.TypeTarget.Existing!=null?resolved.TypeTarget.Existing.Id:"(create)"):"")+(resolved.InsertBefore!=null?" before="+resolved.InsertBefore.Name:" at end")+(change.Kind=="operation" && !string.IsNullOrEmpty(change.Parameters)?" params=("+change.Parameters+")":""));
            }
            else
            {
                string memberId;
                if(!idMap.TryGetValue(change.CurrentId,out memberId))throw new InvalidOperationException("C220: 削除するメンバのモデルIDを特定できません。");
                var member=project.GetModelById(memberId);
                if(member==null || member.IsDeleted || member.IsProxy || !member.IsEditable)throw new InvalidOperationException("C220: 削除するメンバが編集できません。");
                if(ClassText.Inline(ClassText.Normalize(member.Name))!=change.Text)throw new InvalidOperationException("C220: 削除するメンバの名前が読取りと一致しません。");
                // A type definition child (StructureType etc.) may be referenced as the type of
                // other members; refuse when anything outside the member itself points at it.
                // References from outside the member's own subtree (a sequence message calling
                // the operation, an attribute typed by this definition) keep it alive; the
                // stop reason names them so the input can be judged. References from its own
                // arguments do not count.
                var incoming=member.GetRelationsWhere((rel,f)=>rel.Target!=null && rel.Target.Id==member.Id && rel.IsReference).Cast<IRelationship>()
                    .Where(rel=>{var src=rel.Source;int g=0;while(src!=null && g++<32){if(src.Id==member.Id)return false;src=src.Owner;}return true;}).ToList();
                if(incoming.Count>0)
                {
                    var who=incoming.Take(5).Select(rel=>{var src=rel.Source;string field=rel.SourceField!=null?rel.SourceField.Name:"?";var owner=src==null?null:src.Owner;
                        return (src==null?"?":src.ClassName+" '"+ClassText.Normalize(src.Name)+"'")+"."+field+(owner==null?"":" in "+owner.ClassName+" '"+ClassText.Normalize(owner.Name)+"'");}).ToArray();
                    foreach(var w in who)log.AppendLine("referenced by: "+w);
                    throw new InvalidOperationException("C220: メンバ '"+change.Text+"' は "+incoming.Count+" 件の参照先になっているため削除しません。\n参照元: "+string.Join(" / ",who)+(incoming.Count>5?" ...":""));
                }
                // An operation owns its arguments and they go with it; anything else with
                // children (a type definition with members) stays.
                if(change.Kind!="operation" && member.GetChildren().Cast<IModel>().Any(m=>!m.IsDeleted))throw new InvalidOperationException("C220: メンバ '"+change.Text+"' は子モデルを持つため削除しません。");
                resolved.Member=member;
                log.AppendLine("member target: delete "+change.Kind+" '"+change.Text+"' model="+memberId+" class="+member.ClassName);
            }
            members.Add(resolved);
        };
        foreach(var change in preflight.Members)
        {
            if(change.Action=="add" && pendingClassIds.Contains(change.OwnerId)) { deferredMembers.Add(change);continue; }
            resolveMember(change);
        }
        var links=new List<ResolvedLink>();
        var deferredLinks=new List<ClassLinkChange>();
        Action<ClassLinkChange> resolveLink=null;
        // The expected document: the input plus what the product does on the other side of a
        // two-field relationship. Partner lines are dropped for deletes here and added after
        // a Relate once the partner field has been observed.
        var effective=desired.Copy();
        resolveLink=delegate(ClassLinkChange change) {
            string fromId,toId;
            if(!idMap.TryGetValue(change.FromId,out fromId) || !idMap.TryGetValue(change.ToId,out toId))throw new InvalidOperationException("C220: 関連の両端のモデルIDを特定できません。");
            var from=project.GetModelById(fromId);var to=project.GetModelById(toId);
            if(from==null || to==null || from.IsDeleted || to.IsDeleted || from.IsProxy || to.IsProxy || !from.IsEditable)throw new InvalidOperationException("C220: 関連の両端に編集不可のモデルがあります。");
            var field=FieldOf(from,change.Field);
            if(field==null || !field.IsReference)throw new InvalidOperationException("C220: "+from.ClassName+" に参照フィールド '"+change.Field+"' がありません。");
            if(!IsA(to,field.Type))throw new InvalidOperationException("C220: '"+change.Field+"' の型 "+field.Type+" に "+to.ClassName+" は入りません。");
            var present=from.GetFieldValues(change.Field).Cast<object>().OfType<IModel>().Any(m=>m.Id==toId);
            var resolved=new ResolvedLink{From=from,To=to,Change=change};
            if(change.Action=="add")
            {
                if(present)throw new InvalidOperationException("C220: 追加する関連 "+change.FromAlias+" -> "+change.ToAlias+" : "+change.Field+" は既に存在します。");
                if(field.UpperBound>=0 && from.GetFieldValues(change.Field).Cast<object>().Count()>=field.UpperBound)throw new InvalidOperationException("C220: '"+change.Field+"' の多重度の上限に達しています。");
            }
            else
            {
                if(!present)throw new InvalidOperationException("C220: 削除する関連 "+change.FromAlias+" -> "+change.ToAlias+" : "+change.Field+" が図のモデルにありません。");
                IRelationship relation=null;
                try { relation=from.GetRelationsOf(to).Cast<IRelationship>().FirstOrDefault(x=>(x.SourceField!=null && x.SourceField.Name==change.Field) || (x.TargetField!=null && x.TargetField.Name==change.Field)); } catch(Exception ex) { log.AppendLine("GetRelationsOf failed: "+ex.Message); }
                if(relation!=null)
                {
                    var partner=relation.SourceField!=null && relation.SourceField.Name==change.Field?relation.TargetField:relation.SourceField;
                    resolved.RelationId=relation.Id;resolved.PartnerField=partner==null?"":partner.Name;
                    log.AppendLine("delete link relation="+relation.Id+" fields="+change.Field+"/"+resolved.PartnerField+" twoWay="+relation.IsTwoWay);
                    // One relationship carries both fields (K027): removing this side removes the
                    // partner line too. If the input still lists it, drop it from the expected
                    // document instead of failing the read-back.
                    if(resolved.PartnerField.Length>0 && RemovePartnerLine(effective,change.ToAlias,change.FromAlias,resolved.PartnerField))
                        log.AppendLine("partner line dropped from the expected input: "+change.ToAlias+" -> "+change.FromAlias+" : "+resolved.PartnerField);
                }
            }
            log.AppendLine("link target: "+change.Action+" "+from.ClassName+" '"+from.Name+"' -["+change.Field+" : "+field.Type+"]-> "+to.ClassName+" '"+to.Name+"'");
            links.Add(resolved);
        };
        foreach(var change in preflight.Links)
        {
            if(change.Action=="add" && (pendingClassIds.Contains(change.FromId) || pendingClassIds.Contains(change.ToId))) { deferredLinks.Add(change);continue; }
            resolveLink(change);
        }
        if(deferredMembers.Count>0 || deferredLinks.Count>0)log.AppendLine("deferred until the new classes exist: members="+deferredMembers.Count+" links="+deferredLinks.Count);
        string summary="名前 "+preflight.NameCount+" / 可視性 "+preflight.VisibilityCount+" / 型 "+preflight.TypeCount+" / クラス追加 "+preflight.ClassAddCount+" / クラス削除 "+preflight.ClassDeleteCount+" / メンバ追加 "+preflight.MemberAddCount+" / メンバ削除 "+preflight.MemberDeleteCount+" / 関連追加 "+preflight.LinkAddCount+" / 関連削除 "+preflight.LinkDeleteCount;
        string confirmation=(retain?"コピーのプロジェクトで実行してください。\nメンバ "+targets.Count+"件・関連 "+links.Count+"件（"+summary+"）を更新し、読戻しが一致したときだけ確定します。":"コピーのプロジェクトで実行してください。\nメンバ "+targets.Count+"件・関連 "+links.Count+"件（"+summary+"）を更新し、読戻しを照合した後に必ず取り消します。")
            +"\n自動保存はしません。Undo/Redo と保存再読込は手動で確認してください。";
        // A new relationship gets a connector the product keeps hidden in the saved editor
        // (K032/K033); SDK flags do not reach it. The fix re-applies the editor with that
        // connector marked visible, which needs the editor exported before any change
        // (ExportModelUnit refuses a dirty project, K055).
        ClassEditorCapture.Unit unit=null;
        // The editor re-import does not come back on Rollback (K034), so the trial only
        // proves the relationship write; the visible line is applied on commit alone.
        // Class nodes come from AddNodeShape and are visible (K053), so only link additions
        // need the editor capture and its saved-project precondition.
        if(preflight.LinkAddCount>0 && retain)
        {
            var diagramModel=ClassDiagramKind.ModelOf(editor);
            if(diagramModel==null || string.IsNullOrEmpty(project.Path))throw new InvalidOperationException("C220: 保存済みのプロジェクトで実行してください。");
            if(project.HasUnsavedChanges())throw new InvalidOperationException("C220: 関連の追加には更新前の図の退避が必要です。プロジェクトを保存してから実行してください（自動保存はしません）。");
            try { unit=ClassEditorCapture.ReadUnit(project,diagramModel,editor,log); }
            catch(Exception ex) { throw new InvalidOperationException("C220: 更新前の図を退避できません。保存済みの状態で実行してください（未保存扱いのときはコピーを開き直してください）。\n"+ex.Message); }
            if(unit.Editor==null || string.IsNullOrEmpty(unit.Schema))throw new InvalidOperationException("C220: 図の Editor JSON を退避できません。");
            var existing=unit.Editor["Connectors"];
            if(preflight.LinkAddCount>0 && (existing==null || existing.Items==null || existing.Items.Count==0))throw new InvalidOperationException("C220: 図に既存の線がないため、線の雛形を取れません。");
            log.AppendLine("editor captured for re-import: schema="+unit.Schema+" connectors="+(existing==null || existing.Items==null?0:existing.Items.Count));
        }
        if(!confirm(confirmation))return "本文更新: 中止（確認で取消）";
        if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || Current(app)==null || Current(app).Id!=editorId
            || ClassDiagramSnapshot.Read((IDiagram)Current(app),new ClassSyncOptions(),new StringBuilder()).Document.ToJson()!=originalJson)
            throw new InvalidOperationException("C220: 確認中に対象の図が変化しました。");
        string stage="開始前";
        var transaction=project.BeginUndoTransaction(false);
        Action apply=delegate {
            foreach(var c in classes.Where(x=>x.Change.Action=="add"))
            {
                stage="クラスの追加";
                var created=c.Owner.AddNewModel(c.OwningField,c.Class);
                if(created==null)throw new InvalidOperationException("C230: クラスを作成できませんでした。");
                created.SetField("Name",c.Change.Text);
                if(ClassText.Inline(ClassText.Normalize(created.Name))!=c.Change.Text)throw new InvalidOperationException("C230: 作成したクラスの名前の読戻しが一致しません。");
                c.Model=created;idMap[c.Change.ExpectedId]=created.Id;
                log.AppendLine("created class "+created.ClassName+" id="+created.Id+" name='"+created.Name+"' owner="+(created.Owner==null?"?":created.Owner.Name));
                // The node: first through the diagram API using the sibling's element definition,
                // then by checking what the product may have auto-created; the commit falls back to
                // the editor re-import when neither yields a node.
                stage="クラスのノード追加";
                var d=(IDiagram)Current(app);
                INode node=d.Nodes.Cast<object>().OfType<INode>().FirstOrDefault(n=>{var m=ClassDiagramKind.ModelOf(n);return m!=null && m.Id==created.Id;});
                if(node==null)
                {
                    var def=(c.SiblingNode as IRepresentation)==null?null:(c.SiblingNode as IRepresentation).ViewDefinition as IElementDef;
                    log.AppendLine("sibling node view definition: "+((c.SiblingNode as IRepresentation)==null || (c.SiblingNode as IRepresentation).ViewDefinition==null?"(none)":(c.SiblingNode as IRepresentation).ViewDefinition.GetType().Name)+" asElementDef="+(def!=null));
                    try { var added=d.AddNodeShape(created,def);log.AppendLine("AddNodeShape: "+(added==null?"null":"ok")); }
                    catch(Exception ex) { log.AppendLine("AddNodeShape failed: "+ex.Message); }
                    node=d.Nodes.Cast<object>().OfType<INode>().FirstOrDefault(n=>{var m=ClassDiagramKind.ModelOf(n);return m!=null && m.Id==created.Id;});
                }
                if(node!=null)
                {
                    try { node.SetLocationAt(c.SiblingNode.LocationX+c.SiblingNode.Width+40,c.SiblingNode.LocationY);node.SetSizeAt(c.SiblingNode.Width,c.SiblingNode.Height); }
                    catch(Exception ex) { log.AppendLine("node placement failed: "+ex.Message); }
                    c.Node=node;
                    log.AppendLine("class node "+node.Id+" at ("+node.LocationX+","+node.LocationY+") visible="+node.IsVisible);
                }
                else if(unit!=null)log.AppendLine("class node not created through the API; the commit will add it to the editor");
                else throw new InvalidOperationException("C230: クラスのノードを作成できませんでした（AddNodeShape がノードを返しませんでした）。");
            }
            if(deferredMembers.Count>0 || deferredLinks.Count>0)
            {
                stage="追加クラス配下の解決";
                foreach(var change in deferredMembers)resolveMember(change);
                foreach(var change in deferredLinks)resolveLink(change);
            }
            foreach(var t in targets)
            {
                var model=t.Model;var edit=t.Edit;
                if(edit.NameChanged)
                {
                    stage="名前の更新";
                    model.SetField("Name",edit.NewText);
                    string readBack=model.GetFieldString("Name");
                    if(readBack!=edit.NewText)throw new InvalidOperationException("C230: SetField(Name) 後の読戻しが一致しません: '"+readBack+"'");
                }
                if(edit.VisibilityChanged)
                {
                    stage="可視性の更新";
                    var field=options.VisibilityFieldNames.Select(n=>FieldOf(model,n)).First(f=>f!=null && !f.IsReference);
                    model.SetField(field.Name,t.VisibilityValue);
                    string readBack=ClassDiagramSnapshot.TextOf(model,new List<string>{field.Name});string symbol;
                    if(!options.VisibilityMap.TryGetValue(readBack,out symbol) || symbol!=edit.NewVisibility)throw new InvalidOperationException("C230: 可視性の読戻しが一致しません: '"+readBack+"'");
                }
                if(edit.ParametersChanged)
                {
                    stage="引数の更新";
                    ApplyArguments(model,t.Arguments,options,log);
                }
                if(edit.ReturnTypeChanged)
                {
                    stage="戻り値の更新";
                    var field=options.ReturnTypeFieldNames.Concat(options.TypeFieldNames).Select(n=>FieldOf(model,n)).First(f=>f!=null && f.IsReference);
                    var newType=t.ReturnType.Materialize(log);
                    foreach(var oldType in model.GetFieldValues(field.Name).Cast<object>().OfType<IModel>().ToList())model.UnRelate(field.Name,oldType);
                    model.Relate(field.Name,newType);
                    var after=model.GetFieldValues(field.Name).Cast<object>().OfType<IModel>().ToList();
                    if(after.Count!=1 || after[0].Id!=newType.Id)throw new InvalidOperationException("C230: 戻り値の読戻しが一致しません（"+after.Count+"件）。");
                }
                if(edit.MultiplicityChanged) { stage="多重度の更新";WriteBounds(model,edit.NewMultiplicity,log); }
                if(edit.DefaultChanged)
                {
                    stage="既定値の更新";
                    var field=options.DefaultValueFieldNames.Select(n=>FieldOf(model,n)).First(f=>f!=null && !f.IsReference);
                    model.SetField(field.Name,edit.NewDefault);
                    string readBack=ClassText.Inline(ClassText.Normalize(model.GetFieldString(field.Name)));
                    if(readBack!=edit.NewDefault)throw new InvalidOperationException("C230: 既定値の読戻しが一致しません: '"+readBack+"'");
                }
                if(edit.TypeChanged)
                {
                    stage="型の更新";
                    var field=options.TypeFieldNames.Select(n=>FieldOf(model,n)).First(f=>f!=null && f.IsReference);
                    var newType=t.NewType.Materialize(log);
                    if(t.OldType!=null)model.UnRelate(field.Name,t.OldType);
                    model.Relate(field.Name,newType);
                    var after=model.GetFieldValues(field.Name).Cast<object>().OfType<IModel>().ToList();
                    if(after.Count!=1 || after[0].Id!=newType.Id)throw new InvalidOperationException("C230: 型の読戻しが一致しません（"+after.Count+"件）。");
                }
            }
            log.AppendLine("applied "+targets.Count+" member edits: read-back matched");
            bool appendedMembers=false;
            foreach(var m in members)
            {
                if(m.Change.Action=="add")
                {
                    stage="メンバの追加";
                    // AddNewModel appends (K039). Insert before the retained sibling that follows in
                    // the input through AddNewModelAt (marked experimental in the SDK); on any
                    // failure fall back to appending and let the order residual be tolerated.
                    IModel created=null;
                    if(m.InsertBefore!=null)
                    {
                        var siblings=m.Owner.GetFieldValues(m.Field).Cast<object>().OfType<IModel>().ToList();
                        int index=siblings.FindIndex(x=>x.Id==m.InsertBefore.Id);
                        if(index>=0)
                        {
                            try { created=m.Owner.AddNewModelAt(m.OwningField,m.MemberClass,"before",index);log.AppendLine("AddNewModelAt before index "+index+": "+(created==null?"null":"ok")); }
                            catch(Exception ex) { log.AppendLine("AddNewModelAt failed, appending instead: "+ex.Message);created=null; }
                        }
                    }
                    if(created==null) { created=m.Owner.AddNewModel(m.OwningField,m.MemberClass);appendedMembers=true; }
                    if(created==null)throw new InvalidOperationException("C230: メンバを作成できませんでした。");
                    created.SetField("Name",m.Change.Text);
                    if(m.VisibilityValue!=null)
                    {
                        var vf=options.VisibilityFieldNames.Select(n=>FieldOf(created,n)).FirstOrDefault(f=>f!=null && !f.IsReference);
                        if(vf==null)throw new InvalidOperationException("C230: 作成したメンバに可視性フィールドがありません。");
                        created.SetField(vf.Name,m.VisibilityValue);
                    }
                    if(m.Change.IsStatic)
                    {
                        var sf=options.StaticFieldNames.Select(n=>FieldOf(created,n)).FirstOrDefault(f=>f!=null && !f.IsReference);
                        if(sf!=null)created.SetField(sf.Name,true);
                    }
                    if(m.TypeTarget!=null)created.Relate(m.TypeField,m.TypeTarget.Materialize(log));
                    if(m.ReturnType!=null)
                    {
                        var rf=options.ReturnTypeFieldNames.Concat(options.TypeFieldNames).Select(n=>FieldOf(created,n)).FirstOrDefault(f=>f!=null && f.IsReference);
                        if(rf==null)throw new InvalidOperationException("C230: 作成した操作に戻り値の参照フィールドがありません。");
                        created.Relate(rf.Name,m.ReturnType.Materialize(log));
                    }
                    if(m.Change.Kind=="attribute" && m.Change.Multiplicity.Length>0)WriteBounds(created,m.Change.Multiplicity,log);
                    if(m.Change.Kind=="attribute" && m.Change.Default.Length>0)
                    {
                        var df=options.DefaultValueFieldNames.Select(n=>FieldOf(created,n)).FirstOrDefault(f=>f!=null && !f.IsReference);
                        if(df==null)throw new InvalidOperationException("C230: 作成した属性に既定値のフィールドがありません。");
                        created.SetField(df.Name,m.Change.Default);
                    }
                    if(m.Change.Kind=="operation" && !string.IsNullOrEmpty(m.Parameters))
                    {
                        // Arguments are children of the new operation; their metaclass comes from
                        // any existing argument, so this is resolved only now that the parent exists.
                        var argumentPlan=PlanArguments(project,created,m.Owner,m.Parameters,options,ref everything,log);
                        ApplyArguments(created,argumentPlan,options,log);
                    }
                    log.AppendLine("created "+created.ClassName+" id="+created.Id+" name='"+created.Name+"' owner="+(created.Owner==null?"?":created.Owner.Name));
                }
                else
                {
                    stage="メンバの削除";
                    string id=m.Member.Id;
                    m.Member.Delete();
                    var check=project.GetModelById(id);
                    if(check!=null && !check.IsDeleted)throw new InvalidOperationException("C230: メンバの削除が反映されていません。");
                    log.AppendLine("deleted member id="+id);
                }
            }
            if(members.Count>0)log.AppendLine("applied "+members.Count+" member additions/deletions");
            int connectorsBefore=CountConnectors(app);var connectorIdsBefore=ConnectorIds(app);
            foreach(var l in links)
            {
                stage=l.Change.Action=="add"?"関連の追加":"関連の削除";
                bool wanted=l.Change.Action=="add";
                Func<bool> presentNow=()=>l.From.GetFieldValues(l.Change.Field).Cast<object>().OfType<IModel>().Any(m=>m.Id==l.To.Id);
                // The partner side of the same relationship may already have done this.
                if(presentNow()==wanted) { log.AppendLine("already "+(wanted?"present":"absent")+" through the partner field: "+l.Change.FromAlias+" -> "+l.Change.ToAlias+" : "+l.Change.Field);continue; }
                if(wanted)l.From.Relate(l.Change.Field,l.To);else l.From.UnRelate(l.Change.Field,l.To);
                if(presentNow()!=wanted)throw new InvalidOperationException("C230: 関連の読戻しが一致しません: "+l.Change.FromAlias+" -> "+l.Change.ToAlias+" : "+l.Change.Field);
                // Observe what the product did on the other side; an add's partner field is
                // learned here and its line joins the expected document.
                try
                {
                    var relations=l.From.GetRelationsOf(l.To).Cast<IRelationship>().ToList();
                    log.AppendLine("after "+l.Change.Action+": relations "+l.From.Name+"->"+l.To.Name+" = ["+string.Join(", ",relations.Select(x=>x.Id+" "+(x.SourceField==null?"-":x.SourceField.Name)+"/"+(x.TargetField==null?"-":x.TargetField.Name)).ToArray())+"]");
                    if(wanted)
                    {
                        var mine=relations.FirstOrDefault(x=>(x.SourceField!=null && x.SourceField.Name==l.Change.Field) || (x.TargetField!=null && x.TargetField.Name==l.Change.Field));
                        var partner=mine==null?null:(mine.SourceField!=null && mine.SourceField.Name==l.Change.Field?mine.TargetField:mine.SourceField);
                        if(partner!=null && partner.Name!=l.Change.Field && AddPartnerLine(effective,l.Change.ToAlias,l.Change.FromAlias,partner,options))
                            log.AppendLine("partner line added to the expected input: "+l.Change.ToAlias+" -> "+l.Change.FromAlias+" : "+partner.Name);
                    }
                }
                catch(Exception ex) { log.AppendLine("GetRelationsOf after write failed: "+ex.Message); }
            }
            foreach(var c in classes.Where(x=>x.Change.Action=="delete"))
            {
                stage="クラスの削除";
                string id=c.Model.Id;var dd=(IDiagram)Current(app);int nodesBefore=dd.Nodes.Cast<object>().Count();
                // Deleting the model alone may leave its node behind as a shape without a model
                // (K057). Delete through the shape with deleteModel=true, which removes both; when
                // the class has no node on this diagram, delete the model directly.
                var ownNodes=dd.Nodes.Cast<object>().OfType<INode>().Where(n=>{var m=ClassDiagramKind.ModelOf(n);return m!=null && m.Id==id;}).ToList();
                if(ownNodes.Count>0)
                {
                    foreach(var n in ownNodes) { try { n.Delete(true); } catch(Exception ex) { log.AppendLine("shape delete failed: "+ex.Message); } }
                }
                var stillThere=project.GetModelById(id);
                if(stillThere!=null && !stillThere.IsDeleted)c.Model.Delete();
                var check=project.GetModelById(id);
                if(check!=null && !check.IsDeleted)throw new InvalidOperationException("C230: クラスの削除が反映されていません。");
                int nodesAfter=dd.Nodes.Cast<object>().Count();
                int orphan=dd.Nodes.Cast<object>().OfType<INode>().Count(n=>{var m=ClassDiagramKind.ModelOf(n);return m==null || m.IsDeleted;});
                log.AppendLine("deleted class id="+id+" via "+(ownNodes.Count>0?"shape":"model")+" nodes "+nodesBefore+" -> "+nodesAfter+" orphan nodes="+orphan);
            }
            if(links.Count>0 || classes.Any(x=>x.Change.Action=="add"))
            {
                log.AppendLine("connectors on the diagram: "+connectorsBefore+" -> "+CountConnectors(app));
                DescribeNewConnectors(app,connectorIdsBefore,log);
                // The product creates the connector for a new relationship with IsVisible=false
                // (K029); the model is right and only the flag hides the line. Show it and
                // verify the flag reads back true.
                stage="コネクタの表示";
                if(unit!=null)ReapplyEditorWithVisibleConnectors(app,project,unit,connectorIdsBefore,log,classes.Where(x=>x.Change.Action=="add").ToList());
            }
            stage="更新後の照合";
            if(typeTargets.Values.Any(x=>x.Created)) { AddCreatedTypeLines(effective,snapshot,log);appendedMembers=true; }
            AddSystemLinesForNewClasses(app,effective,classes.Where(x=>x.Change.Action=="add" && x.Model!=null).ToList(),options,log);
            VerifyAgainst(app,editorId,effective,"更新後",log,appendedMembers);
        };
        Action rollback=delegate {stage="取消";transaction.Rollback();};
        Action verifyRestored=delegate {stage="取消後の照合";VerifyRestored(app,editorId,originalJson,log);};
        var lines=new List<string>();
        if(retain)
        {
            var completion=new ClassCommitTrial();
            completion.Run(apply,delegate {stage="確定";transaction.Commit();},rollback,verifyRestored);
            foreach(var error in new[]{completion.ApplyError,completion.CommitError,completion.RollbackError,completion.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            Refresh(app,log);
            lines.Add("適用と照合: "+(completion.Applied?"一致":"失敗 ("+stage+")"));
            lines.Add("確定: "+(completion.Committed?"成功":completion.Applied?"失敗":"未実施"));
            if(completion.Committed)
            {
                bool still=Matches(delegate {VerifyAgainst(app,editorId,effective,"確定後",log);},log);
                lines.Add("確定後の再照合: "+(still?"一致":"不一致（診断ファイル参照）"));
                log.AppendLine("undo availability: project="+project.CanUndo+" workspace="+app.Workspace.CanUndo()+" (nested transaction; see K113)");
                lines.Add("Undo/Redo・保存再読込: 手動で確認してください");
            }
            else
            {
                lines.Add("取消API: "+(completion.RollbackReturned?"正常終了":"失敗"));
                lines.Add("復元照合: "+(completion.Restored?"一致":"未確認または不一致。保存せずにコピーを開き直してください"));
            }
            return (applyMode?"PlantUMLの反映":"本文更新の確定 (UPDATE-C001)")+"\n"+string.Join("\n",lines.ToArray());
        }
        var trial=new ClassRollbackTrial();
        trial.Run(apply,rollback,verifyRestored);
        foreach(var error in new[]{trial.ApplyError,trial.RollbackError,trial.VerifyError})if(error!=null)log.AppendLine(error.ToString());
        Refresh(app,log);
        lines.Add("一時適用と照合: "+(trial.Applied?"一致":"失敗 ("+stage+")"));
        if(classes.Count>0)lines.Add("クラス 追加 "+preflight.ClassAddCount+" / 削除 "+preflight.ClassDeleteCount+(preflight.ClassAddCount>0?"（ノードの表示は確定時に整えます）":""));
        if(members.Count>0)lines.Add("メンバ 追加 "+preflight.MemberAddCount+" / 削除 "+preflight.MemberDeleteCount);
        if(links.Count>0)lines.Add("関連 追加 "+preflight.LinkAddCount+" / 削除 "+preflight.LinkDeleteCount+(preflight.LinkAddCount>0?"（線の表示は確定時に付けます）":""));
        lines.Add("取消API: "+(trial.RollbackReturned?"正常終了":"失敗"));
        lines.Add("復元照合: "+(trial.Restored?"一致":"未確認または不一致。保存せずにコピーを開き直してください"));
        return "本文更新の試行 (UPDATE-C000)\n"+string.Join("\n",lines.ToArray());
    }
    // Everything Run() produces for the caller (ribbon dialog or MCP response).
    public sealed class Outcome
    {
        public bool Succeeded, Applied, Committed;
        public string Summary="", Details="", ReportJson, CurrentPuml, Log="", ErrorMessage;
        public int Changes, Limitations, StopReasons;
    }
    [ThreadStatic] static bool applyMode;
    // Compare the PlantUML text with the editor's diagram; optionally apply (trial rolls
    // back, retain commits). confirm() gates the write; the caller supplies dialogs or an
    // automatic yes. No file dialogs, no result windows: the caller decides what to show.
    public static Outcome Run(IApplication app,IEditor editor,string pumlText,string sourceLabel,bool trial,bool retain,bool apply,Func<string,bool> confirm)
    {
        var log=new StringBuilder();var outcome=new Outcome();string screenshot=null;string snapshotNote=null;
        trial=trial||retain;
        targetEditorId=editor==null?null:editor.Id;targetModelId=editor==null?null:editor.ModelId;applyMode=apply;
        try
        {
            string reject=ClassDiagramKind.Reject(editor);
            if(reject!=null)throw new InvalidOperationException(reject);
            var diagram=(IDiagram)editor;var project=app.Workspace.CurrentProject;
            if(pumlText==null || pumlText.Length>300000)throw new InvalidOperationException("C120: 入力は300KB以下にしてください。");
            log.AppendLine("PlantUML source: "+sourceLabel);
            var parser=new ClassPumlParser();
            ClassDocument desired;
            try { desired=parser.Parse(pumlText); }
            catch(InvalidOperationException parseError)
            {
                // The offending lines go to the local diagnostic file only.
                var match=Regex.Match(parseError.Message,@"E120: (\d+)行目:");int row;
                if(match.Success && int.TryParse(match.Groups[1].Value,out row))
                {
                    var inputLines=pumlText.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
                    for(int i=Math.Max(0,row-3);i<Math.Min(inputLines.Length,row+2);i++)log.AppendLine((i+1)+": "+inputLines[i]);
                }
                throw;
            }
            foreach(var ignored in parser.Ignored)log.AppendLine("無視した行: "+ignored);
            var skippedLinks=parser.Ignored.Where(x=>x.Contains("宣言のない別名")).ToList();
            if(skippedLinks.Count>0)snapshotNote="宣言のない別名の関連行 "+skippedLinks.Count+" 件を無視（クラスの削除に伴う）";
            var snapshot=ClassDiagramSnapshot.Read(diagram,new ClassSyncOptions(),log);
            if(snapshotNote!=null)snapshot.Limitations.Add(snapshotNote);
            var current=snapshot.Document;
            var plan=ClassSyncPlan.Build(current,desired,()=>Guid.NewGuid().ToString());
            outcome.CurrentPuml=ClassPumlWriter.Write(current);
            outcome.ReportJson="{\"version\":1,\"project\":"+ClassJson.Q(project==null?"":project.Id)+",\"diagram\":"+ClassJson.Q(editor.Id)
                +",\"current\":"+current.ToJson()+",\"desired\":"+desired.ToJson()+",\"plan\":"+plan.ToJson()
                +",\"limitations\":"+ClassJson.Json(snapshot.Limitations.ToArray())
                +",\"modelIds\":"+ClassJson.Json(snapshot.ModelIds.ToDictionary(p=>p.Key,p=>(object)p.Value))
                +",\"geometry\":"+ClassJson.Json(snapshot.Geometry.ToDictionary(p=>p.Key,p=>(object)p.Value))+"}";
            foreach(var c in plan.Changes)log.AppendLine(c.Action+" "+c.Kind+" line="+c.Line+" id="+c.Id+" detail="+c.Detail);
            foreach(var warning in snapshot.Limitations)log.AppendLine("要照合: "+warning);
            var preflight=ClassTextPreflight.Check(current,desired,plan);
            outcome.Changes=plan.Changes.Count;outcome.Limitations=snapshot.Limitations.Count;outcome.StopReasons=preflight.Reasons.Count;
            screenshot=(trial?"適用前の比較結果（更新後の残差ではありません）\n":"現在の図と入力の比較結果（図は変更していません）\n")+ClassAudit.Summary(plan,snapshot.Limitations.Count)
                +"\f変更候補の内訳（入力行と種類のみ）\n"+ClassAudit.Reasons(plan)
                +"\f要照合項目 "+snapshot.Limitations.Count+"件\n"+(snapshot.Limitations.Count==0?"なし":string.Join("\n",snapshot.Limitations.ToArray()))
                +"\f"+preflight.Summary();
            log.AppendLine(screenshot.Replace('\f','\n'));
            outcome.Summary=ClassAudit.Summary(plan,snapshot.Limitations.Count)+(trial?"":"\n図への反映は行いません。")+"\n反映の停止理由: "+preflight.Reasons.Count+"件（診断表示）";
            log.AppendLine("Scope: "+(project==null?"":project.Id)+" / "+editor.ModelId+" / "+editor.Id);
            if(trial)
            {
                if(plan.Changes.Count==0) { outcome.Summary="差分候補なし。図は変更していません。";outcome.Succeeded=true; }
                else
                {
                    if(!preflight.Candidate)throw new InvalidOperationException("C231: 反映できるのは、クラスの追加削除と改名、属性・操作の追加削除と名前・可視性・型・引数・戻り値・多重度・既定値の変更、関連の追加削除です。クラスのキーワード・所有先の変更、package の追加削除は扱えません。\n"+preflight.Summary());
                    if(project==null)throw new InvalidOperationException("C220: プロジェクトを取得できません。");
                    string result=RunTextUpdate(app,project,editor,snapshot,desired,preflight,retain,log,confirm);
                    outcome.Summary=result;
                    outcome.Applied=result.Contains("一致") && !result.Contains("失敗");
                    outcome.Committed=result.Contains("確定: 成功");
                    outcome.Succeeded=retain?outcome.Committed:outcome.Applied;
                    screenshot=result+"\f会社PC内の試行診断\n"+log.ToString();
                }
            }
            else outcome.Succeeded=true;
        }
        catch(Exception ex)
        {
            outcome.ErrorMessage=ex.Message;
            outcome.Summary=(trial?"反映を完了できませんでした。診断表示を確認してください。":"図全体の読取り検証を完了できませんでした。")+"\n"+ex.Message;
            log.AppendLine(ex.ToString());screenshot=null;
        }
        finally { targetEditorId=null;targetModelId=null;applyMode=false; }
        outcome.Log=log.ToString();
        outcome.Details=screenshot??outcome.Log;
        return outcome;
    }
    // Ribbon entry: pick the file, run against the active editor, save the report, show the result.
    public static void Preview(IApplication app,bool trial=false,bool retain=false,bool apply=false)
    {
        var editor=app.Workspace.CurrentEditor;
        string reject=ClassDiagramKind.Reject(editor);
        if(reject!=null) { ClassExperiment.Summary=reject;ClassExperiment.Details=reject;ClassExperiment.Show(app);return; }
        string path=app.Window.UI.ShowOpenFileDialog(apply?"反映するPlantUML":"図と比較するPlantUML（PlantUmlToolのクラス図出力）","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
        if(string.IsNullOrEmpty(path))return;
        string pumlText;
        try { if(new FileInfo(path).Length>300000)throw new InvalidOperationException("C120: 入力は300KB以下にしてください。");pumlText=File.ReadAllText(path,new UTF8Encoding(false,true)); }
        catch(Exception ex) { ClassExperiment.Summary=ex.Message;ClassExperiment.Details=ex.ToString();ClassExperiment.Show(app);return; }
        var outcome=Run(app,editor,pumlText,path,trial,retain,apply,message=>app.Window.UI.ShowConfirmDialog(message,ClassExperiment.Title));
        ClassExperiment.Summary=outcome.Summary;
        string stem=ClassExperiment.SaveReport(apply?"apply":"preview",outcome.Log,outcome.ReportJson,outcome.CurrentPuml);
        if(stem!=null)ClassExperiment.Summary+="\n診断保存先: "+stem+".txt";
        ClassExperiment.Details=outcome.Details;
        ClassExperiment.Show(app);
    }
}
// END TRANSCRIBED 61-class-sync-runtime.cs

// BEGIN TRANSCRIBED 60-class-sync.cs
// Pure class-diagram synchronization core. No SDK or filesystem dependencies.
// IDs in these documents are local parser keys, never Next Design model IDs.
public sealed class ClassElement
{
    public string Id, Kind, Parent, Text = "";
    public int Order, Line;
    public Dictionary<string,string> Attributes = new Dictionary<string,string>(StringComparer.Ordinal);
    public Dictionary<string,string[]> Links = new Dictionary<string,string[]>(StringComparer.Ordinal);
    public string Attr(string key) { string value; return Attributes.TryGetValue(key,out value) ? value : ""; }
    public string Link(string key) { string[] value; return Links.TryGetValue(key,out value) && value.Length>0 ? value[0] : null; }
    public ClassElement Copy()
    {
        return new ClassElement { Id=Id,Kind=Kind,Parent=Parent,Text=Text,Order=Order,Line=Line,
            Attributes=new Dictionary<string,string>(Attributes,StringComparer.Ordinal),
            Links=Links.ToDictionary(p=>p.Key,p=>p.Value.ToArray(),StringComparer.Ordinal) };
    }
}

public sealed class ClassDocument
{
    public List<ClassElement> Elements = new List<ClassElement>();
    public bool HasTitle;
    public static readonly string[] Kinds = { "diagram","package","class","attribute","operation","literal","link" };
    public static readonly string[] MemberKinds = { "attribute","operation","literal" };
    public static bool IsContainerKeyword(string keyword)
    {
        return string.Equals(keyword,"package",StringComparison.OrdinalIgnoreCase) || string.Equals(keyword,"component",StringComparison.OrdinalIgnoreCase);
    }
    public ClassElement Root { get { return Elements.Single(e=>e.Kind=="diagram"); } }
    public void Validate()
    {
        if(Elements.Count>5000 || Elements.Any(e=>e==null || string.IsNullOrEmpty(e.Id) || !Kinds.Contains(e.Kind))
            || Elements.Select(e=>e.Id).Distinct().Count()!=Elements.Count)
            throw new InvalidOperationException("C201: 要素の型・ID・件数が不正です。");
        var index=Elements.ToDictionary(e=>e.Id);
        if(Elements.Count(e=>e.Kind=="diagram")!=1 || Elements.Any(e=>e.Kind=="diagram" ? e.Parent!=null : e.Parent==null || !index.ContainsKey(e.Parent)))
            throw new InvalidOperationException("C201: 図の所有構造が不正です。");
        foreach(var e in Elements)
        {
            var path=new HashSet<string>();var at=e;
            while(at!=null) { if(!path.Add(at.Id))throw new InvalidOperationException("C201: 所有構造が循環しています。");at=at.Parent==null?null:index[at.Parent]; }
            if(MemberKinds.Contains(e.Kind) && index[e.Parent].Kind!="class")throw new InvalidOperationException("C201: メンバの親がクラスではありません。");
            if(e.Kind=="link")
            {
                if(e.Link("from")==null || e.Link("to")==null)throw new InvalidOperationException("C201: 関連の両端がありません。");
                foreach(var id in e.Links.Values.SelectMany(v=>v))
                    if(!index.ContainsKey(id) || index[id].Kind!="class")throw new InvalidOperationException("C201: 関連の接続先が図のクラスではありません。");
            }
        }
    }
    public ClassDocument Copy() { return new ClassDocument{HasTitle=HasTitle,Elements=Elements.Select(e=>e.Copy()).ToList()}; }
    public string ToJson()
    {
        return ClassJson.Json(ClassJson.Obj("HasTitle",HasTitle,"Elements",Elements.Select(e=>ClassJson.Obj(
            "Id",e.Id,"Kind",e.Kind,"Parent",e.Parent,"Text",e.Text,"Order",e.Order,"Line",e.Line,
            "Attributes",e.Attributes.ToDictionary(p=>p.Key,p=>(object)p.Value),
            "Links",e.Links.ToDictionary(p=>p.Key,p=>(object)p.Value))).ToArray()));
    }
    // An enum member without any rendered detail is indistinguishable from a literal in
    // PlantUML text, so both sides of a comparison classify it the same way.
    public void NormalizeLiterals()
    {
        var index=Elements.ToDictionary(e=>e.Id);
        foreach(var e in Elements.Where(e=>e.Kind=="attribute"))
        {
            var owner=index[e.Parent];
            if(owner.Attr("keyword")!="enum")continue;
            if(e.Attributes.Values.All(string.IsNullOrEmpty)) { e.Kind="literal";e.Attributes.Clear(); }
        }
    }
    public static ClassDocument Parse(string input) { return new ClassPumlParser().Parse(input); }
}

// Minimal JSON writer shared by the pure core and the runtime.
public static class ClassJson
{
    public static string Q(string s)
    {
        if (s == null) throw new ArgumentNullException("s");
        var b = new StringBuilder("\"");
        foreach (char c in s) { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); }
        return b.Append('"').ToString();
    }
    public static Dictionary<string,object> Obj(params object[] values)
    { var d=new Dictionary<string,object>(); for(int i=0;i<values.Length;i+=2)d.Add((string)values[i],values[i+1]); return d; }
    public static string Json(object value)
    {
        if(value==null)return "null";
        var s=value as string; if(s!=null)return Q(s);
        var d=value as Dictionary<string,object>; if(d!=null)return "{"+string.Join(",",d.Select(k=>Q(k.Key)+":"+Json(k.Value)))+"}";
        var list=value as System.Collections.IEnumerable; if(list!=null)return "["+string.Join(",",list.Cast<object>().Select(Json))+"]";
        if(value is bool)return (bool)value?"true":"false";
        return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
    }
}

// Small lossless JSON tree: scalar spelling and unknown properties are preserved.
// Used to cut one editor out of an exported unit without re-serializing its values.
public class ClassJsonNode
{
    public Dictionary<string,ClassJsonNode> Properties;
    public List<ClassJsonNode> Items;
    public string Raw;
    public ClassJsonNode this[string key] { get { ClassJsonNode value;return Properties!=null && Properties.TryGetValue(key,out value)?value:null; } }
    public string StringValue()
    {
        if(Raw==null || !Raw.StartsWith("\"",StringComparison.Ordinal))throw new InvalidOperationException("C180: JSON文字列が必要です。");
        var b=new StringBuilder();
        for(int i=1;i<Raw.Length-1;i++)
        {
            char c=Raw[i];if(c!='\\'){b.Append(c);continue;}
            c=Raw[++i];
            switch(c) {
                case '"':b.Append('"');break;case '\\':b.Append('\\');break;case '/':b.Append('/');break;
                case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;
                case 'u':b.Append((char)int.Parse(Raw.Substring(i+1,4),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture));i+=4;break;
                default:throw new InvalidOperationException("C180: JSONエスケープが不正です。");
            }
        }
        return b.ToString();
    }
    public static string Value(ClassJsonNode node,string key)
    {
        var child=node==null?null:node[key];
        return child==null || child.Raw==null || !child.Raw.StartsWith("\"",StringComparison.Ordinal) ? null : child.StringValue();
    }
    public string ToJsonString()
    {
        if(Properties!=null)return "{"+string.Join(",",Properties.Select(p=>ClassJson.Q(p.Key)+":"+p.Value.ToJsonString()))+"}";
        if(Items!=null)return "["+string.Join(",",Items.Select(n=>n.ToJsonString()))+"]";
        return Raw;
    }
    public static ClassJsonNode Parse(string text)
    {
        var reader=new Reader{Text=text};var result=reader.Read(0);reader.Space();
        if(reader.At!=text.Length)throw new InvalidOperationException("C180: JSONの末尾が不正です。");return result;
    }
    class Reader
    {
        public string Text;public int At;
        public void Space(){while(At<Text.Length && (Text[At]==' ' || Text[At]=='\t' || Text[At]=='\r' || Text[At]=='\n'))At++;}
        bool Take(char c){Space();if(At<Text.Length && Text[At]==c){At++;return true;}return false;}
        void Need(char c){if(!Take(c))throw new InvalidOperationException("C180: JSONの区切りが不正です。");}
        string Quoted()
        {
            Space();int start=At;Need('"');
            while(At<Text.Length)
            {
                char c=Text[At++];if(c=='"')return Text.Substring(start,At-start);
                if(c<32)break;
                if(c=='\\')
                {
                    if(At>=Text.Length)break;c=Text[At++];
                    if(c=='u') { if(At+4>Text.Length || !Regex.IsMatch(Text.Substring(At,4),"^[0-9a-fA-F]{4}$"))break;At+=4; }
                    else if("\"\\/bfnrt".IndexOf(c)<0)break;
                }
            }
            throw new InvalidOperationException("C180: JSON文字列が不正です。");
        }
        public ClassJsonNode Read(int depth)
        {
            if(depth>128)throw new InvalidOperationException("C180: JSONの入れ子が深すぎます。");
            Space();if(At>=Text.Length)throw new InvalidOperationException("C180: JSONが途中で終了しています。");
            if(Text[At]=='"')return new ClassJsonNode{Raw=Quoted()};
            if(Take('{')) {
                var result=new ClassJsonNode{Properties=new Dictionary<string,ClassJsonNode>(StringComparer.Ordinal)};
                if(Take('}'))return result;
                do { string key=new ClassJsonNode{Raw=Quoted()}.StringValue();Need(':');
                    if(result.Properties.ContainsKey(key))throw new InvalidOperationException("C180: JSONの属性名が重複しています。");
                    result.Properties.Add(key,Read(depth+1));if(Take('}'))return result;Need(',');
                }while(true);
            }
            if(Take('[')) {
                var result=new ClassJsonNode{Items=new List<ClassJsonNode>()};if(Take(']'))return result;
                do { result.Items.Add(Read(depth+1));if(Take(']'))return result;Need(','); }while(true);
            }
            int begin=At;
            while(At<Text.Length && Text[At]!=',' && Text[At]!=']' && Text[At]!='}' && !char.IsWhiteSpace(Text[At]))At++;
            string raw=Text.Substring(begin,At-begin);
            if(raw!="true" && raw!="false" && raw!="null" && !Regex.IsMatch(raw,@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$"))
                throw new InvalidOperationException("C180: JSONの値が不正です。");
            return new ClassJsonNode{Raw=raw};
        }
    }
}

// Text rules copied from the PlantUmlTool exporter so both sides normalize identically.
public static class ClassText
{
    public static string ShortHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            var t = s ?? "";
            for (var i = 0; i < t.Length; i++) { h ^= t[i]; h *= 16777619; }
            return h.ToString("x8");
        }
    }
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(); var space = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch)) { if (!space && sb.Length > 0) sb.Append(' '); space = true; }
            else { sb.Append(ch); space = false; }
        }
        return sb.ToString().Trim();
    }
    public static string Inline(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
    }
    public static string Quote(string s) { return "\"" + (s ?? "").Replace("\"", "'") + "\""; }
    public static string AsciiAlias(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? ""))
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
            else if (ch == '_' || ch == ' ' || ch == '-' || ch == '.') sb.Append('_');
        }
        var alias = sb.ToString().Trim('_');
        while (alias.Contains("__")) alias = alias.Replace("__", "_");
        if (alias.Length == 0) return "";
        if (alias[0] >= '0' && alias[0] <= '9') alias = "L" + alias;
        return alias;
    }
    public static bool IsSystemName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.StartsWith("$", StringComparison.Ordinal) || s.StartsWith("___", StringComparison.Ordinal);
    }
}

// Profile-dependent name tables. Defaults are the PlantUmlTool tables plus the DeSIDE
// additions recorded in NdMcp. Unknown names fall back with a limitation, never silently.
public sealed class ClassSyncOptions
{
    public bool EmitEmbedded = false;
    public bool EmitRoleNames = true;
    public bool EmitMultiplicity = true;
    public bool EmitStereotypes = true;
    public bool EmitUnknownStereotype = true;
    public string DefaultLink = "-->";
    // Type definitions live in owning fields of the class named after their metaclass (K046).
    // A type that does not exist is created in this field unless the input names another
    // kind with "Type <<StructureType>>".
    public string DefaultTypeKind = "ImplementationDataType";
    public string EmbeddedLink = "*--";
    public string FallbackLink = "--";
    public Dictionary<string,string> KeywordMap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
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
    public Dictionary<string,string> StereotypeMap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Class", "" }, { "クラス", "" },
    };
    public Dictionary<string,string> MemberKindMap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
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
        // DeSIDE type definitions are rendered as attributes (NdMcp RegisterDesideMaps).
        { "StructureType", "attribute" }, { "PointerType", "attribute" }, { "NumericalType", "attribute" },
        { "ArrayType", "attribute" }, { "EnumeratorType", "attribute" }, { "StringType", "attribute" },
        { "ImplementationDataType", "attribute" }, { "BooleanType", "attribute" }, { "VoidType", "attribute" },
    };
    public Dictionary<string,string> LinkMap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
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
        // DeSIDE fields as the deployed PlantUmlTool draws them (observed in a real export on
        // 2026-09-21). Arrows are never compared; this table only keeps the written-back
        // PlantUML in the same shape as the export so the two files can be diffed.
        { "SuperClasses", "--|>" }, { "SubClasses", "<|--" },
        { "Whole", "--*" }, { "Parts", "*--" },
        { "Related", "-->" }, { "RelateFrom", "<--" },
        { "Children", "o--" },
        // Dependency fields observed on 2026-09-21 (K037). The deployed exporter has no entry
        // for them and prints "-->"; arrows are not compared, so this only shapes _current.puml.
        { "Supplier", "..>" }, { "Client", "<.." },
    };
    public Dictionary<string,string> VisibilityMap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        { "public", "+" }, { "公開", "+" }, { "+", "+" },
        { "private", "-" }, { "非公開", "-" }, { "-", "-" },
        { "protected", "#" }, { "限定公開", "#" }, { "#", "#" },
        { "package", "~" }, { "internal", "~" }, { "パッケージ", "~" }, { "~", "~" },
    };
    // Symbol -> stored value when writing visibility back. Public/Private were observed on the
    // real profile; Protected/Package are the UML names and are unverified.
    public Dictionary<string,string> VisibilityValues = new Dictionary<string,string>(StringComparer.Ordinal)
    {
        { "+", "Public" }, { "-", "Private" }, { "#", "Protected" }, { "~", "Package" },
    };
    public List<string> TypeFieldNames = new List<string> { "Type", "DataType", "AttributeType", "PropertyType", "型", "データ型", "属性型" };
    public List<string> ReturnTypeFieldNames = new List<string> { "ReturnType", "Return", "ResultType", "戻り値", "戻り値型", "返り値" };
    public List<string> MultiplicityFieldNames = new List<string> { "Multiplicity", "Cardinality", "多重度" };
    public List<string> VisibilityFieldNames = new List<string> { "Visibility", "Accessibility", "AccessModifier", "可視性", "公開範囲" };
    public List<string> DefaultValueFieldNames = new List<string> { "DefaultValue", "Default", "InitialValue", "既定値", "初期値" };
    public List<string> ParameterFieldNames = new List<string> { "Parameters", "Parameter", "Arguments", "引数", "パラメータ" };
    public List<string> StaticFieldNames = new List<string> { "IsStatic", "Static", "静的", "クラスメンバ" };
    public List<string> AbstractFieldNames = new List<string> { "IsAbstract", "Abstract", "抽象" };
    public static readonly string[] StateViewDefinitionNames = { "ステートマシン図", "状態遷移図", "StateMachineDiagram", "StateMachine" };
    public static readonly string[] ClassEditorTypes = { "ERDiagram", "TreeDiagram" };
}

// Parses the grammar the PlantUmlTool class exporter writes. Anything else stops with E120.
public sealed class ClassPumlParser
{
    static readonly Regex ClassLine=new Regex(@"^(?<kw>abstract\s+class|class|interface|enum|entity|struct|package|component|annotation|abstract)\s+(?:""(?<qname>[^""]*)""|(?<name>[^\s""{<]+))(?:\s+as\s+(?<alias>[^\s{<]+))?(?:\s*<<(?<st>[^>]*)>>)?\s*(?<open>\{)?\s*$");
    static readonly Regex LinkLine=new Regex(@"^(?<from>[A-Za-z0-9_]+)\s+(?:""(?<fm>[^""]*)""\s+)?(?<arrow>(?:<\||<|o|\*)?[-.]+(?:\|>|>|o|\*)?)\s+(?:""(?<tm>[^""]*)""\s+)?(?<to>[A-Za-z0-9_]+)\s*(?::\s*(?<label>.*?))?\s*$");
    // An operation is name(params)[ : ret]. The parameter list ends at the parenthesis that
    // balances the first "(", so a return type such as decltype(f(a,b)) keeps its own
    // parentheses out of the parameters (K061). Anything else is an attribute.
    sealed class OperationParts { public string Name, Parameters, ReturnType; }
    static OperationParts SplitOperation(string rest)
    {
        int open=rest.IndexOf('(');
        if(open<=0)return null;
        // The exporter never puts a space before "(": "Idle (default)" is a bare name.
        string name=rest.Substring(0,open);
        if(char.IsWhiteSpace(name[name.Length-1]) || name.IndexOf(':')>=0)return null;
        int depth=0,close=-1;
        for(int i=open;i<rest.Length;i++)
        {
            if(rest[i]=='(')depth++;
            else if(rest[i]==')' && --depth==0) { close=i;break; }
        }
        if(close<0)return null;
        string tail=rest.Substring(close+1);
        string returnType="";
        if(tail.Trim().Length>0)
        {
            var m=Regex.Match(tail,@"^\s*:\s*(?<ret>.*)$");
            if(!m.Success)return null;
            returnType=m.Groups["ret"].Value.Trim();
            if(returnType.Contains(" [") || returnType.Contains(" = "))return null;
        }
        return new OperationParts{Name=name,Parameters=rest.Substring(open+1,close-open-1),ReturnType=returnType};
    }
    class Frame { public string Kind, Id, Keyword; }
    class Pending { public int Line; public string From, To, Arrow, FromMult, ToMult, Label; }
    ClassDocument doc; int order;
    Dictionary<string,ClassElement> aliases=new Dictionary<string,ClassElement>(StringComparer.Ordinal);
    List<Pending> pending=new List<Pending>();
    public List<string> Ignored=new List<string>();
    static Exception Error(int line,string message) { return new InvalidOperationException("E120: "+line+"行目: "+message); }
    public ClassDocument Parse(string input)
    {
        doc=new ClassDocument();order=0;
        var root=new ClassElement{Id="root",Kind="diagram"};doc.Elements.Add(root);
        var lines=(input??"").Replace("\r\n","\n").Replace('\r','\n').Split('\n');
        var stack=new Stack<Frame>();
        for(int i=0;i<lines.Length;i++)
        {
            int n=i+1;string line=lines[i].Trim();
            if(line.Length==0 || line.StartsWith("'",StringComparison.Ordinal))continue;
            if(line=="@startuml" || line=="@enduml")continue;
            if(line.StartsWith("title ",StringComparison.Ordinal)) { root.Text=line.Substring(6).Trim();root.Line=n;doc.HasTitle=true;continue; }
            if(line.StartsWith("hide ",StringComparison.Ordinal) || line.StartsWith("show ",StringComparison.Ordinal) || line.StartsWith("skinparam",StringComparison.Ordinal)
                || line.StartsWith("!",StringComparison.Ordinal) || line=="left to right direction" || line=="top to bottom direction")
            { Ignored.Add(n+": "+line);continue; }
            var top=stack.Count>0?stack.Peek():null;
            if(line=="}") { if(top==null)throw Error(n,"対応する開き括弧がありません。");stack.Pop();continue; }
            if(top!=null && top.Kind=="class")
            {
                if(line=="--" || line==".." || line=="==" || line=="__")continue;
                ParseMember(line,top,n);continue;
            }
            var m=ClassLine.Match(line);
            if(m.Success)
            {
                string keyword=Regex.Replace(m.Groups["kw"].Value,@"\s+"," ");
                if(keyword=="abstract")keyword="abstract class";
                string name=m.Groups["qname"].Success?m.Groups["qname"].Value:m.Groups["name"].Value;
                bool open=m.Groups["open"].Success;
                string parent=top==null?"root":top.Id;
                if(keyword=="package" && !m.Groups["alias"].Success)
                {
                    if(!open)throw Error(n,"package の後に { が必要です。");
                    var existing=doc.Elements.FirstOrDefault(e=>e.Kind=="package" && e.Parent==parent && e.Text==name);
                    if(existing==null) { existing=new ClassElement{Id="pkg"+doc.Elements.Count,Kind="package",Parent=parent,Text=name,Order=order++,Line=n};doc.Elements.Add(existing); }
                    stack.Push(new Frame{Kind="package",Id=existing.Id});continue;
                }
                string alias=m.Groups["alias"].Success?m.Groups["alias"].Value:UniqueAlias(name);
                if(aliases.ContainsKey(alias))throw Error(n,"別名 "+alias+" が重複しています。");
                var element=new ClassElement{Id="c:"+alias,Kind="class",Parent=parent,Text=name,Order=order++,Line=n};
                element.Attributes["keyword"]=keyword;
                element.Attributes["stereotype"]=m.Groups["st"].Success?m.Groups["st"].Value.Trim():"";
                element.Attributes["alias"]=alias;
                doc.Elements.Add(element);aliases.Add(alias,element);
                if(open)stack.Push(new Frame{Kind=ClassDocument.IsContainerKeyword(keyword)?"container":"class",Id=element.Id,Keyword=keyword});
                continue;
            }
            m=LinkLine.Match(line);
            if(m.Success)
            {
                string arrow=m.Groups["arrow"].Value;
                if(Regex.IsMatch(arrow,@"^[^-.]*[-.][^-.]*$"))arrow=Regex.Replace(arrow,@"([-.])",  "$1$1");
                pending.Add(new Pending{Line=n,From=m.Groups["from"].Value,To=m.Groups["to"].Value,Arrow=arrow,
                    FromMult=m.Groups["fm"].Success?m.Groups["fm"].Value:"",ToMult=m.Groups["tm"].Success?m.Groups["tm"].Value:"",
                    Label=m.Groups["label"].Success?m.Groups["label"].Value.Trim():""});
                continue;
            }
            throw Error(n,"解釈できない行です: "+line);
        }
        if(stack.Count>0)throw Error(lines.Length,"閉じ括弧が不足しています。");
        foreach(var p in pending)ResolveLink(p);
        doc.NormalizeLiterals();
        doc.Validate();return doc;
    }
    string UniqueAlias(string name)
    {
        string alias=ClassText.AsciiAlias(name);
        if(alias.Length==0)alias="C"+ClassText.ShortHash(name);
        string candidate=alias;int suffix=2;
        while(aliases.ContainsKey(candidate))candidate=alias+"_"+(suffix++);
        return candidate;
    }
    void ParseMember(string line,Frame owner,int n)
    {
        string rest=line;string visibility="";
        if(rest.Length>1 && "+-#~".IndexOf(rest[0])>=0 && char.IsWhiteSpace(rest[1])) { visibility=rest.Substring(0,1);rest=rest.Substring(1).TrimStart(); }
        bool isStatic=false,isAbstract=false;
        while(true)
        {
            if(rest.StartsWith("{static}",StringComparison.Ordinal)) { isStatic=true;rest=rest.Substring(8).TrimStart();continue; }
            if(rest.StartsWith("{abstract}",StringComparison.Ordinal)) { isAbstract=true;rest=rest.Substring(10).TrimStart();continue; }
            break;
        }
        var element=new ClassElement{Id="m"+doc.Elements.Count,Parent=owner.Id,Order=order++,Line=n};
        // The exporter writes operations as name(params)[ : ret]. Anything else with
        // parentheses (a type such as "uint8 (raw)", a name with brackets) is an attribute.
        var operation=SplitOperation(rest);
        if(operation!=null)
        {
            element.Kind="operation";
            element.Text=operation.Name;
            // The exporter prints argument names only (K019), so names are what is compared;
            // "name : Type <<Kind>>" keeps its types for writing in a separate, ignored attribute.
            string rawParameters=operation.Parameters.Trim();
            element.Attributes["parameters"]=string.Join(", ",ClassTextPreflight.ParameterNames(rawParameters));
            if(rawParameters!=element.Attributes["parameters"])element.Attributes["parameterTypes"]=rawParameters;
            string returnType=operation.ReturnType;
            element.Attributes["visibility"]=visibility;element.Attributes["static"]=isStatic?"true":"";
            element.Attributes["abstract"]=isAbstract?"true":"";element.Attributes["returnType"]=returnType;
        }
        else
        {
            string defaultValue="",multiplicity="",type="";
            int eq=rest.IndexOf(" = ",StringComparison.Ordinal);
            if(eq>=0) { defaultValue=rest.Substring(eq+3).Trim();rest=rest.Substring(0,eq).TrimEnd(); }
            if(rest.EndsWith("]",StringComparison.Ordinal))
            {
                int bracket=rest.LastIndexOf(" [",StringComparison.Ordinal);
                if(bracket>=0) { multiplicity=rest.Substring(bracket+2,rest.Length-bracket-3).Trim();rest=rest.Substring(0,bracket).TrimEnd(); }
            }
            int colon=rest.IndexOf(" : ",StringComparison.Ordinal);
            if(colon>=0) { type=rest.Substring(colon+3).Trim();rest=rest.Substring(0,colon).TrimEnd(); }
            // "Type <<StructureType>>" names the type-definition metaclass to create when the
            // type does not exist yet; it is stripped from the compared type text.
            string typeKind="";
            var kindMatch=Regex.Match(type,@"^(.*?)\s*<<([^>]+)>>$");
            if(kindMatch.Success) { type=kindMatch.Groups[1].Value.Trim();typeKind=kindMatch.Groups[2].Value.Trim(); }
            if(rest.Length==0)throw Error(n,"メンバ名がありません。");
            bool bare=visibility.Length==0 && !isStatic && !isAbstract && type.Length==0 && multiplicity.Length==0 && defaultValue.Length==0;
            if(bare && owner.Keyword=="enum") { element.Kind="literal";element.Text=rest; }
            else
            {
                element.Kind="attribute";element.Text=rest;
                element.Attributes["visibility"]=visibility;element.Attributes["static"]=isStatic?"true":"";
                element.Attributes["type"]=type;element.Attributes["multiplicity"]=multiplicity;element.Attributes["default"]=defaultValue;
                if(typeKind.Length>0)element.Attributes["typeKind"]=typeKind;
            }
        }
        doc.Elements.Add(element);
    }
    static string Directed(string arrow) { if(arrow=="--")return "-->";if(arrow=="..")return "..>";return arrow; }
    void ResolveLink(Pending p)
    {
        ClassElement from,to;
        // A line whose end is not declared usually means its class declaration was removed
        // to delete the class while its link lines stayed. The class's links go with it, so
        // such lines are skipped (and listed) rather than rejected.
        bool fromOk=aliases.TryGetValue(p.From,out from),toOk=aliases.TryGetValue(p.To,out to);
        if(!fromOk || !toOk)
        {
            string missing=!fromOk?p.From:p.To;
            if(!Regex.IsMatch(missing,@"^[A-Za-z0-9_]+$"))throw Error(p.Line,"未宣言の別名です: "+missing);
            Ignored.Add(p.Line+": 宣言のない別名 "+missing+" の関連行（クラスの削除に伴い無視）");
            return;
        }
        bool generalization=p.Arrow=="--|>" || p.Arrow=="..|>" || p.Arrow=="<|--" || p.Arrow=="<|..";
        // The exporter joins labels as "a / b"; when the first direction is an anonymous field the
        // line reads ": / b" after trimming, which still means two directions.
        int split=p.Label.IndexOf(" / ",StringComparison.Ordinal);
        bool emptyFirst=split<0 && p.Label.StartsWith("/ ",StringComparison.Ordinal);
        bool twoWay=!generalization && (split>=0 || emptyFirst || p.FromMult.Length>0);
        if(!twoWay) { Add(p.Line,from,to,p.Arrow,p.Label,p.ToMult);return; }
        string first=emptyFirst?"":split>=0?p.Label.Substring(0,split).Trim():p.Label;
        string second=emptyFirst?p.Label.Substring(2).Trim():split>=0?p.Label.Substring(split+3).Trim():p.Label;
        Add(p.Line,from,to,Directed(p.Arrow),first,p.ToMult);
        Add(p.Line,to,from,Directed(p.Arrow),second,p.FromMult);
    }
    void Add(int line,ClassElement from,ClassElement to,string arrow,string label,string toMult)
    {
        var e=new ClassElement{Id="l"+doc.Elements.Count,Kind="link",Parent="root",Text=label,Order=order++,Line=line};
        e.Attributes["arrow"]=arrow;e.Attributes["field"]=label;e.Attributes["toMultiplicity"]=toMult;
        e.Links["from"]=new[]{from.Id};e.Links["to"]=new[]{to.Id};
        doc.Elements.Add(e);
    }
}

// Writes a document back in the exporter's grammar and order, so a snapshot can be
// compared textually against PlantUmlTool output.
public static class ClassPumlWriter
{
    class Line { public string From,To,Arrow,Label,Field,FromMult,ToMult; public string SortKey { get { return From+To+Arrow+Field+Label; } } }
    public static string Write(ClassDocument doc)
    {
        var sb=new StringBuilder();var index=doc.Elements.ToDictionary(e=>e.Id);
        sb.Append("@startuml\n");
        if(doc.HasTitle && ClassText.Normalize(doc.Root.Text).Length>0)sb.Append("title ").Append(ClassText.Inline(ClassText.Normalize(doc.Root.Text))).Append('\n');
        sb.Append("hide empty members\n\n");
        var classes=doc.Elements.Where(e=>e.Kind=="class").OrderBy(e=>e.Order).ToList();
        var roots=classes.Where(c=>index[c.Parent].Kind!="class").ToList();
        var groups=new List<string>();var byPackage=new Dictionary<string,List<ClassElement>>(StringComparer.Ordinal);
        foreach(var c in roots)
        {
            string key=string.Join("/",PackagePath(c,index));
            if(!byPackage.ContainsKey(key)) { byPackage[key]=new List<ClassElement>();groups.Add(key); }
            byPackage[key].Add(c);
        }
        foreach(var key in groups)
        {
            var members=byPackage[key];var path=PackagePath(members[0],index);int depth=0;
            if(key.Length>0) { for(int i=0;i<path.Length;i++)LineAt(sb,i,"package "+ClassText.Quote(path[i])+" {");depth=path.Length; }
            foreach(var c in members)WriteNode(sb,doc,index,c,depth);
            for(int i=depth-1;i>=0;i--)LineAt(sb,i,"}");
            LineAt(sb,0,"");
        }
        var lines=MergeLinks(doc,index);
        foreach(var l in lines.OrderBy(l=>l.SortKey,StringComparer.Ordinal))
        {
            var t=new StringBuilder();t.Append(l.From);
            if(l.FromMult.Length>0)t.Append(' ').Append(ClassText.Quote(l.FromMult));
            t.Append(' ').Append(l.Arrow);
            if(l.ToMult.Length>0)t.Append(' ').Append(ClassText.Quote(l.ToMult));
            t.Append(' ').Append(l.To);
            if(l.Label.Length>0)t.Append(" : ").Append(l.Label);
            LineAt(sb,0,t.ToString());
        }
        if(lines.Count>0)LineAt(sb,0,"");
        sb.Append("@enduml\n");
        return sb.ToString();
    }
    static string[] PackagePath(ClassElement c,Dictionary<string,ClassElement> index)
    {
        var path=new List<string>();var at=index[c.Parent];
        while(at.Kind=="package") { path.Insert(0,at.Text);at=index[at.Parent]; }
        return path.ToArray();
    }
    static void WriteNode(StringBuilder sb,ClassDocument doc,Dictionary<string,ClassElement> index,ClassElement c,int depth)
    {
        var head=new StringBuilder();
        head.Append(c.Attr("keyword")).Append(' ').Append(ClassText.Quote(c.Text)).Append(" as ").Append(c.Attr("alias"));
        if(c.Attr("stereotype").Length>0)head.Append(" <<").Append(c.Attr("stereotype")).Append(">>");
        var children=doc.Elements.Where(e=>e.Kind=="class" && e.Parent==c.Id).OrderBy(e=>e.Order).ToList();
        if(ClassDocument.IsContainerKeyword(c.Attr("keyword")))
        {
            if(children.Count>0) { LineAt(sb,depth,head+" {");foreach(var child in children)WriteNode(sb,doc,index,child,depth+1);LineAt(sb,depth,"}"); }
            else LineAt(sb,depth,head.ToString());
            return;
        }
        var members=doc.Elements.Where(e=>e.Parent==c.Id && ClassDocument.MemberKinds.Contains(e.Kind)).OrderBy(e=>e.Order).ToList();
        var attributes=members.Where(m=>m.Kind!="operation").Select(Render).ToList();
        var operations=members.Where(m=>m.Kind=="operation").Select(Render).ToList();
        if(attributes.Count==0 && operations.Count==0)LineAt(sb,depth,head.ToString());
        else
        {
            LineAt(sb,depth,head+" {");
            foreach(var a in attributes)LineAt(sb,depth+1,a);
            if(attributes.Count>0 && operations.Count>0)LineAt(sb,depth+1,"--");
            foreach(var o in operations)LineAt(sb,depth+1,o);
            LineAt(sb,depth,"}");
        }
        foreach(var child in children)WriteNode(sb,doc,index,child,depth);
    }
    public static string Render(ClassElement m) { return Render(m,false); }
    public static string Render(ClassElement m,bool forComparison)
    {
        if(m.Kind=="literal")return m.Text;
        var sb=new StringBuilder();
        if(m.Attr("visibility").Length>0)sb.Append(m.Attr("visibility")).Append(' ');
        if(m.Attr("static")=="true")sb.Append("{static} ");
        if(m.Kind=="operation")
        {
            if(m.Attr("abstract")=="true")sb.Append("{abstract} ");
            sb.Append(m.Text).Append('(').Append(m.Attr("parameterTypes").Length>0?m.Attr("parameterTypes"):m.Attr("parameters")).Append(')');
            if(!forComparison && m.Attr("returnType").Length>0)sb.Append(" : ").Append(m.Attr("returnType"));
            return sb.ToString();
        }
        sb.Append(m.Text);
        if(m.Attr("type").Length>0)sb.Append(" : ").Append(m.Attr("type"));
        if(!forComparison && m.Attr("multiplicity").Length>0)sb.Append(" [").Append(m.Attr("multiplicity")).Append(']');
        if(m.Attr("default").Length>0)sb.Append(" = ").Append(m.Attr("default"));
        return sb.ToString();
    }
    static string Undirected(string arrow) { if(arrow=="-->")return "--";if(arrow=="..>")return "..";return arrow; }
    static List<Line> MergeLinks(ClassDocument doc,Dictionary<string,ClassElement> index)
    {
        var links=doc.Elements.Where(e=>e.Kind=="link").OrderBy(e=>e.Order).ToList();
        var result=new List<Line>();var consumed=new HashSet<int>();
        for(int i=0;i<links.Count;i++)
        {
            if(consumed.Contains(i))continue;
            var a=links[i];
            var line=new Line{From=index[a.Link("from")].Attr("alias"),To=index[a.Link("to")].Attr("alias"),Arrow=a.Attr("arrow"),Label=a.Text,Field=a.Attr("field"),FromMult="",ToMult=a.Attr("toMultiplicity")};
            int partner=-1;
            for(int j=i+1;j<links.Count;j++)
            {
                if(consumed.Contains(j))continue;var b=links[j];
                if(b.Link("from")!=a.Link("to") || b.Link("to")!=a.Link("from") || b.Attr("arrow")!=a.Attr("arrow"))continue;
                if(a.Attr("arrow")=="--|>" || a.Attr("arrow")=="..|>")continue;
                partner=j;break;
            }
            if(partner>=0)
            {
                var other=links[partner];consumed.Add(partner);
                line.FromMult=other.Attr("toMultiplicity");line.Arrow=Undirected(line.Arrow);
                if(other.Text.Length>0 && other.Text!=a.Text)line.Label=a.Text+" / "+other.Text;
            }
            result.Add(line);
        }
        return result;
    }
    static void LineAt(StringBuilder sb,int depth,string text) { for(int i=0;i<depth;i++)sb.Append("  ");sb.Append(text).Append('\n'); }
}

public sealed class ClassChange { public string Action, Kind, Id, Detail=""; public int Line; }

// Matching: unique anchors by kind and name, LCS alignment of siblings, single-candidate
// renames, then links by mapped endpoints. Everything unmatched becomes add or delete.
public sealed class ClassSyncPlan
{
    public List<ClassChange> Changes = new List<ClassChange>();
    public Dictionary<string,string> Identities = new Dictionary<string,string>(StringComparer.Ordinal);
    public ClassDocument Expected;
    public string ToJson()
    {
        return ClassJson.Json(ClassJson.Obj("Changes",Changes.Select(c=>ClassJson.Obj("Action",c.Action,"Kind",c.Kind,"Id",c.Id,"Line",c.Line,"Detail",c.Detail)).ToArray(),
            "Identities",Identities.ToDictionary(p=>p.Key,p=>(object)p.Value),"Expected",Expected==null?null:(object)Expected.ToJson()));
    }
    static readonly string[] Ignored = { "alias", "field", "arrow", "typeKind", "parameterTypes" };
    // Attributes the exporter never prints (K009/K010): compared only when the input states them.
    static readonly string[] OneSided = { "returnType", "multiplicity" };
    // A member whose name contains parentheses reads as an operation from text although the
    // model calls it an attribute. The rendered line is what PlantUML carries, so members are
    // compared by that line and attribute/operation/literal are one kind for matching.
    static bool IsMember(ClassElement e) { return ClassDocument.MemberKinds.Contains(e.Kind); }
    static string KindKey(ClassElement e) { return IsMember(e)?"member":e.Kind; }
    static string Properties(ClassElement e)
    {
        if(IsMember(e))return "member|"+ClassPumlWriter.Render(e,true);
        return e.Kind+"|"+e.Text+"|"+string.Join("|",e.Attributes.Where(p=>!Ignored.Contains(p.Key)).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value));
    }
    static string Anchor(ClassElement e) { return e.Kind=="class"?e.Kind+"|"+e.Text+"|"+e.Attr("keyword"):KindKey(e)+"|"+e.Text; }
    // Everything but the name: the rendered line with a placeholder name for members, the
    // keyword for classes, the kind alone for packages.
    static string Shape(ClassElement e)
    {
        if(IsMember(e)) { var copy=e.Copy();copy.Text="\u0001";return "member|"+ClassPumlWriter.Render(copy); }
        if(e.Kind=="class")return "class|"+e.Attr("keyword")+"|"+e.Attr("stereotype");
        return e.Kind;
    }
    static string LinkKey(ClassElement e,Dictionary<string,string> map)
    {
        return string.Join("|",e.Links.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+":"+string.Join(",",p.Value.Select(id=>map==null?id:map[id]))));
    }
    // Report-file text for an added or deleted element. Links spell out both ends so an
    // unmatched line can be found in the input and in the written-back PlantUML.
    static string Describe(ClassElement e,ClassDocument doc)
    {
        if(e.Kind!="link")return e.Text;
        var index=doc.Elements.ToDictionary(x=>x.Id);
        ClassElement from,to;
        string a=index.TryGetValue(e.Link("from")??"",out from)?from.Attr("alias"):"?";
        string b=index.TryGetValue(e.Link("to")??"",out to)?to.Attr("alias"):"?";
        return a+" "+e.Attr("arrow")+" "+b+" : "+e.Text+" ["+e.Attr("toMultiplicity")+"] field="+e.Attr("field");
    }
    static string Differences(ClassElement before,ClassElement after)
    {
        var keys=new List<string>();
        // Same kind: compare the line without the one-sided parts. Different kinds (an attribute
        // whose name holds parentheses read back as an operation): the full line must match,
        // since the attribute's type and the operation's return type are the same text there.
        bool sameLine=IsMember(before) && IsMember(after) && (before.Kind==after.Kind
            ? ClassPumlWriter.Render(before,true)==ClassPumlWriter.Render(after,true)
            : ClassPumlWriter.Render(before)==ClassPumlWriter.Render(after));
        if(!sameLine)
        {
            if(before.Text!=after.Text)keys.Add("name");
            if(before.Kind!=after.Kind)keys.Add("kind");
            foreach(var key in before.Attributes.Keys.Union(after.Attributes.Keys).Where(k=>!Ignored.Contains(k) && !OneSided.Contains(k)).OrderBy(k=>k,StringComparer.Ordinal))
                if(before.Attr(key)!=after.Attr(key))keys.Add(key);
        }
        // One-sided values only matter between members of the same kind; a cross-kind pair
        // whose full lines match carries the same text as type and return type already.
        if(!(sameLine && before.Kind!=after.Kind))
            foreach(var key in OneSided)
                if(after.Attr(key).Length>0 && before.Attr(key)!=after.Attr(key))keys.Add(key);
        if(LinkKey(before,null)!=LinkKey(after,null))keys.Add("ends");
        return string.Join(",",keys);
    }
    public static ClassSyncPlan Build(ClassDocument current,ClassDocument desired,Func<string> newId)
    {
        current.Validate();desired.Validate();
        var plan=new ClassSyncPlan();var map=plan.Identities;var used=new HashSet<string>();
        var old=current.Elements.ToDictionary(e=>e.Id);
        Action<ClassElement,ClassElement> bind=(a,b)=>{map.Add(a.Id,b.Id);used.Add(b.Id);};
        bind(desired.Root,current.Root);
        Func<ClassElement,bool> structural=e=>e.Kind!="link";
        // Unique anchors by kind and name under a mapped parent; repeat as parents resolve.
        bool progress=true;
        while(progress)
        {
            progress=false;
            foreach(var a in desired.Elements.Where(e=>structural(e) && !map.ContainsKey(e.Id)).ToArray())
            {
                if(a.Parent==null || !map.ContainsKey(a.Parent))continue;
                string anchor=Anchor(a);
                var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && b.Parent==map[a.Parent] && Anchor(b)==anchor).ToArray();
                int equivalent=desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Parent==a.Parent && Anchor(b)==anchor);
                if(candidates.Length==1 && equivalent==1) {bind(a,candidates[0]);progress=true;}
            }
        }
        Action align=()=>{
            bool alignProgress=true;
            while(alignProgress)
            {
                int beforeCount=map.Count;
                foreach(var parent in desired.Elements.Where(e=>structural(e) && map.ContainsKey(e.Id)).ToArray())
                {
                    var a=desired.Elements.Where(e=>structural(e) && e.Parent==parent.Id).OrderBy(e=>e.Order).ToArray();
                    var b=current.Elements.Where(e=>structural(e) && e.Parent==map[parent.Id]).OrderBy(e=>e.Order).ToArray();
                    Func<int,int,bool> equal=(i,j)=>map.ContainsKey(a[i].Id)?map[a[i].Id]==b[j].Id:!used.Contains(b[j].Id) && Properties(a[i])==Properties(b[j]);
                    int[,] length=new int[a.Length+1,b.Length+1];
                    for(int i=a.Length-1;i>=0;i--)for(int j=b.Length-1;j>=0;j--)
                        length[i,j]=equal(i,j)?1+length[i+1,j+1]:Math.Max(length[i+1,j],length[i,j+1]);
                    int x=0,y=0;
                    while(x<a.Length && y<b.Length)
                    {
                        if(equal(x,y)) {if(!map.ContainsKey(a[x].Id))bind(a[x],b[y]);x++;y++;}
                        else if(length[x+1,y]>length[x,y+1])x++;else y++;
                    }
                }
                alignProgress=map.Count>beforeCount;
            }
        };
        align();
        // Identical elements moved to another mapped-or-unmapped container keep their identity when unambiguous.
        foreach(var a in desired.Elements.Where(e=>structural(e) && !map.ContainsKey(e.Id)).ToArray())
        {
            string props=Properties(a);
            var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && Properties(b)==props).ToArray();
            if(candidates.Length==1 && desired.Elements.Count(b=>!map.ContainsKey(b.Id) && Properties(b)==props)==1)bind(a,candidates[0]);
        }
        // Renames: an unmatched element under a mapped parent binds to the one unmatched element
        // there that looks the same apart from its name. Three keys, strict to loose: the rendered
        // line without the name, the exact kind, then the member/class kind. Several renames in one
        // class (an attribute and an operation, two attributes of different types) resolve this way.
        foreach(var key in new Func<ClassElement,string>[]{Shape,e=>e.Kind,KindKey})
        {
            progress=true;
            while(progress)
            {
                progress=false;
                foreach(var a in desired.Elements.Where(e=>structural(e) && !map.ContainsKey(e.Id)).ToArray())
                {
                    if(a.Parent==null || !map.ContainsKey(a.Parent))continue;
                    string k=key(a);
                    var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && b.Parent==map[a.Parent] && key(b)==k).ToArray();
                    if(candidates.Length==1 && desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Parent==a.Parent && key(b)==k)==1)
                    {bind(a,candidates[0]);progress=true;}
                }
            }
        }
        align();
        // Links: exact endpoints, arrow and label first; then endpoints only. Lines that are
        // indistinguishable in text (anonymous fields print the same line twice) pair up in
        // order when both sides have the same count, since no observable difference exists.
        Func<ClassElement,bool> resolvable=e=>e.Links.Values.SelectMany(v=>v).All(map.ContainsKey);
        foreach(bool exact in new[]{true,false})
        {
            foreach(var a in desired.Elements.Where(e=>e.Kind=="link" && !map.ContainsKey(e.Id) && resolvable(e)).OrderBy(e=>e.Order).ToArray())
            {
                if(map.ContainsKey(a.Id))continue;
                string key=LinkKey(a,map);
                // The label is the field name, which is the identity in Next Design. The arrow is
                // presentation and must not keep two same-direction lines apart.
                Func<ClassElement,bool> same=b=>b.Kind=="link" && !used.Contains(b.Id) && LinkKey(b,null)==key && (!exact || b.Text==a.Text);
                var candidates=current.Elements.Where(same).OrderBy(b=>b.Order).ToArray();
                var inputs=desired.Elements.Where(b=>b.Kind=="link" && !map.ContainsKey(b.Id) && resolvable(b) && LinkKey(b,map)==key && (!exact || b.Text==a.Text)).OrderBy(b=>b.Order).ToArray();
                if(candidates.Length==0)continue;
                int pairs=Math.Min(candidates.Length,inputs.Length);
                if(exact || (candidates.Length==1 && inputs.Length==1))for(int i=0;i<pairs;i++)bind(inputs[i],candidates[i]);
            }
        }
        foreach(var a in desired.Elements.Where(e=>!map.ContainsKey(e.Id)))
        {
            string id=newId();if(string.IsNullOrEmpty(id) || old.ContainsKey(id) || map.ContainsValue(id))throw new InvalidOperationException("C203: 新IDが重複しています。");
            map.Add(a.Id,id);
        }
        plan.Expected=desired.Copy();
        foreach(var e in plan.Expected.Elements)
        {
            e.Id=map[e.Id];e.Parent=e.Parent==null?null:map[e.Parent];
            e.Links=e.Links.ToDictionary(p=>p.Key,p=>p.Value.Select(id=>map[id]).ToArray(),StringComparer.Ordinal);
        }
        var retained=new HashSet<string>(map.Values.Where(old.ContainsKey));
        foreach(var e in plan.Expected.Elements)
        {
            ClassElement before;
            if(!old.TryGetValue(e.Id,out before)) {plan.Changes.Add(new ClassChange{Action="add",Id=e.Id,Kind=e.Kind,Line=e.Line,Detail=Describe(e,plan.Expected)});continue;}
            if(e.Kind=="diagram" && !desired.HasTitle)e.Text=before.Text;
            string differences=Differences(before,e);
            if(differences.Length>0)plan.Changes.Add(new ClassChange{Action="update",Id=e.Id,Kind=e.Kind,Line=e.Line,Detail=differences});
            if(e.Kind=="link" || e.Kind=="diagram")continue;
            if(e.Parent!=before.Parent)plan.Changes.Add(new ClassChange{Action="move",Id=e.Id,Kind=e.Kind,Line=e.Line,Detail="parent"});
        }
        // Class order follows shape position and package order is derived, so only member
        // order counts. Members outside the longest common subsequence of retained siblings moved.
        foreach(var owner in plan.Expected.Elements.Where(e=>e.Kind=="class" && retained.Contains(e.Id)))
        {
            var after=plan.Expected.Elements.Where(n=>ClassDocument.MemberKinds.Contains(n.Kind) && n.Parent==owner.Id && retained.Contains(n.Id) && old[n.Id].Parent==owner.Id).OrderBy(n=>n.Order).Select(n=>n.Id).ToArray();
            var beforeIds=current.Elements.Where(n=>ClassDocument.MemberKinds.Contains(n.Kind) && n.Parent==owner.Id && after.Contains(n.Id)).OrderBy(n=>n.Order).Select(n=>n.Id).ToArray();
            int[,] length=new int[after.Length+1,beforeIds.Length+1];
            for(int i=after.Length-1;i>=0;i--)for(int j=beforeIds.Length-1;j>=0;j--)
                length[i,j]=after[i]==beforeIds[j]?1+length[i+1,j+1]:Math.Max(length[i+1,j],length[i,j+1]);
            var kept=new HashSet<string>();int x=0,y=0;
            while(x<after.Length && y<beforeIds.Length)
            {
                if(after[x]==beforeIds[y]) {kept.Add(after[x]);x++;y++;}
                else if(length[x+1,y]>length[x,y+1])x++;else y++;
            }
            foreach(var id in after.Where(id=>!kept.Contains(id)))
            {
                var e=plan.Expected.Elements.Single(n=>n.Id==id);
                plan.Changes.Add(new ClassChange{Action="move",Id=id,Kind=e.Kind,Line=e.Line,Detail="order"});
            }
        }
        foreach(var e in current.Elements.Where(e=>!map.ContainsValue(e.Id)))plan.Changes.Add(new ClassChange{Action="delete",Id=e.Id,Kind=e.Kind,Detail=Describe(e,current)});
        plan.Expected.Validate();return plan;
    }
}

// One member edit the text-update step may write: name, visibility and (attributes only) the
// type, each as an old/new pair. Empty flags mean the value is unchanged.
public sealed class ClassMemberEdit
{
    public string CurrentId, Kind, OldText, NewText, OldVisibility, NewVisibility, OldType, NewType, OldParameters, NewParameters, TypeKind="", NewReturnType="", NewMultiplicity="", NewDefault="", ReturnTypeKind="";
    public int Line;
    public bool NameChanged, VisibilityChanged, TypeChanged, ParametersChanged, ReturnTypeChanged, MultiplicityChanged, DefaultChanged;
    public string Describe()
    {
        var parts=new List<string>();
        if(NameChanged)parts.Add("name '"+OldText+"'->'"+NewText+"'");
        if(VisibilityChanged)parts.Add("visibility '"+OldVisibility+"'->'"+NewVisibility+"'");
        if(TypeChanged)parts.Add("type '"+OldType+"'->'"+NewType+"'");
        if(ParametersChanged)parts.Add("parameters '"+OldParameters+"'->'"+NewParameters+"'");
        if(ReturnTypeChanged)parts.Add("returnType ->'"+NewReturnType+"'");
        if(MultiplicityChanged)parts.Add("multiplicity ->'"+NewMultiplicity+"'");
        if(DefaultChanged)parts.Add("default ->'"+NewDefault+"'");
        return Kind+" "+string.Join(", ",parts.ToArray());
    }
}

// Preflight for the text-update step: accept a plan only when every change is a member update
// limited to name, visibility and (attributes) type. Any other change is a stop reason, so
// nothing is written for a plan the step cannot fully apply.
// One reference link the update step may add or remove: the current class ids of both ends,
// the field name on the source class, and the input line (adds only).
public sealed class ClassLinkChange { public string Action, FromId, ToId, Field, FromAlias, ToAlias; public int Line; }

// One attribute or operation to create under a class, or one existing member to delete.
public sealed class ClassMemberChange
{
    public string Action, Kind, OwnerId, OwnerAlias, CurrentId, Text, Visibility, Type, Parameters, TypeKind="", ReturnType="", ReturnTypeKind="", Multiplicity="", Default="";
    // For adds: the current id of the first retained sibling of the same kind that follows in the input, or null for the end.
    public string InsertBeforeId;
    public bool IsStatic;
    public int Line;
}

// One class to create on the diagram, or one existing class to remove. A new class is
// placed under the same owner as a sibling class from the input (its container in the
// document), next to the sibling's node; its members and links follow through their own
// changes, which refer to the class by ExpectedId.
public sealed class ClassChangeItem
{
    public string Action, ExpectedId, CurrentId, Text, Keyword, Stereotype, ContainerId, ContainerAlias, SiblingId, SiblingAlias;
    public int Line;
}

public sealed class ClassTextPreflight
{
    public List<ClassMemberEdit> Edits = new List<ClassMemberEdit>();
    public List<ClassLinkChange> Links = new List<ClassLinkChange>();
    public List<ClassMemberChange> Members = new List<ClassMemberChange>();
    public List<ClassChangeItem> Classes = new List<ClassChangeItem>();
    public int ClassAddCount { get { return Classes.Count(c=>c.Action=="add"); } }
    public int ClassDeleteCount { get { return Classes.Count(c=>c.Action=="delete"); } }
    public List<string> Reasons = new List<string>();
    public bool Candidate { get { return Reasons.Count==0 && (Edits.Count>0 || Links.Count>0 || Members.Count>0 || Classes.Count>0); } }
    public int MemberAddCount { get { return Members.Count(m=>m.Action=="add"); } }
    public int MemberDeleteCount { get { return Members.Count(m=>m.Action=="delete"); } }
    public int LinkAddCount { get { return Links.Count(l=>l.Action=="add"); } }
    public int LinkDeleteCount { get { return Links.Count(l=>l.Action=="delete"); } }
    public int NameCount { get { return Edits.Count(e=>e.NameChanged); } }
    public int VisibilityCount { get { return Edits.Count(e=>e.VisibilityChanged); } }
    public int TypeCount { get { return Edits.Count(e=>e.TypeChanged); } }
    static readonly string[] AttributeKeys = { "name", "visibility", "type", "multiplicity", "default" };
    static readonly string[] OperationKeys = { "name", "visibility", "parameters", "returnType" };
    // The exporter prints an operation's parameters as the argument names joined by ", "
    // (K019); a hand-written "name : Type" keeps the type after the colon.
    // "int <<Kind>>" on a return type: the kind names the definition to create.
    public static string StripKind(string type) { return Regex.Replace(type??"",@"\s*<<[^>]+>>$",""); }
    public static string KindOf(string type) { var m=Regex.Match(type??"",@"<<([^>]+)>>$");return m.Success?m.Groups[1].Value.Trim():""; }
    public static bool IsMultiplicity(string text) { return Regex.IsMatch(text??"",@"^(\d+|\*)(\.\.(\d+|\*))?$"); }
    public static string[] ParameterNames(string parameters)
    {
        if(string.IsNullOrEmpty(parameters))return new string[0];
        return parameters.Split(',').Select(x=>x.Trim()).Where(x=>x.Length>0).Select(x=>{int colon=x.IndexOf(" : ",StringComparison.Ordinal);return colon>=0?x.Substring(0,colon).Trim():x;}).ToArray();
    }
    public static string[] ParameterTypes(string parameters)
    {
        return ParameterTypesRaw(parameters).Select(x=>Regex.Replace(x,@"\s*<<[^>]+>>$","")).ToArray();
    }
    public static string[] ParameterTypeKinds(string parameters)
    {
        return ParameterTypesRaw(parameters).Select(x=>{var m=Regex.Match(x,@"<<([^>]+)>>$");return m.Success?m.Groups[1].Value.Trim():"";}).ToArray();
    }
    static string[] ParameterTypesRaw(string parameters)
    {
        if(string.IsNullOrEmpty(parameters))return new string[0];
        return parameters.Split(',').Select(x=>x.Trim()).Where(x=>x.Length>0).Select(x=>{int colon=x.IndexOf(" : ",StringComparison.Ordinal);return colon>=0?x.Substring(colon+3).Trim():"";}).ToArray();
    }
    public static ClassTextPreflight Check(ClassDocument current,ClassDocument desired,ClassSyncPlan plan)
    {
        var result=new ClassTextPreflight();
        var old=current.Elements.ToDictionary(e=>e.Id);
        var target=plan.Expected.Elements.ToDictionary(e=>e.Id);
        // Classes first: a new class becomes a valid owner for member adds and a valid end for
        // link adds below. Its sibling is the nearest existing class in the same container.
        var pendingClasses=new HashSet<string>(StringComparer.Ordinal);
        foreach(var c in plan.Changes.Where(x=>x.Kind=="class"))
        {
            string where=c.Line>0?" 入力"+c.Line+"行":"";
            ClassElement cls;
            if(c.Action=="add" && target.TryGetValue(c.Id,out cls))
            {
                ClassElement container;
                if(!target.TryGetValue(cls.Parent??"",out container)) { result.Reasons.Add("add class"+where+": 所有先を特定できません"); continue; }
                if(container.Kind=="class" && !old.ContainsKey(container.Id)) { result.Reasons.Add("add class"+where+": 新しいクラスの中に入れ子のクラスは扱えません"); continue; }
                if(cls.Text.Length==0 || cls.Text.Contains("\\n")) { result.Reasons.Add("add class"+where+": 空または改行を含む名前は扱えません"); continue; }
                if(ClassDocument.IsContainerKeyword(cls.Attr("keyword"))) { result.Reasons.Add("add class"+where+": package / component の追加は扱えません"); continue; }
                var sibling=plan.Expected.Elements.Where(e=>e.Kind=="class" && e.Id!=cls.Id && e.Parent==cls.Parent && old.ContainsKey(e.Id) && e.Attr("stereotype")==cls.Attr("stereotype") && e.Attr("keyword")==cls.Attr("keyword"))
                    .OrderBy(e=>Math.Abs(e.Order-cls.Order)).FirstOrDefault();
                if(sibling==null)sibling=plan.Expected.Elements.Where(e=>e.Kind=="class" && e.Id!=cls.Id && e.Parent==cls.Parent && old.ContainsKey(e.Id)).OrderBy(e=>Math.Abs(e.Order-cls.Order)).FirstOrDefault();
                if(sibling==null) { result.Reasons.Add("add class"+where+": 同じ所有先に既存のクラスがなく、種類と配置を決められません"); continue; }
                result.Classes.Add(new ClassChangeItem{Action="add",ExpectedId=cls.Id,Text=cls.Text,Keyword=cls.Attr("keyword"),Stereotype=cls.Attr("stereotype"),ContainerId=container.Id,ContainerAlias=container.Attr("alias"),SiblingId=sibling.Id,SiblingAlias=sibling.Attr("alias"),Line=c.Line});
                pendingClasses.Add(cls.Id);
                continue;
            }
            if(c.Action=="delete" && old.TryGetValue(c.Id,out cls))
            {
                if(ClassDocument.IsContainerKeyword(cls.Attr("keyword"))) { result.Reasons.Add("delete class ("+cls.Text+"): package / component の削除は扱えません"); continue; }
                if(current.Elements.Any(e=>e.Kind=="class" && e.Parent==cls.Id)) { result.Reasons.Add("delete class ("+cls.Text+"): 入れ子のクラスを持つため扱えません"); continue; }
                result.Classes.Add(new ClassChangeItem{Action="delete",CurrentId=cls.Id,Text=cls.Text,Keyword=cls.Attr("keyword")});
                continue;
            }
        }
        var deletedClasses=new HashSet<string>(result.Classes.Where(x=>x.Action=="delete").Select(x=>x.CurrentId),StringComparer.Ordinal);
        foreach(var c in plan.Changes)
        {
            string where=c.Line>0?" 入力"+c.Line+"行":"";
            if(c.Kind=="class")
            {
                if((c.Action=="add" && pendingClasses.Contains(c.Id)) || (c.Action=="delete" && deletedClasses.Contains(c.Id)))continue;
                if(c.Action=="add" || c.Action=="delete")continue; // reason already recorded
                if(c.Action=="update")
                {
                    ClassElement classBefore,classAfter;
                    var classKeys=c.Detail.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries);
                    if(classKeys.Any(k=>k!="name")) { result.Reasons.Add("update class"+where+" ["+c.Detail+"]: クラスのキーワード・ステレオタイプの変更は扱えません"); continue; }
                    if(!old.TryGetValue(c.Id,out classBefore) || !target.TryGetValue(c.Id,out classAfter)) { result.Reasons.Add("update class"+where+": 対応する要素を特定できません"); continue; }
                    if(classAfter.Text.Length==0 || classAfter.Text.Contains("\\n")) { result.Reasons.Add("update class"+where+": 空または改行を含む名前は扱えません"); continue; }
                    result.Edits.Add(new ClassMemberEdit{CurrentId=c.Id,Kind="class",Line=c.Line,OldText=classBefore.Text,NewText=classAfter.Text,NameChanged=true});
                    continue;
                }
                result.Reasons.Add(c.Action+" class"+where+": 扱えません"); continue;
            }
            // Members and links that belong to a deleted class go with it and need no separate write.
            if(c.Action=="delete" && (c.Kind=="attribute" || c.Kind=="operation" || c.Kind=="literal"))
            {
                ClassElement gone;
                if(old.TryGetValue(c.Id,out gone) && deletedClasses.Contains(gone.Parent))continue;
            }
            if(c.Action=="delete" && c.Kind=="link")
            {
                ClassElement gone;
                if(old.TryGetValue(c.Id,out gone) && (deletedClasses.Contains(gone.Link("from")??"") || deletedClasses.Contains(gone.Link("to")??"")))continue;
            }
            if(c.Kind=="link")
            {
                // A link is a reference field on the source class. Adds need both ends to be
                // classes that already exist; deletes need a field-backed link (connector-only
                // lines carry no field). Multiplicity comes from the field, so it cannot change.
                ClassElement link;
                if(c.Action=="add" && target.TryGetValue(c.Id,out link))
                {
                    ClassElement from,to;
                    if(link.Text.Length==0) { result.Reasons.Add("add link"+where+": ロール名（フィールド名）のない関連は扱えません"); continue; }
                    string fromKey=link.Link("from")??"",toKey=link.Link("to")??"";
                    bool fromOk=old.TryGetValue(fromKey,out from) || (pendingClasses.Contains(fromKey) && target.TryGetValue(fromKey,out from));
                    bool toOk=old.TryGetValue(toKey,out to) || (pendingClasses.Contains(toKey) && target.TryGetValue(toKey,out to));
                    if(!fromOk || !toOk) { result.Reasons.Add("add link"+where+": 両端が既存または追加するクラスではありません"); continue; }
                    result.Links.Add(new ClassLinkChange{Action="add",FromId=from.Id,ToId=to.Id,Field=link.Text,FromAlias=from.Attr("alias"),ToAlias=to.Attr("alias"),Line=c.Line});
                    continue;
                }
                if(c.Action=="delete" && old.TryGetValue(c.Id,out link))
                {
                    if(link.Attr("field").Length==0) { result.Reasons.Add("delete link ("+link.Attr("arrow")+" "+link.Text+"): フィールドに対応しない線は扱えません"); continue; }
                    var from=old[link.Link("from")];var to=old[link.Link("to")];
                    result.Links.Add(new ClassLinkChange{Action="delete",FromId=from.Id,ToId=to.Id,Field=link.Attr("field"),FromAlias=from.Attr("alias"),ToAlias=to.Attr("alias")});
                    continue;
                }
                result.Reasons.Add(c.Action+" link"+where+" ["+c.Detail+"]: 関連の"+(c.Action=="update"?"多重度・ロール名の変更":"この変更")+"は扱えません"); continue;
            }
            if((c.Action=="add" || c.Action=="delete") && (c.Kind=="attribute" || c.Kind=="operation"))
            {
                ClassElement member;
                if(c.Action=="add" && target.TryGetValue(c.Id,out member))
                {
                    ClassElement owner;
                    string ownerKey=member.Parent??"";
                    if(!old.TryGetValue(ownerKey,out owner) && !(pendingClasses.Contains(ownerKey) && target.TryGetValue(ownerKey,out owner))) { result.Reasons.Add("add "+c.Kind+where+": 所有先のクラスが既存または追加するクラスではありません"); continue; }
                    if(member.Text.Length==0 || member.Text.Contains("\\n")) { result.Reasons.Add("add "+c.Kind+where+": 空または改行を含む名前は扱えません"); continue; }

                    if(c.Kind=="operation" && ParameterNames(member.Attr("parameters")).Any(n=>n.Length==0 || n.Contains("\\n"))) { result.Reasons.Add("add operation"+where+": 引数名が空か改行を含みます"); continue; }
                    if(c.Kind=="operation" && ParameterNames(member.Attr("parameters")).Distinct().Count()!=ParameterNames(member.Attr("parameters")).Length) { result.Reasons.Add("add operation"+where+": 同じ名前の引数があります"); continue; }
                    if(c.Kind=="attribute" && member.Attr("multiplicity").Length>0 && !IsMultiplicity(member.Attr("multiplicity"))) { result.Reasons.Add("add attribute"+where+": 多重度は 1、0..1、0..*、1..* のように書いてください"); continue; }
                    if(member.Attr("type").Contains(", ")) { result.Reasons.Add("add attribute"+where+": 複数の型を持つ属性は扱えません"); continue; }
                    string memberKind=c.Kind;
                    var following=plan.Expected.Elements.Where(e=>e.Parent==member.Parent && e.Kind==memberKind && e.Order>member.Order && old.ContainsKey(e.Id)).OrderBy(e=>e.Order).FirstOrDefault();
                    result.Members.Add(new ClassMemberChange{Action="add",Kind=c.Kind,OwnerId=owner.Id,OwnerAlias=owner.Attr("alias"),Text=member.Text,Visibility=member.Attr("visibility"),Type=member.Attr("type"),Parameters=member.Attr("parameterTypes").Length>0?member.Attr("parameterTypes"):member.Attr("parameters"),IsStatic=member.Attr("static")=="true",Line=c.Line,InsertBeforeId=following==null?null:following.Id,TypeKind=member.Attr("typeKind"),
                        ReturnType=StripKind(member.Attr("returnType")),ReturnTypeKind=KindOf(member.Attr("returnType")),Multiplicity=member.Attr("multiplicity"),Default=member.Attr("default")});
                    continue;
                }
                if(c.Action=="delete" && old.TryGetValue(c.Id,out member))
                {
                    var owner=old[member.Parent];
                    result.Members.Add(new ClassMemberChange{Action="delete",Kind=c.Kind,OwnerId=owner.Id,OwnerAlias=owner.Attr("alias"),CurrentId=member.Id,Text=member.Text});
                    continue;
                }
                result.Reasons.Add(c.Action+" "+c.Kind+where+": 対応する要素を特定できません"); continue;
            }
            if(c.Action!="update") { result.Reasons.Add(c.Action+" "+c.Kind+where+": 本文更新では扱えません"); continue; }
            if(c.Kind!="attribute" && c.Kind!="operation") { result.Reasons.Add("update "+c.Kind+where+": 属性・操作以外の更新は扱えません"); continue; }
            ClassElement before,after;
            if(!old.TryGetValue(c.Id,out before) || !target.TryGetValue(c.Id,out after)) { result.Reasons.Add("update "+c.Kind+where+": 対応する要素を特定できません"); continue; }
            var allowed=c.Kind=="attribute"?AttributeKeys:OperationKeys;
            var keys=c.Detail.Split(new[]{','},StringSplitOptions.RemoveEmptyEntries);
            var unsupported=keys.Where(k=>!allowed.Contains(k)).ToArray();
            if(unsupported.Length>0) { result.Reasons.Add("update "+c.Kind+where+" ["+c.Detail+"]: "+string.Join(",",unsupported)+" の変更は扱えません"); continue; }
            var edit=new ClassMemberEdit{CurrentId=c.Id,Kind=c.Kind,Line=c.Line,OldText=before.Text,NewText=after.Text,
                OldVisibility=before.Attr("visibility"),NewVisibility=after.Attr("visibility"),OldType=before.Attr("type"),NewType=after.Attr("type"),
                OldParameters=before.Attr("parameters"),NewParameters=after.Attr("parameterTypes").Length>0?after.Attr("parameterTypes"):after.Attr("parameters"),TypeKind=after.Attr("typeKind"),
                NameChanged=keys.Contains("name"),VisibilityChanged=keys.Contains("visibility"),TypeChanged=keys.Contains("type"),ParametersChanged=keys.Contains("parameters"),
                ReturnTypeChanged=keys.Contains("returnType"),MultiplicityChanged=keys.Contains("multiplicity"),DefaultChanged=keys.Contains("default"),
                NewReturnType=StripKind(after.Attr("returnType")),ReturnTypeKind=KindOf(after.Attr("returnType")),NewMultiplicity=after.Attr("multiplicity"),NewDefault=after.Attr("default")};
            string problem=null;
            if(edit.ParametersChanged && ParameterNames(edit.NewParameters).Any(n=>n.Length==0 || n.Contains("\\n")))problem="引数名が空か改行を含みます";
            else if(edit.ParametersChanged && ParameterNames(edit.NewParameters).Distinct().Count()!=ParameterNames(edit.NewParameters).Length)problem="同じ名前の引数があります";
            else if(edit.NameChanged && (edit.NewText.Length==0 || edit.NewText.Contains("\\n") || edit.OldText.Contains("\\n")))problem="空または改行を含む名前は扱えません";
            else if(edit.VisibilityChanged && edit.NewVisibility.Length==0)problem="可視性の記号を消す変更は扱えません";
            else if(edit.TypeChanged && edit.NewType.Length==0)problem="型を空にする変更は扱えません";
            else if(edit.TypeChanged && edit.NewType.Contains(", "))problem="複数の型を持つ属性は扱えません";
            else if(edit.MultiplicityChanged && !IsMultiplicity(edit.NewMultiplicity))problem="多重度は 1、0..1、0..*、1..* のように書いてください";
            if(problem!=null) { result.Reasons.Add("update "+c.Kind+where+" ["+c.Detail+"]: "+problem); continue; }
            result.Edits.Add(edit);
        }
        if(plan.Changes.Count==0)result.Reasons.Add("差分候補がありません");
        return result;
    }
    public string Summary()
    {
        var sb=new StringBuilder();
        sb.Append("本文更新の事前判定: ").Append(Candidate?"候補あり":"停止").Append('\n');
        sb.Append("メンバ ").Append(Edits.Count).Append("件（名前 ").Append(NameCount).Append(" / 可視性 ").Append(VisibilityCount).Append(" / 型 ").Append(TypeCount).Append(" / 引数 ").Append(Edits.Count(e=>e.ParametersChanged)).Append(" / 戻り値 ").Append(Edits.Count(e=>e.ReturnTypeChanged)).Append(" / 多重度 ").Append(Edits.Count(e=>e.MultiplicityChanged)).Append(" / 既定値 ").Append(Edits.Count(e=>e.DefaultChanged)).Append("） / クラス追加 ").Append(ClassAddCount).Append(" 削除 ").Append(ClassDeleteCount).Append(" / メンバ追加 ").Append(MemberAddCount).Append(" 削除 ").Append(MemberDeleteCount).Append(" / 関連 追加 ").Append(LinkAddCount).Append(" 削除 ").Append(LinkDeleteCount).Append(" / 停止理由 ").Append(Reasons.Count).Append("件\n");
        foreach(var r in Reasons)sb.Append("  ").Append(r).Append('\n');
        return sb.ToString().TrimEnd();
    }
}

// Apply, then always roll back; verify the restored state. One rollback attempt only.
public sealed class ClassRollbackTrial
{
    public bool Applied, RollbackReturned, Restored;
    public Exception ApplyError, RollbackError, VerifyError;
    public void Run(Action apply,Action rollback,Action verifyRestored)
    {
        try {apply();Applied=true;}
        catch(Exception ex){ApplyError=ex;}
        finally
        {
            try {rollback();RollbackReturned=true;}
            catch(Exception ex){RollbackError=ex;}
            if(RollbackReturned)
            {
                try {verifyRestored();Restored=true;}
                catch(Exception ex){VerifyError=ex;}
            }
        }
    }
}

// Commit only after verified application; failures get one rollback attempt.
public sealed class ClassCommitTrial
{
    public bool Applied, Committed, RollbackReturned, Restored;
    public Exception ApplyError, CommitError, RollbackError, VerifyError;
    public void Run(Action apply,Action commit,Action rollback,Action verifyRestored)
    {
        try {apply();Applied=true;} catch(Exception ex){ApplyError=ex;}
        if(Applied) {try {commit();Committed=true;} catch(Exception ex){CommitError=ex;}}
        if(Committed)return;
        try {rollback();RollbackReturned=true;} catch(Exception ex){RollbackError=ex;}
        if(RollbackReturned) {try {verifyRestored();Restored=true;} catch(Exception ex){VerifyError=ex;}}
    }
}

// Screens: counts only. Names, IDs and design text stay in the local report files.
public static class ClassAudit
{
    static readonly string[] Actions = { "add", "delete", "update", "move" };
    public static string Summary(ClassSyncPlan plan,int limitations)
    {
        if(plan.Changes.Count==0)return "差分候補なし（要照合 "+limitations+"件）";
        var sb=new StringBuilder();
        sb.Append("差分候補 ").Append(plan.Changes.Count).Append("件 / 要照合 ").Append(limitations).Append("件\n");
        sb.Append("種類        追加 削除 更新 移動\n");
        foreach(var kind in ClassDocument.Kinds)
        {
            var rows=plan.Changes.Where(c=>c.Kind==kind).ToArray();if(rows.Length==0)continue;
            sb.Append(Pad(kind,11));
            foreach(var action in Actions)sb.Append(' ').Append(Pad(rows.Count(c=>c.Action==action).ToString(),4));
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }
    public static string Reasons(ClassSyncPlan plan)
    {
        if(plan.Changes.Count==0)return "差分候補はありません。";
        var sb=new StringBuilder();
        foreach(var c in plan.Changes.OrderBy(c=>c.Line).ThenBy(c=>c.Kind,StringComparer.Ordinal))
        {
            sb.Append(c.Action).Append(' ').Append(c.Kind);
            if(c.Line>0)sb.Append(" 入力").Append(c.Line).Append("行");
            if(c.Action=="update" || c.Action=="move")sb.Append(" [").Append(c.Detail).Append(']');
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }
    static string Pad(string s,int width) { int length=0;foreach(var ch in s)length+=ch<128?1:2;return s+new string(' ',Math.Max(0,width-length)); }
}
// END TRANSCRIBED 60-class-sync.cs

// ============================================================
//  Part C / クラス図の PlantUML 同期 API（NdMcp 固有部。src/classsync.cs）
//
//  同期本体は ClassImportProbe/sync/ClassSync.cs と ClassSyncRuntime.cs を
//  tools/build_main.py が転記する。ここには HTTP 要求と同期本体をつなぐ薄い層と、
//  同期本体が参照する ClassExperiment（リボン版では結果ダイアログ）の代替だけを置く。
//  MCP 経由ではダイアログを出せないため、結果はすべて JSON 応答と診断ファイルに載せる。
// ============================================================

// ClassImportProbe/main.cs の ClassExperiment と同じ名前・同じメンバ。ダイアログは出さず出力ウィンドウへ書く。
public static class ClassExperiment
{
    public const string Version = "0.7.2";
    public const string Title = "クラス図同期 (NdMcp) / " + Version;
    public static string Summary = "";
    public static string Details = "";
    public static void Show(IApplication app)
    {
        try { app.Output.WriteLine("NdMcp", Title + ": " + Summary); } catch (Exception) { }
    }
    public static void Write(string path, string text)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
    }
    // 診断にはモデル名と ID が含まれる。リボン版と同じフォルダに残し、リポジトリへは入れない。
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

// HTTP 要求（UI スレッドのコマンド内）からクラス図同期を呼ぶ。
public static class ClassSyncApi
{
    public const int MaxPumlLength = 300000;

    // 対象モデルに紐づくクラス図エディタを選ぶ。editorId 指定があればそれ、無ければ最初のクラス図。
    public static IEditor FindEditor(IApplication app, IModel model, string editorId, out List<object> candidates)
    {
        candidates = new List<object>();
        IEditor found = null;
        IEnumerable<IEditor> editors;
        try { editors = model.GetEditors().Cast<IEditor>().ToList(); }
        catch (Exception ex) { throw new NdMcpHttpError(500, "エディタ一覧を取得できません: " + ex.Message); }
        foreach (var editor in editors)
        {
            string reject = ClassDiagramKind.Reject(editor);
            candidates.Add(new JsonObject()
                .Set("editorId", editor.Id).Set("editorType", editor.EditorType ?? "")
                .Set("viewDefinition", editor.ViewDefinitionName ?? "")
                .Set("classDiagram", reject == null).Set("reason", reject));
            if (reject != null) continue;
            if (!string.IsNullOrEmpty(editorId) ? editor.Id == editorId : found == null) found = editor;
        }
        return found;
    }

    static IEditor RequireEditor(IApplication app, string path, string id, string editorId, out IModel model)
    {
        model = ModelApi.ResolveModel(app, path, id);
        List<object> candidates;
        var editor = FindEditor(app, model, editorId, out candidates);
        if (editor == null)
            throw new NdMcpHttpError(404, "モデルにクラス図がありません" + (string.IsNullOrEmpty(editorId) ? "" : " (editor=" + editorId + ")")
                + ": " + ModelApi.PathOf(model) + " / 図 " + candidates.Count + " 件");
        return editor;
    }

    // GET /class-sync/current: 図の現在の内容を PlantUML（ClassImportProbe の比較用書式）で返す。
    public static object Current(IApplication app, string path, string id, string editorId)
    {
        IModel model;
        var editor = RequireEditor(app, path, id, editorId, out model);
        var log = new StringBuilder();
        var snapshot = ClassDiagramSnapshot.Read((IDiagram)editor, new ClassSyncOptions(), log);
        var editorModel = ClassDiagramKind.ModelOf(editor);
        return new JsonObject()
            .Set("modelPath", ModelApi.PathOf(model)).Set("modelId", model.Id)
            .Set("editorId", editor.Id).Set("diagramName", editorModel != null ? editorModel.Name : "")
            .Set("viewDefinition", editor.ViewDefinitionName ?? "")
            .Set("plantuml", ClassPumlWriter.Write(snapshot.Document))
            .Set("limitations", snapshot.Limitations.Cast<object>().ToList());
    }

    // GET /class-sync/editors: モデルに紐づく図の一覧と、クラス図として扱えるかどうか。
    public static object Editors(IApplication app, string path, string id)
    {
        var model = ModelApi.ResolveModel(app, path, id);
        List<object> candidates;
        FindEditor(app, model, "", out candidates);
        return new JsonObject().Set("modelPath", ModelApi.PathOf(model)).Set("modelId", model.Id).Set("editors", candidates);
    }

    // POST /class-sync/{preview|trial|apply}: PlantUML と図を比較し、mode に応じて反映する。
    //   preview … 比較のみ。trial … 一時適用して照合し、必ず取り消す。apply … 確定する。
    public static object Sync(IApplication app, string path, string id, string editorId, string plantuml, string mode)
    {
        if (plantuml == null || plantuml.Trim().Length == 0) throw new NdMcpHttpError(400, "plantuml が空です");
        if (plantuml.Length > MaxPumlLength) throw new NdMcpHttpError(400, "plantuml は 300KB 以下にしてください");
        bool trial = mode == "trial" || mode == "apply";
        bool retain = mode == "apply";
        IModel model;
        var editor = RequireEditor(app, path, id, editorId, out model);
        var outcome = ClassSyncRuntime.Run(app, editor, plantuml, "mcp:" + mode, trial, retain, true, message => true);
        string stem = ClassExperiment.SaveReport("mcp-" + mode, outcome.Log, outcome.ReportJson, outcome.CurrentPuml);
        var result = new JsonObject()
            .Set("mode", mode).Set("ok", outcome.Succeeded)
            .Set("modelPath", ModelApi.PathOf(model)).Set("modelId", model.Id).Set("editorId", editor.Id)
            .Set("changes", outcome.Changes).Set("limitations", outcome.Limitations).Set("stopReasons", outcome.StopReasons)
            .Set("applied", outcome.Applied).Set("committed", outcome.Committed)
            .Set("summary", outcome.Summary).Set("details", outcome.Details)
            .Set("error", outcome.ErrorMessage)
            .Set("reportFile", stem == null ? null : stem + ".txt");
        if (mode == "preview") result.Set("currentPlantuml", outcome.CurrentPuml);
        return result;
    }
}
