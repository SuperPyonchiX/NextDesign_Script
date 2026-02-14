---
name: nextdesign-script-development
description: 'Next Designエクステンションのスクリプト（C#）開発を対話的に支援するスキル。ユーザーへのヒアリングを通じてmanifest.jsonとmain.csを段階的に生成し、nextdesign_api/のAPIリファレンス約1,940ファイルを活用して適切なAPI使用をガイドする。「エクステンションを作りたい」「スクリプトを書きたい」「コマンドハンドラを追加したい」と依頼されたときに使用する。'
---

# Next Design エクステンション スクリプト開発スキル

Next Designのエクステンションをスクリプト（C#）で対話的に開発するスキルである。
ユーザーが実現したい機能をヒアリングし、manifest.json（リボンUI・コマンド・イベント定義）とmain.cs（ハンドラ実装）を段階的に生成する。
`nextdesign_api/` 配下の約1,940ファイルのAPIリファレンスを活用し、適切なAPIの選定と使用方法も案内する。

本スキルは `copilot-instructions.md` が提供するAPI知識・コーディング規約を前提とし、**対話的な開発ワークフロー**と**コード生成テンプレート**で補完する。

## 使用タイミング

以下のいずれかに該当する場合にこのスキルを使用する：

- 「エクステンションを作りたい」「スクリプトを書きたい」と依頼されたとき
- manifest.json を新規作成または修正するとき
- main.cs にコマンドハンドラやイベントハンドラを追加するとき
- 既存のエクステンションに新しいリボンボタンやイベント処理を追加するとき
- Next Design APIの使い方について具体的なコード生成を伴う支援が求められたとき
- 「〇〇のようなNext Design拡張機能がほしい」と要望されたとき

## ヒアリングプロセス

エクステンション開発を依頼されたら、一度に全てを聞かず、以下のフェーズに分けて対話的に情報を収集する。各ターンの質問は**最大3つ**までとする。

### フェーズ1：目的と概要の把握

まず以下を確認する：

1. **何を実現したいか**：エクステンションで達成したい機能の概要
2. **トリガー方式**：リボンボタンからの手動実行か、イベント駆動の自動実行か、またはその両方か
3. **操作対象**：モデル操作、UI表示、ファイル入出力、検証など主にどの領域か

### フェーズ2：UI・イベント定義の詳細化

#### リボンボタンが必要な場合

1. **タブ・グループの構成**：既存タブに追加か、新規タブを作成か
2. **ボタンの数と名称**：各ボタンのラベル、ツールチップ
3. **ID命名**：エクステンション名のプレフィックス（例：`MyExtension.Tab`）

#### イベント駆動が必要な場合

1. **イベントエリアの特定**：8つのイベントエリアのどれに該当するか

   | エリア | 定義名 | 主なイベント |
   |--------|--------|------------|
   | アプリケーション | `application` | `onAfterStart`, `onBeforeQuit` |
   | コマンド | `commands` | `onBeforeExecute`, `onAfterExecute` |
   | プロジェクト | `project` | `onAfterOpen`, `onBeforeClose`, `onAfterSave` |
   | モデル | `models` | `onFieldChanged`, `onAfterNew`, `onBeforeDelete`, `onValidate`, `onSelectionChanged` |
   | エディタ | `editors` | `onShow`, `onHide`, `onSelectionChanged` |
   | ページ | `pages` | `onBeforeChange`, `onAfterChange` |
   | ナビゲータ | `navigators` | `onShow`, `onHide`, `onSelectionChanged` |
   | 情報ウィンドウ | `informations` | `onShow`, `onHide`, `onSelectionChanged`, `onDoubleClick` |

2. **イベントタイプの特定**：具体的なイベント種別
3. **発火条件**：どのモデルやクラスに対するイベントか

### フェーズ3：処理ロジックの詳細化

各コマンド/イベントハンドラについて以下を確認する：

