IMetamodels.Relate メソッド
=======================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラス間を関連づけます。

引数​
-----------------------

名前

型

説明

name

string

関連クラス名

source

IClass

ソース側のクラス

sourceEndName

string

ソース側クラスの参照名

target

IClass

ターゲット側のクラス

targetEndName

string

ターゲット側クラスの参照名

multiplicity

string

関連多重度  
\- "ManyToMany"：多対多  
\- "OneToMany"：1対多  
\- "ManyToOne"：多対1

戻り値​
--------------------------

*   [IRelationshipClass](_extension_api_NextDesign.Core_IRelationshipClass.md)

例外​
-----------------------

名前

例外クラス

説明

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合