public static class EditorTests
{
    static void Require(bool ok,string message) { if(!ok)throw new Exception(message); }
    static void Reject(Action action)
    { bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected,"invalid snapshot accepted"); }
    public static void Run()
    {
        var bars=new[]{"sender","receiver","shared","empty","already-empty"};
        Require(SequenceActivationCleanup.Unused(bars,new[]{"sender","receiver","shared","shared",null}).SequenceEqual(new[]{"already-empty","empty"}),"connected incoming/outgoing/shared bars deleted");
        Require(SequenceActivationCleanup.Unused(bars,new[]{"shared"}).SequenceEqual(new[]{"already-empty","empty","receiver","sender"}),"last message deletion does not clean bars");
        Require(SequenceActivationCleanup.Unused(new[]{"shared"},new[]{"shared"}).Length==0,"cleanup repeat is not no-op");
        var messages=Enumerable.Range(0,20).Select(i=>PumlBuild.Obj("Id","shape"+i,"ModelId","message"+i,"SourceY",i*70.5,"TargetY",i*70.5,
            "Style",PumlBuild.Obj("Theme",false,"Unknown",new object[]{"custom",null,5}),"FutureProperty",PumlBuild.Obj("nested",true))).ToArray();
        var editor=PumlBuild.Obj("Id","editor","ModelId","root","ViewType","SequenceDiagram",
            "UnknownSettings",PumlBuild.Obj("zoom",1.25,"font","custom","nested",new[]{1,2,3}),
            "Frame",PumlBuild.Obj("Id","frame-shape","ModelId","frame"),"Messages",messages,
            "Notes",new[]{PumlBuild.Obj("Id","note-shape","ModelId","note","X",12.5,"Style",PumlBuild.Obj("Color","blue"))});
        var unit=PumlBuild.Obj("Type","Model","SchemaVersion","11.1","Editors",new[]{editor});
        var before=SequenceEditorDocument.Read(PumlBuild.Json(unit),"root","editor");
        var withBars=SequenceEditorDocument.Read(before.ImportJson(),"root","editor");
        withBars.Editor.Properties["ExecutionSpecifications"]=SequenceJson.Parse(PumlBuild.Json(new[]{
            PumlBuild.Obj("Id","empty-shape","ModelId","empty","X",1,"Length",40),
            PumlBuild.Obj("Id","shared-shape","ModelId","shared","X",2,"Length",50)}));
        var cleanBars=withBars.Without(new[]{"empty","message0"});
        Require(cleanBars.Editor["ExecutionSpecifications"].Items.Count==1 && SequenceEditorDocument.Value(cleanBars.Editor["ExecutionSpecifications"].Items[0],"ModelId")=="shared","empty bar not removed or shared bar removed");
        Require(cleanBars.Messages().Length==19,"combined cleanup missed message");
        Require(cleanBars.Editor["ExecutionSpecifications"].Items[0].ToJsonString()==withBars.Editor["ExecutionSpecifications"].Items[1].ToJsonString(),"retained bar attributes changed");
        Require(withBars.Editor["ExecutionSpecifications"].Items.Count==2,"cleanup mutated baseline");
        string original=before.Editor.ToJsonString();
        var altered=SequenceEditorDocument.Read(before.ImportJson(),"root","editor");
        altered.Messages()[0].Properties["SourceY"]=SequenceJson.Parse("0.000001");
        Require(altered.Fingerprint()!=before.Fingerprint(),"small persisted coordinate change hidden by preservation check");
        Require(before.Messages()[0]["SourceY"].Raw=="0","geometry check mutates original snapshot");

        foreach(int count in new[]{1,5,10,20}) {
            var ids=Enumerable.Range(0,count).Select(i=>"message"+i).ToArray();
            var after=before.Without(ids);
            Require(before.Editor.ToJsonString()==original,"snapshot mutated");
            Require(after.Messages().Length==20-count,"wrong deletion count");
            Require(after.Editor["UnknownSettings"].ToJsonString()==before.Editor["UnknownSettings"].ToJsonString(),"unknown editor settings lost");
            Require(after.Editor["Notes"].ToJsonString()==before.Editor["Notes"].ToJsonString(),"unrelated shapes changed");
            for(int i=count;i<20;i++)Require(after.Messages()[i-count].ToJsonString()==before.Messages()[i].ToJsonString(),"retained message changed");
            var patch=SequenceJson.Parse(after.ImportJson());
            Require(patch["Entities"].Items.Count==0 && patch["Relations"].Items.Count==0,"editor patch recreates models/relations");
            Require(patch["Editors"].Items.Count==1 && patch["TopElementId"].StringValue()=="root","wrong target");
            Require(SequenceEditorDocument.Read(after.ImportJson(),"root","editor").Fingerprint()==after.Fingerprint(),"patch readback differs");
            Require(after.Without(ids).Fingerprint()==after.Fingerprint(),"deletion not idempotent");
        }
        Require(before.Without(new[]{"absent-model"}).Fingerprint()==before.Fingerprint(),"unshaped model deletes unrelated shapes");
        Reject(()=>SequenceEditorDocument.Read(PumlBuild.Json(unit),"other-root","editor"));
        Reject(()=>SequenceEditorDocument.Read(PumlBuild.Json(unit),"root","other-editor"));
        Reject(()=>SequenceEditorDocument.Read(PumlBuild.Json(PumlBuild.Obj("SchemaVersion","11.1","Editors",new[]{editor,editor})),"root","editor"));
        var empty=before.Without(Enumerable.Range(0,20).Select(i=>"message"+i));
        var omitted=SequenceEditorDocument.Read(empty.ImportJson(),"root","editor");omitted.Editor.Properties.Remove("Messages");
        Require(SequenceEditorDocument.Read(omitted.ImportJson(),"root","editor").Fingerprint()==empty.Fingerprint(),"omitted empty message collection is not equivalent");
        Reject(()=>SequenceEditorDocument.Read(empty.ImportJson().Replace("\"Messages\":[]","\"Messages\":null"),"root","editor"));
        Reject(()=>SequenceEditorDocument.Read(PumlBuild.Json(unit).Replace("shape1\"","shape0\""),"root","editor"));
        foreach(string bad in new[]{"{\"a\":1,\"a\":2}","[1,]","{\"a\":}","true false","01","{\"a\":1,}","\"bad\\q\"","\"bad\\u01xx\""})Reject(()=>SequenceJson.Parse(bad));
        string escaped="{\"\\u0061\":\"日本語\\n\\r\\t\\b\\f\\/\\\\\\\"\\uD83D\\uDE00\",\"number\":1.25e+2,\"nil\":null}";
        var parsed=SequenceJson.Parse(escaped);
        Require(parsed["a"].StringValue()=="日本語\n\r\t\b\f/\\\"\ud83d\ude00","JSON escapes decoded incorrectly");
        Require(SequenceJson.Parse(parsed.ToJsonString())["number"].Raw=="1.25e+2","numeric spelling changed");
        Console.WriteLine("PASS: editor-only deletion, unknown styles/attributes, retained shape identity/geometry, idempotence and strict JSON parsing");
    }
}
