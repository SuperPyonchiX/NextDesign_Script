IWorkspaceWindow インタフェース
========================

名前空間: NextDesign.Desktop

説明​
-----------------------

アプリケーションのウィンドウへのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[ユーザインタフェース](_extension_api_overview_interfaces.md)

エディタやナビゲータなどのUIにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[ActiveInfoWindow](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_ActiveInfoWindow.md)

情報ペインのアクティブページ  
\- "Output" : 出力  
\- "Error" : エラー  
\- "SearchResult" : 検索結果

[ActivePage](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_ActivePage.md)

現在のアプリケーションでアクティブなページを取得または設定します。  
取得する場合の振る舞いは以下です。  
\- "Start" : スタートページ  
\- "Editor" ： エディタページのアクティブタブでトレースドキュメント以外を表示している  
\- "Trace" ： エディタページのアクティブタブでトレースドキュメントを表示している  
設定する場合の振る舞いは以下です。  
\- "Editor" ： 固定タブを表示する  
\- "Trace" ： トレースドキュメントタブを表示する

[CurrentInfoView](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_CurrentInfoView.md)

情報ウィンドウ内の現在アクティブな表示ページ

[CurrentOutputCategory](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_CurrentOutputCategory.md)

情報ペインのOutputのカレントカテゴリ

[EditorPage](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_EditorPage.md)

エディタUIへのアクセスオブジェクト  
アクティブなページがエディタページでない場合は、null を返します。

[Finder](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_Finder.md)

ファインダUIへのアクセスオブジェクト

[IsInformationPaneVisible](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_IsInformationPaneVisible.md)

情報ペイン表示状態

[TracePage](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_TracePage.md)

トレースUIへのアクセスオブジェクト  
アクティブタブにトレースドキュメントを表示していない場合は、null を返します。

[UI](_extension_api_NextDesign.Desktop_IWorkspaceWindow_properties_UI.md)

共通UIへのアクセスオブジェクト