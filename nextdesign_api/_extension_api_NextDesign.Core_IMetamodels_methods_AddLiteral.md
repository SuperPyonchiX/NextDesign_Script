IMetamodels.AddLiteral メソッド
===========================

名前空間: NextDesign.Core

説明​
-----------------------

指定した列挙型に、指定したリテラル文字列で新しい列挙型リテラルを追加します。

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

リテラル文字列

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

列挙型が指定されていない場合  
リテラル文字列に null/空文字/使用不可文字 が指定された場合

一意制約違反

ExtensionDuplicationException

指定された名前のリテラルが既に存在する場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合