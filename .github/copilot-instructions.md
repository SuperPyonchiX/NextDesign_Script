# Next Design エクステンション スクリプト開発

本プロジェクトは、Next Designのエクステンションをスクリプト（C#）で開発するためのリポジトリです。
`src/main.cs` にスクリプトを実装し、`src/manifest.json` でリボンUI・コマンド・イベントの拡張ポイントを定義します。
API仕様は `nextdesign_api/` ディレクトリに約1,940ファイルのMarkdownドキュメントとして整理されています。

## プロジェクト構成

```
Script/
├── .github/
│   ├── copilot-instructions.md    # AIアシスタント向けプロジェクト指示
│   └── skills/                    # Copilot用スキル定義
├── nextdesign_api/                # Next Design API リファレンス（参照専用・約1,940ファイル）
│   ├── _extension_api_intro.md                                        # API全体の入口
│   ├── _extension_api_overview_{area}.md                               # エリア別概要
│   ├── _extension_api_{Namespace}_{Interface}.md                       # インタフェース定義
│   ├── _extension_api_{Namespace}_{Interface}_methods_{Method}.md      # メソッド詳細
│   └── _extension_api_{Namespace}_{Interface}_properties_{Property}.md # プロパティ詳細
├── src/
│   ├── main.cs                    # エクステンションのメインスクリプト（エントリポイント）
│   └── manifest.json              # エクステンション定義（UI・コマンド・イベント）
└── .gitignore
```

## 技術スタック

- **言語**: C# スクリプト (.cs) — Next Designのスクリプトエンジンで実行時コンパイルされる
- **設定**: manifest.json（UTF-8、アイコンはPNG形式）
- **名前空間**:
  - `NextDesign.Core` — モデル、プロジェクト、エディタ、図形などのデータ操作API
  - `NextDesign.Desktop` — アプリケーション、ワークスペース、UI、コマンドコンテキストなどのアプリケーション操作API
  - `NextDesign.Extension` — エクステンション実装用インタフェース（IExtension、IEventDispatcher、ICommandDispatcher）
- **利用可能な標準ライブラリ**: `System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Text`, `System.Text.RegularExpressions`

## エクステンションの基本構造

### manifest.json の構造

manifest.json はエクステンションの定義ファイルです。`extensionPoints` でUI・コマンド・イベントを定義します。

```json
{
  "name": "エクステンション名",
  "version": "1.0.0",
  "description": "エクステンションの説明",
  "author": "作者名",
  "main": "main.cs",
  "lifecycle": "application",
  "extensionPoints": {
    "ribbon": {
      "tabs": [
        {
          "id": "MyExtension.Tab",
          "label": "タブ名",
          "groups": [
            {
              "id": "MyExtension.Group",
              "label": "グループ名",
              "controls": [
                {
                  "id": "MyExtension.Button",
                  "type": "Button",
                  "label": "ボタン名",
                  "tooltip": "ツールチップ",
                  "command": "MyExtension.CommandId"
                }
              ]
            }
          ]
        }
      ]
    },
    "commands": [
      {
        "id": "MyExtension.CommandId",
        "name": "コマンド名",
        "execFunc": "HandlerMethodName"
      }
    ],
    "events": {
      "models": [
        {
          "id": "MyExtension.OnFieldChanged",
          "name": "フィールド変更時処理",
          "type": "onFieldChanged",
          "execFunc": "OnFieldChangedHandler"
        }
      ]
    }
  }
}
```

**重要**: `commands[].execFunc` の値は、`main.cs` の **publicメソッド名** と完全一致する必要があります。

### コマンドハンドラの実装パターン

すべてのコマンドハンドラは以下の固定シグネチャで実装します:

```csharp
public void HandlerMethodName(ICommandContext context, ICommandParams parameters)
{
    // ICommandContext は IContext を継承
    // context.App       — IApplication（アプリケーション全体へのアクセス）
    // context.Command   — ICommand（実行中のコマンド定義）
    // context.SenderModel — IModel（コマンドがモデルに紐づく場合はそのモデル、それ以外はnull）

    try
    {
        // 処理を実装
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}
```

### イベントハンドラの実装パターン

manifest.json の `extensionPoints.events` で定義されたイベントに対応するハンドラを実装します。

**8つのイベントエリア:**

