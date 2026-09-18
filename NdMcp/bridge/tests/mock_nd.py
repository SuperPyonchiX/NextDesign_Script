"""NdMcp 拡張（C# 側 HTTP API）の応答形式を模したモック。ブリッジのテストと手元での動作確認に使う。

  python tests/mock_nd.py [port]   で単体起動できる（既定 3560）
"""
from __future__ import annotations

import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

# サンプルのモデルツリー（modelPath は ND の IModel.ModelPath 相当）
TREE = {
    "name": "Sample", "id": "P0", "modelPath": "Sample", "metaclass": "Project",
    "children": [
        {"name": "要求", "id": "M1", "modelPath": "Sample/要求", "metaclass": "RequirementPackage",
         "children": [
             {"name": "REQ-1 起動時間", "id": "M11", "modelPath": "Sample/要求/REQ-1 起動時間", "metaclass": "Requirement",
              "children": []},
             {"name": "REQ-2 応答性", "id": "M12", "modelPath": "Sample/要求/REQ-2 応答性", "metaclass": "Requirement",
              "children": []},
         ]},
        {"name": "機能", "id": "M2", "modelPath": "Sample/機能", "metaclass": "FunctionPackage",
         "children": [
             {"name": "初期化", "id": "M21", "modelPath": "Sample/機能/初期化", "metaclass": "Function",
              "children": []},
         ]},
    ],
}

FIELDS = {
    "M11": [
        {"name": "Description", "type": "RichText", "multiple": False, "kind": "richtext", "value": "起動は **3 秒以内**"},
        {"name": "Priority", "type": "Priority", "multiple": False, "kind": "value", "value": "High"},
        {"name": "RealizedBy", "type": "Function", "multiple": True, "kind": "reference", "typeClass": "Sample.Function",
         "targets": [{"name": "初期化", "id": "M21", "modelPath": "Sample/機能/初期化", "metaclass": "Function"}]},
    ],
}


def _walk(node, out=None):
    out = out if out is not None else []
    out.append(node)
    for c in node["children"]:
        _walk(c, out)
    return out


def _summary(n):
    return {k: n[k] for k in ("name", "id", "modelPath", "metaclass")}


def _find(path, mid):
    for n in _walk(TREE):
        if mid and n["id"] == mid:
            return n
        if path and n["modelPath"] == path:
            return n
    return None


def _tree_node(n, depth):
    node = _summary(n)
    node["childCount"] = len(n["children"])
    if depth > 0:
        node["children"] = [_tree_node(c, depth - 1) for c in n["children"]]
    return node


class MockState:
    requests = 0
    project_open = True
    last_route = ""
    last_query: dict = {}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        MockState.requests += 1
        url = urlparse(self.path)
        q = {k: v[0] for k, v in parse_qs(url.query).items()}
        MockState.last_route = url.path
        MockState.last_query = dict(q)
        try:
            status, body = self.route(url.path, q)
        except KeyError as e:
            status, body = 404, {"error": f"モデルが見つかりません: {e}"}
        data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def route(self, path, q):
        if path == "/ping":
            return 200, {"ok": True, "server": "MockNd", "version": "0.0", "requests": MockState.requests}
        if path == "/thread":
            return 200, {"thread": 1, "isUiThread": False, "isThreadPool": True, "uiThreadId": 1}
        if not MockState.project_open:
            return 503, {"error": "プロジェクトが開かれていません"}
        if path == "/project":
            return 200, {"name": TREE["name"], "path": r"C:\work\Sample.nproj",
                         "children": [_summary(c) for c in TREE["children"]]}
        root = _find(q.get("path", ""), q.get("id", "")) if (q.get("path") or q.get("id")) else TREE
        if root is None:
            raise KeyError(q.get("path") or q.get("id"))
        if path == "/tree":
            return 200, _tree_node(root, int(q.get("depth", 2)))
        if path == "/model":
            body = _summary(root)
            body["fields"] = FIELDS.get(root["id"], [])
            body["children"] = [_summary(c) for c in root["children"]]
            return 200, body
        if path == "/search":
            query = q.get("q", "").lower()
            meta = q.get("metaclass", "")
            hits = [_summary(n) for n in _walk(TREE)
                    if query in n["name"].lower() and (not meta or n["metaclass"] == meta)]
            limit = int(q.get("limit", 50))
            return 200, {"query": q.get("q", ""), "metaclass": meta, "total": len(hits),
                         "returned": min(len(hits), limit), "models": hits[:limit]}
        if path == "/markdown":
            return 200, {"modelPath": root["modelPath"], "modelCount": len(_walk(root)), "warnings": [],
                         "markdown": f"# {root['name']}\n\n(mock)\n"}
        if path == "/export":
            out = q.get("out") or r"C:\Users\x\.nd-mcp\export\20260902_000000_" + root["name"]
            return 200, {"modelPath": root["modelPath"], "dir": out, "modelCount": len(_walk(root)),
                         "diagramCount": 1, "skippedModelCount": 0, "warnings": [],
                         "files": [out + r"\design.md", out + r"\_index.md"]}
        return 404, {"error": f"不明なパス: {path}"}


def start(port: int = 0) -> tuple[ThreadingHTTPServer, str]:
    """バックグラウンドスレッドで起動し、(server, base_url) を返す。port=0 なら空きポート。"""
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server, f"http://127.0.0.1:{server.server_address[1]}"


if __name__ == "__main__":
    srv, url = start(int(sys.argv[1]) if len(sys.argv) > 1 else 3560)
    print(f"mock ND at {url}  (Ctrl+C で終了)")
    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        srv.shutdown()
