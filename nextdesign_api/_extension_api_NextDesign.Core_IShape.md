IShape インタフェース
==============

名前空間: NextDesign.Core

説明​
-----------------------

シェイプ情報へのアクセス手段を提供します。

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

派生先​
--------------------------

名前

説明

[INode](_extension_api_NextDesign.Core_INode.md)

ノード図形要素情報へのアクセスオブジェクトです。

[ISequenceShape](_extension_api_NextDesign.Core_ISequenceShape.md)

シーケンス図のシェイプの共通インターフェースです。

[IConnector](_extension_api_NextDesign.Core_IConnector.md)

コネクタ図形要素情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[IsVisible](_extension_api_NextDesign.Core_IShape_properties_IsVisible.md)

この図形の表示状態

[Style](_extension_api_NextDesign.Core_IShape_properties_Style.md)

スタイル  
このプロパティで取得したスタイルオブジェクトへの変更はプロジェクトを保存した際に永続化されます。

[ZOrder](_extension_api_NextDesign.Core_IShape_properties_ZOrder.md)

Zオーダー  
表示順序を表し、0 が最背面となります。  
ただし、シーケンス図要素のシェイプでは 0 を返します。

メソッド​
-----------------------------

名前

説明

[BringForward](_extension_api_NextDesign.Core_IShape_methods_BringForward.md)

この図形をZオーダーの1つ前面へ移動します。  
ただし、ポート、コネクタ、シーケンス図要素のシェイプでは何も行いません。

[BringToFront](_extension_api_NextDesign.Core_IShape_methods_BringToFront.md)

この図形をZオーダーの最前面に移動します。  
ただし、ポート、コネクタ、シーケンス図要素のシェイプでは何も行いません。

[Delete](_extension_api_NextDesign.Core_IShape_methods_Delete.md)

このシェイプを削除します。  
ただし、シーケンス図要素のシェイプは削除できません。

[SendBackward](_extension_api_NextDesign.Core_IShape_methods_SendBackward.md)

この図形をZオーダーの1つ背面へ移動します。  
ただし、ポート、コネクタ、シーケンス図要素のシェイプでは何も行いません。

[SendToBack](_extension_api_NextDesign.Core_IShape_methods_SendToBack.md)

この図形をZオーダーの最背面へ移動します。  
ただし、ポート、コネクタ、シーケンス図要素のシェイプでは何も行いません。

[SetVisible](_extension_api_NextDesign.Core_IShape_methods_SetVisible.md)

この図形の表示/非表示を切り替えます。

[SetZOrder](_extension_api_NextDesign.Core_IShape_methods_SetZOrder.md)

Zオーダーを設定します。  
ただし、ポート、コネクタ、シーケンス図要素のシェイプでは何も行いません。