// Native reader for the semantic planner. It performs no project mutations.
public sealed class DiagramSnapshot
{
    public SequenceDocument Document=new SequenceDocument{HasTitle=true};
    public Dictionary<string,double> Y=new Dictionary<string,double>();
    public Dictionary<string,string> ShapeIds=new Dictionary<string,string>();
    public Dictionary<string,object> Geometry=new Dictionary<string,object>();
    public List<string> Limitations=new List<string>();
    // A message tied to an operation shows a label the product builds from it ("EndProcess :
    // void", "sleep(100ms)"), while its model keeps the plain name. An open diagram reads the
    // label, the export (which reads diagrams as not shown) writes the name. Kept per message
    // model where the two differ, so either reads as unchanged (AlignOperationLabels).
    public Dictionary<string,string> ModelNames=new Dictionary<string,string>();
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
                // The first branch starts at the frame's top edge, as the export has it: anything drawn
                // in the frame's head area is in the first branch.
                double from=i==0?f.LocationY:top;
                operandRegions.Add(new SequenceRegion{Id=operand.ModelId,Fragment=f.ModelId,X=f.LocationX,Y=from,Width=f.Width,Height=bottom-from});
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
            if(model.Name!=null && SequenceLabels.Fold(model.Name)!=SequenceLabels.Fold(m.Text))snapshot.ModelNames[model.Id]=model.Name;
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
                else { e.Links["anchors"]=n.NoteAnchors.Select(a=>a.Source.Id==n.Id?a.Target.ModelId:a.Source.ModelId).Where(id=>id!=null && id!=n.ModelId).Distinct().ToArray();snapshot.Limitations.Add("Noteの非ライフライン接続: "+n.ModelId); }
            }
            e.Links["targets"]=targets.Distinct().OrderBy(id=>snapshot.Y[id]).ToArray();
            // A note may be tied to the frame or to something this reading does not keep (a message end, the
            // frame itself); the comparison does not use anchors, so only what the reading has stays.
            if(e.Links.ContainsKey("anchors"))e.Links["anchors"]=e.Links["anchors"].Where(id=>doc.Elements.Any(x=>x.Id==id)).ToArray();
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
        var byPosition=SequenceRegion.Nesting(operandRegions,fragmentRegions,annotationRegions).ToList();
        // Where the drawing decides (the user's decision), the model's relations are not asked:
        // a frame, note or ref the position places, and a message drawn in one branch or none.
        var placed=new HashSet<string>(byPosition.Select(m=>m.Child));
        foreach(var m in diagram.Messages)if(SequenceRegion.BranchAt(operandRegions,m.SourceY)!=null)placed.Add(m.ModelId);
        memberships.RemoveAll(m=>placed.Contains(m.Child));
        memberships.AddRange(byPosition);
        SequenceMembership.Resolve(doc,memberships,line=>log.AppendLine(line));

        Func<double,double,string> containerAt=(x,y)=>{
            var candidates=operandRegions.Where(r=>x>=r.X && x<=r.X+r.Width && y>=r.Y && y<r.Y+r.Height-1.0).ToArray();
            var nearest=candidates.Where(r=>!candidates.Any(inner=>inner.Id!=r.Id && SequenceRegion.Contains(r,inner))).ToArray();
            if(nearest.Length>1) {snapshot.Limitations.Add("実行区間境界の所属候補が複数");return root.Id;}
            return nearest.Length==1?nearest[0].Id:root.Id;
        };
        // A message is in the branch it is drawn in, as the export writes it, even where the model
        // relates it to another (a frame stretched over it by hand): the drawing wins.
        foreach(var m in diagram.Messages)
        {
            string branch=SequenceRegion.BranchAt(operandRegions,m.SourceY);
            if(branch==null)continue;
            string was=byId[m.ModelId].Parent;
            byId[m.ModelId].Parent=branch.Length==0?root.Id:branch;
            if(was!=byId[m.ModelId].Parent)log.AppendLine("Message placed by its position: "+m.ModelId+" "+was+" → "+byId[m.ModelId].Parent);
        }
        // A destruction has no relation to the branch it is drawn in; the export puts it there by
        // where it is, so the reading does too.
        foreach(var d in diagram.Destructions)byId[d.ModelId].Parent=containerAt(d.LocationX+d.Width/2,d.LocationY);
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
            int order=0;foreach(var e in group.OrderBy(e=>e.Kind=="participant"?0:1).ThenBy(e=>snapshot.Y[e.Id]).ThenBy(e=>SequenceDocument.ExportRank(e.Kind)).ThenBy(e=>e.Id,StringComparer.Ordinal))e.Order=order++;
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
    // A message whose input text is its model's name, where the diagram shows the label built
    // from its operation, is unchanged: it reads as that name (see DiagramSnapshot.ModelNames).
    internal static void AlignOperationLabels(DiagramSnapshot snapshot,SequenceDocument desired)
    {
        if(snapshot.ModelNames.Count==0)return;
        var written=new HashSet<string>(desired.Elements.Where(d=>d.Kind=="message").Select(d=>SequenceLabels.Fold(d.Text)));
        foreach(var e in snapshot.Document.Elements.Where(e=>e.Kind=="message"))
        {
            string name;
            if(!snapshot.ModelNames.TryGetValue(e.Id,out name))continue;
            if(written.Contains(SequenceLabels.Fold(name)) && !written.Contains(SequenceLabels.Fold(e.Text)))e.Text=name;
        }
    }
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
    // Set by a caller that may save the project before an update without asking, such as an
    // MCP tool acting for the user. Otherwise the user is asked each time.
    public static bool SaveBeforeUpdate;
    // Set by a caller that updates without saving and without asking, such as an MCP tool: the
    // snapshot is built from the SDK instead of the export (see SequenceSnapshotBuilder).
    public static bool UpdateWithoutSaving;
    // Set by the PlantUmlTool ribbon: the result reads as a product's, without the trial's
    // case names and checklists (those stay for the SequenceImportProbe dev extension).
    public static bool Plain;
    // Set by the SequenceImportProbe only: rewrites a snapshot built from the SDK, to find which
    // of its differences from the export keeps Ctrl+Z from bringing deleted elements back.
    public static Func<string,IProject,IInteraction,ISequenceDiagram,StringBuilder,string> SnapshotOverlay;
    // Whether the last update took its snapshot from the SDK (unsaved project).
    internal static bool LastFromSdk;
    // Every update takes the snapshot from the SDK, saved or not: to test that path in the batch.
    public static bool ForceSdkSnapshot;
    static bool UseSdkSnapshot(IApplication app)
    {
        if(UpdateWithoutSaving)return true;
        if(SaveBeforeUpdate || Batch)return false;
        return app.Window.UI.ShowConfirmDialog("プロジェクトに未保存の変更があります。\n保存せずに反映しますか？（試験中）\n図の写しを SDK から組み立てて反映します。図形の色・形・参加者の余白などの見た目の細部が既定に戻ることがあります。反映後の照合で食い違えば元に戻して止まります。\n\nOK: 保存せずに反映する\nキャンセル: 保存して反映するかを次に確認する",SequenceExperiment.Title);
    }
    static bool SaveFirst(IApplication app,IProject project,StringBuilder log)
    {
        if(!SaveBeforeUpdate)
        {
            if(Batch)return false;
            if(!app.Window.UI.ShowConfirmDialog("プロジェクトに未保存の変更があります。\n反映の前に図の写しを取るため、保存が必要です（作業中の他の変更も一緒に保存されます）。\n\nOK: 保存して反映を続ける\nキャンセル: 中止する（何も変更しません）",SequenceExperiment.Title))return false;
        }
        if(!app.Workspace.SaveProject(project,false))throw new InvalidOperationException("S220: プロジェクトを保存できませんでした。");
        log.AppendLine("反映の前にプロジェクトを保存しました（"+(SaveBeforeUpdate?"呼び出し元が許可":"利用者が承認")+"）。");
        return true;
    }
    static bool Unsaved(IProject project)
    {
        try {return project.HasUnsavedChanges() || (project.DesignModel!=null && project.DesignModel.IsDirty);}
        catch(Exception) {return false;}
    }
    // Milliseconds per stage of the last run, shown with its result so a slow step can be
    // named from a measurement rather than guessed.
    internal static readonly List<string> Timings=new List<string>();
    // Set by the scenario batch (SequenceImportProbe only; public so PlantUmlTool, which never
    // sets them, still builds): the diagram and input to use without asking, and what the
    // last run found.
    public static bool Batch;
    public static ISequenceDiagram BatchDiagram;
    public static string BatchInput;
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
            AlignOperationLabels(current,desired);
            var plan=SequenceNotePolicy.Build(current.Document,desired,()=>Guid.NewGuid().ToString());
            LastChanges=plan.Changes.Count;
            var preflight=SequenceStructurePreflight.Check(current.Document,plan);
            Lap("差分計画");
            // The snapshot goes through ExportModelUnit, which refuses to run while the
            // project has unsaved changes. The project is saved only when the caller allows
            // it (SaveBeforeUpdate, for a caller such as an MCP tool) or the user agrees here;
            // otherwise the run stops before any work.
            bool mayRetry=false,fromSdk=prepare && ForceSdkSnapshot;
            if(prepare && !fromSdk && SequenceEditorCapture.BatchSource==null && Unsaved(project))
            {
                if(UseSdkSnapshot(app))fromSdk=true;
                else if(!SaveFirst(app,project,log))throw new InvalidOperationException(UnsavedAdvice);
            }
            else mayRetry=prepare && !fromSdk && SequenceEditorCapture.BatchSource==null;

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
            if(retain && same)SequenceExperiment.Summary="図は入力と一致しています。反映する差分はありません。"+(Plain?"":"\n"+SequenceAudit.Summary(plan,current.Limitations.Count));
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
                    if(fromSdk)
                    {
                        exported=SequenceSnapshotBuilder.Build(project,root,diagram,SequenceSnapshotBuilder.Schema(project),log);
                        log.AppendLine("写し: SDK から組み立て（保存なし）");
                        if(SnapshotOverlay!=null)exported=SnapshotOverlay(exported,project,root,diagram,log);
                    }
                    else try {SequenceEditorCapture.Read(project,root,diagram,log,delegate(string value){exported=value;});}
                    catch(Exception ex)
                    {
                        // The export refuses on a dirty project even when nothing is savable,
                        // for instance right after a trial has rolled its changes back. Saving
                        // clears that; it is offered once, like above.
                        if(ex.Message.IndexOf("保存",StringComparison.Ordinal)<0)throw;
                        if(!mayRetry || !SaveFirst(app,project,log))throw new InvalidOperationException(UnsavedAdvice,ex);
                        SequenceEditorCapture.Read(project,root,diagram,log,delegate(string value){exported=value;});
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
                            foreach(string sort in new[]{"sync","async","reply","create"})
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
                    // A renamed message tied to an operation shows whatever label the product builds.
                    preparation.LooseTextShapeIds=preparation.Renamed.Where(r=>current.ModelNames.ContainsKey(r[0]) && current.ShapeIds.ContainsKey(r[0]))
                        .Select(r=>current.ShapeIds[r[0]]).ToArray();
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
                        // Every shape is compared rounded, whichever snapshot: an editor import also stores
                        // a hand-drawn "42" back as 42.000001 (3.2.4 on the device, saved project).
                        SequenceStructureTrial.RoundAllShapes=true;LastFromSdk=fromSdk;
                        try {SequenceExperiment.Summary=SequenceStructureTrial.Run(app,project,diagram,preparation,plan,exported,directory,log,retain,reconnectCommit);}
                        finally {SequenceStructureTrial.RoundAllShapes=false;}
                        screenshot=SequenceExperiment.Summary+"\f試行診断\n"+log.ToString();
                    }
                }
            }
        }
        catch(Exception ex) {SequenceExperiment.Summary=(LastCommitted?"図への反映は確定しましたが、その後の処理で止まりました。図を確認し、おかしければ保存せずに開き直してください。":Plain?"反映できませんでした。図は変更していません。":trial?"構造更新の試行を完了できませんでした。診断表示を確認してください。":prepare?"構造更新データの準備を完了できませんでした。図への反映なし。":"図全体の読取り検証を完了できませんでした。")+"\n"+ex.Message;log.AppendLine(ex.ToString());screenshot=null;}
        try
        {
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NextDesign.SequenceSync","reports");
            Directory.CreateDirectory(directory);
            string stem=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
            File.WriteAllText(stem+".txt",log.ToString(),new UTF8Encoding(false));
            if(report!=null)File.WriteAllText(stem+".json",report,new UTF8Encoding(false));
            if(screenshot==null || (Plain && LastChanges>0 && !LastCommitted))SequenceExperiment.Summary+="\n診断: "+stem+".txt";
        }
        catch(Exception ex) {log.AppendLine("診断の保存失敗: "+ex.Message);SequenceExperiment.Summary+=Plain?"\n診断ファイルを保存できませんでした。":"\n診断ファイルを保存できませんでした。診断表示で確認してください。";}
        SequenceExperiment.Details=screenshot??log.ToString();
        if(!Batch)SequenceExperiment.Show(app);
    }
}

