// ============================================================
//  Next Design エクステンション : PlantUML 連携（出力 / 取り込み）
//  エントリポイント（C# スクリプト / Next Design V3.x）
//
//  ★ このファイルは tools/build_main.py が src/*.cs をファイル名順に連結して生成する。
//     直接編集しない。編集対象は src/ 配下（構成の各 Part がそのままファイルになっている）。
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
//    Part 8  状態遷移図出力 StatePlantUmlOptions / StateDiagramCollector /
//                          StatePlantUmlExporter / StateExportRunner
//
//  制約:
//    - main に指定できるファイルは1つだけ。src/ を分割して書き、生成物を配置する
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
//  出力ペインの表示
// ------------------------------------------------------------
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
                LifelineNameField = FindValueField(model, model.Name) ?? FieldNamed(model, "Name");
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
                MessageNameField = FindValueField(message, message.Name) ?? FieldNamed(message, "Name");
            if (MessageKindField == null && !string.IsNullOrEmpty(message.Kind))
                MessageKindField = FindValueField(message, message.Kind);
            if (MessageKindField == null)
                MessageKindField = FieldNamed(message, "MessageSort");
            if (MessageSendPortField == null && message.SendPort != null)
                MessageSendPortField = FindRefField(message, message.SendPort as IModel);
            if (MessageReceivePortField == null && message.ReceivePort != null)
                MessageReceivePortField = FindRefField(message, message.ReceivePort as IModel);

            if (MessageKindField != null && MessageSendPortField != null && MessageReceivePortField != null)
                break;
        }
        if (MessageClass == null) Unavailable.Add("メッセージ（見本なし）");
        else if (MessageKindField != null) HarvestKindValues(d);
    }

    // 種別フィールドの実値（列挙の文字列表現）を実データから採取する。
    // 既定の "sync" などはプロファイルの列挙値と一致しないことがある
    private void HarvestKindValues(ISequenceDiagram d)
    {
        foreach (var shape in d.Messages.Cast<IMessageShape>())
        {
            var message = shape.Model as IMessage;
            if (message == null) continue;

            string actual;
            try { actual = message.GetFieldString(MessageKindField); }
            catch (Exception) { continue; }
            if (string.IsNullOrEmpty(actual)) continue;

            var kind = (message.Kind ?? "").ToLowerInvariant();
            if (kind == "async") KindValues[PumlKind.Async] = actual;
            else if (kind == "reply") KindValues[PumlKind.Reply] = actual;
            else if (kind == "create") KindValues[PumlKind.Create] = actual;
            else if (kind == "destroy") KindValues[PumlKind.Destroy] = actual;
            else KindValues[PumlKind.Sync] = actual;
        }
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
                FragmentTextField = FindValueField(model, shape.Text) ?? FindValueField(model, model.Name)
                                 ?? FieldNamed(model, "Operator");

            foreach (var operandShape in shape.Operands.Cast<IOperandShape>())
            {
                var operand = ModelOf(operandShape);
                if (operand == null) continue;

                OperandClass = operand.ClassName;
                FragmentOperandsField = OwnerFieldOf(operand);
                if (OperandGuardField == null)
                    OperandGuardField = FindValueField(operand, operandShape.Guard)
                                     ?? FieldNamed(operand, "Guard")
                                     ?? FindValueField(operand, operand.Name);
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
                NoteTextField = FindValueField(model, shape.Text) ?? FieldNamed(model, "Body")
                             ?? FindValueField(model, model.Name);
            if (NoteTargetsField == null && LifelineClass != null)
                NoteTargetsField = FindReferenceFieldName(model.Metaclass, LifelineClass, "Lifeline")
                                ?? FieldNamed(model, "CommentedTargets");
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
                UseNameField = FindValueField(model, shape.Text) ?? FindValueField(model, model.Name)
                            ?? FieldNamed(model, "Name");
            if (UseTargetsField == null && LifelineClass != null)
                UseTargetsField = FindReferenceFieldName(model.Metaclass, LifelineClass, "Lifeline")
                               ?? FieldNamed(model, "CoveredLifelines");
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
            // UML プロファイルでは Name / Guard / Body などの文字列属性が
            // kind=所有(IsEmbedded) で定義されているため、参照だけを除外する
            if (f.IsReference) continue;
            // $ParentName や ____anonymous____* などのシステム/匿名フィールドは
            // 値が偶然一致しても書き込み先として不適切なので対象外にする
            if (f.Name.StartsWith("$", StringComparison.Ordinal)
                || f.Name.StartsWith("____", StringComparison.Ordinal)) continue;
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
            if (!TypeNameMatches(typeName, typeClassName)) continue;

            if (f.UpperBound < 0) return f.Name;
            if (fallback == null) fallback = f.Name;
            if (!string.IsNullOrEmpty(nameHint)
                && f.Name.IndexOf(nameHint, StringComparison.OrdinalIgnoreCase) >= 0)
                fallback = f.Name;
        }
        return fallback;
    }

    // 'Message' と 'UmlInteractionMessage' のように、フィールドの型名と
    // 実クラス名が基底/派生で食い違うプロファイルがあるため末尾一致も許す
    private static bool TypeNameMatches(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        return a.EndsWith(b, StringComparison.Ordinal) || b.EndsWith(a, StringComparison.Ordinal);
    }

    // メタクラスに指定名のフィールドが実在すればその名前を返す。
    // 値比較による自動判別が失敗したときの、既知の標準名への最終フォールバック
    private static string FieldNamed(IModel m, string fieldName)
    {
        if (m == null || m.Metaclass == null) return null;
        foreach (var f in m.Metaclass.GetFields().Cast<IField>())
            if (string.Equals(f.Name, fieldName, StringComparison.Ordinal)) return fieldName;
        return null;
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

                // 比較は表示ラベル同士で行う（プラン側 MatchLifelines と同じ基準）。
                // model.Name は "インスタンス名 : 型名" のうち内部名だけを持つため、
                // 表示ラベルと比較すると全件が誤って改名対象になる
                if (_s.UpdateLifelineNames && !string.Equals(hit.Label, l.Label, StringComparison.Ordinal))
                {
                    try
                    {
                        model.SetField(_map.LifelineNameField, l.Label);
                        _r.LifelinesUpdated++;
                    }
                    catch (Exception ex)
                    {
                        _r.AddWarning("ライフライン「" + hit.Label + "」を改名できませんでした: " + ex.Message);
                    }
                }
            }
            else
            {
                model = AddNewModelAtIndex(interaction, field, _map.LifelineClass, ordered.Count);
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
                model = AddNewModelAtIndex(interaction, field, _map.MessageClass, ordered.Count);
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
                model = AddNewModelAtIndex(interaction, field, _map.FragmentClass, ordered.Count);
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
                model = AddNewModelAtIndex(fragment, field, _map.OperandClass, ordered.Count);
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
                model = AddNewModelAtIndex(interaction, field, _map.NoteClass, ordered.Count);
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
                model = AddNewModelAtIndex(interaction, field, _map.InteractionUseClass, ordered.Count);
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
                var current = owner.GetFieldValues(field).Cast<IModel>().ToList();
                var at = current.FindIndex(m => m != null && m.Id == desired[i].Id);
                if (at < 0 || at == i) continue;
                // 先頭 i 個は確定済みなので移動対象は常に現在位置 > i にあり、
                // 「index i の要素の前へ挿入」で位置 i に入る
                desired[i].MoveTo(owner, field, "before", i);
                _r.Reordered++;
            }
        }
        catch (Exception ex)
        {
            _r.AddWarning("並べ替えできませんでした（" + field + "）: " + ex.Message);
        }
    }

    // V3 の AddNewModelAt は (fieldName, className, direction, index, fuzzy) の
    // 1 オーバーロードのみ。挿入位置 index を before / last に読み替える
    private static IModel AddNewModelAtIndex(IModel owner, string field, string className, int index)
    {
        var count = 0;
        try { count = owner.Count(field); } catch (Exception) { }

        if (index < 0) index = 0;
        if (index >= count) return owner.AddNewModelAt(field, className, "last", 0, true);
        return owner.AddNewModelAt(field, className, "before", index, true);
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
        DumpGeometry(w, diagram);

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

    // 座標系のトラブル（operand の Position が絶対か相対かなど）を実測するためのダンプ
    private static void DumpGeometry(Action<string> w, ISequenceDiagram d)
    {
        w("---- 形状ジオメトリ ----");

        foreach (var f in d.Fragments.Cast<IFragmentShape>()
                           .OrderBy(x => x.LocationY).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            w("  fragment '" + Shorten(f.Text) + "'"
              + " Y=" + f.LocationY + " H=" + f.Height + " X=" + f.LocationX);
            foreach (var o in f.Operands.Cast<IOperandShape>()
                               .OrderBy(x => x.Position).ThenBy(x => x.Id, StringComparer.Ordinal))
                w("    operand Position=" + o.Position + " guard='" + Shorten(o.Guard) + "'");
        }

        foreach (var e in d.ExecutionSpecifications.Cast<IExecutionSpecificationShape>()
                           .OrderBy(x => x.LocationY).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            var name = e.Lifeline != null ? Shorten(e.Lifeline.Text) : "(不明)";
            w("  exec " + name + " Y=" + e.LocationY + " Len=" + e.Length + " X=" + e.LocationX);
        }

        foreach (var m in d.Messages.Cast<IMessageShape>()
                           .OrderBy(x => x.SourceY).ThenBy(x => x.Id, StringComparer.Ordinal))
            w("  message SourceY=" + m.SourceY + " : " + Shorten(m.Text));

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

/* 撤去済み: 新規シーケンス図 作成実験（CreateProbe）
   実機検証の結果: 図モデルの作成と名前設定は可能だが、ライフライン・
   実行仕様・メッセージの作成はすべて「このモデルはシーケンス図の中
   だけで編集することができます」で拒否された（2026-08-13）。
   V3.x ではシーケンス図の内容をスクリプト拡張から構築できない。
   実験コードは git 履歴（コミット 295d570）を参照。 */

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
        OutputPane.Show(app, Category);
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
        var editor = context.App.Workspace.CurrentEditor;
        var stateOptions = new StatePlantUmlOptions();

        // シーケンス図 → 状態遷移図 → クラス図の順に判別する。
        // 状態遷移図の EditorType は ERDiagram 等と重なる可能性があるため、
        // クラス図判定より先にノード内容で判定する
        if (editor is ISequenceDiagram)
            ExportRunner.ExportCurrent(context.App, new PlantUmlOptions(), settings);
        else if (editor is IDiagram && StateExportRunner.IsStateDiagram((IDiagram)editor, stateOptions))
            StateExportRunner.ExportCurrent(context.App, stateOptions, settings);
        else if (ClassExportRunner.IsClassDiagramEditor(editor))
            ClassExportRunner.ExportCurrent(context.App, new ClassPlantUmlOptions(), settings);
        else
            ExportRunner.ExportCurrent(context.App, new PlantUmlOptions(), settings);   // 対象外の案内は従来どおり
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

        // 状態遷移図・クラス図が 1 枚でもあれば続けて出力する。
        // どちらも無いプロジェクトでは従来と同じ操作感のまま何も起きない
        var root = ExportRunner.ResolveRoot(context.App);
        if (root == null) return;

        var stateOptions = new StatePlantUmlOptions();
        var skipCount = 0;
        List<ClassDiagramEntry> classTargets;
        List<ClassDiagramEntry> stateTargets;
        StateExportRunner.CollectSplit(root, settings.SkipEmptyDiagram, stateOptions,
                                       ref skipCount, out classTargets, out stateTargets);

        if (stateTargets.Count > 0)
            StateExportRunner.ExportAll(context.App, context, stateOptions, settings,
                                        null, false, root, stateTargets, skipCount);

        if (classTargets.Count > 0)
            ClassExportRunner.ExportAll(context.App, context, new ClassPlantUmlOptions(), settings,
                                        null, false, root, classTargets, skipCount);
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(ExportRunner.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "PlantUML 出力に失敗しました。\n\n" + ex.Message, ExportRunner.Category);
    }
}

