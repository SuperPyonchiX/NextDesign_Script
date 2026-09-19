// ============================================================
//  Part S / MCP ブリッジ用 HTTP サーバー（NdMcp 固有部。src/server.cs）
// ============================================================

// ------------------------------------------------------------
//  コマンドハンドラ（UI スレッドで同期実行される）
// ------------------------------------------------------------

public void StartNdMcpServer(ICommandContext context, ICommandParams parameters)
{
    var category = "NdMcp";
    var app = context.App;
    try
    {
        if (NdMcpServer.IsRunning)
        {
            app.Output.WriteLine(category, "[info] サーバーは既に稼働中です: " + NdMcpServer.BaseUrl());
            return;
        }

        // UI スレッド（＝このハンドラのスレッド）の情報を捕獲する
        NdMcpServer.UiThreadId = Thread.CurrentThread.ManagedThreadId;
        NdMcpServer.SyncContext = SynchronizationContext.Current;
        NdMcpServer.App = app;
        if (NdMcpServer.SyncContext == null)
        {
            app.Output.WriteLine(category, "[error] SynchronizationContext.Current が null のため、UI スレッドへ戻せません。サーバーは開始しません。");
            app.Window.UI.ShowInformationDialog(
                "SynchronizationContext が取得できないため、この環境ではサーバーを開始できません。\n"
                + "（出力ウィンドウの NdMcp カテゴリを添えて報告してください）", category);
            return;
        }

        var config = NdMcpConfig.Load();
        NdMcpServer.Port = config.Port;
        NdMcpServer.ExportDir = config.ExportDir;
        NdMcpServer.Start();

        OutputPane.Show(app, category);
        app.Output.WriteLine(category, "=== NdMcp サーバー開始 ===");
        app.Output.WriteLine(category, "[info] URL      : " + NdMcpServer.BaseUrl());
        app.Output.WriteLine(category, "[info] UI thread: " + NdMcpServer.UiThreadId + " / " + NdMcpServer.SyncContext.GetType().FullName);
        app.Output.WriteLine(category, "[info] 出力先   : " + NdMcpServer.ExportDir);
        app.Output.WriteLine(category, "[info] ログ     : " + NdMcpServer.LogPath());
        app.Output.WriteLine(category, "[info] 確認     : curl " + NdMcpServer.BaseUrl() + "/ping");
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] サーバー開始に失敗: " + ex.ToString());
        app.Window.UI.ShowInformationDialog("サーバー開始に失敗しました。\n\n" + ex.Message, category);
    }
}

// HTTP から UI スレッドへ戻した後、正式なコマンドとして呼び出す。
// エディタ取得設定の有効期間内に、モデル取得からファイル出力まで完了させる。
public void ExecuteNdMcpRequest(ICommandContext context, ICommandParams parameters)
{
    var request = parameters[0] as NdMcpCommandRequest;
    if (request == null) throw new ArgumentException("NdMcp の要求がありません。");
    try
    {
        context.ContextOption.EditorAccessMode = EditorAccessMode.GetInactiveValue;
        request.Result = request.Work(context.App);
    }
    catch (Exception ex) { request.Error = ex; }
    finally { request.Completed = true; }
}

public class NdMcpCommandRequest
{
    public Func<IApplication, object> Work;
    public object Result;
    public Exception Error;
    public bool Completed;
}

public void StopNdMcpServer(ICommandContext context, ICommandParams parameters)
{
    var category = "NdMcp";
    var app = context.App;
    try
    {
        if (!NdMcpServer.IsRunning)
        {
            app.Output.WriteLine(category, "[info] サーバーは稼働していません。");
            return;
        }
        NdMcpServer.Stop();
        app.Output.WriteLine(category, "[info] サーバーを停止しました（累計 " + NdMcpServer.RequestCount + " 件受信）。");
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] 停止に失敗: " + ex.ToString());
    }
}

