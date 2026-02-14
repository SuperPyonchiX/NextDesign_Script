IExecutionSpecification インタフェース
===============================

名前空間: NextDesign.Core

説明​
-----------------------

実行仕様情報へのアクセスオブジェクトです。

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

[ActivateMessage](_extension_api_NextDesign.Core_IExecutionSpecification_properties_ActivateMessage.md)

この実行仕様が受信する起動メッセージ  
起動メッセージが存在しない場合は null を返します。  
  
この実行仕様が活性化する起点で受信するメッセージを返します。  
起動メッセージが存在する場合、このメッセージは、ReceiveMessagesの先頭要素となります。  
注意）  
ReceiveMessagesの先頭要素が必ず起動メッセージになるとは限らない点に注意してください。  
シーケンスの起点となる実行仕様では、起動メッセージが存在しないため、ReceiveMessagesの先頭要素が  
起動メッセージではない場合がありえます。

[IsDestruction](_extension_api_NextDesign.Core_IExecutionSpecification_properties_IsDestruction.md)

この実行仕様が破棄区間であるか  
  
この実行仕様の起動メッセージが破棄メッセージの場合、この実行仕様は破棄区間として識別します。  
起動メッセージが破棄メッセージでない、または起動メッセージが存在しない場合は、破棄区間とはなりません。  
相互作用では、同一のライフラインにおいて破棄区間より後に実行仕様を追加（メッセージを受信）することはできません。  
このプロパティは相互作用モデルを変更する際に、この実行仕様より後にメッセージを追加できるかを調べる際などに利用することができます。

[IsInitialization](_extension_api_NextDesign.Core_IExecutionSpecification_properties_IsInitialization.md)

この実行仕様が初期化区間であるか  
  
この実行仕様の起動メッセージが生成メッセージの場合、この実行仕様は初期化区間として識別します。  
起動メッセージが生成メッセージでない、または起動メッセージが存在しない場合は、初期化区間とはなりません。  
相互作用では、同一のライフラインにおいて初期化区間より前に実行仕様を追加（メッセージを受信）することはできません。  
このプロパティは相互作用モデルを変更する際に、この実行仕様より前にメッセージを追加できるかを調べる際などに利用することができます。

[Lifeline](_extension_api_NextDesign.Core_IExecutionSpecification_properties_Lifeline.md)

この実行仕様を保持するライフライン

[ReplyMessage](_extension_api_NextDesign.Core_IExecutionSpecification_properties_ReplyMessage.md)

この実行仕様が送信する応答メッセージ  
応答メッセージが存在しない場合は null を返します。  
  
この実行仕様が活性化する終点で送信する応答メッセージを返します。  
応答メッセージが存在する場合、このメッセージは、SendMessagesの末尾要素となります。  
注意）  
SendMessagesの末尾要素が必ず応答メッセージになるとは限らない点に注意してください。