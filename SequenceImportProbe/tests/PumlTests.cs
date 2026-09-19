public static class PumlTests
{
    public static void Run(string directory,string samples)
    {
        if(PumlTypeSelection.Destruction(new[]{"view-concrete"},new string[0])!="view-concrete")throw new Exception("View type not selected without sample");
        if(PumlTypeSelection.Destruction(new[]{"view-a","view-b"},new[]{"view-b"})!="view-b")throw new Exception("Sample did not resolve view ambiguity");
        if(PumlTypeSelection.Destruction(new string[0],new[]{"sample"})!="sample")throw new Exception("Observed type fallback failed");
        int rejectedTypes=0;
        foreach(var candidate in new[]{new string[0],new[]{"a","b"}})
        { try { PumlTypeSelection.Destruction(candidate,new string[0]); } catch(InvalidOperationException) { rejectedTypes++; } }
        try { PumlTypeSelection.Destruction(new[]{"a"},new[]{"b"}); } catch(InvalidOperationException) { rejectedTypes++; }
        if(rejectedTypes!=3)throw new Exception("Missing or conflicting view types accepted");
        var seed=SequencePayload.Build(new[]{"root","frame","ll","ll","exec","exec","message"},"view","13.0");
        var changed=SequenceUpdateProbe.Payload(seed.Json);
        if(changed==seed.Json || changed.Contains(SequencePayload.Q("probe()")) || !changed.Contains(SequencePayload.Q("updatedProbe()")))throw new Exception("Update probe label failed");
        foreach(var id in seed.Ids)if(!changed.Contains(id))throw new Exception("Update probe changed an ID");
        File.WriteAllText(Path.Combine(directory,"update-seed.json"),seed.Json);
        File.WriteAllText(Path.Combine(directory,"update-changed.json"),changed);
        var profile=new PumlProfile();
        foreach(string type in new[]{"Interaction","Frame","Lifeline","ExecutionSpecification","Message","CombinedFragment","InteractionOperand","InteractionUse","InteractionNote","MessageEnd","Destruction"})profile.Types[type]="fake-"+type;
        foreach(string key in new[]{"Frame","Lifelines","ExecutionSpecifications","Messages","OwnedExecutionSpecification","SendMessage","ReceiveMessage","Fragments","Operands","CrossingFragmentCoveredLifeline","OperandTargetMessage","NestedInteractionFragment","InteractionUses","Notes","MessageEnds","Destructions","DestructionTargetLifeline"})profile.Relations[key]=key;
        foreach(string op in new[]{"alt","opt","loop","par","break","critical","group"})profile.Operators[op]=op.ToUpperInvariant();
        foreach(var file in Directory.GetFiles(samples,"*.puml"))
        {
            var plan=PumlPlan.Parse(File.ReadAllText(file));
            if(Path.GetFileName(file).StartsWith("06-") && (plan.StyleDirectives!=3 || !plan.Summary().Contains("既定表示")))throw new Exception("Style compatibility warning missing");
            var payload=PumlBuild.Build(plan,profile,"fake-view","13.0");
            if(Path.GetFileName(file).StartsWith("05-"))
            {
                var identity=new SequenceIdentity{Root="existing-root",Frame="existing-frame",FrameRelation="existing-frame-relation",Editor="existing-editor",FrameShape="existing-frame-shape"};
                var replacement=PumlBuild.Build(plan,profile,"fake-view","13.0",identity);
                File.WriteAllText(Path.Combine(directory,"replacement.json"),replacement.Json);
                var again=PumlBuild.Build(plan,profile,"fake-view","13.0",identity);
                if(replacement.Ids.Intersect(again.Ids).Count()!=2)throw new Exception("Replacement reused child IDs");
            }
            File.WriteAllText(Path.Combine(directory,Path.GetFileNameWithoutExtension(file)+".json"),payload.Json);
        }
        var cases=new[]{
            "activate A", "deactivate A", "destroy B\nA -> B : reuse", "destroy B\ndestroy B", "skinparam unknownOption value", "!include remote.puml",
            "alt test\nA -> B : call", "else test", "end", "participant A",
            "note over C : missing", "ref over A,A : duplicate", "note over A\nunclosed",
            "activate A\nalt x\ndeactivate A\nelse y\nend\ndeactivate A", "A ->x] : unsupported", "[->] : no lifeline", "actor C", "A <- B : reverse", "A -> B : call\n@enduml\nA -> B : extra"
        };
        foreach(string body in cases)
        {
            bool rejected=false;
            try{PumlPlan.Parse("@startuml\nparticipant A\nparticipant B\n"+body+"\n@enduml");}
            catch(InvalidOperationException e){rejected=e.Message.StartsWith("E120:");}
            if(!rejected)throw new Exception("Unsupported syntax accepted: "+body);
        }
        foreach(var arrow in new[]{"->", "->>", "-->", "-->>"})
        {
            var p=PumlPlan.Parse("@startuml\nparticipant A\nparticipant B\nA "+arrow+"] : outside\n@enduml");
            if(p.Aliases.Count!=2 || p.Nodes[0].Right!="]")throw new Exception("Boundary became a participant");
            string expected=arrow.StartsWith("--")?"reply":arrow=="->>"?"async":"sync";
            if(p.Nodes[0].Kind!=expected)throw new Exception("Boundary message kind changed");
            var outgoing=PumlBuild.Build(p,profile,"fake-view","13.0");
            var end=outgoing.Expected.Single(e=>e.Kind=="messageEnd");
            var sent=outgoing.Expected.Single(e=>e.Kind==expected);
            if(sent.ReceivePort!=end.Id || sent.Right!=null || end.X!=180 || end.Y!=sent.EndY)throw new Exception("Outgoing free endpoint geometry or ownership failed");
            var incoming=PumlPlan.Parse("@startuml\n["+arrow+" A : incoming\nactivate A\ndeactivate A\n@enduml");
            if(incoming.Aliases.Count!=1 || incoming.Nodes[0].Left!="[" || incoming.Nodes[0].Kind!=expected)throw new Exception("Incoming boundary parsing failed");
            var built=PumlBuild.Build(incoming,profile,"fake-view","13.0");
            var message=built.Expected.Single(e=>e.Kind==expected);
            var start=built.Expected.Single(e=>e.Kind=="messageEnd");
            if(message.SendPort!=start.Id || start.X!=180 || start.Y!=message.Y)throw new Exception("Incoming free endpoint geometry failed");
            if(message.Left!=null || message.Right==null || message.SendPort==message.ReceivePort)throw new Exception("Incoming endpoint expectations failed");
        }
        var separate=PumlPlan.Parse("@startuml\nactivate Caller\nCaller -> Service : stop\nactivate Service\nService -> Service_Thread : shutdown\ndestroy Service_Thread\nService --> Caller : result\ndeactivate Service\ndeactivate Caller\n@enduml");
        PumlBuild.Build(separate,profile,"fake-view","13.0");
        var legacy=PumlPlan.Parse("@startuml\nA -> B : shutdown\ndestroy B\nactivate B\nA -> A : finish\ndeactivate B\n@enduml");
        if(!legacy.Summary().Contains("2件") || legacy.All().Any(n=>n.Kind=="activate" || n.Kind=="deactivate"))throw new Exception("Legacy destruction activities must be removed with warning");
        PumlBuild.Build(legacy,profile,"fake-view","13.0");
        bool outside=false;
        try{PumlPlan.Parse("participant A\n@startuml\nA -> B : x\n@enduml");}catch(InvalidOperationException){outside=true;}
        if(!outside)throw new Exception("Syntax outside diagram accepted");
        var unicode=PumlPlan.Parse("@startuml\nparticipant \"日本語\" as A\nparticipant B\nA ->> B : 通知\\n次行\n@enduml");
        if(unicode.Names[0]!="日本語" || unicode.Nodes[0].Text!="通知\n次行" || unicode.Nodes[0].Kind!="async")throw new Exception("Unicode or async parsing failed");
    }
}
