# NdMcp（Next Design を AI から使う）

Next Design で開いているプロジェクトを、Codex や Claude Code などの AI（MCP クライアント）から読み書きできるようにする拡張機能です。AI に「この要求の設計を説明して」「改訂履歴に1行足して」「このシーケンス図にメッセージを追加して」と頼めるようになります。

できること:

- モデルの階層・内容・検索結果を読む
- 設計書（Markdown）と図（PlantUML）をファイルに書き出す
- クラス図・シーケンス図を PlantUML で読み、編集して図に反映する。シーケンス図は新しく作ることもできる
- 図以外のモデル（フィールドの値・リッチテキスト・表の行・参照）を編集する

AI は保存をしません。変更を残すかどうかは、Next Design 上で確認してから利用者が保存してください。

## 必要なもの

- Windows と Next Design V3.x
- Codex CLI または Claude Code CLI（インストールしてログイン済みであること）
- 初回のセットアップ時はインターネット接続（Python などを自動でダウンロードします）

Python や uv を事前に入れておく必要はありません。

## セットアップ

手順は [SETUP.md](SETUP.md) にあります。Next Design と AI クライアントを終了してから、配布フォルダーで次の1行を実行するのが基本です。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\NdMcp\Setup.ps1 -Client Codex
```

Claude Code なら `-Client Claude`、両方なら `-Client Both` にします。セットアップ後も配布フォルダー内の `NdMcp\bridge` を使い続けるので、フォルダーを消したり動かしたりしないでください。

## 毎回の使い方

1. Next Design でプロジェクトを開きます。
2. リボンの「NdMcp」タブで「サーバー開始」を押します。
3. Codex または Claude Code を起動して、やりたいことを日本語で頼みます。

サーバーは Next Design を起動するたびに手動で開始します。開始していないと、AI には「サーバー開始を押してください」という案内が返ります。

頼み方の例:

- 「プロジェクトの構成を教えて」
- 「`要求/REQ-1` の内容を説明して」
- 「○○モジュールの設計書と図を出力して」
- 「改訂履歴一覧に、今日の日付で1行追加して」
- 「このクラス図に属性を追加して」

## リボンのボタン

| グループ | ボタン | 内容 |
|---|---|---|
| MCP サーバー | サーバー開始 | AI からの接続を受け付け始める（`http://127.0.0.1:3560/`） |
| | サーバー停止 | 受け付けを止める |
| | 状態確認 | 稼働状態と直近の受信ログを出力ウィンドウに表示する |
| 設定 | 設定を開く | 設定ファイル（ポート・出力先）をメモ帳で開く |
| 編集の確認 | 書ける項目を出力 | 選んでいるモデルの値と書ける項目、行を1つ足す編集 JSON のひな形を書き出す。AI がなくても使える |
| | 編集 JSON を実行 | 選んだ編集 JSON を、AI からの編集と同じ処理で実行する |

設定ファイルは `%USERPROFILE%\.nd-mcp\config.ini` です。変更したらサーバーを停止して開始し直してください。通常は変える必要はありません。ログは `%USERPROFILE%\.nd-mcp\server.log` に残ります。

