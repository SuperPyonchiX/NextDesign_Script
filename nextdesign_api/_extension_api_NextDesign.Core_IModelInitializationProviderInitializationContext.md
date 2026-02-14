IModelInitializationProviderInitializationContext インタフェース
=========================================================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

IModelInitializationProviderが対象とするメタモデルを登録するコンテキストです。

所属エリア​
--------------------------------

名前

説明

[EditingCapability](_extension_api_overview_editing-capability.md)

EditingCapabilityにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Project](_extension_api_NextDesign.Core_IModelInitializationProviderInitializationContext_properties_Project.md)

対象のプロバイダの実行対象のプロジェクト

[TargetProvider](_extension_api_NextDesign.Core_IModelInitializationProviderInitializationContext_properties_TargetProvider.md)

対象のプロバイダ

メソッド​
-----------------------------

名前

説明

[RegisterClass](_extension_api_NextDesign.Core_IModelInitializationProviderInitializationContext_methods_RegisterClass.md)

対象のプロバイダでインスタンスの初期化を行うメタクラスを登録します。  
指定したクラスの派生クラスや継承元のクラスは自動的には登録されません。個別に指定する必要があります。  
  
同一のクラスに対して複数の初期化プロバイダが登録された場合、対象クラスに対するプロバイダの登録順で全てのプロバイダが呼び出されます。  
  
プロバイダの実行対象のプロジェクト管理外のクラスが指定された場合、そのクラスの指定は無視されます。

[RegisterClasses](_extension_api_NextDesign.Core_IModelInitializationProviderInitializationContext_methods_RegisterClasses.md)

対象のプロバイダでインスタンスの初期化を行うメタクラス群を登録します。  
指定したクラスの派生クラスや継承元のクラスは自動的には登録されません。個別に指定する必要があります。  
  
同一のクラスに対して複数の初期化プロバイダが登録された場合、対象クラスに対するプロバイダの登録順で全てのプロバイダが呼び出されます。  
  
プロバイダの実行対象のプロジェクト管理外のクラスが指定された場合、そのクラスの指定は無視されます。