IMetamodels.RemoveLiteral メソッド
==============================

名前空間: NextDesign.Core

説明​
-----------------------

列挙型リテラルを削除します。

引数​
-----------------------

名前

型

説明

owner

IEnum

列挙型

literal

string

削除対象のリテラル文字列

戻り値​
----------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

指定された名前のリテラルが存在しない場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合