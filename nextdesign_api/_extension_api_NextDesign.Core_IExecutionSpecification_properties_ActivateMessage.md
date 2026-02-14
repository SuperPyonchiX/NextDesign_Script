IExecutionSpecification.ActivateMessage プロパティ
=============================================

名前空間: NextDesign.Core

説明​
-----------------------

この実行仕様が受信する起動メッセージ  
起動メッセージが存在しない場合は null を返します。

この実行仕様が活性化する起点で受信するメッセージを返します。  
起動メッセージが存在する場合、このメッセージは、ReceiveMessagesの先頭要素となります。  
注意）  
ReceiveMessagesの先頭要素が必ず起動メッセージになるとは限らない点に注意してください。  
シーケンスの起点となる実行仕様では、起動メッセージが存在しないため、ReceiveMessagesの先頭要素が  
起動メッセージではない場合がありえます。

    IMessage ActivateMessage { get; }

型​
--------------------

*   [IMessage](_extension_api_NextDesign.Core_IMessage.md)