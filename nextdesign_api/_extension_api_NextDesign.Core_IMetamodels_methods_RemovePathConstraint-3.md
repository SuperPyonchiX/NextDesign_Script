IMetamodels.RemovePathConstraint(IClass,IField) メソッド
====================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスの指定したフィールドのパス制約を削除します。

引数​
-----------------------

名前

型

説明

scope

IClass

制約の有効範囲となるクラス

targetField

IField

制約の対象フィールド

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

制約の有効範囲となるクラス、制約の対象フィールドが指定されていない場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合