IModelEditorSelectionProvider インタフェース
=====================================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

エディタの選択プロバイダです。  
モデルに対して表示可能なサブエディタ・インスペクタをカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

所属エリア​
--------------------------------

名前

説明

[EditingCapability](_extension_api_overview_editing-capability.md)

EditingCapabilityにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IEditingCapabilityProvider](_extension_api_NextDesign.Core_IEditingCapabilityProvider.md)

プロバイダの基底となるインターフェースです。

メソッド​
-----------------------------

名前

説明

[GetCategories](_extension_api_NextDesign.Core_IModelEditorSelectionProvider_methods_GetCategories.md)

エディタのカテゴリ情報を設定した応答オブジェクトを取得します。要求されたパラメータに対して、このプロバイダで処理できない場合は、nullを返します。

[GetModel](_extension_api_NextDesign.Core_IModelEditorSelectionProvider_methods_GetModel.md)

カテゴリの選択に対して、表示すべきモデルを取得します。  
要求されたパラメータに対して、このプロバイダで処理できない場合は、nullを返します。