# NdMcp 実機検証手順

Next Design がインストールされた PC で実施する。McpPoC（技術検証用の最小拡張）の検証項目を NdMcp に統合したもので、
前半（Step 1〜5）は本実装の前提となる技術検証、後半（Step 6〜8）は MCP ブリッジを含めた機能確認。

0.1.0 は会社PCで HTTP 応答・モデル取得まで確認済み。図出力は0件だったが、0.1.1 で内部コマンド内の取得設定と AgentReview の出力処理に揃えた後、ユーザーから「意図通りにシーケンス図と状態遷移図が出力された」と報告を受けた。図出力の不具合は改善確認済み。2026-09-19 に Codex から MCP 経由の `nd_ping`（`ok: true`、0.1.1）と `nd_project` の成功をユーザー提供画像で確認した。Claude Code は実機未確認だが、今回の完了条件には含めない。停止・再開等は未確認。導入手順は [SETUP.md](SETUP.md) を参照する。

- 検証1: スクリプト実行環境で `System.Net.HttpListener` が使えるか
- 検証2: コマンドハンドラ終了後も HTTP 受付が生存するか
- 検証3: サーバースレッドから `SynchronizationContext.Send()` で UI スレッドへ戻して Next Design API を呼べるか

## 準備

1. 配置前に検査する（0.1.1 は ERROR 0、内部コマンドがリボン未参照という想定内の WARN 1 を確認済み。ファイルを直した場合は再実行する）。

   ```
   python NdMcp/tools/build_main.py --check
   python C:\Users\ksk01\.claude\skills\nextdesign-extension\scripts\validate_manifest.py NdMcp --nd-version 3
   ```

   期待する結果は `ERROR 0 件 / WARN 1 件`。WARN は `NdMcp.Command.ExecuteRequest` がリボンから参照されていない件で、MCP サーバーから呼ぶコマンドなので意図どおり。これ以外の WARN が出たら直す。

2. `NdMcp` フォルダのうち `manifest.json`・`main.cs`・`resources/` を次へコピーする（`src/` `tools/` `bridge/` は不要）。[SETUP.md](SETUP.md) のスクリプトでも配置できる。

   ```
   %LOCALAPPDATA%\DENSO CREATE\Next Design\extensions\NdMcp\
   ```

   AppData は隠しフォルダ。同名フォルダが既にある場合は中身を確認してから上書きする。

3. Next Design を再起動し、任意のプロジェクトを開く（lifecycle=project のため、プロジェクトを開かないとリボンが出ない）。
4. リボンに「NdMcp」タブが出ることを確認する。出ない場合はここで中断し、症状を報告する（manifest 起因）。

## 手順と記録

curl はコマンドプロンプトか PowerShell で実行する。ポートを変更した場合は読み替える（`%USERPROFILE%\.nd-mcp\config.ini` の `port=`）。

### Step 1: サーバー開始（検証1）

「サーバー開始」ボタンを押す。

- 期待: 出力ウィンドウ（表示 > 出力）の **NdMcp カテゴリ**に「=== NdMcp サーバー開始 ===」と URL / UI thread / 出力先 / ログ の行が出る。**UI thread 行の型名（SynchronizationContext の実体）を記録する**
- **System カテゴリ**も確認する。コンパイルエラーが出ていたらその内容を丸ごと記録して中断。`System.Net` 系の型が原因なら HttpListener 不可の判定
- 「SynchronizationContext.Current が null のため…サーバーは開始しません」が出た場合はその旨を記録して中断（検証3が成立しない）
- 「サーバー開始に失敗:」が出た場合は例外の全文を記録（HttpListener の Start 失敗＝ポート競合や権限の可能性）

### Step 2: ping（検証1・2）

```
curl http://127.0.0.1:3560/ping
```

- 期待: `{"ok":true,"server":"NdMcp",...,"syncContext":"(型名)"}` が返る。**syncContext の型名を記録する**
- **そのまま2〜3分放置してからもう一度実行する**（ハンドラ終了後のスレッド生存確認）。両方の結果を記録する

### Step 3: スレッド情報

```
curl http://127.0.0.1:3560/thread
```

- 期待: `isUiThread` が `false`、`isThreadPool` が `true`。出力をそのまま記録する

