IModelCreationProvider インタフェース
==============================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

モデル作成プロバイダです。  
モデル作成時の選択対象をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

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

[GetCreatableClasses](_extension_api_NextDesign.Core_IModelCreationProvider_methods_GetCreatableClasses.md)

モデル作成時の選択対象を取得します。  
要求されたパラメータに対して、このプロバイダで処理できない場合は、nullを返します。