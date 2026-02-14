InformationOnDoubleClickEventParams.Entry プロパティ
===============================================

名前空間: NextDesign.Desktop

説明​
-----------------------

ダブルクリックされた情報

    IInfoEntry Entry { get; }

型​
--------------------

*   [IInfoEntry](_extension_api_NextDesign.Core_IInfoEntry.md)

注釈​
-----------------------

情報ウィンドウ内の表示ページの種類が "Output" の場合は null が返ります。  
それ以外の場合、表示ページの種類により、実体型は次のオブジェクトになります。

*   "Error" : IError オブジェクト
*   "SearchResult" : ISearchResultEntry オブジェクト