1. **入力データ**：ハンドラが受け取る・参照するデータ（CurrentModel、選択要素、ファイルなど）
2. **処理内容**：具体的な操作手順（モデル操作、フィールド値の読み書き、検証など）
3. **出力・結果**：処理結果の通知方法（ダイアログ、出力ウィンドウ、ファイル出力など）

この段階で、処理に必要なAPIを `nextdesign_api/` から検索し、正確なメソッドシグネチャを確認する（後述「APIリファレンス検索ガイド」参照）。

### フェーズ4：コード生成とレビュー

収集した情報をもとに以下を生成し、ユーザーにレビューを依頼する：

1. `manifest.json` — UI・コマンド・イベントの定義
2. `main.cs` — ハンドラの実装

フィードバックを受けて修正を繰り返す。生成後は「品質チェックリスト」でセルフチェックを行う。

## manifest.json 生成ガイド

### 基本フィールド

| フィールド | 必須 | 説明 | 例 |
|-----------|------|------|-----|
| `name` | はい | エクステンション名 | `"モデルエクスポーター"` |
| `version` | はい | バージョン | `"1.0.0"` |
| `description` | はい | 説明 | `"モデルデータをCSVにエクスポートする"` |
| `author` | はい | 作者名 | `"開発チーム"` |
| `main` | はい | エントリポイント | `"main.cs"` |
| `lifecycle` | はい | ライフサイクル | `"application"` |

### extensionPoints 構成ルール

#### ribbon定義

- `tabs` > `groups` > `controls` の階層構造で定義する
- `controls` の `command` はcommands配列の `id` と一致させる
- `type` は `"Button"` を指定する

#### commands定義

- `execFunc` は main.cs の **publicメソッド名** と完全一致させる
- `id` にはエクステンションプレフィックスを付与する

#### events定義

- 8つのイベントエリア（`application`, `commands`, `project`, `models`, `editors`, `pages`, `navigators`, `informations`）
- `type` はイベント種別名（`onFieldChanged` 等）と一致させる
- `execFunc` は main.cs のpublicメソッド名と完全一致させる

### ID命名規約

```
{エクステンション名}.{要素種別}[.{識別子}]

例：
  ModelExporter.Tab
  ModelExporter.Group.Export
  ModelExporter.Button.ExportCsv
  ModelExporter.Command.ExportCsv
  ModelExporter.Event.OnFieldChanged
```

### テンプレート

- [最小構成テンプレート](./templates/manifest-minimal.json) — コマンド1つの最小構成
- [フル構成テンプレート](./templates/manifest-full.json) — 複数コマンド＋イベント定義

## main.cs 生成ガイド

### 基本構造

main.cs はトップレベルにpublicメソッドを定義するスクリプトファイルである。クラス定義やnamespace宣言は不要。Next Designのスクリプトエンジンが実行時にコンパイルする。

### 利用可能なusing文

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NextDesign.Core;
using NextDesign.Desktop;
```

### コマンドハンドラの実装規約

1. **シグネチャ**: `public void HandlerName(ICommandContext context, ICommandParams parameters)`
2. **エラーハンドリング**: 処理本体を try-catch で囲む
3. **エラー通知**: catchブロックで `Output.WriteLine` + `UI.ShowInformationDialog` を使用
4. **トランザクション**: モデル変更時は `BeginUndoTransaction` で囲む
5. **nullチェック**: `CurrentModel`, `CurrentProject`, `context.SenderModel` 等は使用前にnullチェック

### イベントハンドラの実装規約

1. **シグネチャ**: コマンドハンドラと同じ `(ICommandContext context, ICommandParams parameters)`
2. **トランザクション**: イベントハンドラ内でのモデル変更も必ずトランザクションで囲む
3. **API制限**: 一部のイベント（`onFieldChanged` 等）では使用できないAPIがある。APIリファレンスの注釈を確認する

### よくある実装パターン

#### パターン1：選択中モデルの操作

```csharp
public void ProcessCurrentModel(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var model = CurrentModel;
        if (model == null)
        {
            UI.ShowInformationDialog("モデルが選択されていません。", "エラー");
            return;
        }

        var name = model.GetField("Name") as string;
        Output.WriteLine("情報", $"モデル名: {name}");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}
