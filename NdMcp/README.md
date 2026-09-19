# NdMcp — Next Design を MCP クライアントから読む

Next Design（V3.x）で開いているプロジェクトのモデルを、Claude Code などの MCP クライアントから読み出す仕組み。読み取り専用。

```
Claude Code ── stdio (MCP) ── Python ブリッジ (bridge/) ── HTTP GET / JSON ── C# スクリプト拡張 (main.cs) ── Next Design API
                                                            127.0.0.1:3560
```

- **C# 側（Next Design 内）**: リボンの「サーバー開始」で `HttpListener` を起動し、`/project` `/tree` `/model` `/search` `/markdown` `/export` を JSON で返す。UI スレッドへ戻した後、内部コマンド `NdMcp.Command.ExecuteRequest` を実行する。そのハンドラ内で `EditorAccessMode.GetInactiveValue` を設定し、モデル・未表示の図を取得する。
- **Python 側（ブリッジ）**: 公式 `mcp` SDK（2.x）の stdio サーバー。MCP ツール 1 つが HTTP エンドポイント 1 つに対応する。

> **状態: 0.1.1 の実機確認待ち。** 0.1.0 は実機で疎通・プロジェクト・階層・モデル詳細・検索と design.md 出力を確認済み。ただし図が0件となる問題を確認。0.1.1 は AgentReview と同じコマンド内の取得設定・出力処理へ揃えた修正版。検証手順は [VERIFY.md](VERIFY.md)。

## ファイル構成

| パス | 役割 |
|---|---|
| `manifest.json` | 拡張定義（lifecycle=project、リボン「NdMcp」タブ） |
| `main.cs` | **生成物**。`tools/build_main.py` が `src/` と AgentReview の転記から組み立てる。直接編集しない |
| `src/header.cs` | ファイルヘッダと using |
| `src/server.cs` | HTTP サーバー本体・ハンドラ・JSON 化・モデル読み出し API |
| `tools/build_main.py` | `main.cs` の生成と `--check` |
| `bridge/` | Python ブリッジ（`uv` プロジェクト）。`nd_mcp_bridge/` 本体、`tests/` モック ND とテスト |
| `VERIFY.md` | 実機検証手順 |

`main.cs` の Markdown / PlantUML 出力部は `AgentReview/main.cs` の Part 0 / 4 / 7 / 8 と `WriteDesignArtifacts` を生成時に転記する。エクスポータの修正は AgentReview 側で行い、`python NdMcp/tools/build_main.py` で再生成する。

`/export` は AgentReview と同じ図グループ判定・保存階層・索引生成を使う。対応表を設定する場合も、AgentReview の `%USERPROFILE%\.nd-agent-review\config.ini` にある `diagramGroups.rulesFile` を参照する。未設定なら共通の自動判別を使う。図が0件でも `_index.md` を更新する。

## セットアップ

### 1. Next Design 側

```
python NdMcp/tools/build_main.py --check
python C:\Users\ksk01\.claude\skills\nextdesign-script-extension\scripts\validate_manifest.py NdMcp --nd-version 3
```

の両方が通ることを確認してから、`manifest.json` と `main.cs` を次へコピーし、Next Design を**再起動**する（拡張は起動時にしか読まれない）。

```
%LOCALAPPDATA%\DENSO CREATE\Next Design\extensions\NdMcp\
```

プロジェクトを開くとリボンに「NdMcp」タブが出る。

| ボタン | 動作 |
|---|---|
| サーバー開始 | `http://127.0.0.1:<port>/` で受付開始。出力ウィンドウ（NdMcp カテゴリ）に URL とログの場所を表示 |
| サーバー停止 | 受付停止 |
| 状態確認 | 稼働状態・受信数・直近ログを出力ウィンドウに表示 |
| 設定を開く | `%USERPROFILE%\.nd-mcp\config.ini` をメモ帳で開く |

`config.ini`（無ければ初回開始時に既定値で作られる。変更は「サーバー開始」のし直しで反映）:

```ini
# key=value 形式。# 始まりはコメント
port=3560
exportDir=C:\Users\<name>\.nd-mcp\export
```

ログは `%USERPROFILE%\.nd-mcp\server.log`。

### 2. ブリッジ側

`uv` が必要。

```
cd NdMcp/bridge
uv sync
```

Claude Code に登録する（`<repo>` はこのリポジトリの絶対パス）:

```
claude mcp add nextdesign -- uv --directory <repo>\NdMcp\bridge run nd-mcp-bridge
```

ポートを変えた場合は環境変数 `ND_MCP_URL`（例 `http://127.0.0.1:4000`）を付ける:

```
claude mcp add nextdesign -e ND_MCP_URL=http://127.0.0.1:4000 -- uv --directory <repo>\NdMcp\bridge run nd-mcp-bridge
```

## 使い方

Next Design でプロジェクトを開き「サーバー開始」を押してから、Claude Code で話しかける。

| ツール | 内容 |
|---|---|
| `nd_ping` | サーバー疎通（モデルに触らない） |
| `nd_project` | プロジェクト名・パス・直下モデル |
| `nd_tree(path, id, depth)` | 階層。`depth` 0〜20（既定 2） |
| `nd_model(path, id)` | 1 モデルの全フィールド値と子。リッチテキストは Markdown 化、参照は参照先の name/modelPath |
| `nd_search(query, metaclass, limit)` | 名前の部分一致検索（大小無視）。`metaclass` は短いクラス名で絞り込み |
| `nd_markdown(path, id)` | 配下を design.md 相当の Markdown で返す（図なし） |
| `nd_export(path, id, out)` | design.md / _index.md / diagrams/*.puml をファイル出力し、出力先と件数を返す |

`path` は Next Design の ModelPath（例 `Project/要求/REQ-1`）、`id` はモデル ID。どちらか一方でよい。

サーバーが起動していないと、各ツールは「サーバー開始を押してください」という案内文を返す（例外にはしない）。

## 開発

```
cd NdMcp/bridge
uv run pytest                 # モック ND に対するテスト + stdio 経由の end-to-end
uv run python tests/mock_nd.py   # モック ND を 3560 で単体起動（ブリッジの手動確認用）
```

C# 側を直したら `python NdMcp/tools/build_main.py` で `main.cs` を再生成し、`validate_manifest.py` を通してから配置する。内部コマンドがリボンから参照されていないという警告は想定どおり。

コマンド境界での設定・例外伝播は `python NdMcp/tests/run_command_tests.py`、共通出力処理は `python AgentReview/tests/run_export_tests.py` で検証する（Windows の .NET Framework C# コンパイラと模擬 SDK を使用）。実機確認の代わりにはならない。

## 既知の制約

- **サーバー起動は手動**（リボンボタン）。Next Design 起動時に自動で立ち上げる仕組みは無い（lifecycle=project の拡張は、ハンドラが初めて呼ばれるまでスクリプトが実行されないため）。
- 読み取り専用。モデルの作成・変更・削除は行わない。
- リクエスト処理中は Next Design の UI スレッドを占有する。大きなサブツリーの `nd_markdown` / `nd_export` は UI が一時的に固まる。
- 127.0.0.1 のみで待ち受ける。認証は無い（同一 PC の他プロセスからは誰でも読める）。
- 同時リクエストは UI スレッドへ直列化されるため、並列には処理されない。
- 0.1.1 の内部コマンド経由での図取得、長時間の受付継続、プロジェクトを閉じた後・Next Design 終了時の挙動は実機確認が必要。診断用の `direct=1` は廃止し、指定すると HTTP 400 を返す。
