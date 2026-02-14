IMessageEnd インタフェース
===================

名前空間: NextDesign.Core

説明​
-----------------------

メッセージ端情報へのアクセスオブジェクトです。

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

[IInteractionElement](_extension_api_NextDesign.Core_IInteractionElement.md)

相互作用モデル要素情報へのアクセスオブジェクトです。  
相互作用を表現する要素に共通の特性を定義します。

[IMessagePort](_extension_api_NextDesign.Core_IMessagePort.md)

メッセージの接続先要素情報へのアクセスオブジェクトです。  
メッセージの接続先要素に共通の特性を定義します。

プロパティ​
--------------------------------

名前

説明

[Message](_extension_api_NextDesign.Core_IMessageEnd_properties_Message.md)

このメッセージ端が送信、または受信するメッセージ  
メッセージが接続されていない場合は null を返します。  
  
メッセージ端は、送信または受信のいずれか一方、かつ単一のメッセージしか接続できません。  
このプロパティが null であるかを確認することで、このメッセージ端がメッセージを送受信することができるかを調べることができます。