ModelEditorCategory インタフェース
===========================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

エディタのカテゴリ情報です。

所属エリア​
--------------------------------

名前

説明

[EditingCapability](_extension_api_overview_editing-capability.md)

EditingCapabilityにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[DisplayName](_extension_api_NextDesign.Core_ModelEditorCategory_properties_DisplayName.md)

表示名

[DisplayOrder](_extension_api_NextDesign.Core_ModelEditorCategory_properties_DisplayOrder.md)

このカテゴリの選択肢の表示順序  
  
既存メニューの選択肢の表示順序は次のような値となっています。  
\- "手動"…100.0  
\- "詳細"…200.0  
\- "入力"…300.0  
\- "出力"…400.0  
\- "メインと同じ"…500.0

[Icon](_extension_api_NextDesign.Core_ModelEditorCategory_properties_Icon.md)

表示するアイコンのストリーム  
  
nullの場合は規定のアイコンを表示します。

[Id](_extension_api_NextDesign.Core_ModelEditorCategory_properties_Id.md)

カテゴリを識別するユニークなId  
  
NextDesign は、この値でカテゴリの選択を認識します。  
GUID値、または"MyExtension.CategoryX"のように、他のカテゴリと重複しないユニークな名前にしてください。

[IsDefault](_extension_api_NextDesign.Core_ModelEditorCategory_properties_IsDefault.md)

既定の選択肢とするか  
  
NextDesign は、既存メニューで選択されている選択肢が維持できなくなった場合、  
または、既存メニューで"詳細"が選択された状態でメニューの構成を変更した場合、  
このプロパティが true を返す最初のカテゴリを初期選択します。  
なお、このプロパティが true を返すカテゴリが存在しない場合は、"詳細"を選択します。