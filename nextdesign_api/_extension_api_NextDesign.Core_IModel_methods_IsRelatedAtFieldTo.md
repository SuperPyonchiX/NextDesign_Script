IModel.IsRelatedAtFieldTo メソッド
==============================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスが指定したフィールドで指定したモデルと参照関連を持つか調べます。  
関連を持つ場合はTrueを返します。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

model

IModel

対象モデル  
null は指定できません。

戻り値​
--------------------------

*   bool

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

model に null を指定した場合  
fieldName に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドがこのインスタンスのメタクラスで見つからない場合