IModel.GetRelationsByFieldTagOf メソッド
====================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したタグが付与されたフィールドから、指定したモデルとの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

該当する関連が存在しない場合は、空のコレクションを返します。

引数​
-----------------------

名前

型

説明

opposite

IModel

相手側モデル  
null は指定できません。

fieldTag

string

フィールドのタグ名  
null、または空文字列が指定された場合は、このメソッドの戻り値が空のコレクションとなります。

value

string

タグ値の文字列表現  
既定値は、null です。  
null または空文字が指定された場合は、フィールドに対するタグの有無のみで評価します。

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