ProjectAfterOpenEventParams インタフェース
===================================

名前空間: NextDesign.Desktop

説明​
-----------------------

プロジェクトオープン後イベントのパラメータです。

所属イベントエリア​
--------------------------------------------

名前

説明

[プロジェクト](_extension_api_overview_events_project.md)

プロジェクトの作成・読み込み・保存・終了を通知します。

継承元​
--------------------------

名前

説明

[EventParams](_extension_api_NextDesign.Desktop_EventParams.md)

イベントパラメータの基底クラスです。

プロパティ​
--------------------------------

名前

説明

[Filename](_extension_api_NextDesign.Desktop_ProjectAfterOpenEventParams_properties_Filename.md)

ファイル名

[Project](_extension_api_NextDesign.Desktop_ProjectAfterOpenEventParams_properties_Project.md)

プロジェクト

注釈​
-----------------------

プロジェクトオープン後イベントハンドラでは、"IWorkspace.CurrentProject" の値を保証しません。  
オープンされたプロジェクトの取得は、本イベントのパラメータの "ProjectAfterOpenEventParams.Project" を使用してください。