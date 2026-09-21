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
    static readonly Regex OperationLine=new Regex(@"^(?<name>[^(:]*[^\s(:])\((?<params>.*)\)(?:\s*:\s*(?<ret>(?!.*(?: \[| = )).*))?$");
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
        var operation=OperationLine.Match(rest);
        if(operation.Success)
        {
            element.Kind="operation";
            element.Text=operation.Groups["name"].Value.Trim();
            element.Attributes["parameters"]=operation.Groups["params"].Value.Trim();
            string returnType=operation.Groups["ret"].Success?operation.Groups["ret"].Value.Trim():"";
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
            if(rest.Length==0)throw Error(n,"メンバ名がありません。");
            bool bare=visibility.Length==0 && !isStatic && !isAbstract && type.Length==0 && multiplicity.Length==0 && defaultValue.Length==0;
            if(bare && owner.Keyword=="enum") { element.Kind="literal";element.Text=rest; }
            else
            {
                element.Kind="attribute";element.Text=rest;
                element.Attributes["visibility"]=visibility;element.Attributes["static"]=isStatic?"true":"";
                element.Attributes["type"]=type;element.Attributes["multiplicity"]=multiplicity;element.Attributes["default"]=defaultValue;
            }
        }
        doc.Elements.Add(element);
    }
    static string Directed(string arrow) { if(arrow=="--")return "-->";if(arrow=="..")return "..>";return arrow; }
    void ResolveLink(Pending p)
    {
        ClassElement from,to;
        if(!aliases.TryGetValue(p.From,out from))throw Error(p.Line,"未宣言の別名です: "+p.From);
        if(!aliases.TryGetValue(p.To,out to))throw Error(p.Line,"未宣言の別名です: "+p.To);
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
    public static string Render(ClassElement m)
    {
        if(m.Kind=="literal")return m.Text;
        var sb=new StringBuilder();
        if(m.Attr("visibility").Length>0)sb.Append(m.Attr("visibility")).Append(' ');
        if(m.Attr("static")=="true")sb.Append("{static} ");
        if(m.Kind=="operation")
        {
            if(m.Attr("abstract")=="true")sb.Append("{abstract} ");
            sb.Append(m.Text).Append('(').Append(m.Attr("parameters")).Append(')');
            if(m.Attr("returnType").Length>0)sb.Append(" : ").Append(m.Attr("returnType"));
            return sb.ToString();
        }
        sb.Append(m.Text);
        if(m.Attr("type").Length>0)sb.Append(" : ").Append(m.Attr("type"));
        if(m.Attr("multiplicity").Length>0)sb.Append(" [").Append(m.Attr("multiplicity")).Append(']');
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
    static readonly string[] Ignored = { "alias", "field", "arrow" };
    // A member whose name contains parentheses reads as an operation from text although the
    // model calls it an attribute. The rendered line is what PlantUML carries, so members are
    // compared by that line and attribute/operation/literal are one kind for matching.
    static bool IsMember(ClassElement e) { return ClassDocument.MemberKinds.Contains(e.Kind); }
    static string KindKey(ClassElement e) { return IsMember(e)?"member":e.Kind; }
    static string Properties(ClassElement e)
    {
        if(IsMember(e))return "member|"+ClassPumlWriter.Render(e);
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
        if(IsMember(before) && IsMember(after) && ClassPumlWriter.Render(before)==ClassPumlWriter.Render(after))return "";
        if(before.Text!=after.Text)keys.Add("name");
        if(before.Kind!=after.Kind)keys.Add("kind");
        foreach(var key in before.Attributes.Keys.Union(after.Attributes.Keys).Where(k=>!Ignored.Contains(k)).OrderBy(k=>k,StringComparer.Ordinal))
            if(before.Attr(key)!=after.Attr(key))keys.Add(key);
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
    public string CurrentId, Kind, OldText, NewText, OldVisibility, NewVisibility, OldType, NewType;
    public int Line;
    public bool NameChanged, VisibilityChanged, TypeChanged;
    public string Describe()
    {
        var parts=new List<string>();
        if(NameChanged)parts.Add("name '"+OldText+"'->'"+NewText+"'");
        if(VisibilityChanged)parts.Add("visibility '"+OldVisibility+"'->'"+NewVisibility+"'");
        if(TypeChanged)parts.Add("type '"+OldType+"'->'"+NewType+"'");
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
    public string Action, Kind, OwnerId, OwnerAlias, CurrentId, Text, Visibility, Type, Parameters;
    public bool IsStatic;
    public int Line;
}

public sealed class ClassTextPreflight
{
    public List<ClassMemberEdit> Edits = new List<ClassMemberEdit>();
    public List<ClassLinkChange> Links = new List<ClassLinkChange>();
    public List<ClassMemberChange> Members = new List<ClassMemberChange>();
    public List<string> Reasons = new List<string>();
    public bool Candidate { get { return Reasons.Count==0 && (Edits.Count>0 || Links.Count>0 || Members.Count>0); } }
    public int MemberAddCount { get { return Members.Count(m=>m.Action=="add"); } }
    public int MemberDeleteCount { get { return Members.Count(m=>m.Action=="delete"); } }
    public int LinkAddCount { get { return Links.Count(l=>l.Action=="add"); } }
    public int LinkDeleteCount { get { return Links.Count(l=>l.Action=="delete"); } }
    public int NameCount { get { return Edits.Count(e=>e.NameChanged); } }
    public int VisibilityCount { get { return Edits.Count(e=>e.VisibilityChanged); } }
    public int TypeCount { get { return Edits.Count(e=>e.TypeChanged); } }
    static readonly string[] AttributeKeys = { "name", "visibility", "type" };
    static readonly string[] OperationKeys = { "name", "visibility" };
    public static ClassTextPreflight Check(ClassDocument current,ClassDocument desired,ClassSyncPlan plan)
    {
        var result=new ClassTextPreflight();
        var old=current.Elements.ToDictionary(e=>e.Id);
        var target=plan.Expected.Elements.ToDictionary(e=>e.Id);
        foreach(var c in plan.Changes)
        {
            string where=c.Line>0?" 入力"+c.Line+"行":"";
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
                    if(!old.TryGetValue(link.Link("from")??"",out from) || !old.TryGetValue(link.Link("to")??"",out to)) { result.Reasons.Add("add link"+where+": 両端が既存のクラスではありません"); continue; }
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
                    if(!old.TryGetValue(member.Parent??"",out owner)) { result.Reasons.Add("add "+c.Kind+where+": 所有先のクラスが既存ではありません"); continue; }
                    if(member.Text.Length==0 || member.Text.Contains("\\n")) { result.Reasons.Add("add "+c.Kind+where+": 空または改行を含む名前は扱えません"); continue; }
                    if(c.Kind=="operation" && member.Attr("parameters").Length>0) { result.Reasons.Add("add operation"+where+": 引数付きの操作の追加は扱えません（引数は別モデル）"); continue; }
                    if(c.Kind=="operation" && member.Attr("returnType").Length>0) { result.Reasons.Add("add operation"+where+": 戻り値付きの操作の追加は扱えません"); continue; }
                    if(c.Kind=="attribute" && (member.Attr("multiplicity").Length>0 || member.Attr("default").Length>0)) { result.Reasons.Add("add attribute"+where+": 多重度・既定値付きの属性の追加は扱えません"); continue; }
                    if(member.Attr("type").Contains(", ")) { result.Reasons.Add("add attribute"+where+": 複数の型を持つ属性は扱えません"); continue; }
                    result.Members.Add(new ClassMemberChange{Action="add",Kind=c.Kind,OwnerId=owner.Id,OwnerAlias=owner.Attr("alias"),Text=member.Text,Visibility=member.Attr("visibility"),Type=member.Attr("type"),Parameters=member.Attr("parameters"),IsStatic=member.Attr("static")=="true",Line=c.Line});
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
                NameChanged=keys.Contains("name"),VisibilityChanged=keys.Contains("visibility"),TypeChanged=keys.Contains("type")};
            string problem=null;
            if(edit.NameChanged && (edit.NewText.Length==0 || edit.NewText.Contains("\\n") || edit.OldText.Contains("\\n")))problem="空または改行を含む名前は扱えません";
            else if(edit.VisibilityChanged && edit.NewVisibility.Length==0)problem="可視性の記号を消す変更は扱えません";
            else if(edit.TypeChanged && edit.NewType.Length==0)problem="型を空にする変更は扱えません";
            else if(edit.TypeChanged && edit.NewType.Contains(", "))problem="複数の型を持つ属性は扱えません";
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
        sb.Append("メンバ ").Append(Edits.Count).Append("件（名前 ").Append(NameCount).Append(" / 可視性 ").Append(VisibilityCount).Append(" / 型 ").Append(TypeCount).Append("） / メンバ追加 ").Append(MemberAddCount).Append(" 削除 ").Append(MemberDeleteCount).Append(" / 関連 追加 ").Append(LinkAddCount).Append(" 削除 ").Append(LinkDeleteCount).Append(" / 停止理由 ").Append(Reasons.Count).Append("件\n");
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
