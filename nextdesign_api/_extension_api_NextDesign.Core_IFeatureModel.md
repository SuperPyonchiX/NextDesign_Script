IFeatureModel インタフェース
=====================

名前空間: NextDesign.Core

説明​
-----------------------

プロダクトの特徴（フィーチャ）を構造化した管理モデルに対するアクセスオブジェクトです。

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

[AllFeatures](_extension_api_NextDesign.Core_IFeatureModel_properties_AllFeatures.md)

このモデル以下で保持するすべてのフィーチャ一覧  
  
このプロパティで取得したフィーチャ一覧の順序は不定です。  
深さ優先の順序で取得したい場合は、IModel.GetAllChildrenで取得したモデルをIFeatureにキャストして下さい。

[RootFeatures](_extension_api_NextDesign.Core_IFeatureModel_properties_RootFeatures.md)

フィーチャツリーの基点となるフィーチャ一覧

メソッド​
-----------------------------

名前

説明

[AddFeatureConstraint](_extension_api_NextDesign.Core_IFeatureModel_methods_AddFeatureConstraint.md)

指定されたフィーチャ間に指定した種類の制約関係を追加します。

[AddNewFeature](_extension_api_NextDesign.Core_IFeatureModel_methods_AddNewFeature.md)

新しい基点フィーチャを追加します。

[AddNewFeatureAt](_extension_api_NextDesign.Core_IFeatureModel_methods_AddNewFeatureAt.md)

指定されたフィーチャの子要素として新しいフィーチャを追加します。

[GetFeature](_extension_api_NextDesign.Core_IFeatureModel_methods_GetFeature.md)

このモデル以下で保持するすべてのフィーチャ一から、指定された名前のフィーチャを取得します。  
該当するフィーチャが存在しない場合は null を返します。

[GetFeatureConstraint](_extension_api_NextDesign.Core_IFeatureModel_methods_GetFeatureConstraint.md)

指定されたフィーチャ間の指定した種類の制約関係を取得します。

[RemoveFeature](_extension_api_NextDesign.Core_IFeatureModel_methods_RemoveFeature.md)

指定されたフィーチャを削除します。  
削除対象のフィーチャが子フィーチャを持つ場合はまとめて削除します。

[RemoveFeatureByName](_extension_api_NextDesign.Core_IFeatureModel_methods_RemoveFeatureByName.md)

指定された名前のフィーチャを削除します。  
削除対象のフィーチャが子フィーチャを持つ場合はまとめて削除します。

[RemoveFeatureConstraint](_extension_api_NextDesign.Core_IFeatureModel_methods_RemoveFeatureConstraint.md)

指定されたフィーチャ間の指定した種類の制約関係を削除します。