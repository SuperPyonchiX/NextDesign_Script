IModel.GetRelationsByFieldOf メソッド
=================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したフィールドにおける指定したモデルとの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

該当する関連が存在しない場合は、空のコレクションを返します。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

opposite

IModel

相手側モデル  
null は指定できません。

戻り値​
--------------------------

*   IRelationshipCollection

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

opposite に null を指定した場合  
fieldName に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドがこのインスタンスのメタクラスで見つからない場合