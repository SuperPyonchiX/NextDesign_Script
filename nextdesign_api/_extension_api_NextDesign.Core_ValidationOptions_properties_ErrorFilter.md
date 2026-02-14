ValidationOptions.ErrorFilter プロパティ
===================================

名前空間: NextDesign.Core

説明​
-----------------------

検証するエラーコードを取得、または設定します。  
他のオプション指定とはOR条件で評価します。  
設定できるエラーコードは以下です。

*   System.Metamodel.UpperBound
*   System.Metamodel.LowerBound
*   System.Metamodel.PathConstraints
*   System.ProductLine.FeatureFormula
*   System.ProductLine.FeatureUniqueness
*   System.ProductLine.FeatureConstraints
*   System.ProductLine.FeatureStructure
*   System.ProductLine.ProductConfiguration

    IList<string> ErrorFilter { get; set; }

型​
--------------------

*   IList<string>