IDifference インタフェース
===================

名前空間: NextDesign.Core

説明​
-----------------------

差分情報へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[モデル差分・比較](_extension_api_overview_model-diff.md)

モデルの比較操作や差分情報にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Field](_extension_api_NextDesign.Core_IDifference_properties_Field.md)

更新されたフィールド  
IsUpdateItem==trueの場合のみ有効

[IsMoveItem](_extension_api_NextDesign.Core_IDifference_properties_IsMoveItem.md)

移動要素であるか（比較元と比較先で場所が異なる）

[IsNewItem](_extension_api_NextDesign.Core_IDifference_properties_IsNewItem.md)

新規要素であるか（比較元にモデルがない）

[IsOldItem](_extension_api_NextDesign.Core_IDifference_properties_IsOldItem.md)

削除要素であるか（比較先にモデルがない）

[IsUpdateItem](_extension_api_NextDesign.Core_IDifference_properties_IsUpdateItem.md)

更新要素であるか（比較元と比較先で内容が異なる）

[NewOrder](_extension_api_NextDesign.Core_IDifference_properties_NewOrder.md)

新しい順序  
IsMoveItem==trueの場合のみ有効

[NewParent](_extension_api_NextDesign.Core_IDifference_properties_NewParent.md)

新しい親要素  
IsMoveItem==trueの場合のみ有効

[NewValue](_extension_api_NextDesign.Core_IDifference_properties_NewValue.md)

新しい値  
IsUpdateItem==trueの場合のみ有効

[OldOrder](_extension_api_NextDesign.Core_IDifference_properties_OldOrder.md)

古い順序  
IsMoveItem==trueの場合のみ有効

[OldParent](_extension_api_NextDesign.Core_IDifference_properties_OldParent.md)

古い親要素  
IsMoveItem==trueの場合のみ有効

[OldValue](_extension_api_NextDesign.Core_IDifference_properties_OldValue.md)

古い値  
IsUpdateItem==trueの場合のみ有効

関連項目​
-----------------------------

名前

説明

モデルの比較結果を確認する

APIを通してNextDesignの各種モデル情報を比較した結果差分を確認します。