IViewDefinitions.FindElementDefByClass(IEditorDef,string,bool,string) メソッド
==========================================================================

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

modelClassName

string

モデルクラス名

fuzzy

bool

あいまい一致オプション  
既定値はtrueです。  
fuzzyにfalseを指定した場合、モデルクラス名を完全修飾名で評価します。  
同名クラスが存在する場合は、fuzzyにfalseを指定し、classNamesに完全修飾名を指定することで期待するクラスを取得することができます。

elementName

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
modelClassName に null、または空文字を指定した場合