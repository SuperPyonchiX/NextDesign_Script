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
