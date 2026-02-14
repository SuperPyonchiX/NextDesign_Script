IModel.Count メソッド
=================

名前空間: NextDesign.Core

説明​
-----------------------

指定したフィールドの値件数を取得します。

このメソッドは、IContextOption.PlModelAccessMode を評価しません。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

戻り値​
--------------------------

*   int

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

fieldName に null、または空文字列を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドがこのインスタンスのメタクラスで見つからない場合