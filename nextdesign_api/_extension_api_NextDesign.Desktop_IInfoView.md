IInfoView インタフェース
=================

名前空間: NextDesign.Desktop

説明​
-----------------------

情報ウィンドウの表示ページへのアクセス手段を提供します。

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

[IsVisible](_extension_api_NextDesign.Desktop_IInfoView_properties_IsVisible.md)

情報ウィンドウ内でこのページが表示されているか

[Items](_extension_api_NextDesign.Desktop_IInfoView_properties_Items.md)

情報ウィンドウ内の表示ページの内容  
出力ページでは空のコレクションが返ります。  
その他のページでのコレクション要素の実体は、以下の通りとなります。  
\- エラーページ : IError  
\- 検索結果ページ : ISearchResultEntry

[Name](_extension_api_NextDesign.Desktop_IInfoView_properties_Name.md)

情報ウィンドウ内の表示ページの種類  
\- "Output" : 出力ページ  
\- "Error" : エラーページ  
\- "SearchResult" : 検索結果ページ

[SelectedItems](_extension_api_NextDesign.Desktop_IInfoView_properties_SelectedItems.md)

情報ウィンドウ内の表示ページの選択要素  
出力ページでは空のコレクションが返ります。  
その他のページでのコレクション要素の実体は、以下の通りとなります。  
\- エラーページ : IError  
\- 検索結果ページ : ISearchResultEntry

[Title](_extension_api_NextDesign.Desktop_IInfoView_properties_Title.md)

情報ウィンドウ内の表示ページ名

メソッド​
-----------------------------

名前

説明

[ScrollToBottom](_extension_api_NextDesign.Desktop_IInfoView_methods_ScrollToBottom.md)

情報ウィンドウのスクロールバーを末尾までスクロールします。

[Select](_extension_api_NextDesign.Desktop_IInfoView_methods_Select.md)

情報ウィンドウ内のこの表示ページで、指定された要素を選択します。