// A stand-in for the product: the generator draws the "before" diagram, a reader that follows
// DiagramSnapshot.Read turns the JSON back into a document, and an applier replays what the
// runtime sends (import, deletions, editor import). It cannot show what the product does on
// its own afterwards, such as laying a bar out again, but it does show whether what we send
// reads back as the input. Every scenario in the batch runs through it.
public static class SequenceSimulator
{
    static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
    static string V(SequenceJson n,string key){return n==null || n[key]==null?null:n[key].StringValue();}
    static double D(SequenceJson n,string key)
    {
        string raw=n[key].Raw;if(raw.StartsWith("\"",StringComparison.Ordinal))raw=n[key].StringValue();
        return double.Parse(raw,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture);
    }
    public static readonly string P=SequencePayload.Prefix;
    // The generator's relation keys, named the way the product names them.
    static readonly string[][] RelationNames={
        new[]{"Frame","___Interaction_Frame"},new[]{"Lifelines","___Interaction_Lifeline"},
        new[]{"ExecutionSpecifications","___Interaction_ExecutionSpecification"},new[]{"Messages","___Interaction_Message"},
        new[]{"OwnedExecutionSpecification","OwnedExecutionSpecification"},new[]{"SendMessage","SendMessage"},new[]{"ReceiveMessage","ReceiveMessage"},
        new[]{"Fragments","___Interaction_CombinedFragment"},new[]{"Operands","___CombinedFragment_InteractionOperand"},
        new[]{"CrossingFragmentCoveredLifeline","CrossingFragmentCoveredLifeline"},new[]{"OperandTargetMessage","OperandTargetMessage"},
        new[]{"NestedInteractionFragment","NestedInteractionFragment"},new[]{"InteractionUses","___Interaction_InteractionUse"},
        new[]{"Notes","___Interaction_InteractionNote"},new[]{"MessageEnds","___Interaction_MessageEnd"},
        new[]{"Destructions","___Interaction_Destruction"},new[]{"DestructionTargetLifeline","DestructionTargetLifeline"},
        new[]{"ReplyMessage","ExecutionSpecificationReplyMessage"},new[]{"DestroyMessage","DestroyMessage"}};
    public static PumlProfile Profile()
    {
        var profile=new PumlProfile();
        foreach(string type in new[]{"Interaction","Frame","Lifeline","ExecutionSpecification","Message","CombinedFragment","InteractionOperand","InteractionUse","InteractionNote","MessageEnd","Destruction"})
            profile.Types[type]="sim-"+type;
        foreach(var pair in RelationNames)profile.Relations[pair[0]]=P+pair[1];
        foreach(string op in new[]{"alt","opt","loop","par","break","critical","group"})profile.Operators[op]=op.ToUpperInvariant();
        return profile;
    }
    static string[] Row(string relation,string kind){return new[]{P+relation,kind,P+relation};}
    public static SequenceFrameTypes FrameTypes()
    {
        var types=new SequenceFrameTypes{Fragment="sim-CombinedFragment",Operand="sim-InteractionOperand",
            Owns=Row("___Interaction_CombinedFragment","Embed"),Branches=Row("___CombinedFragment_InteractionOperand","Embed"),
            Crossing=Row("CrossingFragmentCoveredLifeline","Ref"),OperandMessage=Row("OperandTargetMessage","Ref"),Nested=Row("NestedInteractionFragment","Ref")};
        foreach(string op in new[]{"alt","opt","loop","par","break","critical","group"})types.Operators[op]=op.ToUpperInvariant();
        return types;
    }
    public static SequenceNoteTypes NoteTypes()
    { return new SequenceNoteTypes{Class="sim-InteractionNote",Field="Body",Storage="String",Owns=Row("___Interaction_InteractionNote","Embed")}; }
    public static SequenceRefTypes RefTypes()
    { return new SequenceRefTypes{Class="sim-InteractionUse",Owns=Row("___Interaction_InteractionUse","Embed"),Crossing=Row("CrossingFragmentCoveredLifeline","Ref")}; }
    public static SequenceBaseTypes BaseTypes()
    {
        return new SequenceBaseTypes{Message="sim-Message",Execution="sim-ExecutionSpecification",Lifeline="sim-Lifeline",MessageEnd="sim-MessageEnd",
            OwnsMessage=Row("___Interaction_Message","Embed"),OwnsExecution=Row("___Interaction_ExecutionSpecification","Embed"),
            OwnsLifeline=Row("___Interaction_Lifeline","Embed"),OwnsMessageEnd=Row("___Interaction_MessageEnd","Embed"),
            LaneExecution=Row("OwnedExecutionSpecification","Ref"),SendFromBar=Row("SendMessage","Ref"),ReceiveFromBar=Row("ReceiveMessage","Ref"),
            SendFromEnd=Row("SendMessage","Ref"),ReceiveFromEnd=Row("ReceiveMessage","Ref"),Reply=Row("ExecutionSpecificationReplyMessage","Ref"),
            Nested=Row("NestedInteractionFragment","Ref")};
    }
    public static SequenceDestroyTypes DestroyTypes()
    {
        return new SequenceDestroyTypes{Class="sim-Destruction",Owns=Row("___Interaction_Destruction","Embed"),
            Target=Row("DestructionTargetLifeline","Ref"),Message=Row("DestroyMessage","Ref")};
    }
    public static string Export(string puml)
    {
        // A diagram with nothing on it yet cannot come from the generator, which needs a lane:
        // draw one and take it away again.
        string newline=Environment.NewLine;
        if(!puml.Replace("\r","").Split('\n').Select(l=>l.Trim()).Any(l=>l.Length>0 && !l.StartsWith("@") && !l.StartsWith("'")))
        {
            var data=SequenceJson.Parse(PumlBuild.Build(PumlPlan.Parse(string.Join(newline,new[]{"@startuml","participant Seed","@enduml"})),Profile(),"sim-view","13.0").Json);
            var lanes=new HashSet<string>(data["Entities"].Items.Where(e=>V(e,"EntityType")=="Lifeline").Select(e=>V(e,"Id")));
            data["Entities"].Items.RemoveAll(e=>lanes.Contains(V(e,"Id")));
            data["Relations"].Items.RemoveAll(r=>lanes.Contains(V(r,"TargetId")) || lanes.Contains(V(r,"SourceId")));
            data["Editors"].Items.Single().Properties.Remove("Lifelines");
            return data.ToJsonString();
        }
        return PumlBuild.Build(PumlPlan.Parse(puml),Profile(),"sim-view","13.0").Json;
    }
    public static string EditorId(string json)
    { return V(SequenceJson.Parse(json)["Editors"].Items.Single(),"Id"); }
    public static string RootId(string json)
    { return V(SequenceJson.Parse(json)["Editors"].Items.Single(),"ModelId"); }