public static class SequenceStructureTrial
{
    // Set while an update runs on a snapshot built from the SDK.
    internal static bool RoundAllShapes;
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
        var inTree=new HashSet<string>(tree.Select(m=>m.Id));
        Action<IModel> record=m=>state.Models[m.Id]=PumlBuild.Json(new[]{m.Metaclass.Id,m.Name,m.Owner==null?"":m.Owner.Id,m.IsDeleted.ToString()});
        foreach(var model in tree)
        {
            record(model);
            foreach(var r in model.GetRelationsWhere((relation,field)=>true))
            {
                state.Relations[r.Id]=new[]{r.Source.Id,r.Target.Id,r.SourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),r.TargetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                state.RelationFields[r.Id]=PumlBuild.Json(new[]{FieldId(r.SourceField),FieldId(r.TargetField)});
                // Only models of this diagram: an operation a message refers to is not part of it,
                // and goes out of reach once the message is deleted.
                if(inTree.Contains(r.Source.Id))record(r.Source);if(inTree.Contains(r.Target.Id))record(r.Target);
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
    // What the PlantUmlTool ribbon shows: what changed, or why nothing did. The trial's own
    // summary (the case, stage and checks) is kept below it for a stop.
    static string PlainSummary(SequenceStructurePreparation prepared,int reconnectCount,bool committed,string trialSummary)
    {
        if(!committed)
        {
            bool restored=trialSummary.Contains("復元照合: 一致");
            string cause=trialSummary.Split('\n').FirstOrDefault(l=>l.StartsWith("原因: ",StringComparison.Ordinal));
            return "反映できませんでした。"+(restored?"図は元のままです。":"元に戻ったことを確認できませんでした。保存せずにプロジェクトを開き直してください。")
                +(cause!=null?"\n"+cause:"");
        }
        var counts=new List<string>();
        Action<string,int> add=(label,n)=>{if(n>0)counts.Add(label+" "+n);};
        add("メッセージ追加",prepared.AddedMessages.Length);add("メッセージ削除",prepared.DeleteMessageIds.Length);
        add("参加者追加",prepared.AddedParticipants.Length);add("参加者削除",prepared.DeleteParticipantIds.Length);
        add("実行区間追加",prepared.AddedExecutions.Length);add("実行区間削除",prepared.DeleteIds.Length);add("接続の変更",reconnectCount);
        add("フラグメント追加",prepared.AddedFragments.Length);add("分岐追加",prepared.AddedOperands.Length);add("フラグメント・分岐の削除",prepared.DeleteFrameIds.Length);
        add("Note・ref追加",prepared.AddedNotes.Length);add("Note削除",prepared.DeleteNoteIds.Length);add("ref削除",prepared.DeleteRefIds.Length);
        add("破棄の削除",prepared.DeleteDestroyIds.Length);add("本文の変更",prepared.Renamed.Length);add("種別の変更",prepared.SortChanged.Length);
        add("枠で囲んだメッセージ",prepared.MovedMessages.Length);
        return "図へ反映しました。"+(counts.Count>0?"\n"+string.Join(" / ",counts):"")
            +"\nプロジェクトは保存していません。"
            // On the device (3.2.3 / 3.2.5): an update of a saved project undoes with one Ctrl+Z (the
            // diagram shows it once reopened). One made from the SDK snapshot (unsaved) does not
            // bring deleted elements back, and the next Ctrl+Z stops the product. Adding messages
            // or frames stops it on Undo either way (A15).
            +(SequenceSyncRuntime.LastFromSdk
                ?"\n注意: 未保存のプロジェクトへの反映は Ctrl+Z で戻せません。Ctrl+Z を続けると製品が停止することがあります。取り消すときは保存せずに開き直してください。"
                :prepared.AddedMessages.Length>0 || prepared.AddedFragments.Length>0 || prepared.AddedNotes.Length>0
                ?"\n注意: メッセージ・フラグメント・Note・ref を追加した反映は Ctrl+Z で戻せません（戻すと製品が停止します。製品側の不具合）。取り消すときは保存せずに開き直してください。"
                :prepared.AddedExecutions.Length>0 || prepared.AddedParticipants.Length>0
                ?"\n注意: 実行区間・参加者を追加した反映を Ctrl+Z で戻せるかは確かめていません。取り消すときは保存せずに開き直してください。"
                :"\nCtrl+Z で戻せます。戻したあとは図を開き直すと表示が更新されます。");
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
        // A snapshot built from the SDK writes back what the SDK read, which the product stores
        // and reads again a few millionths off; every shape is then compared as new ones are.
        if(RoundAllShapes)newShapes=newShapes.Concat(diagram.Shapes.Select(sh=>sh.Id)).Distinct().ToArray();
        var removedModels=prepared.DeleteIds.Concat(prepared.DeleteParticipantIds)
            .Concat(prepared.DeleteMessageIds).Concat(prepared.DeleteFrameIds).Concat(prepared.DeleteNoteIds).Concat(prepared.DeleteRefIds).Concat(prepared.DeleteDestroyIds)
            .Concat(prepared.DeleteEndIds).ToArray();
        var removedSet=new HashSet<string>(removedModels);
        var going=diagram.Shapes.Where(sh=>removedSet.Contains(sh.ModelId)).Select(sh=>sh.Id).ToArray();
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
            // What this update deletes is still there after the first import, where the product may
            // have snapped a hand-drawn 420.75 to 420; it is checked gone after the deletion instead.
            expectedReconnect.Loosen(going,connected);expectedReconnect.LoosenText(prepared.LooseTextShapeIds,connected);
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
            expectedFinal.Loosen(prepared.LooseShapeIds,afterDelete);expectedFinal.LoosenText(prepared.LooseTextShapeIds,afterDelete);
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
                // its content only reaches the undo stack after the handler returns. What
                // CanUndo offers here is the user's own earlier edit, never this update: undoing
                // it (as the trial once did, 3.2.2) took back the user's work and failed with
                // an index out of range. Undo/Redo is checked by hand only.
                log.AppendLine("undo availability (not used): project="+project.CanUndo+" workspace="+app.Workspace.CanUndo());
                cycle="\nUndo/Redo: このコマンドの実行中は履歴へ積まれないため自動確認できません。手で1回ずつ確認してください。";
            }
            // A stop says why: the exception, and what the check found when it was a mismatch.
            if(!completion.Committed)
            {
                var cause=completion.ApplyError??completion.CommitError;
                if(cause!=null)cycle+="\n原因: "+cause.GetType().Name+": "+cause.Message+(cause.InnerException!=null?" / "+cause.InnerException.Message:"");
                // Where it was thrown: the frames name the method, ours or the product's, that met the value.
                if(cause!=null)
                {
                    var deepest=cause;while(deepest.InnerException!=null)deepest=deepest.InnerException;
                    var frames=(deepest.StackTrace??"").Split('\n').Select(l=>l.Trim()).Where(l=>l.Length>0).Take(20).ToArray();
                    if(frames.Length>0)cycle+="\n発生箇所:\n"+string.Join("\n",frames);
                }
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
        if(SequenceSyncRuntime.Plain && retain)
        {
            summary=PlainSummary(prepared,reconnectCount,SequenceSyncRuntime.LastCommitted,summary);
            log.AppendLine(summary);
            try{SequenceExperiment.Write(Path.Combine(directory,"trial-result.txt"),summary+"\n"+log.ToString());}catch(Exception){}
            return summary;
        }
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

// The diagram's snapshot built from what the SDK reads live, for an update of a project with
// unsaved changes (ExportModelUnit refuses then). Laid out like the export and like the
// generator's payload. What the SDK cannot read is left out: part of Style, LeftPadding and
// IsRightAtFrame, which an editor import keeps or sets to their defaults (K217).
public static class SequenceSnapshotBuilder
{
    static string N(double v){return v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
    static SequenceJson J(object v)
    {
        if(v==null)return null;
        if(v is bool)return SequenceJson.Parse((bool)v?"true":"false");
        if(v is int || v is long || v is short)return SequenceJson.Parse(Convert.ToInt64(v).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(v is double || v is float || v is decimal)return SequenceJson.Parse(N(Convert.ToDouble(v)));
        if(v is string)return SequenceJson.Parse(SequencePayload.Q((string)v));
        var model=v as IModel;if(model!=null)return SequenceJson.Parse(SequencePayload.Q(model.Id));
        return SequenceJson.Parse(SequencePayload.Q(v.ToString()));
    }
    static SequenceJson O(){return new SequenceJson{Properties=new Dictionary<string,SequenceJson>(StringComparer.Ordinal)};}
    static void P(SequenceJson o,string k,object v){var j=J(v);if(j!=null)o.Properties[k]=j;}
    // A field's value in the JSON type its field declares: the SDK can hand a number or a flag
    // back as text, and the import refuses a text where it expects a number (0.11.10 batch:
    // InvalidCastException String to Double). A value that does not read as its type is left out.
    static void Field(SequenceJson o,IField f,object v)
    {
        string type=(f.Type??"").ToLowerInvariant();
        // Anything other than a plain value (a wrapper the SDK hands back) is read as its text.
        var text=v as string;
        if(text==null && v!=null && !(v is bool) && !(v is int) && !(v is long) && !(v is short) && !(v is double) && !(v is float) && !(v is decimal) && !(v is IModel))text=v.ToString();
        bool numeric=type.Contains("double") || type.Contains("float") || type.Contains("decimal") || type.Contains("int") || type.Contains("long") || type=="number";
        if(text!=null && numeric)
        {
            double number;
            if(double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out number))P(o,f.Name,number);
            return;
        }
        if(text!=null && type.Contains("bool"))
        {
            bool flag;if(bool.TryParse(text,out flag))P(o,f.Name,flag);
            return;
        }
        P(o,f.Name,v);
    }
    // The SDK reads some stored coordinates 1e-6 over their value and some not, and taking the
    // offset off made them drift the other way (0.11.9), so the values go back as the SDK reads
    // them and the trial compares every shape rounded (SequenceStructureTrial.RoundAllShapes).
    static void G(SequenceJson o,string k,double v){P(o,k,v);}
    // The collection a shape is kept in, as the generator writes it.
    public static string Collection(ISequenceShape shape)
    {
        if(shape is IFrameShape)return "Frame";
        if(shape is ILifelineShape)return "Lifelines";
        if(shape is IExecutionSpecificationShape)return "ExecutionSpecifications";
        if(shape is IMessageShape)return "Messages";
        if(shape is IFragmentShape)return "Fragments";
        if(shape is IOperandShape)return "Operands";
        if(shape is IInteractionUseShape)return "InteractionUses";
        if(shape is INoteShape)return "Notes";
        if(shape is IMessageEndShape)return "MessageEnds";
        if(shape is IDestructionShape)return "Destructions";
        if(shape is INoteAnchorShape)return "NoteAnchors";
        return "?"+shape.GetType().Name;
    }
    static string EntityType(IModel m,Dictionary<string,ISequenceShape> shapeOf,string root)
    {
        if(m.Id==root)return "Interaction";
        ISequenceShape s;
        if(shapeOf.TryGetValue(m.Id,out s))
        {
            string c=Collection(s);
            switch(c)
            {
                case "Frame":return "Frame";case "Lifelines":return "Lifeline";case "ExecutionSpecifications":return "ExecutionSpecification";
                case "Messages":return "Message";case "Fragments":return "CombinedFragment";case "Operands":return "InteractionOperand";
                case "InteractionUses":return "InteractionUse";case "Notes":return "InteractionNote";case "MessageEnds":return "MessageEnd";case "Destructions":return "Destruction";
            }
        }
        // A model with no shape of its own (a guard's value, for instance) is exported as a plain entity.
        return "Entity";
    }
    public static string Build(IProject project,IInteraction root,ISequenceDiagram diagram,string schema,StringBuilder log)
    {
        var tree=SequenceMappedUpdate.Tree(root).ToList();
        var inTree=new HashSet<string>(tree.Select(m=>m.Id));
        var shapes=diagram.Shapes.ToList();
        var shapeOf=new Dictionary<string,ISequenceShape>(StringComparer.Ordinal);
        foreach(var s in shapes)if(s.ModelId!=null && !shapeOf.ContainsKey(s.ModelId))shapeOf[s.ModelId]=s;
        var entities=new List<SequenceJson>();var relations=new List<SequenceJson>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var m in tree)
        {
            var e=O();P(e,"Id",m.Id);P(e,"EntityType",EntityType(m,shapeOf,root.Id));P(e,"MetamodelId",m.Metaclass==null?null:m.Metaclass.Id);
            var fields=O();
            if(m.Metaclass!=null)
                foreach(var f in m.Metaclass.GetFields().Cast<IField>().Where(f=>f.RelationshipClass==null))
                {
                    try {Field(fields,f,m.GetField(f.Name));}
                    catch(Exception ex){log.AppendLine("snapshot field "+f.Name+": "+ex.GetType().Name);}
                }
            string name=m.Name;
            if(string.IsNullOrEmpty(name) && fields["Name"]!=null && fields["Name"].Raw.StartsWith("\""))name=fields["Name"].StringValue();
            P(e,"Name",name??"");
            e.Properties["Fields"]=fields;
            entities.Add(e);
            foreach(var r in m.GetRelationsWhere((relation,field)=>true))
            {
                if(!seen.Add(r.Id))continue;
                var o=O();P(o,"Id",r.Id);P(o,"RelationType",r.IsEmbedded?"Embed":"Ref");P(o,"MetamodelId",r.Metaclass==null?null:r.Metaclass.Id);
                P(o,"SourceId",r.Source.Id);P(o,"TargetId",r.Target.Id);P(o,"SourceIndex",r.SourceIndex);P(o,"TargetIndex",r.TargetIndex);
                if(r.IsDerivation)P(o,"IsDerivation",true);
                relations.Add(o);
            }
        }
        var editor=O();
        P(editor,"Id",diagram.Id);P(editor,"ViewType","SequenceDiagram");
        P(editor,"MetamodelId","DensoCreate.Indio.IMF.Extensions.Sequence.ViewInstance.SequenceDiagramViewInstance");
        P(editor,"DefinitionId",diagram.EditorDefinition==null?null:diagram.EditorDefinition.Id);P(editor,"ModelId",root.Id);
        var lists=new Dictionary<string,List<SequenceJson>>(StringComparer.Ordinal);
        foreach(var s in shapes)
        {
            var o=O();P(o,"Id",s.Id);P(o,"ModelId",s.ModelId);
            var node=s as ISequenceNodeShape;
            string c=Collection(s);
            if(node!=null && c!="Operands"){G(o,"X",node.LocationX);if(c!="Lifelines")G(o,"Y",node.LocationY);G(o,"Width",node.Width);if(c!="Lifelines")G(o,"Height",node.Height);}
            var bar=s as IExecutionSpecificationShape;if(bar!=null){G(o,"Length",bar.Length);G(o,"Height",bar.Length);}
            var wire=s as IMessageShape;if(wire!=null){G(o,"SourceY",wire.SourceY);G(o,"TargetY",wire.TargetY);G(o,"SelfloopBendsX",wire.SelfloopBendsX);}
            var branch=s as IOperandShape;if(branch!=null)G(o,"Position",branch.Position);
            var lane=s as ILifelineShape;if(lane!=null)G(o,"LaneLength",lane.TimelineLength);
            var anchor=s as INoteAnchorShape;if(anchor!=null){G(o,"TargetX",anchor.TargetX);G(o,"TargetY",anchor.TargetY);}
            if(c=="Frame"){editor.Properties["Frame"]=o;continue;}
            List<SequenceJson> list;if(!lists.TryGetValue(c,out list))lists[c]=list=new List<SequenceJson>();
            list.Add(o);
        }
        foreach(var pair in lists)editor.Properties[pair.Key]=new SequenceJson{Items=pair.Value};
        var top=O();P(top,"Type","Model");P(top,"SchemaVersion",schema);P(top,"TopElementId",root.Id);
        top.Properties["Entities"]=new SequenceJson{Items=entities};top.Properties["Relations"]=new SequenceJson{Items=relations};
        top.Properties["Editors"]=new SequenceJson{Items=new List<SequenceJson>{editor}};
        log.AppendLine("Snapshot built from the SDK: entities "+entities.Count+", relations "+relations.Count+", shapes "+shapes.Count);
        return top.ToJsonString();
    }
    // The schema the project file declares, as the import reads it.
    public static string Schema(IProject project)
    {
        try
        {
            using(var reader=new StreamReader(project.Path,Encoding.UTF8,true))
            {
                char[] header=new char[4096];int n=reader.Read(header,0,header.Length);
                var match=Regex.Match(new string(header,0,n),"\"SchemaVersion\"\\s*:\\s*\"([0-9]+\\.[0-9]+)\"");
                if(match.Success)return match.Groups[1].Value;
            }
        }
        catch(Exception){}
        return "13.0";
    }
    // Where every shape sits in an editor, as a path of collection names.
    public static Dictionary<string,string> Places(SequenceJson editor)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        Action<SequenceJson,string> walk=null;
        walk=(node,path)=>{
            if(node==null)return;
            if(node.Items!=null){foreach(var i in node.Items)walk(i,path);return;}
            if(node.Properties==null)return;
            if(node["Id"]!=null && node["ModelId"]!=null && node["Id"].Raw.StartsWith("\"") && path.Length>0)result[node["Id"].StringValue()]=path;
            foreach(var p in node.Properties)if(p.Value!=null && (p.Value.Items!=null || p.Value.Properties!=null))walk(p.Value,path.Length==0?p.Key:path+"/"+p.Key);
        };
        walk(editor,"");
        return result;
    }
}
