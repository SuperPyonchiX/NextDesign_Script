
public static class MappingTests
{
    static void Require(bool ok,string text) { if(!ok)throw new Exception(text); }
    static void Reject(Action action,string text)
    { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,text); }
    public static void Run(string directory)
    {
        // All messages use the same route: position alone cannot identify them.
        var baseline=Enumerable.Range(0,20).Select(i=>"operation"+i).ToArray();
        var route=Enumerable.Repeat("same-route",baseline.Length).ToArray();
        foreach(int blockSize in new[]{5,10})
        {
            var block=Enumerable.Range(0,blockSize).Select(i=>"added"+i).ToArray();
            var withBlock=baseline.Take(5).Concat(block).Concat(baseline.Skip(5)).ToArray();
            var insertMap=SequenceExportMatch.Align(route,baseline,Enumerable.Repeat("same-route",withBlock.Length).ToArray(),withBlock);
            Require(insertMap.SequenceEqual(Enumerable.Range(0,20).Select(i=>i<5?i:i+blockSize)),"contiguous insertion shifts unchanged messages: "+blockSize);
            var withoutBlock=baseline.Take(5).Concat(baseline.Skip(5+blockSize)).ToArray();
            var deleteMap=SequenceExportMatch.Align(route,baseline,Enumerable.Repeat("same-route",withoutBlock.Length).ToArray(),withoutBlock);
            Require(deleteMap.SequenceEqual(Enumerable.Range(0,20).Select(i=>i<5?i:i<5+blockSize?-1:i-blockSize)),"contiguous deletion shifts unchanged messages: "+blockSize);
        }
        var inserted=baseline.Take(5).Concat(new[]{"added1","added2","added3"}).Concat(baseline.Skip(5)).ToArray();
        var afterInsert=SequenceExportMatch.Align(route,baseline,Enumerable.Repeat("same-route",inserted.Length).ToArray(),inserted);
        Require(afterInsert.SequenceEqual(Enumerable.Range(0,20).Select(i=>i<5?i:i+3)),"block insertion misidentifies unchanged suffix");
        var deleted=baseline.Take(5).Concat(baseline.Skip(8)).ToArray();
        var afterDelete=SequenceExportMatch.Align(route,baseline,Enumerable.Repeat("same-route",deleted.Length).ToArray(),deleted);
        Require(afterDelete.SequenceEqual(Enumerable.Range(0,20).Select(i=>i<5?i:i<8?-1:i-3)),"block deletion misidentifies unchanged suffix");
        var renamedWithInsert=inserted.ToArray();renamedWithInsert[15]="renamed";
        Require(SequenceExportMatch.Align(route,baseline,Enumerable.Repeat("same-route",renamedWithInsert.Length).ToArray(),renamedWithInsert).SequenceEqual(afterInsert),"insertion plus rename shifts following identities");
        Require(SequenceExportMatch.Align(new[]{"r","r","r","r"},new[]{"head","void","void","tail"},new[]{"r","r","r","r","r"},new[]{"head","added","void","void","tail"}).SequenceEqual(new[]{0,2,3,4}),"insert before repeated replies shifts unchanged suffix");
        Require(SequenceExportMatch.Align(new[]{"k"},new[]{"old"},new[]{"k"},new[]{"renamed"}).SequenceEqual(new[]{0}),"rename treated as absent");
        Require(SequenceExportMatch.Align(new[]{"k","anchor","k"},new[]{"repeat","middle","repeat"},new[]{"k","anchor","k"},new[]{"renamed","middle","repeat"}).SequenceEqual(new[]{0,1,2}),"rename stolen by later exact match");
        Require(SequenceExportMatch.Align(new[]{"k","k"},new[]{"first","second"},new[]{"k","k","k"},new[]{"extra","first","second"}).SequenceEqual(new[]{1,2}),"diagram insertion shifts existing identities");
        Require(SequenceExportMatch.Align(new[]{"k","k"},new[]{"same","same"},new[]{"k","k"},new[]{"same","same"}).SequenceEqual(new[]{0,1}),"duplicate occurrence order changed");
        Require(SequenceExportMatch.Align(new[]{"left","right"},new[]{"same","same"},new[]{"right"},new[]{"same"}).SequenceEqual(new[]{-1,0}),"different route incorrectly paired");
        Require(SequenceExportMatch.Unmapped(new[]{"a","b","extra","extra"},new[]{"a","b"}).SequenceEqual(new[]{"extra"}),"diagram-only model/shape counted twice or ignored");
        Require(SequenceExportMatch.Unmapped(new[]{"b","a"},new[]{"a","b"}).Length==0,"equal coverage reports extras");
        string crossBranch="@startuml\nparticipant A\nparticipant B\nactivate A\nalt done\nA -> B : finish\ndeactivate A\nelse wait\nA -> B : wait\nend\n@enduml";
        Reject(()=>PumlPlan.Parse(crossBranch),"generation lifecycle restriction lost");
        var mapped=PumlPlan.ParseForMapping(crossBranch);
        Require(mapped.All().Count(n=>n.Kind=="activate" || n.Kind=="deactivate")==2,"mapping discarded cross-branch activities");
        Require(SequenceNameDiff.Targets(crossBranch,crossBranch).Count==2,"cross-branch no-op comparison rejected");
        Require(SequenceNameDiff.Analyze(crossBranch,crossBranch.Replace("finish","finished")).Count==1,"cross-branch text update rejected");
        Reject(()=>SequenceNameDiff.Analyze(crossBranch,crossBranch.Replace("deactivate A\n","")),"mapping silently ignores structural activity change");
        var crossMap=new SequenceMapFile{Project="p",Root="r",Editor="e",Source=crossBranch,Fingerprint="f",MessageIds=new[]{"m1","m2"}};
        Require(SequenceMapFile.Parse(crossMap.Serialize()).Source==crossBranch,"cross-branch mapping cannot round-trip");
        Require(PumlPlan.ParseForMapping("@startuml\nA -> B : stop\ndestroy B\nactivate B\ndeactivate B\n@enduml").All().Count(n=>n.Kind=="activate" || n.Kind=="deactivate")==2,"mapping rewrites exported destruction activities");
        Reject(()=>PumlPlan.ParseForMapping(crossBranch.Replace("end\n@enduml","@enduml")),"mapping accepts unclosed fragment");
        Reject(()=>PumlPlan.ParseForMapping(crossBranch.Replace("A -> B : finish","!include unknown.puml")),"mapping accepts unsupported syntax");
        var repeatedShapes=new[]{
            new{Id="c",Model="third",Y=20.0,X=10.0},
            new{Id="b",Model="second",Y=10.0,X=20.0},
            new{Id="a",Model="first",Y=10.0,X=20.0},
            new{Id="z",Model="earliest",Y=10.0,X=5.0}};
        var bound=new List<string>();
        for(int occurrence=0;occurrence<repeatedShapes.Length;occurrence++)
        {
            var selected=SequenceExportMatch.Order(repeatedShapes.Where(s=>!bound.Contains(s.Model)),s=>s.Y,s=>s.X,s=>s.Id).First();
            bound.Add(selected.Model);
        }
        Require(bound.SequenceEqual(new[]{"earliest","first","second","third"}),"duplicate occurrences must bind once each in export order");
        // Export emits shape.Text, which may contain a signature absent from model.Name.
        Require(SequenceExportMatch.Message("async","start(void) : int","a","b","async","start(void) : int","a","b"),"exported signature cannot bind");
        Require(!SequenceExportMatch.Message("async","start","a","b","async","start(void) : int","a","b"),"raw model name accepted instead of exported label");
        Require(SequenceExportMatch.Message("reply","  result\r\n value ","a","b","reply","result value","a","b"),"export whitespace mismatch");
        Require(SequenceExportMatch.Message("create","new()","a","b","sync","new()","a","b"),"export create arrow projection mismatch");
        Require(SequenceExportMatch.Message("async","signal",null,"b","async","signal",null,"b"),"export incoming endpoint mismatch");
        Require(!SequenceExportMatch.Message("sync","call","a","b","async","call","a","b"),"kind mismatch ignored");
        Require(!SequenceExportMatch.Message("sync","call","a","b","sync","call","b","a"),"direction mismatch ignored");
        Require(SequenceNameMerge.Resolve(new[]{new SequenceNameEdit{Index=0,Before="start(void) : int",After="start(void) : int"}},new Dictionary<int,string>{{0,"start(void) : int"}}).Writes.Count==0,"exported signature causes unnecessary Name write");
        Require(SequenceParticipantMatch.Equivalent("Service:: Worker","Service::\nWorker"),"linebreak label mismatch");
        Require(SequenceParticipantMatch.Equivalent("Service : Worker","Service:Worker"),"colon spacing mismatch");
        Require(SequenceParticipantMatch.Equivalent(" Service::\\nWorker ","Service:: Worker"),"escaped linebreak mismatch");
        Require(!SequenceParticipantMatch.Equivalent("Service:Worker","Service::Worker"),"colon count collapsed");
        Require(!SequenceParticipantMatch.Equivalent("First Worker","FirstWorker"),"distinct words collapsed");
        Require(!SequenceParticipantMatch.Equivalent("ServiceA::Worker","ServiceB::Worker"),"class suffix caused false match");
        Require(!SequenceParticipantMatch.Equivalent(null,"Worker"),"null matches participant");
        string input="@startuml\ntitle Sample\nparticipant A\nparticipant B\nA -> B : first()\nB --> A : result\n@enduml";
        Require(SequenceNameDiff.Analyze(input,input).Count==0,"identical input is not no-op");
        Require(SequenceNameDiff.Analyze(input,input.Replace("title Sample","' commentary\ntitle Sample")).Count==0,"comment causes model edit");
        var changes=SequenceNameDiff.Analyze(input,input.Replace("first()","next()"));
        Require(changes.Count==1 && changes[0].Index==0 && changes[0].Before=="first()" && changes[0].After=="next()","rename plan incorrect");
        var current=new Dictionary<int,string>{{0,"diagram()"},{1,"manual result"}};
        var desired=SequenceNameDiff.Targets(input,input.Replace("first()","next()"));
        var merged=SequenceNameMerge.Resolve(desired,current);
        Require(merged.Writes.Count==2 && merged.Conflicts==2 && merged.Writes[0].Before=="diagram()" && merged.Writes[0].After=="next()" && merged.Writes[1].After=="result","all diagram differences must converge to PlantUML");
        Require(current[1]=="manual result" && changes[0].Before=="first()","merge mutated current or baseline");
        current[0]="first()";current[1]="result";
        merged=SequenceNameMerge.Resolve(desired,current);
        Require(merged.Conflicts==0 && merged.Writes.Count==1,"ordinary rename became conflict");
        current[0]="next()";
        merged=SequenceNameMerge.Resolve(desired,current);
        Require(merged.Writes.Count==0 && merged.AlreadyMatched==2,"already applied names rewritten");
        current[0]="manual edit";
        merged=SequenceNameMerge.Resolve(SequenceNameDiff.Targets(input,input),current);
        Require(merged.Writes.Count==1 && merged.Writes[0].After=="first()","unchanged PlantUML fails to restore diagram-only edit");
        string nextInput=input.Replace("first()","next()");
        current[0]="manual next";current[1]="manual result";
        merged=SequenceNameMerge.Resolve(SequenceNameDiff.Targets(nextInput,nextInput.Replace("result","new result")),current);
        Require(merged.Writes.Count==2 && merged.Writes[0].After=="next()" && merged.Writes[1].After=="new result","second update fails to converge");
        foreach(var write in merged.Writes)current[write.Index]=write.After;
        Require(SequenceNameMerge.Resolve(SequenceNameDiff.Targets(nextInput,nextInput.Replace("result","new result")),current).Writes.Count==0,"repeat update is not idempotent");
        Reject(()=>SequenceNameMerge.Resolve(desired,new Dictionary<int,string>()),"missing target silently skipped");
        Reject(()=>SequenceNameDiff.Analyze(input,input.Replace("A -> B","B -> A")),"endpoint change accepted");
        Reject(()=>SequenceNameDiff.Analyze(input,input.Replace("A -> B","A ->> B")),"sort change accepted");
        Reject(()=>SequenceNameDiff.Analyze(input,input.Replace("@enduml","A -> B : extra\n@enduml")),"addition accepted");
        Reject(()=>SequenceNameDiff.Analyze(input,input.Replace("B --> A : result\n","")),"deletion accepted");
        Reject(()=>SequenceNameDiff.Analyze(input,input.Replace("title Sample","title Other")),"title silently ignored");
        string complex="@startuml\nparticipant A\nparticipant B\nalt ready\nA -> B : first()\nelse wait\nnote over A : memo\nB -> A : second()\nend\n@enduml";
        Require(SequenceNameDiff.Analyze(complex,complex.Replace("second()","updated()")).Count==1,"nested rename rejected");
        Reject(()=>SequenceNameDiff.Analyze(complex,complex.Replace("memo","different")),"note change ignored");
        Reject(()=>SequenceNameDiff.Analyze(complex,complex.Replace("alt ready","alt other")),"guard change ignored");
        string repeated="@startuml\nA -> B : one\nA -> B : two\n@enduml";
        Reject(()=>SequenceNameDiff.Analyze(repeated,repeated.Replace("one","TEMP").Replace("two","one").Replace("TEMP","two")),"reorder accepted as rename");
        string duplicates=repeated.Replace("two","one");
        Require(SequenceNameDiff.Analyze(duplicates,duplicates).Count==0,"identical duplicates rejected");
        var map=new SequenceMapFile{Project="project",Root="root",Editor="editor",Source=input,Fingerprint="snapshot",MessageIds=new[]{"m1","m2"}};
        string xml=map.Serialize();var copy=SequenceMapFile.Parse(xml);
        Require(copy.Source==input && copy.MessageIds.SequenceEqual(map.MessageIds) && copy.Fingerprint=="snapshot","map round trip");
        Reject(()=>SequenceMapFile.Parse(xml.Replace("first()","tampered()")),"corrupt map accepted");
        map.MessageIds=new[]{"m1","m1"};Reject(()=>map.Serialize(),"duplicate model IDs accepted");map.MessageIds=new[]{"m1","m2"};
        string file=Path.Combine(directory,"mapping.ndmap.xml");
        SequenceMapFile.WriteNew(file,map);
        string original=File.ReadAllText(file);
        bool collision=false;try{SequenceMapFile.WriteNew(file,map);}catch(IOException){collision=true;}
        Require(collision && File.ReadAllText(file)==original,"map overwritten on collision");
        Require(!Directory.GetFiles(directory,"*.writing-*").Any(),"partial map left behind");
        map.Source=input.Replace("first()","next()");map.Fingerprint="nextsnapshot";
        SequenceMapFile.WriteNew(file+".pending",map);
        File.Replace(file+".pending",file,file+".bak");
        Require(SequenceMapFile.Read(file).Source==map.Source && SequenceMapFile.Read(file+".bak").Source==input,"backup promotion failed");
        map.Source=input.Replace("first()","third()");map.Fingerprint="thirdsnapshot";
        SequenceMapFile.WriteNew(file+".pending",map);
        File.Replace(file+".pending",file,file+".bak");
        Require(SequenceMapFile.Read(file).Source==map.Source && SequenceMapFile.Read(file+".bak").Source.Contains("next()"),"second backup promotion failed");
        bool dtd=false;try{SequenceMapFile.Parse("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///not-read'>]><SequenceMap>&x;</SequenceMap>");}catch(System.Xml.XmlException){dtd=true;}
        Require(dtd,"DTD was accepted");
        Console.WriteLine("PASS: mapping round-trip, integrity, no-op, rename, unsupported edits, duplicates, atomic save and backup");
    }
}
