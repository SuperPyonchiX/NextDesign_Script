ValidationOptions インタフェース
=========================

名前空間: NextDesign.Core

説明​
-----------------------

エラーチェックの検証オプションです。

所属エリア​
--------------------------------

名前

説明

[モデル](_extension_api_overview_model.md)

モデルにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[ErrorFilter](_extension_api_NextDesign.Core_ValidationOptions_properties_ErrorFilter.md)

検証するエラーコードを取得、または設定します。  
他のオプション指定とはOR条件で評価します。  
設定できるエラーコードは以下です。  
\- System.Metamodel.UpperBound  
\- System.Metamodel.LowerBound  
\- System.Metamodel.PathConstraints  
\- System.ProductLine.FeatureFormula  
\- System.ProductLine.FeatureUniqueness  
\- System.ProductLine.FeatureConstraints  
\- System.ProductLine.FeatureStructure  
\- System.ProductLine.ProductConfiguration

[RaiseValidateEvents](_extension_api_NextDesign.Core_ValidationOptions_properties_RaiseValidateEvents.md)

モデル検証時イベントを発行するかどうかを取得、または設定します。  
既定値は true です。  
false を指定した場合、モデル検証時イベントが発行されません。

[Recursive](_extension_api_NextDesign.Core_ValidationOptions_properties_Recursive.md)

子要素を再帰的に検証するかを取得、または設定します。  
既定値は true です。

[ValidateMetamodelConstraints](_extension_api_NextDesign.Core_ValidationOptions_properties_ValidateMetamodelConstraints.md)

メタモデルの整合検証(多重度検証、パス制約検証)を行うかを取得、または設定します。  
既定値は true です。

[ValidateProductLineConstraints](_extension_api_NextDesign.Core_ValidationOptions_properties_ValidateProductLineConstraints.md)

プロダクトラインの整合検証(フィーチャ条件式検証、フィーチャ構造の検証、フィーチャ間制約の検証、フィーチャのユニーク検証、プロダクトのコンフィグレーション検証)を行うかを取得、または設定します。  
既定値は true です。