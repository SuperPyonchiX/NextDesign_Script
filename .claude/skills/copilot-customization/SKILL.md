---
name: copilot-customization
description: 'GitHub Copilotカスタマイゼーションファイル（プロンプト、エージェント、インストラクション、スキル）の作成ガイド。.prompt.md、.agent.md、.instructions.md、SKILL.mdファイルを作成する際に使用してください。'
---

# GitHub Copilot Customization Files

このスキルは、GitHub Copilotのカスタマイゼーションファイル（プロンプト、エージェント、インストラクション、スキル）の作成を支援します。

## いつこのスキルを使用するか

以下の場合に本スキルを活用してください:

- 再利用可能なプロンプトファイル（`.prompt.md`）を作成する
- カスタムエージェント（`.agent.md`）を定義する
- ファイル固有のインストラクション（`.instructions.md`）を設定する
- Agent Skills（`SKILL.md`）を実装する
- プロジェクト固有の開発ワークフローを自動化する

## ファイルタイプ別ガイド

### 1. プロンプトファイル（.prompt.md）

**用途**: 再利用可能な質問・タスクのテンプレート

**作成場所**: `.github/prompts/`（ユーザープロファイルの `prompts/` フォルダにも配置可能）

**Front matter フィールド**:

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `description` | いいえ | プロンプトの説明（シングルクォート） |
| `name` | いいえ | UI表示名（未指定時はファイル名） |
| `agent` | いいえ | 実行エージェント: `ask`, `edit`, `agent`, `plan`, またはカスタムエージェント名 |
| `argument-hint` | いいえ | チャット入力欄に表示するヒント |
| `tools` | いいえ | 利用可能なツール一覧 |
| `model` | 推奨 | 使用するAIモデル（配列形式も可） |

**変数構文**: プロンプト本文内で `${variableName}` 構文を使用可能

| 変数 | 説明 |
|------|------|
| `${workspaceFolder}` | ワークスペースルート |
| `${file}` | 現在のファイルパス |
| `${fileBasename}` | ファイル名 |
| `${selection}` / `${selectedText}` | 選択テキスト |
| `${input:name}` | ユーザー入力を要求 |
| `${input:name:placeholder}` | プレースホルダー付きユーザー入力 |

**ツール参照**: 本文内で `#tool:<tool-name>` 構文でツールを参照可能

**ファイル参照**: 相対パスで他ファイルを参照可能

**テンプレート**: [prompt-template.md](./templates/prompt-template.md)

**例**:
```markdown
---
description: 'コードレビューを実行し、品質とセキュリティの問題を特定'
agent: 'ask'
name: 'code-review'
argument-hint: 'レビュー対象のファイルまたはコードを入力'
tools: ['codebase', 'problems']
model: 'claude-sonnet-4.5'
---

# コードレビュープロンプト

${file} について、以下の観点でレビューを実行してください:

1. コード品質
2. セキュリティ脆弱性（#tool:codebase で関連コードを確認）
3. パフォーマンス最適化の機会
4. ベストプラクティス遵守

レビュー結果は優先度別に整理して報告してください。
```

### 2. エージェントファイル（.agent.md）

**用途**: 特定タスクに特化した自律エージェント

**作成場所**: `.github/agents/`

**Front matter フィールド**:

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `description` | いいえ | エージェントの説明（シングルクォート） |
| `name` | いいえ | UI表示名（未指定時はファイル名） |
| `argument-hint` | いいえ | チャット入力欄に表示するガイド |
| `tools` | いいえ | 利用可能なツール一覧 |
| `model` | 推奨 | 使用するAIモデル。配列で優先順位付きフォールバック指定も可能 |
| `user-invokable` | いいえ | ドロップダウンに表示するか（デフォルト: `true`） |
| `disable-model-invocation` | いいえ | サブエージェントとしての自動呼び出しを無効化（デフォルト: `false`） |
| `agents` | いいえ | 利用可能なサブエージェント: `['*']`=全て, `[]`=なし, `['name']`=特定のみ |
| `target` | いいえ | 対象環境: `vscode` または `github-copilot` |
| `mcp-servers` | いいえ | MCPサーバー設定（JSON形式、GitHub Copilot向け） |
| `handoffs` | いいえ | エージェント間の遷移定義 |

