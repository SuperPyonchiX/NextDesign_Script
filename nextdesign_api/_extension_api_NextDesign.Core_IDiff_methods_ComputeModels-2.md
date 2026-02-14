IDiff.ComputeModels(IProject,IProject,IEnumerable<IModelUnit>) メソッド
===================================================================

名前空間: NextDesign.Core

説明​
-----------------------

比較対象のモデルユニットを指定して、2-way比較により、プロジェクト間の差分抽出を実施します。  
指定したモデルユニットに含まれないモデルについては、差分があっても抽出されません。  
また、target、oldの各プロジェクトにて比較対象のモデルユニットを事前にロードしてください。  
比較対象のモデルユニットがロードされていない状態で実行すると、意図しない追加・削除差分が抽出される可能性があります。

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

比較先になる、targetより古い情報をもつプロジェクトです。

targetModelUnits

IEnumerable<IModelUnit>

比較対象のモデルユニットの一覧です。target または、old のモデルユニットを指定してください。

戻り値​
--------------------------

*   [IModelComparison](_extension_api_NextDesign.Core_IModelComparison.md)

例外​
-----------------------

名前

例外クラス

説明

サポート外

ExtensionNotSupportedException

アプリケーションの現在のエディションが対応していない場合