```

#### パターン2：子モデルの一括処理

```csharp
public void ProcessChildren(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var model = CurrentModel;
        if (model == null) return;

        var children = model.FindChildrenByClass("TargetClassName", true);
        var count = 0;
        foreach (var child in children)
        {
            var value = child.GetField("FieldName");
            // 処理を実装
            count++;
        }

        Output.WriteLine("結果", $"{count} 件のモデルを処理しました。");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}
```

#### パターン3：ファイル入出力を伴う処理

```csharp
public void ExportToFile(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var filePath = UI.ShowSaveFileDialog("保存先を選択", "CSVファイル|*.csv");
        if (filePath == null) return;

        var sb = new StringBuilder();
        sb.AppendLine("名前,説明");

        var models = CurrentModel.GetAllChildren();
        foreach (var m in models)
        {
            var name = m.GetField("Name") as string ?? "";
            var desc = m.GetField("Description") as string ?? "";
            sb.AppendLine($"\"{name}\",\"{desc}\"");
        }

        System.IO.File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Output.WriteLine("エクスポート", $"ファイルを保存しました: {filePath}");
        UI.ShowInformationDialog("エクスポートが完了しました。", "完了");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}
```

#### パターン4：検証（バリデーション）処理

```csharp
public void ValidateModel(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var model = CurrentModel;
        if (model == null) return;

        var errors = new List<string>();
        var children = model.FindChildrenByClass("RequirementClass", true);

        foreach (var child in children)
        {
            var name = child.GetField("Name") as string;
            if (string.IsNullOrEmpty(name))
            {
                errors.Add($"名前が未設定: {child.GetField("Id")}");
            }
        }

        if (errors.Count > 0)
        {
            Output.WriteLine("検証結果", $"{errors.Count} 件のエラーが見つかりました。");
            foreach (var err in errors)
            {
                Output.WriteLine("検証エラー", err);
            }
            UI.ShowInformationDialog($"{errors.Count} 件のエラーがあります。\n出力ウィンドウを確認してください。", "検証結果");
        }
        else
        {
            UI.ShowInformationDialog("エラーはありませんでした。", "検証結果");
        }
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}
```

#### パターン5：トランザクション付きモデル変更

```csharp
public void ModifyModels(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var project = CurrentProject;
        if (project == null) return;

        var transaction = project.BeginUndoTransaction(true);
        try
        {
            var model = CurrentModel;
            if (model == null) return;

            model.SetField("Name", "新しい名前");
            var newChild = model.AddNewModel("Children", "ChildClassName");
            newChild.SetField("Name", "子モデル");

            transaction.Commit();
            Output.WriteLine("情報", "モデルを更新しました。");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            throw;
        }
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}
```

### テンプレート

- [コマンドハンドラテンプレート](./templates/handler-command.cs)
- [イベントハンドラテンプレート](./templates/handler-event.cs)
- [main.cs スキャフォールド](./templates/main-scaffold.cs)
- [トランザクションパターン](./templates/pattern-transaction.cs)

## APIリファレンス検索ガイド

`nextdesign_api/` 配下に約1,940ファイルのAPIドキュメントがある。以下の手順で必要なAPIを効率的に特定する。

### ステップ1：目的からエリアを特定する

| やりたいこと | 参照するエリア概要ファイル |
|-------------|------------------------|
| モデルの読み書き・操作 | `_extension_api_overview_model.md` |
| ダイアログ表示・ユーザー入力 | `_extension_api_overview_interfaces.md` |
| 出力ウィンドウへの書き込み | `_extension_api_overview_errors.md` |
| プロジェクト操作・トランザクション | `_extension_api_overview_workspace.md` |
| エディタ・ダイアグラム操作 | `_extension_api_overview_editors.md` |
| イベント処理の実装 | `_extension_api_overview_events_intro.md` |
| メタモデル・プロファイル参照 | `_extension_api_overview_profile.md` |
| ファイル操作ユーティリティ | `_extension_api_overview_utility.md` |
| ドキュメント生成（Word/HTML） | `_extension_api_overview_documents.md` |
| トレーサビリティ | `_extension_api_overview_traceability.md` |
| カスタムUI | `_extension_api_overview_custom-ui.md` |

### ステップ2：インタフェースを特定する

エリア概要ファイルを読み、目的に合ったインタフェースを特定する。

**主要インタフェース早見表:**

| インタフェース | ファイル名パターン | 用途 |
|--------------|-------------------|------|
| `IModel` | `_extension_api_NextDesign.Core_IModel*` | モデル要素の操作 |
| `IProject` | `_extension_api_NextDesign.Core_IProject*` | プロジェクト操作 |
| `ICommonUI` | `_extension_api_NextDesign.Desktop_ICommonUI*` | ダイアログ表示 |
| `IOutput` | `_extension_api_NextDesign.Desktop_IOutput*` | 出力ウィンドウ |
| `IDiagram` | `_extension_api_NextDesign.Core_IDiagram*` | ダイアグラム操作 |
| `IEditor` | `_extension_api_NextDesign.Core_IEditor*` | エディタ基底 |
| `IShape` | `_extension_api_NextDesign.Core_IShape*` | 図形要素 |
| `IApplication` | `_extension_api_NextDesign.Desktop_IApplication*` | アプリケーション |
| `IWorkspace` | `_extension_api_NextDesign.Desktop_IWorkspace*` | ワークスペース |
| `INavigator` | `_extension_api_NextDesign.Desktop_INavigator*` | ナビゲータ |

### ステップ3：メソッド/プロパティの詳細を確認する

ファイル命名規則に従って個別のメソッド・プロパティのドキュメントを読む：

```
インタフェース定義:    _extension_api_{Namespace}_{Interface}.md
メソッド詳細:          _extension_api_{Namespace}_{Interface}_methods_{Method}.md
プロパティ詳細:        _extension_api_{Namespace}_{Interface}_properties_{Property}.md
オーバーロード:        末尾に -1, -2, -3 のサフィックス
```

### Globパターンによる検索例

必要なAPIを探す際は以下のGlobパターンを活用する：

```
# IModelの全メソッドを一覧
nextdesign_api/_extension_api_NextDesign.Core_IModel_methods_*.md

