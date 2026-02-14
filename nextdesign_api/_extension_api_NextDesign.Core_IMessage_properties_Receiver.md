IMessage.Receiver プロパティ
=======================

名前空間: NextDesign.Core

説明​
-----------------------

メッセージの受信先ライフライン

このメッセージの受信先ポートが実行仕様の場合に、そのオーナとなるライフラインを返します。  
メッセージの受信先がメッセージ端のようにライフラインを特定できない要素であった場合は、null を返します。

    ILifeline Receiver { get; }

型​
--------------------

*   [ILifeline](_extension_api_NextDesign.Core_ILifeline.md)