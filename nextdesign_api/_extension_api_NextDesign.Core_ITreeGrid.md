ITreeGrid インタフェース
=================

名前空間: NextDesign.Core

説明​
-----------------------

ツリーグリッド情報へのアクセスオブジェクトです。  
IEditorのEditorTypeが"TreeGrid"の場合、このインタフェース型にキャストすることでツリーグリッド固有の情報にアクセスすることができます。

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

[IEditor](_extension_api_NextDesign.Core_IEditor.md)

エディタ情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[Columns](_extension_api_NextDesign.Core_ITreeGrid_properties_Columns.md)

表示列情報  
順序は、UI上の表示順序（左側が0番目）と同一となります。  
また、UI上非表示の列は含まれません。

[FixedColumnIndex](_extension_api_NextDesign.Core_ITreeGrid_properties_FixedColumnIndex.md)

固定列のインデックス

[Root](_extension_api_NextDesign.Core_ITreeGrid_properties_Root.md)

ツリーグリッドの基点のツリーノード

[ShowSingleLine](_extension_api_NextDesign.Core_ITreeGrid_properties_ShowSingleLine.md)

要素を1行分の高さで表示するか

メソッド​
-----------------------------

名前

説明

[GetSelectedNodes](_extension_api_NextDesign.Core_ITreeGrid_methods_GetSelectedNodes.md)

エディタで選択されているツリーノードを取得します。  
選択要素のコレクションの順序は不定です。