    // DiagramSnapshot.Read, over the JSON instead of the SDK.
    public static SequenceDocument Read(string json)
    {
        var data=SequenceJson.Parse(json);
        var entities=data["Entities"].Items.ToDictionary(e=>V(e,"Id"));
        var relations=data["Relations"].Items.ToArray();
        var view=data["Editors"].Items.Single();
        string root=V(view,"ModelId");
        Func<string,SequenceJson[]> shapes=name=>view[name]==null || view[name].Items==null?new SequenceJson[0]:view[name].Items.ToArray();
        Func<string,string,string[]> targets=(type,source)=>relations.Where(r=>V(r,"MetamodelId")==P+type && V(r,"SourceId")==source).Select(r=>V(r,"TargetId")).ToArray();
        Func<string,string,string[]> sources=(type,target)=>relations.Where(r=>V(r,"MetamodelId")==P+type && V(r,"TargetId")==target).Select(r=>V(r,"SourceId")).ToArray();
        Func<string,string,string> field=(id,name)=>{var e=entities[id];return e["Fields"]==null?null:V(e["Fields"],name);};
        var doc=new SequenceDocument{HasTitle=true};var y=new Dictionary<string,double>();
        doc.Elements.Add(new SequenceElement{Id=root,Kind="interaction",Text=V(entities[root],"Name")});
        Action<string,string,string,double> add=(id,kind,text,at)=>{
            Require(entities.ContainsKey(id),"S210: モデルのない図形があります: "+id);
            Require(!y.ContainsKey(id),"S210: 同じモデルの図形が複数あります: "+id);
            doc.Elements.Add(new SequenceElement{Id=id,Kind=kind,Text=text??"",Parent=root});y[id]=at;
        };
        var laneX=new Dictionary<string,double>();
        foreach(var l in shapes("Lifelines").OrderBy(l=>D(l,"X")).ThenBy(l=>V(l,"Id"),StringComparer.Ordinal))
        {add(V(l,"ModelId"),"participant",V(entities[V(l,"ModelId")],"Name"),D(l,"X"));laneX[V(l,"ModelId")]=D(l,"X");}
        var fragmentRegions=new List<SequenceRegion>();var operandRegions=new List<SequenceRegion>();var annotationRegions=new List<SequenceRegion>();
        var operandShapes=shapes("Operands").ToDictionary(o=>V(o,"ModelId"));
        foreach(var f in shapes("Fragments"))
        {
            string id=V(f,"ModelId");double fy=D(f,"Y"),fh=D(f,"Height");
            add(id,"fragment","",fy);
            var element=doc.Elements.Last();
            string op=(field(id,"Operator")??"").ToLowerInvariant();element.Attributes["operator"]=op;
            if(op=="group")element.Text=V(entities[id],"Name");
            fragmentRegions.Add(new SequenceRegion{Id=id,X=D(f,"X"),Y=fy,Width=D(f,"Width"),Height=fh});
            var operands=targets("___CombinedFragment_InteractionOperand",id).Where(operandShapes.ContainsKey)
                .Select(o=>operandShapes[o]).OrderBy(o=>D(o,"Position")).ToArray();
            bool absolute=operands.All(o=>D(o,"Position")>=fy-0.00001 && D(o,"Position")<=fy+fh+0.00001);
            var positions=operands.Select(o=>absolute?D(o,"Position"):fy+D(o,"Position")).ToArray();
            for(int i=0;i<operands.Length;i++)
            {
                string oid=V(operands[i],"ModelId");double top=positions[i],bottom=i+1<positions.Length?positions[i+1]:fy+fh;
                string guard=field(oid,"Guard")??"";
                add(oid,"operand",i>0 && string.Equals(SequenceLabels.Fold(guard),"else",StringComparison.OrdinalIgnoreCase)?"":guard,i==0?fy:top);
                doc.Elements.Last().Parent=id;
                Require(!(top<fy-0.00001 || bottom>fy+fh+0.00001 || bottom<=top),"S210: オペランドの境界が不正です: "+oid);
                operandRegions.Add(new SequenceRegion{Id=oid,Fragment=id,X=D(f,"X"),Y=top,Width=D(f,"Width"),Height=bottom-top});
            }
        }
        var bars=shapes("ExecutionSpecifications").ToDictionary(e=>V(e,"ModelId"));
        foreach(var e in bars.Values)
        {
            add(V(e,"ModelId"),"execution","",D(e,"Y"));
            var lane=sources("OwnedExecutionSpecification",V(e,"ModelId"));
            Require(lane.Length==1,"S210: 実行区間のライフラインがありません。");
            doc.Elements.Last().Links["participant"]=lane;
        }
        Func<string,string> port=(message)=>null;
        var wires=shapes("Messages").ToArray();
        Func<SequenceJson,double> sendX=m=>{
            var from=sources("SendMessage",V(m,"ModelId"));
            return from.Length==1 && bars.ContainsKey(from[0])?D(bars[from[0]],"X"):0;
        };
        foreach(var m in wires.OrderBy(m=>D(m,"SourceY")).ThenBy(sendX).ThenBy(m=>V(m,"Id"),StringComparer.Ordinal))
        {
            string id=V(m,"ModelId");
            add(id,"message",V(entities[id],"Name"),D(m,"SourceY"));var e=doc.Elements.Last();
            string sort=(field(id,"MessageSort")??"").ToLowerInvariant();
            e.Attributes["sort"]=sort;
            if(sources("DestroyMessage",id).Length>0)e.Attributes["sort"]="destroy";
            var from=sources("SendMessage",id);var to=sources("ReceiveMessage",id);
            Require(from.Length==1 && to.Length==1,"S210: メッセージの端点が1つではありません: "+id);
            Func<string,string[]> laneOf=p=>bars.ContainsKey(p)?sources("OwnedExecutionSpecification",p):new string[0];
            e.Links["sender"]=laneOf(from[0]);e.Links["receiver"]=laneOf(to[0]);
            if(bars.ContainsKey(from[0]))e.Links["sendExecution"]=new[]{from[0]};
            if(bars.ContainsKey(to[0]))e.Links["receiveExecution"]=new[]{to[0]};
        }
        foreach(var n in shapes("Notes"))
        {
            string id=V(n,"ModelId");
            add(id,"note",V(entities[id],"Name"),D(n,"Y"));var e=doc.Elements.Last();
            annotationRegions.Add(new SequenceRegion{Id=id,X=D(n,"X"),Y=D(n,"Y"),Width=D(n,"Width"),Height=D(n,"Height")});
            e.Links["targets"]=new string[0];e.Attributes["position"]="free";
        }
        foreach(var u in shapes("InteractionUses"))
        {
            string id=V(u,"ModelId");
            add(id,"ref",V(entities[id],"Name"),D(u,"Y"));var e=doc.Elements.Last();
            annotationRegions.Add(new SequenceRegion{Id=id,X=D(u,"X"),Y=D(u,"Y"),Width=D(u,"Width"),Height=D(u,"Height")});
            e.Links["targets"]=targets("CrossingFragmentCoveredLifeline",id).Where(laneX.ContainsKey).OrderBy(l=>laneX[l]).ToArray();
            e.Attributes["reference"]="";
        }
        foreach(var d in shapes("Destructions"))
        {
            string id=V(d,"ModelId");
            add(id,"destroy","",D(d,"Y"));
            doc.Elements.Last().Links["participant"]=targets("DestructionTargetLifeline",id);
        }
        var byId=doc.Elements.ToDictionary(e=>e.Id);
        var memberships=new List<SequenceMembership>();
        foreach(var r in relations)
        {
            if(!byId.ContainsKey(V(r,"SourceId")) || !byId.ContainsKey(V(r,"TargetId")))continue;
            if(V(r,"MetamodelId")==P+"NestedInteractionFragment" || V(r,"MetamodelId")==P+"OperandTargetMessage")
                memberships.Add(new SequenceMembership{Child=V(r,"TargetId"),Parent=V(r,"SourceId"),Evidence=V(r,"MetamodelId")});
        }
        memberships.AddRange(SequenceRegion.Nesting(operandRegions,fragmentRegions.Concat(annotationRegions)));
        SequenceMembership.Resolve(doc,memberships,line=>{});
        Func<double,double,string> containerAt=(x,at)=>{
            var candidates=operandRegions.Where(r=>x>=r.X && x<=r.X+r.Width && at>=r.Y && at<r.Y+r.Height-1.0).ToArray();
            var nearest=candidates.Where(r=>!candidates.Any(inner=>inner.Id!=r.Id && SequenceRegion.Contains(r,inner))).ToArray();
            if(nearest.Length>1)return root;
            return nearest.Length==1?nearest[0].Id:root;
        };
        foreach(var shape in bars.Values)
        {
            string barId=V(shape,"ModelId");var item=byId[barId];
            double top=D(shape,"Y"),length=D(shape,"Length"),x=D(shape,"X");
            var events=doc.Elements.Where(n=>n.Kind!="participant" && n.Kind!="interaction" && n.Kind!="execution").OrderBy(n=>y[n.Id]).ToArray();
            Func<string,double,SequenceElement> endpoint=(role,at)=>{
                var candidates=doc.Elements.Where(n=>n.Kind=="message" && n.Links.ContainsKey(role) && n.Links[role].Contains(barId))
                    .Where(n=>Math.Abs(y[n.Id]-at)<=10.0).OrderBy(n=>Math.Abs(y[n.Id]-at)).ToArray();
                if(candidates.Length>1 && Math.Abs(Math.Abs(y[candidates[0].Id]-at)-Math.Abs(y[candidates[1].Id]-at))<0.00001)return null;
                return candidates.FirstOrDefault();
            };
            var trigger=endpoint("receiveExecution",top);var origin=trigger==null?endpoint("sendExecution",top):null;
            double start=trigger!=null?y[trigger.Id]:origin!=null?y[origin.Id]:top;
            var closer=endpoint("sendExecution",top+length);
            double end=closer==null?top+length:y[closer.Id];
            var preceding=trigger??events.LastOrDefault(n=>y[n.Id]<start);
            var following=events.FirstOrDefault(n=>y[n.Id]>end);
            item.Parent=trigger!=null?trigger.Parent:origin!=null?origin.Parent:containerAt(x,start);
            item.Links["startAfter"]=preceding==null?new string[0]:new[]{preceding.Id};
            item.Links["endBefore"]=following==null?new string[0]:new[]{following.Id};
            item.Links["endContainer"]=new[]{closer!=null?closer.Parent:containerAt(x,end)};
            string lane=item.Links["participant"].Single();
            var parent=bars.Values.Where(p=>V(p,"ModelId")!=barId && byId[V(p,"ModelId")].Links["participant"].Single()==lane
                    && D(p,"Y")<=top+1.0 && D(p,"Y")+D(p,"Length")>=top+length-1.0
                    && (Math.Abs(D(p,"Y")-top)>1.0 || Math.Abs(D(p,"Length")-length)>1.0 || D(p,"X")<x))
                .OrderBy(p=>D(p,"Length")).ThenByDescending(p=>D(p,"X")).FirstOrDefault();
            if(parent!=null)item.Links["outer"]=new[]{V(parent,"ModelId")};
        }
        foreach(var group in doc.Elements.Where(e=>e.Parent!=null).GroupBy(e=>e.Parent))
        {
            int order=0;
            foreach(var e in group.OrderBy(e=>e.Kind=="participant"?0:1).ThenBy(e=>y[e.Id]).ThenBy(e=>e.Id,StringComparer.Ordinal))e.Order=order++;
        }
        doc.SettleExecutions(n=>y[n.Id]);
        doc.Validate();return doc;
    }

