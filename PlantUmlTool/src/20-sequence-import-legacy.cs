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

