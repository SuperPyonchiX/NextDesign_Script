IFeatureModel.GetFeatureConstraint メソッド
=======================================

名前空間: NextDesign.Core

説明​
-----------------------

指定されたフィーチャ間の指定した種類の制約関係を取得します。

引数​
-----------------------

名前

型

説明

source

IFeature

制約元フィーチャ

opposite

IFeature

制約先フィーチャ

kind

string

制約種類  
指定できる値は IFeature の AddConstraint メソッドを参照してください。

戻り値​
--------------------------

*   [IRelationship](_extension_api_NextDesign.Core_IRelationship.md)