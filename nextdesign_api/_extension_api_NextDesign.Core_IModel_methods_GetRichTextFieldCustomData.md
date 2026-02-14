IModel.GetRichTextFieldCustomData メソッド
======================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したリッチテキストフィールドのカスタムフォーマットの値を取得します。  
指定したフォーマットが見つからない場合はnullを返します。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

format

string

カスタムフォーマット名  
大文字小文字は区別しません。

戻り値​
--------------------------

*   string

例外​
------------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

fieldName に null、または空文字列 を指定した場合  
format に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定したフィールドがこのインスタンスのメタクラスで見つからない場合

フィールドの型と一致しない

ExtensionInvalidTypeException

指定したフィールドのデータ型がリッチテキスト型でない場合