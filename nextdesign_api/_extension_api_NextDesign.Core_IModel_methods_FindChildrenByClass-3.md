IModel.FindChildrenByClass(IClass,bool) メソッド
============================================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスの所有関係にあるインスタンスのうち指定したクラスのインスタンスを検索します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

引数​
-----------------------

名前

型

説明

targetClass

IClass

クラス

recursive

bool

所有関係を再帰的に探索するか  
既定値は、Falseです。  
Falseが指定された場合はこのインスタンスが直接所有するインスタンスから、  
Trueが指定された場合はこのインスタンスを基点とする所有関係を再帰的に探索したインスタンスから、該当する要素を検索します。

戻り値​
--------------------------

*   IModelCollection