### Step 4: モデル読み出し・マーシャリングあり（検証3・本命）

```
curl http://127.0.0.1:3560/project
curl "http://127.0.0.1:3560/tree?depth=2"
```

- 期待: プロジェクト名・パス・直下モデルの JSON が返る（日本語は `\uXXXX` エスケープで出る。読みにくければ Step 7 の MCP 経由で確認する）
- 実行中に Next Design の UI が固まらないか、操作して確認する
- curl が返ってこない場合は Next Design 側でダイアログ等が開いていないか確認し、状況を記録する（Ctrl+C で中断してよい）

### Step 5: 未表示の図のエクスポート（0.1.1 の再確認）

```
curl "http://127.0.0.1:3560/export?id=<対象モデルのID>"
```

- `manifest.json` と `main.cs` の両方を更新して再起動し、`/ping` の version が `0.1.1` であることを先に確認する。
- 状態遷移図・シーケンス図を表示していない状態で、両方を含むモデルの ID を指定する。
- AgentReview の「設計情報を出力」でも同じモデルを別のフォルダへ出力し、図の件数・内容・保存階層を比較する。design.md の日時と図数、warnings も記録する。
- `diagrams` 配下に両種の `.puml` があり、`design.md` と `_index.md` から参照できることを確認する。HTTP が成功でも、図が0件なら合格にしない。
- `direct=1` は廃止済み。比較用の直呼びは実施しない。

### Step 6: フィールド・検索・エクスポート

```
curl "http://127.0.0.1:3560/model?path=<Step4 で見えた modelPath>"
curl "http://127.0.0.1:3560/search?q=<モデル名の一部>"
curl "http://127.0.0.1:3560/export?path=<modelPath>"
```

- `/model`: `fields[]` に richtext / value / reference / embedded の各 kind が期待どおり出るか（`kind:"error"` があればその `error` を記録）
- `/export`: 返された `dir` に design.md / _index.md / diagrams\*.puml ができているか。内容が AgentReview の「設計成果物を書き出す」と同等か
- `EditorAccessMode.GetInactiveValue` は各リクエストの内部コマンド内で設定する。エディタで編集中（未確定）のフィールドが `/model` にどう出るかを1件試して記録する。

### Step 7: MCP ブリッジ経由（Codex、Claude Code は任意）

[SETUP.md](SETUP.md) の手順でブリッジを登録した状態で、Codex から次を試す。Claude Code の実機確認は任意とする。

- 「nd_ping を呼んで」→ Step 2 と同じ JSON が返る
- 「nd_project を呼んで」→ 開いているプロジェクトの情報が返る（ここまで Codex で確認済み）
- 「nd_tree で階層を出して」→ 日本語がそのまま読める形で返る
- 「サーバー停止」を押してから「nd_ping を呼んで」→ 「サーバー開始を押してください」の案内が返る

### Step 8: 停止・終了時の挙動

- 「サーバー停止」→ `curl /ping` が接続エラーになる
- 「状態確認」で受信ログを表示し、内容を記録する（`%USERPROFILE%\.nd-mcp\server.log` にも同じログが残る）
- 余裕があれば: サーバー稼働中に**プロジェクトを閉じる** → `/ping` と `/project` がどうなるか（503 の想定だが未確認）。さらに Next Design を終了したときに例外ダイアログが出ないか

### Step 9: クラス図の PlantUML 同期（0.2.0、curl）

PowerShell の `curl.exe` は JSON の引用符が崩れやすいので、POST 本文はファイルに書いて `--data-binary "@body.json"` で渡す。コピーのプロジェクトを開き、`Ctrl+S` で保存してから「サーバー開始」。`/ping` の `version` が `0.2.0` であること。

1. 対象モデルの `modelPath` を `/search?q=` で控え（以下 `<PATH>`）、図を確認する。
   `curl.exe -s -G "http://127.0.0.1:3560/class-sync/editors" --data-urlencode "path=<PATH>"`
   → `editors` に `"classDiagram":true` の要素がある。無ければ `reason` を記録。
2. 現在図を取る。
   `curl.exe -s -G "http://127.0.0.1:3560/class-sync/current" --data-urlencode "path=<PATH>" -o current.json`
   → `plantuml` が `@startuml` で始まり、クラス名が図と一致。`limitations` の内容を記録。
   `plantuml` を `base.puml` に落とし、`edit.puml` にコピーする（PowerShell: `ConvertFrom-Json` → `[IO.File]::WriteAllText`、BOM なし UTF-8）。
