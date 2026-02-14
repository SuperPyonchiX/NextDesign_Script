ITreeGridNode インタフェース
=====================

名前空間: NextDesign.Core

説明​
-----------------------

ツリーグリッドのノード情報へのアクセスオブジェクトです。

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

[IEditorElement](_extension_api_NextDesign.Core_IEditorElement.md)

エディタ要素へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[Cells](_extension_api_NextDesign.Core_ITreeGridNode_properties_Cells.md)

このノードのセル

[Children](_extension_api_NextDesign.Core_ITreeGridNode_properties_Children.md)

ツリーの子ノード  
順序は子要素のUI上の表示順序と同一となります。

[IsExpanded](_extension_api_NextDesign.Core_ITreeGridNode_properties_IsExpanded.md)

このノードが展開されているか

[Parent](_extension_api_NextDesign.Core_ITreeGridNode_properties_Parent.md)

ツリーの親ノード

メソッド​
-----------------------------

名前

説明

[GetCellDisplayValues](_extension_api_NextDesign.Core_ITreeGridNode_methods_GetCellDisplayValues.md)

このノードのすべてのセル表示文字列を取得します。  
値が存在しないセルは空の文字列を返します。  
  
取得できる文字列は、GetCellValueString() を参照してください。

[GetCellValue](_extension_api_NextDesign.Core_ITreeGridNode_methods_GetCellValue.md)

このノードの指定された列のセルの値を取得します。  
セルの値が存在しない場合はnullを返します。  
  
取得できる値の実際の型は、列のデータ型に依存します。  
\- bool型 : bool値  
\- 数値型 : 数値  
\- 文字列型 : 文字列  
\- 列挙型 : IEnumLiteralオブジェクト  
\- リッチテキスト型 : プレーンテキスト  
\- クラス型（モデル参照） : IModelCollectionオブジェクト(多重度1の要素でもコレクションを返します)

[GetCellValueAt](_extension_api_NextDesign.Core_ITreeGridNode_methods_GetCellValueAt.md)

このノードの指定されたインデックスのセルの値を取得します。  
セルの値が存在しない場合はnullを返します。  
  
取得できる値の実際の型は、GetCellValue() を参照してください。

[GetCellValueString](_extension_api_NextDesign.Core_ITreeGridNode_methods_GetCellValueString.md)

このノードの指定された列のセルの値を文字列形式で取得します。  
セルの値が存在しない場合は空の文字列を返します。  
  
取得できる文字列は、列のデータ型に依存します。  
\- bool型 : "True" or "False"  
\- 数値型 : 数値の文字列表現  
\- 文字列型 : 文字列  
\- 列挙型 : リテラル文字列  
\- リッチテキスト型 : プレーンテキスト  
\- クラス型（モデル参照） : モデルの表示名（複数モデルの場合は、スペース区切り）  
  
\[モデルの表示名\]  
モデルの表示名は、UI上の表示と同様に、以下のフォーマットとなります。  
`\{親要素名\}/$\{モデル名\}`  
（例）ユースケース/ドライバ

[GetCellValueStringAt](_extension_api_NextDesign.Core_ITreeGridNode_methods_GetCellValueStringAt.md)

このノードの指定されたインデックスのセルの値を文字列形式で取得します。  
セルの値が存在しない場合は空の文字列を返します。  
  
取得できる文字列は、GetCellValueString() を参照してください。

[GetSelectedCells](_extension_api_NextDesign.Core_ITreeGridNode_methods_GetSelectedCells.md)

このノードで選択されているセルを取得します。

[HasCellValue](_extension_api_NextDesign.Core_ITreeGridNode_methods_HasCellValue.md)

このノードで指定された列のセルの値が存在するか調べます。  
セルの値が存在する場合は True を返します。

[HasCellValueAt](_extension_api_NextDesign.Core_ITreeGridNode_methods_HasCellValueAt.md)

このノードで指定されたインデックスのセルの値が存在するか調べます。  
セルの値が存在する場合は True を返します。

[IsCellSelected](_extension_api_NextDesign.Core_ITreeGridNode_methods_IsCellSelected.md)

このノードで指定された列のセルが選択されているか調べます。  
セルが選択されている場合は True を返します。

[IsCellSelectedAt](_extension_api_NextDesign.Core_ITreeGridNode_methods_IsCellSelectedAt.md)

このノードで指定されたインデックスのセルが選択されているか調べます。  
セルが選択されている場合は True を返します。

注釈​
-----------------------

ツリーグリッドノードでは、基底の制約に追加して、次のプロパティもサポートされません。  
・識別子（Id）  
これらのプロパティにアクセスした場合、例外がスローされます。