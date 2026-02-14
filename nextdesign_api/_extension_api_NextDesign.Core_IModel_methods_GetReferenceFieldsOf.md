IModel.GetReferenceFieldsOf メソッド
================================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスのクラスが持つ参照フィールドのうち、指定したモデルを格納可能な型の参照フィールドを取得します。  
このメソッドで取得できるフィールドは、Model.Metaclass.GetReferenceFieldOf(model.Metaclass) と同じ結果となります。  
また、多重度、パス制約等のフィールドの制約については評価されません。

引数​
-----------------------

名前

型

説明

model

IModel

モデル

戻り値​
--------------------------

*   IFieldCollection