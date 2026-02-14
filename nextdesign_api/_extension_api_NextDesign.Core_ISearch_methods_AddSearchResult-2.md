ISearch.AddSearchResult(IModel,string,string) メソッド
==================================================

名前空間: NextDesign.Core

説明​
-----------------------

検索結果を登録します。

検索対象が特にモデルの場合は、このインタフェースを使用することで検索条件にヒットしたフィールド情報を付与することができます。

引数​
-----------------------

名前

型

説明

model

IModel

検索条件にヒットしたモデル  
  
nullを指定することはできません。

fields

string

検索条件にヒットしたフィールド名（複数の場合はカンマ区切り）  
  
nullを指定した場合は、検索結果にフィールド情報が設定されません。

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

model に null が指定された場合