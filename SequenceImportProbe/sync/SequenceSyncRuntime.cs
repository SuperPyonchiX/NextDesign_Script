// Native reader for the semantic planner. It performs no project mutations.
public sealed class DiagramSnapshot
{
    public SequenceDocument Document=new SequenceDocument{HasTitle=true};
    public Dictionary<string,double> Y=new Dictionary<string,double>();
    public Dictionary<string,string> ShapeIds=new Dictionary<string,string>();
    public Dictionary<string,object> Geometry=new Dictionary<string,object>();
    public List<string> Limitations=new List<string>();
    // Where each bar starts and ends against the messages on it, in the diagram's own numbers,
    // to measure how Next Design lays bars out once the diagram is edited by hand. Lanes and
    // messages are numbered, never named.
    public string BarTimeline()
    {
        var lanes=Document.Elements.Where(e=>e.Kind=="participant").OrderBy(e=>e.Order).Select(e=>e.Id).ToList();
        var messages=Document.Elements.Where(e=>e.Kind=="message" && Geometry.ContainsKey(e.Id)).OrderBy(e=>Y[e.Id]).ToList();
        Func<string,string,double> at=(id,key)=>Convert.ToDouble(((Dictionary<string,object>)Geometry[id])[key],System.Globalization.CultureInfo.InvariantCulture);
        Func<string[],string> lane=l=>l==null || l.Length==0?"外":"L"+lanes.IndexOf(l[0]);
        var rows=new List<Tuple<double,int,string>>();
        for(int i=0;i<messages.Count;i++)
        {
            var m=messages[i];string[] from,to;m.Links.TryGetValue("sender",out from);m.Links.TryGetValue("receiver",out to);
            string sort;m.Attributes.TryGetValue("sort",out sort);
            double source=at(m.Id,"SourceY"),target=at(m.Id,"TargetY");
            rows.Add(Tuple.Create(source,1,"M"+i+" "+sort+" "+lane(from)+"→"+lane(to)+(Math.Abs(target-source)>1e-6?" 受信Y="+target.ToString("0.#"):"")));
        }
        var bars=Document.Elements.Where(e=>e.Kind=="execution" && Geometry.ContainsKey(e.Id)).OrderBy(e=>at(e.Id,"Y")).ToList();
        for(int i=0;i<bars.Count;i++)
        {
            var b=bars[i];string[] owner;b.Links.TryGetValue("participant",out owner);
            var uses=new List<string>();
            for(int k=0;k<messages.Count;k++)
            {
                string[] v;
                if(messages[k].Links.TryGetValue("sendExecution",out v) && v.Contains(b.Id))uses.Add("送M"+k);
                if(messages[k].Links.TryGetValue("receiveExecution",out v) && v.Contains(b.Id))uses.Add("受M"+k);
            }
            rows.Add(Tuple.Create(at(b.Id,"Y"),0,"┌E"+i+" "+lane(owner)+" X="+at(b.Id,"X").ToString("0.#")+" ["+string.Join(",",uses)+"]"));
            rows.Add(Tuple.Create(at(b.Id,"Y")+at(b.Id,"Height"),2,"└E"+i+" 長さ="+at(b.Id,"Height").ToString("0.#")));
        }
        return "実行区間とメッセージの縦位置（Y順）\n"+string.Join("\n",rows.OrderBy(r=>r.Item1).ThenBy(r=>r.Item2).Select(r=>r.Item1.ToString("0.#").PadLeft(7)+"  "+r.Item3));
    }
    public static DiagramSnapshot Read(ISequenceDiagram diagram, StringBuilder log)
    {
        var root=diagram.Model as IInteraction;
        if(root==null)throw new InvalidOperationException("S210: シーケンス図を開いてください。");
        var snapshot=new DiagramSnapshot();var doc=snapshot.Document;
        doc.Elements.Add(new SequenceElement{Id=root.Id,Kind="interaction",Text=root.Name});
        Action<ISequenceShape,string,string,double> add=(shape,kind,text,y)=>{
            if(shape.Model==null || shape.Model.IsDeleted)throw new InvalidOperationException("S210: モデルのない図形があります。");
            if(snapshot.ShapeIds.ContainsKey(shape.ModelId))throw new InvalidOperationException("S210: 同じモデルの図形が複数あります。");
            doc.Elements.Add(new SequenceElement{Id=shape.ModelId,Kind=kind,Text=text??"",Parent=root.Id});
            snapshot.ShapeIds.Add(shape.ModelId,shape.Id);snapshot.Y.Add(shape.ModelId,y);
            var node=shape as ISequenceNodeShape;
            if(node!=null)snapshot.Geometry[shape.ModelId]=PumlBuild.Obj("X",node.LocationX,"Y",node.LocationY,"Width",node.Width,"Height",node.Height);
            var message=shape as IMessageShape;
            if(message!=null)snapshot.Geometry[shape.ModelId]=PumlBuild.Obj("SourceY",message.SourceY,"TargetY",message.TargetY,"SelfloopBendsX",message.SelfloopBendsX);
        };
        foreach(var l in diagram.Lifelines.OrderBy(l=>l.LocationX).ThenBy(l=>l.Id,StringComparer.Ordinal))
            add(l,"participant",l.Text,l.LocationX);
        var fragmentRegions=new List<SequenceRegion>();var operandRegions=new List<SequenceRegion>();var annotationRegions=new List<SequenceRegion>();
        foreach(var f in diagram.Fragments)
        {
            add(f,"fragment","",f.LocationY);
            var element=doc.Elements.Last();
            string op=Convert.ToString(f.Model.GetField("Operator")).ToLowerInvariant();element.Attributes["operator"]=op;
            if(op=="group")element.Text=f.Model.Name;
            fragmentRegions.Add(new SequenceRegion{Id=f.ModelId,X=f.LocationX,Y=f.LocationY,Width=f.Width,Height=f.Height});
            var operands=f.Operands.OrderBy(o=>o.Position).ToArray();
            // Match the exporter convention; the SDK documents Position as an absolute Y.
            bool absolute=operands.All(o=>o.Position>=f.LocationY-0.00001 && o.Position<=f.LocationY+f.Height+0.00001);
            var positions=operands.Select(o=>absolute?(double)o.Position:f.LocationY+o.Position).ToArray();
            log.AppendLine("Fragment bounds: id="+f.ModelId+" x="+f.LocationX+" y="+f.LocationY+" width="+f.Width+" height="+f.Height+" operandPosition="+(absolute?"absolute":"relative"));
            if(!absolute)snapshot.Limitations.Add("オペランドPositionを相対座標として解釈（出力側と同じ規則）: "+f.ModelId);
            for(int i=0;i<operands.Length;i++)
            {
                var operand=operands[i];double top=positions[i],bottom=i+1<positions.Length?positions[i+1]:f.LocationY+f.Height;
                add(operand,"operand",i>0 && string.Equals(SequenceLabels.Fold(operand.Guard),"else",StringComparison.OrdinalIgnoreCase)?"":operand.Guard,i==0?f.LocationY:top);doc.Elements.Last().Parent=f.ModelId;
                log.AppendLine("Operand bounds: id="+operand.ModelId+" fragment="+f.ModelId+" top="+top+" bottom="+bottom+" rawPosition="+operand.Position);
                if(top<f.LocationY-0.00001 || bottom>f.LocationY+f.Height+0.00001 || bottom<=top)
                    throw new InvalidOperationException("S210: オペランドの境界が不正です: "+operand.ModelId);
                operandRegions.Add(new SequenceRegion{Id=operand.ModelId,Fragment=f.ModelId,X=f.LocationX,Y=top,Width=f.Width,Height=bottom-top});
            }
        }
        foreach(var e in diagram.ExecutionSpecifications)
        {
            add(e,"execution","",e.LocationY);
            if(e.Lifeline==null)throw new InvalidOperationException("S210: 実行区間のライフラインがありません。");
            doc.Elements.Last().Links["participant"]=new[]{e.Lifeline.ModelId};
        }
        foreach(var m in SequenceExportMatch.Order(diagram.Messages,m=>m.SourceY,m=>m.SendPort is ISequenceNodeShape?((ISequenceNodeShape)m.SendPort).LocationX:0,m=>m.Id))
        {
            add(m,"message",m.Text,m.SourceY);var e=doc.Elements.Last();var model=m.Model as IMessage;
            if(model==null)throw new InvalidOperationException("S210: メッセージの型が不正です。");
            e.Attributes["sort"]=model.Kind;
            // A message a destruction points back at is the one that destroys the lane, whatever
            // sort the profile records for it; the parser reads it the same way.
            if(model.GetRelationsWhere((r,f)=>r.Target!=null && r.Target.Id==model.Id && r.Metaclass!=null
                && r.Metaclass.Id==SequencePayload.Prefix+"DestroyMessage").Any())e.Attributes["sort"]="destroy";
            e.Links["sender"]=m.Sender==null?new string[0]:new[]{m.Sender.ModelId};
            e.Links["receiver"]=m.Receiver==null?new string[0]:new[]{m.Receiver.ModelId};
            if(m.SendPort is IExecutionSpecificationShape)e.Links["sendExecution"]=new[]{m.SendPort.ModelId};
            if(m.ReceivePort is IExecutionSpecificationShape)e.Links["receiveExecution"]=new[]{m.ReceivePort.ModelId};
        }
        foreach(var n in diagram.Notes)
        {
            add(n,"note",n.Text,n.LocationY);var e=doc.Elements.Last();var targets=new List<string>();
            annotationRegions.Add(new SequenceRegion{Id=n.ModelId,X=n.LocationX,Y=n.LocationY,Width=n.Width,Height=n.Height});
            foreach(var anchor in n.NoteAnchors)
            {
                var other=anchor.Source.Id==n.Id?anchor.Target:anchor.Source;
                var l=other as ILifelineShape;
                if(l!=null)targets.Add(l.ModelId);
                else { e.Links["anchors"]=n.NoteAnchors.Select(a=>a.Source.Id==n.Id?a.Target.ModelId:a.Source.ModelId).Distinct().ToArray();snapshot.Limitations.Add("Noteの非ライフライン接続: "+n.ModelId); }
            }
            e.Links["targets"]=targets.Distinct().OrderBy(id=>snapshot.Y[id]).ToArray();
            e.Attributes["position"]="over";
            if(targets.Count==0) {e.Attributes["position"]="free";snapshot.Limitations.Add("自由配置Note（近傍ライフラインには結び付けない）: "+n.ModelId);}
            else
            {
                var lines=diagram.Lifelines.Where(l=>targets.Contains(l.ModelId)).ToArray();
                if(n.LocationX+n.Width<lines.Min(l=>l.LocationX))e.Attributes["position"]="left of";
                else if(n.LocationX>lines.Max(l=>l.LocationX+l.Width))e.Attributes["position"]="right of";
            }
        }
        foreach(var u in diagram.InteractionUses)
        {
            add(u,"ref",u.Text,u.LocationY);var e=doc.Elements.Last();
            annotationRegions.Add(new SequenceRegion{Id=u.ModelId,X=u.LocationX,Y=u.LocationY,Width=u.Width,Height=u.Height});
            e.Links["targets"]=u.Lifelines.OrderBy(l=>l.LocationX).Select(l=>l.ModelId).ToArray();
            var model=u.Model as IInteractionUse;
            e.Attributes["reference"]=model==null || model.RefersTo==null?"":model.RefersTo.Id;
        }
        foreach(var d in diagram.Destructions)
        {
            add(d,"destroy","",d.LocationY);
            doc.Elements.Last().Links["participant"]=new[]{d.Lifeline.ModelId};
        }
        var byId=doc.Elements.ToDictionary(e=>e.Id);
        // Model ownership is not operand membership: use the actual structural relationships.
        var relations=SequenceMappedUpdate.Tree(root).SelectMany(m=>m.GetRelationsWhere((r,f)=>true)).GroupBy(r=>r.Id).Select(g=>g.First()).ToArray();
        var memberships=new List<SequenceMembership>();
        foreach(var relation in relations)
        {
            if(!byId.ContainsKey(relation.Source.Id) || !byId.ContainsKey(relation.Target.Id))continue;
            if(relation.Metaclass.Id==SequencePayload.Prefix+"NestedInteractionFragment" || relation.Metaclass.Id==SequencePayload.Prefix+"OperandTargetMessage")
            {
                memberships.Add(new SequenceMembership{Child=relation.Target.Id,Parent=relation.Source.Id,
                    Evidence=relation.Metaclass.Id+" / "+relation.Id});
            }
        }
        foreach(var operand in diagram.Fragments.SelectMany(f=>f.Operands))foreach(var message in operand.Messages)
        {
            memberships.Add(new SequenceMembership{Child=message.ModelId,Parent=operand.ModelId,Evidence="SDK operand.Messages"});
        }
        memberships.AddRange(SequenceRegion.Nesting(operandRegions,fragmentRegions.Concat(annotationRegions)));
        SequenceMembership.Resolve(doc,memberships,line=>log.AppendLine(line));

        Func<double,double,string> containerAt=(x,y)=>{
            var candidates=operandRegions.Where(r=>x>=r.X && x<=r.X+r.Width && y>=r.Y && y<r.Y+r.Height-1.0).ToArray();
            var nearest=candidates.Where(r=>!candidates.Any(inner=>inner.Id!=r.Id && SequenceRegion.Contains(r,inner))).ToArray();
            if(nearest.Length>1) {snapshot.Limitations.Add("実行区間境界の所属候補が複数");return root.Id;}
            return nearest.Length==1?nearest[0].Id:root.Id;
        };
        foreach(var e in diagram.ExecutionSpecifications)
        {
            var item=byId[e.ModelId];
            var events=doc.Elements.Where(n=>n.Kind!="participant" && n.Kind!="interaction" && n.Kind!="execution").OrderBy(n=>snapshot.Y[n.Id]).ToArray();
            // The exporter snaps interval ends to neighbouring messages within ten pixels.
            Func<string,double,SequenceElement> endpoint=(role,y)=>{
                var candidates=doc.Elements.Where(n=>n.Kind=="message" && n.Links.ContainsKey(role) && n.Links[role].Contains(e.ModelId))
                    .Where(n=>Math.Abs(snapshot.Y[n.Id]-y)<=10.0).OrderBy(n=>Math.Abs(snapshot.Y[n.Id]-y)).ToArray();
                if(candidates.Length>1 && Math.Abs(Math.Abs(snapshot.Y[candidates[0].Id]-y)-Math.Abs(snapshot.Y[candidates[1].Id]-y))<0.00001)return null;
                return candidates.FirstOrDefault();
            };
            var trigger=endpoint("receiveExecution",e.LocationY);var origin=trigger==null?endpoint("sendExecution",e.LocationY):null;
            double start=trigger!=null?snapshot.Y[trigger.Id]:origin!=null?snapshot.Y[origin.Id]:e.LocationY;
            var closer=endpoint("sendExecution",e.LocationY+e.Length);
            double end=closer==null?e.LocationY+e.Length:snapshot.Y[closer.Id];
            var preceding=trigger??events.LastOrDefault(n=>snapshot.Y[n.Id]<start);
            var following=events.FirstOrDefault(n=>snapshot.Y[n.Id]>end);
            item.Parent=trigger!=null?trigger.Parent:origin!=null?origin.Parent:containerAt(e.LocationX,start);
            item.Links["startAfter"]=preceding==null?new string[0]:new[]{preceding.Id};
            item.Links["endBefore"]=following==null?new string[0]:new[]{following.Id};
            item.Links["endContainer"]=new[]{closer!=null?closer.Parent:containerAt(e.LocationX,end)};
            var parent=diagram.ExecutionSpecifications.Where(p=>p.ModelId!=e.ModelId && p.Lifeline!=null && p.Lifeline.ModelId==e.Lifeline.ModelId
                && p.LocationY<=e.LocationY+1.0 && p.LocationY+p.Length>=e.LocationY+e.Length-1.0
                && (Math.Abs(p.LocationY-e.LocationY)>1.0 || Math.Abs(p.Length-e.Length)>1.0 || p.LocationX<e.LocationX))
                .OrderBy(p=>p.Length).ThenByDescending(p=>p.LocationX).FirstOrDefault();
            if(parent!=null)item.Links["outer"]=new[]{parent.ModelId};
        }
        foreach(var group in doc.Elements.Where(e=>e.Parent!=null).GroupBy(e=>e.Parent))
        {
            int order=0;foreach(var e in group.OrderBy(e=>e.Kind=="participant"?0:1).ThenBy(e=>snapshot.Y[e.Id]).ThenBy(e=>e.Id,StringComparer.Ordinal))e.Order=order++;
        }
        var represented=new HashSet<string>(doc.Elements.Select(e=>e.Id));
        foreach(var m in SequenceMappedUpdate.Tree(root).Where(m=>!represented.Contains(m.Id) && !(m is IFrame) && !(m is IMessageEnd)))
            snapshot.Limitations.Add("共通構造に未収録のモデル: "+m.Id+" / "+m.Metaclass.Id);
        // A read-only report deliberately exposes inference gaps before enabling writes.
        if(diagram.ExecutionSpecifications.Any())snapshot.Limitations.Add("実行区間の境界・分岐跨ぎはSDK読取りとPlantUMLの比較を実機照合してください。");
        doc.SettleExecutions(n=>snapshot.Y[n.Id]);
        doc.Validate();return snapshot;
    }
}

