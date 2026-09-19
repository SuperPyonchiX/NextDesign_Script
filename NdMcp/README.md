# NdMcp — Next Design を MCP クライアントから読む

Next Design（V3.x）で開いているプロジェクトのモデルを、Codex・Claude Code などの MCP クライアントから読み出す仕組み。読み取り専用。

```
Codex / Claude Code ── stdio (MCP) ── Python ブリッジ (bridge/) ── HTTP GET / JSON ── C# スクリプト拡張 (main.cs) ── Next Design API
                                                            127.0.0.1:3560
```

- **C# 側（Next Design 内）**: リボンの「サーバー開始」で `HttpListener` を起動し、`/project` `/tree` `/model` `/search` `/markdown` `/export` を JSON で返す。UI スレッドへ戻した後、内部コマンド `NdMcp.Command.ExecuteRequest` を実行する。そのハンドラ内で `EditorAccessMode.GetInactiveValue` を設定し、モデル・未表示の図を取得する。
- **Python 側（ブリッジ）**: 公式 `mcp` SDK（2.x）の stdio サーバー。MCP ツール 1 つが HTTP エンドポイント 1 つに対応する。

**状態：0.1.1 の主要機能を実機確認済み。** Codex から MCP 経由の `nd_ping`・`nd_project` が成功。HTTP API による情報取得と design.md・シーケンス図・状態遷移図の出力もユーザー確認済み。Claude Code の実機接続は未確認。停止・再開や長時間運用の確認状況は [VERIFY.md](VERIFY.md) を参照する。

## 最短セットアップ

Next Design と使用する AI クライアントを導入・ログイン済みの Windows PC で、両方を終了し、このリポジトリのフォルダーから実行する。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\NdMcp\Setup.ps1 -Client Codex
```

Claude Code は `-Client Claude`、両方なら `-Client Both` を指定する。uv・Python・依存ライブラリの準備、拡張配置、MCP 登録を自動実行する。既存の同名登録は更新し、設定ファイルはバックアップする。配布フォルダー内の `NdMcp/bridge` を使い続けるため、セットアップ後も削除しない。

完了後は Next Design を起動してプロジェクトを開き、「NdMcp → サーバー開始」を押す。AI クライアントを起動し、`nd_ping` と `nd_project` の実行を依頼する。

初めて使う場合は **[環境構築手順書（SETUP.md）](SETUP.md)** を参照する。

## ファイル構成

| パス | 役割 |
|---|---|
| `manifest.json` | 拡張定義（lifecycle=project、リボン「NdMcp」タブ） |
| `main.cs` | **生成物**。`tools/build_main.py` が `src/` と AgentReview の転記から組み立てる。直接編集しない |
| `src/header.cs` | ファイルヘッダと using |
| `src/server.cs` | HTTP サーバー本体・ハンドラ・JSON 化・モデル読み出し API |
| `tools/build_main.py` | `main.cs` の生成と `--check` |
| `bridge/` | Python ブリッジ（`uv` プロジェクト）。`nd_mcp_bridge/` 本体、`tests/` モック ND とテスト |
| `Setup.ps1` | Windows 用セットアップ |
| `resources/` | リボンの開始・停止・状態確認・設定アイコン（16px / 32px） |
| `tools/build_icons.ps1` | アイコンの再生成（Windows / System.Drawing） |
| `SETUP.md` | 環境構築・更新手順 |
| `VERIFY.md` | 実機検証手順 |

`main.cs` の Markdown / PlantUML 出力部は `AgentReview/main.cs` の Part 0 / 4 / 7 / 8 と `WriteDesignArtifacts` を生成時に転記する。エクスポータの修正は AgentReview 側で行い、`python NdMcp/tools/build_main.py` で再生成する。

`/export` は AgentReview と同じ図グループ判定・保存階層・索引生成を使う。対応表を設定する場合も、AgentReview の `%USERPROFILE%\.nd-agent-review\config.ini` にある `diagramGroups.rulesFile` を参照する。未設定なら共通の自動判別を使う。図が0件でも `_index.md` を更新する。

## 設定と運用

| ボタン | 動作 |
|---|---|
| サーバー開始 | `http://127.0.0.1:3560/` で受付開始（既定値） |
| サーバー停止 | 受付停止 |
| 状態確認 | 稼働状態・受信数・直近ログを出力ウィンドウに表示 |
| 設定を開く | `%USERPROFILE%\.nd-mcp\config.ini` を作成して開く（既存ファイルは維持） |

設定変更後はサーバーを停止して再開する。ログは `%USERPROFILE%\.nd-mcp\server.log` に保存される。初回の配置、更新、取り消しは [SETUP.md](SETUP.md) を参照する。

## 使い方

Next Design でプロジェクトを開き「サーバー開始」を押してから、Codex または Claude Code で話しかける。

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

セットアップの配置・バックアップ・引数の受け渡し・失敗時の停止は `python NdMcp/tests/test_setup.py` で検証する。Windows PowerShell と模擬 CLI を使い、実際のユーザー設定は変更しない。新規PCでのダウンロードから接続までの確認は別途必要となる。

## 既知の制約

- **サーバー起動は手動**（リボンボタン）。Next Design 起動時に自動で立ち上げる仕組みは無い（lifecycle=project の拡張は、ハンドラが初めて呼ばれるまでスクリプトが実行されないため）。
- 読み取り専用。モデルの作成・変更・削除は行わない。
- リクエスト処理中は Next Design の UI スレッドを占有する。大きなサブツリーの `nd_markdown` / `nd_export` は UI が一時的に固まる。
- 127.0.0.1 のみで待ち受ける。認証は無い（同一 PC の他プロセスからは誰でも読める）。
- 同時リクエストは UI スレッドへ直列化されるため、並列には処理されない。
- 長時間の受付継続、プロジェクトを閉じた後・Next Design 終了時の挙動は実機確認が必要。診断用の `direct=1` は廃止し、指定すると HTTP 400 を返す。
