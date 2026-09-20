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
public void ShowClassResult(ICommandContext context, ICommandParams parameters) { ClassExperiment.Show(context.App); }
public void ShowClassDetails(ICommandContext context, ICommandParams parameters) { foreach(var page in ClassExperiment.Details.Split('\f')) context.App.Window.UI.ShowInformationDialog(page, ClassExperiment.Title); }

public static class ClassExperiment
{
    public const string Version = "0.1.1";
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
    class NodeInfo { public IModel Model; public INode Node; public ClassElement Element; }
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
            if(parent!=null)info.Element.Parent=parent.Element.Id;
            else info.Element.Parent=PackageOf(info.Model);
            doc.Elements.Add(info.Element);
        }
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
                e.Attributes["parameters"]=ClassText.Inline(ParametersOf(child));e.Attributes["returnType"]=ClassText.Inline(TextOf(child,o.ReturnTypeFieldNames));
                operations.Add(e);
            }
            else if(kind=="literal") { e.Kind="literal";attributes.Add(e); }
            else
            {
                e.Kind="attribute";
                e.Attributes["visibility"]=VisibilityOf(child);e.Attributes["static"]=BoolField(child,o.StaticFieldNames)?"true":"";
                e.Attributes["type"]=ClassText.Inline(TextOf(child,o.TypeFieldNames));
                e.Attributes["multiplicity"]=o.EmitMultiplicity?ClassText.Inline(TextOf(child,o.MultiplicityFieldNames)):"";
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
    static string Multiplicity(IField f)
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
    public static string Read(IProject project,IModel model,IEditor diagram,StringBuilder log)
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
    public static void Preview(IApplication app)
    {
        var log=new StringBuilder();string report=null;string screenshot=null;string currentPuml=null;
        try
        {
            var editor=app.Workspace.CurrentEditor;
            string reject=ClassDiagramKind.Reject(editor);
            if(reject!=null)throw new InvalidOperationException(reject);
            var diagram=(IDiagram)editor;var project=app.Workspace.CurrentProject;
            string path=app.Window.UI.ShowOpenFileDialog("図と比較するPlantUML（PlantUmlToolのクラス図出力）","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
            if(string.IsNullOrEmpty(path))return;
            if(new FileInfo(path).Length>300000)throw new InvalidOperationException("C120: 入力は300KB以下にしてください。");
            log.AppendLine("PlantUML file: "+path);
            var parser=new ClassPumlParser();
            string pumlText=File.ReadAllText(path,new UTF8Encoding(false,true));
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
            foreach(var ignored in parser.Ignored)log.AppendLine("表示指定を無視: "+ignored);
            var snapshot=ClassDiagramSnapshot.Read(diagram,new ClassSyncOptions(),log);
            var current=snapshot.Document;
            var plan=ClassSyncPlan.Build(current,desired,()=>Guid.NewGuid().ToString());
            currentPuml=ClassPumlWriter.Write(current);
            report="{\"version\":1,\"project\":"+ClassJson.Q(project==null?"":project.Id)+",\"diagram\":"+ClassJson.Q(editor.Id)
                +",\"current\":"+current.ToJson()+",\"desired\":"+desired.ToJson()+",\"plan\":"+plan.ToJson()
                +",\"limitations\":"+ClassJson.Json(snapshot.Limitations.ToArray())
                +",\"modelIds\":"+ClassJson.Json(snapshot.ModelIds.ToDictionary(p=>p.Key,p=>(object)p.Value))
                +",\"geometry\":"+ClassJson.Json(snapshot.Geometry.ToDictionary(p=>p.Key,p=>(object)p.Value))+"}";
            foreach(var c in plan.Changes)log.AppendLine(c.Action+" "+c.Kind+" line="+c.Line+" id="+c.Id+" detail="+c.Detail);
            foreach(var warning in snapshot.Limitations)log.AppendLine("要照合: "+warning);
            screenshot="現在の図と入力の比較結果（図は変更していません）\n"+ClassAudit.Summary(plan,snapshot.Limitations.Count)
                +"\f変更候補の内訳（入力行と種類のみ）\n"+ClassAudit.Reasons(plan)
                +"\f要照合項目 "+snapshot.Limitations.Count+"件\n"+(snapshot.Limitations.Count==0?"なし":string.Join("\n",snapshot.Limitations.ToArray()));
            log.AppendLine(screenshot.Replace('\f','\n'));
            ClassExperiment.Summary=ClassAudit.Summary(plan,snapshot.Limitations.Count)+"\n図への反映は行いません（0.1.0は読取り専用）。";
            log.AppendLine("Scope: "+(project==null?"":project.Id)+" / "+editor.ModelId+" / "+editor.Id);
        }
        catch(Exception ex) { ClassExperiment.Summary="図全体の読取り検証を完了できませんでした。\n"+ex.Message;log.AppendLine(ex.ToString());screenshot=null; }
        string stem=ClassExperiment.SaveReport("preview",log.ToString(),report,currentPuml);
        if(stem!=null)ClassExperiment.Summary+="\n診断保存先: "+stem+".txt";
        ClassExperiment.Details=screenshot??log.ToString();
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
        // DeSIDE fields (SuperClasses/SubClasses/Whole/Parts/Related/RelateFrom/Children) are
        // deliberately absent: PlantUmlTool has no entry for them either, so its export draws
        // them with DefaultLink and this table must produce the same text. Arrows are not compared.
    };
    public Dictionary<string,string> VisibilityMap = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        { "public", "+" }, { "公開", "+" }, { "+", "+" },
        { "private", "-" }, { "非公開", "-" }, { "-", "-" },
        { "protected", "#" }, { "限定公開", "#" }, { "#", "#" },
        { "package", "~" }, { "internal", "~" }, { "パッケージ", "~" }, { "~", "~" },
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
        int split=p.Label.IndexOf(" / ",StringComparison.Ordinal);
        bool twoWay=!generalization && (split>=0 || p.FromMult.Length>0);
        if(!twoWay) { Add(p.Line,from,to,p.Arrow,p.Label,p.ToMult);return; }
        string first=split>=0?p.Label.Substring(0,split).Trim():p.Label;
        string second=split>=0?p.Label.Substring(split+3).Trim():p.Label;
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
    static string Properties(ClassElement e)
    {
        return e.Kind+"|"+e.Text+"|"+string.Join("|",e.Attributes.Where(p=>!Ignored.Contains(p.Key)).OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value));
    }
    static string Anchor(ClassElement e) { return e.Kind=="class"?e.Kind+"|"+e.Text+"|"+e.Attr("keyword"):e.Kind+"|"+e.Text; }
    static string LinkKey(ClassElement e,Dictionary<string,string> map)
    {
        return string.Join("|",e.Links.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+":"+string.Join(",",p.Value.Select(id=>map==null?id:map[id]))));
    }
    static string Differences(ClassElement before,ClassElement after)
    {
        var keys=new List<string>();
        if(before.Text!=after.Text)keys.Add("name");
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
        // A single unmatched element of a kind under a mapped parent is a rename or edit.
        progress=true;
        while(progress)
        {
            progress=false;
            foreach(var a in desired.Elements.Where(e=>structural(e) && !map.ContainsKey(e.Id)).ToArray())
            {
                if(a.Parent==null || !map.ContainsKey(a.Parent))continue;
                var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && b.Parent==map[a.Parent] && b.Kind==a.Kind).ToArray();
                if(candidates.Length==1 && desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Parent==a.Parent && b.Kind==a.Kind)==1)
                {bind(a,candidates[0]);progress=true;}
            }
        }
        align();
        // Links: exact endpoints, arrow and label first; then endpoints only when both sides are unique.
        Func<ClassElement,bool> resolvable=e=>e.Links.Values.SelectMany(v=>v).All(map.ContainsKey);
        foreach(bool exact in new[]{true,false})
        {
            foreach(var a in desired.Elements.Where(e=>e.Kind=="link" && !map.ContainsKey(e.Id) && resolvable(e)).ToArray())
            {
                string key=LinkKey(a,map);
                Func<ClassElement,bool> same=b=>b.Kind=="link" && !used.Contains(b.Id) && LinkKey(b,null)==key && (!exact || (b.Attr("arrow")==a.Attr("arrow") && b.Text==a.Text));
                var candidates=current.Elements.Where(same).ToArray();
                int inputs=desired.Elements.Count(b=>b.Kind=="link" && !map.ContainsKey(b.Id) && resolvable(b) && LinkKey(b,map)==key && (!exact || (b.Attr("arrow")==a.Attr("arrow") && b.Text==a.Text)));
                if(candidates.Length==1 && inputs==1)bind(a,candidates[0]);
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
            if(!old.TryGetValue(e.Id,out before)) {plan.Changes.Add(new ClassChange{Action="add",Id=e.Id,Kind=e.Kind,Line=e.Line,Detail=e.Kind=="link"?"":e.Text});continue;}
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
        foreach(var e in current.Elements.Where(e=>!map.ContainsValue(e.Id)))plan.Changes.Add(new ClassChange{Action="delete",Id=e.Id,Kind=e.Kind,Detail=e.Kind=="link"?"":e.Text});
        plan.Expected.Validate();return plan;
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