> **非推奨**: `infer` フィールドは非推奨です。代わりに `user-invokable` と `disable-model-invocation` を使用してください。

**テンプレート**: [agent-template.md](./templates/agent-template.md)

**例**:
```markdown
---
description: 'TypeScript MCPサーバー開発の専門アシスタント'
name: 'typescript-mcp-expert'
argument-hint: 'MCPサーバーに関する質問や要望を入力'
tools: ['codebase', 'terminalCommand', 'editFiles', 'search']
model: ['claude-sonnet-4.5', 'GPT-4o']
user-invokable: true
agents: ['*']
target: 'vscode'
handoffs:
  - label: 'プロンプトを実行'
    agent: 'generate-typescript-mcp-server'
    prompt: 'TypeScript MCPサーバーの包括的な実装を生成'
    send: false
---

# TypeScript MCP エキスパート

あなたは、TypeScript SDKを使用してMCPサーバーを構築する専門家です。

## 専門領域
- TypeScript/Node.js開発
- zodバリデーション
- Express統合
- MCP Inspector使用
```

### 3. インストラクションファイル（.instructions.md）

**用途**: ファイルタイプ別のコーディング規約・ガイドライン

**作成場所**: `.github/instructions/`

**Front matter フィールド**:

| フィールド | 必須 | 説明 |
|-----------|------|------|
| `description` | いいえ | インストラクションの説明（シングルクォート） |
| `name` | いいえ | UI表示名（未指定時はファイル名） |
| `applyTo` | いいえ | 適用対象ファイルパターン（globパターン）。未指定時はセマンティックマッチングまたは手動追加 |

**インストラクションの種類**:

| 種類 | ファイル | 適用方法 |
|------|---------|---------|
| ファイルベース | `.instructions.md` | `applyTo` globパターンまたはセマンティックマッチング |
| プロジェクト全体 | `.github/copilot-instructions.md` | すべてのチャットリクエストに自動適用 |
| AGENTS.md | `AGENTS.md`（ワークスペースルート） | 常時オン。複数AIエージェントで認識 |
| 組織レベル | GitHub組織設定 | `github.copilot.chat.organizationInstructions.enabled` で有効化 |

**優先順位**（高→低）: 個人レベル → リポジトリレベル → 組織レベル

**AGENTS.md**: ワークスペースルートに `AGENTS.md` を配置すると、Copilot・Claude等の複数AIエージェントで認識される常時オンインストラクションとして機能します。サブフォルダへのネスト配置は `chat.useNestedAgentsMdFiles` 設定（実験的機能）で有効化できます。

**自動生成**: `/init` コマンドまたは「Generate Chat Instructions」で自動生成可能

**Markdownリンク参照**: `chat.includeReferencedInstructions` 設定で、インストラクション内のMarkdownリンク先も自動取り込み可能

> **非推奨**: `codeGeneration` および `testGeneration` 設定（v1.102+）は非推奨です。ファイルベースのインストラクションを使用してください。

**テンプレート**: [instructions-template.md](./templates/instructions-template.md)

**例**:
```markdown
---
description: 'Python MCPサーバー開発のコーディング規約'
name: 'python-mcp-guidelines'
applyTo: '**/*.py, **/pyproject.toml'
---

# Python MCP Server 開発ガイドライン

## 必須要件

- Python 3.10以上を使用
- uvでプロジェクト管理
- 型ヒントは必須
- Pydanticモデルで構造化出力

[共通コーディング規約](./general-coding.instructions.md)
```

### 4. Agent Skills（SKILL.md）

**用途**: ツール、スクリプト、リソースを含む専門的なワークフロー

**作成場所**:

| レベル | パス |
|--------|------|
| リポジトリ | `.github/skills/<skill-name>/SKILL.md` |
| パーソナル（推奨） | `~/.copilot/skills/<skill-name>/SKILL.md` |
| パーソナル（レガシー） | `~/.claude/skills/<skill-name>/SKILL.md` |
| カスタム | `chat.agentSkillsLocations` 設定で任意のパスを指定 |

**必須Front matter**:
- `name`: スキル名（小文字、ハイフン区切り、最大64文字）
- `description`: スキルの説明（最大1024文字）

