IFeatureModel.AllFeatures プロパティ
===============================

名前空間: NextDesign.Core

説明​
-----------------------

このモデル以下で保持するすべてのフィーチャ一覧

このプロパティで取得したフィーチャ一覧の順序は不定です。  
深さ優先の順序で取得したい場合は、IModel.GetAllChildrenで取得したモデルをIFeatureにキャストして下さい。

    IFeatureCollection AllFeatures { get; }

型​
--------------------

*   IFeatureCollection