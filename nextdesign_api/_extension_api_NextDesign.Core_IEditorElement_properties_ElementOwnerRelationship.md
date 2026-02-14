IEditorElement.ElementOwnerRelationship プロパティ
=============================================

名前空間: NextDesign.Core

説明​
-----------------------

この要素が対応するモデルと、この要素の親要素が対応するモデルとの関連を取得します。  
例えば、次のようなモデル構造に対して  
モデルA  
　┗ (関連A-B)  
　　　┗ モデルB  
ダイアグラムエディタ構造では、次のようにマッピングされます。  
IDiagram { Model=モデルA }  
┗ NodeShape { Model=モデルB, ElementOwnerRelationship=関連A-B }

なお、親要素のモデルとの関連を特定できない場合は null を返します。

    IRelationship ElementOwnerRelationship { get; }

型​
--------------------

*   [IRelationship](_extension_api_NextDesign.Core_IRelationship.md)