public static class SequenceSyncRuntime
{
    static string QualifiedName(IModel model)
    {
        var parts=new List<string>();var visited=new HashSet<string>();
        while(model!=null) {if(!visited.Add(model.Id))throw new InvalidOperationException("S210: モデルの所有関係が循環しています。");parts.Add(model.Name);model=model.Owner;}
        parts.Reverse();return string.Join("::",parts);
    }
    // What each ref of a new diagram refers to, the way the update resolves it: an interaction
    // of the project named by the ref's text, when exactly one is. Keyed by the input line.
    public static void ResolveReferences(IApplication app,IProject project,IModel near,IEnumerable<PumlNode> refs,Dictionary<int,string> into,StringBuilder log,bool ask)
    {
        var wanted=refs.ToArray();
        if(wanted.Length==0)return;
        var interactions=Interactions(project);
        foreach(var n in wanted)
        {
            string chosen=PickReference(app,project,near,n.Text,n.Line,SequenceReferenceResolver.Find(n.Text,interactions),log,ask);
            if(chosen!=null)into[n.Line]=chosen;
        }
    }
    // The exported editor leaves out a size or place a shape has by default (a lane or frame
    // drawn by hand comes without its Width). The update lays shapes out from those numbers,
    // so what is missing is taken from the diagram as the SDK reads it. Nothing is changed.
    internal static string CompleteGeometry(string exported,ISequenceDiagram diagram,StringBuilder log)
    {
        var data=SequenceJson.Parse(exported);
        var editor=data["Editors"]==null?null:data["Editors"].Items.FirstOrDefault(v=>v["Id"]!=null && v["Id"].StringValue()==diagram.Id);
        if(editor==null || editor.Properties==null)return exported;
        var nodes=diagram.Shapes.OfType<ISequenceNodeShape>().ToDictionary(n=>n.Id);
        Func<double,SequenceJson> number=v=>SequenceJson.Parse(v.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        int filled=0;
        foreach(var list in editor.Properties.Values.Where(v=>v!=null && v.Items!=null))
            foreach(var sh in list.Items.Where(i=>i!=null && i.Properties!=null && i["Id"]!=null && i["Id"].Raw.StartsWith("\"")))
            {
                ISequenceNodeShape node;
                if(!nodes.TryGetValue(sh["Id"].StringValue(),out node))continue;
                var wanted=new List<KeyValuePair<string,double>>{new KeyValuePair<string,double>("X",node.LocationX),new KeyValuePair<string,double>("Width",node.Width)};
                if(!(node is ILifelineShape))
                {
                    wanted.Add(new KeyValuePair<string,double>("Y",node.LocationY));
                    var bar=node as IExecutionSpecificationShape;
                    wanted.Add(new KeyValuePair<string,double>("Height",bar!=null?bar.Length:node.Height));
                    if(bar!=null)wanted.Add(new KeyValuePair<string,double>("Length",bar.Length));
                }
                foreach(var pair in wanted)
                    if(sh[pair.Key]==null){sh.Properties[pair.Key]=number(pair.Value);filled++;}
            }
        if(filled==0)return exported;
        log.AppendLine("Editor geometry completed from the SDK: "+filled+" values");
        return data.ToJsonString();
    }
    internal static SequenceReferenceCandidate[] Interactions(IProject project)
    {
        return SequenceMappedUpdate.Tree(project.DesignModel).OfType<IInteraction>()
            .Select(m=>new SequenceReferenceCandidate{Id=m.Id,Name=m.Name,Path=QualifiedName(m)}).ToArray();
    }
    // One interaction for a ref. Several of the same name are for the designer to tell apart:
    // they are offered one by one, the one nearest the diagram in the model tree first, and
    // declining them all leaves the ref without a target. The batch never asks.
    internal static string PickReference(IApplication app,IProject project,IModel near,string text,int line,SequenceReferenceCandidate[] matches,StringBuilder log,bool ask)
    {
        if(matches.Length==1){log.AppendLine("ref参照先 "+line+"行: 解決");return matches[0].Id;}
        if(matches.Length==0){log.AppendLine("ref参照先 "+line+"行: 0候補（参照先なしで作成）");return null;}
        if(!ask || app==null){log.AppendLine("ref参照先 "+line+"行: "+matches.Length+"候補（一括のため参照先なしで作成）");return null;}
        Func<IModel,List<string>> chain=m=>{var ids=new List<string>();for(var at=m;at!=null && ids.Count<256;at=at.Owner)ids.Insert(0,at.Id);return ids;};
        var mine=near==null?new List<string>():chain(near);
        Func<SequenceReferenceCandidate,int> shared=c=>{var model=project.GetModelById(c.Id);if(model==null)return 0;var theirs=chain(model);int k=0;while(k<mine.Count && k<theirs.Count && mine[k]==theirs[k])k++;return k;};
        var ordered=matches.OrderByDescending(shared).ThenBy(c=>c.Path,StringComparer.Ordinal).ToArray();
        for(int i=0;i<ordered.Length;i++)
            if(app.Window.UI.ShowConfirmDialog("ref「"+text+"」（入力 "+line+"行目）の参照先の候補が "+ordered.Length+"件あります。\n\n候補 "+(i+1)+"/"+ordered.Length+":\n"+ordered[i].Path
                +"\n\nこの相互作用を参照先にしますか？\nOK: この相互作用を参照先にします。\nキャンセル: 次の候補を表示します（最後の候補でキャンセルすると参照先なしで作成します）。",SequenceExperiment.Title))
            {log.AppendLine("ref参照先 "+line+"行: "+ordered.Length+"候補から選択 "+(i+1)+"番目");return ordered[i].Id;}
        log.AppendLine("ref参照先 "+line+"行: "+ordered.Length+"候補（選択なし・参照先なしで作成）");
        return null;
    }
    // A ref linked to an interaction may show that interaction's name instead of its own text.
    // When the input's ref resolves to the same interaction, those texts are the same ref.
    internal static void AlignLinkedRefs(IProject project,SequenceDocument current,SequenceDocument desired)
    {
        foreach(var e in current.Elements.Where(e=>e.Kind=="ref"))
        {
            string reference;
            if(!e.Attributes.TryGetValue("reference",out reference) || string.IsNullOrEmpty(reference))continue;
            var target=project.GetModelById(reference);if(target==null)continue;
            var shown=new[]{target.Name,QualifiedName(target)}.Select(SequenceLabels.Fold).ToArray();
            if(!shown.Contains(SequenceLabels.Fold(e.Text)))continue;
            var texts=desired.Elements.Where(d=>d.Kind=="ref" && d.Attributes.ContainsKey("reference") && d.Attributes["reference"]==reference).Select(d=>d.Text).Distinct().ToArray();
            if(texts.Length==1)e.Text=texts[0];
        }
    }
    const string UnsavedAdvice="S220: 退避データを取得できません。プロジェクトを保存してから実行してください。"
        +"この操作は自動保存しません。";
    // What each message and bar is tied to, as the product holds it: the port shapes, every
    // relation with its fields and order, and the plain field values. Put next to one drawn
    // by hand, it shows what a generated diagram lacks. Ids are cut to their last four
    // characters, enough to pair the rows up.
    static string Connections(ISequenceDiagram diagram)
    {
        Func<string,string> tail=id=>string.IsNullOrEmpty(id)?"-":id.Length<=4?id:id.Substring(id.Length-4);
        Func<object,string> kind=port=>port==null?"なし":port is IExecutionSpecificationShape?"実行区間":port is ILifelineShape?"ライフライン":port.GetType().Name;
        Func<ISequenceShape,string> portId=shape=>shape==null?"-":tail(shape.ModelId);
        Func<IModel,string> describe=model=>{
            var text=new StringBuilder();
            foreach(var r in model.GetRelationsWhere((relation,field)=>true).OrderBy(r=>r.Metaclass.Id,StringComparer.Ordinal))
            {
                bool outgoing=r.Source.Id==model.Id;var other=outgoing?r.Target:r.Source;
                string name=r.Metaclass.Id.Substring(r.Metaclass.Id.LastIndexOf('.')+1);
                text.Append("\n    ").Append(outgoing?"→ ":"← ").Append(name)
                    .Append(" ").Append(other.ClassName).Append(":").Append(tail(other.Id))
                    .Append(" field=").Append(r.SourceField==null?"-":r.SourceField.Name).Append("/").Append(r.TargetField==null?"-":r.TargetField.Name)
                    .Append(" index=").Append(r.SourceIndex).Append("/").Append(r.TargetIndex);
            }
            if(model.Metaclass!=null)
                foreach(var f in model.Metaclass.GetFields().Cast<IField>().Where(f=>!f.IsEmbedded && !f.IsReference))
                {
                    // A field of an enumerated type has no string form; read the value itself.
                    string value=null;try{value=model.GetFieldString(f.Name);}catch(Exception){}
                    if(string.IsNullOrEmpty(value))try{var raw=model.GetField(f.Name);value=raw==null?null:Convert.ToString(raw);}catch(Exception){}
                    if(!string.IsNullOrEmpty(value))text.Append("\n    ").Append(f.Name).Append("=").Append(value.Length>24?value.Substring(0,24):value);
                }
            return text.ToString();
        };
        var lines=new StringBuilder("接続の実測（IDは末尾4文字）");
        foreach(var m in diagram.Messages.OrderBy(m=>m.SourceY))
            lines.Append("\nメッセージ ").Append(tail(m.ModelId)).Append(" Y=").Append(m.SourceY).Append("/").Append(m.TargetY)
                .Append(" 送信=").Append(kind(m.SendPort)).Append(":").Append(portId(m.SendPort as ISequenceShape))
                .Append(" 受信=").Append(kind(m.ReceivePort)).Append(":").Append(portId(m.ReceivePort as ISequenceShape))
                .Append(m.Model==null?"":describe(m.Model));
        foreach(var e in diagram.ExecutionSpecifications.OrderBy(e=>e.LocationY))
            lines.Append("\n実行区間 ").Append(tail(e.ModelId)).Append(" ").Append(e.Lifeline==null?"?":tail(e.Lifeline.ModelId))
                .Append(" Y=").Append(e.LocationY).Append(" 長さ=").Append(e.Length)
                .Append(e.Model==null?"":describe(e.Model));
        foreach(var d in diagram.Destructions.OrderBy(d=>d.LocationY))
            lines.Append("\n破棄 ").Append(tail(d.ModelId)).Append(" ").Append(d.Lifeline==null?"?":tail(d.Lifeline.ModelId))
                .Append(" Y=").Append(d.LocationY).Append(d.Model==null?"":describe(d.Model));
        foreach(var l in diagram.Lifelines.OrderBy(l=>l.LocationX))
            lines.Append("\nライフライン ").Append(tail(l.ModelId)).Append(" 長さ=").Append(l.TimelineLength);
        return lines.ToString();
    }
    // HasUnsavedChanges answers false when the dirty state holds nothing savable, but the
    // export still refuses, so ask the design model as well.
    static bool Unsaved(IProject project)
    {
        try {return project.HasUnsavedChanges() || (project.DesignModel!=null && project.DesignModel.IsDirty);}
        catch(Exception) {return false;}
    }
    // Milliseconds per stage of the last run, shown with its result so a slow step can be
    // named from a measurement rather than guessed.
    internal static readonly List<string> Timings=new List<string>();
    // Set by the scenario batch: the diagram and input to use without asking, and what the
    // last run found.
    internal static bool Batch;
    internal static ISequenceDiagram BatchDiagram;
    internal static string BatchInput;
    internal static int LastChanges=-1;
    internal static bool LastCommitted;
    internal static string LastReasons="";
    static System.Diagnostics.Stopwatch clock;
    internal static void Lap(string stage)
    {
        if(clock==null)return;
        Timings.Add(stage+" "+clock.ElapsedMilliseconds);clock.Restart();
    }
    internal static string TimingLine()
    { return Timings.Count==0?"":"\n処理時間(ms): "+string.Join(" / ",Timings); }
    public static void Preview(IApplication app,bool prepare=false,bool trial=false,bool retain=false,bool reconnectCommit=false)
    {
        Timings.Clear();clock=null;
        var log=new StringBuilder();string report=null;string screenshot=null;
        retain=retain||reconnectCommit;trial=trial||retain;prepare=prepare||trial;
        try
        {
            LastChanges=-1;LastCommitted=false;LastReasons="";
            var diagram=Batch && BatchDiagram!=null?BatchDiagram:app.Workspace.CurrentEditor as ISequenceDiagram;
            if(diagram==null)throw new InvalidOperationException("S210: シーケンス図を開いてください。");
            string path=Batch && BatchInput!=null?BatchInput:app.Window.UI.ShowOpenFileDialog("図全体と比較するPlantUML","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
            if(string.IsNullOrEmpty(path))return;
            if(new FileInfo(path).Length>300000)throw new InvalidOperationException("S210: 入力は300KB以下にしてください。");
            clock=System.Diagnostics.Stopwatch.StartNew();
            var desired=SequenceDocument.Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
            var current=DiagramSnapshot.Read(diagram,log);
            Lap("図の読取り");
            // Resolve only unambiguous references for this non-mutating audit command. That
            // walks the whole design model, so only an input with a ref pays for it.
            var project=app.Workspace.CurrentProject;
            var interactions=desired.Elements.Any(e=>e.Kind=="ref")?Interactions(project):new SequenceReferenceCandidate[0];
            if(interactions.Length>0)Lap("ref参照先の探索");
            var drawn=current.Document.Elements.FirstOrDefault(e=>e.Kind=="interaction");
            var near=drawn==null?null:project.GetModelById(drawn.Id);
            // Candidates the diagram already refers to need no question: that is the one it means.
            var linked=new HashSet<string>(current.Document.Elements.Where(e=>e.Kind=="ref" && e.Attributes.ContainsKey("reference")).Select(e=>e.Attributes["reference"]));
            foreach(var e in desired.Elements.Where(e=>e.Kind=="ref"))
            {
                var matches=SequenceReferenceResolver.Find(e.Text,interactions);
                if(matches.Length>1 && matches.Count(m=>linked.Contains(m.Id))==1)matches=matches.Where(m=>linked.Contains(m.Id)).ToArray();
                string chosen=PickReference(app,project,near,e.Text,e.Line,matches,log,!Batch);
                e.Attributes["reference"]=chosen??"";
                if(chosen==null)current.Limitations.Add("ref参照先 "+e.Line+"行: "+matches.Length+"候補");
            }
            // What the diagram reads as, before a linked ref takes the input's text: the check that
            // nothing changed while preparing compares a fresh read with this.
            string readAs=current.Document.ToJson();
            AlignLinkedRefs(project,current.Document,desired);
            var plan=SequenceNotePolicy.Build(current.Document,desired,()=>Guid.NewGuid().ToString());
            LastChanges=plan.Changes.Count;
            var preflight=SequenceStructurePreflight.Check(current.Document,plan);
            Lap("差分計画");
            // The snapshot goes through ExportModelUnit, which refuses to run while the
            // project has unsaved changes. Say so before any work instead of letting the
            // export throw halfway. This command never saves for you.
            if(prepare && SequenceEditorCapture.BatchSource==null && Unsaved(project))throw new InvalidOperationException(UnsavedAdvice);

            report="{\"version\":1,\"project\":"+SequencePayload.Q(project.Id)+",\"diagram\":"+SequencePayload.Q(diagram.Id)
                +",\"current\":"+current.Document.ToJson()+",\"desired\":"+desired.ToJson()+",\"plan\":"+plan.ToJson()
                +",\"structurePreflight\":"+preflight.ToJson()+",\"expected\":"+plan.Expected.ToJson()+",\"limitations\":"+PumlBuild.Json(current.Limitations.ToArray())
                +",\"shapes\":"+PumlBuild.Json(current.ShapeIds.ToDictionary(p=>p.Key,p=>(object)p.Value))
                +",\"geometry\":"+PumlBuild.Json(current.Geometry)+"}";
            foreach(var c in plan.Changes)log.AppendLine(c.Action+" "+c.Kind+" line="+c.Line+" id="+c.Id);
            foreach(var warning in current.Limitations)log.AppendLine("要照合: "+warning);
            screenshot=(trial?"適用前の比較結果（更新後の残差ではありません）\n":"現在の図と入力の比較結果\n")+SequenceAudit.Reasons(current.Document,desired,plan)+"\f"+preflight.Summary()+"\f"+current.BarTimeline();
            // Only the comparison shows it; applying already has enough pages.
            if(!prepare)
            {
                try {screenshot+="\f"+Connections(diagram);}
                catch(Exception ex) {screenshot+="\f接続の実測: 取得できません: "+ex.Message;}
            }
            log.AppendLine(screenshot);
            SequenceExperiment.Summary=SequenceAudit.Summary(plan,current.Limitations.Count)+"\n構造更新の停止理由: "+preflight.Reasons.Count+"件（診断表示）";
            log.AppendLine("Scope: "+project.Id+" / "+diagram.ModelId+" / "+diagram.Id);
            LastReasons=string.Join(" / ",preflight.Reasons.Distinct());
            // Checked after the comparison is logged, so the reasons reach the diagnostics.
            // Nothing differs: the diagram already is the input, which is not a failure.
            bool same=plan.Changes.Count==0;
            if(retain && same)SequenceExperiment.Summary="図は入力と一致しています。反映する差分はありません。\n"+SequenceAudit.Summary(plan,current.Limitations.Count);
            if(retain && !same && !preflight.CanCommit())
                throw new InvalidOperationException("S231: 反映できない差分が含まれています。"
                    +(preflight.Reasons.Count>0?"\n"+string.Join("\n",preflight.Reasons.Distinct()):"\n構造更新の対象がありません。"));
            if(prepare)
            {
                if(!preflight.Candidate)
                {
                    if(!same)SequenceExperiment.Summary="構造更新データ: 未作成 / 図への反映なし\n"+preflight.Summary();
                }
                else
                {
                    var root=diagram.Model as IInteraction;
                    if(root==null || !root.IsEditable || root.IsProxy || root.IsDeleted || string.IsNullOrEmpty(project.Path))
                        throw new InvalidOperationException("S220: 保存済みで編集可能な図を開いてください。");
                    string exported=null;
                    try {SequenceEditorCapture.Read(project,root,diagram,log,delegate(string value){exported=value;});}
                    catch(Exception ex)
                    {
                        // The export refuses on a dirty project even when nothing is savable,
                        // for instance right after a trial has rolled its changes back.
                        if(ex.Message.IndexOf("保存",StringComparison.Ordinal)<0)throw;
                        throw new InvalidOperationException(UnsavedAdvice,ex);
                    }
                    Lap("エクスポート");
                    exported=CompleteGeometry(exported,diagram,log);
                    SequenceFrameTypes frameTypes=null;
                    string rootId=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
                    var byExpected=plan.Expected.Elements.ToDictionary(e=>e.Id);
                    if(preflight.AddFragments.Count>0 || preflight.AddOperands.Count>0 || preflight.MoveMessages.Count>0 || preflight.NestChanges.Count>0
                        || preflight.AddMessages.Concat(preflight.AddRefs).Any(id=>byExpected[id].Parent!=rootId))
                    {
                        // Resolve the metaclasses only when a frame is being added or a message
                        // goes into an operand, which needs the operand-to-message relation,
                        // so a diagram without them still runs every other change.
                        try {frameTypes=PumlRuntime.FrameTypes(diagram,project);}
                        catch(Exception ex) {throw new InvalidOperationException("S220: フラグメントの型を解決できません: "+ex.Message,ex);}
                        log.AppendLine("frame types: "+frameTypes.ToJson());
                    }
                    // Literals for message sorts, so a message of a sort the diagram has none of
                    // can still be added.
                    SequenceStructurePreparation.SortLiterals.Clear();
                    try
                    {
                        var messageClass=PumlRuntime.BaseTypes(diagram)[6];
                        var sortField=messageClass.GetFields().Cast<IField>().FirstOrDefault(f=>f.Name=="MessageSort");
                        if(sortField!=null && sortField.TypeEnum!=null)
                            foreach(string sort in new[]{"sync","async","reply"})
                            {
                                var literal=sortField.TypeEnum.Literals.FirstOrDefault(l=>string.Equals(l.Name,sort,StringComparison.OrdinalIgnoreCase));
                                if(literal!=null)SequenceStructurePreparation.SortLiterals[sort]=literal.Name;
                            }
                    }
                    catch(Exception ex){log.AppendLine("message sort literals: "+ex.Message);}
                    // Metaclasses and relation rows for building what the diagram holds nothing of to
                    // copy, and for free ends of messages to or from outside the diagram.
                    SequenceStructurePreparation.BaseTypes=null;
                    bool needsEnds=preflight.NeedsFreeEnds(plan);
                    try {SequenceStructurePreparation.BaseTypes=PumlRuntime.SyncBaseTypes(diagram,project,needsEnds);}
                    catch(Exception ex) {log.AppendLine("base types: "+ex.Message);if(needsEnds)throw new InvalidOperationException("S220: 図外の端の型を解決できません: "+ex.Message,ex);}
                    SequenceStructurePreparation.DestroyTypes=null;
                    if(preflight.AddDestroys.Count>0)
                    {
                        try {SequenceStructurePreparation.DestroyTypes=PumlRuntime.DestroyTypes(diagram);}
                        catch(Exception ex) {throw new InvalidOperationException("S220: 破棄の型を解決できません: "+ex.Message,ex);}
                    }
                    SequenceNoteTypes noteTypes=null;
                    if(preflight.AddNotes.Count>0)
                    {
                        try {noteTypes=PumlRuntime.NoteTypes(diagram,project);}
                        catch(Exception ex) {throw new InvalidOperationException("S220: Noteの型を解決できません: "+ex.Message,ex);}
                        log.AppendLine("note types: "+noteTypes.Class+" / "+PumlBuild.Json(noteTypes.Owns)+" / "+noteTypes.Field+":"+noteTypes.Storage);
                    }
                    SequenceRefTypes refTypes=null;
                    if(preflight.AddRefs.Count>0 || preflight.RefTargetChanges.Count>0)
                    {
                        try {refTypes=PumlRuntime.RefTypes(diagram,project);}
                        catch(Exception ex) {throw new InvalidOperationException("S220: refの型を解決できません: "+ex.Message,ex);}
                        log.AppendLine("ref types: "+refTypes.Class+" / "+PumlBuild.Json(refTypes.Owns)+" / "+PumlBuild.Json(refTypes.Crossing)
                            +" / "+(refTypes.RefersTo==null?"no RefersTo":PumlBuild.Json(refTypes.RefersTo)));
                    }
                    Lap("型の解決");
                    var preparation=SequenceStructurePreparation.Build(exported,diagram.Id,current.Document,plan,frameTypes,noteTypes,refTypes);
                    var raw=SequenceJson.Parse(exported);
                    var exportedRelations=new HashSet<string>(raw["Relations"].Items.Select(r=>SequenceEditorDocument.Value(r,"Id")));
                    foreach(string id in preflight.DeleteExecutions.Concat(preflight.ReconnectMessages))
                    {
                        var model=project.GetModelById(id);
                        if(model==null || model.IsDeleted || model.IsProxy || !model.IsEditable)
                            throw new InvalidOperationException("S220: 更新対象に編集不可のモデルがあります。");
                        if(preflight.DeleteExecutions.Contains(id) && model.GetRelationsWhere((r,f)=>true).Any(r=>!exportedRelations.Contains(r.Id)))
                            throw new InvalidOperationException("S220: 削除対象に退避範囲外の関連があります。");
                    }
                    if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || (!Batch && (app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=diagram.Id))
                        || DiagramSnapshot.Read(diagram,new StringBuilder()).Document.ToJson()!=readAs)
                        throw new InvalidOperationException("S220: 準備中に対象の図が変化しました。");
                    Lap("準備");
                    string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NextDesign.SequenceSync","prepared",Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory);
                    // ready.json is written last. Partial directories must never be used as a package.
                    SequenceExperiment.Write(Path.Combine(directory,"snapshot.json"),exported);
                    SequenceExperiment.Write(Path.Combine(directory,"reconnect.json"),preparation.ReconnectJson);
                    SequenceExperiment.Write(Path.Combine(directory,"editor-after-delete.json"),preparation.EditorAfterDeleteJson);
                    SequenceExperiment.Write(Path.Combine(directory,"plan.json"),report);
                    SequenceExperiment.Write(Path.Combine(directory,"ready.json"),PumlBuild.Json(PumlBuild.Obj("Version",1,"Mode","prepare-only", "ProjectId",project.Id,"DiagramId",diagram.Id,
                        "DeleteExecutions",preparation.DeleteIds,"ExecutionOrder",new[]{"reconnect.json","delete listed execution models","editor-after-delete.json"})));
                    log.AppendLine("Prepared directory: "+directory);
                    screenshot+="\f構造更新データの準備: 完了\n受信接続変更: "+preflight.ReconnectMessages.Count+" / 実行区間削除: "+preparation.DeleteIds.Length
                        +"\n退避データ・接続変更JSON・削除後の図形JSONを保存しました。\n図への適用・Undo/Redo・保存再読込: 未実施";
                    SequenceExperiment.Summary="構造更新データの準備: 完了 / 図への反映なし\n受信接続変更: "+preflight.ReconnectMessages.Count+" / 実行区間削除: "+preparation.DeleteIds.Length
                        +"\n保存先: "+directory+"\n準備ファイルの手動インポートはしないでください。保存ファイルから適用する機能はありません。";
                    if(trial)
                    {
                        SequenceExperiment.Summary=SequenceStructureTrial.Run(app,project,diagram,preparation,plan,exported,directory,log,retain,reconnectCommit);
                        screenshot=SequenceExperiment.Summary+"\f試行診断\n"+log.ToString();
                    }
                }
            }
        }
        catch(Exception ex) {SequenceExperiment.Summary=(trial?"構造更新の試行を完了できませんでした。診断表示を確認してください。":prepare?"構造更新データの準備を完了できませんでした。図への反映なし。":"図全体の読取り検証を完了できませんでした。")+"\n"+ex.Message;log.AppendLine(ex.ToString());screenshot=null;}
        try
        {
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NextDesign.SequenceSync","reports");
            Directory.CreateDirectory(directory);
            string stem=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
            File.WriteAllText(stem+".txt",log.ToString(),new UTF8Encoding(false));
            if(report!=null)File.WriteAllText(stem+".json",report,new UTF8Encoding(false));
            if(screenshot==null)SequenceExperiment.Summary+="\n診断保存先: "+stem+".txt";
        }
        catch(Exception ex) {log.AppendLine("診断の保存失敗: "+ex.Message);SequenceExperiment.Summary+="\n診断ファイルを保存できませんでした。診断表示で確認してください。";}
        SequenceExperiment.Details=screenshot??log.ToString();
        if(!Batch)SequenceExperiment.Show(app);
    }
}

public static class SequenceStructureTrial
{
    static string Port(IMessagePort value) {var m=value as IModel;return m==null?"":m.Id;}
    static string FieldId(IField value) {return value==null?"":value.Id;}
    static string Number(double value){return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
    // The editor keeps its old drawing until the page is refreshed, so switching diagrams
    // by hand was the only way to see a result. Refresh it here instead.
    static void Refresh(IApplication app,StringBuilder log)
    {
        try {app.Window.EditorPage.UpdateEditors();}
        catch(Exception ex) {log.AppendLine("editor refresh failed: "+ex.Message);}
    }
    static bool Matches(Action verify,StringBuilder log)
    {
        try {verify();return true;} catch(Exception ex) {log.AppendLine(ex.ToString());return false;}
    }
    static SequenceTrialState Rounded(IProject project,string rootId,Func<ISequenceDiagram> fresh,string[] newShapes)
    {
        var state=Read((IInteraction)project.GetModelById(rootId),fresh());
        state.Round(newShapes);return state;
    }
    static SequenceTrialState Read(IInteraction root,ISequenceDiagram diagram)
    {
        var state=new SequenceTrialState();
        var tree=SequenceMappedUpdate.Tree(root).ToArray();
        Action<IModel> record=m=>state.Models[m.Id]=PumlBuild.Json(new[]{m.Metaclass.Id,m.Name,m.Owner==null?"":m.Owner.Id,m.IsDeleted.ToString()});
        foreach(var model in tree)
        {
            record(model);
            foreach(var r in model.GetRelationsWhere((relation,field)=>true))
            {
                state.Relations[r.Id]=new[]{r.Source.Id,r.Target.Id,r.SourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),r.TargetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                state.RelationFields[r.Id]=PumlBuild.Json(new[]{FieldId(r.SourceField),FieldId(r.TargetField)});
                record(r.Source);record(r.Target);
            }
        }
        foreach(var m in root.Messages)
        {
            // A message a destruction points back at reads as a destroy message, as the snapshot reads it.
            bool destroying=m.GetRelationsWhere((r,f)=>r.Target!=null && r.Target.Id==m.Id && r.Metaclass!=null
                && r.Metaclass.Id==SequencePayload.Prefix+"DestroyMessage").Any();
            state.Ports[m.Id]=new[]{Port(m.SendPort),Port(m.ReceivePort),m.Sender==null?"":m.Sender.Id,m.Receiver==null?"":m.Receiver.Id,destroying?"destroy":m.Kind};
        }
        foreach(var shape in diagram.Shapes)
        {
            var rows=new List<string>();var node=shape as ISequenceNodeShape;
            if(node!=null)rows.AddRange(new[]{Number(node.LocationX),Number(node.LocationY),Number(node.Width),Number(node.Height)});
            var message=shape as IMessageShape;
            if(message!=null)rows.AddRange(new[]{message.Text,Number(message.SourceY),Number(message.TargetY),Number(message.SelfloopBendsX)});
            var execution=shape as IExecutionSpecificationShape;if(execution!=null)rows.Add(Number(execution.Length));
            state.Shapes[shape.Id]=PumlBuild.Json(rows.ToArray());state.ShapeModels[shape.Id]=shape.ModelId;
        }
        foreach(var f in diagram.Fragments){state.Shapes[f.Id]+=f.Text;foreach(var o in f.Operands)state.Shapes[o.Id]+=PumlBuild.Json(new[]{o.Guard,Number(o.Position)});}
        foreach(var n in diagram.Notes)state.Shapes[n.Id]+=n.Text;
        foreach(var u in diagram.InteractionUses)state.Shapes[u.Id]+=u.Text;
        foreach(var l in diagram.Lifelines)state.Shapes[l.Id]+=Number(l.TimelineLength);
        return state;
    }
    // What the last failed check found, for the summary: a batch shows only that.
    internal static string LastMismatch="";
    static void Verify(SequenceTrialState expected,SequenceTrialState actual,string phase,StringBuilder log)
    {
        string differences=expected.DifferenceCounts(actual);log.AppendLine(phase+": "+differences);
        string relationDetails=expected.RelationDifferences(actual);
        if(relationDetails.Length>0)log.AppendLine("\f関連の照合内訳 / "+phase+"\n"+relationDetails+"\f");
        string shapeDetails=expected.ShapeDifferences(actual);
        if(shapeDetails.Length>0)log.AppendLine("\f図形の照合内訳 / "+phase+"\n"+shapeDetails+"\f");
        if(expected.Signature()!=actual.Signature())
        {
            var ports=expected.Ports.Keys.Union(actual.Ports.Keys).Where(k=>!expected.Ports.ContainsKey(k) || !actual.Ports.ContainsKey(k)
                || PumlBuild.Json(expected.Ports[k])!=PumlBuild.Json(actual.Ports[k])).Take(6)
                .Select(k=>k+" "+(expected.Ports.ContainsKey(k)?PumlBuild.Json(expected.Ports[k]):"-")+" / "+(actual.Ports.ContainsKey(k)?PumlBuild.Json(actual.Ports[k]):"-"));
            var models=expected.Models.Keys.Union(actual.Models.Keys).Where(k=>!expected.Models.ContainsKey(k) || !actual.Models.ContainsKey(k) || expected.Models[k]!=actual.Models[k]).Take(6)
                .Select(k=>k+" "+(expected.Models.ContainsKey(k)?expected.Models[k]:"-")+" / "+(actual.Models.ContainsKey(k)?actual.Models[k]:"-"));
            LastMismatch=phase+": "+differences+"\n"+relationDetails+"\n"+shapeDetails+"\n送受信(期待/実測): "+string.Join(" ; ",ports)+"\nモデル(期待/実測): "+string.Join(" ; ",models);
        }
        if(expected.Signature()!=actual.Signature())throw new InvalidOperationException("S230: "+phase+"の照合が不一致です。"+differences);
    }
    static void Import(IProject project,string json,StringBuilder log)
    {
        var result=project.ImportUnitFromJson(json,null,null);
        if(result==null)throw new InvalidOperationException("S230: インポート結果がありません。");
        log.AppendLine("trial import: "+result.State);
        foreach(var e in result.Errors)log.AppendLine(e.Kind+": "+e.Message);
        if(result.State!="success" || result.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))throw new InvalidOperationException("S230: インポートが失敗または警告を返しました。");
    }
    public static string Run(IApplication app,IProject project,ISequenceDiagram diagram,SequenceStructurePreparation prepared,SyncPlan plan,string exported,string directory,StringBuilder log,bool retain=false,bool reconnectCommit=false)
    {
        int reconnectCount=prepared.ReconnectCount;
        if(reconnectCommit && !retain)throw new InvalidOperationException("S231: 確定モードが不正です。");
        // The preflight has already sorted every change into something the package writes;
        // a package that writes nothing at all is the only thing left to refuse.
        int touched=prepared.DeleteIds.Length+prepared.AddedExecutions.Length
            +prepared.AddedParticipants.Length+prepared.DeleteParticipantIds.Length
            +prepared.DeleteMessageIds.Length+prepared.AddedMessages.Length
            +prepared.DeleteFrameIds.Length+prepared.AddedFragments.Length+prepared.AddedOperands.Length+reconnectCount
            +prepared.MovedMessages.Length+prepared.DeleteNoteIds.Length+prepared.AddedNotes.Length+prepared.DeleteRefIds.Length+prepared.DeleteDestroyIds.Length
            +prepared.ShiftedShapes.Length+prepared.Renamed.Length+prepared.UnrelateIds.Length+prepared.SortChanged.Length+prepared.ShapeTexts.Length+prepared.DeleteEndIds.Length;
        if(retain && (touched==0 || !reconnectCommit))
            throw new InvalidOperationException("S231: 確定モードの対象外の差分があります。");
        string caseId=reconnectCommit?"UPDATE007":retain?"UPDATE006":"UPDATE005";
        LastMismatch="";
        var root=diagram.Model as IInteraction;
        var newShapes=prepared.AddedExecutions.Select(a=>a.ShapeId)
            .Concat(prepared.AddedParticipants.Select(a=>a.ShapeId))
            .Concat(prepared.AddedMessages.Select(a=>a.ShapeId))
            .Concat(prepared.AddedFragments.Select(a=>a.ShapeId))
            .Concat(prepared.AddedOperands.Select(a=>a.ShapeId))
            .Concat(prepared.AddedNotes.Where(a=>a.ShapeId!=null).Select(a=>a.ShapeId))
            // Written this run too, so the same rounding applies to them.
            .Concat(prepared.StretchedLifelines.Select(a=>a.ShapeId))
            .Concat(prepared.ShiftedShapes.Select(a=>a.ShapeId)).ToArray();
        var removedModels=prepared.DeleteIds.Concat(prepared.DeleteParticipantIds)
            .Concat(prepared.DeleteMessageIds).Concat(prepared.DeleteFrameIds).Concat(prepared.DeleteNoteIds).Concat(prepared.DeleteRefIds).Concat(prepared.DeleteDestroyIds)
            .Concat(prepared.DeleteEndIds).ToArray();
        var before=Read(root,diagram);before.Round(newShapes);string original=before.Signature();
        var expectedReconnect=before.Expected(prepared,plan,false);
        var expectedFinal=before.Expected(prepared,plan,true);
        var deletionOwners=before.DeletionOwners(prepared);
        expectedReconnect.Round(newShapes);expectedFinal.Round(newShapes);
        string rootId=root.Id,editorId=diagram.Id;
        Func<ISequenceDiagram> fresh=()=>{
            var model=project.GetModelById(rootId) as IInteraction;
            if(model==null)throw new InvalidOperationException("S230: 対象の図を取得できません。");
            return model.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==editorId);
        };
        string confirmation=retain
            ? "PlantUMLの差分を図へ反映します。照合が一致したときだけ確定し、一致しなければ取り消します。\n"
                +"メッセージやフラグメントを追加する更新は確定後にUndoできません（製品側の不具合）。取り消すときは保存せずに開き直してください。\n"
                +"自動保存はしません。実行しますか？"
            : "コピーのプロジェクトで実行してください。\n受信接続変更と実行区間削除を一時適用し、照合後に必ず取り消します。\n自動保存・変更の確定は行いません。試行しますか？";
        SequenceSyncRuntime.Lap("照合の用意");
        if(!SequenceSyncRuntime.Batch && !app.Window.UI.ShowConfirmDialog(confirmation,SequenceExperiment.Title))
            return caseId+": キャンセル / 図への変更なし";
        SequenceSyncRuntime.Lap("確認画面（操作待ち）");
        if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || (!SequenceSyncRuntime.Batch && (app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=editorId))
            || Rounded(project,rootId,fresh,newShapes).Signature()!=original)
            throw new InvalidOperationException("S230: 確認中に対象の図が変化しました。");
        // The confirmation is modal, so nothing can be edited while it is up, and the SDK
        // state above has just been compared again. Exporting the whole unit a second time
        // only to compare the editor's display settings cost as long as the first export.
        SequenceExperiment.Write(Path.Combine(directory,"trial-before-sdk.json"),original);
        SequenceExperiment.Write(Path.Combine(directory,"trial-expected-sdk.json"),expectedFinal.Signature());
        string stage="トランザクション開始";
        var transaction=project.BeginUndoTransaction(false);
        if(transaction==null)throw new InvalidOperationException("S230: トランザクションを開始できません。");
        Action apply=delegate {
            stage=prepared.AddedExecutions.Length>0?"実行区間の追加と受信接続の変更":"受信接続の変更";
            foreach(var wire in prepared.AddedMessages)
                log.AppendLine("add message payload: model="+wire.ModelId+" shape="+wire.ShapeId+" Y="+wire.Y);
            foreach(var lane in prepared.AddedParticipants)
                log.AppendLine("add participant payload: model="+lane.ModelId+" shape="+lane.ShapeId+" X="+lane.X);
            foreach(var entry in prepared.AddedExecutions)
                log.AppendLine("add execution payload: model="+entry.ModelId+" shape="+entry.ShapeId
                    +" geometry(X,Y,Length)="+entry.Geometry+" relation order="+PumlBuild.Json(entry.RelationSources));
            foreach(var frame in prepared.AddedFragments)
                log.AppendLine("add fragment payload: model="+frame.ModelId+" shape="+frame.ShapeId
                    +" geometry(X,Y,Width,Height)="+frame.Geometry+" relation order="+PumlBuild.Json(frame.RelationSources));
            foreach(var branch in prepared.AddedOperands)
                log.AppendLine("add operand payload: model="+branch.ModelId+" shape="+branch.ShapeId
                    +" position="+branch.Position+" owner="+branch.OwnerId);
            foreach(var lane in prepared.StretchedLifelines)
                log.AppendLine("stretch lifeline payload: model="+lane.ModelId+" shape="+lane.ShapeId
                    +" timeline="+lane.Length);
            foreach(var move in prepared.ShiftedShapes)
                log.AppendLine("shift payload: model="+move.ModelId+" shape="+move.ShapeId
                    +" "+PumlBuild.Json(move.Keys)+"="+PumlBuild.Json(move.Values));
            log.AppendLine("created shape collections: "+(prepared.CreatedCollections.Length==0?"none"
                :string.Join(",",prepared.CreatedCollections)));
            Import(project,prepared.ReconnectJson,log);
            // A reference relation is taken off where a message or frame leaves a branch that
            // stays, where a ref stops covering a lane, and where a bar's reply changes.
            if(prepared.UnrelateIds.Length>0)
            {
                stage="関連の解除";
                var tree=SequenceMappedUpdate.Tree(project.GetModelById(rootId)).ToArray();
                foreach(string id in prepared.UnrelateIds)
                {
                    var found=tree.SelectMany(m=>m.GetRelationsWhere((r,f)=>r.Id==id)).FirstOrDefault();
                    if(found==null)throw new InvalidOperationException("S230: 解除する関連が見つかりません。");
                    // The relation itself is taken off: SourceField names the field on the target side,
                    // so it cannot be handed to the source's UnRelate.
                    log.AppendLine("unrelate: "+found.Metaclass.Id+" "+found.Source.Id+" -> "+found.Target.Id);
                    found.UnRelate();
                }
            }
            var connected=Rounded(project,rootId,fresh,newShapes);expectedReconnect.Loosen(prepared.LooseShapeIds,connected);
            Verify(expectedReconnect,connected,"接続変更後",log);
            log.AppendLine("receiver reconnection count: "+prepared.ReconnectCount
                +"; added executions: "+prepared.AddedExecutions.Length
                +"; added participants: "+prepared.AddedParticipants.Length+"; SDK state verified");
            stage="不要な要素の削除";
            using(project.SuspendModelVerification())foreach(string id in removedModels)project.GetModelById(id).Delete();
            // With nothing deleted, that editor is the one just imported, so importing it
            // again only touches the same collections a second time. Skipping it does not
            // stop the product crashing when undoing an added message: that happens with a
            // single import too.
            stage="削除後のエディタ反映";
            if(removedModels.Length>0)Import(project,prepared.EditorAfterDeleteJson,log);
            else log.AppendLine("post-delete editor import skipped: nothing was deleted");
            foreach(string id in removedModels){var m=project.GetModelById(id);if(m!=null && !m.IsDeleted)throw new InvalidOperationException("S230: 削除対象が残っています。");}
            var afterDelete=Rounded(project,rootId,fresh,newShapes);
            if(deletionOwners.Length>0)
                log.AppendLine("\f所有関連の順序 / 削除段階\n削除前(*が消える関連)\n"+before.OrderReport(deletionOwners,prepared)
                    +"\n削除後 期待\n"+expectedFinal.OrderReport(deletionOwners,prepared)
                    +"\n削除後 実測\n"+afterDelete.OrderReport(deletionOwners,prepared)+"\f");
            expectedFinal.Loosen(prepared.LooseShapeIds,afterDelete);
            Verify(expectedFinal,afterDelete,"削除後",log);
            log.AppendLine("trial execution deletion and SDK state: verified");
        };
        Action rollback=delegate {transaction.Rollback();};
        Action verifyRestored=delegate {Verify(before,Rounded(project,rootId,fresh,newShapes),"取消後",log);};
        // Explicit completion only; Dispose may attempt a second rollback.
        string summary;
        if(retain)
        {
            var completion=new SequenceCommitTrial();
            completion.Run(apply,delegate {stage="変更の確定";transaction.Commit();},rollback,verifyRestored);
            SequenceSyncRuntime.LastCommitted=completion.Committed;
            foreach(var error in new[]{completion.ApplyError,completion.CommitError,completion.RollbackError,completion.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            Refresh(app,log);
            string cycle="";
            if(completion.Committed)
            {
                // This command runs inside the host's own transaction, so ours is nested and
                // its content only reaches the undo stack after the handler returns. Both
                // CanUndo answers are false here by design, not by failure.
                stage="Undo";
                log.AppendLine("undo availability: project="+project.CanUndo+" workspace="+app.Workspace.CanUndo());
                if(app.Workspace.CanUndo())
                {
                    app.Workspace.Undo();Refresh(app,log);
                    bool undone=Matches(delegate {Verify(before,Rounded(project,rootId,fresh,newShapes),"Undo後",log);},log);
                    stage="Redo";
                    bool available=app.Workspace.CanRedo(),redone=false;
                    if(available)
                    {
                        app.Workspace.Redo();Refresh(app,log);
                        redone=Matches(delegate {Verify(expectedFinal,Rounded(project,rootId,fresh,newShapes),"Redo後",log);},log);
                    }
                    cycle="\nUndo照合: "+(undone?"一致":"不一致")+" / Redo照合: "+(redone?"一致":available?"不一致":"実行できません");
                    if(!undone || !redone)cycle+="\n保存せずコピーを開き直してください。";
                }
                else cycle="\nUndo/Redo: このコマンドの実行中は履歴へ積まれないため自動確認できません。手で1回ずつ確認してください。";
            }
            // A stop says why: the exception, and what the check found when it was a mismatch.
            if(!completion.Committed)
            {
                var cause=completion.ApplyError??completion.CommitError;
                if(cause!=null)cycle+="\n原因: "+cause.GetType().Name+": "+cause.Message+(cause.InnerException!=null?" / "+cause.InnerException.Message:"");
                if(LastMismatch.Length>0)cycle+="\n照合の内訳:\n"+(LastMismatch.Length>3000?LastMismatch.Substring(0,3000)+"…":LastMismatch);
            }
            summary="ケース: "+caseId+" / "+(completion.Committed?"構造更新・SDK照合・変更確定: 成功":"停止段階: "+stage)
                +cycle
                +(completion.Committed?"\n保存して開き直し、見た目と差分0件を確認してください。":"\n取消API: "+(completion.RollbackReturned?"正常終了":"失敗・未確認")+" / 復元照合: "+(completion.Restored?"一致":"未確認・不一致"))
                +(!completion.Committed && !completion.Restored?"\n保存せずコピーを開き直してください。":"")
                +"\nプロジェクトの自動保存: していません\nスタイル読戻し・保存再読込: 未検証\nこの結果と診断表示を撮影してください。";
        }
        else
        {
            var trial=new SequenceRollbackTrial();trial.Run(apply,rollback,verifyRestored);
            Refresh(app,log);
            foreach(var error in new[]{trial.ApplyError,trial.RollbackError,trial.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            summary="ケース: UPDATE005 / "+(trial.Applied?"一時適用・SDK読戻し照合: 一致":"停止段階: "+stage)
                +"\n取消API: "+(trial.RollbackReturned?"正常終了":"失敗・未確認")+" / 復元照合: "+(trial.Restored?"一致":"未確認・不一致")
                +"\n変更の確定・プロジェクト保存: していません\nスタイルの適用後読戻し・保存再読込: 未検証"
                +(trial.Restored?"":"\n保存せずコピーを開き直してください。")+"\nこの結果と診断表示を撮影してください。";
        }
        SequenceSyncRuntime.Lap("反映と照合");
        summary+=SequenceSyncRuntime.TimingLine();
        summary+="\n今回の対象: 受信接続変更 "+reconnectCount+"件 / 実行区間削除 "+prepared.DeleteIds.Length+"件"
            +" / 実行区間追加 "+prepared.AddedExecutions.Length+"件"
            +" / 参加者追加 "+prepared.AddedParticipants.Length+"件 / 参加者削除 "+prepared.DeleteParticipantIds.Length+"件"
            +" / メッセージ削除 "+prepared.DeleteMessageIds.Length+"件"
            +" / メッセージ追加 "+prepared.AddedMessages.Length+"件"
            +" / フラグメント関連の削除 "+prepared.DeleteFrameIds.Length+"件 / Note削除 "+prepared.DeleteNoteIds.Length+"件 / Note・ref追加 "+prepared.AddedNotes.Length+"件 / ref削除 "+prepared.DeleteRefIds.Length+"件 / 本文変更 "+prepared.Renamed.Length+"件"
            +" / フラグメント追加 "+prepared.AddedFragments.Length+"件 / オペランド追加 "+prepared.AddedOperands.Length+"件"
            +" / タイムラインを伸ばした参加者 "+prepared.StretchedLifelines.Length+"件"
            +(prepared.InsertedMessageId.Length>0?" / 途中への挿入で下げた図形 "+prepared.ShiftedShapes.Length+"件":"")
            +(prepared.MovedMessages.Length>0?" / 枠で囲んだメッセージ "+prepared.MovedMessages.Length+"件 / 下げた図形 "+prepared.ShiftedShapes.Length+"件":"")
            // Committing an addition works, but the product crashes undoing a message or a
            // frame added this way, so say so here rather than leaving it to be discovered.
            // Undo after adding only bars or participants has not been tried.
            +(prepared.AddedMessages.Length>0 || prepared.AddedFragments.Length>0
                ?"\n注意: メッセージやフラグメントを追加した更新はUndoできません。Undoすると製品が停止します（製品側の不具合）。"
                    +"取り消すときは保存せずに開き直してください。"
                :prepared.AddedExecutions.Length>0 || prepared.AddedParticipants.Length>0
                ?"\n注意: 実行区間・参加者の追加をUndoできるかは未確認です。取り消すときは保存せずに開き直してください。":"");
        log.AppendLine(summary);
        try{SequenceExperiment.Write(Path.Combine(directory,"trial-result.txt"),summary+"\n"+log.ToString());}
        catch(Exception ex){log.AppendLine("trial result save: "+ex);summary+="\n試行結果の記録: 保存失敗";}
        return summary;
    }
}

// Runs a list of before/after PlantUML pairs on a copy of a project: each before becomes a
// new diagram beside the one that is open, the after is applied to it, and the result is
// compared again. Saving between steps is what lets the export run; this command is the
// only one that saves. Run again after reopening the project to recheck every diagram.
public static class SequenceBatch
{
    static string Line(string text,int max)
    {
        string flat=(text??"").Replace("\r","").Replace("\n"," / ");
        return flat.Length>max?flat.Substring(0,max)+"…":flat;
    }
    static ISequenceDiagram DiagramOf(IProject project,string root)
    {
        var model=project.GetModelById(root) as IInteraction;
        if(model==null)throw new InvalidOperationException("図のモデルが見つかりません: "+root);
        return model.GetEditors().OfType<ISequenceDiagram>().Single();
    }
    static void Save(IApplication app,IProject project)
    {
        if(!app.Workspace.SaveProject(project,false))throw new InvalidOperationException("プロジェクトを保存できません。");
    }
    // After a project is opened, a diagram nobody has shown yet reads back with no shapes
    // at all. Selecting its model in the navigator is tried first so the product loads it;
    // if it still reads empty, that is reported instead of counted as a difference.
    static ISequenceDiagram Loaded(IApplication app,IProject project,string root,StringBuilder detail)
    {
        var model=project.GetModelById(root) as IInteraction;
        if(model==null)throw new InvalidOperationException("図のモデルが見つかりません: "+root);
        try {app.Window.EditorPage.CurrentNavigator.Select(model,false);}
        catch(Exception ex){detail.AppendLine("ナビゲータで選択できません: "+ex.Message);}
        var open=app.Workspace.CurrentEditor as ISequenceDiagram;
        var diagram=open!=null && open.Model!=null && open.Model.Id==root?open:DiagramOf(project,root);
        if(!diagram.Lifelines.Any() && model.Lifelines.Any())
            throw new InvalidOperationException("図が読み込まれていません（モデルには参加者がありますが図形が0件です）。この図を開いてから再検証してください。");
        return diagram;
    }
    static int Compare(IApplication app,ISequenceDiagram diagram,string after)
    {
        SequenceSyncRuntime.BatchDiagram=diagram;SequenceSyncRuntime.BatchInput=after;
        SequenceSyncRuntime.Preview(app);
        return SequenceSyncRuntime.LastChanges;
    }
    // PlantUmlTool names an exported file after its diagram, with these characters replaced.
    static string FileNameOf(string name)
    {
        var invalid=new HashSet<char>(Path.GetInvalidFileNameChars());
        var b=new StringBuilder();
        foreach(char c in name??"")b.Append(invalid.Contains(c) || c==' '?'_':c);
        string result=b.ToString().Trim('_','.');
        if(result.Length==0)result="sequence";
        return result.Length>100?result.Substring(0,100):result;
    }
    // Every PlantUML file in a folder exported by PlantUmlTool, compared unedited with the
    // diagram it came from. Nothing is written or saved, so it can run on a real project.
    static void RoundTrip(IApplication app,IProject project,string folder)
    {
        string title=SequenceExperiment.Title;
        var diagrams=SequenceMappedUpdate.Tree(project.DesignModel).OfType<IInteraction>()
            .Where(m=>m.GetEditors().OfType<ISequenceDiagram>().Any())
            .GroupBy(m=>FileNameOf(m.Name),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var files=Directory.GetFiles(folder,"*.puml").OrderBy(f=>f,StringComparer.OrdinalIgnoreCase).Take(300).ToArray();
        var rows=new List<string>();var detail=new StringBuilder();var clock=System.Diagnostics.Stopwatch.StartNew();
        int passed=0;
        try
        {
            SequenceExperiment.BatchMode=true;SequenceSyncRuntime.Batch=true;
            foreach(string file in files)
            {
                string name=Path.GetFileNameWithoutExtension(file),result;
                IInteraction[] found;
                if(!diagrams.TryGetValue(name,out found))result="対応する図なし（名前が重複して出力名にハッシュが付いたものを含む）";
                else if(found.Length>1)result="同じ名前の図が"+found.Length+"枚あり、対応を決められません";
                else
                {
                    try
                    {
                        int changes=Compare(app,Loaded(app,project,found[0].Id,detail),file);
                        if(changes==0){result="差分0件";passed++;}
                        else
                        {
                            result=changes<0?"照合できず: "+Line(SequenceExperiment.Summary,160):"差分 "+changes+"件";
                            detail.AppendLine("■ "+name+"\n"+string.Join("\n",SequenceExperiment.Details.Split('\f').Where(page=>!page.StartsWith("接続の実測",StringComparison.Ordinal)))+"\n");
                        }
                    }
                    catch(Exception ex){result="停止: "+Line(ex.Message,160);detail.AppendLine("■ "+name+"\n"+ex+"\n");}
                }
                rows.Add(name+" | "+result);
            }
        }
        finally {SequenceExperiment.BatchMode=false;SequenceSyncRuntime.Batch=false;SequenceSyncRuntime.BatchDiagram=null;SequenceSyncRuntime.BatchInput=null;}
        SequenceExperiment.Summary="既存図の往復確認（書込みなし）: "+passed+"/"+files.Length+"件 差分0件 / "+(clock.ElapsedMilliseconds/1000)+"秒\n"+string.Join("\n",rows);
        SequenceExperiment.Details=SequenceExperiment.Summary+"\f"+detail;
        app.Window.UI.ShowInformationDialog(SequenceExperiment.Summary.Length>3000?SequenceExperiment.Summary.Substring(0,3000)+"\n…（続きは診断表示）":SequenceExperiment.Summary,title);
    }
    public static void Run(IApplication app)
    {
        string title=SequenceExperiment.Title;
        var project=app.Workspace.CurrentProject;
        if(project==null || !(app.Workspace.CurrentEditor is ISequenceDiagram))
        {app.Window.UI.ShowInformationDialog("シーケンス図を開いてから実行してください。新しい図はその図と同じ親に作ります。",title);return;}
        string list=app.Window.UI.ShowOpenFileDialog("シナリオ一覧、または出力済み PlantUML のどれか1つ",
            "シナリオ一覧・出力済み PlantUML (*.txt;*.puml)|*.txt;*.puml");
        if(string.IsNullOrEmpty(list))return;
        if(string.Equals(Path.GetExtension(list),".puml",StringComparison.OrdinalIgnoreCase)){RoundTrip(app,project,Path.GetDirectoryName(list));return;}
        string folder=Path.GetDirectoryName(list),resultPath=Path.ChangeExtension(list,".result.tsv");
        var scenarios=new List<string[]>();
        foreach(var raw in File.ReadAllLines(list,new UTF8Encoding(false,true)))
        {
            string line=raw.Trim();
            if(line.Length==0 || line.StartsWith("#",StringComparison.Ordinal))continue;
            var parts=line.Split('|').Select(p=>p.Trim()).ToArray();
            if(parts.Length!=3){app.Window.UI.ShowInformationDialog("シナリオの行の形が違います（名前 | before | after）: "+line,title);return;}
            scenarios.Add(new[]{parts[0],Path.Combine(folder,parts[1]),Path.Combine(folder,parts[2])});
        }
        bool apply=app.Window.UI.ShowConfirmDialog("シナリオ "+scenarios.Count+"件。\n"
            +"「OK」: 実験用のコピーのプロジェクトで、各シナリオの図を新しく作り、反映して照合します。途中でプロジェクトを自動保存します。\n"
            +"「キャンセル」: 前回の実行で作った図を、保存せずに再検証します（開き直した後に使います）。",title);
        var rows=new List<string>();var detail=new StringBuilder();var created=new List<string>();
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var previous=new Dictionary<string,string>();
        if(!apply)
        {
            if(!File.Exists(resultPath)){app.Window.UI.ShowInformationDialog("前回の実行結果がありません: "+resultPath,title);return;}
            foreach(var row in File.ReadAllLines(resultPath,new UTF8Encoding(false)))
            {var cells=row.Split('\t');if(cells.Length>=2 && cells[1].Length>0)previous[cells[0]]=cells[1];}
        }
        try
        {
            SequenceExperiment.BatchMode=true;SequenceSyncRuntime.Batch=true;
            var roots=new Dictionary<string,string>();
            if(apply)
            {
                // Every diagram first, then one save: importing does not need the export,
                // so each scenario then costs one export and one save instead of two saves.
                var importClock=System.Diagnostics.Stopwatch.StartNew();
                foreach(var s in scenarios)
                {
                    SequenceExperiment.BatchInput=s[1];SequenceExperiment.LastRoot=null;
                    try{SequenceExperiment.Run(app,true);}catch(Exception ex){detail.AppendLine("■ "+s[0]+" 取込\n"+ex+"\n");}
                    if(SequenceExperiment.LastRoot!=null)roots[s[0]]=SequenceExperiment.LastRoot;
                    else detail.AppendLine("■ "+s[0]+" 取込: "+SequenceExperiment.Summary+"\n");
                }
                long imported=importClock.ElapsedMilliseconds;
                var saveClock=System.Diagnostics.Stopwatch.StartNew();
                Save(app,project);
                long saved=saveClock.ElapsedMilliseconds;
                // One export for every scenario: each diagram is cut out of it in its turn,
                // so no save is needed between them.
                var exportClock=System.Diagnostics.Stopwatch.StartNew();
                var host=(app.Workspace.CurrentEditor as ISequenceDiagram).Model;
                string file=Path.Combine(Path.GetTempPath(),"SequenceBatch-"+Guid.NewGuid().ToString("N")+".nmdl");
                try
                {
                    project.UnitManager.ExportModelUnit(host.ModelUnit,file);
                    SequenceEditorCapture.BatchSource=SequenceJson.Parse(File.ReadAllText(file,new UTF8Encoding(false,true)));
                }
                finally{try{File.Delete(file);}catch(Exception){}}
                foreach(var pair in roots)
                {
                    var made=project.GetModelById(pair.Value);
                    if(made==null || made.ModelUnit==null || !ReferenceEquals(made.ModelUnit,host.ModelUnit) && made.ModelUnit.TopElementId!=host.ModelUnit.TopElementId)
                        throw new InvalidOperationException("作った図が開いている図と別のモデルユニットにあります: "+pair.Key);
                }
                rows.Add("（取込 "+roots.Count+"/"+scenarios.Count+"件 "+(imported/1000)+"秒 / 保存 "+(saved/1000)+"秒 / 書き出し "+(exportClock.ElapsedMilliseconds/1000)+"秒）");
            }
            else roots=previous;
            int failedInRow=0;bool anyPassed=false;
            foreach(var s in scenarios)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();
                string root,result,timing="";bool failed=false;
                try
                {
                    if(!roots.TryGetValue(s[0],out root))throw new InvalidOperationException(apply?"取込に失敗しました（診断表示）。":"前回の実行で図が作られていません。");
                    if(apply)
                    {
                        SequenceSyncRuntime.BatchDiagram=DiagramOf(project,root);SequenceSyncRuntime.BatchInput=s[2];
                        SequenceSyncRuntime.Preview(app,true,true,true,true);
                        bool committed=SequenceSyncRuntime.LastCommitted;
                        string reasons=SequenceSyncRuntime.LastReasons,summary=SequenceExperiment.Summary;
                        timing=" / 反映 "+(watch.ElapsedMilliseconds/1000)+"秒";
                        detail.AppendLine("■ "+s[0]+"\n"+summary+"\n");
                        // The whole summary goes on: the row picks the mismatch out of it.
                        if(!committed)throw new InvalidOperationException("反映: "+(reasons.Length>0?reasons:summary));
                    }
                    int changes=Compare(app,apply?DiagramOf(project,root):Loaded(app,project,root,detail),s[2]);
                    // What the comparison found goes to the details, so a difference can be read.
                    if(changes!=0)detail.AppendLine("■ "+s[0]+" 比較\n"+string.Join("\n",SequenceExperiment.Details.Split('\f').Where(page=>!page.StartsWith("接続の実測",StringComparison.Ordinal)))+"\n");
                    // Applied but still different is that scenario's own problem, not the batch's.
                    failed=changes<0;
                    result=changes==0?"成功":changes<0?"照合できず: "+Line(SequenceExperiment.Summary,160):"差分 "+changes+"件";
                    // Applying the same input again has to find nothing to do and write nothing.
                    if(apply && changes==0)
                    {
                        var diagram=DiagramOf(project,root);
                        string shapesBefore=string.Join("|",diagram.Shapes.Select(sh=>sh.Id).OrderBy(x=>x,StringComparer.Ordinal));
                        SequenceSyncRuntime.BatchDiagram=diagram;SequenceSyncRuntime.BatchInput=s[2];
                        SequenceSyncRuntime.Preview(app,true,true,true,true);
                        bool again=SequenceSyncRuntime.LastCommitted;
                        string shapesAfter=string.Join("|",DiagramOf(project,root).Shapes.Select(sh=>sh.Id).OrderBy(x=>x,StringComparer.Ordinal));
                        if(again || SequenceSyncRuntime.LastChanges!=0 || shapesBefore!=shapesAfter)
                        {
                            result="2回目の反映で変化: 差分 "+SequenceSyncRuntime.LastChanges+"件 / 確定="+again;
                            detail.AppendLine("■ "+s[0]+" 2回目の反映\n"+SequenceExperiment.Summary+"\n");
                        }
                        else result+="（2回目: 変化なし）";
                    }
                }
                catch(Exception ex)
                {
                    failed=true;
                    // A mismatch says where in its breakdown; that part is what the row has room for.
                    int at=ex.Message.IndexOf("relation=",StringComparison.Ordinal);
                    if(at<0)at=ex.Message.IndexOf("shape=",StringComparison.Ordinal);
                    result="停止: "+(at>=0?Line(ex.Message.Substring(0,Math.Min(80,ex.Message.Length)),80)+" … "+Line(ex.Message.Substring(at),420):Line(ex.Message,200));
                    detail.AppendLine("■ "+s[0]+"\n"+ex+"\n");
                }
                rows.Add(s[0]+" | "+result+" | "+(watch.ElapsedMilliseconds/1000)+"秒"+timing);
                string kept;roots.TryGetValue(s[0],out kept);created.Add(s[0]+"\t"+(kept??""));
                // Two failures in a row almost always share a cause in the batch itself;
                // running the rest would only repeat it.
                failedInRow=failed?failedInRow+1:0;
                // Only a batch that has not got one scenario through is broken as a whole; after
                // that, a failure is that scenario's own and the rest still run.
                if(!failed)anyPassed=true;
                if(apply && !anyPassed && failedInRow>=2 && scenarios.IndexOf(s)<scenarios.Count-1)
                {rows.Add("2件続けて失敗したため、残り "+(scenarios.Count-1-scenarios.IndexOf(s))+"件を実行せずに中断しました。");break;}
            }
        }
        catch(Exception ex){rows.Add("中断: "+Line(ex.Message,200));detail.AppendLine(ex.ToString());}
        finally {SequenceEditorCapture.BatchSource=null;SequenceExperiment.BatchMode=false;SequenceExperiment.BatchInput=null;SequenceSyncRuntime.Batch=false;SequenceSyncRuntime.BatchDiagram=null;SequenceSyncRuntime.BatchInput=null;}
        if(apply)
        {
            try{File.WriteAllLines(resultPath,created,new UTF8Encoding(false));}
            catch(Exception ex){rows.Add("前回結果の保存に失敗: "+ex.Message);}
            // Saved once at the end, with what the product says before and after, since the
            // last run's changes did not come back after reopening.
            try
            {
                bool dirtyBefore=project.HasUnsavedChanges();
                var saveClock=System.Diagnostics.Stopwatch.StartNew();
                Save(app,project);
                rows.Add("（最後の保存 "+(saveClock.ElapsedMilliseconds/1000)+"秒 / 保存前の未保存変更="+dirtyBefore+" 保存後="+project.HasUnsavedChanges()+"）");
            }
            catch(Exception ex){rows.Add("最後の保存に失敗: "+ex.Message);}
        }
        int passed=rows.Count(r=>r.Contains(" | 成功 | ") || r.Contains(" | 成功（2回目: 変化なし） | "));
        SequenceExperiment.Summary=(apply?"シナリオ一括検証（反映）":"シナリオ一括検証（再検証）")+": "+passed+"/"+scenarios.Count+"件成功 / "+(clock.ElapsedMilliseconds/1000)+"秒\n"
            +string.Join("\n",rows)+(apply?"\n\nプロジェクトを閉じて開き直し、もう一度このボタンで「キャンセル」（再検証）を選んでください。":"");
        SequenceExperiment.Details=SequenceExperiment.Summary+"\f"+detail;
        app.Window.UI.ShowInformationDialog(SequenceExperiment.Summary,title);
    }
}
