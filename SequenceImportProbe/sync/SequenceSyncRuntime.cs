// Native reader for the semantic planner. It performs no project mutations.
public sealed class DiagramSnapshot
{
    public SequenceDocument Document=new SequenceDocument{HasTitle=true};
    public Dictionary<string,double> Y=new Dictionary<string,double>();
    public Dictionary<string,string> ShapeIds=new Dictionary<string,string>();
    public Dictionary<string,object> Geometry=new Dictionary<string,object>();
    public List<string> Limitations=new List<string>();
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
    public static void Preview(IApplication app,bool prepare=false,bool trial=false,bool retain=false)
    {
        var log=new StringBuilder();string report=null;string screenshot=null;
        trial=trial||retain;prepare=prepare||trial;
        try
        {
            var diagram=app.Workspace.CurrentEditor as ISequenceDiagram;
            if(diagram==null)throw new InvalidOperationException("S210: シーケンス図を開いてください。");
            string path=app.Window.UI.ShowOpenFileDialog("図全体と比較するPlantUML","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
            if(string.IsNullOrEmpty(path))return;
            if(new FileInfo(path).Length>300000)throw new InvalidOperationException("S210: 入力は300KB以下にしてください。");
            var desired=SequenceDocument.Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
            var current=DiagramSnapshot.Read(diagram,log);
            // Resolve only unambiguous references for this non-mutating audit command.
            var project=app.Workspace.CurrentProject;
            var interactions=SequenceMappedUpdate.Tree(project.DesignModel).OfType<IInteraction>()
                .Select(m=>new SequenceReferenceCandidate{Id=m.Id,Name=m.Name,Path=QualifiedName(m)}).ToArray();
            foreach(var e in desired.Elements.Where(e=>e.Kind=="ref"))
            {
                var matches=SequenceReferenceResolver.Find(e.Text,interactions);
                e.Attributes["reference"]=matches.Length==1?matches[0].Id:"";
                if(matches.Length!=1)current.Limitations.Add("ref参照先 "+e.Line+"行: "+matches.Length+"候補");
            }
            var plan=SequenceNotePolicy.Build(current.Document,desired,()=>Guid.NewGuid().ToString());
            var preflight=SequenceStructurePreflight.Check(current.Document,plan);
            if(retain && (!preflight.Candidate || preflight.ReconnectMessages.Count!=0 || preflight.DeleteExecutions.Count==0))
                throw new InvalidOperationException("S231: 確定できるのは未使用実行区間の削除だけです。差分を検証してください。");
            report="{\"version\":1,\"project\":"+SequencePayload.Q(project.Id)+",\"diagram\":"+SequencePayload.Q(diagram.Id)
                +",\"current\":"+current.Document.ToJson()+",\"desired\":"+desired.ToJson()+",\"plan\":"+plan.ToJson()
                +",\"structurePreflight\":"+preflight.ToJson()+",\"expected\":"+plan.Expected.ToJson()+",\"limitations\":"+PumlBuild.Json(current.Limitations.ToArray())
                +",\"shapes\":"+PumlBuild.Json(current.ShapeIds.ToDictionary(p=>p.Key,p=>(object)p.Value))
                +",\"geometry\":"+PumlBuild.Json(current.Geometry)+"}";
            foreach(var c in plan.Changes)log.AppendLine(c.Action+" "+c.Kind+" line="+c.Line+" id="+c.Id);
            foreach(var warning in current.Limitations)log.AppendLine("要照合: "+warning);
            screenshot=SequenceAudit.Reasons(current.Document,desired,plan)+"\f"+preflight.Summary();
            log.AppendLine(screenshot);
            SequenceExperiment.Summary=SequenceAudit.Summary(plan,current.Limitations.Count)+"\n構造更新の停止理由: "+preflight.Reasons.Count+"件（診断表示）";
            log.AppendLine("Scope: "+project.Id+" / "+diagram.ModelId+" / "+diagram.Id);
            if(prepare)
            {
                if(!preflight.Candidate)
                    SequenceExperiment.Summary="構造更新データ: 未作成 / 図への反映なし\n"+preflight.Summary();
                else
                {
                    var root=diagram.Model as IInteraction;
                    if(root==null || !root.IsEditable || root.IsProxy || root.IsDeleted || string.IsNullOrEmpty(project.Path))
                        throw new InvalidOperationException("S220: 保存済みで編集可能な図を開いてください。");
                    string exported=null;
                    SequenceEditorCapture.Read(project,root,diagram,log,delegate(string value){exported=value;});
                    var preparation=SequenceStructurePreparation.Build(exported,diagram.Id,current.Document,plan);
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
                    if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=diagram.Id
                        || DiagramSnapshot.Read(diagram,new StringBuilder()).Document.ToJson()!=current.Document.ToJson())
                        throw new InvalidOperationException("S220: 準備中に対象の図が変化しました。");
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
                        SequenceExperiment.Summary=SequenceStructureTrial.Run(app,project,diagram,preparation,plan,exported,directory,log,retain);
                        screenshot=SequenceExperiment.Summary+"\f会社PC内の試行診断\n"+log.ToString();
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
        SequenceExperiment.Details=screenshot??log.ToString();SequenceExperiment.Show(app);
    }
}

public static class SequenceStructureTrial
{
    static string Port(IMessagePort value) {var m=value as IModel;return m==null?"":m.Id;}
    static string Number(double value){return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
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
                record(r.Source);record(r.Target);
            }
        }
        foreach(var m in root.Messages)
            state.Ports[m.Id]=new[]{Port(m.SendPort),Port(m.ReceivePort),m.Sender==null?"":m.Sender.Id,m.Receiver==null?"":m.Receiver.Id,m.Kind};
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
    static void Verify(SequenceTrialState expected,SequenceTrialState actual,string phase,StringBuilder log)
    {
        string differences=expected.DifferenceCounts(actual);log.AppendLine(phase+": "+differences);
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
    public static string Run(IApplication app,IProject project,ISequenceDiagram diagram,SequenceStructurePreparation prepared,SyncPlan plan,string exported,string directory,StringBuilder log,bool retain=false)
    {
        if(retain && (prepared.DeleteIds.Length==0 || plan.Changes.Any(c=>c.Action!="delete" || c.Kind!="execution") || SequenceJson.Parse(prepared.ReconnectJson)["Relations"].Items.Count!=0))
            throw new InvalidOperationException("S231: 削除以外の差分は確定対象外です。");
        var root=diagram.Model as IInteraction;
        var before=Read(root,diagram);string original=before.Signature();
        var expectedReconnect=before.Expected(prepared,plan,false);
        var expectedFinal=before.Expected(prepared,plan,true);
        string rootId=root.Id,editorId=diagram.Id;
        Func<ISequenceDiagram> fresh=()=>{
            var model=project.GetModelById(rootId) as IInteraction;
            if(model==null)throw new InvalidOperationException("S230: 対象の図を取得できません。");
            return model.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==editorId);
        };
        string confirmation=retain
            ? "コピーのプロジェクトで実行してください。\n未使用実行区間を"+prepared.DeleteIds.Length+"件削除し、照合成功時に変更を確定します。\n自動保存はしません。確定後はUndo/Redoと保存再読込を確認してください。実行しますか？"
            : "コピーのプロジェクトで実行してください。\n受信接続変更と実行区間削除を一時適用し、照合後に必ず取り消します。\n自動保存・変更の確定は行いません。試行しますか？";
        if(!app.Window.UI.ShowConfirmDialog(confirmation,SequenceExperiment.Title))
            return (retain?"UPDATE006":"UPDATE005")+": キャンセル / 図への変更なし";
        if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=editorId
            || Read(root,fresh()).Signature()!=original)
            throw new InvalidOperationException("S230: 確認中に対象の図が変化しました。");
        // Serialized attributes are checked before starting; export is unavailable after a write.
        if(SequenceEditorCapture.Read(project,root,diagram,log).Fingerprint()!=SequenceEditorDocument.Read(exported,rootId,editorId).Fingerprint())
            throw new InvalidOperationException("S230: 確認中に表示設定が変化しました。");
        SequenceExperiment.Write(Path.Combine(directory,"trial-before-sdk.json"),original);
        SequenceExperiment.Write(Path.Combine(directory,"trial-expected-sdk.json"),expectedFinal.Signature());
        string stage="トランザクション開始";
        var transaction=project.BeginUndoTransaction(false);
        if(transaction==null)throw new InvalidOperationException("S230: トランザクションを開始できません。");
        Action apply=delegate {
            stage="受信接続の変更";Import(project,prepared.ReconnectJson,log);
            Verify(expectedReconnect,Read((IInteraction)project.GetModelById(rootId),fresh()),"接続変更後",log);
            log.AppendLine("receiver reconnection count: "+SequenceJson.Parse(prepared.ReconnectJson)["Relations"].Items.Count+"; SDK state verified");
            stage="不要実行区間の削除";
            using(project.SuspendModelVerification())foreach(string id in prepared.DeleteIds)project.GetModelById(id).Delete();
            stage="削除後のエディタ反映";Import(project,prepared.EditorAfterDeleteJson,log);
            foreach(string id in prepared.DeleteIds){var m=project.GetModelById(id);if(m!=null && !m.IsDeleted)throw new InvalidOperationException("S230: 削除対象が残っています。");}
            Verify(expectedFinal,Read((IInteraction)project.GetModelById(rootId),fresh()),"削除後",log);
            log.AppendLine("trial execution deletion and SDK state: verified");
        };
        Action rollback=delegate {transaction.Rollback();};
        Action verifyRestored=delegate {Verify(before,Read((IInteraction)project.GetModelById(rootId),fresh()),"取消後",log);};
        // Explicit completion only; Dispose may attempt a second rollback.
        string summary;
        if(retain)
        {
            var completion=new SequenceCommitTrial();
            completion.Run(apply,delegate {stage="変更の確定";transaction.Commit();},rollback,verifyRestored);
            foreach(var error in new[]{completion.ApplyError,completion.CommitError,completion.RollbackError,completion.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            summary="ケース: UPDATE006 / "+(completion.Committed?"削除・SDK照合・変更確定: 成功":"停止段階: "+stage)
                +(completion.Committed?"\nUndo/Redoと保存再読込を確認してください。":"\n取消API: "+(completion.RollbackReturned?"正常終了":"失敗・未確認")+" / 復元照合: "+(completion.Restored?"一致":"未確認・不一致"))
                +(!completion.Committed && !completion.Restored?"\n保存せずコピーを開き直してください。":"")
                +"\nプロジェクトの自動保存: していません\nUndo/Redo・スタイル読戻し・保存再読込: 未検証\nこの結果と診断表示を撮影してください。";
        }
        else
        {
            var trial=new SequenceRollbackTrial();trial.Run(apply,rollback,verifyRestored);
            foreach(var error in new[]{trial.ApplyError,trial.RollbackError,trial.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            summary="ケース: UPDATE005 / "+(trial.Applied?"一時適用・SDK読戻し照合: 一致":"停止段階: "+stage)
                +"\n取消API: "+(trial.RollbackReturned?"正常終了":"失敗・未確認")+" / 復元照合: "+(trial.Restored?"一致":"未確認・不一致")
                +"\n変更の確定・プロジェクト保存: していません\nスタイルの適用後読戻し・保存再読込: 未検証"
                +(trial.Restored?"":"\n保存せずコピーを開き直してください。")+"\nこの結果と診断表示を撮影してください。";
        }
        log.AppendLine(summary);
        try{SequenceExperiment.Write(Path.Combine(directory,"trial-result.txt"),summary+"\n"+log.ToString());}
        catch(Exception ex){log.AppendLine("trial result save: "+ex);summary+="\n試行結果の記録: 保存失敗";}
        return summary;
    }
}