3. 本文 `body.json` を `{"path":"<PATH>","file":"C:\\work\\cs\\edit.puml"}` の形で作り、無編集で preview。
   `curl.exe -s -X POST http://127.0.0.1:3560/class-sync/preview -H "Content-Type: application/json" --data-binary "@body.json"`
   → `"changes":0`、`"ok":true`。差分が出たら `reportFile` を記録（読取り側の問題）。
4. `edit.puml` の属性名を 1 つ変えて preview → `"changes":1`、`"stopReasons":0`。Next Design 側の属性名はまだ元のまま。
5. trial: `/class-sync/trial` に同じ本文 → `"applied":true`、`"committed":false`、summary に「復元照合: 一致」。属性名は元のまま、未保存マークなし。
6. apply（**対象の図をメインエディタで開いていない状態**で）: `/class-sync/apply` → `"committed":true`、「確定後の再照合: 一致」。図を開くと属性名が変わっている。`Ctrl+Z` / `Ctrl+Y` が効く。
   失敗（`"ok":false`）なら `error` / `summary` / `reportFile` を記録し、**図を開いた状態**で同じ apply を再実行して結果を比較する（表示中でないと書けないかの切り分け）。
7. 関連追加（Editor JSON 再反映の確認）: 6 を確定したまま `Ctrl+S`。`edit.puml` に既存クラス 2 つの間の関連行を 1 本足し（ラベルは既存行と同じフィールド名の書き方）、図を開かずに preview → `"changes":1`、apply → `"committed":true`。図を開き直して線が見えている。線が見えない・`C220` が出るなら、図を開いた状態で再実行して比較。
8. Claude Code から: `nd_class_diagram_puml` → 属性を改名した PlantUML を `nd_class_diagram_preview`（`changes: 1`）→ `nd_class_diagram_apply(trial=True)`（`applied: true, committed: false`）→ `nd_class_diagram_apply`（`committed: true`）。

5〜7 が通れば、コマンドの `EditorAccessMode.GetInactiveValue` はトランザクション内の書き込みに影響しないと判断する。5 で `applied:false` かつ `C230` が出る場合はその疑いがある。

## 記録表

| # | 項目 | 結果 |
|---|---|---|
| 1 | Step1: 開始ログ / System カテゴリのエラー有無 | |
| 2 | Step2: ping 直後 / 放置後 / syncContext の型名 | |
| 3 | Step3: /thread の出力 | |
| 4 | Step4: /project /tree の出力 / UI の応答性 | |
| 5 | Step5: 0.1.1 の未表示図出力 / AgentReview との比較 | |
| 6 | Step6: fields の kind / export の生成物 / 編集中フィールドの見え方 | |
| 7 | Step7: Codex からの呼び出し | 2026-09-19、nd_ping・nd_project 成功。その他の MCP 呼び出しと停止時の応答は未確認 |
| 8 | Step8: 停止 / プロジェクトを閉じた後 / ND 終了時 | |
| 9 | Step9: クラス図同期 1〜8 の各応答（changes / applied / committed）。6・7 は図を開いていない状態と開いた状態の比較 | 2026-09-22、1〜6 成功（0.7.2 で無編集 0 件、図を閉じた状態の apply で committed True）。7（クラス追加＋関連追加、図を閉じた状態）も committed True で箱と線を確認。Undo 後の空の箱は図の切替で消える。8 は未実施 |
| 10 | Next Design の正確なバージョン（ヘルプ > バージョン情報） | |

## 判定

| 結果 | 判定 |
|---|---|
| Step 4 まで成立 | 方式成立。Step 6〜8 の不具合は個別に修正（`src/server.cs` を直して `build_main.py` で再生成） |
| Step 2 は通るが Step 4 が失敗/ハング | マーシャリング方式の再検討（WPF Dispatcher の参照可否の追試、またはリクエストキュー+手動ボタン駆動の縮退案） |
| Step 1 でコンパイルエラー | HttpListener 不可。ファイルベース連携（スナップショットエクスポート + Python 側で serve）へ方式転換 |