    static string N(SequenceJson n,string key,string fallback)
    {
        if(n==null || n[key]==null)return fallback;
        return D(n,key).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
    }
    // SequenceStructureTrial.Read over the JSON. A relation's field is its metaclass here.
    public static SequenceTrialState StateOf(string json)
    {
        var data=SequenceJson.Parse(json);
        var state=new SequenceTrialState();
        var entities=data["Entities"].Items.ToDictionary(e=>V(e,"Id"));
        var relations=data["Relations"].Items.ToArray();
        var owners=relations.Where(r=>V(r,"RelationType")=="Embed").GroupBy(r=>V(r,"TargetId")).ToDictionary(g=>g.Key,g=>V(g.First(),"SourceId"));
        foreach(var e in entities.Values)
        {
            string owner;if(!owners.TryGetValue(V(e,"Id"),out owner))owner="";
            state.Models[V(e,"Id")]=PumlBuild.Json(new[]{V(e,"MetamodelId"),V(e,"Name")??"",owner,"False"});
        }
        foreach(var r in relations)
        {
            string id=V(r,"Id");
            state.Relations[id]=new[]{V(r,"SourceId"),V(r,"TargetId"),r["SourceIndex"]==null?"0":r["SourceIndex"].Raw,r["TargetIndex"]==null?"0":r["TargetIndex"].Raw};
            state.RelationFields[id]=V(r,"MetamodelId");
        }
        Func<string,string,string> sourceOf=(type,target)=>relations.Where(r=>V(r,"MetamodelId")==P+type && V(r,"TargetId")==target).Select(r=>V(r,"SourceId")).FirstOrDefault();
        foreach(var m in entities.Values.Where(e=>V(e,"EntityType")=="Message"))
        {
            string id=V(m,"Id"),send=sourceOf("SendMessage",id),receive=sourceOf("ReceiveMessage",id);
            Func<string,string> lane=port=>port==null?"":sourceOf("OwnedExecutionSpecification",port)??"";
            string sort=m["Fields"]==null || m["Fields"]["MessageSort"]==null?"":V(m["Fields"],"MessageSort").ToLowerInvariant();
            if(relations.Any(r=>V(r,"MetamodelId")==P+"DestroyMessage" && V(r,"TargetId")==id))sort="destroy";
            state.Ports[id]=new[]{send??"",receive??"",lane(send),lane(receive),sort};
        }
        var view=data["Editors"].Items.Single();
        foreach(var property in view.Properties)
        {
            if(property.Value==null || property.Value.Items==null)continue;
            foreach(var sh in property.Value.Items)
            {
                string id=V(sh,"Id"),model=V(sh,"ModelId");
                if(id==null || model==null)continue;
                var rows=new List<string>();string tail="";
                switch(property.Key)
                {
                    case "Messages":
                        rows.AddRange(new[]{entities.ContainsKey(model)?V(entities[model],"Name")??"":"",N(sh,"SourceY","0"),N(sh,"TargetY","0"),N(sh,"SelfloopBendsX","0")});break;
                    case "Operands":
                    {
                        string guard=entities.ContainsKey(model) && entities[model]["Fields"]!=null?V(entities[model]["Fields"],"Guard")??"":"";
                        state.Shapes[id]=PumlBuild.Json(new string[0])+PumlBuild.Json(new[]{guard,N(sh,"Position","0")});state.ShapeModels[id]=model;
                        continue;
                    }
                    case "Lifelines":
                        rows.AddRange(new[]{N(sh,"X","0"),"0",N(sh,"Width","100"),"40"});tail=N(sh,"LaneLength","0");break;
                    case "ExecutionSpecifications":
                        rows.AddRange(new[]{N(sh,"X","0"),N(sh,"Y","0"),N(sh,"Width","16"),N(sh,"Height","0"),N(sh,"Length","0")});break;
                    case "Fragments":
                    {
                        rows.AddRange(new[]{N(sh,"X","0"),N(sh,"Y","0"),N(sh,"Width","0"),N(sh,"Height","0")});
                        var e=entities.ContainsKey(model)?entities[model]:null;
                        string op=e==null || e["Fields"]==null?"":(V(e["Fields"],"Operator")??"").ToLowerInvariant();
                        tail=op=="group"?V(e,"Name")??"":op;break;
                    }
                    case "Notes":case "InteractionUses":
                        rows.AddRange(new[]{N(sh,"X","0"),N(sh,"Y","0"),N(sh,"Width","0"),N(sh,"Height","0")});
                        tail=entities.ContainsKey(model)?V(entities[model],"Name")??"":"";break;
                    case "Destructions":case "MessageEnds":
                        rows.AddRange(new[]{N(sh,"X","0"),N(sh,"Y","0"),N(sh,"Width","0"),N(sh,"Height","0")});break;
                    default:continue;
                }
                state.Shapes[id]=PumlBuild.Json(rows.ToArray())+tail;state.ShapeModels[id]=model;
            }
        }
        return state;
    }