public void ShowNdMcpStatus(ICommandContext context, ICommandParams parameters)
{
    var category = "NdMcp";
    var app = context.App;
    try
    {
        OutputPane.Show(app, category);
        app.Output.WriteLine(category, "=== NdMcp 状態 ===");
        app.Output.WriteLine(category, "稼働    : " + (NdMcpServer.IsRunning ? "稼働中 " + NdMcpServer.BaseUrl() : "停止"));
        app.Output.WriteLine(category, "受信数  : " + NdMcpServer.RequestCount);
        app.Output.WriteLine(category, "UI thread: " + NdMcpServer.UiThreadId + " / "
            + (NdMcpServer.SyncContext != null ? NdMcpServer.SyncContext.GetType().FullName : "(null)"));
        app.Output.WriteLine(category, "設定    : " + NdMcpConfig.ConfigPath());
        foreach (var line in NdMcpServer.RecentLog())
            app.Output.WriteLine(category, "[log] " + line);
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] 状態確認に失敗: " + ex.ToString());
    }
}

public void OpenNdMcpConfig(ICommandContext context, ICommandParams parameters)
{
    var category = "NdMcp";
    var app = context.App;
    try
    {
        var path = NdMcpConfig.EnsureFile();
        Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = "\"" + path + "\"", UseShellExecute = true });
        app.Output.WriteLine(category, "[info] 設定ファイルを開きました: " + path + "（変更はサーバー再開始で反映）");
    }
    catch (Exception ex)
    {
        app.Output.WriteLine(category, "[error] 設定を開けません: " + ex.ToString());
    }
}

// ------------------------------------------------------------
//  設定（%USERPROFILE%\.nd-mcp\config.ini、key=value 形式）
// ------------------------------------------------------------

public class NdMcpConfig
{
    public int Port = 3560;
    public string ExportDir;

    public static string ConfigDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nd-mcp");
    }

    public static string ConfigPath() { return Path.Combine(ConfigDir(), "config.ini"); }

    public static NdMcpConfig Load()
    {
        var config = new NdMcpConfig();
        config.ExportDir = Path.Combine(ConfigDir(), "export");
        var path = ConfigPath();
        if (!File.Exists(path)) return config;
        foreach (var pair in IniFile.Read(path))
        {
            switch (pair.Key)
            {
                case "port":
                    int port;
                    if (int.TryParse(pair.Value, out port) && port > 0 && port < 65536) config.Port = port;
                    break;
                case "exportDir":
                    if (pair.Value.Length > 0) config.ExportDir = pair.Value;
                    break;
            }
        }
        return config;
    }

    // 設定ファイルが無ければ既定値入りで作る
    public static string EnsureFile()
    {
        Directory.CreateDirectory(ConfigDir());
        var path = ConfigPath();
        if (!File.Exists(path))
        {
            var nl = "\r\n";
            var sb = new StringBuilder();
            sb.Append("# NdMcp 設定（変更後は「サーバー停止」→「サーバー開始」で反映）").Append(nl);
            sb.Append("# 待ち受けポート。Python ブリッジ側の ND_MCP_URL と合わせる").Append(nl);
            sb.Append("port=3560").Append(nl);
            sb.Append("# /export の既定出力先").Append(nl);
            sb.Append("exportDir=" + Path.Combine(ConfigDir(), "export")).Append(nl);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        return path;
    }
}

// key=value 形式の読み込み（# 始まりと空行は無視）
public static class IniFile
{
    public static List<KeyValuePair<string, string>> Read(string path)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            result.Add(new KeyValuePair<string, string>(
                line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
        }
        return result;
    }
}

// ------------------------------------------------------------
//  JSON ライタ（string / bool / 数値 / null / IDictionary / IEnumerable のみ）
// ------------------------------------------------------------

