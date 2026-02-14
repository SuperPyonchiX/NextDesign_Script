IRepresentation インタフェース
=======================

名前空間: NextDesign.Core

説明​
-----------------------

表現情報へのアクセスオブジェクトです。

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

[IObject](_extension_api_NextDesign.Core_IObject.md)

識別可能なオブジェクトを表します。

派生先​
--------------------------

名前

説明

[IEditorElement](_extension_api_NextDesign.Core_IEditorElement.md)

エディタ要素へのアクセスオブジェクトです。

[IEditor](_extension_api_NextDesign.Core_IEditor.md)

エディタ情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[Model](_extension_api_NextDesign.Core_IRepresentation_properties_Model.md)

対応するモデル

[ModelId](_extension_api_NextDesign.Core_IRepresentation_properties_ModelId.md)

対応するモデル識別子

[ViewDefinition](_extension_api_NextDesign.Core_IRepresentation_properties_ViewDefinition.md)

ビュー定義

[ViewDefinitionName](_extension_api_NextDesign.Core_IRepresentation_properties_ViewDefinitionName.md)

定義名

注釈​
-----------------------

表示要素では、次のメソッドはサポートされません。  
・識別子の設定（SetId()）  
・タグの編集（SetTag(), RemoveTag()）  
これらのメソッドを呼び出した場合、例外がスローされます。