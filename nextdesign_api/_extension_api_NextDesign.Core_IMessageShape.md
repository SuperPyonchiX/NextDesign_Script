IMessageShape インタフェース
=====================

名前空間: NextDesign.Core

説明​
-----------------------

シーケンス図のメッセージ図形へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[シーケンスエディタ](_extension_api_overview_sequence-editor.md)

シーケンス図にアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[ISequenceConnectorShape](_extension_api_NextDesign.Core_ISequenceConnectorShape.md)

シーケンス図のコネクタ図形へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[ReceivePort](_extension_api_NextDesign.Core_IMessageShape_properties_ReceivePort.md)

メッセージ受信先ポート

[Receiver](_extension_api_NextDesign.Core_IMessageShape_properties_Receiver.md)

メッセージ受信先ライフライン  
ライフラインへの受信メッセージでない場合は null を返します。

[SelfloopBendsX](_extension_api_NextDesign.Core_IMessageShape_properties_SelfloopBendsX.md)

自己接続メッセージにおけるベンド位置のX座標  
自己接続メッセージでない場合は、常に0が返ります。  
自己接続メッセージは、送信元と受信先のライフラインが同一となるメッセージです。  
ベンド位置は、該当メッセージにおける最初の折れ線のポイントとなります。

[Sender](_extension_api_NextDesign.Core_IMessageShape_properties_Sender.md)

メッセージ送信元ライフライン  
ライフラインからの送信メッセージでない場合は null を返します。

[SendPort](_extension_api_NextDesign.Core_IMessageShape_properties_SendPort.md)

メッセージ送信元ポート

[SourceY](_extension_api_NextDesign.Core_IMessageShape_properties_SourceY.md)

メッセージの起点のY座標  
メッセージの起点は、送信元となるシェイプとの接続点です。

[TargetY](_extension_api_NextDesign.Core_IMessageShape_properties_TargetY.md)

メッセージの終点のY座標  
メッセージの終点は、受信先となるシェイプとの接続点です。

[Text](_extension_api_NextDesign.Core_IMessageShape_properties_Text.md)

テキスト

[TypeModel](_extension_api_NextDesign.Core_IMessageShape_properties_TypeModel.md)

型にマッピングされたモデル