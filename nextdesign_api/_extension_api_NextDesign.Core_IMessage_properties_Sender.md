IMessage.Sender プロパティ
=====================

名前空間: NextDesign.Core

説明​
-----------------------

メッセージの送信元ライフライン

このメッセージの送信元ポートが実行仕様の場合に、そのオーナとなるライフラインを返します。  
メッセージの送信元がメッセージ端のようにライフラインを特定できない要素であった場合は、null を返します。

    ILifeline Sender { get; }

型​
--------------------

*   [ILifeline](_extension_api_NextDesign.Core_ILifeline.md)