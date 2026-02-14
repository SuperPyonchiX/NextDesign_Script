IProductLineModel.ProductAppliedState プロパティ
===========================================

名前空間: NextDesign.Core

説明​
-----------------------

現在のプロダクト適用状態。  
以下のいずれかの状態文字列を返します。

*   SpecifiedProduct : 任意のプロダクトを適用中。適用中のプロダクトは、CurrentProduct で取得できます。
*   Master : プロダクト適用なし（150%モデル表示）。この状態の場合、CurrentProduct は null を返します。

    string ProductAppliedState { get; }

型​
--------------------

*   string