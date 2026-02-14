EditingCapability
=================

説明​
-----------------------

EditingCapabilityにアクセスするAPI群です。

エリアに属するAPI​
-----------------------------------------------

名前

説明

[IEditingCapabilityProvider](_extension_api_NextDesign.Core_IEditingCapabilityProvider.md)

プロバイダの基底となるインターフェースです。

[IEditingCapabilityProviderRegistry](_extension_api_NextDesign.Core_IEditingCapabilityProviderRegistry.md)

編集支援を行うプロバイダのレジストリです。  
  
・このレジストリは、プロジェクトと同じライフサイクルとなります。

[IModelCreationProvider](_extension_api_NextDesign.Core_IModelCreationProvider.md)

モデル作成プロバイダです。  
モデル作成時の選択対象をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

[IModelInitializationProvider](_extension_api_NextDesign.Core_IModelInitializationProvider.md)

モデルのインスタンス初期化プロバイダです。  
モデルのインスタンス初期化をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

[IModelInitializationProviderInitializationContext](_extension_api_NextDesign.Core_IModelInitializationProviderInitializationContext.md)

IModelInitializationProviderが対象とするメタモデルを登録するコンテキストです。

[IModelEditorSelectionProvider](_extension_api_NextDesign.Core_IModelEditorSelectionProvider.md)

エディタの選択プロバイダです。  
モデルに対して表示可能なサブエディタ・インスペクタをカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

[IModelReferenceProvider](_extension_api_NextDesign.Core_IModelReferenceProvider.md)

モデル関連付けプロバイダです。  
モデル関連付け時の選択対象、及び関連付け可否をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

[ModelCreatableParams](_extension_api_NextDesign.Core_ModelCreatableParams.md)

モデル作成時の選択対象を決めるためのパラメータです。

[ModelCreatableResult](_extension_api_NextDesign.Core_ModelCreatableResult.md)

モデル作成時の選択対象の結果を表すオブジェクトです。  
  
Next Designは、この結果オブジェクトを用いて選択対象を構築します。  
  
※この結果にCapabilityResults.Failを設定してもエラーとはならず、モデル作成時の選択対象が空になります。

[ModelInitializationParams](_extension_api_NextDesign.Core_ModelInitializationParams.md)

モデルのインスタンスの初期化対象を提供するパラメータです。

[ModelEditorCategoriesParams](_extension_api_NextDesign.Core_ModelEditorCategoriesParams.md)

エディタのカテゴリ情報を取得するパラメータです。

[ModelEditorCategoriesResult](_extension_api_NextDesign.Core_ModelEditorCategoriesResult.md)

エディタのカテゴリ情報の取得結果を表すオブジェクトです。  
  
※この結果にCapabilityResults.Failを設定してもエラーとはならず、CapabilityResults.Ignoreと同じ扱いになります。

[ModelEditorCategory](_extension_api_NextDesign.Core_ModelEditorCategory.md)

エディタのカテゴリ情報です。

[ModelEditorSelectionParams](_extension_api_NextDesign.Core_ModelEditorSelectionParams.md)

カテゴリ情報の選択に対して表示すべきモデルを取得するパラメータです。

[ModelEditorSelectionResult](_extension_api_NextDesign.Core_ModelEditorSelectionResult.md)

カテゴリ情報の選択に対して表示すべきモデル取得結果を表すオブジェクトです。  
  
※この結果にCapabilityResults.Failを設定してもエラーとはならず、CapabilityResults.Ignoreと同じ扱いになります。

[ModelReferableParams](_extension_api_NextDesign.Core_ModelReferableParams.md)

モデル参照時の選択対象を決めるためのパラメータです。

[ModelReferableResult](_extension_api_NextDesign.Core_ModelReferableResult.md)

モデル参照時の選択対象の結果を表すオブジェクトです。  
  
Next Designは、この結果オブジェクトを用いて選択対象を構築します。

[ModelRelateParams](_extension_api_NextDesign.Core_ModelRelateParams.md)

モデル関連付け可否を決めるためのパラメータです。

[ModelRelateResult](_extension_api_NextDesign.Core_ModelRelateResult.md)

モデル関連付け可否の結果を表すオブジェクトです。  
  
Next Designは、この結果オブジェクトを用いて関連付け可否を判定します。  
  
※この結果にCapabilityResults.Failを設定してもエラーとはならず、CapabilityResults.Ignoreと同じ扱いになります。

[ParamsBase](_extension_api_NextDesign.Core_ParamsBase.md)

編集支援機能で使用する結果オブジェクトの基底クラスです。

[ResultBase](_extension_api_NextDesign.Core_ResultBase.md)

編集支援機能で使用するパラメータオブジェクトの基底クラスです。