
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

