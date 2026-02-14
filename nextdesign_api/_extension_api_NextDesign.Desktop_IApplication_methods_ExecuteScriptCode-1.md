IApplication.ExecuteScriptCode(string,string,IScriptParams) メソッド
================================================================

名前空間: NextDesign.Desktop

説明​
-----------------------

与えられたスクリプトコードを実行します。

引数​
-----------------------

名前

型

説明

code

string

スクリプトコード

lang

string

スクリプト言語  
\- "cs"：C#

scriptParams

IScriptParams

スクリプトパラメータ

戻り値​
--------------------------

*   object

注釈​
-----------------------

スクリプトの戻り値は、スクリプトのreturn文の結果オブジェクトを返します。  
スクリプトにreturn文がない場合は null を返します。