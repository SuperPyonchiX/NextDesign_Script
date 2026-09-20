public static class StructurePreparationTests
{
    static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
    static SequenceJson Clone(SequenceJson n){return SequenceJson.Parse(n.ToJsonString());}
    static void Set(SequenceJson n,string key,string value){n.Properties[key]=SequenceJson.Parse(SequencePayload.Q(value));}
    static void Reject(Action action,string reason)
    {bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,reason);}
    static void RollbackTrials()
    {
        // Simulate reconnection, deletion, editor import and final verification failures.
        for(int fail=-1;fail<4;fail++)
        {
            int value=0,cancels=0,checks=0;int chosen=fail;var trial=new SequenceRollbackTrial();
            trial.Run(delegate {for(int step=0;step<4;step++){value++;if(step==chosen)throw new Exception("injected");}},
                delegate{cancels++;value=0;},delegate{checks++;Require(value==0,"not restored");});
            Require(cancels==1 && checks==1 && trial.Restored && trial.RollbackReturned,"rollback flow failed");
            Require(trial.Applied==(fail<0) && (trial.ApplyError==null)==(fail<0),"apply result lost");
        }
        int attempts=0,verify=0;var rollbackFailure=new SequenceRollbackTrial();
        rollbackFailure.Run(delegate{throw new Exception("apply");},delegate{attempts++;throw new Exception("rollback");},delegate{verify++;});
        Require(attempts==1 && verify==0 && !rollbackFailure.Restored && !rollbackFailure.RollbackReturned && rollbackFailure.ApplyError!=null && rollbackFailure.RollbackError!=null,"rollback failure reported as restored");
        var verifyFailure=new SequenceRollbackTrial();
        verifyFailure.Run(delegate{},delegate{},delegate{throw new Exception("mismatch");});
        Require(verifyFailure.Applied && verifyFailure.RollbackReturned && !verifyFailure.Restored && verifyFailure.VerifyError!=null,"verification failure ignored");
    }
    static void Batch(SequenceJson raw,string editorId,SequenceDocument current,SyncPlan plan,SequenceTrialState state,string oldPort,string message)
    {
        var data=Clone(raw);var map=new Dictionary<string,string>{{oldPort,"batch-old"},{message,"batch-message"}};
        foreach(var e in raw["Entities"].Items.Where(e=>map.ContainsKey(e["Id"].StringValue())))
        {var copy=Clone(e);Set(copy,"Id",map[e["Id"].StringValue()]);data["Entities"].Items.Add(copy);}
        foreach(var r in raw["Relations"].Items.Where(r=>map.ContainsKey(r["SourceId"].StringValue()) || map.ContainsKey(r["TargetId"].StringValue())))
        {
            var copy=Clone(r);Set(copy,"Id",r["Id"].StringValue()+"-batch");
            foreach(string key in new[]{"SourceId","TargetId"})if(map.ContainsKey(r[key].StringValue()))Set(copy,key,map[r[key].StringValue()]);
            data["Relations"].Items.Add(copy);
        }
        var editor=data["Editors"].Items.Single();
        foreach(string kind in new[]{"Messages","ExecutionSpecifications"})foreach(var shape in raw["Editors"].Items.Single()[kind].Items.Where(sh=>map.ContainsKey(sh["ModelId"].StringValue())))
        {var copy=Clone(shape);Set(copy,"Id",shape["Id"].StringValue()+"-batch");Set(copy,"ModelId",map[shape["ModelId"].StringValue()]);editor[kind].Items.Add(copy);}
        var before=current.Copy();var desired=plan.Expected.Copy();
        foreach(var e in current.Elements.Where(e=>map.ContainsKey(e.Id)))
        {var copy=e.Copy();copy.Id=map[e.Id];foreach(var key in copy.Links.Keys.ToArray())copy.Links[key]=copy.Links[key].Select(id=>map.ContainsKey(id)?map[id]:id).ToArray();before.Elements.Add(copy);}
        var next=plan.Expected.Elements.Single(e=>e.Id==message).Copy();next.Id=map[message];desired.Elements.Add(next);
        var combined=new SyncPlan{Expected=desired};combined.Changes.AddRange(plan.Changes);
        combined.Changes.Add(new SequenceChange{Action="update",Kind="message",Id=map[message]});combined.Changes.Add(new SequenceChange{Action="delete",Kind="execution",Id=map[oldPort]});
        var package=SequenceStructurePreparation.Build(data.ToJsonString(),editorId,before,combined);
        Require(package.DeleteIds.Length==2 && SequenceJson.Parse(package.ReconnectJson)["Relations"].Items.Count==2,"batch package count");
        var snapshot=new SequenceTrialState();
        foreach(var e in data["Entities"].Items)snapshot.Models[e["Id"].StringValue()]=e.ToJsonString();
        foreach(var r in data["Relations"].Items)snapshot.Relations[r["Id"].StringValue()]=new[]{r["SourceId"].StringValue(),r["TargetId"].StringValue(),"7","3"};
        foreach(var sh in SequenceEditorDocument.Read(data.ToJsonString(),before.Elements.Single(e=>e.Kind=="interaction").Id,editorId).Shapes())
        {snapshot.Shapes[sh["Id"].StringValue()]=sh.ToJsonString();snapshot.ShapeModels[sh["Id"].StringValue()]=sh["ModelId"].StringValue();}
        snapshot.Ports[message]=state.Ports[message].ToArray();snapshot.Ports[map[message]]=state.Ports[message].ToArray();snapshot.Ports[map[message]][1]=map[oldPort];
        string signature=snapshot.Signature();var connected=snapshot.Expected(package,combined,false);var final=snapshot.Expected(package,combined,true);
        Require(package.DeleteIds.All(id=>connected.Models.ContainsKey(id) && !final.Models.ContainsKey(id)),"batch deletion not staged");
        Require(final.Ports[message][1]==final.Ports[map[message]][1] && final.Ports[message][1]!=oldPort,"batch shared destination lost");
        Require(snapshot.Signature()==signature,"batch snapshot mutated");
        Require(final.Shapes.Count==snapshot.Shapes.Count-2 && final.Relations.Values.All(r=>!package.DeleteIds.Contains(r[0]) && !package.DeleteIds.Contains(r[1])),"batch dangling relation or extra shape deletion");
        var live=connected;int commits=0,rollbacks=0;var completion=new SequenceCommitTrial();
        completion.Run(()=>{live.Models.Remove(package.DeleteIds[0]);throw new Exception("second deletion failed");},()=>{commits++;},()=>{rollbacks++;live=snapshot;},()=>{Require(live.Signature()==signature,"partial batch not restored");});
        Require(!completion.Committed && completion.Restored && commits==0 && rollbacks==1,"partial batch committed or rollback failed");
    }
    public static void Run()
    {
        var expectedOrder=new SequenceTrialState();var actualOrder=new SequenceTrialState();
        expectedOrder.Relations["r"]=new[]{"source","target","0","0"};actualOrder.Relations["r"]=new[]{"source","target","1","0"};
        Require(expectedOrder.RelationDifferences(actualOrder).Contains("SourceIndex: expected=0 actual=1"),"relation order diagnostic missing");
        Require(!expectedOrder.RelationDifferences(actualOrder).Contains("TargetIndex:"),"equal index reported");
        Require(expectedOrder.Signature()!=actualOrder.Signature(),"order discrepancy was ignored");
        actualOrder.Relations.Clear();Require(expectedOrder.RelationDifferences(actualOrder).Contains("missing actual"),"missing relation diagnostic");
        actualOrder.Relations["new"]=new[]{"s","t","0","0"};Require(expectedOrder.RelationDifferences(actualOrder).Contains("unexpected actual"),"extra relation diagnostic");
        Require(expectedOrder.RelationDifferences(expectedOrder)=="","equal relations reported");
        RollbackTrials();
        for(int failure=0;failure<4;failure++)
        {
            int commits=0,rollbacks=0,verifications=0;var result=new SequenceCommitTrial();
            result.Run(()=>{if(failure==1)throw new Exception("apply");},
                ()=>{commits++;if(failure>=2)throw new Exception("commit");},
                ()=>{rollbacks++;if(failure==3)throw new Exception("rollback");},()=>{verifications++;});
            Require(result.Committed==(failure==0),"commit status incorrect");
            Require(commits==(failure==1?0:1),"commit before successful apply");
            Require(rollbacks==(failure==0?0:1),"rollback retried or committed change rolled back");
            Require(verifications==(failure==1 || failure==2?1:0),"restore verified after failed rollback");
            Require(result.Restored==(failure==1 || failure==2),"restore status incorrect");
        }
        var mismatch=new SequenceCommitTrial();
        mismatch.Run(()=>{throw new Exception("readback mismatch");},()=>{throw new Exception("must not commit");},()=>{},()=>{throw new Exception("restore mismatch");});
        Require(mismatch.ApplyError!=null && mismatch.CommitError==null && mismatch.VerifyError!=null && !mismatch.Restored,"mismatch error lost");
        var seed=SequencePayload.Build(new[]{"root","frame","laneA","laneB","execA","execB","message"},"view","11.1");
        var raw=SequenceJson.Parse(seed.Json);var ids=seed.Ids;string replacement="replacement-execution";
        var extra=Clone(raw["Entities"].Items[5]);Set(extra,"Id",replacement);raw["Entities"].Items.Add(extra);
        foreach(var r in raw["Relations"].Items.Where(r=>r["TargetId"].StringValue()==ids[5]).ToArray())
        {var copy=Clone(r);Set(copy,"Id",r["Id"].StringValue()+"-extra");Set(copy,"TargetId",replacement);copy.Properties["SourceIndex"]=SequenceJson.Parse("2");raw["Relations"].Items.Add(copy);}
        var editor=raw["Editors"].Items.Single();string editorId=editor["Id"].StringValue();
        var shape=Clone(editor["ExecutionSpecifications"].Items[1]);Set(shape,"Id","extra-shape");Set(shape,"ModelId",replacement);editor["ExecutionSpecifications"].Items.Add(shape);
        editor.Properties["UnknownStyle"]=SequenceJson.Parse("{\"color\":\"custom\",\"padding\":[1,2.5]}");
        var receiver=raw["Relations"].Items.Single(r=>r["MetamodelId"].StringValue()==SequencePayload.Prefix+"ReceiveMessage");
        receiver.Properties["UnknownRelationData"]=SequenceJson.Parse("{\"keep\":true}");
        var current=new SequenceDocument();
        current.Elements.Add(new SequenceElement{Id=ids[0],Kind="interaction"});
        current.Elements.Add(new SequenceElement{Id=ids[2],Kind="participant",Parent=ids[0]});
        current.Elements.Add(new SequenceElement{Id=ids[3],Kind="participant",Parent=ids[0]});
        foreach(string id in new[]{ids[4],ids[5],replacement})
        {var e=new SequenceElement{Id=id,Kind="execution",Parent=ids[0]};e.Links["participant"]=new[]{id==ids[4]?ids[2]:ids[3]};current.Elements.Add(e);}
        var msg=new SequenceElement{Id=ids[6],Kind="message",Parent=ids[0],Text="probe()"};
        msg.Links["sender"]=new[]{ids[2]};msg.Links["receiver"]=new[]{ids[3]};msg.Links["sendExecution"]=new[]{ids[4]};msg.Links["receiveExecution"]=new[]{ids[5]};current.Elements.Add(msg);
        var plan=new SyncPlan{Expected=current.Copy()};plan.Expected.Elements.Single(e=>e.Id==ids[6]).Links["receiveExecution"]=new[]{replacement};plan.Expected.Elements.RemoveAll(e=>e.Id==ids[5]);
        plan.Changes.Add(new SequenceChange{Action="update",Kind="message",Id=ids[6],Line=4});plan.Changes.Add(new SequenceChange{Action="delete",Kind="execution",Id=ids[5]});
        string original=raw.ToJsonString(),semantic=current.ToJson()+plan.Expected.ToJson();
        var package=SequenceStructurePreparation.Build(original,editorId,current,plan);
        var state=new SequenceTrialState();
        foreach(var e in raw["Entities"].Items)state.Models[e["Id"].StringValue()]=e.ToJsonString();
        foreach(var r in raw["Relations"].Items)state.Relations[r["Id"].StringValue()]=new[]{r["SourceId"].StringValue(),r["TargetId"].StringValue(),r["SourceIndex"].Raw,r["TargetIndex"].Raw};
        foreach(var sh in SequenceEditorDocument.Read(original,ids[0],editorId).Shapes()){string id=sh["Id"].StringValue();state.Shapes[id]=sh.ToJsonString();state.ShapeModels[id]=sh["ModelId"].StringValue();}
        state.Ports[ids[6]]=new[]{ids[4],ids[5],ids[2],ids[3],"sync"};
        string beforeState=state.Signature();
        var connected=state.Expected(package,plan,false);var finalState=state.Expected(package,plan,true);
        Require(connected.Models.ContainsKey(ids[5]) && connected.Ports[ids[6]][1]==replacement,"connect-only stage removed execution");
        Require(!finalState.Models.ContainsKey(ids[5]) && !finalState.ShapeModels.Values.Contains(ids[5]),"expected deletion retained execution");
        Require(finalState.Relations.ContainsKey(receiver["Id"].StringValue()) && finalState.Relations[receiver["Id"].StringValue()][0]==replacement,"reconnected relation dropped with old port");
        Require(finalState.Ports[ids[6]][0]==ids[4] && finalState.Ports[ids[6]][3]==ids[3] && state.Signature()==beforeState,"expected SDK state mutated source or unrelated port");
        // Exported relations may omit index properties. The patch changes SourceId only;
        // expected ordering must come from the live SDK snapshot, never an assumed zero.
        var sparse=Clone(raw);
        var sparseRelation=sparse["Relations"].Items.Single(r=>r["Id"].StringValue()==receiver["Id"].StringValue());
        sparseRelation.Properties.Remove("SourceIndex");sparseRelation.Properties.Remove("TargetIndex");
        var sparsePackage=SequenceStructurePreparation.Build(sparse.ToJsonString(),editorId,current,plan);
        state.Relations[receiver["Id"].StringValue()][2]="7";state.Relations[receiver["Id"].StringValue()][3]="3";
        var sparseExpected=state.Expected(sparsePackage,plan,true);
        Require(sparseExpected.Relations[receiver["Id"].StringValue()].SequenceEqual(new[]{replacement,ids[6],"7","3"}),"omitted JSON indices lost SDK order");
        var sparsePatch=SequenceJson.Parse(sparsePackage.ReconnectJson)["Relations"].Items.Single();
        Require(sparsePatch["SourceIndex"]==null && sparsePatch["TargetIndex"]==null,"omitted indices were synthesized in import JSON");
        Batch(raw,editorId,current,plan,state,ids[5],ids[6]);
        var unscheduled=new SequenceTrialState();unscheduled.Ports["kept"]=new[]{ids[5],"","","","sync"};
        var deletionOnly=new SequenceStructurePreparation{DeleteIds=new[]{ids[5]},ReconnectJson="{\"Relations\":[]}"};
        Reject(()=>unscheduled.Expected(deletionOnly,plan,true),"deleting referenced port accepted");
        var reconnect=SequenceJson.Parse(package.ReconnectJson);var changed=reconnect["Relations"].Items.Single();
        var expected=Clone(receiver);Set(expected,"SourceId",replacement);
        Require(changed.ToJsonString()==expected.ToJsonString(),"relation identity, order or unknown data changed");
        Require(reconnect["Entities"].Items.Count==0 && reconnect["Editors"].Items.Single().ToJsonString()==editor.ToJsonString(),"reconnect altered models or editor");
        Require(reconnect["SchemaVersion"].StringValue()=="11.1" && reconnect["TopElementId"].StringValue()==ids[0],"patch targets wrong root/schema");
        var deleted=Clone(editor);deleted["ExecutionSpecifications"].Items.RemoveAt(1);
        var final=SequenceJson.Parse(package.EditorAfterDeleteJson);
        Require(final["Entities"].Items.Count==0 && final["Relations"].Items.Count==0 && final["Editors"].Items.Single().ToJsonString()==deleted.ToJsonString(),"delete editor did not preserve exact retained data");
        Require(package.DeleteIds.SequenceEqual(new[]{ids[5]}) && semantic==current.ToJson()+plan.Expected.ToJson(),"plan mutated or wrong deletions");
        Require(SequenceStructurePreparation.Build(original,editorId,current,plan).ReconnectJson==package.ReconnectJson,"preparation is not deterministic");
        var stale=Clone(raw);Set(stale["Relations"].Items.Single(r=>r["Id"].StringValue()==receiver["Id"].StringValue()),"SourceId",ids[4]);
        Reject(()=>SequenceStructurePreparation.Build(stale.ToJsonString(),editorId,current,plan),"stale receiver accepted");
        var external=Clone(raw);external["Relations"].Items.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id","external","MetamodelId","custom.Reference","SourceId","outside","TargetId",ids[5]))));
        Reject(()=>SequenceStructurePreparation.Build(external.ToJsonString(),editorId,current,plan),"unknown incoming reference accepted");
        var shared=Clone(raw);var second=Clone(editor);Set(second,"Id","second-editor");Set(second,"ModelId","other-root");shared["Editors"].Items.Add(second);
        Reject(()=>SequenceStructurePreparation.Build(shared.ToJsonString(),editorId,current,plan),"shared model accepted");
        var duplicate=Clone(raw);duplicate["Relations"].Items.Add(Clone(receiver));
        Reject(()=>SequenceStructurePreparation.Build(duplicate.ToJsonString(),editorId,current,plan),"duplicate relation accepted");
        var missingShape=Clone(raw);missingShape["Editors"].Items[0]["Messages"].Items.Clear();
        Reject(()=>SequenceStructurePreparation.Build(missingShape.ToJsonString(),editorId,current,plan),"missing message shape accepted");
        plan.Expected.Elements.Single(e=>e.Id==ids[6]).Links.Remove("receiveExecution");
        Reject(()=>SequenceStructurePreparation.Build(original,editorId,current,plan),"missing execution plan accepted");
        Console.WriteLine("PASS: structural preparation preserves IDs, geometry and unknown fields; rejects stale, shared and unsupported data");
    }
}
