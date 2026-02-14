INavigatorEventParams インタフェース
=============================

名前空間: NextDesign.Desktop

説明​
-----------------------

ナビゲータイベントの共通パラメータです。

所属イベントエリア​
--------------------------------------------

名前

説明

[ナビゲータ](_extension_api_overview_events_navigators.md)

ナビゲータの表示切替、選択変更を通知します。

派生先​
--------------------------

名前

説明

[NavigatorSelectionChangedEventParams](_extension_api_NextDesign.Desktop_NavigatorSelectionChangedEventParams.md)

ナビゲータ内モデル選択イベントのパラメータです。

[NavigatorOnShowEventParams](_extension_api_NextDesign.Desktop_NavigatorOnShowEventParams.md)

ナビゲータ表示イベントのパラメータです。

[NavigatorOnHideEventParams](_extension_api_NextDesign.Desktop_NavigatorOnHideEventParams.md)

ナビゲータ非表示イベントのパラメータです。

プロパティ​
--------------------------------

名前

説明

[TargetNavigatorName](_extension_api_NextDesign.Desktop_INavigatorEventParams_properties_TargetNavigatorName.md)

イベント発生源のナビゲータ名。  
\- モデルナビゲータ : "Model"  
\- プロファイルナビゲータ : "Profile"  
\- プロジェクトナビゲータ : "Project"  
\- 構成管理ナビゲータ : "Scm"  
\- プロダクトラインナビゲータ : "ProductLine"