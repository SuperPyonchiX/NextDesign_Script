IModelReferenceProvider インタフェース
===============================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

モデル関連付けプロバイダです。  
モデル関連付け時の選択対象、及び関連付け可否をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

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

[CanRelate](_extension_api_NextDesign.Core_IModelReferenceProvider_methods_CanRelate.md)

モデル関連付け可否を判定します。  
要求されたパラメータに対して、このプロバイダで処理できない場合は、nullを返します。

[GetReferences](_extension_api_NextDesign.Core_IModelReferenceProvider_methods_GetReferences.md)

モデル参照時の選択対象を取得します。  
要求されたパラメータに対して、このプロバイダで処理できない場合は、nullを返します。