public static class Json
{
    public static string Write(object value)
    {
        var sb = new StringBuilder();
        WriteValue(sb, value);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, object value)
    {
        if (value == null) { sb.Append("null"); return; }
        var s = value as string;
        if (s != null) { WriteString(sb, s); return; }
        if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }
        if (value is int || value is long || value is short || value is byte)
        { sb.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture)); return; }
        if (value is double || value is float || value is decimal)
        { sb.Append(Convert.ToDouble(value).ToString("R", CultureInfo.InvariantCulture)); return; }
        var dict = value as IDictionary;
        if (dict != null)
        {
            sb.Append('{');
            var first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, entry.Key.ToString());
                sb.Append(':');
                WriteValue(sb, entry.Value);
            }
            sb.Append('}');
            return;
        }
        var list = value as IEnumerable;
        if (list != null)
        {
            sb.Append('[');
            var first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
            return;
        }
        WriteString(sb, value.ToString());
    }

    private static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }
}

// 順序を保つ JSON オブジェクト（Dictionary は列挙順が保証されないため）
public class JsonObject : IDictionary
{
    private readonly List<string> _keys = new List<string>();
    private readonly Dictionary<string, object> _map = new Dictionary<string, object>(StringComparer.Ordinal);

    public JsonObject Set(string key, object value)
    {
        if (!_map.ContainsKey(key)) _keys.Add(key);
        _map[key] = value;
        return this;
    }

    public object this[object key]
    {
        get { object v; return _map.TryGetValue((string)key, out v) ? v : null; }
        set { Set((string)key, value); }
    }
    public void Add(object key, object value) { Set((string)key, value); }
    public bool Contains(object key) { return _map.ContainsKey((string)key); }
    public void Remove(object key) { var k = (string)key; if (_map.Remove(k)) _keys.Remove(k); }
    public void Clear() { _keys.Clear(); _map.Clear(); }
    public ICollection Keys { get { return _keys; } }
    public ICollection Values { get { return _keys.Select(k => _map[k]).ToList(); } }
    public bool IsReadOnly { get { return false; } }
    public bool IsFixedSize { get { return false; } }
    public int Count { get { return _keys.Count; } }
    public object SyncRoot { get { return this; } }
    public bool IsSynchronized { get { return false; } }
    public void CopyTo(Array array, int index) { throw new NotSupportedException(); }
    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    public IDictionaryEnumerator GetEnumerator()
    {
        var ordered = new System.Collections.Specialized.OrderedDictionary();
        foreach (var k in _keys) ordered.Add(k, _map[k]);
        return ordered.GetEnumerator();
    }
}

// ------------------------------------------------------------
//  HTTP サーバー
// ------------------------------------------------------------

public class NdMcpHttpError : Exception
{
    public int Status;
    public NdMcpHttpError(int status, string message) : base(message) { Status = status; }
}

public static class NdMcpServer
{
    public const string Version = "0.1.1";

    public static int Port = 3560;
    public static string ExportDir;
    public static int UiThreadId = -1;
    public static SynchronizationContext SyncContext;
    public static IApplication App;
    public static int RequestCount;

    private static HttpListener _listener;
    private static readonly object _lock = new object();
    private static readonly List<string> _log = new List<string>();

    public static bool IsRunning
    {
        get { return _listener != null && _listener.IsListening; }
    }

    public static string BaseUrl() { return "http://127.0.0.1:" + Port; }

