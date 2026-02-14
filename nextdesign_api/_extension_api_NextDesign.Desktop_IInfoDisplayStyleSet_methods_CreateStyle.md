IInfoDisplayStyleSet.CreateStyle メソッド
=====================================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定された名前のスタイルを作成します。  
ただし、該当する名前のスタイルが既に存在する場合は、そのスタイルを返します。

引数​
-----------------------

名前

型

説明

name

string

スタイル名  
  
null を指定することはできません。

戻り値​
--------------------------

*   [IInfoDisplayStyle](_extension_api_NextDesign.Desktop_IInfoDisplayStyle.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

name に null を指定した場合