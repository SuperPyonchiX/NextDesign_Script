IModelInitializationProvider.InitializeProvider メソッド
====================================================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

このプロバイダの初期化を行います。  
このプロバイダを有効化するメタクラスをコンテキストに登録することで、モデルの生成時にプロバイダが実行されます。

このメソッドは、プロバイダをIEditingCapabilityProviderRegistryに登録した際にコールバックされます。

引数​
-----------------------

名前

型

説明

context

IModelInitializationProviderInitializationContext

初期化コンテキスト

戻り値​
--------------------------

*   void