    public static void Start()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(BaseUrl() + "/");
        listener.Start();
        _listener = listener;
        Log("start " + BaseUrl());
        // 非同期受付を1件仕掛けて即戻る（UI スレッドをブロックしない）
        listener.BeginGetContext(OnRequest, listener);
    }

    public static void Stop()
    {
        var listener = _listener;
        _listener = null;
        if (listener != null)
        {
            try { listener.Stop(); } catch (Exception) { }
            try { listener.Close(); } catch (Exception) { }
        }
        Log("stop");
    }

    // 受付コールバック（スレッドプールのスレッドで実行される）
    private static void OnRequest(IAsyncResult ar)
    {
        var listener = (HttpListener)ar.AsyncState;
        HttpListenerContext ctx;
        try { ctx = listener.EndGetContext(ar); }
        catch (Exception) { return; }   // Stop() 後の ObjectDisposedException 等。終了する

        // 次のリクエストの受付を先に仕掛ける
        try { if (listener.IsListening) listener.BeginGetContext(OnRequest, listener); }
        catch (Exception e) { Log("re-arm failed: " + e.Message); }

        Interlocked.Increment(ref RequestCount);
        var status = 200;
        object body;
        var sw = Stopwatch.StartNew();
        try
        {
            body = Route(ctx.Request);
        }
        catch (NdMcpHttpError e)
        {
            status = e.Status;
            body = new JsonObject().Set("error", e.Message);
        }
        catch (Exception e)
        {
            status = 500;
            body = new JsonObject().Set("error", e.Message).Set("detail", e.ToString());
        }
        Log(ctx.Request.Url.PathAndQuery + " -> " + status + " (" + sw.ElapsedMilliseconds + "ms)");

        try
        {
            var bytes = Encoding.UTF8.GetBytes(Json.Write(body));
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception e)
        {
            Log("response write failed: " + e.Message);
        }
    }

    private static object Route(HttpListenerRequest request)
    {
        if (request.HttpMethod != "GET") throw new NdMcpHttpError(405, "GET のみ対応しています");
        var path = request.Url.AbsolutePath;
        var q = request.QueryString;

        if (path == "/ping")
        {
            return new JsonObject()
                .Set("ok", true).Set("server", "NdMcp").Set("version", Version)
                .Set("time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Set("requests", RequestCount)
                .Set("uiThreadId", UiThreadId)
                .Set("syncContext", SyncContext != null ? SyncContext.GetType().FullName : null);
        }
        if (path == "/thread")
        {
            var tid = Thread.CurrentThread.ManagedThreadId;
            return new JsonObject()
                .Set("thread", tid).Set("isUiThread", tid == UiThreadId)
                .Set("isThreadPool", Thread.CurrentThread.IsThreadPoolThread)
                .Set("uiThreadId", UiThreadId);
        }
        if (path == "/") return Usage();

        // ND API は必ずコマンド内で呼ぶ。診断用の直呼びも許可しない。
        if (q["direct"] == "1") throw new NdMcpHttpError(400, "direct=1 は廃止しました。通常のコマンド経由で実行してください。");
        var modelPath = q["path"] ?? "";
        var modelId = q["id"] ?? "";

        Func<IApplication, object> work = null;
        switch (path)
        {
            case "/project": work = app => ModelApi.Project(app); break;
            case "/tree": work = app => ModelApi.Tree(app, modelPath, modelId, ParseInt(q["depth"], 2, 0, 20)); break;
            case "/model": work = app => ModelApi.Model(app, modelPath, modelId); break;
            case "/search": work = app => ModelApi.Search(app, q["q"] ?? "", q["metaclass"] ?? "", ParseInt(q["limit"], 50, 1, 1000)); break;
            case "/markdown": work = app => ModelApi.Markdown(app, modelPath, modelId); break;
            case "/export": work = app => ModelApi.Export(app, modelPath, modelId, q["out"] ?? "", ExportDir); break;
            default: throw new NdMcpHttpError(404, "不明なパス: " + path);
        }
        return OnUiThread(work);
    }

    private static object OnUiThread(Func<IApplication, object> work)
    {
        var context = SyncContext;
        if (context == null) throw new NdMcpHttpError(503, "SynchronizationContext が捕獲できていません");
        object result = null;
        Exception error = null;
        context.Send(delegate(object state)
        {
            try
            {
                var request = new NdMcpCommandRequest { Work = work };
                var parameters = App.CreateCommandParams();
                parameters.AddParam(request);
                App.ExecuteCommand("NdMcp.Command.ExecuteRequest", parameters);
                if (!request.Completed)
                    throw new NdMcpHttpError(500, "NdMcp の要求コマンドが完了しませんでした。manifest.json と main.cs を一緒に更新してください。");
                if (request.Error != null) throw request.Error;
                result = request.Result;
            }
            catch (Exception e) { error = e; }
        }, null);
        if (error != null) throw error;
        return result;
    }

    private static int ParseInt(string s, int fallback, int min, int max)
    {
        int v;
        if (string.IsNullOrEmpty(s) || !int.TryParse(s, out v)) return fallback;
        return Math.Max(min, Math.Min(max, v));
    }

    private static object Usage()
    {
        return new JsonObject()
            .Set("server", "NdMcp").Set("version", Version)
            .Set("endpoints", new List<object>
            {
                "GET /ping", "GET /thread", "GET /project",
                "GET /tree?path=&id=&depth=2", "GET /model?path=&id=",
                "GET /search?q=&metaclass=&limit=50", "GET /markdown?path=&id=",
                "GET /export?path=&id=&out=",
            });
    }

    // ---- ログ（バックグラウンドスレッドからは Output を使わずここへ書く）----

    public static string LogPath() { return Path.Combine(NdMcpConfig.ConfigDir(), "server.log"); }

    private static void Log(string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [t" + Thread.CurrentThread.ManagedThreadId + "] " + message;
        lock (_lock)
        {
            _log.Add(line);
            if (_log.Count > 50) _log.RemoveAt(0);
            try
            {
                Directory.CreateDirectory(NdMcpConfig.ConfigDir());
                File.AppendAllText(LogPath(), line + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }

    public static List<string> RecentLog()
    {
        lock (_lock) { return new List<string>(_log); }
    }
}

// ------------------------------------------------------------
//  モデル読み出し API（UI スレッドで実行される前提）
//  使う ND API は AgentReview の MarkdownExporter で実績のあるものに限る
// ------------------------------------------------------------

public static class ModelApi
{
    public static object Project(IApplication app)
    {
        var project = RequireProject(app);
        var result = Summary(project);
        result.Set("path", project.Path);
        result.Set("children", ChildrenOf(project).Select(c => (object)Summary(c)).ToList());
        return result;
    }

    public static object Tree(IApplication app, string path, string id, int depth)
    {
        var root = Resolve(app, path, id);
        return TreeNode(root, depth);
    }

    public static object Model(IApplication app, string path, string id)
    {
        var m = Resolve(app, path, id);
        var result = Summary(m);
        result.Set("fields", Fields(m));
        result.Set("children", ChildrenOf(m).Select(c => (object)Summary(c)).ToList());
        return result;
    }

    public static object Search(IApplication app, string query, string metaclass, int limit)
    {
        var project = RequireProject(app);
        var hits = new List<object>();
        var total = 0;
        foreach (var m in AllModels(project))
        {
            if (m.IsDeleted || m.IsProxy) continue;
            if (query.Length > 0)
            {
                var name = m.Name ?? "";
                if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            }
            if (metaclass.Length > 0
                && !string.Equals(ShortClassName(m), metaclass, StringComparison.OrdinalIgnoreCase)) continue;
            total++;
            if (hits.Count < limit) hits.Add(Summary(m));
        }
        return new JsonObject().Set("query", query).Set("metaclass", metaclass)
            .Set("total", total).Set("returned", hits.Count).Set("models", hits);
    }

    public static object Markdown(IApplication app, string path, string id)
    {
        var root = Resolve(app, path, id);
        var exporter = new MarkdownExporter(new MarkdownExportOptions(), null);
        var markdown = exporter.Export(root);
        return new JsonObject()
            .Set("modelPath", PathOf(root)).Set("modelCount", exporter.ModelCount)
            .Set("warnings", exporter.Warnings.Cast<object>().ToList())
            .Set("markdown", markdown);
    }

    // design.md + diagrams\<種別>\*.puml + _index.md を書き出す（AgentReview の WriteDesignArtifacts 相当）
    public static object Export(IApplication app, string path, string id, string outDir, string defaultRoot)
    {
        var root = Resolve(app, path, id);
        if (string.IsNullOrEmpty(outDir))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var folder = AgentText.SafeFileName(root.Name ?? "");
            outDir = Path.Combine(defaultRoot, stamp + (folder.Length > 0 ? "_" + folder : ""));
        }
        Directory.CreateDirectory(outDir);

        // AgentReview と同じ対応表・エクスポータ・ファイル出力メソッドを使う。
        var exporter = new MarkdownExporter(new MarkdownExportOptions(), outDir, LoadDiagramGroupRules());
        DesignArtifactWriter.Write(app, "NdMcp", exporter, root, outDir);
        var files = new List<object>();
        files.Add(Path.Combine(outDir, "design.md"));
        files.Add(Path.Combine(outDir, "_index.md"));
        var diagramsDir = Path.Combine(outDir, "diagrams");
        if (Directory.Exists(diagramsDir))
            foreach (var f in Directory.GetFiles(diagramsDir, "*.puml", SearchOption.AllDirectories))
                files.Add(f);

        return new JsonObject()
            .Set("modelPath", PathOf(root)).Set("dir", outDir)
            .Set("modelCount", exporter.ModelCount).Set("diagramCount", exporter.DiagramCount)
            .Set("skippedModelCount", exporter.SkippedModelCount)
            .Set("warnings", exporter.Warnings.Cast<object>().ToList())
            .Set("files", files);
    }

    // ---- 内部 ----

    private static DiagramGroupRules LoadDiagramGroupRules()
    {
        var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nd-agent-review", "config.ini");
        var rulesFile = "";
        if (File.Exists(config))
            foreach (var pair in IniFile.Read(config))
                if (pair.Key == "diagramGroups.rulesFile") rulesFile = pair.Value;
        return DiagramGroupRules.Load(rulesFile);
    }

    private static IProject RequireProject(IApplication app)
    {
        if (app == null) throw new NdMcpHttpError(503, "IApplication が捕獲できていません（サーバーを開始し直してください）");
        var project = app.Workspace.CurrentProject;
        if (project == null) throw new NdMcpHttpError(409, "プロジェクトが開かれていません");
        return project;
    }

    // path / id からモデルを引く。両方空ならプロジェクト。id 優先
    private static IModel Resolve(IApplication app, string path, string id)
    {
        var project = RequireProject(app);
        if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(id)) return project;
        foreach (var m in AllModels(project))
        {
            if (m.IsDeleted || m.IsProxy) continue;
            if (!string.IsNullOrEmpty(id) && m.Id == id) return m;
            if (!string.IsNullOrEmpty(path) && string.IsNullOrEmpty(id) && PathOf(m) == path) return m;
        }
        throw new NdMcpHttpError(404, "モデルが見つかりません: " + (string.IsNullOrEmpty(id) ? "path=" + path : "id=" + id));
    }

    private static IEnumerable<IModel> AllModels(IProject project)
    {
        yield return project;
        IEnumerable<IModel> all;
        try { all = project.GetAllChildren().Cast<IModel>().ToList(); }
        catch (Exception) { all = new List<IModel>(); }
        foreach (var m in all) if (m != null) yield return m;
    }

    private static List<IModel> ChildrenOf(IModel m)
    {
        try
        {
            return m.GetChildren().Cast<IModel>()
                .Where(c => c != null && !c.IsDeleted && !c.IsProxy).ToList();
        }
        catch (Exception) { return new List<IModel>(); }
    }

    private static JsonObject Summary(IModel m)
    {
        return new JsonObject()
            .Set("name", m.Name).Set("id", m.Id)
            .Set("modelPath", PathOf(m)).Set("metaclass", ShortClassName(m));
    }

    private static object TreeNode(IModel m, int depth)
    {
        var node = Summary(m);
        var children = ChildrenOf(m);
        node.Set("childCount", children.Count);
        if (depth > 0 && children.Count > 0)
            node.Set("children", children.Select(c => TreeNode(c, depth - 1)).ToList());
        return node;
    }

    // 全フィールドを種別つきで返す。判定順は MarkdownExporter.WriteFields と同じ
    private static List<object> Fields(IModel m)
    {
        var result = new List<object>();
        var cls = m.Metaclass;
        if (cls == null) return result;
        List<IField> fields;
        try { fields = cls.GetFields().Cast<IField>().ToList(); }
        catch (Exception) { return result; }

        foreach (var f in fields)
        {
            if (f == null || f.Name == null || AgentText.IsSystemName(f.Name)) continue;
            var entry = new JsonObject().Set("name", f.Name).Set("type", f.Type);
            try
            {
                // 多重度上限（-1 は無制限）。IField.UpperBound は V3.x ドキュメントで確認済み
                entry.Set("multiple", f.UpperBound != 1);
                if (f.Type == "RichText")
                {
                    entry.Set("kind", "richtext");
                    entry.Set("value", RichTextMarkdown(m, f));
                }
                else if (f.IsEmbedded && f.TypeClass != null)
                {
                    entry.Set("kind", "embedded").Set("typeClass", f.TypeClass.FullName);
                    entry.Set("children", m.GetFieldValues(f.Name).Cast<object>()
                        .OfType<IModel>().Where(c => !c.IsDeleted && !c.IsProxy)
                        .Select(c => (object)Summary(c)).ToList());
                }
                else if (f.IsReference)
                {
                    entry.Set("kind", "reference");
                    if (f.TypeClass != null) entry.Set("typeClass", f.TypeClass.FullName);
                    entry.Set("targets", m.GetFieldValues(f.Name).Cast<object>()
                        .OfType<IModel>().Select(c => (object)Summary(c)).ToList());
                }
                else
                {
                    entry.Set("kind", "value");
                    string value = null;
                    try { value = m.GetFieldString(f.Name); } catch (Exception) { }
                    if (string.IsNullOrEmpty(value) || value.Trim().Length == 0) value = JoinScalarValues(m, f.Name);
                    entry.Set("value", value ?? "");
                }
            }
            catch (Exception ex)
            {
                entry.Set("kind", "error").Set("error", ex.Message);
            }
            result.Add(entry);
        }
        return result;
    }

    private static string RichTextMarkdown(IModel m, IField f)
    {
        string text = null;
        try
        {
            var html = m.GetRichTextField(f.Name, "html");
            if (!string.IsNullOrEmpty(html)) text = HtmlToMarkdown.Convert(html);
        }
        catch (Exception) { }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
        {
            try { text = m.GetRichTextField(f.Name, "text"); } catch (Exception) { }
        }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0)
        {
            try { text = m.GetFieldString(f.Name); } catch (Exception) { }
        }
        if (string.IsNullOrEmpty(text) || text.Trim().Length == 0) text = JoinScalarValues(m, f.Name);
        return text ?? "";
    }

    private static string JoinScalarValues(IModel m, string fieldName)
    {
        var values = new List<string>();
        try
        {
            foreach (var v in m.GetFieldValues(fieldName))
            {
                if (v == null || v is IModel) continue;
                var s = v.ToString();
                if (!string.IsNullOrEmpty(s) && s.Trim().Length > 0) values.Add(s);
            }
        }
        catch (Exception) { }
        return values.Count > 0 ? string.Join(", ", values.ToArray()) : null;
    }

    private static string ShortClassName(IModel m)
    {
        string full = null;
        try
        {
            var cls = m.Metaclass;
            full = cls != null ? cls.FullName : m.ClassName;
        }
        catch (Exception) { }
        if (string.IsNullOrEmpty(full)) return "";
        var dot = full.LastIndexOf('.');
        return dot >= 0 ? full.Substring(dot + 1) : full;
    }

    private static string PathOf(IModel m)
    {
        if (m == null) return "";
        string path = null;
        try { path = m.ModelPath; }
        catch (Exception) { }
        return string.IsNullOrEmpty(path) ? (m.Name ?? "") : path;
    }
}
