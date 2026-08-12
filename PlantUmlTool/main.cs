// ============================================================
//  Next Design エクステンション : PlantUML 連携（出力 / 取り込み）
//  エントリポイント（C# スクリプト / Next Design V3.x）
//
//  構成:
//    Part 0  出力エンジン  PlantUmlOptions / PlantUmlText / SeqEvent /
//                          OpenFragment / SequencePlantUmlExporter
//    Part 0  出力実行部    DiagramEntry / ExportSettings / ExportRunner
//    Part 1  解析層        AST / PlantUmlSequenceParser
//    Part 2  適用層 (1)    MetaMap（メタモデルの自動判別）
//    Part 3  適用層 (2)    平坦化 / 既存索引 / 突き合わせ / 差分プラン
//    Part 4  適用層 (3)    ActivationResolver / WriteResult / SequenceWriter
//    Part 5  実行層        MetaProbe / ImportRunner
//    Part 6  コマンドハンドラ（manifest.json の execFunc と名前を一致させる）
//    Part 7  クラス図出力  ClassPlantUmlOptions / ClassDiagramCollector /
//                          ClassPlantUmlExporter / ClassExportRunner / ClassProbe
//
//  制約:
//    - main に指定できるファイルは1つだけ。分割できない
//    - 変換エンジンはグローバルオブジェクト（App / UI / Output）に触らない。
//      クラス内からは参照できないため、IApplication を引数で受け取る
//    - デバッガは使えない。Output.WriteLine が唯一の手がかりになる
//    - 変更は Next Design を再起動するまで反映されない
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NextDesign.Core;
using NextDesign.Desktop;
using NextDesign.Extension;

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
            for (var i = 1; i < operands.Count; i++)   // 先頭のガードはヘッダ行に出す
            {
                events.Add(new SeqEvent
                {
                    Y = operands[i].Position, Priority = 20, X = f.LocationX,
                    Id = operands[i].Id, Kind = "operand", Operand = operands[i]
                });
            }
        }

        if (_o.EmitActivation)
        {
            foreach (var e in _d.ExecutionSpecifications.Cast<IExecutionSpecificationShape>())
            {
                events.Add(new SeqEvent
                {
                    Y = e.LocationY, Priority = 60, X = e.LocationX,
                    Id = e.Id, Kind = "activate", Execution = e
                });
                events.Add(new SeqEvent
                {
                    Y = e.LocationY + e.Length, Priority = 80, X = e.LocationX,
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

        foreach (var ev in ordered)
        {
            CloseFragmentsAbove(ev.Y);

            if (ev.Kind == "fragment") OnFragment(ev.Fragment);
            else if (ev.Kind == "operand") OnOperand(ev.Operand);
            else if (ev.Kind == "message") OnMessage(ev.Message);
            else if (ev.Kind == "activate") OnActivate(ev.Execution);
            else if (ev.Kind == "deactivate") OnDeactivate(ev.Execution);
            else if (ev.Kind == "use") OnInteractionUse(ev.Use);
            else if (ev.Kind == "destruction") OnDestruction(ev.Destruction);
            else if (ev.Kind == "note") OnNote(ev.Note);
        }
    }

    private List<IOperandShape> OperandsOf(IFragmentShape f)
    {
        return f.Operands.Cast<IOperandShape>()
            .OrderBy(o => o.Position)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();
    }

    private double MessageX(IMessageShape m)
    {
        var send = m.SendPort as ISequenceNodeShape;
        if (send != null) return send.LocationX;
        var receive = m.ReceivePort as ISequenceNodeShape;
        return receive != null ? receive.LocationX : 0;
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

    private void OnOperand(IOperandShape o)
    {
        var guard = PlantUmlText.Inline(PlantUmlText.Normalize(o.Guard));
        var depth = _stack.Count > 0 ? _stack.Count - 1 : 0;
        LineAt(depth, guard.Length > 0 ? "else " + guard : "else");
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

    private void OnDeactivate(IExecutionSpecificationShape e)
    {
        var l = e.Lifeline;
        if (l == null) return;

        var alias = AliasOf(l);
        int count;
        if (!_activeCount.TryGetValue(alias, out count) || count <= 0) return;
        _activeCount[alias] = count - 1;
        Line("deactivate " + alias);
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
            var owner = PlantUmlText.SafeFileName((entry.OwnerPath ?? "").Replace('/', '-').Replace('\\', '-'));
            var name = PlantUmlText.SafeFileName(entry.Name);
            if (name.Length == 0) name = "sequence";

            var baseName = owner.Length == 0 ? name : owner + "__" + name;
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
        app.Output.Clear(Category);
        app.Window.IsInformationPaneVisible = true;
        app.Window.ActiveInfoWindow = "Output";
        app.Window.CurrentOutputCategory = Category;
    }

    private static void SaveText(string path, string text)
    {
        System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}

// ============================================================
//  Part 1 / 解析層 : PlantUML パーサと AST
//
//  この層は Next Design の API に一切依存しない。
//  グローバルオブジェクト（App / UI / Output）にも触らない。
// ============================================================

// ------------------------------------------------------------
//  メッセージの種別
// ------------------------------------------------------------
public enum PumlKind { Sync, Async, Reply, Create, Destroy }

// ------------------------------------------------------------
//  警告・情報（読み飛ばした行は必ずここに残す）
// ------------------------------------------------------------
public class PumlWarning
{
    public int Line;
    public string Level = "warn";   // "warn" | "info" | "error"
    public string Message = "";
    public string Source = "";

    public override string ToString()
    {
        var head = Line > 0 ? Line + "行目: " : "";
        var tail = string.IsNullOrEmpty(Source) ? "" : "  '" + Source + "'";
        return head + Message + tail;
    }
}

// ------------------------------------------------------------
//  参加者（ライフライン）
// ------------------------------------------------------------
public class PumlParticipant
{
    public string Alias = "";
    public string DisplayName = "";
    public string Keyword = "participant";
    public int DeclaredOrder;
    public bool DeclaredByCreate;
    public bool Implicit;
    public int Line;

    // Next Design のライフライン名に対応するのは表示名のほう
    public string Label
    {
        get { return string.IsNullOrEmpty(DisplayName) ? Alias : DisplayName; }
    }
}

// ------------------------------------------------------------
//  項目（メッセージ・制御・ノートなど）
// ------------------------------------------------------------
public abstract class PumlItem
{
    public int Line;
}

public class PumlMessage : PumlItem
{
    public string SenderAlias = "";
    public string ReceiverAlias = "";
    public string Text = "";
    public PumlKind Kind = PumlKind.Sync;
    public bool ActivateReceiver;    // 末尾の ++
    public bool DeactivateSender;    // 末尾の --
    public bool Dashed;
    public bool FromReturn;          // return 行から合成したもの

    public bool IsSelf
    {
        get { return SenderAlias == ReceiverAlias && !string.IsNullOrEmpty(SenderAlias); }
    }
}

public class PumlActivate : PumlItem
{
    public string Alias = "";
}

public class PumlDeactivate : PumlItem
{
    public string Alias = "";
}

public class PumlDestroyMark : PumlItem
{
    public string Alias = "";
}

public class PumlNote : PumlItem
{
    public string Position = "over";        // "over" | "left" | "right"
    public List<string> Targets = new List<string>();
    public string Text = "";
}

public class PumlRef : PumlItem
{
    public List<string> Targets = new List<string>();
    public string Text = "";
}

public class PumlOperand
{
    public string Guard = "";
    public int Line;
    public List<PumlItem> Items = new List<PumlItem>();
}

public class PumlFragment : PumlItem
{
    public string Operator = "group";
    public string RawText = "";
    public List<PumlOperand> Operands = new List<PumlOperand>();
}

// ------------------------------------------------------------
//  図（解析結果）
// ------------------------------------------------------------
public class PumlDiagram
{
    public string Name = "";
    public string SourcePath = "";
    public List<PumlParticipant> Participants = new List<PumlParticipant>();
    public List<PumlItem> Items = new List<PumlItem>();
    public List<PumlWarning> Warnings = new List<PumlWarning>();

    public bool HasError
    {
        get { return Warnings.Any(w => w.Level == "error"); }
    }

    public PumlParticipant FindParticipant(string alias)
    {
        if (string.IsNullOrEmpty(alias)) return null;
        return Participants.FirstOrDefault(p => string.Equals(p.Alias, alias, StringComparison.Ordinal));
    }

    // 別名から表示名を引く。未知の別名はそのまま返す
    public string LabelOf(string alias)
    {
        var p = FindParticipant(alias);
        return p != null ? p.Label : (alias ?? "");
    }
}

// ------------------------------------------------------------
//  パーサ
//
//  判定の順序が意味を持つ。
//    1. ブロックコメント / 行コメント
//    2. ディレクティブ（@startuml / title / skinparam ...）
//    3. ノート（複数行対応）
//    4. 制御キーワード（alt / else / end / activate / return ...）
//    5. 参加者宣言
//    6. メッセージ（矢印を含む行）
//    7. 該当なし → 情報として記録して読み飛ばす
//
//  メッセージ判定を先にやると 'alt 条件A - 条件B' が矢印付きメッセージに
//  誤判定される。順序を入れ替えないこと。
// ------------------------------------------------------------
public class PlantUmlSequenceParser
{
    private static readonly string[] ParticipantKeywords = new string[]
    {
        "participant", "actor", "boundary", "control", "entity",
        "database", "collections", "queue"
    };

    private static readonly HashSet<string> FragmentKeywords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "alt", "opt", "loop", "par", "break", "critical", "group",
            "neg", "assert", "consider", "ignore", "strict", "seq"
        };

    // 読み飛ばして良いディレクティブの先頭語
    private static readonly HashSet<string> IgnoredDirectives =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "autonumber", "skinparam", "skin", "hide", "show", "scale",
            "header", "footer", "caption", "center", "left", "right",
            "top", "bottom", "allow_mixing", "allowmixing", "mainframe",
            "footbox", "sequence", "order"
        };

    // end で閉じるが、フラグメントの end ではないもの
    private static readonly HashSet<string> NonFragmentEnds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "note", "hnote", "rnote", "box", "legend", "ref",
            "title", "header", "footer"
        };

    private const string ArrowChars = "-<>\\/xo*";

    private PumlDiagram _d;
    private readonly List<PumlFragment> _fragments = new List<PumlFragment>();
    private readonly List<List<PumlItem>> _containers = new List<List<PumlItem>>();
    private readonly List<PumlMessage> _callStack = new List<PumlMessage>();
    private int _declaredOrder;
    private string _pendingCreate;
    private bool _pageEnded;

    // ========================================================
    //  入口
    // ========================================================
    public PumlDiagram Parse(string text, string fallbackName, string sourcePath)
    {
        _d = new PumlDiagram { SourcePath = sourcePath ?? "" };
        _fragments.Clear();
        _containers.Clear();
        _callStack.Clear();
        _declaredOrder = 0;
        _pendingCreate = null;
        _pageEnded = false;

        _containers.Add(_d.Items);

        var lines = (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var startName = "";
        var titleName = "";
        var inBlockComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var lineNo = i + 1;
            var raw = lines[i];
            var line = raw.Trim();

            // ---- 1. コメント ----
            if (inBlockComment)
            {
                if (line.Contains("'/")) inBlockComment = false;
                continue;
            }
            if (line.StartsWith("/'", StringComparison.Ordinal))
            {
                if (!line.Contains("'/")) inBlockComment = true;
                continue;
            }
            if (line.Length == 0) continue;
            if (line.StartsWith("'", StringComparison.Ordinal)) continue;

            // newpage 以降は取り込まない
            if (_pageEnded) continue;

            // ---- 2. ディレクティブ ----
            if (line.StartsWith("@startuml", StringComparison.OrdinalIgnoreCase))
            {
                startName = line.Substring("@startuml".Length).Trim();
                continue;
            }
            if (line.StartsWith("@enduml", StringComparison.OrdinalIgnoreCase))
            {
                _pageEnded = true;
                continue;
            }
            if (line.StartsWith("newpage", StringComparison.OrdinalIgnoreCase))
            {
                Warn(lineNo, "newpage 以降は取り込みません。最初のページのみを対象にします。", line);
                _pageEnded = true;
                continue;
            }
            if (line.StartsWith("!include", StringComparison.OrdinalIgnoreCase))
            {
                Warn(lineNo, "!include は展開しません。", line);
                continue;
            }
            if (line.StartsWith("!", StringComparison.Ordinal))
            {
                Info(lineNo, "ディレクティブを読み飛ばしました。", line);
                continue;
            }
            if (line.StartsWith("title", StringComparison.OrdinalIgnoreCase) && IsWordBoundary(line, 5))
            {
                titleName = PlantUmlText.Normalize(line.Substring(5));
                continue;
            }
            if (line.StartsWith("==", StringComparison.Ordinal))
            {
                Warn(lineNo, "区切り線に対応するモデルが無いため読み飛ばします。", line);
                continue;
            }
            if (line == "..." || line.StartsWith("...", StringComparison.Ordinal) || line == "|||")
            {
                Warn(lineNo, "遅延・空白に対応するモデルが無いため読み飛ばします。", line);
                continue;
            }
            if (line.StartsWith("box", StringComparison.OrdinalIgnoreCase) && IsWordBoundary(line, 3))
            {
                Info(lineNo, "box は読み飛ばします（内部の participant 宣言は取り込みます）。", line);
                continue;
            }
            if (IsIgnoredDirective(line))
            {
                continue;
            }

            // ---- 3. ノート ----
            if (StartsWithWord(line, "note") || StartsWithWord(line, "hnote") || StartsWithWord(line, "rnote"))
            {
                i = ParseNote(lines, i, lineNo);
                continue;
            }

            // ---- 4. 制御キーワード ----
            if (ParseControl(line, lineNo)) continue;

            // ---- 5. 参加者宣言 ----
            if (ParseParticipant(line, lineNo)) continue;

            // ---- 6. メッセージ ----
            if (ParseMessage(line, lineNo)) continue;

            // ---- 7. 該当なし ----
            Info(lineNo, "解釈できない行を読み飛ばしました。", line);
        }

        // 閉じ忘れ
        while (_fragments.Count > 0)
        {
            var f = _fragments[_fragments.Count - 1];
            Warn(f.Line, "複合フラグメント '" + f.Operator + "' が end で閉じられていません。末尾で閉じたものとみなします。", "");
            CloseFragment();
        }

        _d.Name = PickName(titleName, startName, fallbackName);
        return _d;
    }

    private static string PickName(string titleName, string startName, string fallbackName)
    {
        if (!string.IsNullOrEmpty(titleName)) return titleName;
        if (!string.IsNullOrEmpty(startName)) return startName;
        return fallbackName ?? "";
    }

    // ========================================================
    //  ノート
    // ========================================================
    // 戻り値: 消費した最後の行のインデックス
    private int ParseNote(string[] lines, int index, int lineNo)
    {
        var line = lines[index].Trim();
        var head = FirstWord(line);
        var rest = line.Substring(head.Length).Trim();

        var note = new PumlNote { Line = lineNo };

        // 位置指定
        string targetsPart;
        if (StartsWithWord(rest, "over"))
        {
            note.Position = "over";
            targetsPart = rest.Substring(4).Trim();
        }
        else if (StartsWithWord(rest, "left"))
        {
            note.Position = "left";
            targetsPart = StripOf(rest.Substring(4).Trim());
        }
        else if (StartsWithWord(rest, "right"))
        {
            note.Position = "right";
            targetsPart = StripOf(rest.Substring(5).Trim());
        }
        else
        {
            note.Position = "over";
            targetsPart = rest;
        }

        // 本文が同じ行にあるか
        string inlineText = null;
        var colon = targetsPart.IndexOf(':');
        if (colon >= 0)
        {
            inlineText = targetsPart.Substring(colon + 1).Trim();
            targetsPart = targetsPart.Substring(0, colon).Trim();
        }

        foreach (var t in SplitTargets(targetsPart))
        {
            note.Targets.Add(t);
            TouchImplicit(t, lineNo);
        }

        if (inlineText != null)
        {
            note.Text = NormalizeText(inlineText);
            Add(note);
            return index;
        }

        // 複数行。end note まで読む
        var body = new List<string>();
        var i = index + 1;
        for (; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("end note", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("end hnote", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("end rnote", StringComparison.OrdinalIgnoreCase))
                break;
            body.Add(lines[i].TrimEnd());
        }
        if (i >= lines.Length)
        {
            Warn(lineNo, "ノートが end note で閉じられていません。", line);
            i = lines.Length - 1;
        }

        note.Text = NormalizeText(string.Join("\n", body.Select(b => b.Trim()).ToArray()));
        Add(note);
        return i;
    }

    private static string StripOf(string s)
    {
        return StartsWithWord(s, "of") ? s.Substring(2).Trim() : s;
    }

    // ========================================================
    //  制御キーワード
    // ========================================================
    private bool ParseControl(string line, int lineNo)
    {
        var head = FirstWord(line);
        var rest = line.Substring(head.Length).Trim();

        // ---- ref ----
        if (string.Equals(head, "ref", StringComparison.OrdinalIgnoreCase))
        {
            var use = new PumlRef { Line = lineNo };
            var body = rest;
            if (StartsWithWord(body, "over")) body = body.Substring(4).Trim();

            var colon = body.IndexOf(':');
            if (colon >= 0)
            {
                use.Text = NormalizeText(body.Substring(colon + 1).Trim());
                body = body.Substring(0, colon).Trim();
            }
            foreach (var t in SplitTargets(body))
            {
                use.Targets.Add(t);
                TouchImplicit(t, lineNo);
            }
            Add(use);
            return true;
        }

        // ---- 複合フラグメント ----
        if (FragmentKeywords.Contains(head))
        {
            OpenFragment(head.ToLowerInvariant(), rest, lineNo);
            return true;
        }

        if (string.Equals(head, "else", StringComparison.OrdinalIgnoreCase))
        {
            if (_fragments.Count == 0)
            {
                Warn(lineNo, "対応する複合フラグメントが無い else を読み飛ばしました。", line);
                return true;
            }
            var f = _fragments[_fragments.Count - 1];
            _containers.RemoveAt(_containers.Count - 1);
            var operand = new PumlOperand { Guard = NormalizeText(StripBrackets(rest)), Line = lineNo };
            f.Operands.Add(operand);
            _containers.Add(operand.Items);
            return true;
        }

        if (string.Equals(head, "end", StringComparison.OrdinalIgnoreCase))
        {
            var what = FirstWord(rest);
            if (what.Length > 0 && NonFragmentEnds.Contains(what)) return true;   // end note など
            if (_fragments.Count == 0)
            {
                Warn(lineNo, "対応する複合フラグメントが無い end を読み飛ばしました。", line);
                return true;
            }
            CloseFragment();
            return true;
        }

        // ---- 活性化 ----
        if (string.Equals(head, "activate", StringComparison.OrdinalIgnoreCase))
        {
            var alias = FirstToken(rest);
            if (alias.Length == 0) { Warn(lineNo, "activate の対象がありません。", line); return true; }
            TouchImplicit(alias, lineNo);
            Add(new PumlActivate { Alias = alias, Line = lineNo });
            return true;
        }
        if (string.Equals(head, "deactivate", StringComparison.OrdinalIgnoreCase))
        {
            var alias = FirstToken(rest);
            if (alias.Length == 0) { Warn(lineNo, "deactivate の対象がありません。", line); return true; }
            TouchImplicit(alias, lineNo);
            Add(new PumlDeactivate { Alias = alias, Line = lineNo });
            return true;
        }
        if (string.Equals(head, "return", StringComparison.OrdinalIgnoreCase))
        {
            ParseReturn(rest, lineNo, line);
            return true;
        }

        // ---- 生成 / 破棄 ----
        if (string.Equals(head, "create", StringComparison.OrdinalIgnoreCase))
        {
            var body = rest;
            var kw = FirstWord(body);
            if (ParticipantKeywords.Any(k => string.Equals(k, kw, StringComparison.OrdinalIgnoreCase)))
                body = kw + body.Substring(kw.Length);
            else
                body = "participant " + body;

            var p = DeclareParticipant(body, lineNo);
            if (p != null)
            {
                p.DeclaredByCreate = true;
                _pendingCreate = p.Alias;
            }
            return true;
        }
        if (string.Equals(head, "destroy", StringComparison.OrdinalIgnoreCase))
        {
            var alias = FirstToken(rest);
            if (alias.Length == 0) { Warn(lineNo, "destroy の対象がありません。", line); return true; }
            TouchImplicit(alias, lineNo);

            // 直前のメッセージが同じ相手宛なら、それを破棄メッセージに格上げする
            var items = Current();
            var last = items.Count > 0 ? items[items.Count - 1] as PumlMessage : null;
            if (last != null && last.ReceiverAlias == alias && last.Kind == PumlKind.Sync)
                last.Kind = PumlKind.Destroy;

            Add(new PumlDestroyMark { Alias = alias, Line = lineNo });
            return true;
        }

        return false;
    }

    private void ParseReturn(string rest, int lineNo, string source)
    {
        var text = NormalizeText(StripLeadingColon(rest));

        if (_callStack.Count == 0)
        {
            Warn(lineNo, "起動元を特定できないため return を読み飛ばしました。", source);
            return;
        }
        var call = _callStack[_callStack.Count - 1];
        _callStack.RemoveAt(_callStack.Count - 1);

        Add(new PumlMessage
        {
            SenderAlias = call.ReceiverAlias,
            ReceiverAlias = call.SenderAlias,
            Text = text,
            Kind = PumlKind.Reply,
            Dashed = true,
            FromReturn = true,
            Line = lineNo
        });
    }

    private void OpenFragment(string op, string rest, int lineNo)
    {
        var fragment = new PumlFragment
        {
            Operator = op,
            RawText = NormalizeText(rest),
            Line = lineNo
        };
        var operand = new PumlOperand
        {
            Guard = NormalizeText(StripBrackets(rest)),
            Line = lineNo
        };
        fragment.Operands.Add(operand);

        Add(fragment);
        _fragments.Add(fragment);
        _containers.Add(operand.Items);
    }

    private void CloseFragment()
    {
        _fragments.RemoveAt(_fragments.Count - 1);
        if (_containers.Count > 1) _containers.RemoveAt(_containers.Count - 1);
    }

    // ========================================================
    //  参加者宣言
    // ========================================================
    private bool ParseParticipant(string line, int lineNo)
    {
        var head = FirstWord(line);
        if (!ParticipantKeywords.Any(k => string.Equals(k, head, StringComparison.OrdinalIgnoreCase)))
            return false;

        DeclareParticipant(line, lineNo);
        return true;
    }

    private PumlParticipant DeclareParticipant(string line, int lineNo)
    {
        var keyword = FirstWord(line).ToLowerInvariant();
        var body = line.Substring(keyword.Length).Trim();

        // 装飾を落とす: <<stereotype>> / #color / order N
        body = Regex.Replace(body, @"<<[^>]*>>", " ");
        body = Regex.Replace(body, @"\s#[0-9A-Za-z]+", " ");
        body = Regex.Replace(body, @"\s+order\s+-?\d+", " ", RegexOptions.IgnoreCase);
        body = PlantUmlText.Normalize(body);
        if (body.Length == 0)
        {
            Warn(lineNo, "参加者名がありません。", line);
            return null;
        }

        string alias, display;
        var m = Regex.Match(body, "^(?<l>\"[^\"]*\"|\\S+)\\s+as\\s+(?<r>\"[^\"]*\"|\\S+)\\s*$",
                            RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var left = Unquote(m.Groups["l"].Value);
            var right = Unquote(m.Groups["r"].Value);
            // participant "表示名" as 別名  /  participant 別名 as "表示名"
            if (m.Groups["l"].Value.StartsWith("\"", StringComparison.Ordinal))
            {
                display = left; alias = right;
            }
            else if (m.Groups["r"].Value.StartsWith("\"", StringComparison.Ordinal))
            {
                alias = left; display = right;
            }
            else
            {
                display = left; alias = right;
            }
        }
        else
        {
            display = Unquote(body);
            alias = display;
        }

        var existing = _d.FindParticipant(alias);
        if (existing != null)
        {
            if (existing.Implicit)
            {
                existing.Implicit = false;
                existing.Keyword = keyword;
                existing.DisplayName = display;
                existing.Line = lineNo;
            }
            return existing;
        }

        var p = new PumlParticipant
        {
            Alias = alias,
            DisplayName = display,
            Keyword = keyword,
            DeclaredOrder = _declaredOrder++,
            Line = lineNo
        };
        _d.Participants.Add(p);
        return p;
    }

    // 未宣言の別名が出てきたら暗黙の参加者として登録する
    private void TouchImplicit(string alias, int lineNo)
    {
        if (string.IsNullOrEmpty(alias)) return;
        if (_d.FindParticipant(alias) != null) return;

        _d.Participants.Add(new PumlParticipant
        {
            Alias = alias,
            DisplayName = alias,
            Keyword = "participant",
            DeclaredOrder = _declaredOrder++,
            Implicit = true,
            Line = lineNo
        });
    }

    // ========================================================
    //  メッセージ
    // ========================================================
    private bool ParseMessage(string line, int lineNo)
    {
        // 色指定を落とす（矢印種別だけ採用する）
        var work = Regex.Replace(line, @"\[#[^\]]*\]", "");

        string text = null;
        var colon = IndexOfUnquoted(work, ':');
        if (colon >= 0)
        {
            text = work.Substring(colon + 1).Trim();
            work = work.Substring(0, colon);
        }

        int arrowStart, arrowLength;
        if (!FindArrow(work, out arrowStart, out arrowLength)) return false;

        var arrow = work.Substring(arrowStart, arrowLength);
        var leftPart = work.Substring(0, arrowStart).Trim();
        var rightPart = work.Substring(arrowStart + arrowLength).Trim();

        // 末尾の活性化短縮記法
        var activateReceiver = false;
        var deactivateSender = false;
        var kindOverride = (PumlKind?)null;

        rightPart = StripSuffix(rightPart, "++", ref activateReceiver);
        rightPart = StripSuffix(rightPart, "--", ref deactivateSender);
        var create = false;
        rightPart = StripSuffix(rightPart, "**", ref create);
        var destroy = false;
        rightPart = StripSuffix(rightPart, "!!", ref destroy);
        if (create) kindOverride = PumlKind.Create;
        if (destroy) kindOverride = PumlKind.Destroy;

        var sender = Unquote(leftPart.Trim());
        var receiver = Unquote(rightPart.Trim());

        // 逆向き矢印は送受を入れ替えて正規化する
        var reversed = arrow.StartsWith("<", StringComparison.Ordinal);
        if (reversed)
        {
            var t = sender; sender = receiver; receiver = t;
        }

        if (sender == "[" || sender == "]" || receiver == "[" || receiver == "]"
            || sender.Length == 0 || receiver.Length == 0)
        {
            Warn(lineNo, "出現／消失メッセージは取り込めません（メッセージ端の生成が必要）。", line);
            return true;
        }

        var dashed = CountDashes(arrow) >= 2;
        var kind = kindOverride.HasValue ? kindOverride.Value : ClassifyArrow(arrow, dashed);

        if (_pendingCreate != null && receiver == _pendingCreate)
        {
            kind = PumlKind.Create;
            _pendingCreate = null;
        }

        TouchImplicit(sender, lineNo);
        TouchImplicit(receiver, lineNo);

        var message = new PumlMessage
        {
            SenderAlias = sender,
            ReceiverAlias = receiver,
            Text = NormalizeText(text ?? ""),
            Kind = kind,
            ActivateReceiver = activateReceiver,
            DeactivateSender = deactivateSender,
            Dashed = dashed,
            Line = lineNo
        };
        Add(message);

        // return の対応付けに使う呼び出しスタック
        if (kind == PumlKind.Sync) _callStack.Add(message);
        else if (kind == PumlKind.Reply && _callStack.Count > 0) _callStack.RemoveAt(_callStack.Count - 1);

        return true;
    }

    // '-' を1つ以上含み、長さ2文字以上の記号の連続を矢印とみなす
    private static bool FindArrow(string s, out int start, out int length)
    {
        start = -1; length = 0;
        var i = 0;
        while (i < s.Length)
        {
            if (ArrowChars.IndexOf(s[i]) < 0) { i++; continue; }

            var j = i;
            while (j < s.Length && ArrowChars.IndexOf(s[j]) >= 0) j++;

            var run = s.Substring(i, j - i);
            if (run.Length >= 2 && run.IndexOf('-') >= 0)
            {
                start = i; length = run.Length;
                return true;
            }
            i = j;
        }
        return false;
    }

    private static PumlKind ClassifyArrow(string arrow, bool dashed)
    {
        var async = arrow.EndsWith(">>", StringComparison.Ordinal)
                 || arrow.StartsWith("<<", StringComparison.Ordinal)
                 || arrow.EndsWith("\\\\", StringComparison.Ordinal)
                 || arrow.EndsWith("//", StringComparison.Ordinal);
        if (dashed) return PumlKind.Reply;
        return async ? PumlKind.Async : PumlKind.Sync;
    }

    private static int CountDashes(string arrow)
    {
        var n = 0;
        foreach (var ch in arrow) if (ch == '-') n++;
        return n;
    }

    private static string StripSuffix(string s, string suffix, ref bool found)
    {
        if (s.EndsWith(suffix, StringComparison.Ordinal))
        {
            found = true;
            return s.Substring(0, s.Length - suffix.Length).Trim();
        }
        return s;
    }

    // ========================================================
    //  文字列ユーティリティ
    // ========================================================
    private static string FirstWord(string s)
    {
        var i = 0;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        return s.Substring(0, i);
    }

    private static string FirstToken(string s)
    {
        var t = PlantUmlText.Normalize(s);
        if (t.StartsWith("\"", StringComparison.Ordinal))
        {
            var close = t.IndexOf('"', 1);
            if (close > 0) return t.Substring(1, close - 1);
        }
        return Unquote(FirstWord(t));
    }

    private static bool StartsWithWord(string s, string word)
    {
        if (!s.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;
        return IsWordBoundary(s, word.Length);
    }

    private static bool IsWordBoundary(string s, int index)
    {
        return s.Length == index || char.IsWhiteSpace(s[index]) || s[index] == ':';
    }

    private static bool IsIgnoredDirective(string line)
    {
        var head = FirstWord(line).ToLowerInvariant();
        return IgnoredDirectives.Contains(head);
    }

    private static string Unquote(string s)
    {
        var t = (s ?? "").Trim();
        if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"')
            return t.Substring(1, t.Length - 2).Trim();
        return t;
    }

    private static string StripBrackets(string s)
    {
        var t = PlantUmlText.Normalize(s);
        if (t.StartsWith("[", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal))
            return t.Substring(1, t.Length - 2).Trim();
        return t;
    }

    private static string StripLeadingColon(string s)
    {
        var t = (s ?? "").Trim();
        return t.StartsWith(":", StringComparison.Ordinal) ? t.Substring(1).Trim() : t;
    }

    private static int IndexOfUnquoted(string s, char target)
    {
        var quoted = false;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '"') quoted = !quoted;
            else if (!quoted && s[i] == target) return i;
        }
        return -1;
    }

    private static List<string> SplitTargets(string s)
    {
        var result = new List<string>();
        foreach (var part in (s ?? "").Split(','))
        {
            var t = Unquote(part.Trim());
            if (t.Length > 0) result.Add(t);
        }
        return result;
    }

    // \n \t を実体に戻し、Creole の装飾タグだけを落とす。
    // <value> のような業務語を消さないよう、既知のタグ名に限定する
    private static string NormalizeText(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = s.Replace("\\n", "\n").Replace("\\t", "\t");
        t = Regex.Replace(
            t,
            @"</?(?:b|i|u|s|strike|del|size|color|back|font|w|plain|sub|sup|code|img|k)(?::[^>]*)?>",
            "",
            RegexOptions.IgnoreCase);
        return t.Trim();
    }

    // ========================================================
    //  収集
    // ========================================================
    private List<PumlItem> Current()
    {
        return _containers[_containers.Count - 1];
    }

    private void Add(PumlItem item)
    {
        Current().Add(item);
    }

    private void Warn(int line, string message, string source)
    {
        _d.Warnings.Add(new PumlWarning { Line = line, Level = "warn", Message = message, Source = source });
    }

    private void Info(int line, string message, string source)
    {
        _d.Warnings.Add(new PumlWarning { Line = line, Level = "info", Message = message, Source = source });
    }
}

// ============================================================
//  Part 2 / 適用層 (1) : メタモデルマップ
//
//  シーケンス図のメタモデルのクラス名・フィールド名は API リファレンスに
//  載っておらず、プロファイルによって変わる。そこで既存の図を「見本」にして
//  実行時に判別する。
//
//  判別できるのは、同じ情報に対して「専用 API から取れる正解」と
//  「汎用のフィールドアクセス」の両方の経路があるため。
//  例: IMessage.Kind が "sync" を返すと分かっているので、GetFieldString() が
//      "sync" を返すフィールドを探せば、それが種別フィールドになる。
//
//  見本が取れなかった要素は CanWrite* を false にして、
//  「この図には見本が無いので取り込めない」と明示したうえで処理を続ける。
// ============================================================
public class MetaMap
{
    // ---- クラス名 ----
    public string LifelineClass;
    public string ExecutionClass;
    public string MessageClass;
    public string FragmentClass;
    public string OperandClass;
    public string NoteClass;
    public string InteractionUseClass;
    public string DestructionClass;

    // ---- 所有フィールド ----
    public string InteractionLifelinesField;
    public string InteractionMessagesField;
    public string InteractionFragmentsField;
    public string InteractionNotesField;
    public string InteractionUsesField;
    public string LifelineExecutionsField;
    public string LifelineDestructionField;
    public string FragmentOperandsField;

    // ---- 値・参照フィールド ----
    public string LifelineNameField;
    public string LifelineTypeModelField;
    public string MessageNameField;
    public string MessageKindField;
    public string MessageSendPortField;
    public string MessageReceivePortField;
    public string FragmentTextField;
    public string OperandGuardField;
    public string OperandMessagesField;
    public string NoteTextField;
    public string NoteTargetsField;
    public string UseNameField;
    public string UseTargetsField;

    // ---- 種別の実値 ----
    public Dictionary<PumlKind, string> KindValues = new Dictionary<PumlKind, string>
    {
        { PumlKind.Sync,    "sync"    },
        { PumlKind.Async,   "async"   },
        { PumlKind.Reply,   "reply"   },
        { PumlKind.Create,  "create"  },
        { PumlKind.Destroy, "destroy" }
    };

    // ---- 書き込み可否 ----
    public bool CanWriteLifelines;
    public bool CanWriteMessages;
    public bool CanWriteExecutions;
    public bool CanWriteFragments;
    public bool CanWriteNotes;
    public bool CanWriteUses;
    public bool CanWriteDestructions;

    public List<string> Diagnostics = new List<string>();
    public List<string> Unavailable = new List<string>();

    public IInteraction Interaction;

    // ========================================================
    //  判別
    // ========================================================
    public static MetaMap Detect(ISequenceDiagram sample)
    {
        var map = new MetaMap();
        if (sample == null)
        {
            map.Diagnostics.Add("見本のシーケンス図がありません。");
            map.Finish();
            return map;
        }

        map.Interaction = ResolveInteraction(sample);
        if (map.Interaction == null)
            map.Diagnostics.Add("相互作用（Interaction）を特定できませんでした。");

        map.DetectLifelines(sample);
        map.DetectExecutions(sample);
        map.DetectMessages(sample);
        map.DetectFragments(sample);
        map.DetectNotes(sample);
        map.DetectUses(sample);
        map.DetectDestructions(sample);
        map.Finish();
        return map;
    }

    // 見本を複数の図から寄せ集める。1枚に全要素が揃っていないときに使う
    public void Merge(ISequenceDiagram other)
    {
        if (other == null) return;
        if (LifelineClass == null) DetectLifelines(other);
        if (ExecutionClass == null) DetectExecutions(other);
        if (MessageClass == null) DetectMessages(other);
        if (FragmentClass == null) DetectFragments(other);
        if (NoteClass == null) DetectNotes(other);
        if (InteractionUseClass == null) DetectUses(other);
        if (DestructionClass == null) DetectDestructions(other);
        Finish();
    }

    public static IInteraction ResolveInteraction(ISequenceDiagram d)
    {
        if (d == null) return null;

        var direct = d.Model as IInteraction;
        if (direct != null) return direct;

        foreach (var shape in d.Lifelines.Cast<ILifelineShape>())
        {
            var lifeline = shape.Model as ILifeline;
            if (lifeline != null && lifeline.Interaction != null) return lifeline.Interaction;
        }

        var frame = d.Frame as IRepresentation;
        if (frame != null && frame.Model != null)
        {
            var byFrame = frame.Model as IInteraction;
            if (byFrame != null) return byFrame;
            var byOwner = frame.Model.Owner as IInteraction;
            if (byOwner != null) return byOwner;
        }

        if (d.Model != null)
        {
            foreach (var child in d.Model.GetAllChildren().Cast<IModel>())
            {
                var found = child as IInteraction;
                if (found != null) return found;
            }
        }
        return null;
    }

    // ---------- 要素ごとの判別 ----------

    private void DetectLifelines(ISequenceDiagram d)
    {
        foreach (var shape in d.Lifelines.Cast<ILifelineShape>())
        {
            var model = ModelOf(shape);
            if (model == null) continue;

            LifelineClass = model.ClassName;
            InteractionLifelinesField = OwnerFieldOf(model);
            if (LifelineNameField == null)
                LifelineNameField = FindValueField(model, model.Name);
            if (LifelineTypeModelField == null && shape.TypeModel != null)
                LifelineTypeModelField = FindRefField(model, shape.TypeModel as IModel);
            if (LifelineNameField != null) break;
        }
        if (LifelineClass == null) Unavailable.Add("ライフライン（見本なし）");
    }

    private void DetectExecutions(ISequenceDiagram d)
    {
        foreach (var shape in d.ExecutionSpecifications.Cast<IExecutionSpecificationShape>())
        {
            var model = ModelOf(shape);
            if (model == null) continue;
            ExecutionClass = model.ClassName;
            LifelineExecutionsField = OwnerFieldOf(model);
            break;
        }
        if (ExecutionClass == null) Unavailable.Add("実行仕様（見本なし）");
    }

    private void DetectMessages(ISequenceDiagram d)
    {
        foreach (var shape in d.Messages.Cast<IMessageShape>())
        {
            var message = shape.Model as IMessage;
            if (message == null) continue;

            MessageClass = message.ClassName;
            InteractionMessagesField = OwnerFieldOf(message);

            if (MessageNameField == null)
                MessageNameField = FindValueField(message, message.Name);
            if (MessageKindField == null && !string.IsNullOrEmpty(message.Kind))
                MessageKindField = FindValueField(message, message.Kind);
            if (MessageSendPortField == null && message.SendPort != null)
                MessageSendPortField = FindRefField(message, message.SendPort as IModel);
            if (MessageReceivePortField == null && message.ReceivePort != null)
                MessageReceivePortField = FindRefField(message, message.ReceivePort as IModel);

            if (MessageKindField != null && MessageSendPortField != null && MessageReceivePortField != null)
                break;
        }
        if (MessageClass == null) Unavailable.Add("メッセージ（見本なし）");
    }

    private void DetectFragments(ISequenceDiagram d)
    {
        foreach (var shape in d.Fragments.Cast<IFragmentShape>())
        {
            var model = ModelOf(shape);
            if (model == null) continue;

            FragmentClass = model.ClassName;
            InteractionFragmentsField = OwnerFieldOf(model);
            if (FragmentTextField == null)
                FragmentTextField = FindValueField(model, shape.Text) ?? FindValueField(model, model.Name);

            foreach (var operandShape in shape.Operands.Cast<IOperandShape>())
            {
                var operand = ModelOf(operandShape);
                if (operand == null) continue;

                OperandClass = operand.ClassName;
                FragmentOperandsField = OwnerFieldOf(operand);
                if (OperandGuardField == null)
                    OperandGuardField = FindValueField(operand, operandShape.Guard) ?? FindValueField(operand, operand.Name);
                if (OperandMessagesField == null)
                    OperandMessagesField = FindReferenceFieldName(operand.Metaclass, MessageClass, "Message");
                break;
            }
            if (OperandClass != null) break;
        }
        if (FragmentClass == null) Unavailable.Add("複合フラグメント（見本なし）");
        else if (OperandClass == null) Unavailable.Add("操作領域（見本なし）");
    }

    private void DetectNotes(ISequenceDiagram d)
    {
        foreach (var shape in d.Notes.Cast<INoteShape>())
        {
            var model = ModelOf(shape);
            if (model == null) continue;

            NoteClass = model.ClassName;
            InteractionNotesField = OwnerFieldOf(model);
            if (NoteTextField == null)
                NoteTextField = FindValueField(model, shape.Text) ?? FindValueField(model, model.Name);
            if (NoteTargetsField == null && LifelineClass != null)
                NoteTargetsField = FindReferenceFieldName(model.Metaclass, LifelineClass, "Lifeline");
            break;
        }
        if (NoteClass == null) Unavailable.Add("ノート（見本なし）");
    }

    private void DetectUses(ISequenceDiagram d)
    {
        foreach (var shape in d.InteractionUses.Cast<IInteractionUseShape>())
        {
            var model = ModelOf(shape);
            if (model == null) continue;

            InteractionUseClass = model.ClassName;
            InteractionUsesField = OwnerFieldOf(model);
            if (UseNameField == null)
                UseNameField = FindValueField(model, shape.Text) ?? FindValueField(model, model.Name);
            if (UseTargetsField == null && LifelineClass != null)
                UseTargetsField = FindReferenceFieldName(model.Metaclass, LifelineClass, "Lifeline");
            break;
        }
        if (InteractionUseClass == null) Unavailable.Add("相互作用の利用（見本なし）");
    }

    private void DetectDestructions(ISequenceDiagram d)
    {
        foreach (var shape in d.Destructions.Cast<IDestructionShape>())
        {
            var model = ModelOf(shape);
            if (model == null) continue;
            DestructionClass = model.ClassName;
            LifelineDestructionField = OwnerFieldOf(model);
            break;
        }
        if (DestructionClass == null) Unavailable.Add("破棄（見本なし）");
    }

    // ========================================================
    //  書き込み可否の確定
    // ========================================================
    public void Finish()
    {
        CanWriteLifelines = LifelineClass != null && InteractionLifelinesField != null && LifelineNameField != null;
        CanWriteExecutions = ExecutionClass != null && LifelineExecutionsField != null;
        CanWriteMessages = MessageClass != null && InteractionMessagesField != null
                        && MessageSendPortField != null && MessageReceivePortField != null && CanWriteExecutions;
        CanWriteFragments = FragmentClass != null && OperandClass != null
                         && InteractionFragmentsField != null && FragmentOperandsField != null;
        CanWriteNotes = NoteClass != null && InteractionNotesField != null;
        CanWriteUses = InteractionUseClass != null && InteractionUsesField != null;
        CanWriteDestructions = DestructionClass != null && LifelineDestructionField != null;
    }

    // 手動での上書き。存在しないキーは診断に出すだけで例外にしない
    public void ApplyOverrides(Dictionary<string, string> overrides)
    {
        if (overrides == null) return;
        foreach (var pair in overrides)
        {
            var field = typeof(MetaMap).GetField(pair.Key);
            if (field == null || field.FieldType != typeof(string))
            {
                Diagnostics.Add("上書きできないキー: " + pair.Key);
                continue;
            }
            field.SetValue(this, pair.Value);
            Diagnostics.Add("上書き: " + pair.Key + " = " + pair.Value);
        }
        Finish();
    }

    // ========================================================
    //  フィールド探索
    // ========================================================

    // 期待値と同じ文字列を返す値フィールドを探す
    public static string FindValueField(IModel m, string expected)
    {
        if (m == null || string.IsNullOrEmpty(expected)) return null;
        var cls = m.Metaclass;
        if (cls == null) return null;

        string loose = null;
        foreach (var f in cls.GetFields().Cast<IField>())
        {
            if (f.IsEmbedded || f.IsReference) continue;
            string actual;
            try { actual = m.GetFieldString(f.Name); }
            catch (Exception) { continue; }
            if (actual == null) continue;

            if (string.Equals(actual, expected, StringComparison.Ordinal)) return f.Name;
            if (loose == null && string.Equals(actual.Trim(), expected.Trim(), StringComparison.Ordinal))
                loose = f.Name;
        }
        return loose;
    }

    // 対象モデルを保持している参照フィールドを探す
    public static string FindRefField(IModel m, IModel target)
    {
        if (m == null || target == null) return null;
        var cls = m.Metaclass;
        if (cls == null) return null;

        foreach (var f in cls.GetFields().Cast<IField>())
        {
            if (!f.IsReference) continue;
            IEnumerable<object> values;
            try { values = m.GetFieldValues(f.Name).Cast<object>(); }
            catch (Exception) { continue; }

            foreach (var value in values)
            {
                var model = value as IModel;
                if (model != null && model.Id == target.Id) return f.Name;
            }
        }
        return null;
    }

    // 型名から参照フィールドを探す。多重度が上限なしのものを優先する
    public static string FindReferenceFieldName(IClass owner, string typeClassName, string nameHint)
    {
        if (owner == null || string.IsNullOrEmpty(typeClassName)) return null;

        string fallback = null;
        foreach (var f in owner.GetFields().Cast<IField>())
        {
            if (!f.IsReference && !f.IsEmbedded) continue;
            var typeClass = f.TypeClass;
            var typeName = typeClass != null ? typeClass.Name : f.Type;
            if (!string.Equals(typeName, typeClassName, StringComparison.Ordinal)) continue;

            if (f.UpperBound < 0) return f.Name;
            if (fallback == null) fallback = f.Name;
            if (!string.IsNullOrEmpty(nameHint)
                && f.Name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0)
                fallback = f.Name;
        }
        return fallback;
    }

    // ========================================================
    //  小道具
    // ========================================================
    public static IModel ModelOf(object shape)
    {
        var representation = shape as IRepresentation;
        return representation != null ? representation.Model : null;
    }

    private static string OwnerFieldOf(IModel m)
    {
        if (m == null) return null;
        try
        {
            var f = m.GetOwnerField();
            return f != null ? f.Name : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string KindValueOf(PumlKind kind)
    {
        string value;
        return KindValues.TryGetValue(kind, out value) ? value : "sync";
    }

    // ========================================================
    //  レポート
    // ========================================================
    public List<string> Report()
    {
        var lines = new List<string>();
        lines.Add("---- メタモデル判別結果 ----");
        lines.Add("  ライフライン: class=" + Show(LifelineClass)
                  + " ownerField=" + Show(InteractionLifelinesField)
                  + " name=" + Show(LifelineNameField)
                  + " typeModel=" + Show(LifelineTypeModelField));
        lines.Add("  実行仕様: class=" + Show(ExecutionClass)
                  + " ownerField=" + Show(LifelineExecutionsField));
        lines.Add("  メッセージ: class=" + Show(MessageClass)
                  + " ownerField=" + Show(InteractionMessagesField)
                  + " name=" + Show(MessageNameField)
                  + " kind=" + Show(MessageKindField)
                  + " sendPort=" + Show(MessageSendPortField)
                  + " receivePort=" + Show(MessageReceivePortField));
        lines.Add("  複合フラグメント: class=" + Show(FragmentClass)
                  + " ownerField=" + Show(InteractionFragmentsField)
                  + " text=" + Show(FragmentTextField));
        lines.Add("  操作領域: class=" + Show(OperandClass)
                  + " ownerField=" + Show(FragmentOperandsField)
                  + " guard=" + Show(OperandGuardField)
                  + " messages=" + Show(OperandMessagesField));
        lines.Add("  ノート: class=" + Show(NoteClass)
                  + " ownerField=" + Show(InteractionNotesField)
                  + " text=" + Show(NoteTextField)
                  + " targets=" + Show(NoteTargetsField));
        lines.Add("  相互作用の利用: class=" + Show(InteractionUseClass)
                  + " ownerField=" + Show(InteractionUsesField)
                  + " name=" + Show(UseNameField)
                  + " targets=" + Show(UseTargetsField));
        lines.Add("  破棄: class=" + Show(DestructionClass)
                  + " ownerField=" + Show(LifelineDestructionField));

        lines.Add("  書き込み可否: ライフライン=" + CanWriteLifelines
                  + " 実行仕様=" + CanWriteExecutions
                  + " メッセージ=" + CanWriteMessages
                  + " フラグメント=" + CanWriteFragments
                  + " ノート=" + CanWriteNotes
                  + " ref=" + CanWriteUses
                  + " 破棄=" + CanWriteDestructions);

        if (Unavailable.Count > 0)
        {
            lines.Add("  取り込めない要素:");
            foreach (var u in Unavailable) lines.Add("    - " + u);
        }
        foreach (var diagnostic in Diagnostics) lines.Add("  " + diagnostic);
        return lines;
    }

    private static string Show(string s)
    {
        return string.IsNullOrEmpty(s) ? "(不明)" : s;
    }
}

// ============================================================
//  Part 3 / 適用層 (2) : 平坦化・既存索引・突き合わせ
//
//  原則: 既存モデルを消さずに再利用する。
//  作り直すと Id が変わり、参照関連・タグ付き値・他図からの参照が全部切れる。
// ============================================================

public enum OrphanPolicy { Keep, Report, Delete }
public enum MissingPolicy { Skip, Report, Create }
public enum AmbiguousPolicy { Error, FirstMatch }
public enum ChangeKind { Keep, Add, Update, Remove, Skip }

// ------------------------------------------------------------
//  取り込みの設定
// ------------------------------------------------------------
public class ImportSettings
{
    public bool DryRun = true;
    public OrphanPolicy Orphans = OrphanPolicy.Keep;
    public MissingPolicy Missing = MissingPolicy.Skip;
    public AmbiguousPolicy Ambiguous = AmbiguousPolicy.Error;

    public string TemplateDiagramName = "";
    public string TagName = "PlantUmlImport.Key";
    public string AliasTagName = "PlantUmlImport.Key.Alias";
    public bool WriteTags = true;

    public bool UpdateLifelineNames = true;
    public bool ImportFragments = true;
    public bool ImportNotes = true;
    public bool ImportUses = true;

    public string OutputCategory = "PlantUmlImport";

    // メタモデル判別を手で上書きしたいときに使う（ProbeMetamodel の出力を見て書く）
    public Dictionary<string, string> MetaOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
}

// ------------------------------------------------------------
//  平坦化した要素
// ------------------------------------------------------------
public enum FlatKind
{
    FragmentBegin, OperandBegin, OperandEnd, FragmentEnd,
    Message, Activate, Deactivate, Destroy, Note, Use
}

public class FlatLifeline
{
    public PumlParticipant Src;
    public string Alias = "";
    public string Label = "";
    public string Keyword = "participant";
    public string Key = "";
    public int Order;
}

public class FlatOperand
{
    public PumlOperand Src;
    public FlatFragment Fragment;
    public int Index;
    public string Guard = "";
    public string Key = "";
}

public class FlatFragment
{
    public PumlFragment Src;
    public string Operator = "group";
    public string Text = "";
    public int Depth;
    public int Order;
    public string Key = "";
    public FlatOperand Container;
    public List<FlatOperand> Operands = new List<FlatOperand>();
}

public class FlatMessage
{
    public PumlMessage Src;
    public string FromAlias = "";
    public string ToAlias = "";
    public string SenderLabel = "";
    public string ReceiverLabel = "";
    public string Text = "";
    public PumlKind Kind = PumlKind.Sync;
    public int Order;
    public string Key = "";
    public FlatOperand Container;
}

public class FlatNote
{
    public PumlNote Src;
    public List<string> TargetLabels = new List<string>();
    public string Text = "";
    public int Order;
    public string Key = "";
    public FlatOperand Container;
}

public class FlatUse
{
    public PumlRef Src;
    public List<string> TargetLabels = new List<string>();
    public string Text = "";
    public int Order;
    public string Key = "";
    public FlatOperand Container;
}

// 活性化解決のために文書順に並べた出来事
public class FlatEvent
{
    public FlatKind Kind;
    public int Order;
    public int Line;
    public FlatMessage Message;
    public FlatFragment Fragment;
    public FlatOperand Operand;
    public FlatNote Note;
    public FlatUse Use;
    public string LifelineAlias = "";   // Activate / Deactivate / Destroy 用
}

// ------------------------------------------------------------
//  平坦化
//
//  別名（alias）は表示名（label）に解決してから突き合わせキーを作る。
//  Next Design のライフライン名に対応するのは表示名のほうである。
// ------------------------------------------------------------
public class PumlFlattener
{
    public List<FlatLifeline> Lifelines = new List<FlatLifeline>();
    public List<FlatFragment> Fragments = new List<FlatFragment>();
    public List<FlatOperand> Operands = new List<FlatOperand>();
    public List<FlatMessage> Messages = new List<FlatMessage>();
    public List<FlatNote> Notes = new List<FlatNote>();
    public List<FlatUse> Uses = new List<FlatUse>();
    public List<FlatEvent> Events = new List<FlatEvent>();

    private PumlDiagram _d;
    private int _order;
    private readonly Dictionary<string, int> _occurrence = new Dictionary<string, int>(StringComparer.Ordinal);

    public void Run(PumlDiagram diagram)
    {
        _d = diagram;
        _order = 0;
        _occurrence.Clear();

        foreach (var p in diagram.Participants.OrderBy(p => p.DeclaredOrder))
        {
            Lifelines.Add(new FlatLifeline
            {
                Src = p,
                Alias = p.Alias,
                Label = p.Label,
                Keyword = p.Keyword,
                Order = p.DeclaredOrder,
                Key = "L:" + p.Label
            });
        }

        Walk(diagram.Items, null, 0);
    }

    private void Walk(List<PumlItem> items, FlatOperand container, int depth)
    {
        foreach (var item in items)
        {
            var fragment = item as PumlFragment;
            if (fragment != null) { WalkFragment(fragment, container, depth); continue; }

            var message = item as PumlMessage;
            if (message != null)
            {
                var flat = new FlatMessage
                {
                    Src = message,
                    FromAlias = message.SenderAlias,
                    ToAlias = message.ReceiverAlias,
                    SenderLabel = _d.LabelOf(message.SenderAlias),
                    ReceiverLabel = _d.LabelOf(message.ReceiverAlias),
                    Text = message.Text,
                    Kind = message.Kind,
                    Order = Messages.Count,
                    Container = container
                };
                flat.Key = MessageKey(flat.SenderLabel, flat.ReceiverLabel, flat.Kind, flat.Text);
                Messages.Add(flat);
                Emit(FlatKind.Message, message.Line, m: flat);

                if (message.ActivateReceiver)
                    Emit(FlatKind.Activate, message.Line, alias: message.ReceiverAlias);
                if (message.DeactivateSender)
                    Emit(FlatKind.Deactivate, message.Line, alias: message.SenderAlias);
                continue;
            }

            var activate = item as PumlActivate;
            if (activate != null) { Emit(FlatKind.Activate, activate.Line, alias: activate.Alias); continue; }

            var deactivate = item as PumlDeactivate;
            if (deactivate != null) { Emit(FlatKind.Deactivate, deactivate.Line, alias: deactivate.Alias); continue; }

            var destroy = item as PumlDestroyMark;
            if (destroy != null) { Emit(FlatKind.Destroy, destroy.Line, alias: destroy.Alias); continue; }

            var note = item as PumlNote;
            if (note != null)
            {
                var flat = new FlatNote
                {
                    Src = note,
                    Text = note.Text,
                    Order = Notes.Count,
                    Container = container
                };
                flat.TargetLabels.AddRange(note.Targets.Select(t => _d.LabelOf(t)));
                flat.Key = NextKey("N:" + string.Join(",", flat.TargetLabels.ToArray()) + "|" + flat.Text);
                Notes.Add(flat);
                Emit(FlatKind.Note, note.Line, n: flat);
                continue;
            }

            var use = item as PumlRef;
            if (use != null)
            {
                var flat = new FlatUse
                {
                    Src = use,
                    Text = use.Text,
                    Order = Uses.Count,
                    Container = container
                };
                flat.TargetLabels.AddRange(use.Targets.Select(t => _d.LabelOf(t)));
                flat.Key = NextKey("U:" + string.Join(",", flat.TargetLabels.ToArray()) + "|" + flat.Text);
                Uses.Add(flat);
                Emit(FlatKind.Use, use.Line, u: flat);
            }
        }
    }

    private void WalkFragment(PumlFragment fragment, FlatOperand container, int depth)
    {
        var guard0 = fragment.Operands.Count > 0 ? fragment.Operands[0].Guard : "";
        var flat = new FlatFragment
        {
            Src = fragment,
            Operator = fragment.Operator,
            Text = fragment.RawText,
            Depth = depth,
            Order = Fragments.Count,
            Container = container
        };
        flat.Key = NextKey("F:" + fragment.Operator + "|" + guard0 + "|d" + depth);
        Fragments.Add(flat);
        Emit(FlatKind.FragmentBegin, fragment.Line, f: flat);

        for (var i = 0; i < fragment.Operands.Count; i++)
        {
            var source = fragment.Operands[i];
            var operand = new FlatOperand
            {
                Src = source,
                Fragment = flat,
                Index = i,
                Guard = source.Guard,
                Key = flat.Key + "/O" + i
            };
            flat.Operands.Add(operand);
            Operands.Add(operand);

            Emit(FlatKind.OperandBegin, source.Line, o: operand);
            Walk(source.Items, operand, depth + 1);
            Emit(FlatKind.OperandEnd, source.Line, o: operand);
        }

        Emit(FlatKind.FragmentEnd, fragment.Line, f: flat);
    }

    private void Emit(FlatKind kind, int line,
                      FlatMessage m = null, FlatFragment f = null, FlatOperand o = null,
                      FlatNote n = null, FlatUse u = null, string alias = null)
    {
        Events.Add(new FlatEvent
        {
            Kind = kind,
            Order = _order++,
            Line = line,
            Message = m,
            Fragment = f,
            Operand = o,
            Note = n,
            Use = u,
            LifelineAlias = alias ?? ""
        });
    }

    // 同じ内容が複数あっても区別できるよう、出現回数を末尾に付ける
    private string NextKey(string payload)
    {
        int n;
        _occurrence.TryGetValue(payload, out n);
        _occurrence[payload] = n + 1;
        return payload + "#" + n;
    }

    private string MessageKey(string sender, string receiver, PumlKind kind, string text)
    {
        return NextKey("M:" + sender + ">" + receiver + "|" + kind + "|" + text);
    }
}

// ------------------------------------------------------------
//  既存モデルの索引
//
//  座標は「読む」ためだけに使う。書き込みは順序でしか制御できない。
// ------------------------------------------------------------
public class ExistingLifeline
{
    public ILifelineShape Shape;
    public IModel Model;
    public string Label = "";
    public string Tag;
    public string AliasTag;
    public int Order;
    public List<IModel> Executions = new List<IModel>();
    public bool Consumed;
}

public class ExistingMessage
{
    public IMessageShape Shape;
    public IMessage Model;
    public string SenderLabel = "";
    public string ReceiverLabel = "";
    public string Kind = "sync";
    public string Text = "";
    public string Tag;
    public string Key = "";
    public int Order;
    public bool Consumed;
}

public class ExistingOperand
{
    public IOperandShape Shape;
    public IModel Model;
    public string Guard = "";
    public int Index;
    public string Tag;
    public bool Consumed;
}

public class ExistingFragment
{
    public IFragmentShape Shape;
    public IModel Model;
    public string Text = "";
    public string Operator = "group";
    public int Depth;
    public int Order;
    public string Tag;
    public string Key = "";
    public List<ExistingOperand> Operands = new List<ExistingOperand>();
    public bool Consumed;
}

public class ExistingNote
{
    public INoteShape Shape;
    public IModel Model;
    public string Text = "";
    public List<string> TargetLabels = new List<string>();
    public string Tag;
    public string Key = "";
    public int Order;
    public bool Consumed;
}

public class ExistingUse
{
    public IInteractionUseShape Shape;
    public IModel Model;
    public string Text = "";
    public List<string> TargetLabels = new List<string>();
    public string Tag;
    public string Key = "";
    public int Order;
    public bool Consumed;
}

public class ExistingIndex
{
    public ISequenceDiagram Diagram;
    public IInteraction Interaction;
    public List<ExistingLifeline> Lifelines = new List<ExistingLifeline>();
    public List<ExistingMessage> Messages = new List<ExistingMessage>();
    public List<ExistingFragment> Fragments = new List<ExistingFragment>();
    public List<ExistingNote> Notes = new List<ExistingNote>();
    public List<ExistingUse> Uses = new List<ExistingUse>();

    private static readonly PlantUmlOptions Shared = new PlantUmlOptions();

    public static ExistingIndex Build(ISequenceDiagram d, ImportSettings settings)
    {
        var index = new ExistingIndex { Diagram = d };
        index.Interaction = MetaMap.ResolveInteraction(d);

        var tag = settings.TagName;
        var aliasTag = settings.AliasTagName;

        // ---- ライフライン（左から順） ----
        var order = 0;
        foreach (var shape in d.Lifelines.Cast<ILifelineShape>()
                               .OrderBy(l => l.LocationX)
                               .ThenBy(l => l.Id, StringComparer.Ordinal))
        {
            var model = MetaMap.ModelOf(shape);
            var entry = new ExistingLifeline
            {
                Shape = shape,
                Model = model,
                Label = LabelOf(shape),
                Tag = TagOf(model, tag),
                AliasTag = TagOf(model, aliasTag),
                Order = order++
            };
            index.Lifelines.Add(entry);
        }

        // ---- 実行仕様（ライフラインごとに上から順） ----
        foreach (var shape in d.ExecutionSpecifications.Cast<IExecutionSpecificationShape>()
                               .OrderBy(e => e.LocationY)
                               .ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            var lifeline = shape.Lifeline;
            if (lifeline == null) continue;
            var owner = index.Lifelines.FirstOrDefault(l => l.Shape.Id == lifeline.Id);
            var model = MetaMap.ModelOf(shape);
            if (owner != null && model != null) owner.Executions.Add(model);
        }

        // ---- メッセージ（上から順） ----
        order = 0;
        var messageOccurrence = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var shape in d.Messages.Cast<IMessageShape>()
                               .OrderBy(m => m.SourceY)
                               .ThenBy(m => m.Id, StringComparer.Ordinal))
        {
            var model = shape.Model as IMessage;
            var kind = model != null && !string.IsNullOrEmpty(model.Kind)
                     ? model.Kind.ToLowerInvariant() : "sync";
            var entry = new ExistingMessage
            {
                Shape = shape,
                Model = model,
                SenderLabel = LabelOf(shape.Sender),
                ReceiverLabel = LabelOf(shape.Receiver),
                Kind = kind,
                Text = PlantUmlText.Normalize(shape.Text),
                Tag = TagOf(model, tag),
                Order = order++
            };
            entry.Key = Occ(messageOccurrence,
                "M:" + entry.SenderLabel + ">" + entry.ReceiverLabel
                + "|" + KindEnumName(entry.Kind) + "|" + entry.Text);
            index.Messages.Add(entry);
        }

        // ---- 複合フラグメント（上から順、面積の大きい順） ----
        var fragmentShapes = d.Fragments.Cast<IFragmentShape>()
                              .OrderBy(f => f.LocationY)
                              .ThenByDescending(f => (double)f.Width * (double)f.Height)
                              .ThenBy(f => f.Id, StringComparer.Ordinal)
                              .ToList();

        order = 0;
        var fragmentOccurrence = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var shape in fragmentShapes)
        {
            var operands = shape.Operands.Cast<IOperandShape>()
                                .OrderBy(o => o.Position)
                                .ThenBy(o => o.Id, StringComparer.Ordinal)
                                .ToList();

            var entry = new ExistingFragment
            {
                Shape = shape,
                Model = MetaMap.ModelOf(shape),
                Text = PlantUmlText.Normalize(shape.Text),
                Depth = fragmentShapes.Count(other => Contains(other, shape)),
                Order = order++
            };
            entry.Operator = OperatorOf(entry.Text);
            entry.Tag = TagOf(entry.Model, tag);

            for (var i = 0; i < operands.Count; i++)
            {
                var operandModel = MetaMap.ModelOf(operands[i]);
                entry.Operands.Add(new ExistingOperand
                {
                    Shape = operands[i],
                    Model = operandModel,
                    Guard = PlantUmlText.Normalize(operands[i].Guard),
                    Index = i,
                    Tag = TagOf(operandModel, tag)
                });
            }

            var guard0 = entry.Operands.Count > 0 ? entry.Operands[0].Guard : "";
            entry.Key = Occ(fragmentOccurrence,
                "F:" + entry.Operator + "|" + guard0 + "|d" + entry.Depth);
            index.Fragments.Add(entry);
        }

        // ---- ノート ----
        order = 0;
        var noteOccurrence = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var shape in d.Notes.Cast<INoteShape>()
                               .OrderBy(n => n.LocationY)
                               .ThenBy(n => n.Id, StringComparer.Ordinal))
        {
            var model = MetaMap.ModelOf(shape);
            var entry = new ExistingNote
            {
                Shape = shape,
                Model = model,
                Text = PlantUmlText.Normalize(shape.Text),
                Tag = TagOf(model, tag),
                Order = order++
            };
            entry.TargetLabels.AddRange(NoteTargetsOf(shape, index));
            entry.Key = Occ(noteOccurrence,
                "N:" + string.Join(",", entry.TargetLabels.ToArray()) + "|" + entry.Text);
            index.Notes.Add(entry);
        }

        // ---- 相互作用の利用 ----
        order = 0;
        var useOccurrence = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var shape in d.InteractionUses.Cast<IInteractionUseShape>()
                               .OrderBy(u => u.LocationY)
                               .ThenBy(u => u.Id, StringComparer.Ordinal))
        {
            var model = MetaMap.ModelOf(shape);
            var entry = new ExistingUse
            {
                Shape = shape,
                Model = model,
                Text = PlantUmlText.Normalize(shape.Text),
                Tag = TagOf(model, tag),
                Order = order++
            };
            entry.TargetLabels.AddRange(shape.Lifelines.Cast<ILifelineShape>()
                                             .OrderBy(l => l.LocationX)
                                             .Select(l => LabelOf(l)));
            entry.Key = Occ(useOccurrence,
                "U:" + string.Join(",", entry.TargetLabels.ToArray()) + "|" + entry.Text);
            index.Uses.Add(entry);
        }

        return index;
    }

    // ---------- 小道具 ----------

    public static string LabelOf(ILifelineShape l)
    {
        if (l == null) return "";
        var label = PlantUmlText.Normalize(l.Text);
        if (label.Length == 0 && l.TypeModel != null) label = PlantUmlText.Normalize(l.TypeModel.Name);
        if (label.Length == 0)
        {
            var model = MetaMap.ModelOf(l);
            if (model != null) label = PlantUmlText.Normalize(model.Name);
        }
        return label;
    }

    private static IEnumerable<string> NoteTargetsOf(INoteShape n, ExistingIndex index)
    {
        var result = new List<string>();
        foreach (var anchor in n.NoteAnchors.Cast<INoteAnchorShape>()
                                .OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var other = anchor.Source != null && anchor.Source.Id == n.Id ? anchor.Target : anchor.Source;

            var lifeline = other as ILifelineShape;
            if (lifeline != null) { result.Add(LabelOf(lifeline)); continue; }

            var execution = other as IExecutionSpecificationShape;
            if (execution != null && execution.Lifeline != null)
            {
                result.Add(LabelOf(execution.Lifeline));
                continue;
            }

            var message = other as IMessageShape;
            if (message != null) result.Add(LabelOf(message.Sender ?? message.Receiver));
        }
        return result.Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static bool Contains(IFragmentShape outer, IFragmentShape inner)
    {
        if (outer == null || inner == null || outer.Id == inner.Id) return false;
        return outer.LocationX <= inner.LocationX
            && outer.LocationY <= inner.LocationY
            && outer.LocationX + outer.Width >= inner.LocationX + inner.Width
            && outer.LocationY + outer.Height >= inner.LocationY + inner.Height;
    }

    private static string OperatorOf(string text)
    {
        if (string.IsNullOrEmpty(text)) return "group";

        string op;
        if (Shared.OperatorMap.TryGetValue(text, out op)) return op;

        var head = text.Split(new[] { ' ', '[', '(', '　' }, StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault();
        if (!string.IsNullOrEmpty(head) && Shared.OperatorMap.TryGetValue(head, out op)) return op;

        return "group";
    }

    // Next Design の Kind 文字列を PumlKind の名前に揃える
    public static string KindEnumName(string kind)
    {
        switch ((kind ?? "").ToLowerInvariant())
        {
            case "async": return PumlKind.Async.ToString();
            case "reply": return PumlKind.Reply.ToString();
            case "create": return PumlKind.Create.ToString();
            case "destroy": return PumlKind.Destroy.ToString();
            default: return PumlKind.Sync.ToString();
        }
    }

    private static string Occ(Dictionary<string, int> counter, string payload)
    {
        int n;
        counter.TryGetValue(payload, out n);
        counter[payload] = n + 1;
        return payload + "#" + n;
    }

    public static string TagOf(IModel m, string name)
    {
        if (m == null || string.IsNullOrEmpty(name)) return null;
        try
        {
            var value = m.GetTagValue(name);
            return value == null ? null : value.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

// ------------------------------------------------------------
//  差分プラン（レポート用）
// ------------------------------------------------------------
public class PlanEntry
{
    public string Category = "";
    public ChangeKind Change = ChangeKind.Keep;
    public string Key = "";
    public string Description = "";
}

public class ImportPlan
{
    public List<PlanEntry> Entries = new List<PlanEntry>();
    public List<string> Warnings = new List<string>();

    public void Add(string category, ChangeKind change, string key, string description)
    {
        Entries.Add(new PlanEntry
        {
            Category = category,
            Change = change,
            Key = key ?? "",
            Description = description ?? ""
        });
    }

    public int Count(string category, ChangeKind change)
    {
        return Entries.Count(e => e.Category == category && e.Change == change);
    }

    // 種別を問わない件数
    public int Count2(ChangeKind change)
    {
        return Entries.Count(e => e.Change == change);
    }

    public bool HasChanges
    {
        get { return Entries.Any(e => e.Change == ChangeKind.Add
                                   || e.Change == ChangeKind.Update
                                   || e.Change == ChangeKind.Remove); }
    }

    public static readonly string[] Categories = new string[]
    {
        "ライフライン", "メッセージ", "複合フラグメント", "操作領域", "ノート", "相互作用の利用"
    };

    public List<string> Dump(bool verbose)
    {
        var lines = new List<string>();
        foreach (var category in Categories)
        {
            var keep = Count(category, ChangeKind.Keep);
            var add = Count(category, ChangeKind.Add);
            var update = Count(category, ChangeKind.Update);
            var remove = Count(category, ChangeKind.Remove);
            var skip = Count(category, ChangeKind.Skip);
            if (keep + add + update + remove + skip == 0) continue;

            lines.Add("    " + Pad(category, 14)
                      + "= " + keep + " 件そのまま"
                      + " / +" + add + " 追加"
                      + " / " + update + " 件更新"
                      + " / -" + remove + " 削除"
                      + (skip > 0 ? " / " + skip + " 件スキップ" : ""));
        }

        if (verbose)
        {
            foreach (var e in Entries.Where(e => e.Change != ChangeKind.Keep))
                lines.Add("      [" + Label(e.Change) + "] " + e.Description);
        }
        foreach (var w in Warnings) lines.Add("    警告: " + w);
        return lines;
    }

    public static string Label(ChangeKind change)
    {
        switch (change)
        {
            case ChangeKind.Add: return "追加";
            case ChangeKind.Update: return "更新";
            case ChangeKind.Remove: return "削除";
            case ChangeKind.Skip: return "スキップ";
            default: return "そのまま";
        }
    }

    private static string Pad(string s, int width)
    {
        var length = 0;
        foreach (var ch in s) length += ch < 128 ? 1 : 2;
        return s + new string(' ', Math.Max(0, width - length));
    }
}

// ------------------------------------------------------------
//  突き合わせ結果
// ------------------------------------------------------------
public class MatchSet
{
    public Dictionary<string, ExistingLifeline> Lifelines = new Dictionary<string, ExistingLifeline>(StringComparer.Ordinal);
    public Dictionary<string, ExistingMessage> Messages = new Dictionary<string, ExistingMessage>(StringComparer.Ordinal);
    public Dictionary<string, ExistingFragment> Fragments = new Dictionary<string, ExistingFragment>(StringComparer.Ordinal);
    public Dictionary<string, ExistingOperand> Operands = new Dictionary<string, ExistingOperand>(StringComparer.Ordinal);
    public Dictionary<string, ExistingNote> Notes = new Dictionary<string, ExistingNote>(StringComparer.Ordinal);
    public Dictionary<string, ExistingUse> Uses = new Dictionary<string, ExistingUse>(StringComparer.Ordinal);

    public List<ExistingLifeline> OrphanLifelines = new List<ExistingLifeline>();
    public List<ExistingMessage> OrphanMessages = new List<ExistingMessage>();
    public List<ExistingFragment> OrphanFragments = new List<ExistingFragment>();
    public List<ExistingNote> OrphanNotes = new List<ExistingNote>();
    public List<ExistingUse> OrphanUses = new List<ExistingUse>();

    public ImportPlan Plan = new ImportPlan();
}

// ------------------------------------------------------------
//  3段照合
//
//    1段: インポートタグ、または完全一致キー   … 変更なし
//    2段: 送信元・受信先・種別（本文を無視）    … 本文の書き換えを拾う
//    3段: 種別・本文（送信元と受信先を無視）    … ライフラインの改名を拾う
// ------------------------------------------------------------
public class ImportMatcher
{
    public static MatchSet Build(PumlFlattener flat, ExistingIndex index, ImportSettings settings, MetaMap map)
    {
        var set = new MatchSet();
        MatchLifelines(flat, index, settings, set);
        MatchMessages(flat, index, settings, set);
        MatchFragments(flat, index, settings, set);
        MatchNotes(flat, index, settings, set);
        MatchUses(flat, index, settings, set);
        AddOrphans(index, settings, set);
        NoteUnavailable(map, settings, set);
        return set;
    }

    // ---------- ライフライン ----------
    private static void MatchLifelines(PumlFlattener flat, ExistingIndex index, ImportSettings settings, MatchSet set)
    {
        foreach (var l in flat.Lifelines)
        {
            var hit = Take(index.Lifelines, e => e.Tag == l.Key)
                   ?? Take(index.Lifelines, e => string.Equals(e.Label, l.Label, StringComparison.Ordinal))
                   ?? Take(index.Lifelines, e => e.AliasTag != null && e.AliasTag == l.Alias);

            if (hit == null)
            {
                set.Plan.Add("ライフライン", ChangeKind.Add, l.Key, l.Label);
                continue;
            }

            set.Lifelines[l.Key] = hit;
            var renamed = !string.Equals(hit.Label, l.Label, StringComparison.Ordinal);
            set.Plan.Add("ライフライン",
                         renamed && settings.UpdateLifelineNames ? ChangeKind.Update : ChangeKind.Keep,
                         l.Key,
                         renamed ? hit.Label + " → " + l.Label : l.Label);
        }
    }

    // ---------- メッセージ ----------
    private static void MatchMessages(PumlFlattener flat, ExistingIndex index, ImportSettings settings, MatchSet set)
    {
        foreach (var m in flat.Messages)
        {
            var kind = m.Kind.ToString();

            // 1段
            var hit = Take(index.Messages, e => e.Tag == m.Key)
                   ?? Take(index.Messages, e => string.Equals(e.Key, m.Key, StringComparison.Ordinal));

            // 2段: 本文を無視する（本文の書き換えに追従）
            if (hit == null)
                hit = Take(index.Messages, e =>
                        string.Equals(e.SenderLabel, m.SenderLabel, StringComparison.Ordinal)
                     && string.Equals(e.ReceiverLabel, m.ReceiverLabel, StringComparison.Ordinal)
                     && string.Equals(ExistingIndex.KindEnumName(e.Kind), kind, StringComparison.Ordinal));

            // 3段: 送信元と受信先を無視する（ライフラインの改名に追従）
            if (hit == null)
                hit = Take(index.Messages, e =>
                        string.Equals(e.Text, m.Text, StringComparison.Ordinal)
                     && m.Text.Length > 0
                     && string.Equals(ExistingIndex.KindEnumName(e.Kind), kind, StringComparison.Ordinal));

            if (hit == null)
            {
                set.Plan.Add("メッセージ", ChangeKind.Add, m.Key, Describe(m));
                continue;
            }

            set.Messages[m.Key] = hit;
            var changed = !string.Equals(hit.Text, m.Text, StringComparison.Ordinal)
                       || !string.Equals(ExistingIndex.KindEnumName(hit.Kind), kind, StringComparison.Ordinal)
                       || !string.Equals(hit.SenderLabel, m.SenderLabel, StringComparison.Ordinal)
                       || !string.Equals(hit.ReceiverLabel, m.ReceiverLabel, StringComparison.Ordinal);

            set.Plan.Add("メッセージ", changed ? ChangeKind.Update : ChangeKind.Keep, m.Key,
                         changed ? Describe(hit) + " → " + Describe(m) : Describe(m));
        }
    }

    // ---------- 複合フラグメントと操作領域 ----------
    private static void MatchFragments(PumlFlattener flat, ExistingIndex index, ImportSettings settings, MatchSet set)
    {
        if (!settings.ImportFragments)
        {
            foreach (var f in flat.Fragments)
                set.Plan.Add("複合フラグメント", ChangeKind.Skip, f.Key, f.Operator);
            return;
        }

        foreach (var f in flat.Fragments)
        {
            var hit = Take(index.Fragments, e => e.Tag == f.Key)
                   ?? Take(index.Fragments, e => string.Equals(e.Key, f.Key, StringComparison.Ordinal))
                   ?? Take(index.Fragments, e =>
                        string.Equals(e.Operator, f.Operator, StringComparison.OrdinalIgnoreCase)
                     && e.Depth == f.Depth);

            if (hit == null)
            {
                set.Plan.Add("複合フラグメント", ChangeKind.Add, f.Key, f.Operator + " " + f.Text);
                foreach (var o in f.Operands)
                    set.Plan.Add("操作領域", ChangeKind.Add, o.Key, "[" + o.Guard + "]");
                continue;
            }

            set.Fragments[f.Key] = hit;
            var changed = !string.Equals(hit.Operator, f.Operator, StringComparison.OrdinalIgnoreCase);
            set.Plan.Add("複合フラグメント", changed ? ChangeKind.Update : ChangeKind.Keep, f.Key,
                         f.Operator + " " + f.Text);

            MatchOperands(f, hit, set);
        }
    }

    // 操作領域は所属フラグメント内の位置で対応させる
    private static void MatchOperands(FlatFragment f, ExistingFragment hit, MatchSet set)
    {
        foreach (var o in f.Operands)
        {
            var existing = hit.Operands.FirstOrDefault(e => !e.Consumed && e.Index == o.Index);
            if (existing == null)
            {
                set.Plan.Add("操作領域", ChangeKind.Add, o.Key, "[" + o.Guard + "]");
                continue;
            }
            existing.Consumed = true;
            set.Operands[o.Key] = existing;

            var changed = !string.Equals(existing.Guard, o.Guard, StringComparison.Ordinal);
            set.Plan.Add("操作領域", changed ? ChangeKind.Update : ChangeKind.Keep, o.Key,
                         changed ? "[" + existing.Guard + "] → [" + o.Guard + "]" : "[" + o.Guard + "]");
        }
    }

    // ---------- ノート ----------
    private static void MatchNotes(PumlFlattener flat, ExistingIndex index, ImportSettings settings, MatchSet set)
    {
        if (!settings.ImportNotes)
        {
            foreach (var n in flat.Notes) set.Plan.Add("ノート", ChangeKind.Skip, n.Key, Shorten(n.Text));
            return;
        }

        foreach (var n in flat.Notes)
        {
            var hit = Take(index.Notes, e => e.Tag == n.Key)
                   ?? Take(index.Notes, e => string.Equals(e.Key, n.Key, StringComparison.Ordinal))
                   ?? Take(index.Notes, e => string.Equals(e.Text, n.Text, StringComparison.Ordinal));

            if (hit == null)
            {
                set.Plan.Add("ノート", ChangeKind.Add, n.Key, Shorten(n.Text));
                continue;
            }
            set.Notes[n.Key] = hit;
            var changed = !string.Equals(hit.Text, n.Text, StringComparison.Ordinal);
            set.Plan.Add("ノート", changed ? ChangeKind.Update : ChangeKind.Keep, n.Key, Shorten(n.Text));
        }
    }

    // ---------- 相互作用の利用 ----------
    private static void MatchUses(PumlFlattener flat, ExistingIndex index, ImportSettings settings, MatchSet set)
    {
        if (!settings.ImportUses)
        {
            foreach (var u in flat.Uses) set.Plan.Add("相互作用の利用", ChangeKind.Skip, u.Key, Shorten(u.Text));
            return;
        }

        foreach (var u in flat.Uses)
        {
            var hit = Take(index.Uses, e => e.Tag == u.Key)
                   ?? Take(index.Uses, e => string.Equals(e.Key, u.Key, StringComparison.Ordinal))
                   ?? Take(index.Uses, e => string.Equals(e.Text, u.Text, StringComparison.Ordinal));

            if (hit == null)
            {
                set.Plan.Add("相互作用の利用", ChangeKind.Add, u.Key, Shorten(u.Text));
                continue;
            }
            set.Uses[u.Key] = hit;
            var changed = !string.Equals(hit.Text, u.Text, StringComparison.Ordinal);
            set.Plan.Add("相互作用の利用", changed ? ChangeKind.Update : ChangeKind.Keep, u.Key, Shorten(u.Text));
        }
    }

    // ---------- 孤児（Next Design 側にしか無い要素） ----------
    private static void AddOrphans(ExistingIndex index, ImportSettings settings, MatchSet set)
    {
        set.OrphanLifelines.AddRange(index.Lifelines.Where(e => !e.Consumed));
        set.OrphanMessages.AddRange(index.Messages.Where(e => !e.Consumed));
        set.OrphanFragments.AddRange(index.Fragments.Where(e => !e.Consumed));
        set.OrphanNotes.AddRange(index.Notes.Where(e => !e.Consumed));
        set.OrphanUses.AddRange(index.Uses.Where(e => !e.Consumed));

        var change = settings.Orphans == OrphanPolicy.Delete ? ChangeKind.Remove : ChangeKind.Skip;

        foreach (var e in set.OrphanLifelines) Orphan(set, settings, "ライフライン", change, e.Label);
        foreach (var e in set.OrphanMessages) Orphan(set, settings, "メッセージ", change, Describe(e));
        if (settings.ImportFragments)
            foreach (var e in set.OrphanFragments) Orphan(set, settings, "複合フラグメント", change, e.Operator + " " + e.Text);
        if (settings.ImportNotes)
            foreach (var e in set.OrphanNotes) Orphan(set, settings, "ノート", change, Shorten(e.Text));
        if (settings.ImportUses)
            foreach (var e in set.OrphanUses) Orphan(set, settings, "相互作用の利用", change, Shorten(e.Text));
    }

    private static void Orphan(MatchSet set, ImportSettings settings, string category, ChangeKind change, string what)
    {
        set.Plan.Add(category, change, "", what);
        if (settings.Orphans != OrphanPolicy.Delete)
            set.Plan.Warnings.Add(category + " 「" + what + "」は PlantUML 側に記述がありません（残置）。");
    }

    private static void NoteUnavailable(MetaMap map, ImportSettings settings, MatchSet set)
    {
        if (map == null) return;
        if (!map.CanWriteLifelines) set.Plan.Warnings.Add("ライフラインを書き込めません（メタモデル未判別）。");
        if (!map.CanWriteMessages) set.Plan.Warnings.Add("メッセージを書き込めません（メタモデル未判別）。");
        if (settings.ImportFragments && !map.CanWriteFragments)
            set.Plan.Warnings.Add("複合フラグメントを書き込めません（メタモデル未判別）。");
        if (settings.ImportNotes && !map.CanWriteNotes)
            set.Plan.Warnings.Add("ノートを書き込めません（メタモデル未判別）。");
        if (settings.ImportUses && !map.CanWriteUses)
            set.Plan.Warnings.Add("相互作用の利用を書き込めません（メタモデル未判別）。");
    }

    // ---------- 小道具 ----------

    // 条件に合う未消費の1件を取り、消費済みにする
    private static ExistingLifeline Take(List<ExistingLifeline> pool, Func<ExistingLifeline, bool> match)
    {
        var hit = pool.FirstOrDefault(e => !e.Consumed && match(e));
        if (hit != null) hit.Consumed = true;
        return hit;
    }

    private static ExistingMessage Take(List<ExistingMessage> pool, Func<ExistingMessage, bool> match)
    {
        var hit = pool.FirstOrDefault(e => !e.Consumed && match(e));
        if (hit != null) hit.Consumed = true;
        return hit;
    }

    private static ExistingFragment Take(List<ExistingFragment> pool, Func<ExistingFragment, bool> match)
    {
        var hit = pool.FirstOrDefault(e => !e.Consumed && match(e));
        if (hit != null) hit.Consumed = true;
        return hit;
    }

    private static ExistingNote Take(List<ExistingNote> pool, Func<ExistingNote, bool> match)
    {
        var hit = pool.FirstOrDefault(e => !e.Consumed && match(e));
        if (hit != null) hit.Consumed = true;
        return hit;
    }

    private static ExistingUse Take(List<ExistingUse> pool, Func<ExistingUse, bool> match)
    {
        var hit = pool.FirstOrDefault(e => !e.Consumed && match(e));
        if (hit != null) hit.Consumed = true;
        return hit;
    }

    private static string Describe(FlatMessage m)
    {
        return m.SenderLabel + " " + Arrow(m.Kind.ToString()) + " " + m.ReceiverLabel
             + (m.Text.Length > 0 ? " : " + Shorten(m.Text) : "");
    }

    private static string Describe(ExistingMessage m)
    {
        return m.SenderLabel + " " + Arrow(ExistingIndex.KindEnumName(m.Kind)) + " " + m.ReceiverLabel
             + (m.Text.Length > 0 ? " : " + Shorten(m.Text) : "");
    }

    private static string Arrow(string kindName)
    {
        if (kindName == PumlKind.Async.ToString()) return "->>";
        if (kindName == PumlKind.Reply.ToString()) return "-->";
        if (kindName == PumlKind.Create.ToString()) return "-*>";
        if (kindName == PumlKind.Destroy.ToString()) return "-x>";
        return "->";
    }

    private static string Shorten(string s)
    {
        var t = PlantUmlText.Inline(PlantUmlText.Normalize(s));
        return t.Length <= 40 ? t : t.Substring(0, 40) + "…";
    }
}

// ============================================================
//  Part 4 / 適用層 (3) : 活性化区間の解決とモデルへの書き込み
// ============================================================

// ------------------------------------------------------------
//  実行仕様をどこまで自動で補うか
// ------------------------------------------------------------
public enum ActivationMode
{
    /// 実行仕様を新設しない（既存を再利用するだけ）
    None,
    /// activate / deactivate が書かれた場所だけを区間にする
    Explicit,
    /// 上記に加えて、同期メッセージの受信側を自動で活性化する（既定）
    Auto,
}

public class ActivationSpan
{
    public string LifelineAlias = "";
    public int Index;          // 同一ライフライン内での通し番号（0 起点）
    public int Depth;          // 入れ子の深さ（0 が最外）
    public bool IsExplicit;
    public bool IsRoot;
    public ActivationSpan Parent;
    public IModel Model;       // 対応付けられた Next Design の実行仕様モデル

    public string Key { get { return LifelineAlias + "#" + Index.ToString(); } }
}

// ------------------------------------------------------------
//  活性化区間の解決（Next Design の API には依存しない）
//
//  実行仕様には名前が無く安定キーを作れないので、
//  同じライフラインの上から n 番目どうしを対応させる。
// ------------------------------------------------------------
public class ActivationResolver
{
    private readonly ActivationMode _mode;
    private readonly Dictionary<string, List<ActivationSpan>> _spans =
        new Dictionary<string, List<ActivationSpan>>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ActivationSpan>> _open =
        new Dictionary<string, List<ActivationSpan>>(StringComparer.Ordinal);
    private readonly Dictionary<FlatMessage, ActivationSpan> _send =
        new Dictionary<FlatMessage, ActivationSpan>();
    private readonly Dictionary<FlatMessage, ActivationSpan> _recv =
        new Dictionary<FlatMessage, ActivationSpan>();
    private readonly List<ActivationSpan> _callStack = new List<ActivationSpan>();
    private string _suppressActivateFor;

    public List<string> Warnings = new List<string>();

    public ActivationResolver(ActivationMode mode)
    {
        _mode = mode;
    }

    public ActivationMode Mode { get { return _mode; } }

    public IEnumerable<string> Aliases { get { return _spans.Keys; } }

    public int TotalSpanCount { get { return _spans.Values.Sum(v => v.Count); } }

    public List<ActivationSpan> SpansOf(string alias)
    {
        List<ActivationSpan> list;
        return _spans.TryGetValue(alias ?? "", out list) ? list : new List<ActivationSpan>();
    }

    public ActivationSpan SendSpanOf(FlatMessage m)
    {
        ActivationSpan span;
        return m != null && _send.TryGetValue(m, out span) ? span : null;
    }

    public ActivationSpan ReceiveSpanOf(FlatMessage m)
    {
        ActivationSpan span;
        return m != null && _recv.TryGetValue(m, out span) ? span : null;
    }

    // ========================================================
    //  解決
    // ========================================================
    public void Resolve(List<FlatEvent> events)
    {
        for (var i = 0; i < events.Count; i++)
        {
            var ev = events[i];

            if (ev.Kind == FlatKind.Message)
            {
                var m = ev.Message;
                EnsureSendSpan(m);
                HandleMessage(m, FollowedByActivate(events, i, m.ToAlias));
                continue;
            }

            if (ev.Kind == FlatKind.Activate)
            {
                if (_suppressActivateFor != null
                    && string.Equals(_suppressActivateFor, ev.LifelineAlias, StringComparison.Ordinal))
                {
                    _suppressActivateFor = null;
                    continue;
                }
                Open(ev.LifelineAlias, true);
                continue;
            }

            if (ev.Kind == FlatKind.Deactivate) { Close(ev.LifelineAlias); continue; }
            if (ev.Kind == FlatKind.Destroy) { CloseAll(ev.LifelineAlias); continue; }
        }

        // 閉じ忘れは図の末尾で閉じたものとみなす
        foreach (var alias in _open.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
        {
            var stack = _open[alias];
            var explicitLeft = stack.Count(s => s.IsExplicit);
            if (explicitLeft > 0)
                Warnings.Add("'" + alias + "' の activate が " + explicitLeft
                             + " 件閉じられていません。図の末尾で閉じたものとみなします。");
            stack.Clear();
        }
        _callStack.Clear();
    }

    private void HandleMessage(FlatMessage m, bool activateFollows)
    {
        if (m.Kind == PumlKind.Reply)
        {
            _recv[m] = Top(m.ToAlias);
            CloseImplicitCall(m.FromAlias);
            return;
        }
        if (m.Kind == PumlKind.Destroy)
        {
            _recv[m] = Open(m.ToAlias, false);
            CloseAll(m.ToAlias);
            return;
        }
        if (activateFollows)
        {
            _recv[m] = Open(m.ToAlias, true);
            _suppressActivateFor = m.ToAlias;
            return;
        }
        if (_mode == ActivationMode.Auto && m.Kind == PumlKind.Sync)
        {
            var span = Open(m.ToAlias, false);
            _recv[m] = span;
            if (span != null) _callStack.Add(span);
            return;
        }

        // 非同期メッセージ・生成メッセージなど
        var top = Top(m.ToAlias);
        _recv[m] = top != null ? top : OpenPoint(m.ToAlias);
    }

    // どの区間にも属さないライフラインからの送信には、図の末尾まで続く起点の区間を1本作る
    private void EnsureSendSpan(FlatMessage m)
    {
        var top = Top(m.FromAlias);
        if (top == null)
        {
            top = Open(m.FromAlias, false);
            if (top != null) top.IsRoot = true;
        }
        _send[m] = top;
    }

    private static bool FollowedByActivate(List<FlatEvent> events, int index, string alias)
    {
        for (var i = index + 1; i < events.Count; i++)
        {
            var next = events[i];
            if (next.Kind == FlatKind.Activate)
                return string.Equals(next.LifelineAlias, alias, StringComparison.Ordinal);
            if (next.Kind == FlatKind.OperandBegin || next.Kind == FlatKind.OperandEnd
                || next.Kind == FlatKind.FragmentBegin || next.Kind == FlatKind.FragmentEnd)
                continue;
            return false;
        }
        return false;
    }

    private ActivationSpan Open(string alias, bool isExplicit)
    {
        if (string.IsNullOrEmpty(alias)) return null;
        if (_mode == ActivationMode.None) return null;
        if (_mode == ActivationMode.Explicit && !isExplicit) return null;

        var stack = Stack(alias);
        var all = All(alias);

        var span = new ActivationSpan
        {
            LifelineAlias = alias,
            Index = all.Count,
            Depth = stack.Count,
            IsExplicit = isExplicit,
            Parent = stack.Count > 0 ? stack[stack.Count - 1] : null
        };
        all.Add(span);
        stack.Add(span);
        return span;
    }

    // 幅を持たない一点だけの活性化（非同期の受信側など）
    private ActivationSpan OpenPoint(string alias)
    {
        var span = Open(alias, false);
        if (span != null) Close(alias);
        return span;
    }

    private void Close(string alias)
    {
        if (string.IsNullOrEmpty(alias)) return;
        var stack = Stack(alias);
        if (stack.Count == 0)
        {
            Warnings.Add("'" + alias + "' に対応する activate が無い deactivate を読み飛ばしました。");
            return;
        }
        var span = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        _callStack.Remove(span);
    }

    private void CloseAll(string alias)
    {
        if (string.IsNullOrEmpty(alias)) return;
        var stack = Stack(alias);
        foreach (var span in stack) _callStack.Remove(span);
        stack.Clear();
    }

    // 同期メッセージで自動的に開いた区間を、応答メッセージで閉じる
    private void CloseImplicitCall(string alias)
    {
        for (var i = _callStack.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(_callStack[i].LifelineAlias, alias, StringComparison.Ordinal)) continue;

            var span = _callStack[i];
            _callStack.RemoveAt(i);
            var stack = Stack(alias);
            var at = stack.LastIndexOf(span);
            if (at >= 0) stack.RemoveRange(at, stack.Count - at);
            return;
        }
    }

    private ActivationSpan Top(string alias)
    {
        if (string.IsNullOrEmpty(alias)) return null;
        var stack = Stack(alias);
        return stack.Count > 0 ? stack[stack.Count - 1] : null;
    }

    private List<ActivationSpan> Stack(string alias)
    {
        List<ActivationSpan> stack;
        if (!_open.TryGetValue(alias, out stack))
        {
            stack = new List<ActivationSpan>();
            _open[alias] = stack;
        }
        return stack;
    }

    private List<ActivationSpan> All(string alias)
    {
        List<ActivationSpan> all;
        if (!_spans.TryGetValue(alias, out all))
        {
            all = new List<ActivationSpan>();
            _spans[alias] = all;
        }
        return all;
    }

    public List<string> Dump()
    {
        var lines = new List<string>();
        lines.Add("  活性化区間: " + TotalSpanCount + " 件 (mode=" + _mode + ")");
        foreach (var alias in _spans.Keys.OrderBy(k => k, StringComparer.Ordinal))
            lines.Add("    " + alias + " : " + _spans[alias].Count + " 区間");
        foreach (var w in Warnings) lines.Add("    警告: " + w);
        return lines;
    }
}

// ------------------------------------------------------------
//  書き込み結果
// ------------------------------------------------------------
public class WriteResult
{
    public int LifelinesCreated, LifelinesUpdated, LifelinesRemoved;
    public int ExecutionsCreated, ExecutionsRemoved;
    public int MessagesCreated, MessagesUpdated, MessagesRemoved;
    public int FragmentsCreated, FragmentsUpdated, FragmentsRemoved;
    public int OperandsCreated, OperandsUpdated, OperandsRemoved;
    public int NotesCreated, NotesUpdated, NotesRemoved;
    public int UsesCreated, UsesUpdated, UsesRemoved;
    public int Reordered;

    public List<string> Warnings = new List<string>();
    public List<string> Errors = new List<string>();
    public bool DryRun;

    public bool HasError { get { return Errors.Count > 0; } }

    public int TotalCreated
    {
        get
        {
            return LifelinesCreated + ExecutionsCreated + MessagesCreated
                 + FragmentsCreated + OperandsCreated + NotesCreated + UsesCreated;
        }
    }

    public int TotalUpdated
    {
        get { return LifelinesUpdated + MessagesUpdated + FragmentsUpdated + OperandsUpdated + NotesUpdated + UsesUpdated; }
    }

    public int TotalRemoved
    {
        get
        {
            return LifelinesRemoved + ExecutionsRemoved + MessagesRemoved
                 + FragmentsRemoved + OperandsRemoved + NotesRemoved + UsesRemoved;
        }
    }

    public bool HasChange
    {
        get { return TotalCreated + TotalUpdated + TotalRemoved + Reordered > 0; }
    }

    public void AddWarning(string message) { Warnings.Add(message); }
    public void AddError(string message) { Errors.Add(message); }

    public void Merge(WriteResult other)
    {
        if (other == null) return;
        LifelinesCreated += other.LifelinesCreated;
        LifelinesUpdated += other.LifelinesUpdated;
        LifelinesRemoved += other.LifelinesRemoved;
        ExecutionsCreated += other.ExecutionsCreated;
        ExecutionsRemoved += other.ExecutionsRemoved;
        MessagesCreated += other.MessagesCreated;
        MessagesUpdated += other.MessagesUpdated;
        MessagesRemoved += other.MessagesRemoved;
        FragmentsCreated += other.FragmentsCreated;
        FragmentsUpdated += other.FragmentsUpdated;
        FragmentsRemoved += other.FragmentsRemoved;
        OperandsCreated += other.OperandsCreated;
        OperandsUpdated += other.OperandsUpdated;
        OperandsRemoved += other.OperandsRemoved;
        NotesCreated += other.NotesCreated;
        NotesUpdated += other.NotesUpdated;
        NotesRemoved += other.NotesRemoved;
        UsesCreated += other.UsesCreated;
        UsesUpdated += other.UsesUpdated;
        UsesRemoved += other.UsesRemoved;
        Reordered += other.Reordered;
        Warnings.AddRange(other.Warnings);
        Errors.AddRange(other.Errors);
    }

    public List<string> ToReport()
    {
        var lines = new List<string>();
        if (DryRun) lines.Add("    ※ドライラン（モデルは変更していません）");
        lines.Add("    追加 " + TotalCreated + " / 更新 " + TotalUpdated
                  + " / 削除 " + TotalRemoved + " / 並べ替え " + Reordered);
        lines.Add("      ライフライン +" + LifelinesCreated + " ~" + LifelinesUpdated + " -" + LifelinesRemoved);
        lines.Add("      実行仕様     +" + ExecutionsCreated + " -" + ExecutionsRemoved);
        lines.Add("      メッセージ   +" + MessagesCreated + " ~" + MessagesUpdated + " -" + MessagesRemoved);
        lines.Add("      フラグメント +" + FragmentsCreated + " ~" + FragmentsUpdated + " -" + FragmentsRemoved);
        lines.Add("      操作領域     +" + OperandsCreated + " ~" + OperandsUpdated + " -" + OperandsRemoved);
        lines.Add("      ノート       +" + NotesCreated + " ~" + NotesUpdated + " -" + NotesRemoved);
        lines.Add("      ref          +" + UsesCreated + " ~" + UsesUpdated + " -" + UsesRemoved);
        foreach (var w in Warnings) lines.Add("    警告: " + w);
        foreach (var e in Errors) lines.Add("    エラー: " + e);
        return lines;
    }
}

// ------------------------------------------------------------
//  モデルへの書き込み
//
//  座標は書けない。順序だけを AddNewModelAt / MoveTo で制御する。
//  メッセージはライフラインではなく実行仕様に接続するので、
//  送信元・受信先の両方に実行仕様が必要になる。
// ------------------------------------------------------------
public class SequenceWriter
{
    private readonly MetaMap _map;
    private readonly ImportSettings _s;
    private readonly WriteResult _r = new WriteResult();

    // 別名 → ライフラインのモデル
    private readonly Dictionary<string, IModel> _lifelineModels =
        new Dictionary<string, IModel>(StringComparer.Ordinal);
    // 別名 → 既存索引のライフライン
    private readonly Dictionary<string, ExistingLifeline> _lifelineExisting =
        new Dictionary<string, ExistingLifeline>(StringComparer.Ordinal);

    public SequenceWriter(MetaMap map, ImportSettings settings)
    {
        _map = map;
        _s = settings;
    }

    public WriteResult Apply(PumlFlattener flat, ExistingIndex index, MatchSet set,
                             ActivationResolver activation)
    {
        var interaction = index.Interaction as IModel;
        if (interaction == null)
        {
            _r.AddError("相互作用（Interaction）を特定できないため書き込めません。");
            return _r;
        }

        try
        {
            WriteLifelines(interaction, flat, set);
            WriteExecutions(flat, set, activation);
            WriteMessages(interaction, flat, set, activation);
            WriteFragments(interaction, flat, set);
            WriteNotes(interaction, flat, set);
            WriteUses(interaction, flat, set);
            WriteDestructions(flat, activation);
            RemoveOrphans(set);
        }
        catch (Exception ex)
        {
            _r.AddError(ex.Message);
        }
        return _r;
    }

    // ========================================================
    //  ライフライン
    // ========================================================
    private void WriteLifelines(IModel interaction, PumlFlattener flat, MatchSet set)
    {
        if (!_map.CanWriteLifelines)
        {
            _r.AddWarning("ライフラインのメタモデルを判別できないため、ライフラインは書き込みません。");
            // 既存だけでも対応付けておく（メッセージの接続に要る）
            foreach (var l in flat.Lifelines)
            {
                ExistingLifeline hit;
                if (set.Lifelines.TryGetValue(l.Key, out hit) && hit.Model != null)
                {
                    _lifelineModels[l.Alias] = hit.Model;
                    _lifelineExisting[l.Alias] = hit;
                }
            }
            return;
        }

        var field = _map.InteractionLifelinesField;
        var ordered = new List<IModel>();

        foreach (var l in flat.Lifelines.OrderBy(x => x.Order))
        {
            ExistingLifeline hit;
            IModel model;

            if (set.Lifelines.TryGetValue(l.Key, out hit) && hit.Model != null)
            {
                model = hit.Model;
                _lifelineExisting[l.Alias] = hit;

                if (_s.UpdateLifelineNames && !string.Equals(model.Name, l.Label, StringComparison.Ordinal))
                {
                    model.SetField(_map.LifelineNameField, l.Label);
                    _r.LifelinesUpdated++;
                }
            }
            else
            {
                model = interaction.AddNewModelAt(field, _map.LifelineClass, Clamp(interaction, field, ordered.Count));
                model.SetField(_map.LifelineNameField, l.Label);
                _r.LifelinesCreated++;
            }

            Tag(model, l.Key);
            TagAlias(model, l.Alias);
            _lifelineModels[l.Alias] = model;
            ordered.Add(model);
        }

        Reorder(interaction, field, ordered);
    }

    // ========================================================
    //  実行仕様（同じライフラインの上から n 番目どうしを対応させる）
    // ========================================================
    private void WriteExecutions(PumlFlattener flat, MatchSet set, ActivationResolver activation)
    {
        if (!_map.CanWriteExecutions)
        {
            _r.AddWarning("実行仕様のメタモデルを判別できないため、メッセージを接続できません。");
            return;
        }

        foreach (var alias in activation.Aliases.OrderBy(a => a, StringComparer.Ordinal))
        {
            var spans = activation.SpansOf(alias);
            if (spans.Count == 0) continue;

            IModel lifeline;
            if (!_lifelineModels.TryGetValue(alias, out lifeline) || lifeline == null)
            {
                _r.AddWarning("ライフライン '" + alias + "' が見つからないため実行仕様を作れません。");
                continue;
            }

            ExistingLifeline existing;
            var pool = _lifelineExisting.TryGetValue(alias, out existing)
                     ? new List<IModel>(existing.Executions)
                     : new List<IModel>();

            for (var i = 0; i < spans.Count; i++)
            {
                if (i < pool.Count)
                {
                    spans[i].Model = pool[i];
                    continue;
                }
                spans[i].Model = lifeline.AddNewModel(_map.LifelineExecutionsField, _map.ExecutionClass);
                _r.ExecutionsCreated++;
            }

            // 余った実行仕様
            for (var i = spans.Count; i < pool.Count; i++)
            {
                if (_s.Orphans == OrphanPolicy.Delete)
                {
                    pool[i].Delete();
                    _r.ExecutionsRemoved++;
                }
                else
                {
                    _r.AddWarning("'" + alias + "' の実行仕様が PlantUML 側より 1 件多く残っています。");
                }
            }
        }
    }

    // ========================================================
    //  メッセージ
    // ========================================================
    private void WriteMessages(IModel interaction, PumlFlattener flat, MatchSet set,
                               ActivationResolver activation)
    {
        if (!_map.CanWriteMessages)
        {
            _r.AddWarning("メッセージのメタモデルを判別できないため、メッセージは書き込みません。");
            return;
        }

        var field = _map.InteractionMessagesField;
        var ordered = new List<IModel>();
        // 操作領域 → 含まれるメッセージ
        var byOperand = new Dictionary<string, List<IModel>>(StringComparer.Ordinal);

        foreach (var m in flat.Messages.OrderBy(x => x.Order))
        {
            ExistingMessage hit;
            IModel model;

            if (set.Messages.TryGetValue(m.Key, out hit) && hit.Model != null)
            {
                model = hit.Model as IModel;
                if (!string.Equals(hit.Text, m.Text, StringComparison.Ordinal)
                    || !string.Equals(ExistingIndex.KindEnumName(hit.Kind), m.Kind.ToString(), StringComparison.Ordinal))
                    _r.MessagesUpdated++;
            }
            else
            {
                model = interaction.AddNewModelAt(field, _map.MessageClass, Clamp(interaction, field, ordered.Count));
                _r.MessagesCreated++;
            }

            if (model == null) continue;

            if (!string.IsNullOrEmpty(_map.MessageNameField))
                model.SetField(_map.MessageNameField, m.Text);
            if (!string.IsNullOrEmpty(_map.MessageKindField))
                model.SetField(_map.MessageKindField, _map.KindValueOf(m.Kind));

            Connect(model, m, activation);
            Tag(model, m.Key);

            ordered.Add(model);
            if (m.Container != null)
            {
                List<IModel> list;
                if (!byOperand.TryGetValue(m.Container.Key, out list))
                {
                    list = new List<IModel>();
                    byOperand[m.Container.Key] = list;
                }
                list.Add(model);
            }
        }

        Reorder(interaction, field, ordered);
        _operandMessages = byOperand;
    }

    private Dictionary<string, List<IModel>> _operandMessages = new Dictionary<string, List<IModel>>(StringComparer.Ordinal);

    // メッセージは実行仕様に接続する
    private void Connect(IModel model, FlatMessage m, ActivationResolver activation)
    {
        var send = activation.SendSpanOf(m);
        var receive = activation.ReceiveSpanOf(m);

        if (send != null && send.Model != null)
            model.SetField(_map.MessageSendPortField, send.Model);
        else
            _r.AddWarning("送信元の実行仕様が無いため接続できません: " + m.SenderLabel + " → " + m.ReceiverLabel);

        if (receive != null && receive.Model != null)
            model.SetField(_map.MessageReceivePortField, receive.Model);
        else
            _r.AddWarning("受信先の実行仕様が無いため接続できません: " + m.SenderLabel + " → " + m.ReceiverLabel);
    }

    // ========================================================
    //  複合フラグメントと操作領域
    // ========================================================
    private void WriteFragments(IModel interaction, PumlFlattener flat, MatchSet set)
    {
        if (!_s.ImportFragments) return;
        if (!_map.CanWriteFragments)
        {
            if (flat.Fragments.Count > 0)
                _r.AddWarning("複合フラグメントのメタモデルを判別できないため、フラグメントは書き込みません。");
            return;
        }

        var field = _map.InteractionFragmentsField;
        var ordered = new List<IModel>();

        foreach (var f in flat.Fragments.OrderBy(x => x.Order))
        {
            ExistingFragment hit;
            IModel model;
            var existingOperands = new List<IModel>();

            if (set.Fragments.TryGetValue(f.Key, out hit) && hit.Model != null)
            {
                model = hit.Model;
                existingOperands.AddRange(hit.Operands.Select(o => o.Model).Where(o => o != null));
                if (!string.Equals(hit.Operator, f.Operator, StringComparison.OrdinalIgnoreCase))
                    _r.FragmentsUpdated++;
            }
            else
            {
                model = interaction.AddNewModelAt(field, _map.FragmentClass, Clamp(interaction, field, ordered.Count));
                _r.FragmentsCreated++;
            }
            if (model == null) continue;

            if (!string.IsNullOrEmpty(_map.FragmentTextField))
                model.SetField(_map.FragmentTextField, HeaderTextOf(f));
            Tag(model, f.Key);
            ordered.Add(model);

            WriteOperands(model, f, existingOperands);
        }

        Reorder(interaction, field, ordered);
    }

    private void WriteOperands(IModel fragment, FlatFragment f, List<IModel> existing)
    {
        var field = _map.FragmentOperandsField;
        var ordered = new List<IModel>();

        for (var i = 0; i < f.Operands.Count; i++)
        {
            var o = f.Operands[i];
            IModel model;

            if (i < existing.Count)
            {
                model = existing[i];
                var current = string.IsNullOrEmpty(_map.OperandGuardField)
                            ? "" : (model.GetFieldString(_map.OperandGuardField) ?? "");
                if (!string.Equals(current, o.Guard, StringComparison.Ordinal)) _r.OperandsUpdated++;
            }
            else
            {
                model = fragment.AddNewModelAt(field, _map.OperandClass, Clamp(fragment, field, ordered.Count));
                _r.OperandsCreated++;
            }
            if (model == null) continue;

            if (!string.IsNullOrEmpty(_map.OperandGuardField))
                model.SetField(_map.OperandGuardField, o.Guard);
            Tag(model, o.Key);

            // 操作領域に属するメッセージを結び付ける
            List<IModel> messages;
            if (!string.IsNullOrEmpty(_map.OperandMessagesField)
                && _operandMessages.TryGetValue(o.Key, out messages))
                TrySetField(model, _map.OperandMessagesField, messages, "操作領域のメッセージ");

            ordered.Add(model);
        }

        // 余った操作領域
        for (var i = f.Operands.Count; i < existing.Count; i++)
        {
            if (_s.Orphans == OrphanPolicy.Delete)
            {
                existing[i].Delete();
                _r.OperandsRemoved++;
            }
            else
            {
                _r.AddWarning("操作領域が PlantUML 側より多く残っています（フラグメント '" + f.Operator + "'）。");
            }
        }

        Reorder(fragment, field, ordered);
    }

    private static string HeaderTextOf(FlatFragment f)
    {
        var guard = f.Operands.Count > 0 ? f.Operands[0].Guard : "";
        if (f.Operator == "group") return f.Text.Length > 0 ? f.Text : "group";
        return guard.Length > 0 ? f.Operator + " " + guard : f.Operator;
    }

    // ========================================================
    //  ノート
    // ========================================================
    private void WriteNotes(IModel interaction, PumlFlattener flat, MatchSet set)
    {
        if (!_s.ImportNotes) return;
        if (!_map.CanWriteNotes)
        {
            if (flat.Notes.Count > 0)
                _r.AddWarning("ノートのメタモデルを判別できないため、ノートは書き込みません。");
            return;
        }

        var field = _map.InteractionNotesField;
        var ordered = new List<IModel>();

        foreach (var n in flat.Notes.OrderBy(x => x.Order))
        {
            ExistingNote hit;
            IModel model;

            if (set.Notes.TryGetValue(n.Key, out hit) && hit.Model != null)
            {
                model = hit.Model;
                if (!string.Equals(hit.Text, n.Text, StringComparison.Ordinal)) _r.NotesUpdated++;
            }
            else
            {
                model = interaction.AddNewModelAt(field, _map.NoteClass, Clamp(interaction, field, ordered.Count));
                _r.NotesCreated++;
            }
            if (model == null) continue;

            if (!string.IsNullOrEmpty(_map.NoteTextField)) model.SetField(_map.NoteTextField, n.Text);
            if (!string.IsNullOrEmpty(_map.NoteTargetsField))
                TrySetField(model, _map.NoteTargetsField, TargetModels(n.TargetLabels, flat), "ノートの対象");

            Tag(model, n.Key);
            ordered.Add(model);
        }

        Reorder(interaction, field, ordered);
    }

    // ========================================================
    //  相互作用の利用
    // ========================================================
    private void WriteUses(IModel interaction, PumlFlattener flat, MatchSet set)
    {
        if (!_s.ImportUses) return;
        if (!_map.CanWriteUses)
        {
            if (flat.Uses.Count > 0)
                _r.AddWarning("相互作用の利用のメタモデルを判別できないため、ref は書き込みません。");
            return;
        }

        var field = _map.InteractionUsesField;
        var ordered = new List<IModel>();

        foreach (var u in flat.Uses.OrderBy(x => x.Order))
        {
            ExistingUse hit;
            IModel model;

            if (set.Uses.TryGetValue(u.Key, out hit) && hit.Model != null)
            {
                model = hit.Model;
                if (!string.Equals(hit.Text, u.Text, StringComparison.Ordinal)) _r.UsesUpdated++;
            }
            else
            {
                model = interaction.AddNewModelAt(field, _map.InteractionUseClass, Clamp(interaction, field, ordered.Count));
                _r.UsesCreated++;
            }
            if (model == null) continue;

            if (!string.IsNullOrEmpty(_map.UseNameField)) model.SetField(_map.UseNameField, u.Text);
            if (!string.IsNullOrEmpty(_map.UseTargetsField))
                TrySetField(model, _map.UseTargetsField, TargetModels(u.TargetLabels, flat), "ref の対象");

            Tag(model, u.Key);
            ordered.Add(model);
        }

        Reorder(interaction, field, ordered);
    }

    // ========================================================
    //  破棄
    // ========================================================
    private void WriteDestructions(PumlFlattener flat, ActivationResolver activation)
    {
        if (!_map.CanWriteDestructions) return;

        var destroyed = flat.Events
            .Where(e => e.Kind == FlatKind.Destroy)
            .Select(e => e.LifelineAlias)
            .Concat(flat.Messages.Where(m => m.Kind == PumlKind.Destroy).Select(m => m.ToAlias))
            .Where(a => !string.IsNullOrEmpty(a))
            .Distinct(StringComparer.Ordinal);

        foreach (var alias in destroyed)
        {
            IModel lifeline;
            if (!_lifelineModels.TryGetValue(alias, out lifeline) || lifeline == null) continue;

            int count;
            try { count = lifeline.Count(_map.LifelineDestructionField); }
            catch (Exception) { continue; }
            if (count > 0) continue;

            try { lifeline.AddNewModel(_map.LifelineDestructionField, _map.DestructionClass); }
            catch (Exception ex) { _r.AddWarning("破棄を作れませんでした（" + alias + "）: " + ex.Message); }
        }
    }

    // ========================================================
    //  孤児の削除
    // ========================================================
    private void RemoveOrphans(MatchSet set)
    {
        if (_s.Orphans != OrphanPolicy.Delete) return;

        foreach (var e in set.OrphanUses) { Kill(e.Model); _r.UsesRemoved++; }
        foreach (var e in set.OrphanNotes) { Kill(e.Model); _r.NotesRemoved++; }
        foreach (var e in set.OrphanFragments) { Kill(e.Model); _r.FragmentsRemoved++; }
        foreach (var e in set.OrphanMessages) { Kill(e.Model as IModel); _r.MessagesRemoved++; }
        foreach (var e in set.OrphanLifelines) { Kill(e.Model); _r.LifelinesRemoved++; }
    }

    private void Kill(IModel model)
    {
        if (model == null) return;
        try { model.Delete(); }
        catch (Exception ex) { _r.AddWarning("削除できませんでした: " + ex.Message); }
    }

    // ========================================================
    //  小道具
    // ========================================================

    // 図の順序はモデルの並び順で決まる。ずれている分だけ MoveTo で直す
    private void Reorder(IModel owner, string field, List<IModel> desired)
    {
        if (owner == null || string.IsNullOrEmpty(field) || desired.Count == 0) return;

        try
        {
            for (var i = 0; i < desired.Count; i++)
            {
                var current = owner.GetFieldValues(field).ToList();
                var at = current.FindIndex(m => m != null && m.Id == desired[i].Id);
                if (at < 0 || at == i) continue;
                desired[i].MoveTo(owner, field, i);
                _r.Reordered++;
            }
        }
        catch (Exception ex)
        {
            _r.AddWarning("並べ替えできませんでした（" + field + "）: " + ex.Message);
        }
    }

    private static int Clamp(IModel owner, string field, int index)
    {
        try
        {
            var count = owner.Count(field);
            return index < 0 ? 0 : (index > count ? count : index);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private List<IModel> TargetModels(List<string> labels, PumlFlattener flat)
    {
        var result = new List<IModel>();
        foreach (var label in labels)
        {
            var lifeline = flat.Lifelines.FirstOrDefault(l => string.Equals(l.Label, label, StringComparison.Ordinal));
            if (lifeline == null) continue;

            IModel model;
            if (_lifelineModels.TryGetValue(lifeline.Alias, out model) && model != null) result.Add(model);
        }
        return result;
    }

    private void TrySetField(IModel model, string field, object value, string what)
    {
        try { model.SetField(field, value); }
        catch (Exception ex) { _r.AddWarning(what + "を設定できませんでした（" + field + "）: " + ex.Message); }
    }

    private void Tag(IModel model, string key)
    {
        if (!_s.WriteTags || model == null || string.IsNullOrEmpty(key)) return;
        try { model.SetTag(_s.TagName, key); }
        catch (Exception) { /* タグを付けられなくても取り込み自体は続ける */ }
    }

    private void TagAlias(IModel model, string alias)
    {
        if (!_s.WriteTags || model == null || string.IsNullOrEmpty(alias)) return;
        try { model.SetTag(_s.AliasTagName, alias); }
        catch (Exception) { }
    }
}

// ============================================================
//  Part 5 / 実行層 : メタモデル調査と取り込みの実行
// ============================================================

// ------------------------------------------------------------
//  メタモデル調査
//
//  シーケンス図のクラス名・フィールド名は API リファレンスに載っておらず、
//  プロファイルによって変わる。自動判別が外れたときは、ここの出力を見て
//  ImportSettings.MetaOverrides に書き写す。
// ------------------------------------------------------------
public class MetaProbe
{
    public const string Category = "PlantUmlImport";

    public static void Run(IApplication app, ISequenceDiagram diagram)
    {
        var w = new Action<string>(text => app.Output.WriteLine(Category, text));

        w("=== メタモデル調査 ===");
        w("EditorType         : " + diagram.EditorType);
        w("ViewDefinitionName : " + diagram.ViewDefinitionName);
        w("");

        DumpModel(w, "エディタの対象モデル", diagram.Model);
        DumpModel(w, "相互作用", MetaMap.ResolveInteraction(diagram) as IModel);
        DumpModel(w, "ライフライン", FirstModel(diagram.Lifelines));
        DumpModel(w, "実行仕様", FirstModel(diagram.ExecutionSpecifications));
        DumpModel(w, "メッセージ", FirstModel(diagram.Messages));
        DumpModel(w, "複合フラグメント", FirstModel(diagram.Fragments));
        DumpModel(w, "操作領域", FirstOperand(diagram));
        DumpModel(w, "相互作用の利用", FirstModel(diagram.InteractionUses));
        DumpModel(w, "ノート", FirstModel(diagram.Notes));
        DumpModel(w, "破棄", FirstModel(diagram.Destructions));

        DumpMessageKinds(w, diagram);

        w("");
        var map = MetaMap.Detect(diagram);
        foreach (var line in map.Report()) w(line);
        w("");
        w("=== 調査終了 ===");
    }

    private static IModel FirstModel(System.Collections.IEnumerable shapes)
    {
        if (shapes == null) return null;
        foreach (var shape in shapes)
        {
            var model = MetaMap.ModelOf(shape);
            if (model != null) return model;
        }
        return null;
    }

    private static IModel FirstOperand(ISequenceDiagram d)
    {
        foreach (var f in d.Fragments.Cast<IFragmentShape>())
            foreach (var o in f.Operands.Cast<IOperandShape>())
            {
                var model = MetaMap.ModelOf(o);
                if (model != null) return model;
            }
        return null;
    }

    // Part 7 のクラス図調査からも使うため public
    public static void DumpModel(Action<string> w, string title, IModel m)
    {
        w("---- " + title + " ----");
        if (m == null)
        {
            w("  (見本なし)");
            w("");
            return;
        }

        w("  ClassName  : " + m.ClassName);
        w("  Name       : " + m.Name);

        var cls = m.Metaclass;
        if (cls != null)
        {
            w("  FullName   : " + cls.FullName);
            w("  IsAbstract : " + cls.IsAbstract);
            var supers = cls.GetAllSuperClasses().Cast<IClass>().Select(c => c.Name).ToList();
            w("  SuperClass : " + (supers.Count > 0 ? string.Join(", ", supers.ToArray()) : "(なし)"));
        }

        try
        {
            var ownerField = m.GetOwnerField();
            w("  OwnerField : " + (ownerField != null ? ownerField.Name : "(不明)"));
        }
        catch (Exception) { w("  OwnerField : (取得できません)"); }

        w("  Owner      : " + (m.Owner != null ? m.Owner.ClassName + " / " + m.Owner.Name : "(なし)"));

        if (cls != null)
        {
            w("  Fields:");
            foreach (var f in cls.GetFields().Cast<IField>())
            {
                var sb = new StringBuilder();
                sb.Append("    ").Append(Pad(f.Name, 30));
                sb.Append(" kind=").Append(f.IsEmbedded ? "所有" : (f.IsReference ? "参照" : "値  "));
                sb.Append(" type=").Append(Pad(f.Type, 24));
                sb.Append(" mult=").Append(f.LowerBound).Append("..")
                  .Append(f.UpperBound < 0 ? "*" : f.UpperBound.ToString());

                if (!f.IsEmbedded && !f.IsReference)
                {
                    string value = null;
                    try { value = m.GetFieldString(f.Name); }
                    catch (Exception) { }
                    if (!string.IsNullOrEmpty(value)) sb.Append(" value='").Append(Shorten(value)).Append("'");
                }
                w(sb.ToString());
            }
        }
        w("");
    }

    private static void DumpMessageKinds(Action<string> w, ISequenceDiagram d)
    {
        w("---- メッセージ種別の実値 ----");
        var any = false;
        foreach (var shape in d.Messages.Cast<IMessageShape>())
        {
            var model = shape.Model as IMessage;
            if (model == null) continue;
            any = true;
            w("  Kind='" + model.Kind + "'"
              + " IsSynchronous=" + model.IsSynchronous
              + " IsAsynchronous=" + model.IsAsynchronous
              + " IsReply=" + model.IsReply
              + " : " + PlantUmlText.Normalize(shape.Text));
        }
        if (!any) w("  (見本なし)");
        w("");
    }

    private static string Pad(string s, int width)
    {
        var t = s ?? "";
        return t.Length >= width ? t : t + new string(' ', width - t.Length);
    }

    private static string Shorten(string s)
    {
        var t = PlantUmlText.Inline(PlantUmlText.Normalize(s));
        return t.Length <= 40 ? t : t.Substring(0, 40) + "…";
    }
}

// ------------------------------------------------------------
//  1 ファイル分の取り込み計画
// ------------------------------------------------------------
public class ImportJob
{
    public string SourcePath = "";
    public DiagramEntry Target;
    public PumlDiagram Puml;
    public PumlFlattener Flat;
    public ExistingIndex Index;
    public MatchSet Set;
    public ActivationResolver Activation;
    public MetaMap Map;
    public bool Ready;
    public string SkipReason = "";

    public string Label
    {
        get
        {
            var name = Puml != null ? Puml.Name : System.IO.Path.GetFileNameWithoutExtension(SourcePath);
            return name + "  <- " + System.IO.Path.GetFileName(SourcePath);
        }
    }
}

// ------------------------------------------------------------
//  取り込みの実行
// ------------------------------------------------------------
public class ImportRunner
{
    public const string Category = "PlantUmlImport";

    // ==================== ファイル 1 件 ====================

    public static void ImportFile(IApplication app, IContext context, ImportSettings settings)
    {
        settings = settings ?? new ImportSettings();
        var ui = app.Window.UI;

        var path = ui.ShowOpenFileDialog(
            "取り込む PlantUML ファイルを選択してください",
            "PlantUML (*.puml)|*.puml|テキスト (*.txt)|*.txt|すべてのファイル (*.*)|*.*");
        if (string.IsNullOrEmpty(path)) return;

        Run(app, context, settings, new List<string> { path }, "ファイル取り込み");
    }

    // ==================== フォルダ一括 ====================

    public static void ImportFolder(IApplication app, IContext context, ImportSettings settings)
    {
        settings = settings ?? new ImportSettings();
        var ui = app.Window.UI;

        var folder = ui.ShowSelectFolderDialog("取り込む PlantUML ファイルのあるフォルダを選択してください");
        if (string.IsNullOrEmpty(folder)) return;

        List<string> files;
        try
        {
            files = System.IO.Directory
                .GetFiles(folder, "*.puml", System.IO.SearchOption.AllDirectories)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            ui.ShowInformationDialog("フォルダを読めませんでした。\n\n" + ex.Message, Category);
            return;
        }

        if (files.Count == 0)
        {
            ui.ShowInformationDialog("「" + folder + "」配下に .puml ファイルが見つかりませんでした。", Category);
            return;
        }

        Run(app, context, settings, files, "フォルダ一括取り込み");
    }

    // ==================== 本体 ====================
    //
    //  必ずドライランで差分を出してから、確認ダイアログを経て書き込む。
    //
    public static void Run(IApplication app, IContext context, ImportSettings settings,
                           List<string> files, string title)
    {
        var ui = app.Window.UI;

        // 未表示エディタの詳細も取得できるようにする（バッチでは必須）
        context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

        ShowPane(app);
        app.Output.WriteLine(Category, "=== PlantUML " + title + " ===");
        app.Output.WriteLine(Category, "対象ファイル " + files.Count + " 件");
        app.Output.WriteLine(Category, "");

        var root = ExportRunner.ResolveRoot(app);
        if (root == null)
        {
            ui.ShowInformationDialog("プロジェクトが開かれていません。", Category);
            return;
        }

        var skipCount = 0;
        var diagrams = ExportRunner.Collect(root, false, ref skipCount);
        if (diagrams.Count == 0)
        {
            ui.ShowInformationDialog(
                "「" + root.Name + "」配下にシーケンス図が見つかりませんでした。\n"
                + "取り込み先の図をあらかじめ作成しておいてください。", Category);
            return;
        }

        // ---- 見本を集めてメタモデルを判別する ----
        var map = BuildMetaMap(diagrams, settings);
        foreach (var line in map.Report()) app.Output.WriteLine(Category, line);
        app.Output.WriteLine(Category, "");

        // ---- ドライラン ----
        var jobs = new List<ImportJob>();
        foreach (var file in files)
        {
            var job = Prepare(app, file, diagrams, settings, map);
            jobs.Add(job);
            Report(app, job);
        }

        var ready = jobs.Where(j => j.Ready).ToList();
        var changing = ready.Where(j => j.Set.Plan.HasChanges).ToList();

        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "=== ドライラン完了 : 対象 " + ready.Count
                             + " 件 / 変更あり " + changing.Count
                             + " 件 / 取り込めない " + (jobs.Count - ready.Count) + " 件 ===");

        if (ready.Count == 0)
        {
            ui.ShowInformationDialog(
                "取り込める図がありませんでした。出力ウィンドウの詳細を確認してください。", Category);
            return;
        }
        if (changing.Count == 0)
        {
            ui.ShowInformationDialog(
                "差分はありませんでした。モデルは変更していません。\n\n"
                + "対象: " + ready.Count + " 件", Category);
            return;
        }

        // ---- 確認 ----
        var message = "以下の内容でモデルを更新します。\n\n"
                    + "対象の図: " + changing.Count + " 件\n"
                    + "追加: " + changing.Sum(j => j.Set.Plan.Count2(ChangeKind.Add)) + " 件\n"
                    + "更新: " + changing.Sum(j => j.Set.Plan.Count2(ChangeKind.Update)) + " 件\n"
                    + "削除: " + changing.Sum(j => j.Set.Plan.Count2(ChangeKind.Remove)) + " 件\n\n"
                    + (settings.Orphans == OrphanPolicy.Delete
                        ? "※ PlantUML 側に無い要素は削除されます。\n\n" : "")
                    + "続行しますか？（Ctrl+Z で元に戻せます）";
        if (!ui.ShowConfirmDialog(message, Category))
        {
            app.Output.WriteLine(Category, "取り込みをキャンセルしました。モデルは変更していません。");
            return;
        }

        // ---- 適用 ----
        settings.DryRun = false;
        var total = new WriteResult();
        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "=== 適用 ===");

        foreach (var job in changing)
        {
            try
            {
                var writer = new SequenceWriter(job.Map, settings);
                var result = writer.Apply(job.Flat, job.Index, job.Set, job.Activation);
                total.Merge(result);

                app.Output.WriteLine(Category, (result.HasError ? "[error] " : "[ok]    ") + job.Label);
                foreach (var line in result.ToReport()) app.Output.WriteLine(Category, line);
            }
            catch (Exception ex)
            {
                total.AddError(job.Label + " : " + ex.Message);
                app.Output.WriteLine(Category, "[error] " + job.Label + " : " + ex.Message);
            }
        }

        app.Output.WriteLine(Category, "");
        app.Output.WriteLine(Category, "=== 完了 ===");
        foreach (var line in total.ToReport()) app.Output.WriteLine(Category, line);

        ui.ShowInformationDialog(
            "PlantUML の取り込みが完了しました。\n\n"
            + "追加: " + total.TotalCreated + " 件\n"
            + "更新: " + total.TotalUpdated + " 件\n"
            + "削除: " + total.TotalRemoved + " 件\n"
            + "並べ替え: " + total.Reordered + " 件\n"
            + (total.HasError ? "\nエラーが発生しました。出力ウィンドウを確認してください。" : "")
            + "\n取り消したい場合は Ctrl+Z で元に戻せます。", Category);
    }

    // ==================== 準備（ドライラン） ====================

    private static ImportJob Prepare(IApplication app, string path,
                                     List<DiagramEntry> diagrams, ImportSettings settings, MetaMap map)
    {
        var job = new ImportJob { SourcePath = path, Map = map };

        string text;
        try
        {
            text = System.IO.File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            job.SkipReason = "ファイルを読めませんでした: " + ex.Message;
            return job;
        }

        var fallbackName = System.IO.Path.GetFileNameWithoutExtension(path);
        job.Puml = new PlantUmlSequenceParser().Parse(text, fallbackName, path);

        // ---- 取り込み先の図を探す ----
        var hits = diagrams
            .Where(d => string.Equals(d.Name, job.Puml.Name, StringComparison.Ordinal))
            .ToList();

        if (hits.Count == 0)
        {
            job.SkipReason = "図名「" + job.Puml.Name + "」に一致するシーケンス図がプロジェクトにありません。"
                           + (settings.Missing == MissingPolicy.Create
                              ? "（新規作成は未対応です）" : "");
            return job;
        }
        if (hits.Count > 1 && settings.Ambiguous == AmbiguousPolicy.Error)
        {
            job.SkipReason = "図名「" + job.Puml.Name + "」に一致するシーケンス図が "
                           + hits.Count + " 件あります（"
                           + string.Join(" / ", hits.Select(h => h.OwnerPath).ToArray()) + "）。";
            return job;
        }
        job.Target = hits[0];

        // ---- 突き合わせ ----
        job.Flat = new PumlFlattener();
        job.Flat.Run(job.Puml);

        job.Activation = new ActivationResolver(ActivationMode.Auto);
        job.Activation.Resolve(job.Flat.Events);

        job.Index = ExistingIndex.Build(job.Target.Diagram, settings);
        job.Set = ImportMatcher.Build(job.Flat, job.Index, settings, map);
        job.Ready = true;
        return job;
    }

    private static void Report(IApplication app, ImportJob job)
    {
        if (!job.Ready)
        {
            app.Output.WriteLine(Category, "[skip]  " + job.Label);
            app.Output.WriteLine(Category, "    " + job.SkipReason);
            DumpParseWarnings(app, job);
            return;
        }

        app.Output.WriteLine(Category, "図: " + job.Target.Name + "  (更新)  <- "
                             + System.IO.Path.GetFileName(job.SourcePath));
        app.Output.WriteLine(Category, "    モデルパス: " + job.Target.OwnerPath);

        foreach (var line in job.Set.Plan.Dump(true)) app.Output.WriteLine(Category, line);
        foreach (var line in job.Activation.Dump()) app.Output.WriteLine(Category, line);
        DumpParseWarnings(app, job);
        app.Output.WriteLine(Category, "");
    }

    // 読み飛ばした行は黙って捨てず、必ず行番号付きで残す
    private static void DumpParseWarnings(IApplication app, ImportJob job)
    {
        if (job.Puml == null) return;
        foreach (var w in job.Puml.Warnings)
            app.Output.WriteLine(Category, "    " + (w.Level == "info" ? "情報: " : "警告: ") + w.ToString());
    }

    // ==================== メタモデルの見本集め ====================
    //
    //  1 枚に全要素が揃っていないことが多いので、テンプレート図と
    //  プロジェクト内の他の図から見本を寄せ集める。
    //
    private static MetaMap BuildMetaMap(List<DiagramEntry> diagrams, ImportSettings settings)
    {
        var ordered = new List<DiagramEntry>();

        if (!string.IsNullOrEmpty(settings.TemplateDiagramName))
            ordered.AddRange(diagrams.Where(d =>
                string.Equals(d.Name, settings.TemplateDiagramName, StringComparison.Ordinal)));

        ordered.AddRange(diagrams
            .Where(d => !ordered.Contains(d))
            .OrderByDescending(d => d.Diagram.Messages.Cast<IMessageShape>().Count()));

        MetaMap map = null;
        foreach (var entry in ordered)
        {
            if (map == null) map = MetaMap.Detect(entry.Diagram);
            else map.Merge(entry.Diagram);

            if (map.CanWriteLifelines && map.CanWriteMessages && map.CanWriteFragments
                && map.CanWriteNotes && map.CanWriteUses) break;
        }

        if (map == null)
        {
            map = new MetaMap();
            map.Diagnostics.Add("見本になるシーケンス図がありません。");
            map.Finish();
        }

        if (settings.MetaOverrides.Count > 0) map.ApplyOverrides(settings.MetaOverrides);
        return map;
    }

    private static void ShowPane(IApplication app)
    {
        app.Output.Clear(Category);
        app.Window.IsInformationPaneVisible = true;
        app.Window.ActiveInfoWindow = "Output";
        app.Window.CurrentOutputCategory = Category;
    }
}

// ============================================================
//  コマンドハンドラ（manifest.json の execFunc と名前を一致させる）
// ============================================================

public void ExportCurrentDiagram(ICommandContext context, ICommandParams commandParams)
{
    try
    {
        var settings = new ExportSettings();

        // クラス図（EditorType = ERDiagram / TreeDiagram）ならクラス図出力に回す。
        // それ以外は従来どおりシーケンス図として扱う（対象外の案内も従来のまま）
        if (ClassExportRunner.IsClassDiagramEditor(context.App.Workspace.CurrentEditor))
            ClassExportRunner.ExportCurrent(context.App, new ClassPlantUmlOptions(), settings);
        else
            ExportRunner.ExportCurrent(context.App, new PlantUmlOptions(), settings);
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(ExportRunner.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "PlantUML 出力に失敗しました。\n\n" + ex.Message, ExportRunner.Category);
    }
}

public void ExportAllDiagrams(ICommandContext context, ICommandParams commandParams)
{
    try
    {
        var settings = new ExportSettings();
        ExportRunner.ExportAll(context.App, context, new PlantUmlOptions(), settings);

        // クラス図が 1 枚でもあれば続けて出力する。
        // 1 枚も無いプロジェクトでは従来と同じ操作感のまま何も起きない
        var root = ExportRunner.ResolveRoot(context.App);
        if (root == null) return;

        var skipCount = 0;
        if (ClassExportRunner.Collect(root, settings.SkipEmptyDiagram, ref skipCount).Count == 0) return;

        ClassExportRunner.ExportAll(context.App, context, new ClassPlantUmlOptions(), settings, null, false);
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(ExportRunner.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "PlantUML 出力に失敗しました。\n\n" + ex.Message, ExportRunner.Category);
    }
}

// ============================================================
//  Part 6 / 取り込み側のコマンドハンドラ
//  （manifest.json の execFunc と名前を一致させる）
// ============================================================

// 取り込みの既定設定。挙動を変えたいときはここを直す
private ImportSettings NewImportSettings()
{
    return new ImportSettings
    {
        DryRun = true,                       // 必ずドライラン → 確認ダイアログ → 適用
        Orphans = OrphanPolicy.Keep,         // PlantUML 側に無い要素は残して警告する
        Missing = MissingPolicy.Skip,        // 対応する図が無ければスキップ
        Ambiguous = AmbiguousPolicy.Error,   // 同名の図が複数あればエラー
        TemplateDiagramName = "",            // 要素が一通り揃った見本の図名（任意）
        UpdateLifelineNames = true,
        ImportFragments = true,
        ImportNotes = true,
        ImportUses = true,
    };
}

public void ImportFromFile(ICommandContext context, ICommandParams commandParams)
{
    try
    {
        ImportRunner.ImportFile(context.App, context, NewImportSettings());
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(ImportRunner.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "PlantUML の取り込みに失敗しました。\n\n" + ex.Message, ImportRunner.Category);
    }
}

public void ImportFromFolder(ICommandContext context, ICommandParams commandParams)
{
    try
    {
        ImportRunner.ImportFolder(context.App, context, NewImportSettings());
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(ImportRunner.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "PlantUML の取り込みに失敗しました。\n\n" + ex.Message, ImportRunner.Category);
    }
}

public void ProbeClassDiagram(ICommandContext context, ICommandParams commandParams)
{
    try
    {
        var app = context.App;
        var editor = app.Workspace.CurrentEditor;
        var diagram = editor as IDiagram;
        if (diagram == null)
        {
            app.Window.UI.ShowInformationDialog(
                "クラス図を開いた状態で実行してください。（EditorType = "
                + (editor != null ? editor.EditorType : "エディタなし") + "）", ClassProbe.Category);
            return;
        }

        app.Output.Clear(ClassProbe.Category);
        app.Window.IsInformationPaneVisible = true;
        app.Window.ActiveInfoWindow = "Output";
        app.Window.CurrentOutputCategory = ClassProbe.Category;

        ClassProbe.Run(app, diagram);
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(ClassProbe.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "クラス図調査に失敗しました。\n\n" + ex.Message, ClassProbe.Category);
    }
}

public void ProbeMetamodel(ICommandContext context, ICommandParams commandParams)
{
    try
    {
        var app = context.App;
        var diagram = app.Workspace.CurrentEditor as ISequenceDiagram;
        if (diagram == null)
        {
            app.Window.UI.ShowInformationDialog(
                "シーケンス図を開いた状態で実行してください。", MetaProbe.Category);
            return;
        }

        app.Output.Clear(MetaProbe.Category);
        app.Window.IsInformationPaneVisible = true;
        app.Window.ActiveInfoWindow = "Output";
        app.Window.CurrentOutputCategory = MetaProbe.Category;

        MetaProbe.Run(app, diagram);
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(MetaProbe.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "メタモデル調査に失敗しました。\n\n" + ex.Message, MetaProbe.Category);
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
        { "public", "+" }, { "Public", "+" }, { "公開", "+" }, { "+", "+" },
        { "private", "-" }, { "Private", "-" }, { "非公開", "-" }, { "-", "-" },
        { "protected", "#" }, { "Protected", "#" }, { "限定公開", "#" }, { "#", "#" },
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
            if (_o.EmitMembers) CollectMembers(info);
            if (_o.EmitPackages) info.PackagePath = PackagePathOf(model);

            Nodes.Add(info);
            _byModelId[model.Id] = info;
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
    private static string TextOf(IModel m, List<string> candidates)
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

    private static bool BoolField(IModel m, List<string> candidates)
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
                        Label = _o.EmitRoleNames ? PlantUmlText.Inline(f.Name) : "",
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

        if (!string.IsNullOrEmpty(f.Name) && _unknownLink.Add(f.Name))
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
        var groups = new List<string>();
        var byPackage = new Dictionary<string, List<ClassNodeInfo>>(StringComparer.Ordinal);

        foreach (var info in _c.Nodes)
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

        var hasBody = info.Attributes.Count > 0 || info.Operations.Count > 0;
        if (!hasBody)
        {
            LineAt(depth, head.ToString());
            return;
        }

        LineAt(depth, head.ToString() + " {");
        foreach (var attribute in info.Attributes) LineAt(depth + 1, attribute);
        if (info.Attributes.Count > 0 && info.Operations.Count > 0) LineAt(depth + 1, "--");
        foreach (var operation in info.Operations) LineAt(depth + 1, operation);
        LineAt(depth, "}");
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
        options = options ?? new ClassPlantUmlOptions();
        settings = settings ?? new ExportSettings();

        var ui = app.Window.UI;
        context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;

        var root = ExportRunner.ResolveRoot(app);
        if (root == null)
        {
            if (!quiet) ui.ShowInformationDialog("プロジェクトが開かれていません。", Category);
            return 0;
        }

        var skipCount = 0;
        var targets = Collect(root, settings.SkipEmptyDiagram, ref skipCount);

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
            var owner = PlantUmlText.SafeFileName((entry.OwnerPath ?? "").Replace('/', '-').Replace('\\', '-'));
            var name = PlantUmlText.SafeFileName(entry.Name);
            if (name.Length == 0) name = "class";

            var baseName = owner.Length == 0 ? name : owner + "__" + name;
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
        app.Output.Clear(Category);
        app.Window.IsInformationPaneVisible = true;
        app.Window.ActiveInfoWindow = "Output";
        app.Window.CurrentOutputCategory = Category;
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
