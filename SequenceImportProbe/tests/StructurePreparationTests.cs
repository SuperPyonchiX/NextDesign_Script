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
        Require(!gate.CanCommit(true) && !gate.CanCommit(false),"addition reached a commit mode");

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
    public static void Run()
    {
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
