"""Compile production message/activation handlers from every exporter with fake shapes.

Run with Python on Windows; uses the .NET Framework compiler, no Next Design.
"""
from pathlib import Path
import os
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
HARNESS = r'''
using System;
using System.Linq;
using System.Collections.Generic;
class ILifelineShape { public string Id; }
class IExecutionSpecificationShape { public ILifelineShape Lifeline; }
class IDestructionShape { public ILifelineShape Lifeline; }
class IMessage { public string Kind; }
class IMessageShape { public ILifelineShape Sender, Receiver; public object Model; public string Text = "message"; }
class Options { public bool UseCreateParticipant = true; }
static class PlantUmlText { public static string Inline(string s) { return s; } public static string Normalize(string s) { return s; } }
class Test {
    readonly HashSet<string> _destroyed = new HashSet<string>();
    readonly HashSet<string> _declared = new HashSet<string>();
    readonly Dictionary<string,int> _activeCount = new Dictionary<string,int>();
    readonly List<string> lines = new List<string>();
    readonly Options _o = new Options();
    string AliasOf(ILifelineShape l) { return l.Id; }
    string DeclarationOf(ILifelineShape l) { return "participant " + l.Id; }
    void EnsureDeclared(ILifelineShape l) { if(l != null) _declared.Add(l.Id); }
    void Line(string s) { lines.Add(s); }
    // PRODUCTION
    static void Check(bool value, string label) { if(!value) throw new Exception(label); }
    public static void Main() {
        var a = new ILifelineShape { Id="A" }; var b = new ILifelineShape { Id="B" };
        var execution = new IExecutionSpecificationShape { Lifeline=b };
        foreach(bool existing in new[]{false,true}) foreach(bool explicitShape in new[]{false,true}) {
            var t = new Test();
            if(existing) { t.OnActivate(execution); t.OnActivate(execution); }
            if(explicitShape) t.OnDestruction(new IDestructionShape { Lifeline=b });
            else t.OnMessage(new IMessageShape { Sender=a, Receiver=b, Model=new IMessage { Kind="destroy" } });
            // Scheduled activation (including deferred activation) after destruction.
            t.OnActivate(execution);
            Check(!t.OnDeactivate(execution), "destroyed execution must not deactivate");
            t.OnDestruction(new IDestructionShape { Lifeline=b });
            t.DeactivateAll();
            int destroy = t.lines.IndexOf("destroy B");
            Check(destroy >= 0, "destruction retained");
            Check(t.lines.Count(s=>s=="destroy B")==1, "destruction deduplicated");
            Check(!t.lines.Skip(destroy+1).Any(s=>s=="activate B" || s=="deactivate B"), "no activation after destruction");
            Check(!t._activeCount.ContainsKey("B"), "nested activations cleared");
            // Destruction does not suppress another participant's ordinary nested bars.
            var other = new IExecutionSpecificationShape { Lifeline=a };
            t.OnActivate(other); t.OnActivate(other);
            Check(t.OnDeactivate(other), "normal inner deactivation");
            t.DeactivateAll();
            Check(t.lines.Count(s=>s=="activate A")==2 && t.lines.Count(s=>s=="deactivate A")==2, "normal nesting retained");
        }
        var normal = new Test();
        normal.OnMessage(new IMessageShape { Sender=a, Receiver=b, Model=new IMessage { Kind="async" } });
        normal.OnActivate(execution); normal.OnDeactivate(execution);
        Check(normal.lines.SequenceEqual(new[]{"A ->> B : message", "activate B", "deactivate B"}), "ordinary message unchanged");
        Console.WriteLine("PASS: destruction message/shape, empty/nested activations, duplicate destruction, delayed activation, ordinary messages");
    }
}
'''

compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
handlers = []
with tempfile.TemporaryDirectory(prefix='sequence-export-') as tmp:
    for extension in ['AgentReview', 'PlantUmlTool', 'NdMcp']:
        source = (ROOT / extension / 'main.cs').read_text(encoding='utf-8-sig')
        start = source.index('    private void OnMessage(IMessageShape m)')
        end = source.index('    // ---------- 相互作用の利用・ノート ----------', start)
        production = source[start:end]
        handlers.append(production)
        program = Path(tmp) / (extension + '.cs')
        program.write_text(HARNESS.replace('// PRODUCTION', production), encoding='utf-8-sig')
        exe = program.with_suffix('.exe')
        subprocess.run([str(compiler), '/nologo', '/warnaserror+', '/out:' + str(exe), str(program)], check=True)
        print(extension, flush=True)
        subprocess.run([str(exe)], check=True)
assert handlers[0] == handlers[1] == handlers[2], 'Exporter handlers have drifted'
print('PASS: all three exporters use identical message lifecycle handlers')

# Exercise the production Note handler, including the unattached path.
NOTE_HARNESS = r'''
using System;
using System.Linq;
using System.Collections.Generic;
class INoteAnchorShape {}
class ILifelineShape {}
class INoteShape { public string Text; public double LocationX=0, Width=100; public List<INoteAnchorShape> NoteAnchors=new List<INoteAnchorShape>(); }
static class PlantUmlText { public static string Normalize(string s) {return s.Trim();} public static string Inline(string s) {return s;} }
class Test {
    List<string> lines=new List<string>();List<int> _stack=new List<int>();
    void Line(string s) {lines.Add(s);} void LineAt(int depth,string s) {lines.Add(s);}
    ILifelineShape AnchoredLifelineOf(INoteShape n) {return new ILifelineShape();}
    string AliasOf(ILifelineShape l) {return "A";}
    string NearestAlias(double x) {throw new Exception("free Note must not infer an anchor");}
    // PRODUCTION
    public static void Main() {
        var t=new Test();t.OnNote(new INoteShape{Text="line1\r\nline2"});
        if(!t.lines.SequenceEqual(new[]{"note across","line1","line2","end note"}))throw new Exception("free Note output");
        t=new Test();var linked=new INoteShape{Text="linked"};linked.NoteAnchors.Add(new INoteAnchorShape());t.OnNote(linked);
        if(!t.lines.SequenceEqual(new[]{"note over A","linked","end note"}))throw new Exception("linked Note changed");
        Console.WriteLine("PASS: production free and anchored Note output");
    }
}
'''
source=(ROOT/'PlantUmlTool/main.cs').read_text(encoding='utf-8-sig')
start=source.index('    private void OnNote(INoteShape n)')
end=source.index('    private ILifelineShape AnchoredLifelineOf',start)
with tempfile.TemporaryDirectory(prefix='sequence-note-') as tmp:
    program=Path(tmp)/'Note.cs'
    program.write_text(NOTE_HARNESS.replace('// PRODUCTION',source[start:end]),encoding='utf-8-sig')
    exe=program.with_suffix('.exe')
    subprocess.run([str(compiler),'/nologo','/warnaserror+','/out:'+str(exe),str(program)],check=True)
    subprocess.run([str(exe)],check=True)
