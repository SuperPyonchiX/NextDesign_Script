IWorkspaceWindow.ActiveInfoWindow プロパティ
=======================================

名前空間: NextDesign.Desktop

説明​
-----------------------

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

現在のアプリケーションウィンドウの状態（ActivePage）により、指定したページを表示できない場合があります。  
その場合、設定された値は無視されます。  
・ActivePage が "Start" の場合は、"Error", "SearchResult" は表示できません。  
・ActivePage が "Trace"の場合は、すべてのページが表示できません。