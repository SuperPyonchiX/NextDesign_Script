// Class diagram synchronization probe. 0.1.0 is read-only: it probes the metamodel of the
// active class diagram and compares the diagram with a PlantUML file. Nothing is written.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NextDesign.Core;
using NextDesign.Desktop;

public void ProbeClassDiagram(ICommandContext context, ICommandParams parameters) { ClassDiagramProbe.Run(context.App); }
public void PreviewClassSync(ICommandContext context, ICommandParams parameters) { ClassSyncRuntime.Preview(context.App); }
public void TrialClassText(ICommandContext context, ICommandParams parameters) { ClassSyncRuntime.Preview(context.App, true); }
public void CommitClassText(ICommandContext context, ICommandParams parameters) { ClassSyncRuntime.Preview(context.App, true, true); }
public void ApplyClassSync(ICommandContext context, ICommandParams parameters) { ClassSyncRuntime.Preview(context.App, true, true, true); }
public void ShowClassResult(ICommandContext context, ICommandParams parameters) { ClassExperiment.Show(context.App); }
public void ShowClassDetails(ICommandContext context, ICommandParams parameters) { foreach(var page in ClassExperiment.Details.Split('\f')) context.App.Window.UI.ShowInformationDialog(page, ClassExperiment.Title); }

public static class ClassExperiment
{
    public const string Version = "0.7.2";
    public const string Title = "クラス図同期実験 / " + Version;
    public static string Summary = "クラス図を開き「クラス図調査」または「差分を検証」を押してください。";
    public static string Details = "まだ実行していません。";
    public static void Show(IApplication app) { app.Window.UI.ShowInformationDialog(Summary, Title); }
    public static void Write(string path, string text)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(text);
    }
    // Reports contain model names and IDs. They stay on this PC and never enter the repository.
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

// BEGIN GENERATED ClassSyncRuntime.cs
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
// END GENERATED ClassSyncRuntime.cs

// BEGIN GENERATED ClassSync.cs
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
// END GENERATED ClassSync.cs
