IProductLineModel インタフェース
=========================

名前空間: NextDesign.Core

説明​
-----------------------

プロダクトライン開発支援モデルに対するアクセスオブジェクトです。

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

[ConfigurationModel](_extension_api_NextDesign.Core_IProductLineModel_properties_ConfigurationModel.md)

コンフィグレーションモデル

[CurrentProduct](_extension_api_NextDesign.Core_IProductLineModel_properties_CurrentProduct.md)

現在適用状態のプロダクト。  
適用プロダクトなし（150%モデル表示）の場合は null を返します。

[FeatureModels](_extension_api_NextDesign.Core_IProductLineModel_properties_FeatureModels.md)

フィーチャモデル一覧

[ProductAppliedState](_extension_api_NextDesign.Core_IProductLineModel_properties_ProductAppliedState.md)

現在のプロダクト適用状態。  
以下のいずれかの状態文字列を返します。  
\- SpecifiedProduct : 任意のプロダクトを適用中。適用中のプロダクトは、CurrentProduct で取得できます。  
\- Master : プロダクト適用なし（150%モデル表示）。この状態の場合、CurrentProduct は null を返します。

メソッド​
-----------------------------

名前

説明

[AddNewFeatureModel](_extension_api_NextDesign.Core_IProductLineModel_methods_AddNewFeatureModel.md)

新しいフィーチャモデルを追加します。

[ApplyProduct](_extension_api_NextDesign.Core_IProductLineModel_methods_ApplyProduct.md)

指定されたプロダクトを適用します。  
プロダクト適用後に、フィーチャモデル構造や、プロダクト適用条件式を変更した場合は、このメソッドを呼び出すことでプロダクト適用結果が再計算されます。

[ApplyProductBy](_extension_api_NextDesign.Core_IProductLineModel_methods_ApplyProductBy.md)

指定された名前のプロダクトを適用して、カレントプロダクトに設定します。

[ExportAppliedProject](_extension_api_NextDesign.Core_IProductLineModel_methods_ExportAppliedProject.md)

指定されたプロダクトを適用したプロジェクトを指定されたパスにエクスポートします。  
エクスポートしたプロジェクトは以下の状態となります。  
\- プロファイルはエクスポート元と同一  
\- 指定されたプロダクトで有効なモデル要素、エディタのみが存在する  
\- プロダクトラインモデル（フィーチャモデル、コンフィグレーションモデル）はなし  
\- ユニット分割はなし

[RemoveFeatureModel](_extension_api_NextDesign.Core_IProductLineModel_methods_RemoveFeatureModel.md)

指定されたフィーチャモデルを削除します。

[RemoveFeatureModelByName](_extension_api_NextDesign.Core_IProductLineModel_methods_RemoveFeatureModelByName.md)

指定された名前のフィーチャモデルを削除します。