IDiagram インタフェース
================

名前空間: NextDesign.Core

説明​
-----------------------

ダイアグラムエディタ情報へのアクセスオブジェクトです。  
IEditorのEditorTypeが"ERDiagram"、または”TreeDiagram”の場合、このインタフェース型にキャストすることでダイアグラムエディタ固有の情報にアクセスすることができます。

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

[Connectors](_extension_api_NextDesign.Core_IDiagram_properties_Connectors.md)

コネクタシェイプ一覧

[DisplayedShapes](_extension_api_NextDesign.Core_IDiagram_properties_DisplayedShapes.md)

表示中のシェイプ一覧

[Nodes](_extension_api_NextDesign.Core_IDiagram_properties_Nodes.md)

ノードシェイプ一覧

[Shapes](_extension_api_NextDesign.Core_IDiagram_properties_Shapes.md)

シェイプ一覧

メソッド​
-----------------------------

名前

説明

[AddNodeShape](_extension_api_NextDesign.Core_IDiagram_methods_AddNodeShape.md)

指定されたモデルに対応するノードシェイプを追加します。  
ただし、対応するノードシェイプが既に非表示で存在する場合は、そのシェイプを表示します。

[CanAddNodeShape](_extension_api_NextDesign.Core_IDiagram_methods_CanAddNodeShape.md)

指定されたモデルに対応するノードシェイプを追加できるか調べます。

[GetChildNodes](_extension_api_NextDesign.Core_IDiagram_methods_GetChildNodes.md)

指定されたノードの子ノードを取得します。  
該当するノードが存在しない場合は空のコレクションを返します。

[GetConnectorByNode](_extension_api_NextDesign.Core_IDiagram_methods_GetConnectorByNode.md)

指定されたノードに接続されているコネクタを取得します。  
該当するコネクタが存在しない場合は空のコレクションを返します。

[GetSelectedShapes](_extension_api_NextDesign.Core_IDiagram_methods_GetSelectedShapes.md)

エディタで選択されているシェイプを取得します。  
選択要素のコレクションの順序は不定です。  
選択されたシェイプが存在しない場合は空のコレクションを返します。

[GetShapeById](_extension_api_NextDesign.Core_IDiagram_methods_GetShapeById.md)

指定された識別子のシェイプを取得します。  
該当する識別子のシェイプが存在しない場合は null を返します。

[GetShapesByModel](_extension_api_NextDesign.Core_IDiagram_methods_GetShapesByModel.md)

指定されたモデルに対応するシェイプを取得します。  
該当するシェイプが存在しない場合は空のコレクションを返します。

[HideShape](_extension_api_NextDesign.Core_IDiagram_methods_HideShape.md)

指定されたシェイプを非表示にします。  
マッピング対象がクラスのシェイプを指定した場合、削除します。

[HideShapes](_extension_api_NextDesign.Core_IDiagram_methods_HideShapes.md)

指定された全てのシェイプを非表示にします。  
マッピング対象がクラスのシェイプが含まれている場合、そのシェイプは削除します。

[MoveToCanvas(IEnumerable<INode>)](_extension_api_NextDesign.Core_IDiagram_methods_MoveToCanvas-2.md)

指定された全てのノードシェイプを表示上ダイアグラムが親となるように移動します。  
この時、ノードシェイプが対応するモデルの構造は変更しません。  
なお、指定するノードシェイプは次の条件をすべて満たしている必要があります。条件を満たさないノードシェイプは無視されます。  
\- ポートシェイプでない  
\- マッピング対象がフィールドでない。

[MoveToCanvas(INode)](_extension_api_NextDesign.Core_IDiagram_methods_MoveToCanvas-1.md)

指定されたノードシェイプを表示上ダイアグラムが親となるように移動します。  
この時、ノードシェイプが対応するモデルの構造は変更しません。  
なお、指定するノードシェイプは次の条件をすべて満たしている必要があります。条件を満たさないノードシェイプが指定された場合は何も行いません。  
\- ポートシェイプでない  
\- マッピング対象がフィールドでない。

[Relocate](_extension_api_NextDesign.Core_IDiagram_methods_Relocate-1.md)

全てのノードを再配置します。

[Relocate(IEnumerable<INode>)](_extension_api_NextDesign.Core_IDiagram_methods_Relocate-2.md)

指定されたノードを再配置します。

[Reroute(bool,IEnumerable<IConnector>)](_extension_api_NextDesign.Core_IDiagram_methods_Reroute-2.md)

指定されたコネクタの経路を再計算します。  
この処理には時間がかかることがあります。経路計算の対象に直交折れ線を含む場合、引数"avoidOverlap"に"false"を指定することで計算処理を高速にできる可能性があります。

[Reroute(bool)](_extension_api_NextDesign.Core_IDiagram_methods_Reroute-1.md)

このダイアグラム上の全てのコネクタの経路を再計算します。  
この処理には時間がかかることがあります。経路計算の対象に直交折れ線を含む場合、引数"avoidOverlap"に"false"を指定することで計算処理を高速にできる可能性があります。

[ShowShape](_extension_api_NextDesign.Core_IDiagram_methods_ShowShape.md)

指定されたシェイプを表示します。

[ShowShapes](_extension_api_NextDesign.Core_IDiagram_methods_ShowShapes.md)

指定された全てのシェイプを表示します。