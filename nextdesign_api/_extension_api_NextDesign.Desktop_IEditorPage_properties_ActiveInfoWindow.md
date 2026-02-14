IEditorPage.ActiveInfoWindow プロパティ
==================================

名前空間: NextDesign.Desktop

説明​
-----------------------

\[Obsolete\]  
情報ペインのアクティブページ

*   "Output" : 出力
*   "Error" : エラー
*   "SearchResult" : 検索結果

    string ActiveInfoWindow { get; set; }

型​
--------------------

*   string

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

値が値域外の場合

注釈​
-----------------------

**Ver.1.1 での API 仕様変更と移行方法について**

Ver.1.1 より、このプロパティは、IWorkspaceWindowに移行されました。このプロパティは将来のバージョンで削除されます。  
本 API を利用している場合は、Ver.1.1 以降へのバージョンアップと合わせてエクステンション内の該当箇所をIWorkspaceWindow.ActiveInfoWindowを利用するように変更してください。