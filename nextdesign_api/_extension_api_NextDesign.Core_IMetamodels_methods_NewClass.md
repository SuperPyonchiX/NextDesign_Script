IMetamodels.NewClass メソッド
=========================

名前空間: NextDesign.Core

説明​
-----------------------

新しいクラスを生成します。

引数​
-----------------------

名前

型

説明

name

string

クラス名

owner

IPackage

所属するパッケージ  
既定値は null です。

isAbstract

bool

抽象型とするか  
既定値は false です。

戻り値​
--------------------------

*   [IClass](_extension_api_NextDesign.Core_IClass.md)

例外​
-----------------------

名前

例外クラス

説明

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合