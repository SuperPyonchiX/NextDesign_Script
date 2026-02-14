IDiff.ComputeModels(IProject,IProject) メソッド
===========================================

名前空間: NextDesign.Core

説明​
-----------------------

2-way比較により、指定されたプロジェクト間の差分抽出を実施します。

引数​
-----------------------

名前

型

説明

target

IProject

比較元になるプロジェクトです。

old

IProject

比較先になる、targetより古い情報をもつプロジェクト

戻り値​
--------------------------

*   [IModelComparison](_extension_api_NextDesign.Core_IModelComparison.md)

例外​
------------------------

名前

例外クラス

説明

不正操作

ExtensionInvalidOperationException

指定されたプロジェクトに未ロードのモデルファイルが含まれる場合

注釈​
-----------------------

比較対象先プロジェクト(target)で部分ロードした場合、比較対象元のプロジェクト(old)でも同じモデルユニットをロードすることで比較可能です。