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
