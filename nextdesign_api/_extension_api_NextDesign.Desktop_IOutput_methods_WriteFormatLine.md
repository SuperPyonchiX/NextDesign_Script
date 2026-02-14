IOutput.WriteFormatLine メソッド
============================

名前空間: NextDesign.Desktop

説明​
-----------------------

出力の指定されたカテゴリに指定のフォーマットで文字列を追加します。  
なお、フォーマット文字列の置換についてはC#のString.Formatに準拠します。

引数​
-----------------------

名前

型

説明

category

string

カテゴリ  
  
null を指定することはできません。

format

string

フォーマット文字列  
  
null を指定することはできません。  
  
文字列中の置換子（"{0}"等）の範囲をparameterで与えられたオブジェクトの文字列表現に置き換えて出力します。

parameter

object\[\]

置換オブジェクトの配列  
  
null を指定することはできません。

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

category、format、または parameter に null が指定された場合