// ============================================================
//  Part 6 / 診断側のコマンドハンドラ
//  （manifest.json の execFunc と名前を一致させる）
//
//  注: PlantUML からの取り込み（ImportFromFile / ImportFromFolder）は
//      撤去済み。Part 1〜5 の旧ライターはリボンから到達しない。
//      汎用モデル更新だけでシーケンスの構造と表示を構築できるとは限らない。
//      V3全般の作成不可、純正PlantUMLImporterの提供は根拠未確認。
//      公開SDKのインポートAPI候補と未確認事項は
//      docs/sequence-import-api-research.md を参照する。
// ============================================================

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

        OutputPane.Show(app, ClassProbe.Category);

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

        OutputPane.Show(app, MetaProbe.Category);

        MetaProbe.Run(app, diagram);
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(MetaProbe.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "メタモデル調査に失敗しました。\n\n" + ex.Message, MetaProbe.Category);
    }
}


// ------------------------------------------------------------
//  クラス図同期（Part 9）。本体は ClassSyncRuntime（61-class-sync-runtime.cs）
// ------------------------------------------------------------

public void PreviewClassSync(ICommandContext context, ICommandParams commandParams) { ClassSyncRuntime.Preview(context.App); }
public void ApplyClassSync(ICommandContext context, ICommandParams commandParams) { ClassSyncRuntime.Preview(context.App, true, true, true); }
public void CreateClassDiagram(ICommandContext context, ICommandParams commandParams) { ClassDiagramCreator.Create(context.App); }
public void ShowClassSyncDetails(ICommandContext context, ICommandParams commandParams) { foreach (var page in ClassExperiment.Details.Split((char)12)) context.App.Window.UI.ShowInformationDialog(page, ClassExperiment.Title); }
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
// 2.2.0: 本文は同期側の読取り（ClassDiagramSnapshot）と書出し（ClassPumlWriter）で作る。
// 「差分を検証」「PlantUMLを反映」が比較に使うのと同じ経路なので、出力した直後の
// ファイルは差分 0 件になることが構成上保証される。旧 ClassDiagramCollector /
// WriteNodes / WriteLinks は状態遷移図が共有する TextOf 等のために残しているが、
// クラス図の出力には使わない。
public class ClassPlantUmlExporter
{
    private readonly IDiagram _d;
    private readonly ClassPlantUmlOptions _o;
    private readonly StringBuilder _sb = new StringBuilder();
    private ClassDiagramCollector _c;
    private ClassDiagramSnapshot _snapshot;

    public ClassPlantUmlExporter(IDiagram diagram, ClassPlantUmlOptions options)
    {
        _d = diagram;
        _o = options ?? new ClassPlantUmlOptions();
    }

    public List<string> Warnings
    {
        get { return _snapshot != null ? _snapshot.Limitations : new List<string>(); }
    }

    public int NodeCount { get { return _snapshot != null ? _snapshot.Document.Elements.Count(e => e.Kind == "class") : 0; } }
    public int LinkCount { get { return _snapshot != null ? _snapshot.Document.Elements.Count(e => e.Kind == "link") : 0; } }

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
        var log = new StringBuilder();
        _snapshot = ClassDiagramSnapshot.Read(_d, new ClassSyncOptions(), log);
        var text = ClassPumlWriter.Write(_snapshot.Document);
        // ヘッダだけオプションを反映する（本文は同期側と同一に保つ）
        var lines = new List<string>(text.Split('\n'));
        var insertAt = 1;
        if (!string.IsNullOrEmpty(_o.Theme)) lines.Insert(insertAt++, "!theme " + _o.Theme);
        if (_o.EmitTimestamp) lines.Insert(insertAt++, "' generated at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (!_o.IncludeTitle) lines.RemoveAll(l => l.StartsWith("title ", StringComparison.Ordinal));
        if (!_o.HideEmptyMembers) lines.Remove("hide empty members");
        return string.Join(_o.NewLine, lines.ToArray());
    }

