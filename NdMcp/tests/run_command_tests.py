"""Compile the real command adapter against a fake ND command lifecycle.

The fake resets editor access at every command boundary, so setting it only
at server startup cannot pass. This does not replace a real Next Design test.
"""
from pathlib import Path
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
source = (root / "src/server.cs").read_text(encoding="utf-8")

def between(start, end):
    return source[source.index(start):source.index(end, source.index(start))]

handler = between("public void ExecuteNdMcpRequest(", "public class NdMcpCommandRequest")
request = between("public class NdMcpCommandRequest", "public void StopNdMcpServer")
dispatch = between("private static object OnUiThread(", "private static int ParseInt(")
error = between("public class NdMcpHttpError", "public static class NdMcpServer")
program = """
using System; using System.Collections.Generic; using System.Threading;
public enum EditorAccessMode { Default, GetInactiveValue }
public class Options { public EditorAccessMode EditorAccessMode; }
public class ICommandContext { public IApplication App; public Options ContextOption = new Options(); }
public class ICommandParams {
    private List<object> values = new List<object>();
    public object this[int i] { get { return values[i]; } }
    public void AddParam(object value) { values.Add(value); }
}
public class IApplication {
    public bool OmitHandler;
    public ICommandContext Active;
    public ICommandParams CreateCommandParams() { return new ICommandParams(); }
    public void ExecuteCommand(string id, ICommandParams parameters) {
        if (id != "NdMcp.Command.ExecuteRequest") throw new Exception("wrong command");
        if (OmitHandler) return;
        Active = new ICommandContext { App = this };
        try { new Handlers().ExecuteNdMcpRequest(Active, parameters); }
        finally { Active = null; }
    }
}
""" + "public class Handlers {\n" + handler + "\n}\n" + request + error + """
public static class Adapter {
    public static IApplication App;
    public static SynchronizationContext SyncContext;
""" + dispatch.replace("private static object", "public static object", 1) + """
}
public static class Program {
    private static int checks;
    private static void Check(bool value) { if (!value) throw new Exception("assertion failed"); checks++; }
    private static object ReadInactiveDiagram(IApplication app) {
        Check(app.Active != null);
        Check(app.Active.ContextOption.EditorAccessMode == EditorAccessMode.GetInactiveValue);
        return "diagram-with-nodes";
    }
    public static void Main() {
        var app = new IApplication(); Adapter.App = app;
        Adapter.SyncContext = new SynchronizationContext();
        // Each request must get a new command context with the option set.
        Check((string)Adapter.OnUiThread(ReadInactiveDiagram) == "diagram-with-nodes");
        Check(app.Active == null);
        Check((string)Adapter.OnUiThread(ReadInactiveDiagram) == "diagram-with-nodes");
        Check(app.Active == null);
        try { Adapter.OnUiThread(a => { throw new NdMcpHttpError(404, "missing model"); }); throw new Exception("error swallowed"); }
        catch (NdMcpHttpError e) { Check(e.Status == 404 && e.Message == "missing model"); }
        Check(app.Active == null);
        app.OmitHandler = true;
        try { Adapter.OnUiThread(ReadInactiveDiagram); throw new Exception("missing handler accepted"); }
        catch (NdMcpHttpError e) { Check(e.Status == 500); }
        Adapter.SyncContext = null;
        try { Adapter.OnUiThread(ReadInactiveDiagram); throw new Exception("missing context accepted"); }
        catch (NdMcpHttpError e) { Check(e.Status == 503); }
        Console.WriteLine("PASS: " + checks + " command lifecycle assertions (fake SDK)");
    }
}
"""
compiler = Path(os.environ["WINDIR"]) / "Microsoft.NET/Framework64/v4.0.30319/csc.exe"
with tempfile.TemporaryDirectory(prefix="ndmcp-command-tests-") as tmp:
    directory = Path(tmp)
    cs = directory / "Tests.cs"
    exe = directory / "Tests.exe"
    cs.write_text(program, encoding="utf-8-sig")
    subprocess.run([str(compiler), "/nologo", "/warnaserror+", "/out:" + str(exe), str(cs)], check=True)
    subprocess.run([str(exe)], check=True)
