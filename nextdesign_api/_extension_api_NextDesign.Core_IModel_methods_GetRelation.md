IModel.GetRelation メソッド
=======================

名前空間: NextDesign.Core

説明​
-----------------------

指定したフィールドの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

指定したフィールドが有効な関連を持たない場合は、null を返します。  
なお、指定したフィールドの多重度が2以上の場合は、該当フィールドの先頭要素への関連を取得します。

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

*   [IRelationship](_extension_api_NextDesign.Core_IRelationship.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

fieldName に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドがこのインスタンスのメタクラスで見つからない場合