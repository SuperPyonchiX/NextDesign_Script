IViewDefinitions.UnregisterStyleCallback(string,string) メソッド
============================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素の全てのコールバック関数を登録解除します。  
・エクステンションのDeactivate時に呼び出すことを推奨します。

なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

引数​
-----------------------

名前

型

説明

editorModelClassName

string

エディタのモデルクラス名

elementModelClassName

string

エディタ要素のモデルクラス名

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

editorModelClassName に null、または空文字を指定した場合  
elementModelClassName に null、または空文字を指定した場合