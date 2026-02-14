ICommonUI インタフェース
=================

名前空間: NextDesign.Desktop

説明​
-----------------------

基本的なダイアログなどのUIへのアクセス手段を提供します。

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

メソッド​
-----------------------------

名前

説明

[MessageBox](_extension_api_NextDesign.Desktop_ICommonUI_methods_MessageBox.md)

\[Obsolete\] メッセージを表示します。

[SelectRevision](_extension_api_NextDesign.Desktop_ICommonUI_methods_SelectRevision.md)

カレントプロジェクトの指定されたモデルファイルのリビジョン一覧を表示して選択されたリビジョンを取得します。  
モデルファイルが未指定の場合は、カレントプロジェクトのリビジョン一覧を表示します。  
リビジョンが選択されなかった場合は null を返します。  
指定されたモデルファイルがカレントプロジェクトの管理下でない、または、カレントプロジェクトがSCM連携していない場合は例外をスローします。

[ShowConfirmDialog](_extension_api_NextDesign.Desktop_ICommonUI_methods_ShowConfirmDialog.md)

確認ダイアログを表示します。  
ダイアログの操作結果(OK=true / Cancel=false)を返します。

[ShowInformationDialog](_extension_api_NextDesign.Desktop_ICommonUI_methods_ShowInformationDialog.md)

通知ダイアログを表示します。

[ShowMessageBox](_extension_api_NextDesign.Desktop_ICommonUI_methods_ShowMessageBox.md)

メッセージを表示します。

[ShowOpenFileDialog](_extension_api_NextDesign.Desktop_ICommonUI_methods_ShowOpenFileDialog.md)

ファイルを開くダイアログを表示して、ダイアログで選択したファイルのパスを取得します。  
ダイアログがキャンセルされた場合はnullを返します。

[ShowRevisions](_extension_api_NextDesign.Desktop_ICommonUI_methods_ShowRevisions.md)

カレントプロジェクトの指定されたモデルファイルのリビジョン一覧を表示します。  
モデルファイルが未指定の場合は、カレントプロジェクトのリビジョン一覧を表示します。  
指定されたモデルファイルがカレントプロジェクトの管理下でない、または、カレントプロジェクトがSCM連携していない場合は例外をスローします。

[ShowSaveFileDialog](_extension_api_NextDesign.Desktop_ICommonUI_methods_ShowSaveFileDialog.md)

ファイルの保存ダイアログを表示して、ダイアログで選択したファイルのパスを取得します。  
ダイアログがキャンセルされた場合はnullを返します。

[ShowSelectFolderDialog](_extension_api_NextDesign.Desktop_ICommonUI_methods_ShowSelectFolderDialog.md)

フォルダを開くダイアログを表示して、ダイアログで選択したフォルダのパスを取得します。  
ダイアログがキャンセルされた場合はnullを返します。