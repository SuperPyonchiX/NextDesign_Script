IModelInitializationProvider インタフェース
====================================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

モデルのインスタンス初期化プロバイダです。  
モデルのインスタンス初期化をカスタマイズしたい場合は、このインタフェースの実装オブジェクトをレジストリに登録します。

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

[InitializeFields](_extension_api_NextDesign.Core_IModelInitializationProvider_methods_InitializeFields.md)

モデルに対する初期化を行います。  
  
性能面のボトルネックとならないように注意して実装してください。  
本メソッドの実装において、さらにフィールドにモデルを追加した場合、追加したモデルに該当する  
プロバイダの登録があれば、その時点でIModelInitializationProvider.InitializeFieldsが実行されます。  
  
プロバイダの実装では、呼び出しが再帰しないように注意してください。  
例えば、コンポジット構造のモデルに対して、プロバイダ内で子要素を作成するように実装した場合  
IModelInitializationProvider.InitializeFieldsの呼び出しが無限ループします。  
  
このメソッドが例外をスローした場合は、新しいモデルの作成自体が失敗したものとして処理全体がロールバックされます。

[InitializeProvider](_extension_api_NextDesign.Core_IModelInitializationProvider_methods_InitializeProvider.md)

このプロバイダの初期化を行います。  
このプロバイダを有効化するメタクラスをコンテキストに登録することで、モデルの生成時にプロバイダが実行されます。  
  
このメソッドは、プロバイダをIEditingCapabilityProviderRegistryに登録した際にコールバックされます。