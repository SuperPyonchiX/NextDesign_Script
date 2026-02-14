IModel.GetEditor メソッド
=====================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスに対応する指定した名前のエディタを取得します。  
このメソッドは、IContextOption.EditorAccessModeを評価します。

該当するエディタが見つからない場合は、null を返します。

EditorAccessModeの指定によっては、最新のモデルの変更が同期されません。  
そのため、モデルを変更してから一度も表示していないエディタ等では、正しい情報が取得できない場合があることに注意してください。

引数​
-----------------------

名前

型

説明

editorName

string

エディタ名  
  
null、または空文字列は指摘できません。

戻り値​
--------------------------

*   [IEditor](_extension_api_NextDesign.Core_IEditor.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

editorName に null、または空文字列 を指定した場合