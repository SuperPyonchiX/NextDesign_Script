
public static class MappingTests
{
    static void Require(bool ok,string text) { if(!ok)throw new Exception(text); }
    static void Reject(Action action,string text)
    { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,text); }
    public static void Run(string directory)
    {
        string input="@startuml\ntitle Sample\nparticipant A\nparticipant B\nA -> B : first()\nB --> A : result\n@enduml";
        Require(SequenceNameDiff.Analyze(input,input).Count==0,"identical input is not no-op");
        Require(SequenceNameDiff.Analyze(input,input.Replace("title Sample","' commentary\ntitle Sample")).Count==0,"comment causes model edit");
        var changes=SequenceNameDiff.Analyze(input,input.Replace("first()","next()"));
        Require(changes.Count==1 && changes[0].Index==0 && changes[0].Before=="first()" && changes[0].After=="next()","rename plan incorrect");
        var current=new Dictionary<int,string>{{0,"diagram()"},{1,"manual result"}};
        var merged=SequenceNameMerge.Resolve(changes,current);
        Require(merged.Writes.Count==1 && merged.Conflicts==1 && merged.Writes[0].Before=="diagram()" && merged.Writes[0].After=="next()","PlantUML conflict priority");
        Require(current[1]=="manual result" && changes[0].Before=="first()","merge mutated current or baseline");
        current[0]="first()";
        merged=SequenceNameMerge.Resolve(changes,current);
        Require(merged.Conflicts==0 && merged.Writes.Count==1,"ordinary rename became conflict");
        current[0]="next()";
        merged=SequenceNameMerge.Resolve(changes,current);
        Require(merged.Writes.Count==0 && merged.AlreadyMatched==1,"already applied name rewritten");
        Require(SequenceNameMerge.Resolve(SequenceNameDiff.Analyze(input,input),new Dictionary<int,string>()).Writes.Count==0,"unchanged input requires diagram-deleted elements");
        Require(SequenceNameMerge.Resolve(SequenceNameDiff.Analyze(input,input),current).Writes.Count==0,"diagram-only changes reverted");
        string nextInput=input.Replace("first()","next()");
        current[0]="manual next";current[1]="manual result";
        merged=SequenceNameMerge.Resolve(SequenceNameDiff.Analyze(nextInput,nextInput.Replace("result","new result")),current);
        Require(merged.Writes.Count==1 && merged.Writes[0].Index==1 && merged.Conflicts==1,"second update touches preserved manual name");
        Reject(()=>SequenceNameMerge.Resolve(changes,new Dictionary<int,string>()),"missing changed target silently skipped");
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
