# NdMcp — Next Design を MCP クライアントから読む

## 0.5.2: schema に表の列を出す

`/model/schema`（と「書ける項目を出力」）で、所有フィールドの `addableClasses` ごとに、その行の列（`fields`：名前・型・種類・列挙の候補）も返す。既存の行の書き換えや行の追加で、行のモデルを別に調べなくてよい。

## 0.5.1: モデル編集の確認ボタン

MCP サーバー（HTTP・Python）を使えない PC でもモデル編集 API を試せるよう、リボンに「編集の確認」グループを足した。
- 書ける項目を出力: モデルナビゲータで選んだモデルの `/model` と `/model/schema` の内容を JSON に書き、表（所有フィールド）があれば最後の行の値を写した「行を 1 つ足す」編集 JSON のひな形（dryRun: true）も作る
- 編集 JSON を実行: 選んだ JSON を `/model/edit` と同じ処理（ModelEditApi.Edit）で実行し、結果を JSON に書く。dryRun が true でなければ確認してから確定する
- 出力先は `%USERPROFILE%\.nd-mcp\edit-check\`
- 実機（2026-09-26）: 改訂履歴一覧で「書ける項目を出力」→ ひな形で dryRun → 本番（行の追加・リッチテキスト）が成功

## 0.5.0: UML 以外のモデル編集 API

AI がフィールドの値・リッチテキスト・表の行（所有フィールドの子モデル）・参照を編集できるようにした（`src/modeledit.cs`）。

| MCP ツール | HTTP | 内容 |
|---|---|---|
| `nd_model_schema(path, id)` | `GET /model/schema` | 書き込めるフィールド（kind: value / richtext / embedded / reference）、列挙の候補、表の行として追加できるクラス、参照先の型、編集可否 |
| `nd_model_edit(operations, dry_run)` | `POST /model/edit` | 操作をまとめて 1 トランザクションで実行。1 つでも失敗したら全部取り消し。`dry_run` は実行して結果を返したあと必ず取り消す |

操作は `set`（値）・`set_richtext`（Markdown または HTML）・`add`（子モデル＝表の行。位置は before / after / index。`as` で名前を付けて後の操作から `{"ref": 名前}` で指せる）・`delete`・`move`・`relate` / `unrelate`。

- SDK のモデル操作（SetField / SetRichTextField / AddNewModel(At) / MoveTo / Relate / UnRelate / Delete）だけを使い、エディタの取込は使わない。確定した編集は保存していなくても Ctrl+Z で 1 回で戻せる見込み（未確認）
- リッチテキストは Markdown を HTML にして、テキスト値と一緒に設定する（`MarkdownHtml`。見出し・段落・箇条書き（入れ子）・番号付き・表・引用・コード・太字・斜体・リンク）
- target / parent の path と id が両方空なのは受け付けない（プロジェクト自体を指さないため）
- 複数値のスカラーフィールドへの配列の設定は未対応
- 実機未確認

## 0.4.0: シーケンス図の PlantUML 同期 API

AI がシーケンス図を読み、編集し、新しく作れるようにした。同期の本体は PlantUmlTool/src/70〜74（リボンの「PlantUMLで更新」「PlantUMLから新規作成」と同じ処理）を直接ビルドする。ダイアログは出さない。

| MCP ツール | HTTP | 内容 |
|---|---|---|
| `nd_sequence_diagrams(path, id, limit)` | `GET /sequence-sync/diagrams` | 指定モデル配下（省略時はプロジェクト全体）のシーケンス図の一覧（name / modelPath / modelId / editorId / 参加者数・メッセージ数） |
| `nd_sequence_diagram_puml(path, id, editor)` | `GET /sequence-sync/current` | 図を PlantUML（PlantUmlTool の出力と同じ書式）で返す |
| `nd_sequence_diagram_preview(plantuml, path, id, editor, file)` | `POST /sequence-sync/preview` | 編集した PlantUML と図を比較し、差分件数・行ごとの内訳・反映できない理由を返す。図は変えない |
| `nd_sequence_diagram_apply(plantuml, path, id, editor, file, trial, save)` | `POST /sequence-sync/trial` / `apply` | 図を更新する。照合が一致したときだけ確定。`trial=True` は一時適用して必ず取り消す。`save=True` は未保存のプロジェクトを先に保存する |
| `nd_sequence_diagram_create(plantuml, path, id, file)` | `POST /sequence-sync/create` | 新しいシーケンス図を作る。path/id は既存の図のモデル（その隣）か、図を置くモデル |

- ref の参照先は、名前が一致する相互作用が 1 つのときだけ結び付ける（候補が複数なら参照先なしで差分に残る）
- 未保存のまま更新した後の Ctrl+Z は、図を最後に保存した状態の図形に戻す（製品のエディタ取込の Undo の挙動。PlantUmlTool 3.3.3 の README）。apply の応答の `undo` にその旨を返す
- 図を開いていない状態での更新・作成は実機未確認（クラス図は図を閉じたままの apply が成功している）
- 実機未確認

## 0.3.0: DLL 方式に移行

スクリプト（main.cs）から DLL（`NdMcp.dll`）に移した。機能は 0.2.1 と同じ。AgentReview と PlantUmlTool の共有部品は、転記せず正本のファイルを `NdMcp.csproj` が直接ビルドする。`Setup.ps1` はビルド済みの `publish\`（無ければ .NET SDK でその場でビルド）を配置し、スクリプト版の `main.cs` はバックアップしてから外す。実機での読み込みは未確認。

## 0.2.1: PlantUML 出力を PlantUmlTool/src から転記

`/export` の .puml と `/class-sync/current` が同じ出力コードになるよう、シーケンス図・クラス図・状態遷移図の出力部を AgentReview 経由ではなく `PlantUmlTool/src` から直接転記するようにした。AgentReview からは Part 0 / 4（共通ヘルパ・Markdown 出力）だけを転記する。クラス図の .puml は PlantUmlTool 2.2.0 の書式（戻り値・多重度付き）になる。実機は未確認（`/export` で以前と同じフォルダ構成が出ること）。

## 0.2.0: クラス図の PlantUML 同期 API

PlantUmlTool 2.2.0 のクラス図同期本体（`PlantUmlTool/src/60〜61`。ClassImportProbe 0.7.2 として実機確認したもの）を `main.cs` に転記し、MCP ツール 4 つと HTTP エンドポイント 5 つを足した。読み出し API はこれまでどおり読み取り専用で、書き込むのは `/class-sync/trial` と `/class-sync/apply` だけ。

| ツール | HTTP | 内容 |
|---|---|---|
| `nd_class_diagram_editors(path, id)` | `GET /class-sync/editors` | モデルに紐づく図の一覧と、クラス図として同期できるか |
| `nd_class_diagram_puml(path, id, editor)` | `GET /class-sync/current` | クラス図を PlantUML（PlantUmlTool のクラス図出力と同じ書式）で返す |
| `nd_class_diagram_preview(plantuml, path, id, editor, file)` | `POST /class-sync/preview` | 編集した PlantUML と図を比較し、差分候補と停止理由を返す。図は変えない |
| `nd_class_diagram_apply(plantuml, path, id, editor, file, trial)` | `POST /class-sync/trial` / `apply` | 反映する。`trial=True` は一時適用して照合し必ず取り消す |

- 対象の図は `path` / `id` のモデルが持つ図（`IModel.GetEditors()`）から、クラス図と判定できる最初の 1 件を選ぶ。複数あるときは `editor` に editorId を渡す。
- POST の本文は JSON `{path|id, editor?, plantuml|file}`。`file` は Next Design が動く PC 上の .puml パス（300KB 以下）。
- 応答は `ok` / `changes` / `stopReasons` / `applied` / `committed` / `summary` / `details` と、診断ファイル `reportFile`（`%LOCALAPPDATA%\NextDesign.ClassSync\reports\`、リボン版と同じ場所）。`preview` には `currentPlantuml` も付く。
- 扱える差分・停止条件・関連の追加に保存済みプロジェクトが要る点は [docs/class-sync-history.md](../docs/class-sync-history.md) と同じ。確認ダイアログは出さず自動で「はい」にする（クラス削除などの確認はエージェント側が preview の結果で行う）。
- **実機確認（2026-09-22）**: 実プロジェクトのクラス図（893 行）で `editors` → `current` → 無編集 preview 0 件 → 属性改名の preview / trial / apply（図をメインエディタで閉じた状態）まで成功。同じ状態でクラス追加（属性・操作付き）と関連追加の確定も成功し、図を開くと箱と線が見える。確定後の Ctrl+Z ではモデルは戻るが、線を見せるために再反映した図形が空の箱として残る（図を切り替えて再表示すると消える。Ctrl+Y でも正常に戻る）。元に戻すときは Undo ではなく、元の PlantUML を `apply` し直す方が確実。

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
Codex / Claude Code ── stdio (MCP) ── Python ブリッジ (bridge/) ── HTTP GET / JSON ── DLL 拡張 (NdMcp.dll) ── Next Design API
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
| `NdMcp.csproj` | ビルドするソースの一覧（記載順）。AgentReview / PlantUmlTool の共有部品は正本を直接指す |
| `src/extension.cs` | ファイルヘッダとエントリ `NdMcpExtension`（IExtension） |
| `src/server.cs` | HTTP サーバー本体・ハンドラ（`NdMcpExtension` の partial）・JSON 化・モデル読み出し API |
| `src/classsync.cs` | クラス図同期 API の窓口（`ClassSyncApi`）と、同期本体が参照する `ClassExperiment` の代替 |
| `bridge/` | Python ブリッジ（`uv` プロジェクト）。`nd_mcp_bridge/` 本体、`tests/` モック ND とテスト |
| `Setup.ps1` | Windows 用セットアップ |
| `resources/` | リボンの開始・停止・状態確認・設定アイコン（16px / 32px） |
| `tools/build_icons.ps1` | アイコンの再生成（Windows / System.Drawing） |
| `SETUP.md` | 環境構築・更新手順 |
| `VERIFY.md` | 実機検証手順 |

Markdown 出力部は `AgentReview/src` の 01 / 05 / 08（共通ヘルパ・Markdown 出力・`DesignArtifactWriter` などの共有部品）を、PlantUML 出力部とクラス図同期部は `PlantUmlTool/src`（shims/metamap, 10, 15, 40, 50, 60, 61, 63）を `NdMcp.csproj` が直接ビルドする。Markdown 出力の修正は AgentReview 側、PlantUML 出力と同期の修正は PlantUmlTool 側で行い、NdMcp をビルドし直す（`powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1 -Name NdMcp`）。

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

C# 側を直したら `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1 -Name NdMcp` でビルドと検査を行い、`-Deploy` を付けて配置する。内部コマンドがリボンから参照されていないという警告は想定どおり。

コマンド境界での設定・例外伝播は `python NdMcp/tests/run_command_tests.py`、共通出力処理は `python AgentReview/tests/run_export_tests.py` で検証する（Windows の .NET Framework C# コンパイラと模擬 SDK を使用）。実機確認の代わりにはならない。

セットアップの配置・バックアップ・引数の受け渡し・失敗時の停止は `python NdMcp/tests/test_setup.py` で検証する。Windows PowerShell と模擬 CLI を使い、実際のユーザー設定は変更しない。新規PCでのダウンロードから接続までの確認は別途必要となる。

## 既知の制約

- **サーバー起動は手動**（リボンボタン）。Next Design 起動時に自動で立ち上げる仕組みは無い（lifecycle=project の拡張は、ハンドラが初めて呼ばれるまでスクリプトが実行されないため）。
- モデル読み出しは読み取り専用。書き込むのは `/class-sync/trial`（取り消す）と `/class-sync/apply` だけで、いずれも 1 つのトランザクション内で行い、確定後は Next Design 側で Undo できる。
- リクエスト処理中は Next Design の UI スレッドを占有する。大きなサブツリーの `nd_markdown` / `nd_export` は UI が一時的に固まる。
- 127.0.0.1 のみで待ち受ける。認証は無い（同一 PC の他プロセスからは誰でも読める）。
- 同時リクエストは UI スレッドへ直列化されるため、並列には処理されない。
- 長時間の受付継続、プロジェクトを閉じた後・Next Design 終了時の挙動は実機確認が必要。診断用の `direct=1` は廃止し、指定すると HTTP 400 を返す。
