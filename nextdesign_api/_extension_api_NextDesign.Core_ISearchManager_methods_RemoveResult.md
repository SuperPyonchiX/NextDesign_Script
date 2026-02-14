ISearchManager.RemoveResult メソッド
================================

名前空間: NextDesign.Core

説明​
-----------------------

指定した検索結果を削除します。  
既に削除済みの検索結果を指定した場合は何も行いません。

引数​
-----------------------

名前

型

説明

entry

ISearchResultEntry

個別の検索結果

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

entry に null が指定された場合