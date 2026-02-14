IEditorView.SelectViewDefinition メソッド
=====================================

名前空間: NextDesign.Desktop

説明​
-----------------------

エディタで表示するビューを切り替えます。  
ビュー定義には、ViewDefinitionsで取得できる、エディタのモデルに対応するビュー定義のみが指定できます。

引数​
-----------------------

名前

型

説明

viewDefinition

IEditorDef

ビュー定義  
  
ViewDefinitionsで取得できる、エディタのモデルに対応するビュー定義が指定できます。

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

viewDefinition に null を指定した場合

不正操作

ExtensionInvalidOperationException

このエディタで表示できないビュー定義が指定された場合  
例えば、以下のようなケースに該当します。  
・エディタで表示しているモデルに対応しないビュー定義を指定する  
・メインエディタに対して、サブエディタでのみ表示できるビュー定義を指定する