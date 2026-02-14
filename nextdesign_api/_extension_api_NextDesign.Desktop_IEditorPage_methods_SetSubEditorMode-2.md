IEditorPage.SetSubEditorMode(string,IModel) メソッド
================================================

名前空間: NextDesign.Desktop

説明​
-----------------------

アクティブタブのサブエディタの表示モードを string 型で指定します。

引数​
-----------------------

名前

型

説明

subEditorMode

string

サブエディタの表示モードの種別名  
\- "Manual" : 手動  
\- "Detail" : 詳細  
\- "Input" : 入力  
\- "Output" : 出力  
\- "SameAsMain" : メインと同じ  
\- "Custom.{ModelEditorCategory.Id}" : カスタム

displayModel

IModel

subEditorModeでManualを指定した場合にサブエディタにて表示するモデル  
  
subEditorMode に Manual 以外を指定した場合は無視されます。  
また、null を指定した場合は、プロジェクトを表示対象モデルとして設定します。

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

subEditorMode に値域外の文字列が指定された場合

不正操作

ExtensionInvalidOperationException

アクティブタブのサブエディタが非表示の状態でこのメソッドが呼び出された場合  
displayModel に削除されたモデルを指定した場合  
displayModel に編集しているプロジェクト以外のモデルを指定した場合