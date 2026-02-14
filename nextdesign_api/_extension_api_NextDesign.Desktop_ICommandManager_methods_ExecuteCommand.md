ICommandManager.ExecuteCommand メソッド
===================================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定された識別子のコマンドを実行します。  
このメソッドは、Extensionから別のExtensionを実行する際に使用します。  
また、コマンドパラメータが不要な場合は指定無しでも実行できます。

引数​
-----------------------

名前

型

説明

commandIdentifier

string

コマンド識別子

commandParams

ICommandParams

コマンドパラメータ。  
未指定の場合は null で実行します。

戻り値​
--------------------------

*   void