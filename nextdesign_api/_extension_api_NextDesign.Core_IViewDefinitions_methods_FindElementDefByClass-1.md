IViewDefinitions.FindElementDefByClass(IEditorDef,IClass,string) メソッド
=====================================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したエディタ定義から指定したモデルクラスに対応するエディタ要素定義を検索します。

引数​
-----------------------

名前

型

説明

editor

IEditorDef

エディタ定義

modelClass

IClass

モデルのメタクラス

name

string

エディタ要素定義名。既定値は null です。

戻り値​
--------------------------

*   IElementDefCollection

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

editor に nullを指定した場合  
modelClass に nullを指定した場合