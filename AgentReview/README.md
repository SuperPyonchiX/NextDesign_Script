# AgentReview — Claude Code / Codex による設計レビュー支援

Next Design V3.x のスクリプト拡張。設計情報をエクスポートし、ターミナル上の
Claude Code / Codex エージェントと対話しながら設計レビューと修正提案の生成を行う。

## 役割分担

- **本拡張**: 設計情報のエクスポート / エージェント向け指示書の生成 / ターミナルでのエージェント起動 / 結果ファイルの表示
- **対話**: ターミナル上の claude / codex 本来の UI（V3.x の拡張 UI では対話画面を作れないため）
- **自動修正の範囲**: レビュー指摘（review.md）と修正提案（proposal.md / proposed/*.puml）のファイル出力まで。
  Next Design のモデルへの直接書き戻しは行わない（V3.x は読み取り専用要素が多いため）

## 前提

- Next Design V3.x
- Claude Code（`claude`）または Codex（`codex`）CLI がインストール済みで PATH が通っていること
  （リボンの「環境診断」で確認できる。インストール直後は Next Design の再起動が必要）

## 配置

このフォルダ（`AgentReview`）ごと次のいずれかへコピーし、Next Design を再起動する。

| 配置先 | 適用範囲 |
|---|---|
| `%LOCALAPPDATA%\DENSO CREATE\Next Design\extensions\AgentReview\` | そのユーザーのみ |
| `C:\ProgramData\DENSO CREATE\Next Design\extensions\AgentReview\` | そのPCの全ユーザー |

## 使い方

1. ナビゲータでレビュー対象のモデル（またはプロジェクト）を選択する
2. リボン「AI レビュー」タブの **レビュー開始** を押す
   - 初回は基点フォルダを選択する（設定に記憶される）
   - セッションフォルダが作成され、設計情報が `design\design.md` にエクスポートされ、ターミナルでエージェントが起動する
3. ターミナルで「レビューして」と入力する。**design-review スキル**に従い、エージェントが最初に開発工程（要求分析 / 基本設計 / 詳細設計）を質問し、工程別の観点表でレビューする
4. 指摘は `review\review.md`、修正提案は `review\proposal.md` に出力される。リボンの **結果を開く** で参照できる
5. 対話を中断した後は **ターミナル再開** で続きから再開できる

図（シーケンス図・クラス図・状態遷移図）は自動で `design\*.puml` に出力され、design.md の該当箇所から参照される（一覧は `design\_index.md`）。シーケンス図・状態遷移図の構成要素（メッセージ・状態など）はテキストには出力せず .puml に委ねる。出力エンジンは PlantUmlTool からの転記（修正は PlantUmlTool 側で検証してから反映すること）。

## セッションフォルダの構成

```
<基点フォルダ>\<モデル名>_<日時>\
├── CLAUDE.md / AGENTS.md   指示書（拡張が生成。両エージェント分）
├── .agents\skills\design-review\   レビュースキル（SKILL.md + 工程別観点表）
├── .claude\skills          → .agents\skills へのジャンクション（Claude Code 用。失敗時はコピー）
├── design\                 入力（design.md / *.puml / _index.md。エージェントは変更禁止）
├── review\                 エージェントの出力（review.md / proposal.md / proposed\）
└── session.ini             セッション情報（拡張が管理）
```

## design-review スキル

レビューの進め方と工程別観点は Agent Skill として渡す。エージェントは最初に対象工程を質問し、
`references/` の観点表（要求分析 / 基本設計 / 詳細設計）を適用してレビューする。

- **観点表のカスタマイズ**: 原本は `%USERPROFILE%\.nd-agent-review\skills\design-review\`。
  初回実行時に既定内容が生成され、以降は上書きされない（ユーザー編集が保護される）。
  編集すれば次回の「レビュー開始」から反映される（ND 再起動不要）
- **既定内容の変更（開発者向け）**: リポジトリの `skills/design-review/` が正本。
  編集後に `python AgentReview/tools/embed_skills.py` で main.cs へ再埋め込みする

## 設定

`%USERPROFILE%\.nd-agent-review\config.ini`。リボンの **設定** ボタンで開ける。
ボタン操作のたびに読み直されるため、保存すれば Next Design の再起動なしで反映される。

| キー | 意味 |
|---|---|
| `agent` | 使用するエージェント（`claude` / `codex`）。「エージェント切替」ボタンでも変更可 |
| `workspaceRoot` | セッションの基点フォルダ |
| `terminal` | `auto`（Windows Terminal があれば使う）/ `wt` / `cmd` |
| `claude.command` / `codex.command` | CLI コマンド名 |
| `claude.args` / `codex.args` | 対話起動時の追加引数（例: `--permission-mode acceptEdits`） |
| `perspectives` | レビュー観点（カンマ区切り。指示書に埋め込まれる） |

## 制約・注意

- design.md に出ない情報がある場合は、対象モデルを選択して **エクスポート診断** を実行すると、フィールド構成（RichText の有無など）・子モデル・エディタが出力ウィンドウにダンプされる（プロファイル依存の調査用）
- スクリプトの変更は Next Design を再起動するまで反映されない
- エージェントの実行完了を拡張は検知しない。「結果を開く」でファイルの有無を確認する
- 配置前検証: `python <skills>/nextdesign-script-extension/scripts/validate_manifest.py AgentReview --nd-version 3`
