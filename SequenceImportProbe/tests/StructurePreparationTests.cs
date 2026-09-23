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
    // Destinations other than a single shared execution: separate targets, a shared
    // origin, mixed explicit/omitted indices, and a reconnect whose origin is deleted.
    static SequenceTrialState Spread()
    {
        var state=new SequenceTrialState();
        state.Relations["a1"]=new[]{"srcX","mA1","0","0"};
        state.Relations["a2"]=new[]{"srcX","mA2","1","0"};
        state.Relations["b1"]=new[]{"srcY","mB1","0","0"};
        state.Relations["p0"]=new[]{"destP","mP","0","0"};
        foreach(string id in new[]{"mA1","mA2","mB1","mP"})state.Ports[id]=new[]{"send",Source(state,id),"A","B","sync"};
        var plan=new SyncPlan{Expected=new SequenceDocument()};
        foreach(string id in new[]{"mA1","mA2","mB1","mP"})
        {var e=new SequenceElement{Id=id};e.Links["receiver"]=new[]{"B"};plan.Expected.Elements.Add(e);}
        var package=new SequenceStructurePreparation{DeleteIds=new string[0],
            ReceiveRelationIds=new[]{"a1","a2","b1","p0"},ReconnectJson=Patch(
                PumlBuild.Obj("Id","a1","SourceId","destP","TargetId","mA1"),
                PumlBuild.Obj("Id","b1","SourceId","destQ","TargetId","mB1"))};
        var split=state.Expected(package,plan,false);
        Require(split.Relations["a2"][2]=="0","origin order not compacted after a move away");
        Require(split.Relations["a1"][2]=="1" && split.Relations["p0"][2]=="0","append past an existing destination relation failed");
        Require(split.Relations["b1"][2]=="0","empty destination did not start at zero");

        package.ReconnectJson=Patch(PumlBuild.Obj("Id","a1","SourceId","destP","TargetId","mA1"),
            PumlBuild.Obj("Id","a2","SourceId","destQ","TargetId","mA2"));
        var shared=state.Expected(package,plan,false);
        Require(shared.Relations["a1"][2]=="1" && shared.Relations["a2"][2]=="0" && shared.Relations["p0"][2]=="0",
            "shared origin to separate destinations ordered incorrectly");

        var mixed=PumlBuild.Obj("Id","a1","SourceId","destP","TargetId","mA1");
        package.ReconnectJson=Patch(mixed,PumlBuild.Obj("Id","b1","SourceId","destP","TargetId","mB1"));
        var withIndex=SequenceJson.Parse(package.ReconnectJson);
        withIndex["Relations"].Items[0].Properties["SourceIndex"]=SequenceJson.Parse("0");
        package.ReconnectJson=withIndex.ToJsonString();
        var blended=state.Expected(package,plan,false);
        Require(blended.Relations["a1"][2]=="0" && blended.Relations["p0"][2]=="1" && blended.Relations["b1"][2]=="2",
            "explicit index followed by an omitted one ordered incorrectly");
        Reject(delegate {
            var bad=SequenceJson.Parse(package.ReconnectJson);
            bad["Relations"].Items[0].Properties["SourceIndex"]=SequenceJson.Parse("3");
            var reach=new SequenceStructurePreparation{DeleteIds=package.DeleteIds,
                ReceiveRelationIds=package.ReceiveRelationIds,ReconnectJson=bad.ToJsonString()};
            state.Expected(reach,plan,false);
        },"out of range insertion accepted");

        // The origin of a reconnect is itself deleted, as in the batch sample.
        state.Relations["own-x"]=new[]{"lane","srcX","1","0"};
        state.Relations["own-w"]=new[]{"lane","srcW","0","0"};
        state.Relations["root-x"]=new[]{"root","srcX","0","0"};
        state.Models["srcX"]="origin";
        package.DeleteIds=new[]{"srcX"};
        package.ReconnectJson=Patch(PumlBuild.Obj("Id","a1","SourceId","destP","TargetId","mA1"),
            PumlBuild.Obj("Id","a2","SourceId","destP","TargetId","mA2"));
        var dropped=state.Expected(package,plan,true);
        Require(dropped.Relations["a1"][2]=="1" && dropped.Relations["a2"][2]=="2","moved relations lost their destination order on deletion");
        Require(!dropped.Relations.ContainsKey("own-x") && !dropped.Relations.ContainsKey("root-x"),"owning relations of the deleted origin remain");
        Require(dropped.Relations["own-w"][2]=="0" && dropped.Relations.ContainsKey("b1"),"unrelated relations were disturbed");
        Require(!dropped.Models.ContainsKey("srcX") && state.Models.ContainsKey("srcX"),"deletion leaked into the snapshot");
        return state;
    }
    static string Source(SequenceTrialState state,string message)
    { return state.Relations.Where(p=>p.Value[1]==message).Select(p=>p.Value[0]).DefaultIfEmpty("").First(); }
    static string Patch(params object[] relations)
    { return PumlBuild.Json(PumlBuild.Obj("Relations",relations)); }
    // A receive bar the diagram does not have: the payload clones an existing bar of the
    // same participant, and the expected state predicts its order and geometry.
    static void AddedExecution()
    {
        var seed=SequencePayload.Build(new[]{"root","frame","laneA","laneB","execA","execB","message"},"view","11.1");
        var raw=SequenceJson.Parse(seed.Json);var ids=seed.Ids;
        string editorId=raw["Editors"].Items.Single()["Id"].StringValue();
        var current=new SequenceDocument();
        current.Elements.Add(new SequenceElement{Id=ids[0],Kind="interaction"});
        current.Elements.Add(new SequenceElement{Id=ids[2],Kind="participant",Parent=ids[0]});
        current.Elements.Add(new SequenceElement{Id=ids[3],Kind="participant",Parent=ids[0]});
        foreach(string id in new[]{ids[4],ids[5]})
        {var e=new SequenceElement{Id=id,Kind="execution",Parent=ids[0]};e.Links["participant"]=new[]{id==ids[4]?ids[2]:ids[3]};current.Elements.Add(e);}
        var msg=new SequenceElement{Id=ids[6],Kind="message",Parent=ids[0],Text="probe()"};
        msg.Links["sender"]=new[]{ids[2]};msg.Links["receiver"]=new[]{ids[3]};
        msg.Links["sendExecution"]=new[]{ids[4]};msg.Links["receiveExecution"]=new[]{ids[5]};current.Elements.Add(msg);

        string bar="added-bar";
        var desired=current.Copy();
        var extra=new SequenceElement{Id=bar,Kind="execution",Parent=ids[0]};
        extra.Links["participant"]=new[]{ids[3]};extra.Links["outer"]=new[]{ids[5]};extra.Links["endContainer"]=new[]{ids[0]};
        desired.Elements.Add(extra);
        desired.Elements.Single(e=>e.Id==ids[6]).Links["receiveExecution"]=new[]{bar};
        var plan=new SyncPlan{Expected=desired};
        plan.Changes.Add(new SequenceChange{Action="add",Kind="execution",Id=bar,Line=5});
        plan.Changes.Add(new SequenceChange{Action="update",Kind="message",Id=ids[6],Line=4});

        var gate=SequenceStructurePreflight.Check(current,plan);
        Require(gate.Candidate && gate.AddExecutions.SequenceEqual(new[]{bar}) && gate.ReconnectMessages.SequenceEqual(new[]{ids[6]}),
            "added bar with a reconnect was not a candidate");
        Require(gate.CanCommit(),"commit modes accepted the wrong scope for an addition");

        var package=SequenceStructurePreparation.Build(raw.ToJsonString(),editorId,current,plan);
        Require(package.AddedExecutions.Length==1,"addition was not prepared");
        var add=package.AddedExecutions[0];
        var patch=SequenceJson.Parse(package.ReconnectJson);
        Require(patch["Entities"].Items.Count==1 && patch["Entities"].Items[0]["Id"].StringValue()==bar,"new model missing from the payload");
        Require(patch["Entities"].Items[0]["MetamodelId"].StringValue()=="execB","new model did not clone the sample type");
        Require(patch["Relations"].Items.Count==3,"owning relations or the reconnection missing");
        foreach(string id in add.RelationIds)
        {
            var relation=patch["Relations"].Items.Single(r=>r["Id"].StringValue()==id);
            Require(relation["TargetId"].StringValue()==bar,"owning relation does not point at the new bar");
            Require(relation["SourceIndex"]==null && relation["TargetIndex"]==null,"owning relation pinned an order");
        }
        var bars=patch["Editors"].Items.Single()["ExecutionSpecifications"].Items;
        var created=bars.Single(sh=>sh["ModelId"].StringValue()==bar);
        Require(bars.Count==3 && created["X"].Raw=="278" && created["Y"].Raw=="80" && created["Length"].Raw=="80" && created["Height"].Raw=="80",
            "new bar geometry does not follow the generator rule");
        Require(SequenceJson.Parse(package.EditorAfterDeleteJson)["Editors"].Items.Single()["ExecutionSpecifications"].Items
            .Count(sh=>sh["ModelId"].StringValue()==bar)==1,"new bar was dropped from the deletion stage editor");

        // The export carries the order the relation had where it was. Moving it to a bar
        // that holds nothing must not ask for that position.
        var occupiedSeed=Clone(raw);
        var movedRelation=occupiedSeed["Relations"].Items.Single(r=>r["MetamodelId"].StringValue()==SequencePayload.Prefix+"ReceiveMessage");
        movedRelation.Properties["SourceIndex"]=SequenceJson.Parse("3");
        var occupiedPackage=SequenceStructurePreparation.Build(occupiedSeed.ToJsonString(),editorId,current,plan);
        var moved=SequenceJson.Parse(occupiedPackage.ReconnectJson)["Relations"].Items
            .Single(r=>r["Id"].StringValue()==movedRelation["Id"].StringValue());
        Require(moved["SourceIndex"]==null,"stale order from the source collection was sent");

        var state=new SequenceTrialState();
        foreach(var e in raw["Entities"].Items)state.Models[e["Id"].StringValue()]=e.ToJsonString();
        foreach(var r in raw["Relations"].Items)
        {
            string id=r["Id"].StringValue();
            state.Relations[id]=new[]{r["SourceId"].StringValue(),r["TargetId"].StringValue(),r["SourceIndex"].Raw,r["TargetIndex"].Raw};
            state.RelationFields[id]=r["MetamodelId"].StringValue();
        }
        foreach(var sh in SequenceEditorDocument.Read(raw.ToJsonString(),ids[0],editorId).Shapes())
        {string id=sh["Id"].StringValue();state.Shapes[id]=sh.ToJsonString();state.ShapeModels[id]=sh["ModelId"].StringValue();}
        state.Shapes[add.TemplateShapeId]=PumlBuild.Json(new[]{"270","80","16","80","80"});
        state.Ports[ids[6]]=new[]{ids[4],ids[5],ids[2],ids[3],"sync"};
        string before=state.Signature();
        var expected=state.Expected(package,plan,false);
        Require(expected.Models.ContainsKey(bar) && expected.Models[bar]==PumlBuild.Json(new[]{"execB","",ids[0],"False"}),"new model not in the expected state");
        Require(expected.Relations[add.RelationIds[0]].SequenceEqual(new[]{ids[3],bar,"1","0"}),"participant owning order not appended");
        Require(expected.Relations[add.RelationIds[1]].SequenceEqual(new[]{ids[0],bar,"2","0"}),"interaction owning order not appended");
        Require(add.RelationSources[0]==ids[3],"the lifeline link is not sent first");
        Require(expected.Shapes[add.ShapeId]==PumlBuild.Json(new[]{"278","80","16","80","80"}),"new bar shape not predicted");
        Require(expected.ShapeModels[add.ShapeId]==bar,"new shape owner missing");
        Require(expected.Ports[ids[6]][1]==bar,"receive port not moved onto the new bar");
        Require(state.Signature()==before,"expected state mutated the snapshot");
    }
    // A lane the input declares and the diagram does not have. It goes at the right end,
    // one lane spacing past the current rightmost lane, so nothing existing moves.
    static void AddedParticipant()
    {
        var seed=SequencePayload.Build(new[]{"root","frame","laneA","laneB","execA","execB","message"},"view","11.1");
        var raw=SequenceJson.Parse(seed.Json);var ids=seed.Ids;
        string editorId=raw["Editors"].Items.Single()["Id"].StringValue();
        var current=new SequenceDocument();
        current.Elements.Add(new SequenceElement{Id=ids[0],Kind="interaction"});
        current.Elements.Add(new SequenceElement{Id=ids[2],Kind="participant",Parent=ids[0],Order=0,Text="A"});
        current.Elements.Add(new SequenceElement{Id=ids[3],Kind="participant",Parent=ids[0],Order=1,Text="B"});

        string lane="added-lane";
        var desired=current.Copy();
        desired.Elements.Add(new SequenceElement{Id=lane,Kind="participant",Parent=ids[0],Order=2,Text="C"});
        var plan=new SyncPlan{Expected=desired};
        plan.Changes.Add(new SequenceChange{Action="add",Kind="participant",Id=lane,Line=4});

        var gate=SequenceStructurePreflight.Check(current,plan);
        Require(gate.Candidate && gate.AddParticipants.SequenceEqual(new[]{lane}),"added lane was not a candidate");
        Require(gate.CanCommit(),"commit modes accepted the wrong scope for a lane");

        var middle=current.Copy();
        middle.Elements.Add(new SequenceElement{Id=lane,Kind="participant",Parent=ids[0],Order=-1,Text="C"});
        var middlePlan=new SyncPlan{Expected=middle};middlePlan.Changes.AddRange(plan.Changes);
        Require(SequenceStructurePreflight.Check(current,middlePlan).AddParticipants.Count==0,"a lane inserted before existing ones was accepted");

        var used=desired.Copy();
        var msg=new SequenceElement{Id="msg",Kind="message",Parent=ids[0],Text="call"};
        msg.Links["sender"]=new[]{ids[2]};msg.Links["receiver"]=new[]{lane};used.Elements.Add(msg);
        var usedPlan=new SyncPlan{Expected=used};usedPlan.Changes.AddRange(plan.Changes);
        Require(SequenceStructurePreflight.Check(current,usedPlan).AddParticipants.Count==0,"a lane a message already uses was accepted");

        var package=SequenceStructurePreparation.Build(raw.ToJsonString(),editorId,current,plan);
        Require(package.AddedParticipants.Length==1,"lane was not prepared");
        var added=package.AddedParticipants[0];
        var patch=SequenceJson.Parse(package.ReconnectJson);
        var entity=patch["Entities"].Items.Single(e=>e["Id"].StringValue()==lane);
        Require(entity["MetamodelId"].StringValue()=="laneB" && entity["Name"].StringValue()=="C","new lane did not clone the sample type or take its name");
        var link=patch["Relations"].Items.Single(r=>r["Id"].StringValue()==added.RelationId);
        Require(link["TargetId"].StringValue()==lane && link["SourceIndex"]==null,"lane ownership relation is wrong");
        var laneShapes=patch["Editors"].Items.Single()["Lifelines"].Items;
        var created=laneShapes.Single(sh=>sh["ModelId"].StringValue()==lane);
        // Sample lanes sit at X=20 and X=220 with width 100, so the new one lands at 460.
        Require(laneShapes.Count==3 && created["X"].Raw=="460" && created["Width"].Raw=="100","new lane was not placed one spacing to the right");

        var state=new SequenceTrialState();
        foreach(var e in raw["Entities"].Items)state.Models[e["Id"].StringValue()]=e.ToJsonString();
        foreach(var r in raw["Relations"].Items)
        {
            string id=r["Id"].StringValue();
            state.Relations[id]=new[]{r["SourceId"].StringValue(),r["TargetId"].StringValue(),r["SourceIndex"].Raw,r["TargetIndex"].Raw};
            state.RelationFields[id]=r["MetamodelId"].StringValue();
        }
        foreach(var sh in SequenceEditorDocument.Read(raw.ToJsonString(),ids[0],editorId).Shapes())
        {string id=sh["Id"].StringValue();state.Shapes[id]=sh.ToJsonString();state.ShapeModels[id]=sh["ModelId"].StringValue();}
        state.Shapes[added.TemplateShapeId]=PumlBuild.Json(new[]{"220","20","100","40"})+"300";
        string before=state.Signature();
        var expected=state.Expected(package,plan,false);
        Require(expected.Models[lane]==PumlBuild.Json(new[]{"laneB","C",ids[0],"False"}),"new lane not in the expected state");
        Require(expected.Relations[added.RelationId].SequenceEqual(new[]{ids[0],lane,"2","0"}),"lane ownership order not appended");
        Require(expected.Shapes[added.ShapeId]==PumlBuild.Json(new[]{"460","20","100","40"})+"300","new lane shape not predicted");
        Require(expected.ShapeModels[added.ShapeId]==lane,"new lane shape owner missing");
        Require(state.Signature()==before,"expected state mutated the snapshot");

        var drifted=state.Expected(package,plan,false);
        drifted.Shapes[added.ShapeId]=PumlBuild.Json(new[]{"460.000001","20.000001","100","40.000001"})+"300";
        drifted.Round(new[]{added.ShapeId});expected.Round(new[]{added.ShapeId});
        Require(drifted.Shapes[added.ShapeId]==expected.Shapes[added.ShapeId],"lane drift was not reconciled or the timeline length was lost");
        Require(expected.Shapes[added.ShapeId].EndsWith("300"),"timeline length dropped by rounding");
    }
    // Removing a message takes its model, its three endpoint relations and its shape,
    // and leaves every other coordinate alone.
    static void DeletedMessage()
    {
        var seed=SequencePayload.Build(new[]{"root","frame","laneA","laneB","execA","execB","message"},"view","11.1");
        var raw=SequenceJson.Parse(seed.Json);var ids=seed.Ids;
        string editorId=raw["Editors"].Items.Single()["Id"].StringValue();
        var current=new SequenceDocument();
        current.Elements.Add(new SequenceElement{Id=ids[0],Kind="interaction"});
        current.Elements.Add(new SequenceElement{Id=ids[2],Kind="participant",Parent=ids[0]});
        current.Elements.Add(new SequenceElement{Id=ids[3],Kind="participant",Parent=ids[0]});
        foreach(string id in new[]{ids[4],ids[5]})
        {var e=new SequenceElement{Id=id,Kind="execution",Parent=ids[0]};e.Links["participant"]=new[]{id==ids[4]?ids[2]:ids[3]};current.Elements.Add(e);}
        var msg=new SequenceElement{Id=ids[6],Kind="message",Parent=ids[0],Text="probe()"};
        msg.Links["sender"]=new[]{ids[2]};msg.Links["receiver"]=new[]{ids[3]};
        msg.Links["sendExecution"]=new[]{ids[4]};msg.Links["receiveExecution"]=new[]{ids[5]};current.Elements.Add(msg);

        var desired=current.Copy();desired.Elements.RemoveAll(e=>e.Id==ids[6]);
        var plan=new SyncPlan{Expected=desired};
        plan.Changes.Add(new SequenceChange{Action="delete",Kind="message",Id=ids[6],Line=4});

        var gate=SequenceStructurePreflight.Check(current,plan);
        Require(gate.Candidate && gate.DeleteMessages.SequenceEqual(new[]{ids[6]}),"message deletion was not a candidate");
        Require(gate.CanCommit(),"commit modes accepted the wrong scope for a message");

        var package=SequenceStructurePreparation.Build(raw.ToJsonString(),editorId,current,plan);
        Require(package.DeleteMessageIds.SequenceEqual(new[]{ids[6]}),"message was not prepared for deletion");
        Require(SequenceJson.Parse(package.ReconnectJson)["Relations"].Items.Count==0,"an unrelated patch was built");
        var remaining=SequenceJson.Parse(package.EditorAfterDeleteJson)["Editors"].Items.Single();
        Require(remaining["Messages"].Items.Count==0,"message shape was kept");
        Require(remaining["ExecutionSpecifications"].Items.Count==2 && remaining["Lifelines"].Items.Count==2,"unrelated shapes were dropped");

        var state=new SequenceTrialState();
        foreach(var e in raw["Entities"].Items)state.Models[e["Id"].StringValue()]=e.ToJsonString();
        foreach(var r in raw["Relations"].Items)
        {
            string id=r["Id"].StringValue();
            state.Relations[id]=new[]{r["SourceId"].StringValue(),r["TargetId"].StringValue(),r["SourceIndex"].Raw,r["TargetIndex"].Raw};
            state.RelationFields[id]=r["MetamodelId"].StringValue();
        }
        foreach(var sh in SequenceEditorDocument.Read(raw.ToJsonString(),ids[0],editorId).Shapes())
        {string id=sh["Id"].StringValue();state.Shapes[id]=sh.ToJsonString();state.ShapeModels[id]=sh["ModelId"].StringValue();}
        state.Ports[ids[6]]=new[]{ids[4],ids[5],ids[2],ids[3],"sync"};
        string before=state.Signature();
        var final=state.Expected(package,plan,true);
        Require(!final.Models.ContainsKey(ids[6]) && !final.Ports.ContainsKey(ids[6]),"message model or port survived");
        Require(!final.ShapeModels.Values.Contains(ids[6]),"message shape survived");
        Require(final.Relations.Values.All(r=>r[0]!=ids[6] && r[1]!=ids[6]),"a relation still points at the message");
        Require(final.Models.ContainsKey(ids[4]) && final.Models.ContainsKey(ids[5]),"the executions it used were removed");
        Require(state.Signature()==before,"expected state mutated the snapshot");

        var anchored=current.Copy();
        anchored.Elements.Single(e=>e.Id==ids[4]).Links["endBefore"]=new[]{ids[6]};
        var stillUsed=anchored.Copy();
        var anchoredPlan=new SyncPlan{Expected=stillUsed};
        anchoredPlan.Changes.Add(new SequenceChange{Action="delete",Kind="message",Id=ids[6],Line=4});
        Require(SequenceStructurePreflight.Check(anchored,anchoredPlan).DeleteMessages.Count==0,"a message still used as a boundary was accepted");
    }
    // A message the input adds after the last one, into space the bars already cover.
    static void AddedMessage()
    {
        var seed=SequencePayload.Build(new[]{"root","frame","laneA","laneB","execA","execB","message"},"view","11.1");
        var raw=SequenceJson.Parse(seed.Json);var ids=seed.Ids;
        string editorId=raw["Editors"].Items.Single()["Id"].StringValue();
        var current=new SequenceDocument();
        current.Elements.Add(new SequenceElement{Id=ids[0],Kind="interaction"});
        current.Elements.Add(new SequenceElement{Id=ids[2],Kind="participant",Parent=ids[0]});
        current.Elements.Add(new SequenceElement{Id=ids[3],Kind="participant",Parent=ids[0]});
        foreach(string id in new[]{ids[4],ids[5]})
        {var e=new SequenceElement{Id=id,Kind="execution",Parent=ids[0]};e.Links["participant"]=new[]{id==ids[4]?ids[2]:ids[3]};current.Elements.Add(e);}
        var msg=new SequenceElement{Id=ids[6],Kind="message",Parent=ids[0],Order=0,Text="probe()"};
        msg.Attributes["sort"]="sync";
        msg.Links["sender"]=new[]{ids[2]};msg.Links["receiver"]=new[]{ids[3]};
        msg.Links["sendExecution"]=new[]{ids[4]};msg.Links["receiveExecution"]=new[]{ids[5]};current.Elements.Add(msg);

        string wire="added-message";
        var desired=current.Copy();
        var extra=new SequenceElement{Id=wire,Kind="message",Parent=ids[0],Order=1,Text="again()"};
        extra.Attributes["sort"]="sync";
        extra.Links["sender"]=new[]{ids[2]};extra.Links["receiver"]=new[]{ids[3]};
        extra.Links["sendExecution"]=new[]{ids[4]};extra.Links["receiveExecution"]=new[]{ids[5]};
        desired.Elements.Add(extra);
        var plan=new SyncPlan{Expected=desired};
        plan.Changes.Add(new SequenceChange{Action="add",Kind="message",Id=wire,Line=5});

        var gate=SequenceStructurePreflight.Check(current,plan);
        Require(gate.Candidate && gate.AddMessages.SequenceEqual(new[]{wire}),"added message was not a candidate");
        Require(gate.CanCommit(),"commit modes accepted the wrong scope for a message");

        var first=desired.Copy();first.Elements.Single(e=>e.Id==wire).Order=-1;
        var firstPlan=new SyncPlan{Expected=first};firstPlan.Changes.AddRange(plan.Changes);
        Require(SequenceStructurePreflight.Check(current,firstPlan).AddMessages.Count==0,"a message placed before existing ones was accepted");

        var reply=desired.Copy();reply.Elements.Single(e=>e.Id==wire).Attributes["sort"]="reply";
        var replyPlan=new SyncPlan{Expected=reply};replyPlan.Changes.AddRange(plan.Changes);
        Require(SequenceStructurePreflight.Check(current,replyPlan).AddMessages.Count==0,"a sort with no existing sample was accepted");

        var package=SequenceStructurePreparation.Build(raw.ToJsonString(),editorId,current,plan);
        Require(package.AddedMessages.Length==1,"message was not prepared");
        var added=package.AddedMessages[0];
        var patch=SequenceJson.Parse(package.ReconnectJson);
        var entity=patch["Entities"].Items.Single(e=>e["Id"].StringValue()==wire);
        Require(entity["MetamodelId"].StringValue()=="message" && entity["Name"].StringValue()=="again()","new message did not clone the sample type or take its name");
        Require(patch["Relations"].Items.Count==3,"the three endpoint relations were not built");
        Require(added.RelationSources[0]==ids[4] && added.RelationSources[1]==ids[5] && added.RelationSources[2]==ids[0],
            "endpoints are not sent before the interaction membership");
        foreach(string id in added.RelationIds)
        {
            var relation=patch["Relations"].Items.Single(r=>r["Id"].StringValue()==id);
            Require(relation["TargetId"].StringValue()==wire && relation["SourceIndex"]==null,"endpoint relation is wrong");
        }
        // The sample message sits at Y=80 inside bars that run from 50 to 170 and 80 to 160.
        var wires=patch["Editors"].Items.Single()["Messages"].Items;
        var created=wires.Single(sh=>sh["ModelId"].StringValue()==wire);
        Require(wires.Count==2 && created["SourceY"].Raw=="130" && created["TargetY"].Raw=="130","new message was not placed one step below the last");

        var state=new SequenceTrialState();
        foreach(var e in raw["Entities"].Items)state.Models[e["Id"].StringValue()]=e.ToJsonString();
        foreach(var r in raw["Relations"].Items)
        {
            string id=r["Id"].StringValue();
            state.Relations[id]=new[]{r["SourceId"].StringValue(),r["TargetId"].StringValue(),r["SourceIndex"].Raw,r["TargetIndex"].Raw};
            state.RelationFields[id]=r["MetamodelId"].StringValue();
        }
        foreach(var sh in SequenceEditorDocument.Read(raw.ToJsonString(),ids[0],editorId).Shapes())
        {string id=sh["Id"].StringValue();state.Shapes[id]=sh.ToJsonString();state.ShapeModels[id]=sh["ModelId"].StringValue();}
        state.Shapes[added.TemplateShapeId]=PumlBuild.Json(new[]{"probe()","80","80","0"});
        state.Ports[ids[6]]=new[]{ids[4],ids[5],ids[2],ids[3],"sync"};
        string before=state.Signature();
        var expected=state.Expected(package,plan,false);
        Require(expected.Models[wire]==PumlBuild.Json(new[]{"message","again()",ids[0],"False"}),"new message not in the expected state");
        Require(expected.Shapes[added.ShapeId]==PumlBuild.Json(new[]{"again()","130","130","0"}),"new message shape not predicted");
        Require(expected.ShapeModels[added.ShapeId]==wire,"new message shape owner missing");
        Require(expected.Ports[wire].SequenceEqual(new[]{ids[4],ids[5],ids[2],ids[3],"sync"}),"new message ports not predicted");
        Require(expected.Ports[ids[6]].SequenceEqual(state.Ports[ids[6]]),"the sample message ports changed");
        Require(state.Signature()==before,"expected state mutated the snapshot");
    }
    // A frame holding one operand pair, drawn below the sample message: a frame from 100
    // to 190, operands 30 and 70 below its top, and one message inside the first at 140.
    static void InsertedAroundFrame(bool into)
    {
        var seed=SequencePayload.Build(new[]{"root","frame","laneA","laneB","execA","execB","message"},"view","11.1");
        var raw=SequenceJson.Parse(seed.Json);var ids=seed.Ids;
        var editor=raw["Editors"].Items.Single();string editorId=editor["Id"].StringValue();
        string inner="inner-message",frame="alt-frame",first="operand-1",second="operand-2";
        var entity=Clone(raw["Entities"].Items.Single(e=>e["Id"].StringValue()==ids[6]));Set(entity,"Id",inner);raw["Entities"].Items.Add(entity);
        foreach(var r in raw["Relations"].Items.Where(r=>r["TargetId"].StringValue()==ids[6]).ToArray())
        {var copy=Clone(r);Set(copy,"Id",r["Id"].StringValue()+"-inner");Set(copy,"TargetId",inner);copy.Properties["SourceIndex"]=SequenceJson.Parse("1");raw["Relations"].Items.Add(copy);}
        var wire=Clone(editor["Messages"].Items[0]);Set(wire,"Id","inner-shape");Set(wire,"ModelId",inner);
        wire.Properties["SourceY"]=SequenceJson.Parse("140");wire.Properties["TargetY"]=SequenceJson.Parse("140");editor["Messages"].Items.Add(wire);
        editor.Properties["Fragments"]=SequenceJson.Parse(PumlBuild.Json(new object[]{PumlBuild.Obj("Id","frame-shape","ModelId",frame,"X",4,"Y",100,"Width",332,"Height",90)}));
        editor.Properties["Operands"]=SequenceJson.Parse(PumlBuild.Json(new object[]{PumlBuild.Obj("Id","first-shape","ModelId",first,"Position",30),
            PumlBuild.Obj("Id","second-shape","ModelId",second,"Position",70)}));
        var current=new SequenceDocument();
        current.Elements.Add(new SequenceElement{Id=ids[0],Kind="interaction"});
        current.Elements.Add(new SequenceElement{Id=ids[2],Kind="participant",Parent=ids[0]});
        current.Elements.Add(new SequenceElement{Id=ids[3],Kind="participant",Parent=ids[0]});
        foreach(string id in new[]{ids[4],ids[5]})
        {var e=new SequenceElement{Id=id,Kind="execution",Parent=ids[0]};e.Links["participant"]=new[]{id==ids[4]?ids[2]:ids[3]};current.Elements.Add(e);}
        Func<string,string,int,string,SequenceElement> message=(id,parent,order,text)=>{
            var m=new SequenceElement{Id=id,Kind="message",Parent=parent,Order=order,Text=text};m.Attributes["sort"]="sync";
            m.Links["sender"]=new[]{ids[2]};m.Links["receiver"]=new[]{ids[3]};m.Links["sendExecution"]=new[]{ids[4]};m.Links["receiveExecution"]=new[]{ids[5]};
            return m;
        };
        current.Elements.Add(message(ids[6],ids[0],0,"probe()"));
        var box=new SequenceElement{Id=frame,Kind="fragment",Parent=ids[0],Order=2,Text="alt"};box.Attributes["operator"]="alt";current.Elements.Add(box);
        current.Elements.Add(new SequenceElement{Id=first,Kind="operand",Parent=frame,Order=0,Text="ready"});
        current.Elements.Add(new SequenceElement{Id=second,Kind="operand",Parent=frame,Order=1,Text="else"});
        current.Elements.Add(message(inner,first,0,"inside()"));
        string added="inserted-message";
        var desired=current.Copy();
        desired.Elements.Add(into?message(added,first,1,"also()"):message(added,ids[0],1,"mid()"));
        var plan=new SyncPlan{Expected=desired};
        plan.Changes.Add(new SequenceChange{Action="add",Kind="message",Id=added,Line=5});
        var gate=SequenceStructurePreflight.Check(current,plan);
        Require(gate.Candidate && gate.AddMessages.SequenceEqual(new[]{added}) && gate.CanCommit(),
            "insertion "+(into?"into":"above")+" a frame was not a candidate: "+gate.ToJson());
        var types=new SequenceFrameTypes{Fragment="fragment",Operand="operand",Owns=new[]{"owns","Embed","f1"},
            Branches=new[]{"branches","Embed","f2"},Crossing=new[]{"crossing","Ref","f3"},OperandMessage=new[]{"operand-message","Ref","f4"}};
        var package=SequenceStructurePreparation.Build(raw.ToJsonString(),editorId,current,plan,types);
        Require(package.InsertedMessageId==added,"the message was not treated as an insertion");
        Func<string,string,string> moved=(shape,key)=>{
            var hit=package.ShiftedShapes.Where(m=>m.ShapeId==shape).ToArray();
            if(hit.Length==0)return null;
            int i=Array.IndexOf(hit[0].Keys,key);return i<0?null:hit[0].Values[i];
        };
        var view=SequenceJson.Parse(package.ReconnectJson)["Editors"].Items.Single();
        var y=view["Messages"].Items.Single(sh=>sh["ModelId"].StringValue()==added)["TargetY"].Raw;
        if(into)
        {
            // Placed one step under the message above it, inside the first operand.
            Require(y=="190","inserted message not placed under the one above: "+y);
            Require(moved("frame-shape","Height")=="140" && moved("frame-shape","Y")==null,"the frame the point falls in did not grow");
            Require(moved("first-shape","Position")==null,"the operand holding the point moved");
            Require(moved("second-shape","Position")=="120","the later operand did not move down");
            Require(moved("inner-shape","TargetY")==null,"the message above the point moved");
            var patch=SequenceJson.Parse(package.ReconnectJson);
            Require(patch["Relations"].Items.Any(r=>r["MetamodelId"].StringValue()=="operand-message"
                && r["SourceId"].StringValue()==first && r["TargetId"].StringValue()==added),"the operand does not point at the message");
        }
        else
        {
            Require(y=="130","inserted message not placed under the one above: "+y);
            Require(moved("frame-shape","Y")=="150" && moved("frame-shape","Height")==null,"the frame below did not move whole");
            Require(moved("first-shape","Position")==null && moved("second-shape","Position")==null,"operands moved with a frame that moved whole");
            Require(moved("inner-shape","TargetY")=="190","the message inside the frame below did not move");
        }
        Require(view["Fragments"].Items.Single()["Height"].Raw==(into?"140":"90"),"the frame change was not written to the editor");
        Require(moved(editor["ExecutionSpecifications"].Items[0]["Id"].StringValue(),"Length")=="170","a bar open across the point did not grow");
        var after=SequenceJson.Parse(package.EditorAfterDeleteJson)["Editors"].Items.Single();
        Require(after["Operands"].Items.Single(sh=>sh["Id"].StringValue()=="second-shape")["Position"].Raw==(into?"120":"70"),
            "the delete stage editor lost the operand offset");

        var state=new SequenceTrialState();
        foreach(var e in raw["Entities"].Items)state.Models[e["Id"].StringValue()]=e.ToJsonString();
        foreach(var r in raw["Relations"].Items)
        {
            string id=r["Id"].StringValue();
            state.Relations[id]=new[]{r["SourceId"].StringValue(),r["TargetId"].StringValue(),r["SourceIndex"].Raw,r["TargetIndex"].Raw};
            state.RelationFields[id]=r["MetamodelId"].StringValue();
        }
        foreach(var sh in SequenceEditorDocument.Read(raw.ToJsonString(),ids[0],editorId).Shapes())
        {string id=sh["Id"].StringValue();state.Shapes[id]=sh.ToJsonString();state.ShapeModels[id]=sh["ModelId"].StringValue();}
        // What the SDK side reads back for the shapes that move.
        state.Shapes["frame-shape"]=PumlBuild.Json(new[]{"4","100","332","90"})+"alt";
        state.Shapes["first-shape"]="[]"+PumlBuild.Json(new[]{"ready","30"});
        state.Shapes["second-shape"]="[]"+PumlBuild.Json(new[]{"else","70"});
        state.Shapes["inner-shape"]=PumlBuild.Json(new[]{"inside()","140","140","0"});
        state.Shapes[editor["Messages"].Items[0]["Id"].StringValue()]=PumlBuild.Json(new[]{"probe()","80","80","0"});
        foreach(var bar in editor["ExecutionSpecifications"].Items)
            state.Shapes[bar["Id"].StringValue()]=PumlBuild.Json(new[]{bar["X"].Raw,bar["Y"].Raw,"16",bar["Height"].Raw,bar["Length"].Raw});
        foreach(var lane in editor["Lifelines"].Items)
            state.Shapes[lane["Id"].StringValue()]=PumlBuild.Json(new[]{lane["X"].Raw,"0","100","40"})+lane["LaneLength"].Raw;
        state.Ports[ids[6]]=new[]{ids[4],ids[5],ids[2],ids[3],"sync"};
        state.Ports[inner]=new[]{ids[4],ids[5],ids[2],ids[3],"sync"};
        var expected=state.Expected(package,plan,false);
        Require(expected.Shapes["frame-shape"]==PumlBuild.Json(into?new[]{"4","100","332","140"}:new[]{"4","150","332","90"})+"alt",
            "frame readback not predicted: "+expected.Shapes["frame-shape"]);
        Require(expected.Shapes["second-shape"]=="[]"+PumlBuild.Json(new[]{"else",into?"120":"70"}),
            "operand readback not predicted: "+expected.Shapes["second-shape"]);
        Require(expected.Shapes["first-shape"]=="[]"+PumlBuild.Json(new[]{"ready","30"}),"the first operand readback changed");
    }
    // Three messages at 80, 130 and 180 on bars from 50 to 250 and 80 to 230. The middle
    // one gets a frame around it.
    static void WrappedMessage()
    {
        var seed=SequencePayload.Build(new[]{"root","frame","laneA","laneB","execA","execB","message"},"view","11.1");
        var raw=SequenceJson.Parse(seed.Json);var ids=seed.Ids;
        var editor=raw["Editors"].Items.Single();string editorId=editor["Id"].StringValue();
        editor["ExecutionSpecifications"].Items[0].Properties["Length"]=SequenceJson.Parse("200");
        editor["ExecutionSpecifications"].Items[0].Properties["Height"]=SequenceJson.Parse("200");
        editor["ExecutionSpecifications"].Items[1].Properties["Length"]=SequenceJson.Parse("150");
        editor["ExecutionSpecifications"].Items[1].Properties["Height"]=SequenceJson.Parse("150");
        foreach(var pair in new[]{new[]{"wrapped","130"},new[]{"after","180"}})
        {
            var entity=Clone(raw["Entities"].Items.Single(e=>e["Id"].StringValue()==ids[6]));Set(entity,"Id",pair[0]);raw["Entities"].Items.Add(entity);
            foreach(var r in raw["Relations"].Items.Where(r=>r["TargetId"].StringValue()==ids[6]).ToArray())
            {var copy=Clone(r);Set(copy,"Id",r["Id"].StringValue()+"-"+pair[0]);Set(copy,"TargetId",pair[0]);raw["Relations"].Items.Add(copy);}
            var wire=Clone(editor["Messages"].Items[0]);Set(wire,"Id",pair[0]+"-shape");Set(wire,"ModelId",pair[0]);
            wire.Properties["SourceY"]=SequenceJson.Parse(pair[1]);wire.Properties["TargetY"]=SequenceJson.Parse(pair[1]);editor["Messages"].Items.Add(wire);
        }
        var current=new SequenceDocument();
        current.Elements.Add(new SequenceElement{Id=ids[0],Kind="interaction"});
        current.Elements.Add(new SequenceElement{Id=ids[2],Kind="participant",Parent=ids[0]});
        current.Elements.Add(new SequenceElement{Id=ids[3],Kind="participant",Parent=ids[0]});
        foreach(string id in new[]{ids[4],ids[5]})
        {var e=new SequenceElement{Id=id,Kind="execution",Parent=ids[0]};e.Links["participant"]=new[]{id==ids[4]?ids[2]:ids[3]};current.Elements.Add(e);}
        int order=0;
        foreach(var pair in new[]{new[]{ids[6],"probe()"},new[]{"wrapped","wrapped()"},new[]{"after","after()"}})
        {
            var m=new SequenceElement{Id=pair[0],Kind="message",Parent=ids[0],Order=order++,Text=pair[1]};m.Attributes["sort"]="sync";
            m.Links["sender"]=new[]{ids[2]};m.Links["receiver"]=new[]{ids[3]};m.Links["sendExecution"]=new[]{ids[4]};m.Links["receiveExecution"]=new[]{ids[5]};
            current.Elements.Add(m);
        }
        var desired=current.Copy();
        var box=new SequenceElement{Id="wrap-frame",Kind="fragment",Parent=ids[0],Order=1,Text="alt"};box.Attributes["operator"]="alt";desired.Elements.Add(box);
        desired.Elements.Add(new SequenceElement{Id="wrap-operand",Kind="operand",Parent="wrap-frame",Order=0,Text="ready"});
        var inner=desired.Elements.Single(e=>e.Id=="wrapped");inner.Parent="wrap-operand";inner.Order=0;
        desired.Elements.Single(e=>e.Id=="after").Order=2;
        var plan=new SyncPlan{Expected=desired};
        plan.Changes.Add(new SequenceChange{Action="add",Kind="fragment",Id="wrap-frame",Line=5});
        plan.Changes.Add(new SequenceChange{Action="add",Kind="operand",Id="wrap-operand",Line=5});
        plan.Changes.Add(new SequenceChange{Action="move",Kind="message",Id="wrapped",Line=6});
        plan.Changes.Add(new SequenceChange{Action="move",Kind="message",Id="after",Line=8});
        var gate=SequenceStructurePreflight.Check(current,plan);
        Require(gate.Candidate && gate.MoveMessages.SequenceEqual(new[]{"wrapped"}) && gate.WrapFragments.Count==1 && gate.CanCommit(),
            "a frame around one existing message was not a candidate: "+gate.ToJson());
        var types=new SequenceFrameTypes{Fragment="fragment",Operand="operand",Owns=new[]{"owns","Embed","f1"},
            Branches=new[]{"branches","Embed","f2"},Crossing=new[]{"crossing","Ref","f3"},OperandMessage=new[]{"operand-message","Ref","f4"}};
        types.Operators["alt"]="Alt";
        var package=SequenceStructurePreparation.Build(raw.ToJsonString(),editorId,current,plan,types);
        Require(package.MovedMessages.Length==1 && package.MovedMessages[0].OperandId=="wrap-operand","the wrapped message was not moved into the operand");
        Func<string,string,string> moved=(shape,key)=>{
            var hit=package.ShiftedShapes.Where(m=>m.ShapeId==shape).ToArray();
            if(hit.Length==0)return null;
            int i=Array.IndexOf(hit[0].Keys,key);return i<0?null:hit[0].Values[i];
        };
        string first=editor["Messages"].Items[0]["Id"].StringValue();
        Require(moved(first,"TargetY")==null,"a message above the frame moved");
        Require(moved("wrapped-shape","TargetY")=="190" && moved("wrapped-shape","SourceY")=="190","the wrapped message did not go under the guard");
        Require(moved("after-shape","TargetY")=="278","the message below did not clear the frame: "+moved("after-shape","TargetY"));
        string barA=editor["ExecutionSpecifications"].Items[0]["Id"].StringValue(),barB=editor["ExecutionSpecifications"].Items[1]["Id"].StringValue();
        Require(moved(barA,"Y")==null && moved(barA,"Length")=="298" && moved(barA,"Height")=="298","a bar open across the frame did not grow by the room made");
        Require(moved(barB,"Y")==null && moved(barB,"Length")=="248","the other bar did not grow");
        foreach(var lane in editor["Lifelines"].Items)Require(moved(lane["Id"].StringValue(),"LaneLength")=="338","a lane did not follow the growth");
        var patch=SequenceJson.Parse(package.ReconnectJson);
        var frameShape=patch["Editors"].Items.Single()["Fragments"].Items.Single();
        Require(frameShape["X"].StringValue()=="4" && frameShape["Y"].StringValue()=="120" && frameShape["Width"].StringValue()=="332" && frameShape["Height"].StringValue()=="128",
            "frame not drawn around the message: "+frameShape.ToJsonString());
        Require(patch["Editors"].Items.Single()["Operands"].Items.Single()["Position"].StringValue()=="30","guard not at the generator's offset");
        Require(patch["Relations"].Items.Count(r=>r["MetamodelId"].StringValue()=="operand-message" && r["SourceId"].StringValue()=="wrap-operand"
            && r["TargetId"].StringValue()=="wrapped")==1,"the operand does not point at the wrapped message");
        Require(!patch["Entities"].Items.Any(e=>e["Id"].StringValue()=="wrapped"),"the wrapped message was recreated");

        var state=new SequenceTrialState();
        foreach(var e in raw["Entities"].Items)state.Models[e["Id"].StringValue()]=e.ToJsonString();
        foreach(var r in raw["Relations"].Items)
        {
            string id=r["Id"].StringValue();
            state.Relations[id]=new[]{r["SourceId"].StringValue(),r["TargetId"].StringValue(),r["SourceIndex"].Raw,r["TargetIndex"].Raw};
            state.RelationFields[id]=r["MetamodelId"].StringValue();
        }
        foreach(var sh in SequenceEditorDocument.Read(raw.ToJsonString(),ids[0],editorId).Shapes())
        {string id=sh["Id"].StringValue();state.Shapes[id]=sh.ToJsonString();state.ShapeModels[id]=sh["ModelId"].StringValue();}
        foreach(var wire in editor["Messages"].Items)
            state.Shapes[wire["Id"].StringValue()]=PumlBuild.Json(new[]{"m",wire["TargetY"].Raw,wire["TargetY"].Raw,"0"});
        foreach(var bar in editor["ExecutionSpecifications"].Items)
            state.Shapes[bar["Id"].StringValue()]=PumlBuild.Json(new[]{bar["X"].Raw,bar["Y"].Raw,"16",bar["Height"].Raw,bar["Length"].Raw});
        foreach(var lane in editor["Lifelines"].Items)
            state.Shapes[lane["Id"].StringValue()]=PumlBuild.Json(new[]{lane["X"].Raw,"0","100","40"})+lane["LaneLength"].Raw;
        foreach(string id in new[]{ids[6],"wrapped","after"})state.Ports[id]=new[]{ids[4],ids[5],ids[2],ids[3],"sync"};
        var expected=state.Expected(package,plan,false);
        var link=expected.Relations[package.MovedMessages[0].RelationId];
        Require(link.SequenceEqual(new[]{"wrap-operand","wrapped","0","0"}),"the operand reference was not predicted: "+string.Join(",",link));
        Require(expected.Shapes["after-shape"]==PumlBuild.Json(new[]{"m","278","278","0"}),"the moved message readback was not predicted");
        Require(expected.Shapes[barA]==PumlBuild.Json(new[]{"70","50","16","298","298"}),"the grown bar readback was not predicted: "+expected.Shapes[barA]);
        Require(expected.Models.ContainsKey("wrap-frame") && expected.Models.ContainsKey("wrap-operand"),"the frame was not predicted");
    }
    public static void Run()
    {
        WrappedMessage();
        AddedMessage();
        InsertedAroundFrame(false);
        InsertedAroundFrame(true);
        DeletedMessage();
        AddedParticipant();
        AddedExecution();
        var ordered=new SequenceTrialState();
        ordered.Relations["r1"]=new[]{"old1","m1","0","0"};ordered.Relations["r2"]=new[]{"old2","m2","0","0"};
        ordered.Ports["m1"]=new[]{"send","old1","A","B","sync"};ordered.Ports["m2"]=new[]{"send","old2","A","B","sync"};
        var orderPlan=new SyncPlan{Expected=new SequenceDocument()};
        foreach(string id in new[]{"m1","m2"}){var e=new SequenceElement{Id=id};e.Links["receiver"]=new[]{"B"};orderPlan.Expected.Elements.Add(e);}
        var orderPackage=new SequenceStructurePreparation{DeleteIds=new string[0],ReceiveRelationIds=new[]{"r1","r2"},ReconnectJson=PumlBuild.Json(PumlBuild.Obj("Relations",new object[]{PumlBuild.Obj("Id","r1","SourceId","new","TargetId","m1"),PumlBuild.Obj("Id","r2","SourceId","new","TargetId","m2")}))};
        var appended=ordered.Expected(orderPackage,orderPlan,false);
        Require(appended.Relations["r1"][2]=="0" && appended.Relations["r2"][2]=="1","shared receiver append ordering incorrect");
        Require(ordered.Relations["r2"][2]=="0","original order mutated");
        var reversed=SequenceJson.Parse(orderPackage.ReconnectJson);reversed["Relations"].Items.Reverse();orderPackage.ReconnectJson=reversed.ToJsonString();
        var reverseResult=ordered.Expected(orderPackage,orderPlan,false);
        Require(reverseResult.Relations["r2"][2]=="0" && reverseResult.Relations["r1"][2]=="1","patch order was ignored");
        ordered.Relations["existing"]=new[]{"new","kept","0","0"};
        orderPackage.ReceiveRelationIds=new[]{"r1","r2","existing"};
        var occupied=ordered.Expected(orderPackage,orderPlan,false);
        Require(occupied.Relations["existing"][2]=="0" && occupied.Relations["r2"][2]=="1" && occupied.Relations["r1"][2]=="2","existing destination order not preserved");
        var insertion=SequenceJson.Parse(orderPackage.ReconnectJson);insertion["Relations"].Items[0].Properties["SourceIndex"]=SequenceJson.Parse("0");orderPackage.ReconnectJson=insertion.ToJsonString();
        var inserted=ordered.Expected(orderPackage,orderPlan,false);
        Require(inserted.Relations["r2"][2]=="0" && inserted.Relations["existing"][2]=="1" && inserted.Relations["r1"][2]=="2","explicit insertion order incorrect");
        Spread();
        // Measured on the product: deleting a model closes the gap in the source/field
        // collection that held it, and leaves other fields of the same source alone.
        var owned=new SequenceTrialState();
        owned.Models["execB"]="removed";
        owned.Relations["own-a"]=new[]{"lane","execA","0","0"};
        owned.Relations["own-b"]=new[]{"lane","execB","1","0"};
        owned.Relations["own-c"]=new[]{"lane","execC","2","0"};
        owned.Relations["note-x"]=new[]{"lane","noteX","1","0"};
        owned.Relations["root-b"]=new[]{"root","execB","2","0"};
        owned.Relations["root-m"]=new[]{"root","msgM","3","0"};
        foreach(string id in new[]{"own-a","own-b","own-c","root-b"})owned.RelationFields[id]="owned";
        owned.RelationFields["note-x"]="notes";owned.RelationFields["root-m"]="messages";
        var ownedPackage=new SequenceStructurePreparation{DeleteIds=new[]{"execB"},
            ReconnectJson=PumlBuild.Json(PumlBuild.Obj("Relations",new object[0]))};
        var ownedPlan=new SyncPlan{Expected=new SequenceDocument()};
        var owners=owned.DeletionOwners(ownedPackage);
        Require(owners.Length==2 && owners[0]=="lane" && owners[1]=="root","deletion owners not detected");
        string ownedReport=owned.OrderReport(owners,ownedPackage);
        Require(ownedReport.Contains("0:own-a/owned 1:note-x/notes 1:own-b/owned* 2:own-c/owned"),"deleted sibling not marked in order report");
        var ownedAfter=owned.Expected(ownedPackage,ownedPlan,true);
        Require(!ownedAfter.Relations.ContainsKey("own-b") && !ownedAfter.Relations.ContainsKey("root-b"),"owning relation not removed");
        Require(!ownedAfter.RelationFields.ContainsKey("own-b"),"field entry of a removed relation kept");
        Require(ownedAfter.Relations["own-c"][2]=="1","gap in the same collection was not closed");
        Require(ownedAfter.Relations["note-x"][2]=="1","another field of the same source was shifted");
        Require(ownedAfter.Relations["root-m"][2]=="3","a different field lost its index after deletion");
        Require(ownedAfter.OrderReport(owners,ownedPackage)=="source=lane 0:own-a/owned 1:note-x/notes 1:own-c/owned\nsource=root 3:root-m/messages",
            "post-delete order report incorrect");
        Require(owned.Relations["own-c"][2]=="2" && owned.Relations.ContainsKey("own-b") && owned.RelationFields.ContainsKey("own-b"),
            "deletion report mutated the snapshot");
        var drift=new SequenceTrialState();
        drift.Shapes["new"]=PumlBuild.Json(new[]{"488.000001","140.000001","100","40.000001","40.000001"});
        drift.Shapes["kept"]=PumlBuild.Json(new[]{"10.000001","20","30","40","50"});
        drift.Shapes["text"]="[\"1\",\"2\"]note body";
        var asked=new SequenceTrialState();
        asked.Shapes["new"]=PumlBuild.Json(new[]{"488","140","100","40","40"});
        asked.Shapes["kept"]=PumlBuild.Json(new[]{"10","20","30","40","50"});
        asked.Shapes["text"]="[\"1\",\"2\"]note body";
        drift.Round(new[]{"new","text","absent"});asked.Round(new[]{"new","text","absent"});
        Require(drift.Shapes["new"]==asked.Shapes["new"],"representation drift on a created shape was not reconciled");
        Require(drift.Shapes["kept"]!=asked.Shapes["kept"],"an existing shape was rounded");
        Require(drift.Shapes["text"]=="[\"1\",\"2\"]note body","a shape carrying text was rewritten");
        drift.Shapes["new"]=PumlBuild.Json(new[]{"496","140","100","40","40"});
        drift.Round(new[]{"new"});
        Require(drift.Shapes["new"]!=asked.Shapes["new"],"a real position difference was rounded away");
        var expectedShapes=new SequenceTrialState();var actualShapes=new SequenceTrialState();
        expectedShapes.Shapes["s1"]="[10,20,16,40,40]";expectedShapes.ShapeModels["s1"]="exec";
        actualShapes.Shapes["s1"]="[10,24,16,40,40]";actualShapes.ShapeModels["s1"]="exec";
        Require(expectedShapes.ShapeDifferences(actualShapes).Contains("値: expected=[10,20,16,40,40] actual=[10,24,16,40,40]"),"shape value diagnostic missing");
        Require(!expectedShapes.ShapeDifferences(actualShapes).Contains("所属:"),"equal owner reported");
        actualShapes.Shapes["s1"]=expectedShapes.Shapes["s1"];actualShapes.ShapeModels["s1"]="other";
        Require(expectedShapes.ShapeDifferences(actualShapes).Contains("所属: expected=exec actual=other"),"shape owner diagnostic missing");
        Require(expectedShapes.ShapeDifferences(expectedShapes)=="","equal shapes reported");
        actualShapes.Shapes.Clear();actualShapes.ShapeModels.Clear();
        Require(expectedShapes.ShapeDifferences(actualShapes).Contains("missing actual"),"missing shape diagnostic");
        actualShapes.Shapes["s2"]="[0,0,0,0,0]";
        Require(expectedShapes.ShapeDifferences(actualShapes).Contains("unexpected actual"),"extra shape diagnostic");
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
        // source ordering follows the destination collection; target ordering remains unchanged.
        var sparse=Clone(raw);
        var sparseRelation=sparse["Relations"].Items.Single(r=>r["Id"].StringValue()==receiver["Id"].StringValue());
        sparseRelation.Properties.Remove("SourceIndex");sparseRelation.Properties.Remove("TargetIndex");
        var sparsePackage=SequenceStructurePreparation.Build(sparse.ToJsonString(),editorId,current,plan);
        state.Relations[receiver["Id"].StringValue()][2]="7";state.Relations[receiver["Id"].StringValue()][3]="3";
        var sparseExpected=state.Expected(sparsePackage,plan,true);
        Require(sparseExpected.Relations[receiver["Id"].StringValue()].SequenceEqual(new[]{replacement,ids[6],"0","3"}),"omitted source index did not append; target order lost");
        var sparsePatch=SequenceJson.Parse(sparsePackage.ReconnectJson)["Relations"].Items.Single();
        Require(sparsePatch["SourceIndex"]==null && sparsePatch["TargetIndex"]==null,"omitted indices were synthesized in import JSON");
        Batch(raw,editorId,current,plan,state,ids[5],ids[6]);
        var unscheduled=new SequenceTrialState();unscheduled.Ports["kept"]=new[]{ids[5],"","","","sync"};
        var deletionOnly=new SequenceStructurePreparation{DeleteIds=new[]{ids[5]},ReconnectJson="{\"Relations\":[]}"};
        Reject(()=>unscheduled.Expected(deletionOnly,plan,true),"deleting referenced port accepted");
        var reconnect=SequenceJson.Parse(package.ReconnectJson);var changed=reconnect["Relations"].Items.Single();
        var expected=Clone(receiver);Set(expected,"SourceId",replacement);
        // The order of the collection being left is dropped so the move appends.
        Require(changed["SourceIndex"]==null,"stale source order carried to the destination");
        expected.Properties.Remove("SourceIndex");
        Require(changed.ToJsonString()==expected.ToJsonString(),"relation identity, target order or unknown data changed");
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