**構造**:
```
.github/skills/
  └── my-skill/
      ├── SKILL.md          # メインスキルファイル
      ├── templates/        # テンプレートファイル
      ├── examples/         # サンプルコード
      └── scripts/          # 実行スクリプト
```

**3段階ローディング**:
1. **スキル検出**: `name` と `description` のみ読み込み
2. **インストラクションロード**: リクエストに関連する場合、SKILL.md本文をロード
3. **リソースアクセス**: 参照されたファイルをオンデマンドでロード

**テンプレート**: [skill-template.md](./templates/skill-template.md)

## ビルトインツール一覧

プロンプト・エージェントで使用可能なビルトインツール:

| ツール名 | 説明 |
|---------|------|
| `codebase` | ワークスペースのコードを検索・分析 |
| `search` | ファイル・コード検索 |
| `editFiles` | ファイル編集 |
| `terminalCommand` | ターミナルコマンド実行 |
| `terminalLastCommand` | 直前のターミナルコマンド参照 |
| `fetch` | Webコンテンツ取得 |
| `githubRepo` | GitHubリポジトリアクセス |
| `problems` | ワークスペースの診断・問題表示 |
| `changes` | ファイル変更の確認 |
| `usages` | シンボルの参照箇所検索 |

**MCPツール**: `<server-name>/*` 形式でMCPサーバーの全ツールを指定可能

```yaml
tools: ['codebase', 'my-mcp-server/*']
```

**ツールセット**: `.jsonc` ファイルでツールをグループ定義可能

```json
{
  "reader": {
    "tools": ["changes", "codebase", "problems", "usages"],
    "description": "コード分析用ツールセット",
    "icon": "book"
  }
}
```

**ツール参照構文**: プロンプト本文内で `#tool:<tool-name>` を使用してツールを明示的に参照

## ベストプラクティス

### 命名規約

**ファイル名**: 小文字、ハイフン区切り
- `generate-mcp-server.prompt.md`
- `python-best-practices.instructions.md`

**スキル名**: 小文字、ハイフン区切り、最大64文字
- `python-mcp-development`
- `cpp14-code-review`

### Description フィールド

**要件**:
- シングルクォートで囲む
- 空でない
- 明確で具体的
- 使用タイミングを含める

**良い例**:
```yaml
description: 'Guide for building MCP servers using Python SDK. Use this when creating, debugging, or optimizing Python-based MCP servers.'
```

**悪い例**:
```yaml
description: "Python MCP"  # ダブルクォート、詳細不足
```

### Model 指定（推奨）

単一モデル:
```yaml
model: 'claude-sonnet-4.5'
```

優先順位付きフォールバック（エージェント）:
```yaml
model: ['Claude Opus 4.5', 'GPT-4o']
```

### 非推奨フィールドの移行

| 旧フィールド | 新フィールド | 説明 |
|-------------|-------------|------|
| `infer: true` | `user-invokable: true` + `disable-model-invocation: false` | デフォルト動作 |
| `infer: false` | `user-invokable: false` + `disable-model-invocation: true` | 非表示・呼び出し不可 |

## 段階的作成ワークフロー

### ステップ1: 要件定義

```markdown
質問:
- 何を自動化したいか?
- 誰が使うか?
- どのファイルに適用するか?
- 必要なツールは?
```

### ステップ2: ファイルタイプ選択

| タイプ | 用途 |
|--------|------|
| `.prompt.md` | 単発の質問・タスク |
| `.agent.md` | 複雑な自律タスク |
| `.instructions.md` | ファイル別のコーディング規約 |
| `AGENTS.md` | 常時オンの全体ルール（複数AIエージェント対応） |
| `SKILL.md` | スクリプト・リソースを含む専門ワークフロー |

### ステップ3: テンプレート使用

[テンプレート集](./templates/)から適切なテンプレートを選択し、カスタマイズ。

### ステップ4: 検証

- [ ] Front matterが正しいYAML形式
- [ ] 必須フィールドが存在（`description`など）
- [ ] ファイル名が命名規約に準拠
- [ ] `applyTo`（インストラクション）が正しいglobパターン
- [ ] 非推奨フィールド（`infer`）を使用していない
- [ ] 説明が具体的で有用