| エリア | 定義名 | 説明 | 主なイベント |
|--------|--------|------|------------|
| アプリケーション | `application` | アプリ起動・終了 | `onAfterStart`, `onBeforeQuit` |
| コマンド | `commands` | コマンド実行 | `onBeforeExecute`, `onAfterExecute` |
| プロジェクト | `project` | プロジェクト操作 | `onAfterOpen`, `onBeforeClose`, `onAfterSave` |
| モデル | `models` | モデルCRUD・選択 | `onBeforeNew`, `onAfterNew`, `onFieldChanged`, `onBeforeDelete`, `onValidate`, `onSelectionChanged` |
| エディタ | `editors` | エディタ表示・選択 | `onShow`, `onHide`, `onSelectionChanged` |
| ページ | `pages` | ページ切替 | `onBeforeChange`, `onAfterChange` |
| ナビゲータ | `navigators` | ナビゲータ操作 | `onShow`, `onHide`, `onSelectionChanged` |
| 情報ウィンドウ | `informations` | 情報ウィンドウ | `onShow`, `onHide`, `onSelectionChanged`, `onDoubleClick` |

## グローバルオブジェクト

スクリプト内で直接使用できるグローバルオブジェクト:

| オブジェクト | 型 | 説明 |
|-------------|-----|------|
| `App` | `IApplication` | アプリケーション全体へのアクセス（Commands, Env, Errors, Output, Workspace, Window等） |
| `Context` | `IContext` | エクステンション実行コンテキスト（共有変数、エクステンション設定情報） |
| `Errors` | `IErrors` | モデル検証エラー情報 |
| `Output` | `IOutput` | 出力ウィンドウへの書き込み |
| `Search` | `ISearchManager` | 検索マネージャ |
| `Window` | `IWorkspaceWindow` | アプリケーションウィンドウ |
| `Workspace` | `IWorkspace` | ワークスペース（プロジェクト操作、エディタ状態管理） |
| `CurrentModel` | `IModel` | 現在選択中のモデル |
| `CurrentProject` | `IProject` | 現在開いているプロジェクト |
| `EditorPage` | `IEditorPage` | エディタページUI |
| `ViewDefinitions` | `IViewDefinitions` | ビュー定義 |
| `UI` | `ICommonUI` | ダイアログなどの共通UI |

## API リファレンスの探し方

`nextdesign_api/` ディレクトリには約1,940ファイルのAPIドキュメントがあります。以下の命名規則に従ってファイルを特定してください。

### ファイル命名規則

| 種類 | 命名パターン | 例 |
|------|-------------|-----|
| API全体入口 | `_extension_api_intro.md` | — |
| エリア概要 | `_extension_api_overview_{area}.md` | `_extension_api_overview_global.md` |
| イベント概要 | `_extension_api_overview_events_{area}.md` | `_extension_api_overview_events_models.md` |
| イベント詳細 | `_extension_api_overview_events_{area}_{event}.md` | `_extension_api_overview_events_models_onFieldChanged.md` |
| インタフェース | `_extension_api_{Namespace}_{Interface}.md` | `_extension_api_NextDesign.Core_IModel.md` |
| メソッド | `_extension_api_{Namespace}_{Interface}_methods_{Method}.md` | `_extension_api_NextDesign.Core_IModel_methods_GetField.md` |
| プロパティ | `_extension_api_{Namespace}_{Interface}_properties_{Prop}.md` | `_extension_api_NextDesign.Core_IModel_properties_Name.md` |
| オーバーロード | サフィックス `-1`, `-2`, `-3` | `_extension_api_NextDesign.Core_IModel_methods_As-1.md` |

### 名前空間と主要インタフェース

**NextDesign.Core（モデル・データ）:**

| インタフェース | 説明 |
|---------------|------|
| `IModel` | モデル要素（フィールド操作、子要素操作、検証など100以上のメソッド） |
| `IProject` | プロジェクト（トランザクション管理、プロファイル、モデル検索） |
| `IRelationship` | モデル間の関連 |
| `IClass` | メタクラス定義（フィールド定義、継承、制約） |
| `IField` | フィールド定義（型、多重度、埋込/参照の区別） |
| `IEditor` | エディタ基底（派生: IDiagram, IForm, ITreeGrid, ISequenceDiagram, ICustomEditor） |
| `IDiagram` | ダイアグラムエディタ（図形操作、レイアウト） |
| `IShape` | 図形要素（派生: INode, IConnector） |
| `IForm` | フォームエディタ |
| `ITreeGrid` | ツリーグリッドエディタ |
| `IModelUnit` | モデルユニット（物理ファイル情報） |

**NextDesign.Desktop（アプリケーション・UI）:**

