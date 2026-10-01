# NdMcp 開発メモ

利用者向けの説明は [README.md](README.md)、セットアップ手順は [SETUP.md](SETUP.md)、実機での確認手順は [VERIFY.md](VERIFY.md) にある。ここには構成、内部の仕組み、HTTP API、ビルドとテスト、変更履歴をまとめる。

## 構成

```
Codex / Claude Code ── stdio (MCP) ── Python ブリッジ (bridge/) ── HTTP / JSON ── DLL 拡張 (NdMcp.dll) ── Next Design API
                                                            127.0.0.1:3560
```

- C# 側（Next Design 内）: リボンの「サーバー開始」で `HttpListener` を起動する。リクエストは UI スレッドへ戻し、内部コマンド `NdMcp.Command.ExecuteRequest` の中で処理する。そのハンドラ内で `EditorAccessMode.GetInactiveValue` を設定し、未表示の図も取得する。
- Python 側（ブリッジ）: 公式 `mcp` SDK（2.x）の stdio サーバー。MCP ツール 1 つが HTTP エンドポイント 1 つに対応する。

| パス | 役割 |
|---|---|
| `manifest.json` | 拡張定義（lifecycle=project、リボン「NdMcp」タブ） |
| `NdMcp.csproj` | ビルドするソースの一覧（記載順）。AgentReview / PlantUmlTool の共有部品は正本を直接指す |
| `src/extension.cs` | エントリ `NdMcpExtension`（IExtension） |
| `src/server.cs` | HTTP サーバー本体・ハンドラ・JSON 化・モデル読み出し API・設定 |
| `src/classsync.cs` | クラス図同期 API の窓口（`ClassSyncApi`） |
| `src/sequencesync.cs` | シーケンス図同期 API の窓口 |
| `src/modeledit.cs` | モデル編集 API（`ModelEditApi`） |
| `src/editcheck.cs` | リボン「編集の確認」グループ |
| `bridge/` | Python ブリッジ（`uv` プロジェクト）。`nd_mcp_bridge/` 本体、`tests/` にモック ND とテスト |
| `Setup.ps1` | Windows 用セットアップ |
| `resources/` | リボンのアイコン（16px / 32px） |
| `tools/build_icons.ps1` | アイコンの再生成（System.Drawing） |

共有部品:

- Markdown 出力は `AgentReview/src` の 01 / 05 / 08（共通ヘルパ・Markdown 出力・`DesignArtifactWriter` など）。修正は AgentReview 側で行う。
- PlantUML 出力とクラス図・シーケンス図の同期は `PlantUmlTool/src`（shims/metamap, 10, 15, 40, 50, 60, 61, 63, 70〜74）。修正は PlantUmlTool 側で行う。
- `/export` は AgentReview と同じ図グループ判定・保存階層・索引生成を使う。図グループの対応表は AgentReview の `%USERPROFILE%\.nd-agent-review\config.ini` の `diagramGroups.rulesFile` を参照する。未設定なら自動判別。

## 設定

`%USERPROFILE%\.nd-mcp\config.ini`（key=value）。「設定を開く」で無ければ作成する。サーバーの開始時に読む。

| キー | 意味 |
|---|---|
| `port` | 待ち受けポート（既定 3560）。変えたらクライアント登録の `ND_MCP_URL` も合わせる |
| `exportDir` | `nd_export` で `out` を省略したときの出力先（既定 `%USERPROFILE%\.nd-mcp\export`） |

