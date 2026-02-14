IMetamodels.AddProperty メソッド
============================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスに新しいプロパティを追加します。

引数​
-----------------------

名前

型

説明

owner

IClass

クラス

name

string

プロパティ名

type

string

プロパティの型名  
\- "Object" : オブジェクト型  
\- "String"：文字列型  
\- "Integer"：整数型  
\- "Double"：浮動小数点型  
\- "Boolean"：真偽値型  
\- "RichText" : リッチテキスト型  
  
上記以外の型名を指定した場合は、クラス名または列挙名として一致する型を特定します。

multiplicity

string

多重度  
\- "ZeroOne" : 0または1  
\- "One"：1  
\- "ZeroToMany"：0以上の複数  
\- "OneToMany"：1以上の複数

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

型が見つからない

ExtensionTypeNotFoundException

プロパティの型名に指定したクラス、または列挙型が見つからない場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合