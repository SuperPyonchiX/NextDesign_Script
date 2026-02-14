INamedElement インタフェース
=====================

名前空間: NextDesign.Core

説明​
-----------------------

名前付け可能要素を表します。

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

[IObject](_extension_api_NextDesign.Core_IObject.md)

識別可能なオブジェクトを表します。

派生先​
--------------------------

名前

説明

[IConstraint](_extension_api_NextDesign.Core_IConstraint.md)

制約へのアクセスオブジェクトです。

[IEnumLiteral](_extension_api_NextDesign.Core_IEnumLiteral.md)

列挙型リテラルへのアクセスオブジェクトです。

[IPackage](_extension_api_NextDesign.Core_IPackage.md)

パッケージへのアクセスオブジェクトです。

[IField](_extension_api_NextDesign.Core_IField.md)

フィールドへのアクセスオブジェクトです。

[IType](_extension_api_NextDesign.Core_IType.md)

型を表すオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[BaseId](_extension_api_NextDesign.Core_INamedElement_properties_BaseId.md)

ベース識別子  
  
この要素がプロファイル参照として追加された要素の場合、参照先のプロファイルにおける識別子を返します。  
この要素がプロファイル参照として追加された要素でない場合は　null を返します。

[Description](_extension_api_NextDesign.Core_INamedElement_properties_Description.md)

説明

[DisplayName](_extension_api_NextDesign.Core_INamedElement_properties_DisplayName.md)

表示名

[IsDisabled](_extension_api_NextDesign.Core_INamedElement_properties_IsDisabled.md)

この要素が無効化されているか

[Name](_extension_api_NextDesign.Core_INamedElement_properties_Name.md)

名前

メソッド​
-----------------------------

名前

説明

[GetProfileReferencePackage](_extension_api_NextDesign.Core_INamedElement_methods_GetProfileReferencePackage.md)

この要素を管理するプロファイル参照パッケージを取得します。  
この要素自身がプロファイル参照パッケージの場合は、この要素自身を取得します。  
  
この要素が多段でプロファイル参照される要素（プロファイル参照を持つプロファイルをさらにプロファイル参照で追加した要素）である場合は、  
プロファイル参照情報で依存関係があるプロファイルの基点として記録されているパッケージを取得します。  
  
また、この要素がプロファイル参照パッケージ配下の要素でない場合は、プロファイルのルートパッケージを取得します。  
  
この動作は、あるプロファイルで定義された要素は、そのプロファイルがどのように参照されていたとしても、  
その要素を定義したプロファイルのルートパッケージに対応するパッケージを返すことになります。