| インタフェース | 説明 |
|---------------|------|
| `IApplication` | アプリケーション全体（Workspace, Window, Output, UI, Env等へのアクセス） |
| `IContext` | 実行コンテキスト（共有変数管理） |
| `ICommandContext` | コマンド実行コンテキスト（IContext継承、Command, SenderModel） |
| `ICommandParams` | コマンドパラメータ |
| `IWorkspace` | ワークスペース（プロジェクト操作、Undo/Redo） |
| `IWorkspaceWindow` | アプリケーションウィンドウ |
| `IEditorPage` | エディタページUI |
| `INavigator` | ナビゲータ（モデル、プロジェクト等のツリー表示） |
| `ICommonUI` | 共通UI（ダイアログ表示） |
| `IOutput` | 出力ウィンドウ |
| `IEnv` | 実行環境情報（バージョン、言語、パス） |

### 主要APIエリア一覧

| エリア | 概要ファイル | 説明 |
|--------|-------------|------|
| グローバル | `_extension_api_overview_global.md` | 実行環境、コンテキスト |
| コマンド | `_extension_api_overview_commands.md` | コマンド管理 |
| イベント | `_extension_api_overview_events_intro.md` | イベント一覧と各エリアの詳細 |
| ワークスペース・プロジェクト | `_extension_api_overview_workspace.md` | プロジェクト操作、状態管理 |
| プロファイル | `_extension_api_overview_profile.md` | メタモデル、ビュー定義 |
| モデル | `_extension_api_overview_model.md` | モデル操作、検証 |
| インタラクションモデル | `_extension_api_overview_interaction-model.md` | シーケンス図のモデル（ライフライン、メッセージ） |
| エディタ | `_extension_api_overview_editors.md` | フォーム、ダイアグラム、ツリーグリッド |
| シーケンスエディタ | `_extension_api_overview_sequence-editor.md` | シーケンス図のビジュアル要素 |
| ユーザインタフェース | `_extension_api_overview_interfaces.md` | ウィンドウ、ナビゲータ、ダイアログ |
| ユーティリティ | `_extension_api_overview_utility.md` | ファイル操作、テキスト構築 |
| 検索・エラー・出力 | `_extension_api_overview_errors.md` | エラー管理、検索、出力ウィンドウ |
| モデル差分 | `_extension_api_overview_model-diff.md` | モデル比較 |
| プロダクトライン | `_extension_api_overview_productline.md` | プロダクトラインモデル |
| チーム開発(SCM) | `_extension_api_overview_scm.md` | 構成管理連携 |
| トレーサビリティ | `_extension_api_overview_traceability.md` | トレーサビリティ情報 |
| 編集機能拡張 | `_extension_api_overview_editing-capability.md` | モデル作成・編集のカスタマイズ |
| カスタムUI | `_extension_api_overview_custom-ui.md` | カスタムエディタ、ナビゲータ、インスペクタ |
| ドキュメント生成 | `_extension_api_overview_documents.md` | Word/HTMLドキュメント生成 |

### API検索の推奨手順

1. **概要ファイルで該当エリアを特定**: `_extension_api_overview_{area}.md` を読み、必要なインタフェースを特定する
2. **インタフェース定義でメソッド/プロパティ一覧を確認**: `_extension_api_{Namespace}_{Interface}.md` でAPI一覧を把握する
3. **個別メソッドの詳細を確認（引数・戻り値・例外）**: `_extension_api_{Namespace}_{Interface}_methods_{Method}.md` で正確なシグネチャと使用上の注意を確認する

## よく使うAPIパターン

### モデル操作

```csharp
// フィールド値の取得・設定
var value = model.GetField("FieldName");
model.SetField("FieldName", "新しい値");

// 子モデルの追加
var newChild = parentModel.AddNewModel("FieldName", "ClassName");

// 子モデルの検索（再帰的にクラス名で検索）
var children = model.FindChildrenByClass("ClassName", true);

// 全子要素の取得
var allChildren = model.GetAllChildren();

// モデルの削除
model.Delete();

// 関連の作成
sourceModel.Relate("RelationFieldName", targetModel);
```

API参照: `nextdesign_api/_extension_api_NextDesign.Core_IModel.md`

### UI操作

```csharp
// 通知ダイアログ（第1引数: message, 第2引数: caption）
UI.ShowInformationDialog("メッセージ", "タイトル");

// 確認ダイアログ（OK=true / Cancel=false、第1引数: message, 第2引数: caption）
bool result = UI.ShowConfirmDialog("確認しますか？", "タイトル");

// ファイル選択ダイアログ（キャンセル時はnull）
string filePath = UI.ShowOpenFileDialog("ファイルを選択", "テキストファイル|*.txt");
string savePath = UI.ShowSaveFileDialog("保存先を選択", "JSONファイル|*.json");

// フォルダ選択ダイアログ（キャンセル時はnull）
string folderPath = UI.ShowSelectFolderDialog("フォルダを選択");
```

