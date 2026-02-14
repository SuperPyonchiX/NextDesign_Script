IInteraction インタフェース
====================

名前空間: NextDesign.Core

説明​
-----------------------

相互作用モデル情報へのアクセスオブジェクトです。

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

[IModel](_extension_api_NextDesign.Core_IModel.md)

NextDesignの設計モデル情報へのアクセス手段を提供します。

プロパティ​
--------------------------------

名前

説明

[Frame](_extension_api_NextDesign.Core_IInteraction_properties_Frame.md)

フレーム

[InteractionUses](_extension_api_NextDesign.Core_IInteraction_properties_InteractionUses.md)

相互作用の利用の一覧  
コレクションの順序は作成した順序となります。

[Lifelines](_extension_api_NextDesign.Core_IInteraction_properties_Lifelines.md)

ライフラインの一覧  
コレクションの順序はシーケンス図の左側からの順序となります。

[MessageEnds](_extension_api_NextDesign.Core_IInteraction_properties_MessageEnds.md)

メッセージ端の一覧  
コレクションの順序は作成した順序となります。

[Messages](_extension_api_NextDesign.Core_IInteraction_properties_Messages.md)

メッセージの一覧  
コレクションの順序は作成した順序となります。

メソッド​
-----------------------------

名前

説明

[GetLifelinesByName](_extension_api_NextDesign.Core_IInteraction_methods_GetLifelinesByName.md)

指定した名前のライフラインの一覧を取得します。  
該当するライフラインが存在しない場合は空のコレクションを返します。

[GetLifelinesByTypeModel](_extension_api_NextDesign.Core_IInteraction_methods_GetLifelinesByTypeModel.md)

指定した型モデルにマッピングされたライフラインの一覧を取得します。  
該当するライフラインが存在しない場合は空のコレクションを返します。

[GetMessagesByTypeModel](_extension_api_NextDesign.Core_IInteraction_methods_GetMessagesByTypeModel.md)

指定した型モデルにマッピングされたメッセージの一覧を取得します。  
該当するメッセージが存在しない場合は空のコレクションを返します。