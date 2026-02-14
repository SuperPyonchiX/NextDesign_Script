IProject.GetModelById メソッド
==========================

名前空間: NextDesign.Core

説明​
-----------------------

このプロジェクトから指定された識別子のモデルを取得します。  
指定されたモデルが見つからない場合は null を返します。  
なお、この呼び出しでは、関連は取得できません。関連を取得する場合は、GetRelationshipById()を使用してください。

この呼び出しでは、プロジェクト読み込み後に削除されたモデルも対象となります。  
取得したモデルが削除されているかは、IModel.IsDeleted で評価してください。

引数​
-----------------------

名前

型

説明

identifier

string

モデルの識別子  
  
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

identifier に null または空文字列を指定した場合