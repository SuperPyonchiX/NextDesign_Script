commands イベントエリア
================

説明​
-----------------------

コマンドの実行を通知します。

イベント​
-----------------------------

名前

定義名

説明

[コマンド実行前イベント](_extension_api_overview_events_commands_onBeforeExecute.md)

onBeforeExecute

コマンドの実行前に通知されるイベントです。

[コマンド実行後イベント](_extension_api_overview_events_commands_onAfterExecute.md)

onAfterExecute

コマンドの実行後に通知されるイベントです。

エリアに属するインタフェース​
-----------------------------------------------------------

名前

説明

[ICommandParams](_extension_api_NextDesign.Desktop_ICommandParams.md)

コマンドのパラメータを提供します。

[BeforeExecuteEventParams](_extension_api_NextDesign.Desktop_BeforeExecuteEventParams.md)

コマンド実行前イベントのパラメータです。

[AfterExecuteEventParams](_extension_api_NextDesign.Desktop_AfterExecuteEventParams.md)

コマンド実行後イベントのパラメータです。

[IExecuteEventParams](_extension_api_NextDesign.Desktop_IExecuteEventParams.md)

コマンドイベントの共通パラメータです。

注釈​
-----------------------

*   通知されるコマンドは、システムコマンドとエクステンションの拡張ポイントで定義されたコマンドです。
*   コマンド実行中に例外が発生した場合、コマンド実行後イベントは通知されません。