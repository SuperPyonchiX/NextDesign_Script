ResultBase インタフェース
==================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

編集支援機能で使用するパラメータオブジェクトの基底クラスです。

所属エリア​
--------------------------------

名前

説明

[EditingCapability](_extension_api_overview_editing-capability.md)

EditingCapabilityにアクセスするAPI群です。

派生先​
--------------------------

名前

説明

[ModelReferableResult](_extension_api_NextDesign.Core_ModelReferableResult.md)

モデル参照時の選択対象の結果を表すオブジェクトです。  
  
Next Designは、この結果オブジェクトを用いて選択対象を構築します。

[ModelRelateResult](_extension_api_NextDesign.Core_ModelRelateResult.md)

モデル関連付け可否の結果を表すオブジェクトです。  
  
Next Designは、この結果オブジェクトを用いて関連付け可否を判定します。  
  
※この結果にCapabilityResults.Failを設定してもエラーとはならず、CapabilityResults.Ignoreと同じ扱いになります。

[ModelEditorCategoriesResult](_extension_api_NextDesign.Core_ModelEditorCategoriesResult.md)

エディタのカテゴリ情報の取得結果を表すオブジェクトです。  
  
※この結果にCapabilityResults.Failを設定してもエラーとはならず、CapabilityResults.Ignoreと同じ扱いになります。

[ModelCreatableResult](_extension_api_NextDesign.Core_ModelCreatableResult.md)

モデル作成時の選択対象の結果を表すオブジェクトです。  
  
Next Designは、この結果オブジェクトを用いて選択対象を構築します。  
  
※この結果にCapabilityResults.Failを設定してもエラーとはならず、モデル作成時の選択対象が空になります。

[ModelEditorSelectionResult](_extension_api_NextDesign.Core_ModelEditorSelectionResult.md)

カテゴリ情報の選択に対して表示すべきモデル取得結果を表すオブジェクトです。  
  
※この結果にCapabilityResults.Failを設定してもエラーとはならず、CapabilityResults.Ignoreと同じ扱いになります。

プロパティ​
--------------------------------

名前

説明

[ErrorMessage](_extension_api_NextDesign.Core_ResultBase_properties_ErrorMessage.md)

エラーメッセージ

[Result](_extension_api_NextDesign.Core_ResultBase_properties_Result.md)

プロバイダの処理結果  
※初期値は「Success(成功)」