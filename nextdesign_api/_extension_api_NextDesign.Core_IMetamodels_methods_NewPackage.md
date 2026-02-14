IMetamodels.NewPackage メソッド
===========================

名前空間: NextDesign.Core

説明​
-----------------------

新しいパッケージを生成します。

引数​
-----------------------

名前

型

説明

name

string

パッケージ名

parent

IPackage

親パッケージ  
既定値はnullです。

戻り値​
--------------------------

*   [IPackage](_extension_api_NextDesign.Core_IPackage.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

パッケージ名 に null/空文字/使用不可文字 が指定された場合

一意制約違反

ExtensionDuplicationException

指定された名前のパッケージが既に存在する場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合