IErrors.FindErrorOfModelByCategory メソッド
=======================================

名前空間: NextDesign.Core

説明​
-----------------------

与えられたモデルの指定されたカテゴリのエラー情報を検索します。  
検索結果のエラー情報にはサマリー情報も含みます。  
該当するエラー情報がない場合は空のコレクションを返します。

引数​
-----------------------

名前

型

説明

model

IModel

対象モデル  
  
null を指定することはできません。

category

string

カテゴリ

戻り値​
--------------------------

*   IErrorCollection

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

model に null が指定された場合