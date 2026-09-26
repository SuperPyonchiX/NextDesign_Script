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
