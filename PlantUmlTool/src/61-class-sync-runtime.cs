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
