IModel.GetRefRelatedModels(IRelationshipClass) メソッド
===================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定した関連クラスにより、このインスタンスと参照関係にあるインスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

なお、指定された関連クラスが、自己参照関連の場合は、関連元、関連先のいずれの関係も評価します。

引数​
-----------------------

名前

型

説明

targetClass

IRelationshipClass

関連クラス

戻り値​
--------------------------

*   IModelCollection