「編集の確認」の出力先は `%USERPROFILE%\.nd-mcp\edit-check\` です。AI クライアントが使えない PC でも、モデル編集を試せます。

## AI が使うツール

AI は次のツールを自分で選んで使います。利用者がツール名を覚える必要はありませんが、うまく動かないときに「`nd_tree` で階層を見てから」のように指示すると確実です。

モデルは `path`（Next Design のモデルパス。例 `Project/要求/REQ-1`）か `id`（モデル ID）で指定します。どちらか一方で構いません。

### 読む

| ツール | 内容 |
|---|---|
| `nd_ping` | 接続の確認（モデルには触らない） |
| `nd_project` | 開いているプロジェクトの名前・パス・直下のモデル |
| `nd_tree(path, id, depth)` | モデルの階層。`depth` は 0〜20（既定 2） |
| `nd_model(path, id)` | モデル1件の全フィールドと子。リッチテキストは Markdown で返す |
| `nd_search(query, metaclass, limit, count)` | モデル名の部分一致検索。`metaclass` でクラス名を絞り込める。既定で 50 件まで |
| `nd_markdown(path, id)` | 指定したモデル配下を設計書形式の Markdown で返す（図は含まない）。モデルは短い ID で示し、ID → モデルパスの対応も一緒に返す |
| `nd_export(path, id, out)` | 設計書 `design.md`・ID とモデルパスの対応 `paths.tsv`・図の一覧 `_index.md`・図 `diagrams/*.puml` をファイルに書き出す。分量が多いと `design.md` は目次になり、本文は `model/` 配下にモデル階層のファイルで分かれる。`out` を省略すると `%USERPROFILE%\.nd-mcp\export` の下に作る |

### クラス図

| ツール | 内容 |
|---|---|
| `nd_class_diagram_editors(path, id)` | モデルが持つ図の一覧と、クラス図として扱えるか |
| `nd_class_diagram_puml(path, id, editor)` | クラス図を PlantUML で返す |
| `nd_class_diagram_preview(plantuml, …)` | 編集した PlantUML と今の図の差分を返す。図は変えない |
| `nd_class_diagram_apply(plantuml, …)` | 編集した PlantUML を図とモデルに反映する |

### シーケンス図

| ツール | 内容 |
|---|---|
| `nd_sequence_diagrams(path, id, limit)` | シーケンス図の一覧 |
| `nd_sequence_diagram_puml(path, id, editor)` | シーケンス図を PlantUML で返す |
| `nd_sequence_diagram_preview(plantuml, …)` | 編集した PlantUML と今の図の差分を返す。図は変えない |
| `nd_sequence_diagram_apply(plantuml, …, save)` | 編集した PlantUML で図を更新する |
| `nd_sequence_diagram_create(plantuml, path, id)` | PlantUML から新しいシーケンス図を作る |

### 図以外のモデルを編集する

| ツール | 内容 |
|---|---|
| `nd_model_schema(path, id)` | 書き込めるフィールドと、追加できる表の行の種類 |
| `nd_model_edit(operations, dry_run)` | 値の設定・リッチテキスト・表の行の追加/削除/移動・参照の設定をまとめて実行する |

PlantUML は、PlantUmlTool で出力するものと同じ書式です。

## 編集するときの注意

- 図への反映は、反映した結果を照合し、一致したときだけ確定します。一致しなければ元に戻ります。
- 図で要素を削除・改名する編集は、AI に先に差分（preview）を見せてもらってください。クラス図の削除は確認なしで実行されます。シーケンス図は別名や並びを書き換えると「削除して追加」になり、既存の要素へのトレースが消えます。
- `nd_model_edit` は、1件でも失敗すると全部取り消します。途中までの変更は残りません。確定した編集は Ctrl+Z 1回で戻せます。
- クラス図で関連を追加する反映には、プロジェクトを保存済みであることが必要です。
- クラス図の反映を Ctrl+Z で戻すと、モデルは戻りますが、図に空の箱が残ることがあります（図を切り替えて表示し直すと消えます）。元に戻したいときは、元の PlantUML を反映し直すのが確実です。
- シーケンス図を未保存のまま更新すると、その後の Ctrl+Z は「最後に保存した状態の図形」に戻します。保存後に足した図形も消えます。また、メッセージやフラグメントを追加した更新を Ctrl+Z すると Next Design が止まる既知の不具合があります。シーケンス図を AI に編集させる前に保存し、戻すときは Ctrl+Z ではなく元の PlantUML を反映し直してください。
- シーケンス図など、図の中でしか編集できないモデルを `nd_model_edit` で変えようとすると Next Design が拒否します。

## 制約

- AI からの処理中は Next Design の画面が操作できません。大きな範囲の `nd_markdown` や `nd_export` では、しばらく固まることがあります。対象のモデルを絞ってください。
- 複数の依頼は1件ずつ順番に処理します。
- 同じ PC からの接続だけを受け付けます。認証はないので、同じ PC で動く他のプログラムからも読めます。

## 困ったとき

| 症状 | 対処 |
|---|---|
| AI が「サーバーに接続できません」と言う | 同じ PC の Next Design でプロジェクトを開き、「サーバー開始」を押す |
| AI に `nextdesign` のツールが見えない | AI クライアントを再起動する。それでも見えなければ [SETUP.md](SETUP.md) のトラブルシューティングを見る |
| 応答が返ってこない | Next Design に確認ダイアログが出ていないか見る。範囲の大きい取得は対象を絞る |
| サーバー開始でポートが使われていると出る | 別の Next Design がサーバーを開始していないか確認し、1つにそろえる |
| 「NdMcp」タブが出ない | Next Design を再起動してプロジェクトを開く |
| 状況が分からない | 「状態確認」を押し、出力ウィンドウの内容と `%USERPROFILE%\.nd-mcp\server.log` を担当者に送る |

更新・取り外しの方法も [SETUP.md](SETUP.md) にあります。

開発・保守向けの情報（仕組み、HTTP API、ビルド、テスト、変更履歴）は [DEVELOPMENT.md](DEVELOPMENT.md) にあります。
