IRelationship インタフェース
=====================

名前空間: NextDesign.Core

説明​
-----------------------

関連情報へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[モデル](_extension_api_overview_model.md)

モデルにアクセスするAPI群です。

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

[IsDerivation](_extension_api_NextDesign.Core_IRelationship_properties_IsDerivation.md)

導出関連か  
  
注意）導出関連と関連端オブジェクト  
導出関連は、導出先から導出元方向への関連として定義されます。  
そのため、導出関連におけるSourceオブジェクトが導出先、Targetオブジェクトが導出元を表す点に注意してください。

[IsEmbedded](_extension_api_NextDesign.Core_IRelationship_properties_IsEmbedded.md)

所有関連か

[IsReference](_extension_api_NextDesign.Core_IRelationship_properties_IsReference.md)

参照関連か

[IsTwoWay](_extension_api_NextDesign.Core_IRelationship_properties_IsTwoWay.md)

双方向関連か

[Source](_extension_api_NextDesign.Core_IRelationship_properties_Source.md)

関連元側関連端オブジェクト

[SourceField](_extension_api_NextDesign.Core_IRelationship_properties_SourceField.md)

関連元に対する関連端フィールド。  
このフィールドは関連先オブジェクトのフィールドとなります。

[SourceIndex](_extension_api_NextDesign.Core_IRelationship_properties_SourceIndex.md)

関連元に対する関連端フィールドにおけるインデックス

[Target](_extension_api_NextDesign.Core_IRelationship_properties_Target.md)

関連先側関連端オブジェクト

[TargetField](_extension_api_NextDesign.Core_IRelationship_properties_TargetField.md)

関連先に対する関連端フィールド。  
このフィールドは関連元オブジェクトのフィールドとなります。

[TargetIndex](_extension_api_NextDesign.Core_IRelationship_properties_TargetIndex.md)

関連先に対する関連端フィールドにおけるインデックス  
関連が単方向の場合はインデックス値を保存しないため、プロジェクトを再ロードするとインデックス値が変わる場合があります。

メソッド​
-----------------------------

名前

説明

[Relate](_extension_api_NextDesign.Core_IRelationship_methods_Relate.md)

この関連の関連端を指定されたモデルに置き換えます。  
このメソッドにてパス制約、多重度制約はチェックされません。

[UnRelate](_extension_api_NextDesign.Core_IRelationship_methods_UnRelate.md)

この関連による関連づけを解除して、この関連を削除します。  
既に削除済みの関連に対してこのメソッドが呼び出された場合は何も行われません。  
  
なお、このメソッドは所有関連に対しては実行できません。

関連項目​
-----------------------------

名前

説明

モデルを編集する

APIを通してNextDesignの各種モデル情報を編集します。