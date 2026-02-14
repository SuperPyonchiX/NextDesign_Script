IViewDefinitions.UnregisterStyleCallback(IElementDef) メソッド
==========================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したエディタ要素定義より生成されるエディタ要素の全てのコールバック関数を登録解除します。  
・エクステンションのDeactivate時に呼び出すことを推奨します。

引数​
-----------------------

名前

型

説明

elementDef

IElementDef

エディタ要素定義

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

elementDef に null を指定した場合