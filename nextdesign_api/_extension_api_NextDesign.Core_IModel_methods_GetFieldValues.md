IModel.GetFieldValues メソッド
==========================

名前空間: NextDesign.Core

説明​
-----------------------

指定したフィールドの値コレクションを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

該当するフィールド値が存在しない場合は、空のコレクションを返します。  
なお、このメソッドで指定するフィールドのデータ型はクラス型でなければなりません。

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

*   IModelCollection

例外​
-------------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

fieldName に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドがこのインスタンスのメタクラスで見つからない場合

無効なデータ型

ExtensionInvalidTypeException

指定されたフィールドのデータ型がクラス型でない場合