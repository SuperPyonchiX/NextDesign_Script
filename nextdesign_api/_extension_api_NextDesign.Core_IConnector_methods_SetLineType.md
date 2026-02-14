IConnector.SetLineType メソッド
===========================

名前空間: NextDesign.Core

説明​
-----------------------

コネクタの線の種類を設定します。

引数​
-----------------------

名前

型

説明

lineTypeString

string

線の種類  
  
以下のいずれかの値を指定できます。  
\- "Straight" : 直線  
\- "Orthogonal" : 折れ線  
\- "Bezier1Dimension" : 1次ベジェ曲線  
\- "Bezier2Dimension" : 2次ベジェ曲線  
  
null 、もしくは空白は指定できません。

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

lineTypeString に null 、もしくは空白を指定した場合  
lineTypeString に "Tree" を指定した場合  
lineTypeString に線の種類以外の文字列を指定した場合

不正操作

ExtensionInvalidOperationException

ツリーダイアグラムのコネクタに対し設定しようとした場合  
フィーチャツリーダイアグラムのコネクタに対し設定しようとした場合