# NdMcp 実機検証手順

Next Design がインストールされた PC で実施する。McpPoC（技術検証用の最小拡張）の検証項目を NdMcp に統合したもので、
前半（Step 1〜5）は本実装の前提となる技術検証、後半（Step 6〜8）は MCP ブリッジを含めた機能確認。

前提となる3点は**この PC では未検証**である。

- 検証1: スクリプト実行環境で `System.Net.HttpListener` が使えるか
- 検証2: コマンドハンドラ終了後も HTTP 受付が生存するか
- 検証3: サーバースレッドから `SynchronizationContext.Send()` で UI スレッドへ戻して Next Design API を呼べるか

## 準備

1. 配置前に検査する（この PC で ERROR 0 / WARN 0 を確認済み。ファイルを直した場合は再実行する）。

   ```
   python NdMcp/tools/build_main.py --check
   python C:\Users\ksk01\.claude\skills\nextdesign-script-extension\scripts\validate_manifest.py NdMcp --nd-version 3
   ```

2. `NdMcp` フォルダのうち `manifest.json` と `main.cs` を次へコピーする（`src/` `tools/` `bridge/` は不要）。

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

### Step 5: モデル読み出し・直呼び（比較用）

```
curl "http://127.0.0.1:3560/project?direct=1"
```

- **失敗してよいテスト**。正常応答・例外（JSON の error）・Next Design のクラッシュのどれになったかを記録する
- 万一落ちた場合は、再起動して Step 1〜4 が再現することだけ確認すればよい

### Step 6: フィールド・検索・エクスポート

```
curl "http://127.0.0.1:3560/model?path=<Step4 で見えた modelPath>"
curl "http://127.0.0.1:3560/search?q=<モデル名の一部>"
curl "http://127.0.0.1:3560/export?path=<modelPath>"
```

- `/model`: `fields[]` に richtext / value / reference / embedded の各 kind が期待どおり出るか（`kind:"error"` があればその `error` を記録）
- `/export`: 返された `dir` に design.md / _index.md / diagrams\*.puml ができているか。内容が AgentReview の「設計成果物を書き出す」と同等か
- `EditorAccessMode.GetInactiveValue` は「サーバー開始」ハンドラ内で設定しているが、**Send() で戻った別のコールバック内でも効いているかは未確認**。エディタで編集中（未確定）のフィールドが `/model` にどう出るかを1件試して記録する

### Step 7: MCP ブリッジ経由（Claude Code）

README.md の手順でブリッジを登録した状態で、Claude Code から次を試す。

- 「nd_ping を呼んで」→ Step 2 と同じ JSON が返る
- 「nd_tree で階層を出して」→ 日本語がそのまま読める形で返る
- 「サーバー停止」を押してから「nd_ping を呼んで」→ 「サーバー開始を押してください」の案内が返る

### Step 8: 停止・終了時の挙動

- 「サーバー停止」→ `curl /ping` が接続エラーになる
- 「状態確認」で受信ログを表示し、内容を記録する（`%USERPROFILE%\.nd-mcp\server.log` にも同じログが残る）
- 余裕があれば: サーバー稼働中に**プロジェクトを閉じる** → `/ping` と `/project` がどうなるか（503 の想定だが未確認）。さらに Next Design を終了したときに例外ダイアログが出ないか

## 記録表

| # | 項目 | 結果 |
|---|---|---|
| 1 | Step1: 開始ログ / System カテゴリのエラー有無 | |
| 2 | Step2: ping 直後 / 放置後 / syncContext の型名 | |
| 3 | Step3: /thread の出力 | |
| 4 | Step4: /project /tree の出力 / UI の応答性 | |
| 5 | Step5: direct=1 の結果 | |
| 6 | Step6: fields の kind / export の生成物 / 編集中フィールドの見え方 | |
| 7 | Step7: Claude Code からの呼び出し | |
| 8 | Step8: 停止 / プロジェクトを閉じた後 / ND 終了時 | |
| 9 | Next Design の正確なバージョン（ヘルプ > バージョン情報） | |

## 判定

| 結果 | 判定 |
|---|---|
| Step 4 まで成立 | 方式成立。Step 6〜8 の不具合は個別に修正（`src/server.cs` を直して `build_main.py` で再生成） |
| Step 2 は通るが Step 4 が失敗/ハング | マーシャリング方式の再検討（WPF Dispatcher の参照可否の追試、またはリクエストキュー+手動ボタン駆動の縮退案） |
| Step 1 でコンパイルエラー | HttpListener 不可。ファイルベース連携（スナップショットエクスポート + Python 側で serve）へ方式転換 |
