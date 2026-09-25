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
        foreach(string key in new[]{"Frame","Lifelines","ExecutionSpecifications","Messages","OwnedExecutionSpecification","SendMessage","ReceiveMessage","Fragments","Operands","CrossingFragmentCoveredLifeline","OperandTargetMessage","NestedInteractionFragment","InteractionUses","Notes","MessageEnds","Destructions","DestructionTargetLifeline","ReplyMessage","DestroyMessage"})profile.Relations[key]=key;
        foreach(string op in new[]{"alt","opt","loop","par","break","critical","group"})profile.Operators[op]=op.ToUpperInvariant();
        // A large diagram (well over the old 500-line limit) imports and reads back in reasonable time.
        {
            var big=new StringBuilder("@startuml\nparticipant A\nparticipant B\nparticipant C\nactivate A\n");
            for(int i=0;i<200;i++)
            {
                big.Append("A -> B : call"+i+"()\nactivate B\n");
                if(i%10==0)big.Append("opt case"+i+"\nB -> C : ask"+i+"()\nactivate C\nC --> B : told"+i+"()\ndeactivate C\nend\n");
                big.Append("B --> A : done"+i+"()\ndeactivate B\n");
            }
            big.Append("deactivate A\n@enduml\n");
            var clock=System.Diagnostics.Stopwatch.StartNew();
            var bigPayload=PumlBuild.Build(PumlPlan.Parse(big.ToString()),profile,"fake-view","13.0");
            var bigDoc=SequenceDocument.Parse(big.ToString());
            if(SyncPlan.Build(bigDoc,SequenceDocument.Parse(big.ToString()),()=>Guid.NewGuid().ToString()).Changes.Count!=0)throw new Exception("a large diagram does not read as itself");
            if(clock.Elapsed.TotalSeconds>20)throw new Exception("a large diagram took "+clock.Elapsed.TotalSeconds+"s");
            Console.WriteLine("Large diagram: "+bigDoc.Elements.Count+" elements in "+clock.Elapsed.TotalSeconds.ToString("0.0")+"s");
        }
        // A ref whose target the runtime resolved links to it, so double-clicking it opens it.
        {
            var linked=new PumlProfile();
            foreach(var pair in profile.Types)linked.Types[pair.Key]=pair.Value;
            foreach(var pair in profile.Relations)linked.Relations[pair.Key]=pair.Value;
            linked.Relations["RefersTo"]="RefersTo";
            var refPlan=PumlPlan.Parse("@startuml\nparticipant A\nparticipant B\nref over A, B : Handshake\nref over A, B : Unknown\n@enduml");
            linked.References[4]="existing-interaction";
            var built=SequenceJson.Parse(PumlBuild.Build(refPlan,linked,"fake-view","13.0").Json);
            var links=built["Relations"].Items.Where(r=>r["MetamodelId"].StringValue()=="RefersTo").ToArray();
            if(links.Length!=1 || links[0]["TargetId"].StringValue()!="existing-interaction")throw new Exception("a resolved ref was not linked to its target");
        }
        // gap-empty stands for a diagram with nothing on it, which only the sync can start from.
        foreach(var file in Directory.GetFiles(samples,"*.puml").Where(f=>!Path.GetFileName(f).StartsWith("gap-empty")))
        {
            var plan=PumlPlan.Parse(File.ReadAllText(file));
            // The import reads it the way the sync does too, so a caller that sends while it waits stops there.
            SequenceDocument.Parse(File.ReadAllText(file));
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
            // Next Design fails laying out a bar no message uses.
            foreach(var bar in payload.Expected.Where(e=>e.Kind=="execution"))
                if(!payload.Expected.Any(e=>e.SendPort==bar.Id || e.ReceivePort==bar.Id))
                    throw new Exception(Path.GetFileName(file)+": a bar holds no message");
            // Next Design refuses every edit to a diagram whose bar goes on after a reply leaves it.
            foreach(var reply in payload.Expected.Where(e=>e.Kind=="reply" && e.SendPort!=null))
            {
                var later=payload.Expected.Where(e=>(e.Kind=="sync" || e.Kind=="async" || e.Kind=="reply") && e.Y>reply.Y
                    && (e.SendPort==reply.SendPort || e.ReceivePort==reply.SendPort)).Select(e=>e.Text).ToArray();
                if(later.Length>0)throw new Exception(Path.GetFileName(file)+": a bar goes on after the reply "+reply.Text+": "+string.Join(",",later));
            }
            // Each call answered from its own bar: the reply is tied back to the bar it leaves,
            // as a hand-drawn reply is, and no bar is tied to two replies.
            // A destruction points at the message that destroys its lane, as one drawn by hand does:
            // the message right before it, perhaps with an activate of the receiver between.
            if(Path.GetFileName(file)=="09-destroy.puml")
            {
                var built=SequenceJson.Parse(payload.Json)["Relations"].Items;
                if(built.Count(r=>r["MetamodelId"].StringValue()=="DestroyMessage")!=2)
                    throw new Exception("each destruction should point at its destroy message");
            }
            if(Path.GetFileName(file)=="structure-wrap-before.puml")
            {
                var built=SequenceJson.Parse(payload.Json)["Relations"].Items;
                var replies=built.Where(r=>r["MetamodelId"].StringValue()=="ReplyMessage").ToArray();
                if(replies.Length!=3 || replies.Select(r=>r["SourceId"].StringValue()).Distinct().Count()!=3
                    || replies.Any(r=>!built.Any(x=>x["MetamodelId"].StringValue()=="SendMessage"
                        && x["SourceId"].StringValue()==r["SourceId"].StringValue() && x["TargetId"].StringValue()==r["TargetId"].StringValue())))
                    throw new Exception("replies are not tied to the bars they leave");
            }
            // B keeps one bar across both calls: firstDone() leaves it midway and must not be
            // tied to it, or the product cuts the bar there and refuses every edit.
            if(Path.GetFileName(file)=="structure-batch-after.puml")
            {
                var built=SequenceJson.Parse(payload.Json);
                var replies=built["Relations"].Items.Where(r=>r["MetamodelId"].StringValue()=="ReplyMessage").ToArray();
                var names=replies.Select(r=>built["Entities"].Items.Single(e=>e["Id"].StringValue()==r["TargetId"].StringValue())["Name"].StringValue()).ToArray();
                // Each reply ends its own bar now, so each is tied to a bar of its own.
                if(!names.SequenceEqual(new[]{"firstDone()","secondDone()"}) || replies.Select(r=>r["SourceId"].StringValue()).Distinct().Count()!=2)
                    throw new Exception("each reply should close and be tied to a bar of its own: "+string.Join(",",names));
            }
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
