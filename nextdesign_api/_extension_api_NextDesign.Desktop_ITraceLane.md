ITraceLane インタフェース
==================

名前空間: NextDesign.Desktop

説明​
-----------------------

トレース対象のルートオブジェクトを管理するレーン（系列）情報へのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[トレース](_extension_api_overview_traceability.md)

トーレス情報にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[ModelId](_extension_api_NextDesign.Desktop_ITraceLane_properties_ModelId.md)

このレーンの基点に指定しているモデルの識別子

[RootModel](_extension_api_NextDesign.Desktop_ITraceLane_properties_RootModel.md)

このレーンの基点モデル  
ModelIdで指定されているモデルが見つからない場合は　null を返します。

[RootNode](_extension_api_NextDesign.Desktop_ITraceLane_properties_RootNode.md)

このレーンの基点ノード  
ModelIdで指定されているモデルが見つからない場合は　null を返します。

メソッド​
-----------------------------

名前

説明

[FindNode](_extension_api_NextDesign.Desktop_ITraceLane_methods_FindNode.md)

このレーン内の指定されたモデルに対応するノードを検索します。  
モデルフィルタや関連フィルタが適用されている場合、フィルタが適用された状態で検索します。  
該当するノードがレーン内で見つからない場合は null を返します。