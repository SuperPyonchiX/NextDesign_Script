IMessagePort インタフェース
====================

名前空間: NextDesign.Core

説明​
-----------------------

メッセージの接続先要素情報へのアクセスオブジェクトです。  
メッセージの接続先要素に共通の特性を定義します。

所属エリア​
--------------------------------

名前

説明

[インタラクションモデル](_extension_api_overview_interaction-model.md)

インタラクションモデルにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IMessageSender](_extension_api_NextDesign.Core_IMessageSender.md)

メッセージの送信元として指定することができる要素であることを表します。

[IMessageReceiver](_extension_api_NextDesign.Core_IMessageReceiver.md)

メッセージの受信先として指定することができる要素であることを表します。

派生先​
--------------------------

名前

説明

[IInteractionUse](_extension_api_NextDesign.Core_IInteractionUse.md)

相互作用の利用情報へのアクセスオブジェクトです。

[IMessageEnd](_extension_api_NextDesign.Core_IMessageEnd.md)

メッセージ端情報へのアクセスオブジェクトです。

[IFrame](_extension_api_NextDesign.Core_IFrame.md)

フレーム情報へのアクセスオブジェクトです。

[IExecutionSpecification](_extension_api_NextDesign.Core_IExecutionSpecification.md)

実行仕様情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[ReceiveMessages](_extension_api_NextDesign.Core_IMessagePort_properties_ReceiveMessages.md)

受信メッセージの一覧  
コレクションの順序はメッセージの受信順序となります。

[SendMessages](_extension_api_NextDesign.Core_IMessagePort_properties_SendMessages.md)

送信メッセージの一覧  
コレクションの順序はメッセージの送信順序となります。