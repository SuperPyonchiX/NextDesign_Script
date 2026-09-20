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
    public static void Run()
    {
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
        Require(barPlan.Changes.Any(c=>c.Kind=="execution" && c.Action=="delete"),"activation difference silently suppressed");
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
        Console.WriteLine("PASS: all-kind semantic plans, mixed changes, ID retention, block edits, ambiguity, moves, source trivia and idempotence");
    }
}
