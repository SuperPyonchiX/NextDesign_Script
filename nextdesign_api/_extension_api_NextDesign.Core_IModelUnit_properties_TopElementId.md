IModelUnit.TopElementId プロパティ
=============================

名前空間: NextDesign.Core

説明​
-----------------------

このユニットにおける基点要素の識別子。  
基点要素がない場合は null を返します。

通常、基点要素はユニット種別により次の要素となります。

*   "Project" : プロジェクト
*   "Model" : ユニットに分割したモデル
*   "Profile" : プロファイル
*   上記以外 : なし

    string TopElementId { get; }

型​
--------------------

*   string