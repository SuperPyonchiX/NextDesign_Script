IModel.GetRelationsOf メソッド
==========================

名前空間: NextDesign.Core

説明​
-----------------------

指定したモデルとの全ての関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

該当する関連が存在しない場合は、空のコレクションを返します。  
なお、このメソッドは、所有関連/参照関連に関係なく、全ての関連を取得します。

引数​
-----------------------

名前

型

説明

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