ログは `%USERPROFILE%\.nd-mcp\server.log`。「編集の確認」の出力は `%USERPROFILE%\.nd-mcp\edit-check\`（モデル名と ID を含むのでリポジトリへ入れない）。

## HTTP API

モデルの指定は `path`（ModelPath）か `id`。両方空ならプロジェクト全体（編集系は受け付けない）。

| HTTP | MCP ツール | 備考 |
|---|---|---|
| `GET /ping` | `nd_ping` | |
| `GET /project` | `nd_project` | |
| `GET /tree` | `nd_tree` | |
| `GET /model` | `nd_model` | 表の行（所有フィールドの子）は `fields[].children` にだけ出す |
| `GET /search` | `nd_search` | limit 超過で探索をやめ `truncated` を返す。`total` は `count=true` か未超過のときだけ |
| `GET /markdown` | `nd_markdown` | |
| `GET /export` | `nd_export` | |
| `GET /class-sync/editors` | `nd_class_diagram_editors` | |
| `GET /class-sync/current` | `nd_class_diagram_puml` | |
| `POST /class-sync/preview` | `nd_class_diagram_preview` | `currentPlantuml` は `includeCurrent: true` のときだけ |
| `POST /class-sync/trial` | なし | 一時適用して必ず取り消す。開発用 |
| `POST /class-sync/apply` | `nd_class_diagram_apply` | |
| `GET /sequence-sync/diagrams` | `nd_sequence_diagrams` | 相互作用のモデルだけ `GetEditors` を呼ぶ。ツリー順。`shapes=true` で参加者数・メッセージ数 |
| `GET /sequence-sync/current` | `nd_sequence_diagram_puml` | |
| `POST /sequence-sync/preview` | `nd_sequence_diagram_preview` | 1〜2 枚目（行ごとの差分と反映できない理由）だけ返す。接続の実測は取らない（`SequenceSyncRuntime.SkipConnections`） |
| `POST /sequence-sync/trial` | なし | 開発用 |
| `POST /sequence-sync/apply` | `nd_sequence_diagram_apply` | |
| `POST /sequence-sync/create` | `nd_sequence_diagram_create` | |
| `GET /model/schema` | `nd_model_schema` | `addableClasses` ごとに行の列（`fields`）も返す |
| `POST /model/edit` | `nd_model_edit` | 本文 `{operations, dryRun}` |

- POST の同期系の本文は JSON `{path|id, editor?, plantuml|file}`。`file` は Next Design が動く PC 上の .puml パス（300KB 以下）。
- 同期系の応答は `ok` / `changes` / `stopReasons` / `applied` / `committed` / `summary` と診断ファイル `reportFile`（`%LOCALAPPDATA%\NextDesign.ClassSync\reports\`、リボン版と同じ場所）。`details` は失敗時だけ先頭 4000 文字まで返す。
- 同期系は確認ダイアログを出さず自動で「はい」にする。クラス図で扱える差分と停止条件は [docs/class-sync-history.md](../docs/class-sync-history.md) と同じ。
- モデルの解決: `id` は `GetModelById`（削除済み・プロキシは見つからない扱い）。`path` は区切りごとに子を名前で下り、下れないとき（名前に `/` を含む等）だけ全モデルを `GetChildren` の前順で走査する。
- モデル編集は SDK のモデル操作（SetField / SetRichTextField / AddNewModel(At) / MoveTo / Relate / UnRelate / Delete）だけを使い、エディタの取込は使わない。リッチテキストは Markdown を HTML にして設定する（`MarkdownHtml`）。複数値のスカラーフィールドへの配列の設定は未対応。確定時の応答 `models` は name / id / modelPath だけで、全フィールドの読み返しは dryRun のときだけ。
- 診断用の `direct=1` は廃止。指定すると HTTP 400。

## ビルドとテスト

- ビルドと検査: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1 -Name NdMcp`。`-Deploy` で配置する（Next Design の起動中は不可）。内部コマンドがリボンから参照されていないという警告は想定どおり。
- ブリッジ: `cd NdMcp/bridge` で `uv run pytest`（モック ND に対するテストと stdio 経由の end-to-end）。`uv run python tests/mock_nd.py` でモック ND を 3560 で単体起動できる。
- コマンド境界での設定・例外伝播: `python NdMcp/tests/run_command_tests.py`。共通出力処理: `python AgentReview/tests/run_export_tests.py`（.NET Framework の C# コンパイラと模擬 SDK）。
- セットアップ: `python NdMcp/tests/test_setup.py`（Windows PowerShell と模擬 CLI。実際のユーザー設定は変えない）。新規 PC でのダウンロードから接続までは別途確認が必要。
- 3拡張共通のシーケンス図出力の回帰テスト: `python Tools/Test-SequenceExport.py`
- 以前の版へ戻すときは、Next Design を終了し、セットアップ時のバックアップ（`.ndmcp-backup-日時`）の `manifest.json` と本体を元の名前で戻す。本体はスクリプト版（0.2.1 以前）なら `main.cs`、DLL 版（0.3.0 以降）なら `NdMcp.dll`。スクリプト版へ戻すときは、配置先に残った `NdMcp.dll` などを拡張フォルダーの外へ移す。
- `Setup.ps1` はビルド済みの `publish\` を配置し、無ければ .NET SDK でその場でビルドする。スクリプト版の `main.cs` が配置先にあればバックアップしてから外す。

## 既知の制約（実装上の理由）

- lifecycle=project の拡張は、ハンドラが初めて呼ばれるまで読み込まれないため、Next Design 起動時にサーバーを自動で立ち上げられない。
- リクエストは UI スレッドで直列に処理する。
- 127.0.0.1 のみで待ち受け、認証はない。
- 長時間の受付継続、プロジェクトを閉じた後・Next Design 終了時の挙動は未確認。

## 実機確認の状況

- 0.1.1: Codex から MCP 経由の `nd_ping`・`nd_project` が成功。HTTP API による情報取得と design.md・シーケンス図・状態遷移図の出力も確認済み。Claude Code の実機接続と新規 PC でのセットアップ全工程は未確認。
- 0.2.0（2026-09-22）: 実プロジェクトのクラス図（893 行）で editors → current → 無編集 preview 0 件 → 属性改名の preview / trial / apply（図を閉じた状態）が成功。クラス追加・関連追加の確定も成功。確定後の Ctrl+Z ではモデルは戻るが、再反映した図形が空の箱として残る（図を切り替えて再表示すると消える）。
- 0.5.1（2026-09-26）: 改訂履歴一覧で「書ける項目を出力」→ ひな形で dryRun → 本番（行の追加・リッチテキスト）が成功。MCP 経由とシーケンス図 API は未確認。
- 0.6.0 / 0.6.1 は未確認。
- 詳細は [VERIFY.md](VERIFY.md)。

## 変更履歴

- **0.7.3**: AgentReview 0.17.3 と共有するエクスポータの変更（表にする条件を広げた）。
- **0.7.2**: AgentReview 0.17.2 と共有するエクスポータの変更（リンク先を `<…>` で囲んで日本語のまま書く）。
- **0.7.1**: AgentReview 0.17.1 と共有するエクスポータの修正（パス長で短縮するページ名を必要最小限に）。
- **0.7.0**: AgentReview 0.17.0 と共有するエクスポータの変更に追従。`/markdown` の本文はモデルを短い ID で示し、応答の `paths` に ID → モデルパスを返す。`/export` は大きいと `model/` 配下にページを分け、`paths.tsv` も書く。応答の `files` はディスクの走査ではなく今回書いたファイルだけにした。実機未確認。
- **0.6.1**: MCP の apply から `trial` 引数を外した（apply 自体が反映→照合→不一致なら取り消しなので、試行は二重実行だった）。preview は削除・改名のときだけ使うよう案内を変更。`nd_model_edit` の dry_run は求められたときだけにした。同期 API の `details` を失敗時だけに絞った。`/model` の `children` から表の行を外した。
- **0.6.0**: モデルの指定の解決を `GetModelById` と名前での下降に変え、全走査をやめた。`/search` と `/sequence-sync/diagrams` を limit 超過で打ち切るようにした。`/class-sync/preview` の `currentPlantuml` を任意にした。ブリッジの応答 JSON を整形しないようにした。
- **0.5.2**: `/model/schema` に表の列を出すようにした。
- **0.5.1**: リボンに「編集の確認」グループ（書ける項目を出力 / 編集 JSON を実行）を追加。
- **0.5.0**: UML 以外のモデル編集 API（`/model/schema`・`/model/edit`）を追加。
- **0.4.0**: シーケンス図の PlantUML 同期 API を追加。本体は PlantUmlTool/src/70〜74 を直接ビルドする。
- **0.3.0**: スクリプト（main.cs）から DLL に移行。共有部品は正本を csproj が直接ビルドする。
- **0.2.1**: PlantUML 出力部を PlantUmlTool/src から取り込むようにした。
- **0.2.0**: クラス図の PlantUML 同期 API を追加。
- **0.1.2**: シーケンス図で破棄の後に余分な `activate` / `deactivate` が出る不具合を修正。
