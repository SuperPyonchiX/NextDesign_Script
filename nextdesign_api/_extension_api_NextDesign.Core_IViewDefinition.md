IViewDefinition インタフェース
=======================

名前空間: NextDesign.Core

説明​
-----------------------

表現情報へのアクセスオブジェクトを表す基底インタフェースです。

所属エリア​
--------------------------------

名前

説明

[プロファイル](_extension_api_overview_profile.md)

プロファイルにアクセスするAPI群です。

派生先​
--------------------------

名前

説明

[IEditorDef](_extension_api_NextDesign.Core_IEditorDef.md)

エディタ定義へのアクセスオブジェクトです。

[IElementDef](_extension_api_NextDesign.Core_IElementDef.md)

エディタ要素定義へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[BaseId](_extension_api_NextDesign.Core_IViewDefinition_properties_BaseId.md)

ベース識別子  
  
この要素がプロファイル参照として追加された要素の場合、参照先のプロファイルにおける識別子を返します。  
この要素がプロファイル参照として追加された要素でない場合は　null を返します。

[DisplayName](_extension_api_NextDesign.Core_IViewDefinition_properties_DisplayName.md)

表示名

[Id](_extension_api_NextDesign.Core_IViewDefinition_properties_Id.md)

識別子

[IsDisabled](_extension_api_NextDesign.Core_IViewDefinition_properties_IsDisabled.md)

この要素が無効化されているか

[ModelClass](_extension_api_NextDesign.Core_IViewDefinition_properties_ModelClass.md)

モデルクラス

[ModelClassName](_extension_api_NextDesign.Core_IViewDefinition_properties_ModelClassName.md)

モデルクラス名

[Name](_extension_api_NextDesign.Core_IViewDefinition_properties_Name.md)

名前

メソッド​
-----------------------------

名前

説明

[GetProfileReferencePackage](_extension_api_NextDesign.Core_IViewDefinition_methods_GetProfileReferencePackage.md)

この要素を管理する基点パッケージを取得します。  
  
基点パッケージは以下のいずれかのパッケージを指します。  
\- プロファイルのルートパッケージ  
\- プロファイル参照が記録されているプロファイル参照パッケージ  
\- プロファイル参照で依存関係があるプロファイルの基点として記録されているパッケージ