"""stdio MCP サーバー。各ツールは NdMcp 拡張の HTTP API を 1 対 1 で呼び出す。
モデル読み出しは読み取り専用。書き込みは nd_class_diagram_apply・nd_sequence_diagram_apply / create・nd_model_edit（と取り消し前提の dry_run）だけ。
一時適用の /class-sync/trial・/sequence-sync/trial は HTTP にだけ残す（apply が同じ照合をしてから確定するので、MCP には出さない）。"""
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
        "クラス図は nd_class_diagram_puml で PlantUML として読み、編集した PlantUML を nd_class_diagram_apply に渡すと図とモデルが更新される。"
        "シーケンス図は nd_sequence_diagrams で探し、nd_sequence_diagram_puml で PlantUML として読み、編集して "
        "nd_sequence_diagram_apply で図を更新する。新しい図は nd_sequence_diagram_create で作る。"
        "apply は反映して照合し、一致したときだけ確定する（一致しなければ元に戻す）ので、事前の試行は要らない。"
        "既存の要素を削除・改名する編集のときだけ、先に *_preview で差分を確かめる（削除は確認なしで実行され、改名の書き方を誤ると削除＋追加になって既存のトレースが消えるため）。"
        "UML 以外のモデル（フィールド値・リッチテキスト・表の行・参照）は nd_model_schema で書ける項目を確かめ、"
        "nd_model_edit で編集する（1 件でも失敗すれば全部取り消すので、そのまま本番で実行してよい）。"
        "Next Design の処理は重いので、同じ内容を読み直したり、同じ編集を試行と本番の 2 回実行したりしない。"
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
    # 整形しない（字下げと空白は読み手の LLM にとってトークンの無駄）
    return json.dumps(data, ensure_ascii=False, separators=(",", ":"))


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
    リッチテキストは Markdown に変換済み。参照フィールドは参照先の name / modelPath を返す。
    表の行（所有フィールドの子）は fields の children に出し、children にはそれ以外の子だけを出す。"""
    return _call("/model", {"path": path, "id": id})


@mcp.tool()
def nd_search(query: str, metaclass: str = "", limit: int = 50, count: bool = False) -> str:
    """モデル名の部分一致検索（大文字小文字を区別しない）。metaclass で短いクラス名による絞り込みができる。
    limit 件を超える一致が見つかった時点で探索をやめ、truncated=true を返す（このとき total は付かない）。
    該当の総数が必要なときだけ count=True にする（全モデルを走査するので重い）。"""
    return _call("/search", {"q": query, "metaclass": metaclass, "limit": limit, "count": "true" if count else ""})


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
def nd_class_diagram_preview(plantuml: str, path: str = "", id: str = "", editor: str = "", file: str = "",
                             include_current: bool = False) -> str:
    """編集した PlantUML を現在のクラス図と比較し、差分候補（追加・削除・更新）と反映できない理由を返す。図は変更しない。
    plantuml の代わりに file（Next Design が動く PC 上の .puml パス）でも渡せる。
    クラス・属性・操作・関連を削除または改名する編集のときに、apply の前に使う（apply は削除を確認なしで実行する）。追加だけなら不要。
    include_current=True なら今の図の PlantUML（currentPlantuml）も返す。nd_class_diagram_puml で読んだ直後なら不要。"""
    return _post("/class-sync/preview", {"path": path, "id": id, "editor": editor, "plantuml": plantuml, "file": file,
                                         "includeCurrent": include_current or None}, timeout=300)


@mcp.tool()
def nd_class_diagram_apply(plantuml: str, path: str = "", id: str = "", editor: str = "", file: str = "") -> str:
    """編集した PlantUML をクラス図とモデルに反映する（クラス・属性・操作・関連の追加削除、名前・可視性・型・引数・
    戻り値・多重度・既定値の変更）。反映して照合し、一致したときだけ確定する（一致しなければ取り消す）。
    確定後は Next Design 側で Undo できる。反映結果は ok / applied / committed / summary で返す。
    details は失敗したときだけ付き、全文は reportFile に残る。関連の追加を含む確定には、プロジェクトが保存済みであることが必要。"""
    return _post("/class-sync/apply", {"path": path, "id": id, "editor": editor, "plantuml": plantuml, "file": file},
                 timeout=600)


# ---- シーケンス図の PlantUML 同期（PlantUmlTool の同期本体と同じ処理） ----


@mcp.tool()
def nd_sequence_diagrams(path: str = "", id: str = "", limit: int = 50, count: bool = False, shapes: bool = False) -> str:
    """指定モデル配下（省略時は設計モデル全体）のシーケンス図をモデルツリー順に一覧する。
    各図の name / modelPath / modelId / editorId を返す。以降のツールには modelPath か modelId を渡す。
    探す範囲が分かっていれば path で絞ると速い。limit 件を超えた時点で探索をやめ truncated=true を返す（count は付かない）。
    count=True で総数も返す（全体を走査するので重い）。shapes=True で参加者数・メッセージ数（lifelines / messages）も返す（図ごとに図形を読むので重い）。"""
    return _call("/sequence-sync/diagrams", {"path": path, "id": id, "limit": limit,
                                              "count": "true" if count else "", "shapes": "true" if shapes else ""},
                 timeout=300)


@mcp.tool()
def nd_sequence_diagram_puml(path: str = "", id: str = "", editor: str = "") -> str:
    """シーケンス図（path/id は図のモデル）を PlantUML テキストとして返す。これを編集して preview / apply に渡す。
    書ける内容（PlantUmlTool の出力と同じ書式）:
    - 参加者: participant / actor / boundary / control / entity / database / collections / queue "表示名" as 別名。途中で作る参加者は create participant
    - メッセージ: 同期 A -> B : 本文 / 非同期 A ->> B / 返信 B --> A / 図外から [-> A / 図外へ A ->]
    - 実行区間: 受信の直後に activate B、終わりに deactivate B。返信で終わるバーは返信の後に deactivate
    - 破棄: 破棄メッセージ A -> B の直後に destroy B
    - 複合フラグメント: alt / else / opt / loop / par / break / critical / group … end（条件は alt 条件 のように書く）
    - Note: note over A / note left of A / note right of A（複数行は note … end note）
    - ref: ref over A, B : 相互作用名（名前が一致する相互作用が 1 つなら参照先に結び付ける）
    既存の図を更新するときは、nd_sequence_diagram_puml で得た本文を編集する。参加者の別名（as xxx）と
    変更しない行はそのまま残すこと（別名と並びで既存の要素と対応付ける。書き換えると削除＋追加になり、
    既存の要素へのトレースなどが失われる）。
    """
    return _call("/sequence-sync/current", {"path": path, "id": id, "editor": editor}, timeout=300)


@mcp.tool()
def nd_sequence_diagram_preview(plantuml: str, path: str = "", id: str = "", editor: str = "", file: str = "") -> str:
    """編集した PlantUML を現在のシーケンス図と比較し、差分（changes 件数、details に行ごとの内訳）と、
    反映できない理由（stopReasons）を返す。図は変更しない。
    既存の要素を削除・改名・並べ替える編集のときに、apply の前に使う（別名や並びを誤ると削除＋追加になり、既存のトレースが消える）。
    追加だけなら不要。plantuml の代わりに file（Next Design が動く PC 上の .puml パス）でも渡せる。"""
    return _post("/sequence-sync/preview", {"path": path, "id": id, "editor": editor, "plantuml": plantuml, "file": file}, timeout=600)


@mcp.tool()
def nd_sequence_diagram_apply(plantuml: str, path: str = "", id: str = "", editor: str = "", file: str = "",
                              save: bool = False) -> str:
    """編集した PlantUML でシーケンス図を更新する（参加者・メッセージ・実行区間・複合フラグメント・Note・ref・破棄の
    追加削除と本文・種別・並びの変更）。照合が一致したときだけ確定し、一致しなければ元に戻す（ok=false）。
    details は失敗したときだけ付き、全文は reportFile に残る。
    save=True なら未保存のプロジェクトを先に保存する（Ctrl+Z で戻せるようにする）。既定は保存しない：
    未保存のまま更新した後の Ctrl+Z は、図を最後に保存した状態の図形に戻し、保存後に追加した図形を消す。
    メッセージ・フラグメント等を追加した更新は Undo で製品が停止する既知の不具合があるので、利用者に Ctrl+Z を勧めないこと。"""
    return _post("/sequence-sync/apply", {"path": path, "id": id, "editor": editor, "plantuml": plantuml, "file": file,
                                          "save": save}, timeout=600)


@mcp.tool()
def nd_sequence_diagram_create(plantuml: str, path: str = "", id: str = "", file: str = "") -> str:
    """PlantUML から新しいシーケンス図を作る。path/id は既存のシーケンス図のモデル（その隣に作る）か、
    シーケンス図を置くモデル（パッケージ等。置ける欄が 1 種類のとき）。図の名前は title 行。保存はしない。
    作成後は返る modelPath / modelId で nd_sequence_diagram_puml 等を使える。
    書ける内容（PlantUmlTool の出力と同じ書式）:
    - 参加者: participant / actor / boundary / control / entity / database / collections / queue "表示名" as 別名。途中で作る参加者は create participant
    - メッセージ: 同期 A -> B : 本文 / 非同期 A ->> B / 返信 B --> A / 図外から [-> A / 図外へ A ->]
    - 実行区間: 受信の直後に activate B、終わりに deactivate B。返信で終わるバーは返信の後に deactivate
    - 破棄: 破棄メッセージ A -> B の直後に destroy B
    - 複合フラグメント: alt / else / opt / loop / par / break / critical / group … end（条件は alt 条件 のように書く）
    - Note: note over A / note left of A / note right of A（複数行は note … end note）
    - ref: ref over A, B : 相互作用名（名前が一致する相互作用が 1 つなら参照先に結び付ける）
    既存の図を更新するときは、nd_sequence_diagram_puml で得た本文を編集する。参加者の別名（as xxx）と
    変更しない行はそのまま残すこと（別名と並びで既存の要素と対応付ける。書き換えると削除＋追加になり、
    既存の要素へのトレースなどが失われる）。
    """
    return _post("/sequence-sync/create", {"path": path, "id": id, "plantuml": plantuml, "file": file}, timeout=600)


# ---- UML 以外のモデル編集（フィールド値・リッチテキスト・表の行・参照） ----

@mcp.tool()
def nd_model_schema(path: str = "", id: str = "") -> str:
    """モデルの書き込めるフィールドを返す。各フィールドの kind は
    value（文字列・数値・真偽・列挙。列挙は literals に候補）/ richtext / embedded（子モデル＝表の行。addableClasses に追加できるクラス）/
    reference（参照。typeClass が参照先の型）。editable=false のモデルは編集できない。nd_model_edit の前にこれで確かめる。"""
    return _call("/model/schema", {"path": path, "id": id})


@mcp.tool()
def nd_model_edit(operations: list[dict], dry_run: bool = False) -> str:
    """モデルを編集する。operations の操作をまとめて 1 回で実行し、1 つでも失敗したら全部取り消す（failedIndex と error を返す）。
    失敗しても途中までの変更は残らないので、通常は dry_run なしでそのまま実行する。確定した編集は Next Design の Ctrl+Z で 1 回で戻せる。保存はしない。
    models には編集したモデルの name / id / modelPath を返す。
    dry_run=True は、利用者が確定前に結果を見たいと言ったときだけ使う。実行して models に編集後のフィールド値を付けたあと、必ず取り消す。

    モデルの指定（target / parent / to / before / after）は {"path": モデルパス} / {"id": ID} / {"ref": 名前}。
    ref は同じ operations の中で先に add したモデルに "as" で付けた名前。
    操作:
    - {"op": "set", "target": …, "fields": {"フィールド名": 値}}  値は文字列・数値・真偽・列挙のリテラル名。リッチテキストは Markdown 文字列
    - {"op": "set_richtext", "target": …, "field": "名前", "markdown": "…"}（または "html"）。見出し・段落・箇条書き・番号付き・表・引用・コード・太字・斜体・リンク
    - {"op": "add", "parent": …, "field": "所有フィールド", "class": "クラス名（候補が 1 つなら省略可）",
       "before" | "after": 兄弟モデル, または "index": 0 始まりの位置（省略時は末尾）,
       "fields": {…}, "richtext": {"名前": "markdown"}, "as": "名前"}   表の行の追加はこれ（行は所有フィールドの子モデル、列はそのフィールド）
    - {"op": "delete", "target": …}
    - {"op": "move", "target": …, "parent": …（省略時は今の親）, "field": …（省略時は今の所有フィールド）, "before" | "after" | "index"}
    - {"op": "relate" | "unrelate", "target": …, "field": "参照フィールド", "to": …}
    例: 改訂履歴に 1 行足す
      [{"op": "add", "parent": {"path": "…/改訂履歴一覧"}, "field": "改訂履歴", "fields": {"バージョン": "1.0.2", "日付": "2026/09/26"},
        "richtext": {"改訂内容": "- 項目を追加"}}]
    フィールド名・クラス名は nd_model / nd_model_schema で確かめる。シーケンス図などエディタの中でしか編集できないモデルは製品が拒否する。"""
    return _post("/model/edit", {"operations": operations, "dryRun": dry_run}, timeout=600)


def main() -> None:
    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
