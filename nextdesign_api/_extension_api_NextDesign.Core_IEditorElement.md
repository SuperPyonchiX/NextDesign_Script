IEditorElement インタフェース
======================

名前空間: NextDesign.Core

説明​
-----------------------

エディタ要素へのアクセスオブジェクトです。

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

[ITreeGridNode](_extension_api_NextDesign.Core_ITreeGridNode.md)

ツリーグリッドのノード情報へのアクセスオブジェクトです。

[IShape](_extension_api_NextDesign.Core_IShape.md)

シェイプ情報へのアクセス手段を提供します。

[IFormElement](_extension_api_NextDesign.Core_IFormElement.md)

フォーム要素情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[Editor](_extension_api_NextDesign.Core_IEditorElement_properties_Editor.md)

この要素を保持するエディタ

[ElementDefinition](_extension_api_NextDesign.Core_IEditorElement_properties_ElementDefinition.md)

このエディタ要素のビュー定義  
エディタ要素がビュー定義と関連しない場合は null を返します。

[ElementOwnerRelationship](_extension_api_NextDesign.Core_IEditorElement_properties_ElementOwnerRelationship.md)

この要素が対応するモデルと、この要素の親要素が対応するモデルとの関連を取得します。  
例えば、次のようなモデル構造に対して  
モデルA  
　┗ (関連A-B)  
　　　┗ モデルB  
ダイアグラムエディタ構造では、次のようにマッピングされます。  
IDiagram { Model=モデルA }  
┗ NodeShape { Model=モデルB, ElementOwnerRelationship=関連A-B }  
  
なお、親要素のモデルとの関連を特定できない場合は null を返します。

[IsSelected](_extension_api_NextDesign.Core_IEditorElement_properties_IsSelected.md)

この要素が選択されているか

[Path](_extension_api_NextDesign.Core_IEditorElement_properties_Path.md)

このエディタ要素がマッピングしているフィールド（パス）  
直接モデルにマッピングしている場合は空文字となります。

メソッド​
-----------------------------

名前

説明

[GetValue](_extension_api_NextDesign.Core_IEditorElement_methods_GetValue.md)

このエディタ要素に対応する値を取得します。  
このエディタ要素がフィールドに対応する場合、このメソッドの呼び出しは、this.Model.GetField(this.Path) と等価になります。  
このエディタ要素がクラスに対応する場合、このメソッドの呼び出しは、this.Model と等価になります。

[SetSelected](_extension_api_NextDesign.Core_IEditorElement_methods_SetSelected.md)

この要素を選択状態を切り替えます。