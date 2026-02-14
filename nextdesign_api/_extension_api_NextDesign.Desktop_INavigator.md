INavigator インタフェース
==================

名前空間: NextDesign.Desktop

説明​
-----------------------

ナビゲータへのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[ユーザインタフェース](_extension_api_overview_interfaces.md)

エディタやナビゲータなどのUIにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IUIElement](_extension_api_NextDesign.Desktop_IUIElement.md)

UI要素へのアクセス手段を提供します。

プロパティ​
--------------------------------

名前

説明

[BaseType](_extension_api_NextDesign.Desktop_INavigator_properties_BaseType.md)

ナビゲータの形態  
\- "TreeNavigator" : ツリー形式

[IsValid](_extension_api_NextDesign.Desktop_INavigator_properties_IsValid.md)

現在このナビゲータが有効であるか

[IsVisible](_extension_api_NextDesign.Desktop_INavigator_properties_IsVisible.md)

現在このナビゲータが表示されているか

[Items](_extension_api_NextDesign.Desktop_INavigator_properties_Items.md)

このナビゲータの全ての管理要素  
  
現在のバージョンでは、以下のナビゲータのみこのプロパティをサポートします。  
\- モデルナビゲータ  
\- プロダクトラインナビゲータ  
\- プロジェクトナビゲータ  
  
モデルナビゲータとプロダクトラインナビゲータはIModel型のコレクションを返します。  
プロジェクトナビゲータはIModelUnit型のコレクションを返します。

[MultiSelection](_extension_api_NextDesign.Desktop_INavigator_properties_MultiSelection.md)

複数要素を選択可能とできるか

[Name](_extension_api_NextDesign.Desktop_INavigator_properties_Name.md)

ナビゲータの種類  
\- "Model" : モデルナビゲータ  
\- "ProductLine" : プロダクトラインナビゲータ  
\- "Scm" : 構成管理ナビゲータ  
\- "Project" : プロジェクトナビゲータ  
\- "Profile" : プロファイルナビゲータ

[SelectedItems](_extension_api_NextDesign.Desktop_INavigator_properties_SelectedItems.md)

ナビゲータで選択されている要素  
  
現在のバージョンでは、以下のナビゲータのみこのプロパティをサポートします。  
\- モデルナビゲータ  
\- プロダクトラインナビゲータ  
\- プロジェクトナビゲータ  
  
選択要素のコレクションの順序は不定です。  
モデルナビゲータとプロダクトラインナビゲータはIModel型のコレクションを返します。  
プロジェクトナビゲータはIModelUnit型のコレクションを返します。

[Title](_extension_api_NextDesign.Desktop_INavigator_properties_Title.md)

ナビゲータの表示名

メソッド​
-----------------------------

名前

説明

[Select](_extension_api_NextDesign.Desktop_INavigator_methods_Select.md)

このナビゲータで指定された要素を選択します。  
  
現在のバージョンでは、以下のナビゲータのみこのメソッドをサポートします。  
\- モデルナビゲータ  
\- プロダクトラインナビゲータ