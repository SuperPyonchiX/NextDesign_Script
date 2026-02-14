ILifeline インタフェース
=================

名前空間: NextDesign.Core

説明​
-----------------------

ライフライン情報へのアクセスオブジェクトです。

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

[IMessageSender](_extension_api_NextDesign.Core_IMessageSender.md)

メッセージの送信元として指定することができる要素であることを表します。

[IMessageReceiver](_extension_api_NextDesign.Core_IMessageReceiver.md)

メッセージの受信先として指定することができる要素であることを表します。

プロパティ​
--------------------------------

名前

説明

[Destruction](_extension_api_NextDesign.Core_ILifeline_properties_Destruction.md)

このライフライン上の破棄  
破棄が存在しない場合は null を返します。

[ExecutionSpecifications](_extension_api_NextDesign.Core_ILifeline_properties_ExecutionSpecifications.md)

このライフライン上の実行仕様一覧  
コレクションの順序は実行仕様が活性化する順序（シーケンス図上の上側からの順序）となります。

[ReceiveMessages](_extension_api_NextDesign.Core_ILifeline_properties_ReceiveMessages.md)

このライフラインが受信先となるメッセージ一覧  
このライフライン上の全ての実行仕様の受信メッセージを結合した一覧を返します。  
コレクションの順序は以下のルールで並べ替えされます。  
1\. このライフラインが保持する実行仕様の順序  
2\. 実行仕様の受信メッセージの順序

[SendMessages](_extension_api_NextDesign.Core_ILifeline_properties_SendMessages.md)

このライフラインが送信元となるメッセージ一覧  
このライフライン上の全ての実行仕様の送信メッセージを結合した一覧を返します。  
コレクションの順序は以下のルールで並べ替えされます。  
1\. このライフラインが保持する実行仕様の順序  
2\. 実行仕様の送信メッセージの順序

[TypeModel](_extension_api_NextDesign.Core_ILifeline_properties_TypeModel.md)

ライフラインの型にマッピングされたモデル