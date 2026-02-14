IExecutionSpecification.ReplyMessage プロパティ
==========================================

名前空間: NextDesign.Core

説明​
-----------------------

この実行仕様が送信する応答メッセージ  
応答メッセージが存在しない場合は null を返します。

この実行仕様が活性化する終点で送信する応答メッセージを返します。  
応答メッセージが存在する場合、このメッセージは、SendMessagesの末尾要素となります。  
注意）  
SendMessagesの末尾要素が必ず応答メッセージになるとは限らない点に注意してください。

    IMessage ReplyMessage { get; }

型​
--------------------

*   [IMessage](_extension_api_NextDesign.Core_IMessage.md)