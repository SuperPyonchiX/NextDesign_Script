"""stdio MCP サーバー。各ツールは NdMcp 拡張の HTTP API を 1 対 1 で呼び出す（読み取り専用）。"""
from __future__ import annotations

import json
from typing import Any

from mcp.server.mcpserver import MCPServer  # mcp SDK 2.x（1.x の FastMCP に相当）

from .client import NdClient, NdError

mcp = MCPServer(
    "nextdesign",
    instructions=(
        "Next Design で開いているプロジェクトのモデルを読み出す。"
        "モデルは path（モデルパス。例 'Project/要求/REQ-1'）または id で指定する。"
        "まず nd_project で概要を、nd_tree で階層を把握してから nd_model で詳細を読むとよい。"
        "Next Design 側でサーバーが開始されていないと接続に失敗する。"
    ),
)

_client: NdClient | None = None


def client() -> NdClient:
    global _client
    if _client is None:
        _client = NdClient()
    return _client


def _dump(data: Any) -> str:
    return json.dumps(data, ensure_ascii=False, indent=2)


def _call(path: str, params: dict | None = None, timeout: float | None = None) -> str:
    try:
        return _dump(client().get(path, params, timeout=timeout))
    except NdError as e:
        return f"ERROR: {e}"


@mcp.tool()
def nd_ping() -> str:
    """Next Design 側の NdMcp サーバーが応答するか確認する（モデルには触らない）。"""
    return _call("/ping")


@mcp.tool()
def nd_project() -> str:
    """開いているプロジェクトの概要（名前、ファイルパス、直下のモデル一覧）を返す。"""
    return _call("/project")


@mcp.tool()
def nd_tree(path: str = "", id: str = "", depth: int = 2) -> str:
    """モデル階層を返す。path/id を省略するとプロジェクト直下から。depth は展開する深さ（0〜20）。
    各ノードは name / id / modelPath / metaclass / childCount を持つ。"""
    return _call("/tree", {"path": path, "id": id, "depth": depth})


@mcp.tool()
def nd_model(path: str = "", id: str = "") -> str:
    """モデル 1 件の詳細（全フィールドの値と直下の子モデル）を返す。path か id のどちらかを指定する。
    リッチテキストは Markdown に変換済み。参照フィールドは参照先の name / modelPath を返す。"""
    return _call("/model", {"path": path, "id": id})


@mcp.tool()
def nd_search(query: str, metaclass: str = "", limit: int = 50) -> str:
    """モデル名の部分一致検索（大文字小文字を区別しない）。metaclass で短いクラス名による絞り込みができる。"""
    return _call("/search", {"q": query, "metaclass": metaclass, "limit": limit})


@mcp.tool()
def nd_markdown(path: str = "", id: str = "") -> str:
    """指定モデル配下を設計書形式の Markdown（design.md 相当）にして返す。図は含まない。
    大きなサブツリーでは応答が長くなるので、まず nd_tree で範囲を確認すること。"""
    return _call("/markdown", {"path": path, "id": id}, timeout=600)


@mcp.tool()
def nd_export(path: str = "", id: str = "", out: str = "") -> str:
    """指定モデル配下を design.md / _index.md / diagrams/*.puml としてファイルに書き出し、出力先と件数を返す。
    out を省略すると設定の ExportDir 配下にタイムスタンプ付きのディレクトリを作る。"""
    return _call("/export", {"path": path, "id": id, "out": out}, timeout=600)


def main() -> None:
    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
