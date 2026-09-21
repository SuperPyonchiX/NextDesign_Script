public static class SyncTests
{
    static int serial;
    static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
    static SequenceDocument Doc(string body) { return SequenceDocument.Parse("@startuml\nparticipant A\nparticipant B\n"+body+"\n@enduml"); }
    static SyncPlan Plan(SequenceDocument a,SequenceDocument b) { return SyncPlan.Build(a,b,()=>"new-"+(serial++)); }
    static void Ids(SequenceDocument d)
    {
        var ids=d.Elements.ToDictionary(e=>e.Id,e=>"old-"+e.Id);
        foreach(var e in d.Elements) {e.Id=ids[e.Id];e.Parent=e.Parent==null?null:ids[e.Parent];e.Links=e.Links.ToDictionary(p=>p.Key,p=>p.Value.Select(id=>ids[id]).ToArray());}
    }
    // An input that never opens a bar on one side of a message states nothing about that
    // endpoint, so the diagram keeps its own. Deleting a bar still works through the forms
    // that do not rely on a missing link.
    static void OmittedActivations()
    {
        var model=Doc("activate A\nA -> B : call\nactivate B\nB --> A : done\ndeactivate B\ndeactivate A");Ids(model);
        Require(Plan(model,Doc("A -> B : call\nB --> A : done")).IsEmpty,"omitted activations produced a diff");
        Require(Plan(model,Doc("A -> B : call\nactivate B\nB --> A : done\ndeactivate B")).IsEmpty,"sender omission not inherited");
        Require(Plan(model,Doc("activate A\nA -> B : call\nB --> A : done\ndeactivate A")).IsEmpty,"receiver omission not inherited");
        // Stating a different extent for a bar is not an omission, and stays a difference.
        var mixed=Doc("activate A\nA -> B : m1\nactivate B\nB --> A : m2\ndeactivate B\ndeactivate A");Ids(mixed);
        var shortened=Plan(mixed,Doc("A -> B : m1\nactivate B\ndeactivate B\nB --> A : m2"));
        Require(shortened.Changes.Count(c=>c.Kind=="execution" && c.Action=="update")==1,"shortened bar extent was swallowed by inheritance");
        Require(!shortened.Changes.Any(c=>c.Kind=="execution" && (c.Action=="add" || c.Action=="delete")),"shortened bar extent recreated bars");
        var inherited=Plan(model,Doc("A -> B : call\nB --> A : done"));
        Require(inherited.InheritedPorts.Count>0 && inherited.InheritRefusals.Count==0,"inheritance was not reported");
        Require(Plan(inherited.Expected,Doc("A -> B : call\nB --> A : done")).IsEmpty,"inherited plan is not idempotent");
        Require(inherited.Expected.Elements.Where(e=>e.Kind=="execution").All(e=>e.Id.StartsWith("old-")),"inheritance invented execution ids");
        var renamed=Plan(model,Doc("A -> B : renamed\nB --> A : done"));
        Require(renamed.Changes.Count(c=>c.Action=="update" && c.Kind=="message")==1,"text edit hidden by inheritance");
        Require(!renamed.Changes.Any(c=>c.Kind=="execution"),"text edit disturbed the kept bars");
        // The confirmed way to delete a bar: leave the enclosing activate open.
        var nested=Doc("activate A\nactivate B\nA -> B : call\nactivate B\ndeactivate B\nB --> A : done\ndeactivate B\ndeactivate A");Ids(nested);
        var dropped=Plan(nested,Doc("activate A\nactivate B\nA -> B : call\nB --> A : done\ndeactivate B\ndeactivate A"));
        Require(dropped.Changes.Count(c=>c.Kind=="execution" && c.Action=="delete")==1,"inner bar deletion lost");
        Require(!dropped.Changes.Any(c=>c.Kind=="execution" && c.Action=="add"),"inner bar deletion recreated bars");
        var idle=Doc("activate A\nA -> B : call\nactivate B\ndeactivate B\nactivate B\ndeactivate B\ndeactivate A");Ids(idle);
        Require(Plan(idle,Doc("activate A\nA -> B : call\nactivate B\ndeactivate B\ndeactivate A"))
            .Changes.Count(c=>c.Kind=="execution" && c.Action=="delete")==1,"idle bar deletion lost");
        // A bar cannot be kept when the frame that owns it is going away.
        var framed=Doc("opt scope\nA -> B : inside\nactivate B\ndeactivate B\nend");Ids(framed);
        var unframed=Plan(framed,Doc("A -> B : inside"));
        Require(unframed.Changes.Any(c=>c.Kind=="execution" && c.Action=="delete"),"bar kept although its frame is deleted");
    }
    static void StructurePreflight()
    {
        var before=Doc("activate A\nA -> B : call\nactivate B\ndeactivate B\ndeactivate A");
        var message=before.Elements.Single(e=>e.Kind=="message");
        string original=message.Links["receiveExecution"].Single();
        var extra=before.Elements.Single(e=>e.Id==original).Copy();extra.Id="second-receiver";extra.Order+=1;before.Elements.Add(extra);
        var after=before.Copy();after.Elements.Single(e=>e.Id==message.Id).Links["receiveExecution"]=new[]{extra.Id};
        var plan=new SyncPlan{Expected=after};plan.Changes.Add(new SequenceChange{Action="update",Kind="message",Id=message.Id,Line=3});
        string unchanged=before.ToJson()+plan.Expected.ToJson()+plan.ToJson();
        var candidate=SequenceStructurePreflight.Check(before,plan);
        Require(candidate.Candidate && candidate.ReconnectMessages.SequenceEqual(new[]{message.Id}),"existing receiver candidate missing");
        Require(unchanged==before.ToJson()+plan.Expected.ToJson()+plan.ToJson(),"preflight changed diff");
        after.Elements.Single(e=>e.Id==message.Id).Links.Remove("receiveExecution");
        var missing=SequenceStructurePreflight.Check(before,plan);
        Require(!missing.Candidate && missing.Reasons.Any(r=>r.Contains("書込み表現が未確定")),"missing receiver was allowed");
        after.Elements.Single(e=>e.Id==message.Id).Links["receiveExecution"]=new[]{extra.Id};
        after.Elements.Single(e=>e.Id==message.Id).Text="changed";
        Require(!SequenceStructurePreflight.Check(before,plan).Candidate,"text change silently accepted");
        after.Elements.Single(e=>e.Id==message.Id).Text=message.Text;
        plan.Changes.Add(new SequenceChange{Action="move",Kind="message",Id=message.Id,Line=3});
        Require(!SequenceStructurePreflight.Check(before,plan).Candidate,"supported subset accepted");
        plan.Changes.RemoveAt(1);
        after.Elements.Single(e=>e.Id==extra.Id).Links["participant"]=message.Links["sender"];
        Require(!SequenceStructurePreflight.Check(before,plan).Candidate,"wrong lifeline accepted");
        after.Elements.Single(e=>e.Id==extra.Id).Links["participant"]=message.Links["receiver"];
        after.Elements.RemoveAll(e=>e.Id==original);
        plan.Changes.Add(new SequenceChange{Action="delete",Kind="execution",Id=original});
        Require(SequenceStructurePreflight.Check(before,plan).DeleteExecutions.SequenceEqual(new[]{original}),"unreferenced execution deletion missing");
        Require(!SequenceStructurePreflight.Check(before,new SyncPlan{Expected=before.Copy()}).Candidate,"no-op marked candidate");
    }
    // Going the other way from the structural samples: the input asks for a receive bar
    // that the diagram does not have. Acceptance is settled here; writing is not.
    static void AddedExecutionPreflight()
    {
        var before=Doc("activate B\nA -> B : first\nB --> A : firstDone\nA -> B : second\ndeactivate B");
        var after=Doc("activate B\nA -> B : first\nB --> A : firstDone\nA -> B : second\nactivate B\ndeactivate B\ndeactivate B");
        var plan=Plan(before,after);
        var added=plan.Expected.Elements.Where(e=>e.Kind=="execution" && !before.Elements.Any(o=>o.Id==e.Id)).ToArray();
        Require(added.Length==1,"sample does not add exactly one execution");
        string unchanged=before.ToJson()+plan.Expected.ToJson()+plan.ToJson();
        var gate=SequenceStructurePreflight.Check(before,plan);
        Require(gate.AddExecutions.SequenceEqual(new[]{added[0].Id}),"added receive bar was not accepted: "+plan.ToJson()+gate.ToJson());
        Require(gate.Candidate && gate.Reasons.Count==0,"added receive bar was not a candidate");
        Require(gate.ReconnectMessages.Count==1,"the message pointing at the new bar was not accepted");
        Require(gate.CanCommit(true),"a receiver change with an addition cannot be committed");
        Require(!gate.CanCommit(false),"the deletion-only mode accepted an addition");
        Require(unchanged==before.ToJson()+plan.Expected.ToJson()+plan.ToJson(),"preflight changed diff");

        Func<Action<SequenceElement>,SequenceStructurePreflight> probe=mutate=>{
            var copy=plan.Expected.Copy();mutate(copy.Elements.Single(e=>e.Id==added[0].Id));
            var trial=new SyncPlan{Expected=copy};trial.Changes.AddRange(plan.Changes);
            return SequenceStructurePreflight.Check(before,trial);
        };
        Require(probe(e=>e.Links["participant"]=new string[0]).AddExecutions.Count==0,"execution without a participant accepted");
        Require(probe(e=>e.Links["participant"]=new[]{added[0].Id}).AddExecutions.Count==0,"execution owned by a new participant accepted");
        string interaction=before.Elements.Single(e=>e.Kind=="interaction").Id;
        Require(probe(e=>e.Links["outer"]=new[]{interaction}).AddExecutions.Count==0,"nesting in a non-execution accepted");
        Require(probe(e=>e.Links["note"]=new[]{added[0].Id}).AddExecutions.Count==0,"extra link on a new execution accepted");
        var outerId=before.Elements.First(e=>e.Kind=="execution").Id;
        Require(probe(e=>e.Links["outer"]=new[]{outerId}).AddExecutions.Count==1,"nesting in an existing bar of the same participant rejected");

        var sender=before.Elements.First(e=>e.Kind=="message").Links["sender"].Single();
        var wrongLane=plan.Expected.Copy();wrongLane.Elements.Single(e=>e.Id==added[0].Id).Links["outer"]=new[]{outerId};
        wrongLane.Elements.Single(e=>e.Id==added[0].Id).Links["participant"]=new[]{sender};
        Require(SequenceStructurePreflight.Check(before,new SyncPlan{Expected=wrongLane}).AddExecutions.Count==0,"nesting across lifelines accepted");

        var unused=plan.Expected.Copy();
        unused.Elements.Single(e=>e.Kind=="message" && e.Links.ContainsKey("receiveExecution")
            && e.Links["receiveExecution"].Contains(added[0].Id)).Links["receiveExecution"]=new[]{outerId};
        var unusedPlan=new SyncPlan{Expected=unused};unusedPlan.Changes.AddRange(plan.Changes);
        Require(SequenceStructurePreflight.Check(before,unusedPlan).AddExecutions.Count==0,"execution nothing receives on accepted");
    }
    public static void Run()
    {
        StructurePreflight();
        AddedExecutionPreflight();
        OmittedActivations();
        string body="activate A\nA -> B : first\nalt ready\nA -> B : work\nnote over B\nline one\nline two\nend note\nelse wait\nB --> A : wait\nref over A,B : Service\nend\ndeactivate A";
        var old=Doc(body);Ids(old);
        Require(Plan(old,Doc(body)).IsEmpty,"all-kind no-op changed semantics");
        Require(Plan(old,Doc("' comment\n\n"+body)).IsEmpty,"source line numbers treated as semantics");
        foreach(var pair in new[]{new[]{"first","renamed"},new[]{"ready","prepared"},new[]{"line two","revised"},new[]{"Service","NewService"},new[]{"alt ready","par ready"}})
        {
            var p=Plan(old,Doc(body.Replace(pair[0],pair[1])));
            Require(p.Changes.Any(c=>c.Action=="update"),"attribute edit not detected: "+pair[0]);
            Require(Plan(p.Expected,Doc(body.Replace(pair[0],pair[1]))).IsEmpty,"second run not empty: "+pair[0]);
        }
        foreach(string construct in new[]{"note over A : extra","ref over A : extra","opt extra\nA -> B : inside\nend","A -> B : extra"})
        {
            var p=Plan(old,Doc(body+"\n"+construct));
            Require(p.Changes.Any(c=>c.Action=="add"),"addition missing: "+construct);
            var back=Plan(p.Expected,Doc(body));
            Require(back.Changes.Any(c=>c.Action=="delete"),"deletion missing: "+construct);
            Require(Plan(back.Expected,Doc(body)).IsEmpty,"add/delete did not converge");
        }
        foreach(int size in new[]{1,5,10})
        {
            var labels=Enumerable.Range(0,25).Select(i=>"A -> B : m"+i).ToArray();
            var all=Doc(string.Join("\n",labels));Ids(all);
            var reduced=Doc(string.Join("\n",labels.Take(7).Concat(labels.Skip(7+size))));
            var p=Plan(all,reduced);
            Require(p.Changes.Count(c=>c.Action=="delete")==size,"block deletion count");
            Require(!p.Changes.Any(c=>c.Action=="move" || c.Action=="update"),"suffix moved by deletion");
            foreach(var e in p.Expected.Elements.Where(e=>e.Kind=="message"))Require(e.Id==all.Elements.Single(n=>n.Text==e.Text && n.Kind==e.Kind).Id,"suffix ID changed");
            var restored=Plan(p.Expected,Doc(string.Join("\n",labels)));
            Require(restored.Changes.Count(c=>c.Action=="add")==size,"restoration count");
        }
        var empty=Plan(old,Doc(""));Require(empty.Changes.Count(c=>c.Action=="delete")==old.Elements.Count-3,"all events deletion");
        Require(Plan(empty.Expected,Doc("")).IsEmpty,"empty second run");
        var move=Doc("A -> B : first\nB --> A : second\nA -> B : third");Ids(move);
        var reordered=Plan(move,Doc("A -> B : third\nA -> B : first\nB --> A : second"));
        Require(reordered.Changes.Any(c=>c.Action=="move") && !reordered.Changes.Any(c=>c.Action=="add" || c.Action=="delete"),"unique move recreated IDs");
        var duplicates=Doc("alt one\nA -> B : same\nelse two\nA -> B : same\nend");Ids(duplicates);
        Require(Plan(duplicates,Doc("alt one\nA -> B : same\nelse two\nA -> B : same\nend")).IsEmpty,"cross-branch repeated message no-op");
        string repeated="alt same\nA -> B : same\nA -> B : same\nelse same\nA -> B : same\nA -> B : same\nend";
        var twins=Doc(repeated+"\n"+repeated);Ids(twins);
        Require(Plan(twins,Doc(repeated+"\n"+repeated)).IsEmpty,"nested repeated subtrees recreated on no-op");
        var frames=Doc("opt one\nA -> B : first\nend\nopt two\nA -> B : second\nend");Ids(frames);
        var frameDeletion=Plan(frames,Doc("opt two\nA -> B : second\nend"));
        var secondFrame=frames.Elements.Single(e=>e.Kind=="operand" && e.Text=="two").Parent;
        Require(frameDeletion.Expected.Elements.Single(e=>e.Kind=="fragment").Id==secondFrame,"first deleted frame consumed surviving frame identity");
        Require(!frameDeletion.Changes.Any(c=>c.Action=="update" || c.Action=="move"),"frame deletion changed surviving subtree");
        var switched=Plan(twins,Doc((repeated+"\n"+repeated).Replace("alt same","par same")));
        Require(Plan(switched.Expected,Doc((repeated+"\n"+repeated).Replace("alt same","par same"))).IsEmpty,"ambiguous container replacement did not converge");
        var interleaved=Doc("A -> B : first\nnote over A : remark\nB --> A : second");Ids(interleaved);
        Require(Plan(interleaved,Doc("A -> B : first\nB --> A : second\nnote over A : remark")).Changes.Any(c=>c.Action=="move" && c.Kind=="note"),"cross-kind note move lost");
        var noteTargets=Doc("note over A : same\nnote over B : same");Ids(noteTargets);
        var noteMove=Plan(noteTargets,Doc("note over B : same\nnote over A : same"));
        Require(!noteMove.Changes.Any(c=>c.Action=="update"),"same-label note targets confused");
        var ambiguous=Doc("A -> B : old1\nA -> B : old2");Ids(ambiguous);
        var replacement=Plan(ambiguous,Doc("A -> B : new1\nA -> B : new2"));
        Require(replacement.Recreated==2,"ambiguous rows silently paired");
        Require(replacement.Changes.Count(c=>c.Action=="delete")==2 && replacement.Changes.Count(c=>c.Action=="add")==2,"ambiguous range not recreated");
        var invalid=Doc("");invalid.Elements.Last().Parent=invalid.Elements.Last().Id;
        bool failed=false;try {invalid.Validate();}catch(InvalidOperationException){failed=true;}Require(failed,"cycle accepted");
        var positions=SequenceLocalLayout.Arrange(new[]{
            new SequenceLayoutSlot{Id="a",Size=20,Existing=100},new SequenceLayoutSlot{Id="added",Size=20},
            new SequenceLayoutSlot{Id="b",Size=20,Existing=200}},0,10);
        Require(positions["a"]==100 && positions["added"]==130 && positions["b"]==200,"available gap not used");
        positions=SequenceLocalLayout.Arrange(new[]{new SequenceLayoutSlot{Id="a",Size=20,Existing=100},
            new SequenceLayoutSlot{Id="added",Size=100},new SequenceLayoutSlot{Id="b",Size=20,Existing=140}},0,10);
        Require(positions["a"]==100 && positions["b"]==240,"insertion did not move only necessary suffix");
        var candidates=new[]{new SequenceReferenceCandidate{Id="1",Name="Service",Path="A.Service"},new SequenceReferenceCandidate{Id="2",Name="Service",Path="B.Service"}};
        Require(SequenceReferenceResolver.Find("A.Service",candidates).Single().Id=="1","qualified ref resolution");
        Require(SequenceReferenceResolver.Find("Service",candidates).Length==2,"ambiguous ref selected silently");
        Require(SequenceReferenceResolver.Find("missing",candidates).Length==0,"missing ref invented");
        Require(SequenceJson.Parse(old.ToJson())["Elements"].Items.Count==old.Elements.Count,"diagnostic serialization lost elements");
        var membership=Doc("opt outer\nopt inner\nA -> B : inside\nend\nend");
        var member=membership.Elements.Single(e=>e.Kind=="message");
        var inner=member.Parent;var outer=membership.Elements.Single(e=>e.Kind=="operand" && e.Text=="outer").Id;
        var evidence=new[]{new SequenceMembership{Child=member.Id,Parent=outer,Evidence="ancestor"},
            new SequenceMembership{Child=member.Id,Parent=inner,Evidence="direct"},
            new SequenceMembership{Child=member.Id,Parent=inner,Evidence="SDK duplicate"}};
        foreach(var order in new[]{evidence,evidence.Reverse().ToArray()})
        {
            var copy=membership.Copy();copy.Elements.Single(e=>e.Id==member.Id).Parent="root";
            SequenceMembership.Resolve(copy,order,line=>{});copy.Validate();
            Require(copy.Elements.Single(e=>e.Id==member.Id).Parent==inner,"nested membership lost nearest operand");
        }
        var conflict=Doc("alt left\nA -> B : one\nelse right\nA -> B : two\nend");
        var rows=conflict.Elements.Where(e=>e.Kind=="message").ToArray();
        var logs=new List<string>();failed=false;
        try {SequenceMembership.Resolve(conflict,new[]{new SequenceMembership{Child=rows[0].Id,Parent=rows[1].Parent,Evidence="conflict"}},logs.Add);}
        catch(InvalidOperationException){failed=true;}
        Require(failed && logs.Any(l=>l.Contains("conflict")),"unrelated operands silently selected or evidence lost");
        failed=false;try {SequenceMembership.Resolve(membership,new[]{new SequenceMembership{Child=outer,Parent=inner,Evidence="cycle"}},line=>{});}
        catch(InvalidOperationException){failed=true;}Require(failed,"membership cycle accepted");
        var regionOuter=new SequenceRegion{Id="outer-branch",Fragment="outer-frame",X=0,Y=100,Width=400,Height=300};
        var regionInner=new SequenceRegion{Id="inner-frame",X=20,Y=140,Width=350,Height=150};
        Require(SequenceRegion.Nesting(new[]{regionOuter},new[]{regionInner}).Single().Parent=="outer-branch","geometry did not supplement missing nesting relation");
        Require(!SequenceRegion.Contains(regionOuter,new SequenceRegion{X=20,Y=390,Width=350,Height=150}),"crossing branch boundary treated as containment");
        Require(!SequenceRegion.Contains(regionOuter,new SequenceRegion{X=0,Y=100,Width=400,Height=300}),"identical bounds arbitrarily nested");
        Require(!SequenceRegion.Contains(regionOuter,new SequenceRegion{X=double.NaN,Y=140,Width=350,Height=150}),"invalid geometry accepted");
        var missingNesting=membership.Copy();
        var innerFrame=missingNesting.Elements.Single(e=>e.Id==inner).Parent;
        missingNesting.Elements.Single(e=>e.Id==innerFrame).Parent="root";
        missingNesting.Elements.Single(e=>e.Id==member.Id).Parent="root";
        var nesting=SequenceRegion.Nesting(new[]{new SequenceRegion{Id=outer,Fragment="unused",X=0,Y=100,Width=400,Height=300}},
            new[]{new SequenceRegion{Id=innerFrame,X=20,Y=140,Width=350,Height=150}});
        SequenceMembership.Resolve(missingNesting,evidence.Concat(nesting),line=>{});
        missingNesting.Validate();Require(missingNesting.Elements.Single(e=>e.Id==member.Id).Parent==inner,"ancestor SDK membership unresolved without model nesting relation");
        var privateDoc=Doc("A -> B : private-design-label");Ids(privateDoc);
        privateDoc.Elements.Single(e=>e.Kind=="participant" && e.Text=="A").Text=" A\n";
        var auditPlan=Plan(privateDoc,Doc("A -> B : private-design-label"));
        var audit=SequenceAudit.Reasons(privateDoc,Doc("A -> B : private-design-label"),auditPlan);
        Require(!audit.Contains("private-design-label") && !audit.Contains("old-"),"screenshot exposes design text or identifiers");
        Require(audit.Contains("参加者 追加+削除") && audit.Contains("差分操作数"),"normalization experiment missing");
        Require(SequenceAudit.Summary(auditPlan,3).Split('\n').Length<=16,"summary exceeds screenshot row budget");
        Require(privateDoc.Elements.Single(e=>e.Kind=="participant" && e.Text.Contains("A")).Text==" A\n","diagnostic mutated current document");
        Require(auditPlan.IsEmpty,"participant whitespace triggered a semantic change");
        var withBars=Doc("opt check\nactivate A\nA -> B : first\ndeactivate A\nend");Ids(withBars);
        var withoutBars=Doc("opt check\nA -> B : first\nend");
        var barPlan=Plan(withBars,withoutBars);
        Require(!barPlan.Changes.Any(c=>(c.Kind=="fragment" || c.Kind=="operand" || c.Kind=="message") && (c.Action=="add" || c.Action=="delete")),"activation difference recreated enclosing structure");
        // Omitting the activate states nothing about the endpoint, so the bar stays.
        // OmittedActivations() holds the forms that do delete a bar.
        Require(!barPlan.Changes.Any(c=>c.Kind=="execution"),"omitted activation was treated as a deletion");
        var triggered=Doc("A -> B : begin\nactivate B\nB --> A : end\ndeactivate B");
        var triggerMessage=triggered.Elements.Single(e=>e.Kind=="message" && e.Text=="begin");
        Require(triggerMessage.Links["receiveExecution"].Single()==triggered.Elements.Single(e=>e.Kind=="execution").Id,"post-message activate not bound to receiver");
        var outgoing=Doc("A -> B : begin\nactivate A\nA -> B : next");
        Require(!outgoing.Elements.Single(e=>e.Kind=="message" && e.Text=="begin").Links.ContainsKey("receiveExecution"),"sender activation bound to receiver");
        var bars=Doc("A -> B : first\nactivate B\nB --> A : one\ndeactivate B\nA -> B : second\nactivate B\nB --> A : two\ndeactivate B");Ids(bars);
        var shifted=bars.Copy();foreach(var bar in shifted.Elements.Where(e=>e.Kind=="execution"))bar.Links["startAfter"]=new string[0];
        var barsPlan=Plan(bars,shifted);
        Require(!barsPlan.Changes.Any(c=>c.Kind=="execution" && (c.Action=="add" || c.Action=="delete")),"boundary difference recreated bars with identical incident messages");
        Require(barsPlan.Changes.Count(c=>c.Kind=="execution" && c.Action=="update")==2,"boundary differences hidden");
        var unused=Doc("activate A\ndeactivate A");Ids(unused);
        Require(Plan(unused,Doc("activate A\ndeactivate A")).IsEmpty,"unique empty bar no-op recreated");
        var positionsOnly=triggered.Copy();Ids(positionsOnly);var reorderedBars=positionsOnly.Copy();
        foreach(var bar in reorderedBars.Elements.Where(e=>e.Kind=="execution"))bar.Order+=100;
        Require(Plan(positionsOnly,reorderedBars).IsEmpty,"interval storage order shifted unrelated message ordering");
        var refLabels=Doc("ref over A : Service Name");Ids(refLabels);
        refLabels.Elements.Single(e=>e.Kind=="ref").Text="Service\nName";
        Require(Plan(refLabels,Doc("ref over A : Service Name")).IsEmpty,"exported ref whitespace caused recreation");
        Require(!audit.Contains("private-design-label") && !audit.Contains("old-"),"residual diagnostic contains confidential values");
        var annotationScope=Doc("opt left\nref over A,B : Shared\nend\nopt right\nref over A,B : Shared\nend");Ids(annotationScope);
        Require(Plan(annotationScope,Doc("opt left\nref over A,B : Shared\nend\nopt right\nref over A,B : Shared\nend")).IsEmpty,"same-label refs in different operands recreated");
        var freeNote=Doc("opt scope\nnote over A : remark\nend");Ids(freeNote);
        var freeNode=freeNote.Elements.Single(e=>e.Kind=="note");freeNode.Links["targets"]=new string[0];freeNode.Attributes["position"]="free";
        var noteProjection=Plan(freeNote,Doc("opt scope\nnote over A : remark\nend"));
        Require(noteProjection.Changes.Any(c=>c.Kind=="note" && c.Action=="update") && !noteProjection.Changes.Any(c=>c.Kind=="note" && (c.Action=="add" || c.Action=="delete")),"unique note projection replaced identity or hid semantic difference");
        var across=Doc("opt scope\nnote across\nremark\nend note\nend");
        Require(across.Elements.Single(e=>e.Kind=="note").Links["targets"].Length==0,"across invented participant anchor");
        Require(Plan(freeNote,across).IsEmpty,"free note export roundtrip changed connection or position");
        Require(Doc("note across : remark").Elements.Single(e=>e.Kind=="note").Text=="remark","inline across note parse");
        var placementOnly=SequenceNotePolicy.Build(freeNote,Doc("opt scope\nnote over A : remark\nend"),()=>"note-policy-new");
        Require(placementOnly.IsEmpty,"note placement created an anchor change");
        Require(placementOnly.Expected.Elements.Single(e=>e.Kind=="note").Links["targets"].Length==0,"free note acquired anchor");
        var linkedNote=Doc("note over A : linked");Ids(linkedNote);
        var renamedNote=SequenceNotePolicy.Build(linkedNote,Doc("note over B : renamed"),()=>"new-note");
        Require(renamedNote.Changes.Any(c=>c.Kind=="note" && c.Action=="update"),"note text edit hidden");
        Require(renamedNote.Expected.Elements.Single(e=>e.Kind=="note").Links["targets"].SequenceEqual(linkedNote.Elements.Single(e=>e.Kind=="note").Links["targets"]),"existing anchor replaced by placement hint");
        Require(SequenceNotePolicy.Build(Doc(""),Doc("note over B : added"),()=>"new-note").Expected.Elements.Single(e=>e.Kind=="note").Links["targets"].Length==0,"new note acquired anchor");
        var noteBody="heading\n  detail\n\nnext";
        var sourceNote=Doc("note over A : original");Ids(sourceNote);
        sourceNote.Elements.Single(e=>e.Kind=="note").Text=noteBody;
        var exportedNote=Doc("note over A\n  heading\n    detail\n\n  next\nend note");
        Require(SequenceNotePolicy.Build(sourceNote,exportedNote,()=>"new-note").IsEmpty,"export indentation created note update");
        Require(Doc("opt scope\n  note over A\n    heading\n      detail\n\n    next\n  end note\nend").Elements.Single(e=>e.Kind=="note").Text==noteBody,"nested note indentation or paragraphs lost");
        Require(Doc("note across\n  heading\n    detail\n\n  next\nend note").Elements.Single(e=>e.Kind=="note").Text==noteBody,"across block indentation differs");
        Require(Doc("note over A\nheading\n  detail\nend note").Elements.Single(e=>e.Kind=="note").Text=="heading\n  detail","unformatted note indentation lost");
        Require(Doc("note over A\n    intentional\nend note").Elements.Single(e=>e.Kind=="note").Text=="  intentional","intentional leading indent collapsed");
        var editedNote=Doc("note over A\n  heading\n      detail\n\n  next\nend note");
        Require(SequenceNotePolicy.Build(sourceNote,editedNote,()=>"new-note").Changes.Any(c=>c.Kind=="note" && c.Action=="update"),"relative whitespace edit hidden");
        Require(SequenceNotePolicy.Build(sourceNote,Doc("note over A\n  heading\n    detail\n  next\nend note"),()=>"new-note").Changes.Any(c=>c.Kind=="note" && c.Action=="update"),"paragraph deletion hidden");
        var containers=Doc("alt private-one\nA -> B : x\nend\nopt private-two\nA -> B : y\nend");Ids(containers);
        foreach(var f in containers.Elements.Where(e=>e.Kind=="fragment"))f.Attributes["operator"]="private-operator";
        var containerInput=Doc("alt private-one\nA -> B : x\nend\nopt private-two\nA -> B : y\nend");
        var containerPlan=Plan(containers,containerInput);
        string containerAudit=SequenceAudit.Reasons(containers,containerInput,containerPlan);
        Require(containerAudit.Contains("図側候補=") && containerAudit.Contains("演算子=その他"),"unmatched container diagnostics missing");
        Require(!containerAudit.Contains("private-one") && !containerAudit.Contains("private-two") && !containerAudit.Contains("private-operator"),"container diagnostics disclosed private text");
        // Two sibling alts cannot be distinguished by operator alone. A child
        // annotation move must not destroy the identities of both containers.
        string siblingBlocks="alt first\nA -> B : one\nloop repeat\nB -> A : nested\nend\nref over A : hint\nelse fallback\nB -> A : two\nend\nalt second\nA -> B : three\nelse fallback\nB -> A : four\nend";
        var siblingOld=Doc(siblingBlocks);Ids(siblingOld);
        var siblingInput=Doc(siblingBlocks.Replace("end\nref over A : hint","ref over A : hint\nend").Replace("three","changed"));
        var siblingPlan=Plan(siblingOld,siblingInput);
        Require(!siblingPlan.Changes.Any(c=>(c.Kind=="fragment" || c.Kind=="operand") && (c.Action=="add" || c.Action=="delete")),"sibling alt child edit recreated containers");
        Require(siblingPlan.Changes.Any(c=>c.Kind=="ref" && c.Action=="move") && siblingPlan.Changes.Any(c=>c.Kind=="message" && c.Action=="update"),"container matching hid real child edits");
        Require(Plan(siblingOld,Doc(siblingBlocks)).IsEmpty,"sibling alt no-op changed");
        // Reconnect the receive end to the enclosing bar, the shape the reconnect samples use.
        // Simply removing the link would now mean "unspecified" and be inherited, not reported.
        var portBody="activate B\nactivate A\nA -> B : hidden-label\nactivate B\ndeactivate B\nB --> A : reply\ndeactivate B\ndeactivate A\nref over A : hidden-ref";
        var portOld=Doc(portBody);Ids(portOld);
        var portNew=Doc(portBody.Replace("hidden-label\nactivate B\ndeactivate B","hidden-label"));
        portOld.Elements.Single(e=>e.Kind=="ref").Attributes["reference"]="secret-target";
        string portReport=SequenceAudit.Reasons(portOld,portNew,Plan(portOld,portNew));
        Require(portReport.Contains("receiveExecution") && portReport.Contains("入力=未解決"),"port and reference diagnostics missing >>>"+portReport.Replace("","|"));
        Require(!portReport.Contains("secret-target") && !portReport.Contains("hidden-label") && !portReport.Contains("hidden-ref"),"residual details disclosed source data");
        var spaceRefs=new[]{new SequenceReferenceCandidate{Id="wide",Name="Task　Start",Path="Area　One::Task　Start"},new SequenceReferenceCandidate{Id="other",Name="Task  Start",Path="Else::Task  Start"}};
        Require(SequenceReferenceResolver.Find("Task Start",spaceRefs).Length==2,"normalized ambiguity hidden");
        Require(SequenceReferenceResolver.Find("Task　Start",spaceRefs).Single().Id=="wide","exact match lost priority");
        Require(SequenceReferenceResolver.Find("Area One::Task Start",spaceRefs).Single().Id=="wide","qualified whitespace fallback failed");
        Require(SequenceReferenceResolver.Find("Task Start",spaceRefs.Take(1)).Single().Id=="wide","wide space fallback failed");
        Require(SequenceReferenceResolver.Find("TaskStart",spaceRefs).Length==0,"significant word boundary removed");
        var selfReturn=Doc("activate A\nA -> A : nested\nactivate A\nA --> A : result\ndeactivate A\ndeactivate A");
        var reply=selfReturn.Elements.Single(e=>e.Kind=="message" && e.Attributes["sort"]=="reply");
        var returnInner=selfReturn.Elements.Single(e=>e.Id==reply.Links["sendExecution"][0]);
        Require(reply.Links["receiveExecution"].SequenceEqual(returnInner.Links["outer"]),"self reply did not return to outer activation");
        var selfNoClose=Doc("activate A\nactivate A\nA --> A : result\nA -> B : later\ndeactivate A\ndeactivate A").Elements.First(e=>e.Kind=="message");
        Require(selfNoClose.Links["sendExecution"].SequenceEqual(selfNoClose.Links["receiveExecution"]),"self reply without immediate close was guessed");
        var destroyInput=Doc("A -> B : finish-one\ndestroy B\nB -> A : finish-two\ndestroy A");
        Require(destroyInput.Elements.Count(e=>e.Kind=="message" && e.Attributes["sort"]=="destroy")==2,"destroy message kinds lost");
        Require(destroyInput.Elements.Count(e=>e.Kind=="destroy")==2,"destroy markers lost");
        var destroyCurrent=destroyInput.Copy();Ids(destroyCurrent);
        foreach(var e in destroyCurrent.Elements.Where(e=>e.Kind=="message"))e.Attributes["sort"]="destroy";
        Require(Plan(destroyCurrent,destroyInput).IsEmpty,"two destruction messages recreated");
        Require(Doc("A -> B : normal\ndestroy A").Elements.Single(e=>e.Kind=="message").Attributes["sort"]=="sync","unrelated destruction changed message kind");
        Require(Doc("A -> B : normal\nnote over B : gap\ndestroy B").Elements.Single(e=>e.Kind=="message").Attributes["sort"]=="sync","non-adjacent destruction changed message kind");
        Require(Doc("A --> B : reply\ndestroy B").Elements.Single(e=>e.Kind=="message").Attributes["sort"]=="reply","reply kind overwritten");
        Console.WriteLine("PASS: all-kind semantic plans, mixed changes, ID retention, block edits, ambiguity, moves, source trivia and idempotence");
    }
}
