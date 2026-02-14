IProject.GetRelationshipById メソッド
=================================

名前空間: NextDesign.Core

説明​
-----------------------

このプロジェクトから指定された識別子の関連を取得します。  
指定された関連が見つからない場合は null を返します。

この呼び出しでは、プロジェクト読み込み後に削除された関連も対象となります。  
取得した関連が削除されているかは、IRelationship.IsDeleted を評価してください。

引数​
-----------------------

名前

型

説明

identifier

string

関連の識別子  
  
null、または空文字列を指定することはできません。

戻り値​
--------------------------

*   [IRelationship](_extension_api_NextDesign.Core_IRelationship.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

identifier に null または空文字列を指定した場合