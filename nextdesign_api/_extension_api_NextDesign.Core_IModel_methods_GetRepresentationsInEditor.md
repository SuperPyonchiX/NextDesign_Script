IModel.GetRepresentationsInEditor メソッド
======================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したエディタ内でこのインスタンスに対応するすべての表現情報を取得します。  
このメソッドは、IContextOption.EditorAccessModeを評価します。

該当する要素がない場合は、空のコレクションを返します。

EditorAccessModeの指定によっては、最新のモデルの変更が同期されません。  
そのため、モデルを変更してから一度も表示していないエディタ等では、正しい情報が取得できない場合があることに注意してください。

引数​
-----------------------

名前

型

説明

editor

IEditor

エディタ  
  
null は指定できません。

戻り値​
--------------------------

*   IRepresentationCollection

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

editor に null を指定した場合