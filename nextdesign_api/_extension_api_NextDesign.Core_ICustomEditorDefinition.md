ICustomEditorDefinition インタフェース
===============================

名前空間: NextDesign.Core

説明​
-----------------------

カスタムエディタ定義へのアクセスオブジェクトです。

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

[IEditorDef](_extension_api_NextDesign.Core_IEditorDef.md)

エディタ定義へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[CustomEditorTypeId](_extension_api_NextDesign.Core_ICustomEditorDefinition_properties_CustomEditorTypeId.md)

カスタムエディタの種類識別子。

[CustomElements](_extension_api_NextDesign.Core_ICustomEditorDefinition_properties_CustomElements.md)

カスタムエディタ要素ビュー定義の列挙。

[DefinitionDescriptor](_extension_api_NextDesign.Core_ICustomEditorDefinition_properties_DefinitionDescriptor.md)

このビュー定義のタイプ記述子。  
該当するタイプ記述子が未登録の場合は null を返します。

[PropertyValues](_extension_api_NextDesign.Core_ICustomEditorDefinition_properties_PropertyValues.md)

このビュー定義のプロパティ値。  
タイプ記述子で既定した各プロパティに対する値の辞書です。