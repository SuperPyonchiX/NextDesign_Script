IAsyncValidationContext インタフェース
===============================

名前空間: NextDesign.Core

説明​
-----------------------

非同期検証のコンテキストです。

所属エリア​
--------------------------------

名前

説明

[モデル](_extension_api_overview_model.md)

モデルにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[CancellationToken](_extension_api_NextDesign.Core_IAsyncValidationContext_properties_CancellationToken.md)

キャンセルトークンを取得します。

[CancellationTokenSource](_extension_api_NextDesign.Core_IAsyncValidationContext_properties_CancellationTokenSource.md)

キャンセルトークンのソースオブジェクトを取得します。

[IsCancelRequested](_extension_api_NextDesign.Core_IAsyncValidationContext_properties_IsCancelRequested.md)

コンテキストで実行中の検証に対してキャンセルが要求されているか調べます。

[Options](_extension_api_NextDesign.Core_IAsyncValidationContext_properties_Options.md)

検証オプションを取得します。  
この検証オプションはコンテキスト生成時に指定された検証オプションのスナップショットです。  
ここで取得したオプションに対して設定を変更しても検証処理には反映されません。

[Result](_extension_api_NextDesign.Core_IAsyncValidationContext_properties_Result.md)

検証結果を取得します。  
ただし、検証未実行、または実行中の場合は null を返します。

[State](_extension_api_NextDesign.Core_IAsyncValidationContext_properties_State.md)

検証の実行状態を取得します。

[TargetModel](_extension_api_NextDesign.Core_IAsyncValidationContext_properties_TargetModel.md)

検証対象の起点モデルを取得します。

メソッド​
-----------------------------

名前

説明

[Cancel](_extension_api_NextDesign.Core_IAsyncValidationContext_methods_Cancel.md)

コンテキストで実行中の検証をキャンセルします。

[RegisterOnFinish](_extension_api_NextDesign.Core_IAsyncValidationContext_methods_RegisterOnFinish.md)

検証終了時に1回だけ実行する追加の検証アクションを登録します。

[RegisterOnModelValidate](_extension_api_NextDesign.Core_IAsyncValidationContext_methods_RegisterOnModelValidate.md)

モデルの検証時に実行する追加の検証アクションを登録します。

[RegisterOnStart](_extension_api_NextDesign.Core_IAsyncValidationContext_methods_RegisterOnStart.md)

検証開始時に1回だけ実行する追加の検証アクションを登録します。

[ValidateAsync](_extension_api_NextDesign.Core_IAsyncValidationContext_methods_ValidateAsync.md)

非同期で検証します。  
検証の詳細は、IModel の Validate(ValidationOptions options) メソッドを参照してください。