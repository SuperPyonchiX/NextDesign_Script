IModel.GetRelationsOfWhere メソッド
===============================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスの指定した条件に合致する指定したモデルとの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

該当する関連が存在しない場合は、空のコレクションを返します。

取得対象の関連は評価関数により、任意に決定することができます。

引数​
-----------------------

名前

型

説明

opposite

IModel

相手側モデル  
null は指定できません。

predicate

Func<IRelationship, IField, bool>

関連評価関数  
　第1引数：関連インスタンス  
　第2引数：関連付けされているフィールド  
　戻り値：取得対象とする関連の場合は True  
  
nullが指定された場合は、与えられたモデルとの全ての関連を取得します。

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