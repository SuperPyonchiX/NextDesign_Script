ISearch.AddSearchResult(object,string) メソッド
===========================================

名前空間: NextDesign.Core

説明​
-----------------------

検索結果を登録します。

モデル以外の情報を検索結果として扱う場合は、本メソッドにより登録することができます。

引数​
-----------------------

名前

型

説明

item

object

検索条件にヒットしたオブジェクト  
  
nullを指定することはできません。  
  
注意）  
任意のオブジェクトを指定できますが、検索結果一覧では、このオブジェクトの文字列表現を表示します。そのため、指定するオブジェクトでは、ToString()を実装することを推奨します。

message

string

メッセージ

戻り値​
--------------------------

*   [ISearchResultEntry](_extension_api_NextDesign.Core_ISearchResultEntry.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

item に null が指定された場合