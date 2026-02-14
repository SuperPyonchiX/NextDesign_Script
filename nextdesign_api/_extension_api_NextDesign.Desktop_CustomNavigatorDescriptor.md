CustomNavigatorDescriptor インタフェース
=================================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

カスタムナビゲータのタイプ記述子です。

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

[Configs](_extension_api_NextDesign.Desktop_CustomNavigatorDescriptor_properties_Configs.md)

ナビゲータの動作オプション。

[DisplayName](_extension_api_NextDesign.Desktop_CustomNavigatorDescriptor_properties_DisplayName.md)

ナビゲータの表示名。

[DisplayOrder](_extension_api_NextDesign.Desktop_CustomNavigatorDescriptor_properties_DisplayOrder.md)

ナビゲータの表示位置。

[Icon](_extension_api_NextDesign.Desktop_CustomNavigatorDescriptor_properties_Icon.md)

ナビゲータアイコン。  
pack-uri 形式の文字列を返すように実装してください。

[IsEnabledFunc](_extension_api_NextDesign.Desktop_CustomNavigatorDescriptor_properties_IsEnabledFunc.md)

ナビゲータが表示対象であるかの評価関数。  
評価関数が指定されていない場合、常にtrueを返すとして扱います。