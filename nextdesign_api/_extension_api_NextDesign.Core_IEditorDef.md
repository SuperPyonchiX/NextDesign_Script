IEditorDef インタフェース
==================

名前空間: NextDesign.Core

説明​
-----------------------

エディタ定義へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[プロファイル](_extension_api_overview_profile.md)

プロファイルにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IViewDefinition](_extension_api_NextDesign.Core_IViewDefinition.md)

表現情報へのアクセスオブジェクトを表す基底インタフェースです。

派生先​
--------------------------

名前

説明

[ICustomEditorDefinition](_extension_api_NextDesign.Core_ICustomEditorDefinition.md)

カスタムエディタ定義へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[Elements](_extension_api_NextDesign.Core_IEditorDef_properties_Elements.md)

エディタ要素定義一覧

[Type](_extension_api_NextDesign.Core_IEditorDef_properties_Type.md)

エディタの種類  
\- "ERDiagram" : ERダイアグラム  
\- "TreeDiagram" : ツリーダイアグラム  
\- "SequenceDiagram" : シーケンスダイアグラム  
\- "DocumentForm" : ドキュメントフォーム  
\- "TreeGrid" : ツリーグリッド  
\- "Custom" : カスタム

メソッド​
-----------------------------

名前

説明

[GetElementsByTag](_extension_api_NextDesign.Core_IEditorDef_methods_GetElementsByTag.md)

指定されたタグが付与されたエディタ要素定義を取得します。