    // 旧経路（ClassDiagramCollector 直結）。比較用に残す。リボンからは呼ばれない
    public string ExportLegacy()
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
// PlantUmlTool only: the class diagram probe uses MetaProbe from the legacy import part,
// so AgentReview and NdMcp do not transcribe this file.
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

// A new class diagram made from PlantUML, without any diagram to copy. The input may be an
// exported diagram (package blocks for the owner path, package/component boxes with an alias
// for owners shown on the diagram) or written by hand (package blocks naming owners). Every
// element names its owner by that path. The runtime puts every existing box on the empty
// diagram, adds one new class per owner and kind with no existing class to sit next to, then
// the ordinary sync adds the rest. Plan reads the text; the runtime fills Seeds and Anchors.
public sealed class ClassDiagramDraft
{
    // Path: names of the enclosing package blocks and boxes, outermost first.
    public sealed class Item { public string Name, Keyword, Stereotype; public string[] Path; public bool Container; public int Order; }
    public sealed class Seed { public string Name; public bool Existing; }
    public string Title = "";
    public List<Item> Items = new List<Item>();
    public List<Seed> Seeds = new List<Seed>();
    // Input class name -> the class whose owner and kind a new class takes (itself for a seed).
    public Dictionary<string,string> Anchors = new Dictionary<string,string>(StringComparer.Ordinal);
    public List<string> Reasons = new List<string>();
    public int ExistingCount { get { return Seeds.Count(s=>s.Existing); } }
    public int NewCount { get { return Items.Count(i=>!i.Container)-ExistingCount; } }
    public int ContainerCount { get { return Items.Count(i=>i.Container); } }
    static bool IsClass(ClassElement e) { return e.Kind=="class" && !ClassDocument.IsContainerKeyword(e.Attr("keyword")); }
    static string Key(ClassElement e) { return e.Attr("keyword")+"\u0001"+e.Text; }
    // Plain package blocks only name owners; the document compared with the diagram drops them
    // and takes the owners as the diagram reads them.
    public static void Flatten(ClassDocument doc)
    {
        var packages=new HashSet<string>(doc.Elements.Where(e=>e.Kind=="package").Select(e=>e.Id),StringComparer.Ordinal);
        if(packages.Count==0)return;
        doc.Elements.RemoveAll(e=>packages.Contains(e.Id));
        foreach(var e in doc.Elements)if(e.Parent!=null && packages.Contains(e.Parent))e.Parent="root";
    }
    public static ClassDiagramDraft Plan(ClassDocument input,string fallbackTitle)
    {
        var draft=new ClassDiagramDraft();
        var index=input.Elements.ToDictionary(e=>e.Id);
        string title=input.HasTitle?ClassText.Inline(ClassText.Normalize(input.Root.Text)):"";
        if(title.Length==0)title=ClassText.Inline(ClassText.Normalize(fallbackTitle??""));
        draft.Title=title;
        if(title.Length==0 || title.Contains("\\n"))draft.Reasons.Add("図の名前を決められません。title 行を書いてください");
        foreach(var e in input.Elements.Where(x=>x.Kind=="class").OrderBy(x=>x.Order))
        {
            var path=new List<string>();var at=e.Parent;bool nested=false;
            while(at!=null && at!="root")
            {
                var owner=index[at];
                if(owner.Kind=="class" && !ClassDocument.IsContainerKeyword(owner.Attr("keyword"))) { nested=true;break; }
                path.Insert(0,owner.Text);at=owner.Parent;
            }
            if(nested) { draft.Reasons.Add("クラスの中のクラスは扱えません: "+e.Text);continue; }
            bool container=ClassDocument.IsContainerKeyword(e.Attr("keyword"));
            if(!container && path.Count==0) { draft.Reasons.Add("'"+e.Text+"' を置く場所がありません。package \"既存のモデル名\" { } で囲んでください");continue; }
            draft.Items.Add(new Item{Name=e.Text,Keyword=e.Attr("keyword"),Stereotype=e.Attr("stereotype"),Path=path.ToArray(),Container=container,Order=e.Order});
        }
        foreach(var name in draft.Items.Where(i=>!i.Container).GroupBy(i=>i.Name).Where(g=>g.Count()>1).Select(g=>g.Key))draft.Reasons.Add("同じ名前のクラスが複数あります: "+name);
        if(!draft.Items.Any(i=>!i.Container) && draft.Reasons.Count==0)draft.Reasons.Add("クラスがありません");
        return draft;
    }
    // Run against the new diagram as read once the boxes are on it. Every element goes under
    // the owner the diagram reads for it (a new class: its anchor's), plain package blocks give
    // way to the owner path the diagram reads, a new class written without a stereotype takes
    // its anchor's, an existing class written without members keeps its members, and
    // relationships already between the diagram's classes stay even when the input leaves
    // them out, so making a diagram never deletes anything.
    public void Prepare(ClassDocument desired,ClassDocument current)
    {
        Flatten(desired);
        if(desired.HasTitle)desired.Root.Text=current.Root.Text;
        var index=current.Elements.ToDictionary(e=>e.Id);
        var shown=current.Elements.Where(e=>e.Kind=="class").GroupBy(Key).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
        var mine=desired.Elements.Where(e=>e.Kind=="class").GroupBy(Key).ToDictionary(g=>g.Key,g=>g.First(),StringComparer.Ordinal);
        // Diagram element id -> input element id; owners missing from the input are copied in.
        var map=new Dictionary<string,string>(StringComparer.Ordinal){{"root","root"}};
        foreach(var pair in shown) { ClassElement written;if(mine.TryGetValue(pair.Key,out written))map[pair.Value.Id]=written.Id; }
        Func<string,string> mapped=null;
        mapped=id=>{
            string known;if(map.TryGetValue(id,out known))return known;
            var e=index[id];var copy=e.Copy();copy.Id="cp:"+id;copy.Line=0;map[id]=copy.Id;
            copy.Parent=e.Parent==null?null:mapped(e.Parent);
            desired.Elements.Add(copy);return copy.Id;
        };
        foreach(var written in mine.Values.ToList())
        {
            ClassElement read;
            if(shown.TryGetValue(Key(written),out read))
            {
                if(written.Parent=="root" || !map.ContainsValue(written.Parent))written.Parent=mapped(read.Parent);
                if(written.Attr("stereotype").Length==0)written.Attributes["stereotype"]=read.Attr("stereotype");
                continue;
            }
            string anchor;
            if(!Anchors.TryGetValue(written.Text,out anchor))continue;
            var seed=current.Elements.FirstOrDefault(e=>IsClass(e) && e.Text==anchor);
            if(seed==null)continue;
            if(written.Parent=="root")written.Parent=mapped(seed.Parent);
            if(written.Attr("stereotype").Length==0 && written.Attr("keyword")==seed.Attr("keyword"))written.Attributes["stereotype"]=seed.Attr("stereotype");
        }
        foreach(var pair in shown.Where(p=>IsClass(p.Value)))
        {
            ClassElement written;
            if(!mine.TryGetValue(pair.Key,out written))continue;
            if(desired.Elements.Any(e=>e.Parent==written.Id && ClassDocument.MemberKinds.Contains(e.Kind)))continue;
            foreach(var member in current.Elements.Where(e=>e.Parent==pair.Value.Id && ClassDocument.MemberKinds.Contains(e.Kind)).OrderBy(e=>e.Order).ToList())
            {
                var copy=member.Copy();copy.Id="cp:"+member.Id;copy.Parent=written.Id;copy.Line=0;
                desired.Elements.Add(copy);
            }
        }
        foreach(var link in current.Elements.Where(e=>e.Kind=="link").ToList())
        {
            string from,to;
            if(!map.TryGetValue(link.Link("from")??"",out from) || !map.TryGetValue(link.Link("to")??"",out to))continue;
            if(desired.Elements.Any(e=>e.Kind=="link" && e.Link("from")==from && e.Link("to")==to && e.Text==link.Text))continue;
            var copy=link.Copy();copy.Id="cp:"+link.Id;copy.Line=0;
            copy.Links["from"]=new[]{from};copy.Links["to"]=new[]{to};
            desired.Elements.Add(copy);
        }
    }
}
// SDK-facing read side: recognize a class diagram editor and read it into a ClassDocument.
// Shared by the exporter (PlantUmlTool / AgentReview / NdMcp) and the sync runtime.
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
// ============================================================
//  Part 9 / クラス図同期のリボン側（結果表示と診断ファイル）
//
//    同期本体（Part 9 の前半、60-class-sync.cs / 61-class-sync-runtime.cs）は
//    実験拡張 ClassImportProbe（削除済み）で実機検証したものをそのまま置いている。ここは本体が
//    参照する結果置き場（ClassExperiment）だけ。NdMcp では同名のクラスを
//    ダイアログ無しの版に差し替えて同じ本体を使う。
// ============================================================

public static class ClassExperiment
{
    public const string Version = "0.7.2";
    public const string Title = "PlantUML 連携 / クラス図同期 " + Version;
    public static string Summary = "クラス図を開き「差分を検証」または「PlantUMLを反映」を押してください。";
    public static string Details = "まだ実行していません。";
    public static void Show(IApplication app) { app.Window.UI.ShowInformationDialog(Summary, Title); }
    public static void Write(string path, string text)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
    }
    // 診断にはモデル名と ID が含まれる。この PC に残すだけで、リポジトリへは入れない。
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
// SDK-facing write-back: capture the editor JSON, probe the metamodel, compare and apply
// PlantUML to the class diagram. AgentReview does not transcribe this file (read-only).
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
    // Set by ClassDiagramCreator for a diagram it has just made: the line to clone when the
    // diagram has no connector yet, and for each class it created empty, the class whose
    // member metaclasses to reuse. Null for an ordinary run.
    [ThreadStatic] public static ClassJsonNode ConnectorTemplate;
    [ThreadStatic] public static Dictionary<string,string> MemberTemplates;
    // Set by the creator when no line can be cloned: relationships are still written, and
    // their connectors stay as the product made them (hidden, K029).
    [ThreadStatic] public static bool AllowHiddenLines;
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
        if((connectors==null || connectors.Items==null || connectors.Items.Count==0) && ConnectorTemplate==null)
        {
            if(added==0) { log.AppendLine("no connector entries to re-apply");return; }
        }
        var template=connectors!=null && connectors.Items!=null && connectors.Items.Count>0?connectors.Items[0]:ConnectorTemplate;
        // A diagram made by ClassDiagramCreator may have no Connectors list yet.
        if((connectors==null || connectors.Items==null) && template!=null) { connectors=new ClassJsonNode{Items=new List<ClassJsonNode>()};unit.Editor.Properties["Connectors"]=connectors; }
        foreach(var c in d.Connectors.Cast<object>().ToList())
        {
            var shape=c as IConnector;if(shape==null || before.Contains(shape.Id))continue;
            if(template==null && AllowHiddenLines) { log.AppendLine("connector "+shape.Id+" left hidden: no line to clone");continue; }
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
                    // A class made empty by the diagram creator borrows from the class it copied.
                    string templateId;
                    var lender=created!=null?created.Sibling:owner;
                    if(sibling==null && MemberTemplates!=null && MemberTemplates.TryGetValue(lender.Id,out templateId))
                    {
                        var template=project.GetModelById(templateId);
                        if(template!=null)sibling=template.GetFieldValues(fieldName).Cast<object>().OfType<IModel>().FirstOrDefault(m=>!m.IsDeleted);
                    }
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
            if(preflight.LinkAddCount>0 && (existing==null || existing.Items==null || existing.Items.Count==0) && ConnectorTemplate==null && !AllowHiddenLines)throw new InvalidOperationException("C220: 図に既存の線がないため、線の雛形を取れません。");
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
                    // Right of the sibling; classes added next to the same sibling stack downward
                    // instead of landing on each other. A node enclosing the sibling (its package
                    // or component box) is not an obstacle.
                    var s=c.SiblingNode;
                    double x=s.LocationX+s.Width+40,y=s.LocationY,w=s.Width,h=s.Height;
                    var others=d.Nodes.Cast<object>().OfType<INode>().Where(n=>n.Id!=node.Id
                        && !(n.Id!=s.Id && n.LocationX<=s.LocationX && n.LocationY<=s.LocationY && s.LocationX+s.Width<=n.LocationX+n.Width && s.LocationY+s.Height<=n.LocationY+n.Height)).ToList();
                    // Only on a diagram the creator made: its nodes are all top level, while nested
                    // nodes on other diagrams may not share one coordinate frame (unverified).
                    for(int guard=0;MemberTemplates!=null && guard<200 && others.Any(n=>n.LocationX<x+w && x<n.LocationX+n.Width && n.LocationY<y+h && y<n.LocationY+n.Height);guard++)y+=h+40;
                    try { node.SetLocationAt(x,y);node.SetSizeAt(w,h); }
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
    // prepare: lets the diagram creator adjust the parsed input against the diagram as read
    // (placing classes under the owners of the classes it put there) before the comparison.
    public static Outcome Run(IApplication app,IEditor editor,string pumlText,string sourceLabel,bool trial,bool retain,bool apply,Func<string,bool> confirm,Action<ClassDocument,ClassDiagramSnapshot,StringBuilder> prepare=null)
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
            if(prepare!=null) { prepare(desired,snapshot,log);desired.Validate(); }
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
// ============================================================
//  Part 9 / PlantUML から新しいクラス図を作る
//
//    見本の図は使わない。クラス図グループ（または開いているクラス図の所有先）の
//    クラス図欄に図のモデルを作り、PlantUML の package "名前" { } をグループ配下の
//    既存モデル（Domain など）に対応させてクラスを置く。
//    ・そのモデルに同じ名前のクラスがあれば、既存のクラスを図に載せる
//    ・無ければ、そのモデルが持てるメタクラスのうちキーワード・ステレオタイプが合うもので作る
//    ノードの定義はプロファイルのビュー定義から引く（FindElementDefByClass）。
//    package と種類ごとに 1 つ（種）を先に作って図に置き、いったん保存してから、
//    残りのクラス・メンバ・関連を通常の「PlantUMLを反映」と同じ本体で足す。
//    計画（テキストの読み取りと所有先の決め方）は純粋部 ClassDiagramDraft にある。
// ============================================================

public static class ClassDiagramCreator
{
    // Ribbon entry: find where the diagram goes, pick the file, create, show the result.
    public static void Create(IApplication app)
    {
        var editor=app.Workspace.CurrentEditor;
        IModel owner;IField field;IClass diagramClass;string where;
        try { where=ResolveGroup(app,editor,out owner,out field,out diagramClass); }
        catch(Exception ex) { ClassExperiment.Summary=ex.Message;ClassExperiment.Details=ex.ToString();ClassExperiment.Show(app);return; }
        string path=app.Window.UI.ShowOpenFileDialog("新しいクラス図にするPlantUML","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
        if(string.IsNullOrEmpty(path))return;
        string pumlText;
        try { if(new FileInfo(path).Length>300000)throw new InvalidOperationException("C120: 入力は300KB以下にしてください。");pumlText=File.ReadAllText(path,new UTF8Encoding(false,true)); }
        catch(Exception ex) { ClassExperiment.Summary=ex.Message;ClassExperiment.Details=ex.ToString();ClassExperiment.Show(app);return; }
        var outcome=Run(app,owner,field,diagramClass,where,pumlText,path,Path.GetFileNameWithoutExtension(path),message=>app.Window.UI.ShowConfirmDialog(message,ClassExperiment.Title));
        ClassExperiment.Summary=outcome.Summary;
        string stem=ClassExperiment.SaveReport("create",outcome.Log,outcome.ReportJson,outcome.CurrentPuml);
        if(stem!=null)ClassExperiment.Summary+="\n診断保存先: "+stem+".txt";
        ClassExperiment.Details=outcome.Details;
        ClassExperiment.Show(app);
    }

    static string Name(IModel m) { return m==null?"":ClassText.Inline(ClassText.Normalize(m.Name)); }
    static List<IModel> Children(IModel m)
    {
        // Distinct by id: on the real project every class came back twice (2.4.2).
        try { return m.GetChildren().Cast<IModel>().Where(c=>c!=null && !c.IsDeleted).GroupBy(c=>c.Id).Select(g=>g.First()).ToList(); }
        catch(Exception) { return new List<IModel>(); }
    }
    static List<IEditor> Editors(IModel m)
    {
        try { return m.GetEditors().Cast<object>().OfType<IEditor>().ToList(); }
        catch(Exception) { return new List<IEditor>(); }
    }
    // Models 2..depth levels below m (the direct children are looked at by the caller).
    static List<IModel> Below(IModel m,int depth)
    {
        var result=new List<IModel>();var level=Children(m);
        for(int d=2;d<=depth && level.Count>0 && result.Count<2000;d++)
        {
            level=level.SelectMany(Children).ToList();
            result.AddRange(level);
        }
        return result.GroupBy(c=>c.Id).Select(g=>g.First()).ToList();
    }
    static bool HasClassDiagram(IModel m) { return Editors(m).Any(e=>ClassDiagramKind.Reject(e)==null); }
    // The reference field of the diagram model through which the diagram shows this model:
    // one whose type accepts it, preferring the field another class diagram in the group
    // already uses for the same model, then any it uses at all.
    static IField DisplayField(IModel diagramModel,IModel shown,IModel group,StringBuilder log)
    {
        var all=diagramModel.Metaclass.GetFields().Cast<IField>().ToList();
        var fits=all.Where(f=>f.IsReference && f.TypeClass!=null && f.TypeClass.IsClassOf(shown.Metaclass)).ToList();
        if(fits.Count==0)
            throw new InvalidOperationException("C320: 図のモデル（"+diagramModel.ClassName+"）に '"+Name(shown)+"'（"+shown.ClassName+"）を参照できるフィールドがありません。参照フィールド: "
                +string.Join(", ",all.Where(f=>f.IsReference).Select(f=>f.Name+":"+f.Type).ToArray()));
        if(fits.Count==1)return fits[0];
        var others=Children(group).Where(m=>m.Id!=diagramModel.Id && m.Metaclass!=null && m.Metaclass.FullName==diagramModel.Metaclass.FullName).ToList();
        Func<IField,Func<IModel,bool>,int> uses=(f,which)=>others.Count(o=>{try{return o.GetFieldValues(f.Name).Cast<object>().OfType<IModel>().Any(which);}catch(Exception){return false;}});
        var chosen=fits.OrderByDescending(f=>uses(f,m=>m.Id==shown.Id)).ThenByDescending(f=>uses(f,m=>true)).First();
        log.AppendLine("display field candidates for "+shown.ClassName+": "+string.Join(", ",fits.Select(f=>f.Name).ToArray())+" -> "+chosen.Name);
        return chosen;
    }
    static IEnumerable<IClass> Concrete(IClass declared)
    {
        var all=new List<IClass>{declared};
        try { all.AddRange(declared.GetAllSubClasses().Cast<IClass>()); } catch(Exception) { }
        return all.Where(c=>c!=null && !c.IsAbstract).GroupBy(c=>c.FullName).Select(g=>g.First());
    }

    // The model that gets the diagram, the field that holds class diagrams and their metaclass.
    // With a class diagram open: the same as that diagram. Otherwise the open or selected model
    // (a class diagram group): the field whose existing children are class diagrams, or else the
    // one whose element type has a class-diagram editor in the profile.
    public static string ResolveGroup(IApplication app,IEditor editor,out IModel owner,out IField field,out IClass diagramClass)
    {
        owner=null;field=null;diagramClass=null;
        if(ClassDiagramKind.Reject(editor)==null)
        {
            var diagramModel=ClassDiagramKind.ModelOf(editor);
            if(diagramModel==null || diagramModel.Owner==null)throw new InvalidOperationException("C310: 開いている図のモデルの所有先を取得できません。");
            owner=diagramModel.Owner;diagramClass=diagramModel.Metaclass;
            try { field=diagramModel.GetOwnerField(); } catch(Exception) { }
            if(field==null)throw new InvalidOperationException("C310: 開いている図のモデルの所有フィールドを取得できません。");
            return "開いている図と同じ「"+Name(owner)+"」の下";
        }
        IModel parent=editor!=null?ClassDiagramKind.ModelOf(editor):null;
        if(parent==null) { try { parent=app.Window.EditorPage.CurrentModel; } catch(Exception) { } }
        if(parent==null)throw new InvalidOperationException("C310: クラス図を追加するモデル（クラス図グループなど）を開くか選んでから実行してください。");
        var fields=parent.Metaclass.GetFields().Cast<IField>().Where(f=>f.IsEmbedded && f.TypeClass!=null).ToList();
        // A class diagram already in the group shows which field and metaclass to use.
        foreach(var child in Children(parent))
        {
            if(!HasClassDiagram(child))continue;
            IField own=null;try { own=child.GetOwnerField(); } catch(Exception) { }
            if(own==null)continue;
            owner=parent;field=own;diagramClass=child.Metaclass;
            return "「"+Name(parent)+"」の下";
        }
        // No diagram yet: the profile's editor definitions.
        var views=app.Workspace.CurrentProject.Profile.ViewDefinitions;
        var found=new List<KeyValuePair<IField,IClass>>();
        foreach(var f in fields)
            foreach(var c in Concrete(f.TypeClass))
            {
                bool classEditor=false;
                try { classEditor=views.FindEditorDefByClass(c,null).Cast<IEditorDef>().Any(d=>ClassSyncOptions.ClassEditorTypes.Contains(d.Type)); } catch(Exception) { }
                if(classEditor)found.Add(new KeyValuePair<IField,IClass>(f,c));
            }
        if(found.Count==0)throw new InvalidOperationException("C310: '"+Name(parent)+"'（"+parent.ClassName+"）にはクラス図を追加できません。クラス図グループを開くか選んでから実行してください。");
        if(found.Count>1)throw new InvalidOperationException("C310: '"+Name(parent)+"' に追加できる図の種類が複数あり、クラス図を決められません: "+string.Join(", ",found.Select(p=>p.Key.Name+"/"+p.Value.Name).ToArray()));
        owner=parent;field=found[0].Key;diagramClass=found[0].Value;
        return "「"+Name(parent)+"」の下";
    }

    // Keyword and stereotype a class of this metaclass reads back as (the snapshot's rules).
    static string KeywordOf(IClass c,ClassSyncOptions o)
    {
        string keyword;
        if(o.KeywordMap.TryGetValue(c.Name,out keyword))return keyword;
        try { foreach(var s in c.GetAllSuperClasses().Cast<IClass>())if(o.KeywordMap.TryGetValue(s.Name,out keyword))return keyword; } catch(Exception) { }
        return "class";
    }
    static string StereotypeOf(IClass c,string keyword,ClassSyncOptions o)
    {
        if(!o.EmitStereotypes)return "";
        string stereotype;
        if(o.StereotypeMap.TryGetValue(c.Name,out stereotype))return ClassText.Normalize(stereotype);
        if(!string.Equals(keyword,"class",StringComparison.OrdinalIgnoreCase) || !o.EmitUnknownStereotype)return "";
        return ClassText.Normalize(c.Name);
    }

    // A package path from the input: first below the group, then from the project root (the
    // full owner path the exporter writes).
    static IModel ResolvePath(IProject project,IModel group,string[] path,out string problem)
    {
        problem=null;
        // The exporter writes the owner path from the top, starting with the project name (seen
        // on a real export: "OnBoardClient/OnBoardClient/ソフトウェア詳細設計/..."), so the head is
        // not assumed: the path is taken after each place the group's (or the root's) name
        // appears in it, then as written (a hand-written block naming a model below the group).
        var starts=new List<KeyValuePair<IModel,string[]>>();
        var root=project.DesignModel;
        foreach(var from in new[]{group,root})
        {
            if(from==null)continue;
            for(int i=path.Length-1;i>=0;i--)
                if(path[i]==Name(from))starts.Add(new KeyValuePair<IModel,string[]>(from,path.Skip(i+1).ToArray()));
        }
        starts.Add(new KeyValuePair<IModel,string[]>(group,path));
        if(root!=null)starts.Add(new KeyValuePair<IModel,string[]>(root,path));
        foreach(var start in starts)
        {
            var at=start.Key;string missing=null;
            foreach(var segment in start.Value)
            {
                var next=Children(at).Where(c=>Name(c)==segment).ToList();
                if(next.Count!=1) { missing=next.Count==0?"'"+segment+"' が '"+Name(at)+"' の下にありません":"'"+Name(at)+"' の下に '"+segment+"' が複数あります";break; }
                at=next[0];
            }
            if(missing==null)return at;
            if(problem==null)problem=missing;
        }
        return null;
    }

    sealed class Kind { public IField Field; public IClass Class; public string Keyword, Stereotype; }
    sealed class Placed { public ClassDiagramDraft.Item Item; public IModel Package, Model; public Kind Kind; public bool Created, Container; }

    public static ClassSyncRuntime.Outcome Run(IApplication app,IModel owner,IField field,IClass diagramClass,string where,string pumlText,string sourceLabel,string fallbackTitle,Func<string,bool> confirm)
    {
        var log=new StringBuilder();var outcome=new ClassSyncRuntime.Outcome();
        IModel diagramModel=null;var placed=new List<Placed>();bool saved=false;
        try
        {
            var project=app.Workspace.CurrentProject;
            if(project==null || string.IsNullOrEmpty(project.Path))throw new InvalidOperationException("C310: 保存済みのプロジェクトで実行してください。");
            if(project.HasUnsavedChanges())throw new InvalidOperationException("C310: 未保存の変更があります。作成の途中でプロジェクトを保存するので、先に保存してから実行してください。");
            if(!owner.IsEditable)throw new InvalidOperationException("C310: '"+Name(owner)+"' は編集できません。");
            log.AppendLine("PlantUML source: "+sourceLabel);
            log.AppendLine("place: "+where+" "+owner.ClassName+"."+field.Name+" as "+diagramClass.FullName);
            var input=new ClassPumlParser().Parse(pumlText);
            var draft=ClassDiagramDraft.Plan(input,fallbackTitle);
            if(draft.Reasons.Count>0)throw new InvalidOperationException("C310: 新しい図を作れません。\n"+string.Join("\n",draft.Reasons.ToArray()));
            // An exported diagram keeps its title; the copy gets the next free number.
            var taken=new HashSet<string>(Children(owner).Where(m=>m.ClassName==diagramClass.Name).Select(Name),StringComparer.Ordinal);
            if(taken.Contains(draft.Title))
            {
                int n=2;while(taken.Contains(draft.Title+" "+n))n++;
                log.AppendLine("title '"+draft.Title+"' is taken; using '"+draft.Title+" "+n+"'");
                draft.Title=draft.Title+" "+n;
            }

            // Owners and kinds, all before anything is written.
            var options=new ClassSyncOptions();var reasons=new List<string>();
            var packages=new Dictionary<string,IModel>(StringComparer.Ordinal);
            var kinds=new Dictionary<string,List<Kind>>(StringComparer.Ordinal);
            foreach(var item in draft.Items.Where(i=>i.Container))
            {
                // A package/component box: the owner the exporter showed on the diagram. It must exist.
                string problem;var box=ResolvePath(project,owner,item.Path.Concat(new[]{item.Name}).ToArray(),out problem);
                if(box==null) { reasons.Add("箱 '"+string.Join("/",item.Path.Concat(new[]{item.Name}).ToArray())+"' に対応するモデルがありません（"+problem+"）。package / component の新規作成は扱えません");continue; }
                log.AppendLine("box "+item.Name+" -> "+box.ClassName+" '"+box.Name+"' id="+box.Id);
                placed.Add(new Placed{Item=item,Package=box.Owner,Model=box,Container=true});
            }
            foreach(var item in draft.Items.Where(i=>!i.Container))
            {
                string key=string.Join("\u0001",item.Path);IModel package;
                if(!packages.TryGetValue(key,out package))
                {
                    string problem;package=ResolvePath(project,owner,item.Path,out problem);
                    if(package==null) { reasons.Add("package '"+string.Join("/",item.Path)+"' に対応するモデルがありません（"+problem+"）");packages[key]=null;continue; }
                    packages[key]=package;
                    log.AppendLine("package "+string.Join("/",item.Path)+" -> "+package.ClassName+" '"+package.Name+"' id="+package.Id);
                }
                if(package==null)continue;
                var p=new Placed{Item=item,Package=package};
                var same=Children(package).Where(c=>Name(c)==item.Name).ToList();
                // The exporter writes a class owned by another class at the depth of the nearest box.
                if(same.Count==0)same=Below(package,3).Where(c=>Name(c)==item.Name && c.Metaclass!=null && !ClassDocument.IsContainerKeyword(KeywordOf(c.Metaclass,options))).ToList();
                if(same.Count>1)
                {
                    // Same name, different models: the keyword and stereotype written in the input decide.
                    var fitting=same.Where(c=>c.Metaclass!=null && KeywordOf(c.Metaclass,options)==item.Keyword
                        && (item.Stereotype.Length==0 || StereotypeOf(c.Metaclass,item.Keyword,options)==item.Stereotype)).ToList();
                    if(fitting.Count==1)same=fitting;
                    else { reasons.Add("'"+Name(package)+"' に '"+item.Name+"' が複数あります: "+string.Join(", ",same.Select(c=>c.ClassName+" id="+c.Id).ToArray()));continue; }
                }
                if(same.Count==1) { p.Model=same[0];placed.Add(p);continue; }
                List<Kind> available;
                if(!kinds.TryGetValue(package.Id,out available))
                {
                    available=new List<Kind>();
                    foreach(var f in package.Metaclass.GetFields().Cast<IField>().Where(f=>f.IsEmbedded && f.TypeClass!=null))
                        foreach(var c in Concrete(f.TypeClass))
                        {
                            string k=KeywordOf(c,options);
                            if(ClassDocument.IsContainerKeyword(k))continue;
                            available.Add(new Kind{Field=f,Class=c,Keyword=k,Stereotype=StereotypeOf(c,k,options)});
                        }
                    kinds[package.Id]=available;
                }
                var fits=available.Where(k=>k.Keyword==item.Keyword && (item.Stereotype.Length==0 || k.Stereotype==item.Stereotype)).ToList();
                if(fits.Count>1)
                {
                    // Prefer the metaclass the package's classes already use.
                    var used=Children(package).GroupBy(c=>c.Metaclass==null?"":c.Metaclass.FullName).ToDictionary(g=>g.Key,g=>g.Count());
                    int best=fits.Max(k=>{int n;return used.TryGetValue(k.Class.FullName,out n)?n:0;});
                    if(best>0)fits=fits.Where(k=>{int n;return used.TryGetValue(k.Class.FullName,out n) && n==best;}).ToList();
                }
                if(fits.Count!=1)
                {
                    var choices=available.Where(k=>k.Keyword==item.Keyword).Select(k=>"<<"+k.Stereotype+">>").Distinct().ToArray();
                    reasons.Add("'"+item.Name+"' の種類を決められません（'"+Name(package)+"' に置ける "+item.Keyword+(fits.Count==0?": "+(choices.Length==0?"なし":string.Join(" ",choices)):" が複数: "+string.Join(" ",fits.Select(k=>"<<"+k.Stereotype+">>").ToArray()))+"）。ステレオタイプを書いてください");
                    continue;
                }
                p.Kind=fits[0];placed.Add(p);
            }
            if(reasons.Count>0)throw new InvalidOperationException("C310: 新しい図を作れません。\n"+string.Join("\n",reasons.ToArray()));
            // Seeds: every existing class, and the first new class of each package and kind that
            // has no existing class of that kind on the diagram to sit next to.
            var anchors=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var p in placed.Where(x=>x.Model!=null && !x.Container))
            {
                draft.Seeds.Add(new ClassDiagramDraft.Seed{Name=p.Item.Name,Existing=true});draft.Anchors[p.Item.Name]=p.Item.Name;
                string key=p.Package.Id+"|"+p.Model.Metaclass.FullName;if(!anchors.ContainsKey(key))anchors[key]=p.Item.Name;
            }
            foreach(var p in placed.Where(x=>x.Model==null))
            {
                string key=p.Package.Id+"|"+p.Kind.Class.FullName;string anchor;
                if(anchors.TryGetValue(key,out anchor)) { draft.Anchors[p.Item.Name]=anchor;continue; }
                anchors[key]=p.Item.Name;draft.Anchors[p.Item.Name]=p.Item.Name;
                draft.Seeds.Add(new ClassDiagramDraft.Seed{Name=p.Item.Name});p.Created=true;
                log.AppendLine("seed '"+p.Item.Name+"': new "+p.Kind.Class.FullName+" in '"+p.Package.Name+"'."+p.Kind.Field.Name);
            }
            var seeds=placed.Where(p=>p.Model!=null || p.Created).ToList();
            log.AppendLine("draft: title='"+draft.Title+"' existing="+draft.ExistingCount+" new="+draft.NewCount+" seeds="+seeds.Count);

            // The line to clone for relationships: from a class diagram already in the group, if any.
            ClassJsonNode connectorTemplate=null;
            if(input.Elements.Any(e=>e.Kind=="link"))connectorTemplate=FindConnectorTemplate(project,owner,log);

            string question="クラス図「"+draft.Title+"」を"+where+"に新しく作ります。\n"
                +"既存の箱 "+draft.ContainerCount+" 件・既存のクラス "+draft.ExistingCount+" 件を載せ、新しいクラス "+draft.NewCount+" 件を作ります（置き場は package で指定したモデル）。\n"
                +(input.Elements.Any(e=>e.Kind=="link") && connectorTemplate==null?"同じグループに線のあるクラス図が無いため、関連はモデルには作りますが図の線は表示されません。\n":"")
                +"途中でプロジェクトを保存し、そのあと反映の内容を確認します。";
            if(!confirm(question)) { outcome.Summary="新しい図の作成を中止しました。";outcome.Succeeded=true;return Finish(outcome,log); }

            var transaction=project.BeginUndoTransaction(false);
            IDiagram created;
            try
            {
                diagramModel=owner.AddNewModel(field,diagramClass);
                if(diagramModel==null)throw new InvalidOperationException("C320: 図のモデルを作成できませんでした。");
                diagramModel.SetField("Name",draft.Title);
                log.AppendLine("diagram model "+diagramModel.ClassName+" id="+diagramModel.Id+" name='"+diagramModel.Name+"'");
                var editors=Editors(diagramModel);
                log.AppendLine("editors of the new model: "+string.Join(", ",editors.Select(e=>e.EditorType+"/"+e.ViewDefinitionName).ToArray()));
                created=editors.Where(e=>ClassDiagramKind.Reject(e)==null).OfType<IDiagram>().FirstOrDefault();
                if(created==null)throw new InvalidOperationException("C320: 新しい図のモデルにクラス図のエディタがありません（"+string.Join(", ",editors.Select(e=>e.EditorType+"/"+e.ViewDefinitionName).ToArray())+"）。");
                var views=project.Profile.ViewDefinitions;var editorDef=((IEditor)created).EditorDefinition;
                // Outer boxes first: a box or class inside a box already on the diagram is shown
                // through ownership, while an outermost one is shown only when the diagram model
                // refers to it (AddNodeShape refused a Domain without it on the real project).
                Func<IModel,int> depth=m=>{int d=0;for(var o=m.Owner;o!=null && d<64;o=o.Owner)d++;return d;};
                var onDiagram=new HashSet<string>(StringComparer.Ordinal);
                double x=40,y=40,rowHeight=0;
                foreach(var p in seeds.OrderBy(s=>s.Model==null?depth(s.Package)+1:depth(s.Model)).ThenBy(s=>s.Item.Order))
                {
                    if(p.Created)
                    {
                        p.Model=p.Package.AddNewModel(p.Kind.Field,p.Kind.Class);
                        if(p.Model==null)throw new InvalidOperationException("C320: クラスを作成できませんでした: "+p.Item.Name);
                        p.Model.SetField("Name",p.Item.Name);
                        if(Name(p.Model)!=p.Item.Name)throw new InvalidOperationException("C320: 作成したクラスの名前の読戻しが一致しません: "+p.Item.Name);
                        log.AppendLine("created class "+p.Model.ClassName+" id="+p.Model.Id+" name='"+p.Model.Name+"'");
                    }
                    bool inside=false;
                    for(var o=p.Model.Owner;o!=null && !inside;o=o.Owner)inside=onDiagram.Contains(o.Id);
                    if(!inside)
                    {
                        var shows=DisplayField(diagramModel,p.Model,owner,log);
                        diagramModel.Relate(shows.Name,p.Model);
                        log.AppendLine("diagram refers to '"+p.Item.Name+"' through "+shows.Name);
                    }
                    IElementDef def=null;
                    try { def=views.FindElementDefByClass(editorDef,p.Model.Metaclass,null).Cast<IElementDef>().FirstOrDefault(); }
                    catch(Exception ex) { log.AppendLine("FindElementDefByClass failed for "+p.Model.ClassName+": "+ex.Message); }
                    try { created.AddNodeShape(p.Model,def); } catch(Exception ex) { log.AppendLine("AddNodeShape failed for '"+p.Item.Name+"' ("+(def==null?"no definition":def.Type)+"): "+ex.Message); }
                    var node=created.Nodes.Cast<object>().OfType<INode>().FirstOrDefault(n=>{var m=ClassDiagramKind.ModelOf(n);return m!=null && m.Id==p.Model.Id;});
                    if(node==null)throw new InvalidOperationException("C320: 新しい図に '"+p.Item.Name+"'（"+p.Model.ClassName+"）の箱を置けませんでした。"+(inside?"":"図のモデルからの参照は張りました。"));
                    onDiagram.Add(p.Model.Id);
                    // Outermost boxes go in rows; boxes inside another stay where the product puts them.
                    if(!inside)
                    {
                        if(x>40 && x+node.Width>6000) { x=40;y+=rowHeight+120;rowHeight=0; }
                        node.SetLocationAt(x,y);x+=node.Width+160;rowHeight=Math.Max(rowHeight,node.Height);
                    }
                    log.AppendLine("node "+node.Id+" '"+p.Item.Name+"' "+(inside?"inside":"outer")+" at ("+node.LocationX+","+node.LocationY+" "+node.Width+"x"+node.Height+") visible="+node.IsVisible);
                }
                try { created.Relocate();log.AppendLine("relocated "+created.Nodes.Cast<object>().Count()+" nodes"); }
                catch(Exception ex) { log.AppendLine("relocate failed: "+ex.Message); }
                transaction.Commit();
            }
            catch(Exception)
            {
                try { transaction.Rollback(); } catch(Exception rollbackError) { log.AppendLine("rollback failed: "+rollbackError.Message); }
                diagramModel=null;foreach(var p in placed.Where(x=>x.Created))p.Model=null;
                throw;
            }
            // The sync exports the new diagram before adding relationship lines, which the
            // product refuses while the project is dirty.
            saved=app.Workspace.SaveProject(project,false);
            log.AppendLine("save: "+saved+" unsaved after="+project.HasUnsavedChanges());
            if(!saved || project.HasUnsavedChanges())throw new InvalidOperationException("C320: 作成した図を保存できませんでした。");

            ClassSyncRuntime.ConnectorTemplate=connectorTemplate;
            ClassSyncRuntime.AllowHiddenLines=connectorTemplate==null;
            ClassSyncRuntime.MemberTemplates=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var p in placed.Where(x=>x.Created))
            {
                // A class of the same metaclass already in the package lends its member metaclasses.
                var lender=Children(p.Package).FirstOrDefault(c=>c.Id!=p.Model.Id && c.Metaclass!=null && c.Metaclass.FullName==p.Kind.Class.FullName);
                if(lender!=null)ClassSyncRuntime.MemberTemplates[p.Model.Id]=lender.Id;
            }
            var sync=ClassSyncRuntime.Run(app,(IEditor)created,pumlText,sourceLabel,true,true,true,confirm,(desired,snapshot,prepareLog)=>{
                draft.Prepare(desired,snapshot.Document);
                prepareLog.AppendLine("input adjusted for the new diagram: "+desired.Elements.Count+" elements");
            });
            log.AppendLine("---- sync ----").Append(sync.Log);
            outcome.ReportJson=sync.ReportJson;outcome.CurrentPuml=sync.CurrentPuml;
            outcome.Changes=sync.Changes;outcome.Limitations=sync.Limitations;outcome.StopReasons=sync.StopReasons;
            if(sync.Succeeded)
            {
                outcome.Succeeded=outcome.Applied=outcome.Committed=true;
                SelectInNavigator(app,diagramModel,log);
                outcome.Summary="クラス図「"+draft.Title+"」を作成しました（既存のクラス "+draft.ExistingCount+" 件 / 新しいクラス "+draft.NewCount+" 件）。\n"
                    +(connectorTemplate==null && input.Elements.Any(e=>e.Kind=="link")?"関連の線は表示されていません（線の雛形なし）。\n":"")
                    +"反映の結果:\n"+sync.Summary+"\n保存はしていません。";
                outcome.Details=outcome.Summary+"\f"+sync.Details;
                return Finish(outcome,log,true);
            }
            outcome.ErrorMessage=sync.ErrorMessage??"反映できませんでした";
            string removed=Remove(project,diagramModel,placed,log);
            outcome.Summary="新しい図に PlantUML を反映できませんでした。\n"+sync.Summary+"\n"+removed;
            outcome.Details=outcome.Summary+"\f"+sync.Details;
            return Finish(outcome,log,true);
        }
        catch(Exception ex)
        {
            outcome.ErrorMessage=ex.Message;
            log.AppendLine(ex.ToString());
            string removed=saved?Remove(app.Workspace.CurrentProject,diagramModel,placed,log):"";
            outcome.Summary="新しい図を作成できませんでした。\n"+ex.Message+(removed.Length>0?"\n"+removed:"");
            return Finish(outcome,log);
        }
        finally { ClassSyncRuntime.ConnectorTemplate=null;ClassSyncRuntime.MemberTemplates=null;ClassSyncRuntime.AllowHiddenLines=false; }
    }

    static ClassSyncRuntime.Outcome Finish(ClassSyncRuntime.Outcome outcome,StringBuilder log,bool detailsSet=false)
    {
        outcome.Log=log.ToString();
        if(!detailsSet)outcome.Details=outcome.Summary+"\f"+outcome.Log;
        return outcome;
    }

    // A connector entry of a class diagram already under the group, from the group's unit as
    // saved. Null when there is none; the relationships are then made without visible lines.
    static ClassJsonNode FindConnectorTemplate(IProject project,IModel owner,StringBuilder log)
    {
        var diagrams=new HashSet<string>(Children(owner).Where(HasClassDiagram).Select(m=>m.Id),StringComparer.Ordinal);
        if(diagrams.Count==0) { log.AppendLine("connector template: no class diagram in the group");return null; }
        string directory=Path.Combine(Path.GetTempPath(),"ClassEditor-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(directory,"snapshot.nmdl");
        try
        {
            Directory.CreateDirectory(directory);
            if(owner.ModelUnit==null) { log.AppendLine("connector template: the group has no model unit");return null; }
            project.UnitManager.ExportModelUnit(owner.ModelUnit,path);
            var exported=ClassJsonNode.Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
            var editors=exported["Editors"];
            if(editors==null || editors.Items==null) { log.AppendLine("connector template: no Editors in the export");return null; }
            foreach(var e in editors.Items.Where(e=>diagrams.Contains(ClassJsonNode.Value(e,"ModelId")??"")))
            {
                var lines=e["Connectors"];
                if(lines!=null && lines.Items!=null && lines.Items.Count>0) { log.AppendLine("connector template: from editor "+ClassJsonNode.Value(e,"Id"));return lines.Items[0]; }
            }
            log.AppendLine("connector template: the group's class diagrams have no line");
            return null;
        }
        catch(Exception ex) { log.AppendLine("connector template: export failed: "+ex.Message);return null; }
        finally
        {
            try { if(File.Exists(path))File.Delete(path);if(Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory); }
            catch(Exception ex) { log.AppendLine("Temporary export cleanup failed: "+ex.Message); }
        }
    }

    // Undo what the creator made when the sync did not go through. The saved file still holds
    // the diagram until the project is saved again.
    static string Remove(IProject project,IModel diagramModel,List<Placed> placed,StringBuilder log)
    {
        if(project==null || diagramModel==null)return "";
        var made=placed.Where(x=>x.Created && x.Model!=null && !x.Model.IsDeleted).ToList();
        var transaction=project.BeginUndoTransaction(false);
        try
        {
            foreach(var p in made)p.Model.Delete();
            if(!diagramModel.IsDeleted)diagramModel.Delete();
            transaction.Commit();
            log.AppendLine("removed the new diagram and "+made.Count+" created classes");
            return "作成した図と、そのために作ったクラス "+made.Count+" 件を削除しました。保存済みのファイルには残っているので、上書き保存すると削除が確定します。";
        }
        catch(Exception ex)
        {
            try { transaction.Rollback(); } catch(Exception) { }
            log.AppendLine("removal failed: "+ex);
            return "作成した図「"+Name(diagramModel)+"」を削除できませんでした。不要なら手動で削除してください。";
        }
    }

    static void SelectInNavigator(IApplication app,IModel model,StringBuilder log)
    {
        try { app.Window.EditorPage.CurrentNavigator.Select(model,false);log.AppendLine("selected in navigator"); }
        catch(Exception ex) { log.AppendLine("navigator select failed: "+ex.Message); }
    }
}
