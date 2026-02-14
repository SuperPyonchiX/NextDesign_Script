ICommandDispatcher インタフェース
==========================

名前空間: NextDesign.Extension

説明​
-----------------------

コマンド転送オブジェクトのインタフェースです。  
必要に応じて、エクステンション側でエントリポイント（IExtensionの実装クラス）に対して追加実装することができます。

Next Design では、コマンド処理が要求された際に、エントリポイントがこのインタフェースを実装している場合に限り、コマンドのディスパッチを要求します。  
エントリポイントがこのインタフェースを実装しない場合は、従来通りエントリポイントからコマンドに対応する関数を探索して呼び出します。

所属エリア​
--------------------------------

名前

説明

[グローバル](_extension_api_overview_global.md)

エクステンションの実行環境や実行状態にアクセスするAPI群です。

メソッド​
-----------------------------

名前

説明

[DispatchCanExecuteCommand](_extension_api_NextDesign.Extension_ICommandDispatcher_methods_DispatchCanExecuteCommand.md)

指定されたコマンドの実行可否判定処理をディスパッチします。

[DispatchCommand](_extension_api_NextDesign.Extension_ICommandDispatcher_methods_DispatchCommand.md)

指定されたコマンドの実行処理をディスパッチします。

注釈​
-----------------------

エントリポイントがディスパッチャを実装している場合、Next Designからのエクステンションへの実行要求は、全てディスパッチャ呼び出しの動作となります。  
そのため、従来エントリポイントで実装していた関数は呼び出されなくなる点に注意してください。