IEditor インタフェース
===============

名前空間: NextDesign.Core

説明​
-----------------------

エディタ情報へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[エディタ](_extension_api_overview_editors.md)

エディタにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IRepresentation](_extension_api_NextDesign.Core_IRepresentation.md)

表現情報へのアクセスオブジェクトです。

派生先​
--------------------------

名前

説明

[IDiagram](_extension_api_NextDesign.Core_IDiagram.md)

ダイアグラムエディタ情報へのアクセスオブジェクトです。  
IEditorのEditorTypeが"ERDiagram"、または”TreeDiagram”の場合、このインタフェース型にキャストすることでダイアグラムエディタ固有の情報にアクセスすることができます。

[ITreeGrid](_extension_api_NextDesign.Core_ITreeGrid.md)

ツリーグリッド情報へのアクセスオブジェクトです。  
IEditorのEditorTypeが"TreeGrid"の場合、このインタフェース型にキャストすることでツリーグリッド固有の情報にアクセスすることができます。

[IForm](_extension_api_NextDesign.Core_IForm.md)

フォームエディタ情報へのアクセスオブジェクトです。  
IEditorのEditorTypeが"DocumentForm"の場合、このインタフェース型にキャストすることでフォーム固有の情報にアクセスすることができます。

[ICustomEditor](_extension_api_NextDesign.Core_ICustomEditor.md)

カスタムエディタ情報へのアクセスオブジェクトです。

[ISequenceDiagram](_extension_api_NextDesign.Core_ISequenceDiagram.md)

シーケンス図（ダイアグラム）情報へのアクセスオブジェクトです。

[IConfigurationEditor](_extension_api_NextDesign.Core_IConfigurationEditor.md)

コンフィグレーションエディタ情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[EditorDefinition](_extension_api_NextDesign.Core_IEditor_properties_EditorDefinition.md)

このエディタのビュー定義

[EditorType](_extension_api_NextDesign.Core_IEditor_properties_EditorType.md)

エディタ種類  
"ERDiagram" : ERダイアグラム  
"TreeDiagram" : ツリーダイアグラム  
"SequenceDiagram" : シーケンスダイアグラム  
"DocumentForm" : ドキュメントフォーム  
"TreeGrid" : ツリーグリッド  
"Custom" : カスタム

メソッド​
-----------------------------

名前

説明

[GetSelectedElements](_extension_api_NextDesign.Core_IEditor_methods_GetSelectedElements.md)

エディタで選択されている要素を取得します。  
選択要素のコレクションの順序は不定です。  
選択されている要素がない場合は、空のコレクションを返します。