    // What the runtime does with a package: import, take relations off, delete, import the
    // editor again. Relation order follows what was measured on the product: a new relation
    // goes last on both ends, one that changes its source leaves a gap that closes and goes
    // last at the new source, and a removed relation closes the gap on its source side.
    static int I(SequenceJson r,string key){return r[key]==null?0:int.Parse(r[key].Raw,System.Globalization.CultureInfo.InvariantCulture);}
    static void SetIndex(SequenceJson r,string key,int value){r.Properties[key]=SequenceJson.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture));}
    static void Close(List<SequenceJson> list,SequenceJson gone)
    {
        foreach(var peer in list.Where(r=>r!=gone && V(r,"SourceId")==V(gone,"SourceId") && V(r,"MetamodelId")==V(gone,"MetamodelId") && I(r,"SourceIndex")>I(gone,"SourceIndex")))
            SetIndex(peer,"SourceIndex",I(peer,"SourceIndex")-1);
    }
    // A removed relation also closes up the target side, where that side keeps an order.
    static void CloseBoth(List<SequenceJson> list,SequenceJson gone)
    {
        Close(list,gone);
        if(I(gone,"TargetIndex")<0)return;
        foreach(var peer in list.Where(r=>r!=gone && V(r,"TargetId")==V(gone,"TargetId") && V(r,"MetamodelId")==V(gone,"MetamodelId") && I(r,"TargetIndex")>I(gone,"TargetIndex")))
            SetIndex(peer,"TargetIndex",I(peer,"TargetIndex")-1);
    }
    public static string[] Apply(string json,SequenceStructurePreparation prepared)
    {
        var data=SequenceJson.Parse(json);
        Action<string> import=patchJson=>{
            var patch=SequenceJson.Parse(patchJson);
            if(patch["Entities"]!=null)
                foreach(var item in patch["Entities"].Items)
                {
                    var list=data["Entities"].Items;
                    int at=list.FindIndex(x=>V(x,"Id")==V(item,"Id"));
                    if(at>=0)list[at]=SequenceJson.Parse(item.ToJsonString());else list.Add(SequenceJson.Parse(item.ToJsonString()));
                }
            if(patch["Relations"]!=null)
                foreach(var item in patch["Relations"].Items)
                {
                    var list=data["Relations"].Items;
                    var copy=SequenceJson.Parse(item.ToJsonString());
                    var old=list.FirstOrDefault(x=>V(x,"Id")==V(item,"Id"));
                    Func<string,string,int> count=(key,value)=>list.Count(r=>r!=old && V(r,key)==value && V(r,"MetamodelId")==V(copy,"MetamodelId"));
                    if(old==null)
                    {
                        if(copy["SourceIndex"]==null)SetIndex(copy,"SourceIndex",count("SourceId",V(copy,"SourceId")));
                        if(copy["TargetIndex"]==null)SetIndex(copy,"TargetIndex",count("TargetId",V(copy,"TargetId")));
                        list.Add(copy);continue;
                    }
                    if(V(old,"SourceId")!=V(copy,"SourceId"))
                    {
                        Close(list,old);
                        int insertion=copy["SourceIndex"]==null?count("SourceId",V(copy,"SourceId")):I(copy,"SourceIndex");
                        foreach(var peer in list.Where(r=>r!=old && V(r,"SourceId")==V(copy,"SourceId") && V(r,"MetamodelId")==V(copy,"MetamodelId") && I(r,"SourceIndex")>=insertion))
                            SetIndex(peer,"SourceIndex",I(peer,"SourceIndex")+1);
                        SetIndex(copy,"SourceIndex",insertion);
                    }
                    else SetIndex(copy,"SourceIndex",I(old,"SourceIndex"));
                    if(V(old,"TargetId")!=V(copy,"TargetId"))SetIndex(copy,"TargetIndex",count("TargetId",V(copy,"TargetId")));
                    else SetIndex(copy,"TargetIndex",I(old,"TargetIndex"));
                    list[list.IndexOf(old)]=copy;
                }
            if(patch["Editors"]!=null)
                foreach(var editor in patch["Editors"].Items)
                {
                    int at=data["Editors"].Items.FindIndex(x=>V(x,"Id")==V(editor,"Id"));
                    Require(at>=0,"インポートしたエディタが元の図にありません。");
                    data["Editors"].Items[at]=SequenceJson.Parse(editor.ToJsonString());
                }
        };
        import(prepared.ReconnectJson);
        foreach(var unrelate in prepared.UnrelateIds)
        {
            var gone=data["Relations"].Items.FirstOrDefault(r=>V(r,"Id")==unrelate);
            Require(gone!=null,"外す関連がありません: "+unrelate);
            CloseBoth(data["Relations"].Items,gone);data["Relations"].Items.Remove(gone);
        }
        string connected=data.ToJsonString();
        var removed=new HashSet<string>(prepared.DeleteIds.Concat(prepared.DeleteParticipantIds).Concat(prepared.DeleteMessageIds)
            .Concat(prepared.DeleteFrameIds).Concat(prepared.DeleteNoteIds).Concat(prepared.DeleteRefIds).Concat(prepared.DeleteDestroyIds).Concat(prepared.DeleteEndIds));
        // IModel.Delete takes what the model owns with it, and every relation touching any of them.
        bool grew=true;
        while(grew)
        {
            grew=false;
            foreach(var r in data["Relations"].Items)
                if(V(r,"RelationType")=="Embed" && removed.Contains(V(r,"SourceId")) && removed.Add(V(r,"TargetId")))grew=true;
        }
        data["Entities"].Items.RemoveAll(e=>removed.Contains(V(e,"Id")));
        foreach(var gone in data["Relations"].Items.Where(r=>removed.Contains(V(r,"SourceId")) || removed.Contains(V(r,"TargetId"))).ToArray())
        {CloseBoth(data["Relations"].Items,gone);data["Relations"].Items.Remove(gone);}
        if(removed.Count>0)import(prepared.EditorAfterDeleteJson);
        foreach(var view in data["Editors"].Items)
            foreach(var property in view.Properties.Values.Where(p=>p!=null && p.Items!=null))
                Require(!property.Items.Any(sh=>removed.Contains(V(sh,"ModelId"))),"削除したモデルの図形が残っています。");
        return new[]{connected,data.ToJsonString()};
    }
    static string Compare(SequenceTrialState expected,SequenceTrialState actual,string phase)
    {
        if(expected.Signature()==actual.Signature())return null;
        var ports=expected.Ports.Keys.Union(actual.Ports.Keys).Where(k=>!expected.Ports.ContainsKey(k) || !actual.Ports.ContainsKey(k)
            || PumlBuild.Json(expected.Ports[k])!=PumlBuild.Json(actual.Ports[k]))
            .Select(k=>k+" "+(expected.Ports.ContainsKey(k)?PumlBuild.Json(expected.Ports[k]):"-")+" / "+(actual.Ports.ContainsKey(k)?PumlBuild.Json(actual.Ports[k]):"-"));
        var models=expected.Models.Keys.Union(actual.Models.Keys).Where(k=>!expected.Models.ContainsKey(k) || !actual.Models.ContainsKey(k) || expected.Models[k]!=actual.Models[k])
            .Select(k=>k+" "+(expected.Models.ContainsKey(k)?expected.Models[k]:"-")+" / "+(actual.Models.ContainsKey(k)?actual.Models[k]:"-"));
        return string.Join(Environment.NewLine,new[]{phase+"の照合が不一致: "+expected.DifferenceCounts(actual),expected.RelationDifferences(actual),
            expected.ShapeDifferences(actual),"送受信: "+string.Join(" ",ports),"モデル: "+string.Join(" ",models)});
    }

    // Next Design refuses every edit to a diagram whose bar goes on after a reply leaves it.
    public static string Editable(string json)
    {
        var data=SequenceJson.Parse(json);
        var relations=data["Relations"].Items.ToArray();
        var wires=data["Editors"].Items.Single()["Messages"];
        if(wires==null || wires.Items==null)return null;
        var y=wires.Items.ToDictionary(w=>V(w,"ModelId"),w=>D(w,"SourceY"));
        Func<string,string,string> port=(kind,message)=>relations.Where(r=>V(r,"MetamodelId")==P+kind && V(r,"TargetId")==message).Select(r=>V(r,"SourceId")).FirstOrDefault();
        var entities=data["Entities"].Items.ToDictionary(e=>V(e,"Id"));
        // Next Design fails laying out a bar no message uses.
        foreach(var bar in entities.Values.Where(e=>V(e,"EntityType")=="ExecutionSpecification"))
            if(!relations.Any(r=>(V(r,"MetamodelId")==P+"SendMessage" || V(r,"MetamodelId")==P+"ReceiveMessage") && V(r,"SourceId")==V(bar,"Id")))
                return "メッセージのないバーがあります: "+V(bar,"Id");
        foreach(var reply in y.Keys.Where(id=>entities.ContainsKey(id) && entities[id]["Fields"]!=null && (V(entities[id]["Fields"],"MessageSort")??"").ToLowerInvariant()=="reply"))
        {
            string bar=port("SendMessage",reply);
            if(bar==null)continue;
            var later=y.Keys.Where(m=>y[m]>y[reply] && (port("SendMessage",m)==bar || port("ReceiveMessage",m)==bar)).ToArray();
            if(later.Length>0)return "応答 "+V(entities[reply],"Name")+" の後にも同じバーを使うメッセージがあります: "+string.Join(",",later.Select(m=>V(entities[m],"Name")));
        }
        return null;
    }
    // What Next Design makes of a diagram once anything on it is moved (measured on the device,
    // 0.10.11): a bar starts at its first message; a bar a reply ends stops at that reply; any
    // other bar stops 20 under the lower of its last message and the bars its calls opened (a
    // bar with one receive is 20 long). The reading must not depend on the generator's margins.
    public static string Settle(string json)
    {
        var data=SequenceJson.Parse(json);
        var relations=data["Relations"].Items.ToArray();
        var view=data["Editors"].Items.Single();
        if(view["ExecutionSpecifications"]==null || view["Messages"]==null)return json;
        var wires=view["Messages"].Items.ToDictionary(w=>V(w,"ModelId"));
        var entities=data["Entities"].Items.ToDictionary(e=>V(e,"Id"));
        var shapes=view["ExecutionSpecifications"].Items.ToDictionary(b=>V(b,"ModelId"));
        Func<string,bool> isReply=id=>entities[id]["Fields"]!=null && (V(entities[id]["Fields"],"MessageSort")??"").ToLowerInvariant()=="reply";
        Func<string,string,string> port=(kind,message)=>relations.Where(r=>V(r,"MetamodelId")==P+kind && V(r,"TargetId")==message).Select(r=>V(r,"SourceId")).FirstOrDefault();
        var uses=shapes.Keys.ToDictionary(id=>id,id=>relations.Where(r=>(V(r,"MetamodelId")==P+"SendMessage" || V(r,"MetamodelId")==P+"ReceiveMessage") && V(r,"SourceId")==id && wires.ContainsKey(V(r,"TargetId")))
            .Select(r=>new{Send=V(r,"MetamodelId")==P+"SendMessage",Id=V(r,"TargetId"),At=V(r,"MetamodelId")==P+"SendMessage"?D(wires[V(r,"TargetId")],"SourceY"):D(wires[V(r,"TargetId")],"TargetY")})
            .OrderBy(u=>u.At).ToArray());
        // A lane's destruction ends its bars still open there (assumed as the generator and the
        // reading do; not measured), and such an end does not carry up to the calling bar.
        Func<string,string> laneOf=bar=>relations.Where(r=>V(r,"MetamodelId")==P+"OwnedExecutionSpecification" && V(r,"TargetId")==bar).Select(r=>V(r,"SourceId")).FirstOrDefault();
        var destroyed=(view["Destructions"]==null?new SequenceJson[0]:view["Destructions"].Items.ToArray())
            .Select(d=>new{Lane=relations.Where(r=>V(r,"MetamodelId")==P+"DestructionTargetLifeline" && V(r,"SourceId")==V(d,"ModelId")).Select(r=>V(r,"TargetId")).FirstOrDefault(),Y=D(d,"Y")}).ToList();
        var bottoms=new Dictionary<string,Tuple<double,bool>>();
        Func<string,int,Tuple<double,bool>> bottom=null;
        bottom=(id,depth)=>{
            Tuple<double,bool> known;if(bottoms.TryGetValue(id,out known))return known;
            var list=uses[id];var last=list.Last();
            Tuple<double,bool> end;
            if(last.Send && isReply(last.Id))end=Tuple.Create(last.At,false);
            else
            {
                double point=last.At,reach=last.At;
                foreach(var u in list.Where(u=>u.Send && !isReply(u.Id)))
                {
                    string opened=port("ReceiveMessage",u.Id);
                    if(opened==null || opened==id || !uses.ContainsKey(opened) || uses[opened].Length==0 || uses[opened][0].Id!=u.Id || depth>=64)continue;
                    var theirs=bottom(opened,depth+1);
                    if(!theirs.Item2)reach=Math.Max(reach,theirs.Item1);
                }
                end=Tuple.Create(Math.Max(point,reach)+20,false);
                string lane=laneOf(id);
                var destroy=destroyed.Where(d=>d.Lane==lane && d.Y>point).OrderBy(d=>d.Y).FirstOrDefault();
                if(destroy!=null && !uses.Any(o=>o.Key!=id && laneOf(o.Key)==lane && o.Value.Length>0 && o.Value[0].At>point && o.Value[0].At<destroy.Y))
                    end=Tuple.Create(destroy.Y,true);
            }
            bottoms[id]=end;return end;
        };
        foreach(var pair in shapes)
        {
            if(uses[pair.Key].Length==0)continue;
            double top=uses[pair.Key][0].At,end=bottom(pair.Key,0).Item1;
            string length=(end-top).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            pair.Value.Properties["Y"]=SequenceJson.Parse(top.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            pair.Value.Properties["Length"]=SequenceJson.Parse(length);pair.Value.Properties["Height"]=SequenceJson.Parse(length);
        }
        return data.ToJsonString();
    }
    // One scenario end to end. Returns null when the diagram reads back as the input and a
    // second pass finds nothing to do; otherwise what went wrong.
    public static string Run(string beforePuml,string afterPuml,bool settled=false)
    {
        string stage="出力";
        try
        {
            string before=Export(beforePuml);
            if(settled && beforePuml.Contains("participant "))
            {
                // The generator already draws bars the way the product tidies them.
                Func<string,string> barsOf=json=>{var v=SequenceJson.Parse(json)["Editors"].Items.Single()["ExecutionSpecifications"];
                    return v==null?"":string.Join(";",v.Items.Select(x=>V(x,"ModelId")+":"+D(x,"Y")+"+"+D(x,"Length")).OrderBy(t=>t,StringComparer.Ordinal));};
                string drawnBars=barsOf(before);
                before=Settle(before);
                if(barsOf(before)!=drawnBars)return "生成したバーが製品の整え方と違う: "+drawnBars+" / "+barsOf(before);
                stage="整えた図の読取り";
                var drawn=SequenceDocument.Parse(beforePuml);
                foreach(var e in drawn.Elements.Where(e=>e.Kind=="ref"))e.Attributes["reference"]="";
                var same=SequenceNotePolicy.Build(Read(before),drawn,()=>"sim-check");
                if(same.Changes.Count>0)return "製品が整えた図が入力と違って読める: "+string.Join(", ",same.Changes.Select(c=>c.Kind+" "+c.Action+" L"+c.Line))+"\n"+SequenceAudit.Reasons(Read(before),SequenceDocument.Parse(beforePuml),same).Replace("\f","\n");
            }
            stage="読取り";
            var current=Read(before);
            var desired=SequenceDocument.Parse(afterPuml);
            // The runtime resolves each ref against the project; here nothing is there to find.
            foreach(var e in desired.Elements.Where(e=>e.Kind=="ref"))e.Attributes["reference"]="";
            int serial=0;Func<string> newId=()=>"sim-new-"+(serial++);
            var plan=SequenceNotePolicy.Build(current,desired,newId);
            if(plan.Changes.Count==0)return "差分がありません";
            stage="事前判定";
            var gate=SequenceStructurePreflight.Check(current,plan);
            if(!gate.CanCommit())return "対象外: "+string.Join(" / ",gate.Reasons.Distinct());
            stage="準備";
            SequenceStructurePreparation.DestroyTypes=DestroyTypes();
            // As the runtime resolves them: the MessageEnd type only when the update needs free ends.
            var baseTypes=BaseTypes();
            if(!gate.NeedsFreeEnds(plan)){baseTypes.MessageEnd=null;baseTypes.OwnsMessageEnd=null;baseTypes.SendFromEnd=null;baseTypes.ReceiveFromEnd=null;}
            SequenceStructurePreparation.BaseTypes=baseTypes;
            SequenceStructurePreparation.SortLiterals.Clear();
            SequenceStructurePreparation.SortLiterals["sync"]="Sync";SequenceStructurePreparation.SortLiterals["async"]="Async";SequenceStructurePreparation.SortLiterals["reply"]="Reply";
            var prepared=SequenceStructurePreparation.Build(before,EditorId(before),current,plan,FrameTypes(),NoteTypes(),RefTypes());
            stage="適用";
            var applied=Apply(before,prepared);string after=applied[1];
            stage="照合";
            var stateBefore=StateOf(before);
            var connectedState=StateOf(applied[0]);var finalState=StateOf(after);
            var expectConnected=stateBefore.Expected(prepared,plan,false);expectConnected.Loosen(prepared.LooseShapeIds,connectedState);
            var expectFinal=stateBefore.Expected(prepared,plan,true);expectFinal.Loosen(prepared.LooseShapeIds,finalState);
            string mismatch=Compare(expectConnected,connectedState,"接続変更後")??Compare(expectFinal,finalState,"削除後");
            if(mismatch!=null)return mismatch;
            stage="読み直し";
            // What the update draws is already what the product makes of it on the next edit.
            {
                Func<string,string> barsOf=json=>{var v=SequenceJson.Parse(json)["Editors"].Items.Single()["ExecutionSpecifications"];
                    return v==null?"":string.Join(";",v.Items.Select(x=>D(x,"Y")+"+"+D(x,"Length")).OrderBy(t=>t,StringComparer.Ordinal));};
                if(barsOf(after)!=barsOf(Settle(after)))return "反映したバーが製品の整え方と違う: "+barsOf(after)+" / "+barsOf(Settle(after));
            }
            string invalid=Editable(after);
            if(invalid!=null)return "製品が編集を拒否する形: "+invalid;
            var read=Read(after);
            var again=SequenceNotePolicy.Build(read,desired,newId);
            if(again.Changes.Count>0)
                return "反映後の差分 "+again.Changes.Count+"件: "+string.Join(", ",again.Changes.Select(c=>c.Kind+" "+c.Action))
                    +" / "+string.Join(" ; ",again.Changes.Where(c=>c.Kind=="execution").Select(c=>{
                        var want=again.Expected.Elements.First(e=>e.Id==c.Id);var got=read.Elements.First(e=>e.Id==c.Id);
                        Func<SequenceElement,string> show=e=>string.Join(" ",e.Links.OrderBy(p=>p.Key).Select(p=>p.Key+"="+string.Join(",",p.Value.Select(v=>{var x=read.Elements.FirstOrDefault(r=>r.Id==v);return x==null?v:x.Kind+":"+x.Text;}))));
                        return "期待 "+show(want)+" / 実測 "+show(got);}))
                    +"\n"+SequenceAudit.Reasons(read,desired,again).Replace("\f","\n");
            return null;
        }
        catch(Exception ex){return stage+"で停止: "+ex.Message;}
    }
}

public static class SimulatorTests
{
    public static void Run(string samples)
    {
        var failures=new List<string>();int passed=0;
        foreach(var line in new[]{"scenarios.txt","scenarios-smoke.txt","scenarios-sim.txt"}.Where(f=>File.Exists(Path.Combine(samples,f)))
            .SelectMany(f=>File.ReadAllLines(Path.Combine(samples,f))).Select(l=>l.Trim()).Where(l=>l.Length>0 && !l.StartsWith("#")).Distinct())
        {
            var cells=line.Split('|').Select(c=>c.Trim()).ToArray();
            foreach(bool settled in new[]{false,true})
            {
                string result=SequenceSimulator.Run(File.ReadAllText(Path.Combine(samples,cells[1])),File.ReadAllText(Path.Combine(samples,cells[2])),settled);
                if(result==null)passed++;else failures.Add((settled?"[整えた図] ":"")+cells[0]+" ("+cells[1]+" → "+cells[2]+"): "+result);
            }
        }
        Console.WriteLine("Simulated scenarios: "+passed+" passed, "+failures.Count+" failed");
        foreach(var f in failures)Console.WriteLine("  SIM FAIL "+f);
        if(failures.Count>0)throw new Exception("simulated scenarios failed: "+failures.Count);
    }
}
