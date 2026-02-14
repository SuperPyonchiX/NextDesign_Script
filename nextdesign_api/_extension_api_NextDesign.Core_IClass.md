IClass インタフェース
==============

名前空間: NextDesign.Core

説明​
-----------------------

メタモデルの構成要素を表すオブジェクトです。

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

[IType](_extension_api_NextDesign.Core_IType.md)

型を表すオブジェクトです。

派生先​
--------------------------

名前

説明

[IRelationshipClass](_extension_api_NextDesign.Core_IRelationshipClass.md)

関連クラスへのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[DeclaredFields](_extension_api_NextDesign.Core_IClass_properties_DeclaredFields.md)

このクラスの固有フィールドを取得します。  
固有フィールドを持たない場合は、空のコレクションを返します。

[Fields](_extension_api_NextDesign.Core_IClass_properties_Fields.md)

フィールド  
このクラスで扱えるすべてのフィールドを取得できます。

[FullName](_extension_api_NextDesign.Core_IClass_properties_FullName.md)

完全修飾名  
値の変更により、パッケージ移動、およびクラス名が変更できます。  
ただし、移動先のパッケージが存在しない場合は、例外がスローされます。

[IsAbstract](_extension_api_NextDesign.Core_IClass_properties_IsAbstract.md)

抽象クラスか

[Owner](_extension_api_NextDesign.Core_IClass_properties_Owner.md)

パッケージ  
値の変更により、パッケージを移動できますが、null を指定することはできません。

[SuperClasses](_extension_api_NextDesign.Core_IClass_properties_SuperClasses.md)

このクラスの直接のスーパークラス  
直接のスーパークラスを持たない場合は、空のコレクションを返します。

メソッド​
-----------------------------

名前

説明

[AddSuperClass](_extension_api_NextDesign.Core_IClass_methods_AddSuperClass.md)

指定したクラスをこのクラスのスーパークラスに追加します。  
指定したクラスが既にこのクラスのスーパークラスに含まれる場合は何も行われません。

[AddSuperClasses](_extension_api_NextDesign.Core_IClass_methods_AddSuperClasses.md)

指定したクラス群をこのクラスのスーパークラスに追加します。  
指定したクラス群のうち、既にこのクラスのスーパークラスに含まれるクラスはスキップされます。

[As](_extension_api_NextDesign.Core_IClass_methods_As.md)

指定したモデルがこのクラスと互換するインスタンスであるか調べます。  
指定したモデルのメタクラスが、このクラスと一致するか、このクラスのサブクラスに一致する場合にtureを返します。

[GetAllSubClasses](_extension_api_NextDesign.Core_IClass_methods_GetAllSubClasses.md)

このクラスから派生するすべてのクラスを取得します。  
派生クラスがない場合は空のコレクションを返します。

[GetAllSuperClasses](_extension_api_NextDesign.Core_IClass_methods_GetAllSuperClasses.md)

このクラスの全てのスーパークラスを取得します。  
スーパークラスがない場合は空のコレクションを返します。

[GetConstraintByName](_extension_api_NextDesign.Core_IClass_methods_GetConstraintByName.md)

このクラスで定義する指定された名前の制約を取得します。  
同じ名前の制約が複数ある場合は、最初に見つかった制約を返します。

[GetConstraints](_extension_api_NextDesign.Core_IClass_methods_GetConstraints.md)

このクラスで定義する制約を取得します。

[GetConstraintsByField](_extension_api_NextDesign.Core_IClass_methods_GetConstraintsByField.md)

このクラスで定義する指定されたフィールドの制約を取得します。

[GetConstraintsByTarget](_extension_api_NextDesign.Core_IClass_methods_GetConstraintsByTarget.md)

このクラスで定義する指定された制約適用対象の要素の制約を取得します。

[GetEmbeddedFieldsOf](_extension_api_NextDesign.Core_IClass_methods_GetEmbeddedFieldsOf.md)

このクラスの指定された型の所有フィールドを取得します。  
このメソッドではプリミティブ型、および列挙型のフィールドを取得することはできません。  
プリミティブ型、および列挙型のフィールドをデータ型で取得したい場合は、GetFieldsByType() を使用してください。

[GetField](_extension_api_NextDesign.Core_IClass_methods_GetField.md)

このクラスの指定された名前のフィールドを取得します。  
指定された名前のフィールドが未定義の場合はnullを返します。

[GetFields](_extension_api_NextDesign.Core_IClass_methods_GetFields.md)

このクラスのフィールドを取得します。  
フィールドの順序はメタクラスのフィールド定義順となります。

[GetFieldsByTag](_extension_api_NextDesign.Core_IClass_methods_GetFieldsByTag.md)

指定されたタグが付与されたこのクラスのフィールドを取得します。  
タグ値が未指定の場合はタグの有無のみで評価します。

[GetFieldsByType](_extension_api_NextDesign.Core_IClass_methods_GetFieldsByType.md)

このクラスの指定された型名のフィールドを取得します。  
クラス型フィールドは、クラスの完全修飾名を指定することで取得できます。  
列挙型フィールドは、列挙の完全修飾名を指定することで取得できます。

[GetFieldsOf](_extension_api_NextDesign.Core_IClass_methods_GetFieldsOf.md)

このクラスの指定された型のフィールドを取得します。  
このメソッドではプリミティブ型、および列挙型のフィールドを取得することはできません。  
プリミティブ型、および列挙型のフィールドをデータ型で取得したい場合は、GetFieldsByType() を使用してください。

[GetReferenceFieldsOf](_extension_api_NextDesign.Core_IClass_methods_GetReferenceFieldsOf.md)

このクラスの指定された型の参照フィールドを取得します。

[GetSubClasses](_extension_api_NextDesign.Core_IClass_methods_GetSubClasses.md)

このクラスの直接の派生クラスを取得します。  
直接の派生クラスがない場合は空のコレクションを返します。

[Is](_extension_api_NextDesign.Core_IClass_methods_Is.md)

指定したモデルがこのクラスのインスタンスであるか調べます。  
指定したモデルのメタクラスが、このクラスと一致する場合にtureを返します。

[IsClassOf](_extension_api_NextDesign.Core_IClass_methods_IsClassOf.md)

指定したクラスがこのクラスと互換するか調べます。  
指定したクラスが、このクラスと一致するか、サブクラスの場合にtureを返します。

[IsSuperClass](_extension_api_NextDesign.Core_IClass_methods_IsSuperClass.md)

このクラスが指定したクラスのスーパークラスであるか調べます。  
指定したクラスが、このクラスのスーパークラスの場合にtureを返します。

[RemoveSuperClass](_extension_api_NextDesign.Core_IClass_methods_RemoveSuperClass.md)

指定したクラスをこのクラスのスーパークラスから削除します。  
指定したクラスがこのクラスのスーパークラスに含まれない場合は何も行われません。  
クラスの継承関係を削除すると継承先クラスのモデルも削除します。

[RemoveSuperClasses](_extension_api_NextDesign.Core_IClass_methods_RemoveSuperClasses.md)

指定したクラス群をこのクラスのスーパークラスから削除します。  
指定したクラス群のうち、このクラスのスーパークラスに含まれないクラスはスキップされます。  
クラスの継承関係を削除すると継承先クラスのモデルも削除します。