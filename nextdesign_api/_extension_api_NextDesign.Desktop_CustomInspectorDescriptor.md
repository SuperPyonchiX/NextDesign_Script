CustomInspectorDescriptor インタフェース
=================================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

カスタムインスペクタのタイプ記述子です。

所属エリア​
---------------------------------

名前

説明

[カスタムUI](_extension_api_overview_custom-ui.md)

カスタムUIにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[CustomDescriptorBase](_extension_api_NextDesign.Desktop_CustomDescriptorBase.md)

カスタムUIのタイプ記述子の基底です。

プロパティ​
--------------------------------

名前

説明

[Configs](_extension_api_NextDesign.Desktop_CustomInspectorDescriptor_properties_Configs.md)

インスペクタの動作オプション。

[DisablingInspectorIds](_extension_api_NextDesign.Desktop_CustomInspectorDescriptor_properties_DisablingInspectorIds.md)

無効(非表示)にするインスペクタのId。  
このカスタムインスペクタが登録されている間は無効となります。

[DisplayName](_extension_api_NextDesign.Desktop_CustomInspectorDescriptor_properties_DisplayName.md)

インスペクタの表示名。

[DisplayOrder](_extension_api_NextDesign.Desktop_CustomInspectorDescriptor_properties_DisplayOrder.md)

インスペクタの表示順序。  
組み込みインスペクタの前後に表示順序を指定できます。

[IsEnabledFunc](_extension_api_NextDesign.Desktop_CustomInspectorDescriptor_properties_IsEnabledFunc.md)

インスペクタが有効かの評価関数。  
評価関数が指定されていない場合、常にtrueを返すとして扱います。