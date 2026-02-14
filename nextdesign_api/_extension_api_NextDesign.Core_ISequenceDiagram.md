ISequenceDiagram インタフェース
========================

名前空間: NextDesign.Core

説明​
-----------------------

シーケンス図（ダイアグラム）情報へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[シーケンスエディタ](_extension_api_overview_sequence-editor.md)

シーケンス図にアクセスするAPI群です。

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

[Destructions](_extension_api_NextDesign.Core_ISequenceDiagram_properties_Destructions.md)

破棄一覧

[ExecutionSpecifications](_extension_api_NextDesign.Core_ISequenceDiagram_properties_ExecutionSpecifications.md)

実行仕様一覧

[Fragments](_extension_api_NextDesign.Core_ISequenceDiagram_properties_Fragments.md)

複合フラグメント一覧

[Frame](_extension_api_NextDesign.Core_ISequenceDiagram_properties_Frame.md)

フレーム

[InteractionUses](_extension_api_NextDesign.Core_ISequenceDiagram_properties_InteractionUses.md)

相互作用の利用一覧

[Lifelines](_extension_api_NextDesign.Core_ISequenceDiagram_properties_Lifelines.md)

ライフライン一覧

[MessageEnds](_extension_api_NextDesign.Core_ISequenceDiagram_properties_MessageEnds.md)

メッセージ端一覧

[Messages](_extension_api_NextDesign.Core_ISequenceDiagram_properties_Messages.md)

メッセージ一覧

[NoteAnchors](_extension_api_NextDesign.Core_ISequenceDiagram_properties_NoteAnchors.md)

ノートアンカー一覧

[Notes](_extension_api_NextDesign.Core_ISequenceDiagram_properties_Notes.md)

ノート一覧

[Shapes](_extension_api_NextDesign.Core_ISequenceDiagram_properties_Shapes.md)

全てのシェイプの一覧  
コレクションの順序は不定です。  
このプロパティでは、ライフライン、メッセージ、実行仕様等のダイアグラム上の全てのシェイプを取得できます。

メソッド​
-----------------------------

名前

説明

[GetSelectedShapes](_extension_api_NextDesign.Core_ISequenceDiagram_methods_GetSelectedShapes.md)

エディタで選択されているシェイプを取得します。  
選択要素のコレクションの順序は不定です。

[GetShapeById](_extension_api_NextDesign.Core_ISequenceDiagram_methods_GetShapeById.md)

指定された識別子のシェイプを取得します。  
該当する識別子のシェイプが存在しない場合は null を返します。

[GetShapesByModel](_extension_api_NextDesign.Core_ISequenceDiagram_methods_GetShapesByModel.md)

指定されたモデルに対応するシェイプを取得します。