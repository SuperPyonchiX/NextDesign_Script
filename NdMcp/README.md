# NdMcp — Next Design を MCP クライアントから読む

## 0.2.0: クラス図の PlantUML 同期 API

ClassImportProbe（0.7.1）で実機確認したクラス図の同期本体を `main.cs` に転記し、MCP ツール 4 つと HTTP エンドポイント 5 つを足した。読み出し API はこれまでどおり読み取り専用で、書き込むのは `/class-sync/trial` と `/class-sync/apply` だけ。

| ツール | HTTP | 内容 |
|---|---|---|
| `nd_class_diagram_editors(path, id)` | `GET /class-sync/editors` | モデルに紐づく図の一覧と、クラス図として同期できるか |
| `nd_class_diagram_puml(path, id, editor)` | `GET /class-sync/current` | クラス図を PlantUML（PlantUmlTool のクラス図出力と同じ書式）で返す |
| `nd_class_diagram_preview(plantuml, path, id, editor, file)` | `POST /class-sync/preview` | 編集した PlantUML と図を比較し、差分候補と停止理由を返す。図は変えない |
| `nd_class_diagram_apply(plantuml, path, id, editor, file, trial)` | `POST /class-sync/trial` / `apply` | 反映する。`trial=True` は一時適用して照合し必ず取り消す |

- 対象の図は `path` / `id` のモデルが持つ図（`IModel.GetEditors()`）から、クラス図と判定できる最初の 1 件を選ぶ。複数あるときは `editor` に editorId を渡す。
- POST の本文は JSON `{path|id, editor?, plantuml|file}`。`file` は Next Design が動く PC 上の .puml パス（300KB 以下）。
- 応答は `ok` / `changes` / `stopReasons` / `applied` / `committed` / `summary` / `details` と、診断ファイル `reportFile`（`%LOCALAPPDATA%\NextDesign.ClassSync\reports\`、リボン版と同じ場所）。`preview` には `currentPlantuml` も付く。
- 扱える差分・停止条件・関連の追加に保存済みプロジェクトが要る点は [ClassImportProbe/README.md](../ClassImportProbe/README.md) と同じ。確認ダイアログは出さず自動で「はい」にする（クラス削除などの確認はエージェント側が preview の結果で行う）。
- **実機確認（2026-09-22）**: 実プロジェクトのクラス図（893 行）で `editors` → `current` → 無編集 preview 0 件 → 属性改名の preview / trial / apply（図をメインエディタで閉じた状態）まで成功。**未確認**: 図を閉じた状態での関連追加の確定（Editor JSON の再反映）とクラス追加（`AddNodeShape`）。失敗したら対象の図を開いた状態で再実行する。

実機手順:

1. `NdMcp` フォルダを配置し直して Next Design を再起動、プロジェクト（コピー）を開き「サーバー開始」。
2. `curl "http://127.0.0.1:3560/class-sync/editors?path=<クラス図を持つモデルのパス>"` で `classDiagram: true` の editorId が出る。
3. `curl "http://127.0.0.1:3560/class-sync/current?path=..."` の `plantuml` を .puml に保存し、属性名を 1 つ変える。
4. `curl -X POST http://127.0.0.1:3560/class-sync/preview -H "Content-Type: application/json" -d "{\"path\":\"...\",\"file\":\"C:\\\\work\\\\edit.puml\"}"` で `changes: 1`、`stopReasons: 0`。
5. 同じ本文で `/class-sync/trial` → `applied: true`, `committed: false`、図が元のまま。`/class-sync/apply` → `committed: true`、図とモデルが変わり Ctrl+Z で戻る。
6. Claude Code から `nd_class_diagram_puml` → 編集 → `nd_class_diagram_preview` → `nd_class_diagram_apply` の順で同じ結果になる。

## 0.1.2

0.1.2 では、シーケンス図の破棄後に余分な `activate` / `deactivate` を出力する不具合を修正。破棄メッセージと破棄点の両方に適用する。3拡張の共通回帰テストは `python Tools/Test-SequenceExport.py`。Next Designでの再出力・描画は実機確認待ち。 共通出力処理の再生成に必要な依存コードも生成対象へ追加し、図の未確認一覧出力を同期した。

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
| `src/classsync.cs` | クラス図同期 API の窓口（`ClassSyncApi`）と、転記した同期本体が参照する `ClassExperiment` の代替 |
| `tools/build_main.py` | `main.cs` の生成と `--check` |
| `bridge/` | Python ブリッジ（`uv` プロジェクト）。`nd_mcp_bridge/` 本体、`tests/` モック ND とテスト |
| `Setup.ps1` | Windows 用セットアップ |
| `resources/` | リボンの開始・停止・状態確認・設定アイコン（16px / 32px） |
| `tools/build_icons.ps1` | アイコンの再生成（Windows / System.Drawing） |
| `SETUP.md` | 環境構築・更新手順 |
| `VERIFY.md` | 実機検証手順 |

`main.cs` の Markdown / PlantUML 出力部は `AgentReview/main.cs` の Part 0 / 4 / 7 / 8 と `WriteDesignArtifacts` を生成時に転記する。エクスポータの修正は AgentReview 側で行い、`python NdMcp/tools/build_main.py` で再生成する。クラス図同期部は `ClassImportProbe/sync/ClassSyncRuntime.cs` と `ClassSync.cs` をそのまま転記する。同期の修正は ClassImportProbe 側で行い、テストを通してから再生成する。

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
| `nd_class_diagram_editors` / `nd_class_diagram_puml` / `nd_class_diagram_preview` / `nd_class_diagram_apply` | クラス図の PlantUML 同期（先頭の 0.2.0 の節を参照） |

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
- モデル読み出しは読み取り専用。書き込むのは `/class-sync/trial`（取り消す）と `/class-sync/apply` だけで、いずれも 1 つのトランザクション内で行い、確定後は Next Design 側で Undo できる。
- リクエスト処理中は Next Design の UI スレッドを占有する。大きなサブツリーの `nd_markdown` / `nd_export` は UI が一時的に固まる。
- 127.0.0.1 のみで待ち受ける。認証は無い（同一 PC の他プロセスからは誰でも読める）。
- 同時リクエストは UI スレッドへ直列化されるため、並列には処理されない。
- 長時間の受付継続、プロジェクトを閉じた後・Next Design 終了時の挙動は実機確認が必要。診断用の `direct=1` は廃止し、指定すると HTTP 400 を返す。
