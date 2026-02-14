IProject.GetModelByPath(string,string) メソッド
===========================================

名前空間: NextDesign.Core

説明​
-----------------------

指定された基点要素の識別子を持つモデルから、指定された相対パスのモデルを取得します。  
指定したモデル階層パスのモデルが存在しない場合は null を返します。

なお、一致するモデル階層パスが複数ある場合、一番最初に見つかったモデルを返します。

引数​
------------------------

名前

型

説明

baseElementId

string

探索の基点となるモデルId  
  
null、または空文字列を指定することはできません。

elementPath

string

baseElementId で取得したモデルからの相対パス  
  
null、または空文字列を指定することはできません。

戻り値​
--------------------------

*   [IModel](_extension_api_NextDesign.Core_IModel.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

baseElementId に null または空文字列を指定した場合  
elementPath に null または空文字列を指定した場合  
baseElementId で指定された基点要素の識別子を持つモデルが見つからない場合  
baseElementId で指定された基点要素の識別子を持つモデルが一時プロキシの場合