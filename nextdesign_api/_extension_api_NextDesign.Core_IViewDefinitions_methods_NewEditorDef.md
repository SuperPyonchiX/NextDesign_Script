IViewDefinitions.NewEditorDef メソッド
==================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したモデルクラスのエディタ定義を生成します。

引数​
-----------------------

名前

型

説明

name

string

エディタ定義名

modelClass

IClass

モデルのメタクラス

type

string

エディタの種類  
\- "ERDiagram" : ERダイアグラム  
\- "TreeDiagram" : ツリーダイアグラム  
\- "DocumentForm" : ドキュメントフォーム  
\- "TreeGrid" : ツリーグリッド

戻り値​
--------------------------

*   [IEditorDef](_extension_api_NextDesign.Core_IEditorDef.md)