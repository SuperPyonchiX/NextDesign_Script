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
    last_body: dict = {}
    applied: list = []


CLASS_PUML = "@startuml\ntitle Sample\nclass \"Engine\" as Engine {\n  + start()\n}\n@enduml\n"


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def do_GET(self):
        self._handle(None)

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length).decode("utf-8") if length else ""
        try:
            payload = json.loads(raw) if raw else {}
        except ValueError:
            payload = None
        self._handle(payload)

    def _handle(self, payload):
        MockState.requests += 1
        url = urlparse(self.path)
        q = {k: v[0] for k, v in parse_qs(url.query).items()}
        MockState.last_route = url.path
        MockState.last_query = dict(q)
        try:
            if url.path.startswith("/class-sync/"):
                status, body = self.route_class_sync(url.path, q, payload)
            elif url.path.startswith("/sequence-sync/"):
                status, body = self.route_sequence_sync(url.path, q, payload)
            elif url.path == "/model/edit":
                status, body = self.route_model_edit(payload)
            else:
                if payload is not None:
                    status, body = 405, {"error": "GET のみ対応しています"}
                else:
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
        if path == "/model/schema":
            return 200, dict(_summary(root), editable=True, fields=[
                {"name": "Priority", "type": "Priority", "kind": "value", "multiple": False, "literals": ["High", "Low"]},
                {"name": "Description", "type": "RichText", "kind": "richtext", "multiple": False}])
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

    # C# 側 ClassSyncApi の応答形式を模す。M2（機能パッケージ）だけがクラス図 E2 を持つ。
    def route_class_sync(self, path, q, payload):
        if not MockState.project_open:
            return 503, {"error": "プロジェクトが開かれていません"}
        if path in ("/class-sync/current", "/class-sync/editors"):
            if payload is not None:
                return 405, {"error": path + " は GET のみ対応しています"}
            root = _find(q.get("path", ""), q.get("id", ""))
            if root is None:
                raise KeyError(q.get("path") or q.get("id"))
            editors = [{"editorId": "E2", "editorType": "ERDiagram", "viewDefinition": "クラス図",
                        "classDiagram": True, "reason": None}] if root["id"] == "M2" else []
            if path == "/class-sync/editors":
                return 200, {"modelPath": root["modelPath"], "modelId": root["id"], "editors": editors}
            if not editors or (q.get("editor") and q.get("editor") != "E2"):
                return 404, {"error": f"モデルにクラス図がありません: {root['modelPath']} / 図 {len(editors)} 件"}
            return 200, {"modelPath": root["modelPath"], "modelId": root["id"], "editorId": "E2",
                         "diagramName": "機能構成", "viewDefinition": "クラス図",
                         "plantuml": CLASS_PUML, "limitations": []}
        mode = path[len("/class-sync/"):]
        if mode not in ("preview", "trial", "apply"):
            return 404, {"error": f"不明なパス: {path}"}
        if payload is None:
            return 405, {"error": path + " は POST のみ対応しています"}
        MockState.last_body = dict(payload)
        plantuml = payload.get("plantuml") or ""
        if not plantuml and not payload.get("file"):
            return 400, {"error": "plantuml（本文）か file（このPC上の .puml パス）を指定してください"}
        root = _find(payload.get("path", ""), payload.get("id", ""))
        if root is None:
            raise KeyError(payload.get("path") or payload.get("id"))
        if root["id"] != "M2":
            return 404, {"error": f"モデルにクラス図がありません: {root['modelPath']} / 図 0 件"}
        changes = 0 if plantuml == CLASS_PUML else 1
        if mode == "apply":
            MockState.applied.append(plantuml)
        body = {"mode": mode, "ok": True, "modelPath": root["modelPath"], "modelId": "M2", "editorId": "E2",
                "changes": changes, "limitations": 0, "stopReasons": 0,
                "applied": mode != "preview" and changes > 0, "committed": mode == "apply" and changes > 0,
                "summary": "差分候補なし。図は変更していません。" if changes == 0 else "PlantUMLの反映\n適用と照合: 一致",
                "details": "(mock)", "error": None, "reportFile": r"C:\Users\x\AppData\Local\NextDesign.ClassSync\reports\mock.txt"}
        if mode == "preview":
            body["currentPlantuml"] = CLASS_PUML
        return 200, body

    # C# 側 SequenceSyncApi の応答形式を模す。M21（初期化）だけがシーケンス図 S21 を持つ。
    def route_sequence_sync(self, path, q, payload):
        if not MockState.project_open:
            return 503, {"error": "プロジェクトが開かれていません"}
        diagram = {"name": "初期化", "modelPath": "Sample/機能/初期化", "modelId": "M21", "editorId": "S21",
                   "viewDefinition": "シーケンス図", "lifelines": 2, "messages": 1}
        if path in ("/sequence-sync/diagrams", "/sequence-sync/current"):
            if payload is not None:
                return 405, {"error": path + " は GET のみ対応しています"}
            if path == "/sequence-sync/diagrams":
                return 200, {"root": q.get("path") or "Sample", "count": 1, "diagrams": [diagram], "truncated": False}
            root = _find(q.get("path", ""), q.get("id", ""))
            if root is None:
                raise KeyError(q.get("path") or q.get("id"))
            if root["id"] != "M21":
                return 404, {"error": "モデルにシーケンス図がありません: " + root["modelPath"]}
            return 200, dict(diagram, plantuml=SEQUENCE_PUML)
        mode = path[len("/sequence-sync/"):]
        if mode not in ("preview", "trial", "apply", "create"):
            return 404, {"error": f"不明なパス: {path}"}
        if payload is None:
            return 405, {"error": path + " は POST のみ対応しています"}
        MockState.last_body = dict(payload)
        plantuml = payload.get("plantuml") or ""
        if not plantuml and not payload.get("file"):
            return 400, {"error": "plantuml（本文）か file（このPC上の .puml パス）を指定してください"}
        root = _find(payload.get("path", ""), payload.get("id", ""))
        if root is None:
            raise KeyError(payload.get("path") or payload.get("id"))
        if mode == "create":
            return 200, {"ok": True, "error": None, "name": "新しい図", "where": "「機能」の下", "modelId": "M99",
                         "modelPath": "Sample/機能/新しい図", "editorId": "S99", "summary": "(mock)", "reportFile": "mock.txt"}
        if root["id"] != "M21":
            return 404, {"error": "モデルにシーケンス図がありません: " + root["modelPath"]}
        changes = 0 if plantuml == SEQUENCE_PUML else 1
        if mode == "apply":
            MockState.applied.append(plantuml)
        return 200, dict(diagram, mode=mode, ok=True, changes=changes, committed=mode == "apply" and changes > 0,
                         stopReasons="", summary="(mock)", details="(mock)", reportFile="mock.txt")


    # C# 側 ModelEditApi の応答形式を模す。どの操作も受け付け、dryRun なら committed=False。
    def route_model_edit(self, payload):
        if payload is None:
            return 405, {"error": "/model/edit は POST のみ対応しています"}
        MockState.last_body = dict(payload)
        ops = payload.get("operations") or []
        if not ops:
            return 400, {"error": "operations に操作の配列を指定してください"}
        for i, op in enumerate(ops):
            if op.get("op") not in ("set", "set_richtext", "add", "delete", "move", "relate", "unrelate"):
                return 200, {"ok": False, "dryRun": bool(payload.get("dryRun")), "committed": False, "results": [],
                             "models": [], "failedIndex": i, "error": "不明な op"}
        dry = bool(payload.get("dryRun"))
        return 200, {"ok": True, "dryRun": dry, "committed": not dry,
                     "results": [{"op": op["op"], "index": i} for i, op in enumerate(ops)], "models": []}


SEQUENCE_PUML = "@startuml\nparticipant A\nparticipant B\nA -> B : init()\n@enduml\n"


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
