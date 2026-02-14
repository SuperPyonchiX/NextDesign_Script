IMessage インタフェース
================

名前空間: NextDesign.Core

説明​
-----------------------

メッセージ情報へのアクセスオブジェクトです。

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

プロパティ​
--------------------------------

名前

説明

[Activator](_extension_api_NextDesign.Core_IMessage_properties_Activator.md)

このメッセージの起動メッセージ  
起動メッセージが存在しない場合は null を返します。  
  
■ 起動メッセージとは  
メッセージの送信元となる実行仕様を活性化するメッセージのことです。  
このメッセージの送信元ポートが実行仕様であった場合に、その実行仕様が最初に受信するメッセージが該当します。  
相互作用では、起動メッセージを受信することで、このメッセージの送信が有効となります。

[After](_extension_api_NextDesign.Core_IMessage_properties_After.md)

このメッセージの後行メッセージ  
後行メッセージが存在しない場合は null を返します。  
  
■ 後行メッセージとは  
このメッセージと送信元が同一のメッセージのうち、直後に送信されるメッセージのことです。  
ただし、後行メッセージには応答メッセージは含まれません。  
また、このメッセージの種類が応答メッセージの場合には、後行メッセージは null となります。  
注意）  
同一のライフラインから送信するメッセージであっても、実行仕様が異なる送信メッセージは後行メッセージとはならない点に注意してください。

[Before](_extension_api_NextDesign.Core_IMessage_properties_Before.md)

このメッセージの先行メッセージ  
先行メッセージが存在しない場合は null を返します。  
  
■ 先行メッセージとは  
このメッセージと送信元が同一のメッセージのうち、直前に送信されるメッセージのことです。  
先行メッセージには応答メッセージは含まれません。  
また、このメッセージの種類が応答メッセージの場合には、先行メッセージは null となります。  
注意）  
同一のライフラインから送信するメッセージであっても、実行仕様が異なる送信メッセージは先行メッセージとはならない点に注意してください。

[IsAppearance](_extension_api_NextDesign.Core_IMessage_properties_IsAppearance.md)

このメッセージが出現メッセージであるか  
このメッセージの送信元ポートがメッセージ端である場合に出現メッセージと評価します。

[IsAsynchronous](_extension_api_NextDesign.Core_IMessage_properties_IsAsynchronous.md)

このメッセージが非同期メッセージであるか

[IsLost](_extension_api_NextDesign.Core_IMessage_properties_IsLost.md)

このメッセージが消失メッセージであるか  
このメッセージの受信先ポートがメッセージ端である場合に出現メッセージと評価します。

[IsReply](_extension_api_NextDesign.Core_IMessage_properties_IsReply.md)

このメッセージが応答メッセージであるか

[IsSynchronous](_extension_api_NextDesign.Core_IMessage_properties_IsSynchronous.md)

このメッセージが同期メッセージであるか

[Kind](_extension_api_NextDesign.Core_IMessage_properties_Kind.md)

メッセージの種類  
\- 同期："sync"  
\- 非同期："async"  
\- 生成："create"  
\- 破棄："destroy"  
\- 応答："reply"

[ReceivePort](_extension_api_NextDesign.Core_IMessage_properties_ReceivePort.md)

メッセージの受信先ポート  
  
メッセージの受信先ポートの実際の型には、以下のモデル要素が該当します。  
\- IExecutionSpecification : 実行仕様  
\- IMessageEnd : メッセージ端  
\- IFrame : フレーム  
実際の型は、ReceivePortTypeを確認することで調べることができます。

[ReceivePortType](_extension_api_NextDesign.Core_IMessage_properties_ReceivePortType.md)

メッセージの受信先ポートの型  
  
以下のいずれかを返します。  
\- 実行仕様 : "ExecutionSpecification"  
\- メッセージ端 : "MessageEnd"  
\- フレーム : "Frame"

[Receiver](_extension_api_NextDesign.Core_IMessage_properties_Receiver.md)

メッセージの受信先ライフライン  
  
このメッセージの受信先ポートが実行仕様の場合に、そのオーナとなるライフラインを返します。  
メッセージの受信先がメッセージ端のようにライフラインを特定できない要素であった場合は、null を返します。

[Reply](_extension_api_NextDesign.Core_IMessage_properties_Reply.md)

このメッセージに対応する応答メッセージ  
対応する応答メッセージが存在しない場合は null を返します。  
  
■ 対応する応答メッセージとは  
このメッセージの受信先の実行仕様が返す応答メッセージが該当します。

[Sender](_extension_api_NextDesign.Core_IMessage_properties_Sender.md)

メッセージの送信元ライフライン  
  
このメッセージの送信元ポートが実行仕様の場合に、そのオーナとなるライフラインを返します。  
メッセージの送信元がメッセージ端のようにライフラインを特定できない要素であった場合は、null を返します。

[SendPort](_extension_api_NextDesign.Core_IMessage_properties_SendPort.md)

メッセージの送信元ポート  
  
メッセージの送信元ポートの実際の型には、以下のモデル要素が該当します。  
\- IExecutionSpecification : 実行仕様  
\- IMessageEnd : メッセージ端  
\- IFrame : フレーム  
実際の型は、SendPortTypeを確認することで調べることができます。

[SendPortType](_extension_api_NextDesign.Core_IMessage_properties_SendPortType.md)

メッセージの送信元ポートの型  
  
以下のいずれかを返します。  
\- 実行仕様 : "ExecutionSpecification"  
\- メッセージ端 : "MessageEnd"  
\- フレーム : "Frame"

[TypeModel](_extension_api_NextDesign.Core_IMessage_properties_TypeModel.md)

メッセージの型にマッピングされたモデル