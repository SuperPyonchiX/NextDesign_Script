public static class PumlTests
{
    public static void Run(string directory,string samples)
    {
        var profile=new PumlProfile();
        foreach(string type in new[]{"Interaction","Frame","Lifeline","ExecutionSpecification","Message","CombinedFragment","InteractionOperand","InteractionUse","InteractionNote"})profile.Types[type]="fake-"+type;
        foreach(string key in new[]{"Frame","Lifelines","ExecutionSpecifications","Messages","OwnedExecutionSpecification","SendMessage","ReceiveMessage","Fragments","Operands","CrossingFragmentCoveredLifeline","OperandTargetMessage","NestedInteractionFragment","InteractionUses","Notes"})profile.Relations[key]=key;
        foreach(string op in new[]{"alt","opt","loop","par","break","critical","group"})profile.Operators[op]=op.ToUpperInvariant();
        foreach(var file in Directory.GetFiles(samples,"*.puml"))
        {
            var plan=PumlPlan.Parse(File.ReadAllText(file));
            if(Path.GetFileName(file).StartsWith("06-") && (plan.StyleDirectives!=3 || !plan.Summary().Contains("既定表示")))throw new Exception("Style compatibility warning missing");
            var payload=PumlBuild.Build(plan,profile,"fake-view","13.0");
            File.WriteAllText(Path.Combine(directory,Path.GetFileNameWithoutExtension(file)+".json"),payload.Json);
        }
        var cases=new[]{
            "activate A", "deactivate A", "skinparam unknownOption value", "!include remote.puml",
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
            var incoming=PumlPlan.Parse("@startuml\n["+arrow+" A : incoming\nactivate A\ndeactivate A\n@enduml");
            if(incoming.Aliases.Count!=1 || incoming.Nodes[0].Left!="[" || incoming.Nodes[0].Kind!=expected)throw new Exception("Incoming boundary parsing failed");
            var built=PumlBuild.Build(incoming,profile,"fake-view","13.0");
            var message=built.Expected.Single(e=>e.Kind==expected);
            if(message.Left!=null || message.Right==null || message.SendPort==message.ReceivePort)throw new Exception("Incoming endpoint expectations failed");
        }
        bool outside=false;
        try{PumlPlan.Parse("participant A\n@startuml\nA -> B : x\n@enduml");}catch(InvalidOperationException){outside=true;}
        if(!outside)throw new Exception("Syntax outside diagram accepted");
        var unicode=PumlPlan.Parse("@startuml\nparticipant \"日本語\" as A\nparticipant B\nA ->> B : 通知\\n次行\n@enduml");
        if(unicode.Names[0]!="日本語" || unicode.Nodes[0].Text!="通知\n次行" || unicode.Nodes[0].Kind!="async")throw new Exception("Unicode or async parsing failed");
    }
}
