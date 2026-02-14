IModel.AddError メソッド
====================

名前空間: NextDesign.Core

説明​
-----------------------

このモデルに対するエラー情報を追加して、追加したエラー情報を返します。

引数​
-----------------------

名前

型

説明

fields

string

エラーを検出したフィールド名（複数ある場合はカンマ区切り）  
  
null または空文字列を指定した場合は、エラー検出フィールドなしでエラー情報を追加します。

type

string

エラー種類  
\- "Error" : エラー  
\- "Warning" : 警告  
\- "Information" : 情報  
\- "Summary" : サマリー

title

string

タイトル  
  
null、空文字列を含むすべての文字列を指定できます。

message

string

メッセージ  
  
null、空文字列を含むすべての文字列を指定できます。

戻り値​
--------------------------

*   [IError](_extension_api_NextDesign.Core_IError.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

type に 値域外の文字列を指定した場合

注釈​
-----------------------

引数 "type" に "Summary" を指定して登録されたエラー情報は、ナビゲータ等の件数積み上げの対象外となります。