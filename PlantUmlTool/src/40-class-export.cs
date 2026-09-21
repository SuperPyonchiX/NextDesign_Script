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

        MetaProbe.DumpModel(w, "ノード(1件目)", models.Count > 0 ? models[0] : null);
        MetaProbe.DumpModel(w, "ノードの子(1件目)", FirstChild(models));

        DumpClassNames(w, "ノードのクラス名一覧", models);
        DumpClassNames(w, "子のクラス名一覧", AllChildren(models));

        DumpConnectors(w, connectors);
        DumpReferenceFields(w, models);

        w("=== 調査終了 ===");
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

