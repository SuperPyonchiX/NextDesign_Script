IEditingCapabilityProviderRegistry インタフェース
==========================================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

編集支援を行うプロバイダのレジストリです。

・このレジストリは、プロジェクトと同じライフサイクルとなります。

所属エリア​
--------------------------------

名前

説明

[EditingCapability](_extension_api_overview_editing-capability.md)

EditingCapabilityにアクセスするAPI群です。

メソッド​
-----------------------------

名前

説明

[Register<TInterface>](_extension_api_NextDesign.Core_IEditingCapabilityProviderRegistry_methods_Register_TInterface_.md)

プロバイダを登録します。  
  
・TInterface には、登録するプロバイダの型を指定します。  
・プロバイダは複数登録できます。  
・同じ種類の複数のプロバイダが登録されている場合、登録順でプロバイダを実行します。  
・プロバイダの実行結果がnullでない場合、そのプロバイダの結果を採用し、以降のプロバイダは実行されません。  
・全てのプロバイダの結果がnullの場合、既定の動作を行います。

[UnRegister<TInterface>](_extension_api_NextDesign.Core_IEditingCapabilityProviderRegistry_methods_UnRegister_TInterface_.md)

プロバイダの登録を解除します。  
  
・TInterface には、登録を解除するプロバイダの型を指定します。

[UnRegisterAll](_extension_api_NextDesign.Core_IEditingCapabilityProviderRegistry_methods_UnRegisterAll.md)

全プロバイダの登録を解除します。