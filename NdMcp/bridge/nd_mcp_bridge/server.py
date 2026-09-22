"""stdio MCP サーバー。各ツールは NdMcp 拡張の HTTP API を 1 対 1 で呼び出す。
モデル読み出しは読み取り専用。書き込みは nd_class_diagram_apply（と一時適用の trial）だけ。"""
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
        "クラス図は nd_class_diagram_puml で PlantUML として読み、編集した PlantUML を "
        "nd_class_diagram_preview（比較のみ）→ nd_class_diagram_apply（反映）の順で渡すと図とモデルが更新される。"
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


def _post(path: str, body: dict, timeout: float | None = None) -> str:
    try:
        return _dump(client().post(path, body, timeout=timeout))
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


# ---- クラス図の PlantUML 同期（PlantUmlTool の同期本体を NdMcp 拡張に転記したもの） ----

@mcp.tool()
def nd_class_diagram_editors(path: str = "", id: str = "") -> str:
    """モデルに紐づく図の一覧を返し、各図がクラス図として同期できるか（classDiagram）と editorId を示す。
    同じモデルに複数の図があるときは、ここで得た editorId を他の nd_class_diagram_* ツールの editor に渡す。"""
    return _call("/class-sync/editors", {"path": path, "id": id})


@mcp.tool()
def nd_class_diagram_puml(path: str = "", id: str = "", editor: str = "") -> str:
    """モデル（クラス図を持つパッケージ等）のクラス図を PlantUML テキストとして返す。
    返る書式は PlantUmlTool のクラス図出力と同じで、これを編集して nd_class_diagram_preview / apply に渡す。
    別名（as xxx）やクラス名は同期の照合キーになるので、変更したい箇所以外は書き換えないこと。"""
    return _call("/class-sync/current", {"path": path, "id": id, "editor": editor}, timeout=300)


@mcp.tool()
def nd_class_diagram_preview(plantuml: str, path: str = "", id: str = "", editor: str = "", file: str = "") -> str:
    """編集した PlantUML を現在のクラス図と比較し、差分候補（追加・削除・更新）と反映できない理由を返す。図は変更しない。
    plantuml の代わりに file（Next Design が動く PC 上の .puml パス）でも渡せる。まずこれで意図した差分だけが出ることを確認する。"""
    return _post("/class-sync/preview", {"path": path, "id": id, "editor": editor, "plantuml": plantuml, "file": file}, timeout=300)


@mcp.tool()
def nd_class_diagram_apply(plantuml: str, path: str = "", id: str = "", editor: str = "", file: str = "",
                           trial: bool = False) -> str:
    """編集した PlantUML をクラス図とモデルに反映する（クラス・属性・操作・関連の追加削除、名前・可視性・型・引数・
    戻り値・多重度・既定値の変更）。trial=True なら一時適用して照合したあと必ず取り消す（確定しない）。
    確定後は Next Design 側で Undo できる。反映結果は ok / applied / committed / summary で返し、詳細は reportFile に残る。
    関連の追加を含む確定には、プロジェクトが保存済みであることが必要。"""
    mode = "trial" if trial else "apply"
    return _post(f"/class-sync/{mode}", {"path": path, "id": id, "editor": editor, "plantuml": plantuml, "file": file},
                 timeout=600)


def main() -> None:
    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
