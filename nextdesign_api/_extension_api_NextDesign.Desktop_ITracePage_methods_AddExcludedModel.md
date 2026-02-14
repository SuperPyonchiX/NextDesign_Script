ITracePage.AddExcludedModel メソッド
================================

名前空間: NextDesign.Desktop

説明​
-----------------------

新しいトレース除外対象のモデル情報を追加します。  
このメソッドでは、指定された識別子のモデルの存在を確認しません。

引数​
-----------------------

名前

型

説明

modelId

string

モデルの識別子

direction

string

トレースの除外方向  
\- "Source" : 導出元への関連を除外する  
\- "Target" : 導出先への関連を除外する  
\- "Both" : 導出元、および導出先への関連を除外する  

reason

string

除外理由

戻り値​
--------------------------

*   [ITraceCoverageExcludedModel](_extension_api_NextDesign.Desktop_ITraceCoverageExcludedModel.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

modelId、または reason に null、または空文字列 を指定した場合  
direction に規定された文字列以外を指定した場合