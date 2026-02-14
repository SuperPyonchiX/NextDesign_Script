ITraceLane.FindNode メソッド
========================

名前空間: NextDesign.Desktop

説明​
-----------------------

このレーン内の指定されたモデルに対応するノードを検索します。  
モデルフィルタや関連フィルタが適用されている場合、フィルタが適用された状態で検索します。  
該当するノードがレーン内で見つからない場合は null を返します。

引数​
-----------------------

名前

型

説明

model

IModel

検索対象のモデル

戻り値​
--------------------------

*   [ITraceNode](_extension_api_NextDesign.Desktop_ITraceNode.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

model に null を指定した場合