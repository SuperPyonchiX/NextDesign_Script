IEditingCapabilityProvider インタフェース
==================================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

プロバイダの基底となるインターフェースです。

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

[IModelEditorSelectionProvider](_extension_api_NextDesign.Core_IModelEditorSelectionProvider.md)

エディタの選択プロバイダです。  
モデルに対して表示可能なサブエディタ・インスペクタをカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

[IModelInitializationProvider](_extension_api_NextDesign.Core_IModelInitializationProvider.md)

モデルのインスタンス初期化プロバイダです。  
モデルのインスタンス初期化をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

[IModelReferenceProvider](_extension_api_NextDesign.Core_IModelReferenceProvider.md)

モデル関連付けプロバイダです。  
モデル関連付け時の選択対象、及び関連付け可否をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

[IModelCreationProvider](_extension_api_NextDesign.Core_IModelCreationProvider.md)

モデル作成プロバイダです。  
モデル作成時の選択対象をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。