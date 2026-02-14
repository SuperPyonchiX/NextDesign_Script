IFeature インタフェース
================

名前空間: NextDesign.Core

説明​
-----------------------

プロダクトの特徴（フィーチャ）情報に対するアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[プロダクトライン](_extension_api_overview_productline.md)

プロダクトラインモデルにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IModel](_extension_api_NextDesign.Core_IModel.md)

NextDesignの設計モデル情報へのアクセス手段を提供します。

プロパティ​
--------------------------------

名前

説明

[IsDefaultSelected](_extension_api_NextDesign.Core_IFeature_properties_IsDefaultSelected.md)

フィーチャを初期選択状態とするか

[OwnerModel](_extension_api_NextDesign.Core_IFeature_properties_OwnerModel.md)

このフィーチャを管理するフィーチャモデル

[ParentFeature](_extension_api_NextDesign.Core_IFeature_properties_ParentFeature.md)

親フィーチャ

[SubFeatures](_extension_api_NextDesign.Core_IFeature_properties_SubFeatures.md)

サブフィーチャ一覧

[UniqueName](_extension_api_NextDesign.Core_IFeature_properties_UniqueName.md)

ユニーク名

[VariationKind](_extension_api_NextDesign.Core_IFeature_properties_VariationKind.md)

フィーチャの種類  
\- 必須 : "Mandatory"  
\- 任意 : "Optional"  
\- 1つを選択 : "Alternative"  
\- 1つ以上を選択 : "Or"

メソッド​
-----------------------------

名前

説明

[AddConstraint](_extension_api_NextDesign.Core_IFeature_methods_AddConstraint.md)

指定されたフィーチャとの間に指定した種類の制約関係を追加します。

[GetAssignedModels](_extension_api_NextDesign.Core_IFeature_methods_GetAssignedModels.md)

このフィーチャを割り当てているモデル一覧を取得します。

[RemoveAllConstraint](_extension_api_NextDesign.Core_IFeature_methods_RemoveAllConstraint.md)

このフィーチャのすべての制約関係を削除します。

[RemoveConstraint](_extension_api_NextDesign.Core_IFeature_methods_RemoveConstraint.md)

指定されたフィーチャとの間の指定した種類の制約関係を削除します。