# IModelの全プロパティを一覧
nextdesign_api/_extension_api_NextDesign.Core_IModel_properties_*.md

# 特定メソッド名で検索（例：Getで始まるメソッド）
nextdesign_api/_extension_api_NextDesign.Core_IModel_methods_Get*.md

# イベント詳細を検索（モデルイベント）
nextdesign_api/_extension_api_overview_events_models_*.md

# 名前空間全体を検索
nextdesign_api/_extension_api_NextDesign.Desktop_*.md

# 全エリア概要を一覧
nextdesign_api/_extension_api_overview_*.md
```

### API検索の推奨手順

1. **概要ファイルでエリアを特定**: 上記の表から該当するエリア概要ファイルを読む
2. **インタフェース定義で一覧を把握**: `_extension_api_{Namespace}_{Interface}.md` で全メソッド/プロパティの一覧を確認する
3. **個別メソッドの詳細を確認**: `_extension_api_{Namespace}_{Interface}_methods_{Method}.md` で正確なシグネチャ、引数、戻り値、例外を確認する
4. **コードに反映**: 確認したシグネチャをそのまま使用する（推測でメソッド名や引数を記述しない）

## サンプル

完成したエクステンションの例を参照する：

- [Hello World サンプル](./examples/example-hello-world/) — 最小構成のエクステンション（ボタン1つ、ダイアログ表示）
- [モデルエクスポート サンプル](./examples/example-model-export/) — 実践的なエクステンション（モデル走査、CSVエクスポート、トランザクション使用）

## 品質チェックリスト

コード生成後、以下の観点でセルフチェックを行う。

### manifest.json チェック

- [ ] `name`, `version`, `description`, `author`, `main` が全て設定されているか
- [ ] `lifecycle` が `"application"` に設定されているか
- [ ] ribbon > tabs > groups > controls の階層構造が正しいか
- [ ] `commands[].execFunc` がmain.csのpublicメソッド名と一致しているか
- [ ] `commands[].id` がribbon controls の `command` と一致しているか
- [ ] イベント定義がある場合、`events.{area}[].execFunc` がmain.csのメソッド名と一致しているか
- [ ] イベント定義がある場合、`events.{area}[].type` が有効なイベント種別名か
- [ ] IDが命名規約（`{Prefix}.{ElementType}` 形式）に従っているか
- [ ] JSONの構文が正しいか（末尾カンマ、閉じ括弧など）

### main.cs チェック

- [ ] ハンドラメソッドが `public` で宣言されているか
- [ ] コマンドハンドラのシグネチャが `(ICommandContext context, ICommandParams parameters)` か
- [ ] try-catchでエラーハンドリングされているか
- [ ] catchブロックで `Output.WriteLine` と `UI.ShowInformationDialog` が使用されているか
- [ ] モデル変更操作が `BeginUndoTransaction` で囲まれているか
- [ ] APIから取得したオブジェクトのnullチェックが行われているか
- [ ] 命名規則が遵守されているか（PascalCase: メソッド、camelCase: ローカル変数）
- [ ] 使用しているAPIのメソッド名・引数がリファレンスと一致しているか

### 整合性チェック

- [ ] manifest.jsonの全 `execFunc` に対応するpublicメソッドがmain.csに存在するか
- [ ] main.csに未使用のハンドラメソッドが残っていないか
- [ ] ribbon controlsの `command` がcommands配列の `id` と一致しているか

## トラブルシューティング

### 症状：コマンドが実行されない

**原因**：manifest.jsonの `execFunc` とmain.csのメソッド名が一致していない。

**対処法**：

1. manifest.jsonの `commands[].execFunc` の値を確認する
2. main.csで同名の `public` メソッドが定義されているか確認する
3. メソッドの可視性が `public` であることを確認する
4. メソッドのシグネチャが `(ICommandContext context, ICommandParams parameters)` であることを確認する

### 症状：イベントハンドラが発火しない

**原因**：イベントの `type` 指定が誤っている、またはイベントエリアが正しくない。

**対処法**：

1. `events` 配下のエリア名（`models`, `editors` 等）が正しいか確認する
2. `type` がAPIリファレンスに記載されたイベント種別名と一致しているか確認する
3. `lifecycle` が `"application"` になっているか確認する
4. イベント詳細ドキュメント（`nextdesign_api/_extension_api_overview_events_{area}_{event}.md`）で発火条件を確認する

### 症状：モデル操作でエラーが発生する

**原因**：トランザクション未使用、nullアクセス、フィールド名不一致など。

**対処法**：

1. モデル変更操作を `BeginUndoTransaction` で囲んでいるか確認する
2. `CurrentModel` / `CurrentProject` のnullチェックを行っているか確認する
3. フィールド名がプロファイル定義と一致しているか確認する
4. APIリファレンスでメソッドの引数・戻り値を再確認する

### 症状：使いたいAPIのメソッドが見つからない

**原因**：APIリファレンスの検索方法が不適切。

**対処法**：

1. まずインタフェース定義ファイル（`_extension_api_{Namespace}_{Interface}.md`）で全メソッド一覧を確認する
2. メソッド名にオーバーロードがある場合はサフィックス（`-1`, `-2`, `-3`）付きのファイルを確認する
3. 上記「APIリファレンス検索ガイド」のGlobパターンを活用する
4. `_extension_api_intro.md` から全体構造を辿って探す
