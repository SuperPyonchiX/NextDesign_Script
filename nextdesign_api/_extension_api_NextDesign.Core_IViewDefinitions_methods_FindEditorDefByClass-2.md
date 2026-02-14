IViewDefinitions.FindEditorDefByClass(string,bool,string) メソッド
==============================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスに定義されたエディタのビュー定義を検索します。

引数​
-----------------------

名前

型

説明

modelClassName

string

検索対象のモデルクラス名

fuzzy

bool

あいまい一致オプション  
既定値はtrueです。  
fuzzyにfalseを指定した場合、モデルクラス名を完全修飾名で評価します。  
同名クラスが存在する場合は、fuzzyにfalseを指定し、classNamesに完全修飾名を指定することで期待するクラスを取得することができます。

editorName

string

検索するエディタ定義名  
この引数で指定された文字列に、ビュー名が一致するビュー定義を検索します。(表示名では検索されません。）  
nullが指定された場合は、クラスに定義されたすべてのビュー定義を返します。  
検索で合致するものが見つからない場合は、空のコレクションを返します。  
初期値はnullです。

戻り値​
--------------------------

*   IEditorDefCollection

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

modelClassName に null、または空文字を指定した場合