---
description: 'エージェントの説明（シングルクォート）'
name: 'agent-name'  # オプション: UI表示名（未指定時はファイル名）
argument-hint: 'ユーザー入力のヒント'  # オプション: チャット入力欄に表示するガイド
tools: ['codebase', 'terminalCommand', 'editFiles', 'search']  # オプション
model: 'claude-sonnet-4.5'  # 推奨: 単一モデル名、または優先順位付き配列 ['Claude Opus 4.5', 'GPT-4o']
user-invokable: true  # オプション: ドロップダウンに表示するか（デフォルト: true）
disable-model-invocation: false  # オプション: サブエージェントとしての自動呼び出しを無効化（デフォルト: false）
agents: ['*']  # オプション: 利用可能なサブエージェント（'*' = すべて, [] = なし, ['agent1'] = 特定のみ）
target: 'vscode'  # オプション: vscode または github-copilot
mcp-servers:  # オプション: MCPサーバー設定（GitHub Copilot向け）
  example-server:
    command: 'npx'
    args: ['-y', '@modelcontextprotocol/server-example']
handoffs:  # オプション: エージェント間の遷移定義
  - label: '関連プロンプトを実行'
    agent: 'agent-name'
    prompt: 'プロンプトの説明'
    send: false
    model: 'GPT-4o (copilot)'  # オプション: 遷移先モデル（qualified name）
---

# エージェント名

エージェントの詳細な説明と役割を記述します。

## 専門領域

- 専門分野1
- 専門分野2
- 専門分野3

## 能力

### 主要機能1

機能の詳細説明

### 主要機能2

機能の詳細説明

## ワークフロー

```
ユーザー入力 → 分析 → 実装 → 検証 → 報告
```

## 使用例

具体的な使用ケースを記述。

## 関連リソース

- リソース1へのリンク
- リソース2へのリンク

## 非推奨フィールドの移行ガイド

`infer` フィールドは非推奨です。以下の2つのフィールドに置き換えてください:

| 旧フィールド | 新フィールド | 説明 |
|-------------|-------------|------|
| `infer: true` | `user-invokable: true` + `disable-model-invocation: false` | ドロップダウンに表示し、サブエージェントとしても利用可能（デフォルト動作） |
| `infer: false` | `user-invokable: false` + `disable-model-invocation: true` | ドロップダウンに非表示、サブエージェントとしても利用不可 |