API参照: `nextdesign_api/_extension_api_NextDesign.Desktop_ICommonUI.md`

### 出力ウィンドウ

```csharp
// カテゴリ付きメッセージ出力（カテゴリ・メッセージともにnull不可）
Output.WriteLine("カテゴリ名", "メッセージ");

// フォーマット付き出力
Output.WriteFormatLine("カテゴリ名", "値: {0}, 件数: {1}", value, count);

// 出力クリア
Output.Clear("カテゴリ名");
Output.ClearAll();
```

API参照: `nextdesign_api/_extension_api_NextDesign.Desktop_IOutput.md`

### トランザクション管理

モデルを変更する場合は、アンドゥトランザクションで囲みます:

```csharp
// autoCommit=true の場合、Commit/Rollbackを呼ばずにスコープを抜けると自動コミット
var transaction = CurrentProject.BeginUndoTransaction(true);
try
{
    // モデル変更操作
    model.SetField("Name", "新しい名前");
    var child = model.AddNewModel("Children", "ChildClass");

    transaction.Commit();
}
catch (Exception ex)
{
    transaction.Rollback();
    Output.WriteLine("エラー", ex.Message);
}
```

API参照: `nextdesign_api/_extension_api_NextDesign.Core_IProject_methods_BeginUndoTransaction.md`

### エディタ・ナビゲータアクセス

```csharp
// 現在のエディタ取得
var editor = Window.EditorPage.CurrentEditor;

// エディタ種別の判定とキャスト
if (editor.EditorType == "ERDiagram" || editor.EditorType == "TreeDiagram")
{
    var diagram = editor as IDiagram;
    var shapes = diagram.Shapes;
}
else if (editor.EditorType == "DocumentForm")
{
    var form = editor as IForm;
}
else if (editor.EditorType == "TreeGrid")
{
    var treeGrid = editor as ITreeGrid;
}
```

API参照: `nextdesign_api/_extension_api_NextDesign.Core_IEditor.md`

## コーディング規約

- **命名規則**: PascalCase（メソッド名、クラス名）、camelCase（ローカル変数、引数名）
- **エラー処理**: ハンドラ本体を try-catch で囲み、`Output.WriteLine` でログ出力、`UI.ShowInformationDialog` でユーザ通知
- **null安全**: APIから取得したオブジェクト（`CurrentModel`, `CurrentProject`, `context.SenderModel` 等）は使用前にnullチェック
- **ハンドラの可視性**: manifest.json から呼び出されるハンドラは必ず **public** で宣言

## 実行・デバッグ

- **ビルド不要**: Next Designのスクリプトエンジンが実行時にC#スクリプトをコンパイルするため、事前ビルドは不要
- **スクリプトエディタ**: Next Design上のスクリプトエディタでスクリプトの実行・デバッグが可能
- **エクステンション配置**: `src/` 配下のファイル（main.cs, manifest.json）をNext Designのエクステンションフォルダに配置して実行

## 参考文書

### 公式マニュアル
- [Next Design エクステンション開発マニュアル](https://docs.nextdesign.app/extension/)
- [スクリプトで開発 - ファイルの準備](https://docs.nextdesign.app/extension/docs/getting-started/dev-with-scripts/manifest)
- [スクリプトで開発 - 拡張ポイントの定義](https://docs.nextdesign.app/extension/docs/getting-started/dev-with-scripts/extension-points)
- [共通 - 拡張ポイントの定義](https://docs.nextdesign.app/extension/docs/getting-started/common/extension-points)
- [スクリプトで開発 - スクリプトによるハンドラの実装](https://docs.nextdesign.app/extension/docs/getting-started/dev-with-scripts/impl-handlers)
- [スクリプトで開発 - グローバルオブジェクト](https://docs.nextdesign.app/extension/docs/getting-started/dev-with-scripts/global-objects)
- [スクリプトで開発 - 実行とデバッグ](https://docs.nextdesign.app/extension/docs/getting-started/dev-with-scripts/debugging)
- [スクリプトで開発 - スクリプトエディタでの実行](https://docs.nextdesign.app/extension/docs/getting-started/dev-with-scripts/run-with-script-editor)
- [スクリプトで開発 - エクステンションの配布](https://docs.nextdesign.app/extension/docs/getting-started/dev-with-scripts/deployment)

### APIリファレンス
- [Next Design API リファレンス（本リポジトリ）](../nextdesign_api/_extension_api_intro.md)

### サンプルコード
- [Next Design エクステンション Samplesリポジトリ](https://github.com/denso-create/NextDesign-Samples)
- [Hello World サンプル](https://github.com/denso-create/NextDesign-Samples/tree/main/extensions/hello-world)
