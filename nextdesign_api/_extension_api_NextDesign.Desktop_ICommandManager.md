ICommandManager インタフェース
=======================

名前空間: NextDesign.Desktop

説明​
-----------------------

Extensionで追加登録するコマンド、およびアプリケーションが提供するシステムコマンドを管理します。

所属エリア​
--------------------------------

名前

説明

[コマンド](_extension_api_overview_commands.md)

コマンドハンドラで受け取ったコマンドにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[AllCommands](_extension_api_NextDesign.Desktop_ICommandManager_properties_AllCommands.md)

コマンド一覧

メソッド​
-----------------------------

名前

説明

[CanExecuteCommand](_extension_api_NextDesign.Desktop_ICommandManager_methods_CanExecuteCommand.md)

指定された識別子のコマンドを実行可能であるか調べます。  
実行可能な場合は真を返します。  
このメソッドは、Extensionから別のExtensionを実行する際に、その可否判断に用います。

[CreateCommandParams](_extension_api_NextDesign.Desktop_ICommandManager_methods_CreateCommandParams.md)

コマンドパラメータを作成します。

[ExecuteCommand](_extension_api_NextDesign.Desktop_ICommandManager_methods_ExecuteCommand.md)

指定された識別子のコマンドを実行します。  
このメソッドは、Extensionから別のExtensionを実行する際に使用します。  
また、コマンドパラメータが不要な場合は指定無しでも実行できます。

[GetCommand](_extension_api_NextDesign.Desktop_ICommandManager_methods_GetCommand.md)

指定された識別子のコマンドを取得します。  
  
コマンドの識別子は、アプリケーション全体で一意となります。  
エクステンションで登録するコマンド識別子は、`$\{エクステンション名\}.$\{コマンド名\}` のように命名ルールにより一意性を確保してください。