### ステップ5: テスト

- プロンプト: VS Code Copilot Chatで `/`（スラッシュコマンド）を実行
- エージェント: ドロップダウンから選択して呼び出し
- インストラクション: 対象ファイルで動作確認（診断ビューで確認可能）
- スキル: Copilot Chatが自動的にスキルを提案することを確認

> **診断方法**: チャットビューで右クリック → 「Diagnostics」を選択して、読み込まれたインストラクションを確認

## 実装例

### 例1: テストコード生成プロンプト

[generate-tests.prompt.md](./templates/examples/generate-tests.prompt.md)

### 例2: ドキュメント生成エージェント

[doc-generator.agent.md](./templates/examples/doc-generator.agent.md)

### 例3: TypeScript コーディング規約

[typescript.instructions.md](./templates/examples/typescript.instructions.md)

### 例4: Python MCP開発スキル

[python-mcp-development/SKILL.md](./templates/examples/python-mcp-skill.md)

## トラブルシューティング

### 問題1: Front matterエラー

**症状**: YAMLパースエラー

**解決策**:
- ダブルクォートではなくシングルクォートを使用
- インデントをスペース2つで統一
- `---`の前後に余計な空白を入れない

### 問題2: プロンプトが認識されない

**症状**: `/`（スラッシュコマンド）で呼び出せない

**解決策**:
- ファイル名が`.prompt.md`で終わるか確認
- `.github/prompts/`ディレクトリに配置
- VS Code を再読み込み
- `chat.promptFilesLocations` 設定でカスタムパスを追加している場合はパスを確認

### 問題3: インストラクションが適用されない

**症状**: 対象ファイルで有効にならない

**解決策**:
- `applyTo`のglobパターンを確認（`'**/*.py'`など）
- 複数パターンはカンマ区切り: `'**/*.ts, **/*.js'`
- チャットビューで右クリック → 「Diagnostics」で読み込み状況を確認
- インラインサジェスト（入力補完）にはインストラクションは適用されないことに注意

### 問題4: infer フィールドが動作しない

**症状**: `infer` フィールドを設定しても期待通りに動作しない

**解決策**:
`infer` は非推奨です。以下の2つのフィールドに移行してください:
- `user-invokable`: ドロップダウン表示の制御（デフォルト: `true`）
- `disable-model-invocation`: サブエージェントとしての自動呼び出し無効化（デフォルト: `false`）

## 関連設定

| 設定 | 説明 |
|------|------|
| `chat.promptFilesLocations` | プロンプトファイルの追加検索パス |
| `chat.agentFilesLocations` | エージェントファイルの追加検索パス |
| `chat.instructionsFilesLocations` | インストラクションファイルの追加検索パス |
| `chat.agentSkillsLocations` | スキルファイルの追加検索パス |
| `chat.useAgentsMdFile` | AGENTS.md サポートの有効化 |
| `chat.useNestedAgentsMdFiles` | ネストされたAGENTS.md（実験的） |
| `chat.includeReferencedInstructions` | Markdownリンク参照インストラクションの取り込み |
| `chat.promptFilesRecommendations` | プロンプト推奨表示 |
| `github.copilot.chat.organizationInstructions.enabled` | 組織レベルインストラクション |

## 参考リソース

- [VS Code プロンプトファイル](https://code.visualstudio.com/docs/copilot/customization/prompt-files)
- [VS Code カスタムエージェント](https://code.visualstudio.com/docs/copilot/customization/custom-agents)
- [VS Code カスタムインストラクション](https://code.visualstudio.com/docs/copilot/customization/custom-instructions)
- [VS Code Agent Skills](https://code.visualstudio.com/docs/copilot/customization/agent-skills)
- [VS Code Agent Tools](https://code.visualstudio.com/docs/copilot/agents/agent-tools)
- [Awesome Copilot](https://github.com/github/awesome-copilot)
- [テンプレート集](./templates/)

## 次のステップ

1. [テンプレート](./templates/)から開始
2. 要件に応じてカスタマイズ
3. `.github/`配下の適切なディレクトリに配置
4. VS Code Copilot Chatでテスト（診断ビューで確認）
5. チーム・コミュニティと共有
