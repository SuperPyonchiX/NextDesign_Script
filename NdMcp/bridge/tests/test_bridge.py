"""ブリッジのテスト。モック ND を立てて (1) クライアント直呼び (2) ツール関数 (3) stdio 経由の MCP 通信 を確認する。"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent))
import mock_nd  # noqa: E402

from nd_mcp_bridge import server as bridge  # noqa: E402
from nd_mcp_bridge.client import NdClient, NdError  # noqa: E402


@pytest.fixture(scope="module")
def mock():
    srv, url = mock_nd.start()
    yield url
    srv.shutdown()


@pytest.fixture(autouse=True)
def use_mock(mock, monkeypatch):
    monkeypatch.setenv("ND_MCP_URL", mock)
    bridge._client = NdClient(mock)
    mock_nd.MockState.project_open = True
    yield
    bridge._client = None


# ---- クライアント ---------------------------------------------------------

def test_client_ping(mock):
    body = NdClient(mock).get("/ping")
    assert body["ok"] is True


def test_client_query_drops_empty_and_encodes_utf8(mock):
    NdClient(mock).get("/tree", {"path": "Sample/要求", "id": "", "depth": 1})
    assert mock_nd.MockState.last_route == "/tree"
    assert mock_nd.MockState.last_query == {"path": "Sample/要求", "depth": "1"}


def test_client_http_error_message(mock):
    with pytest.raises(NdError) as ei:
        NdClient(mock).get("/model", {"path": "存在しない"})
    assert "HTTP 404" in str(ei.value)
    assert "存在しない" in str(ei.value)


def test_client_connection_refused_hint():
    with pytest.raises(NdError) as ei:
        NdClient("http://127.0.0.1:1").get("/ping", timeout=2)
    assert "サーバー開始" in str(ei.value)


# ---- ツール関数（FastMCP を介さず直接） -------------------------------------

def test_tool_project():
    body = json.loads(bridge.nd_project())
    assert body["name"] == "Sample"
    assert [c["name"] for c in body["children"]] == ["要求", "機能"]


def test_tool_tree_depth():
    body = json.loads(bridge.nd_tree(depth=0))
    assert body["childCount"] == 2 and "children" not in body
    body = json.loads(bridge.nd_tree(path="Sample/要求", depth=1))
    assert [c["name"] for c in body["children"]] == ["REQ-1 起動時間", "REQ-2 応答性"]


def test_tool_model_fields():
    body = json.loads(bridge.nd_model(id="M11"))
    kinds = {f["name"]: f["kind"] for f in body["fields"]}
    assert kinds == {"Description": "richtext", "Priority": "value", "RealizedBy": "reference"}
    ref = next(f for f in body["fields"] if f["kind"] == "reference")
    assert ref["targets"][0]["modelPath"] == "Sample/機能/初期化"


def test_tool_search_metaclass_and_limit():
    body = json.loads(bridge.nd_search("REQ", metaclass="Requirement", limit=1))
    assert body["total"] == 2 and body["returned"] == 1


def test_tool_markdown_and_export():
    md = json.loads(bridge.nd_markdown(path="Sample/要求"))
    assert md["markdown"].startswith("# 要求")
    ex = json.loads(bridge.nd_export(id="M2", out=r"D:\tmp\out"))
    assert ex["dir"] == r"D:\tmp\out" and ex["files"]


def test_tool_error_is_text_not_exception():
    mock_nd.MockState.project_open = False
    text = bridge.nd_project()
    assert text.startswith("ERROR:") and "HTTP 503" in text


# ---- stdio 経由（本物の MCP クライアントでブリッジを子プロセスとして起動） -------

async def test_end_to_end_stdio(mock):
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client

    env = dict(os.environ, ND_MCP_URL=mock)
    params = StdioServerParameters(command=sys.executable, args=["-m", "nd_mcp_bridge.server"], env=env)
    async with stdio_client(params) as (read, write):
        async with ClientSession(read, write) as session:
            await session.initialize()
            tools = await session.list_tools()
            names = sorted(t.name for t in tools.tools)
            assert names == ["nd_export", "nd_markdown", "nd_model", "nd_ping", "nd_project", "nd_search", "nd_tree"]

            result = await session.call_tool("nd_search", {"query": "初期化"})
            body = json.loads(result.content[0].text)
            assert body["models"][0]["id"] == "M21"

            result = await session.call_tool("nd_model", {"path": "Sample/要求/REQ-1 起動時間"})
            body = json.loads(result.content[0].text)
            assert body["id"] == "M11"
