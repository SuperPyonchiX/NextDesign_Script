IInfoView.SelectedItems プロパティ
=============================

名前空間: NextDesign.Desktop

説明​
-----------------------

情報ウィンドウ内の表示ページの選択要素  
出力ページでは空のコレクションが返ります。  
その他のページでのコレクション要素の実体は、以下の通りとなります。

*   エラーページ : IError
*   検索結果ページ : ISearchResultEntry

    IInfoEntryCollection SelectedItems { get; }

型​
--------------------

*   IInfoEntryCollection