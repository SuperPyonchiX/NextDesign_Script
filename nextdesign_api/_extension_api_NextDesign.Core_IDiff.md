IDiff インタフェース
=============

名前空間: NextDesign.Core

説明​
-----------------------

任意のプロジェクト間でのモデル情報の比較操作を提供します。

所属エリア​
--------------------------------

名前

説明

[モデル差分・比較](_extension_api_overview_model-diff.md)

モデルの比較操作や差分情報にアクセスするAPI群です。

メソッド​
-----------------------------

名前

説明

[ComputeModels(IProject,IProject,IEnumerable<IModelUnit>)](_extension_api_NextDesign.Core_IDiff_methods_ComputeModels-2.md)

比較対象のモデルユニットを指定して、2-way比較により、プロジェクト間の差分抽出を実施します。  
指定したモデルユニットに含まれないモデルについては、差分があっても抽出されません。  
また、target、oldの各プロジェクトにて比較対象のモデルユニットを事前にロードしてください。  
比較対象のモデルユニットがロードされていない状態で実行すると、意図しない追加・削除差分が抽出される可能性があります。

[ComputeModels(IProject,IProject)](_extension_api_NextDesign.Core_IDiff_methods_ComputeModels-1.md)

2-way比較により、指定されたプロジェクト間の差分抽出を実施します。

[GetComparison](_extension_api_NextDesign.Core_IDiff_methods_GetComparison.md)

指定されたプロジェクトの差分情報を取得します。  
指定されたプロジェクトを対象にした、直前のComputeModelsの実行結果を取得します。

関連項目​
-----------------------------

名前

説明

モデルを比較する

APIを通してNextDesignの各種モデル情報を比較します。