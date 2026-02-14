IModel インタフェース
==============

名前空間: NextDesign.Core

説明​
-----------------------

NextDesignの設計モデル情報へのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[モデル](_extension_api_overview_model.md)

モデルにアクセスするAPI群です。

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

[IFrame](_extension_api_NextDesign.Core_IFrame.md)

フレーム情報へのアクセスオブジェクトです。

[IInteractionElement](_extension_api_NextDesign.Core_IInteractionElement.md)

相互作用モデル要素情報へのアクセスオブジェクトです。  
相互作用を表現する要素に共通の特性を定義します。

[IFeature](_extension_api_NextDesign.Core_IFeature.md)

プロダクトの特徴（フィーチャ）情報に対するアクセスオブジェクトです。

[IProductLineModel](_extension_api_NextDesign.Core_IProductLineModel.md)

プロダクトライン開発支援モデルに対するアクセスオブジェクトです。

[IConfigurationModel](_extension_api_NextDesign.Core_IConfigurationModel.md)

プロダクトコンフィグレーションモデル対するアクセスオブジェクトです。

[IProject](_extension_api_NextDesign.Core_IProject.md)

プロジェクト情報へのアクセス手段を提供します。

[IInteraction](_extension_api_NextDesign.Core_IInteraction.md)

相互作用モデル情報へのアクセスオブジェクトです。

[IFeatureModel](_extension_api_NextDesign.Core_IFeatureModel.md)

プロダクトの特徴（フィーチャ）を構造化した管理モデルに対するアクセスオブジェクトです。

[IRelationship](_extension_api_NextDesign.Core_IRelationship.md)

関連情報へのアクセスオブジェクトです。

[IProduct](_extension_api_NextDesign.Core_IProduct.md)

プロダクト情報対するアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[ClassName](_extension_api_NextDesign.Core_IModel_properties_ClassName.md)

クラス名  
Metaclass の名前を取得します。

[Description](_extension_api_NextDesign.Core_IModel_properties_Description.md)

説明

[Errors](_extension_api_NextDesign.Core_IModel_properties_Errors.md)

このモデルのエラー情報のコレクションを取得します。  
  
エラー情報にはサマリー情報を含みます。

[HasError](_extension_api_NextDesign.Core_IModel_properties_HasError.md)

このモデルにエラーがあるか調べます。  
このモデルにエラーがある場合はTrueを返します。  
  
この評価はサマリー情報を含みます。

[HasErrorWithChildren](_extension_api_NextDesign.Core_IModel_properties_HasErrorWithChildren.md)

このモデル、およびこのモデルが所有する子要素以下の要素においてエラーがあるか調べます。  
このモデル、およびこのモデルが所有する子要素以下の要素にエラーがある場合はTrueを返します。  
  
この評価はサマリー情報を含みます。

[IsDeleted](_extension_api_NextDesign.Core_IModel_properties_IsDeleted.md)

このインスタンスが削除済みであるか調べます。

[IsDeleting](_extension_api_NextDesign.Core_IModel_properties_IsDeleting.md)

このモデルが削除中であるか調べます。

[IsDirty](_extension_api_NextDesign.Core_IModel_properties_IsDirty.md)

このモデルがダーティー状態（未保存）であるか調べます。  
未保存の場合はTrueを返します。

[IsEditable](_extension_api_NextDesign.Core_IModel_properties_IsEditable.md)

このインスタンスが編集可能であるかを調べます。

[IsProductLineElement](_extension_api_NextDesign.Core_IModel_properties_IsProductLineElement.md)

このモデルがプロダクトラインのモデルであるか調べます。

[IsProxy](_extension_api_NextDesign.Core_IModel_properties_IsProxy.md)

このインスタンスがプロキシ要素であるか調べます。  
プロキシ要素は、分散環境において、このインスタンス情報を記録したユニットが読み込まれていない場合に生成されます。

[IsUnitTopModel](_extension_api_NextDesign.Core_IModel_properties_IsUnitTopModel.md)

このインスタンスがユニットの基点モデルであるか調べます。

[Metaclass](_extension_api_NextDesign.Core_IModel_properties_Metaclass.md)

クラス

[ModelPath](_extension_api_NextDesign.Core_IModel_properties_ModelPath.md)

このインスタンスのモデル階層パス  
モデルのルート、プロダクトラインまたはユニットにおける基点であり親要素が読み込まれていないモデルを起点としたモデル階層を、パス区切り文字（"/"）で結合したパス文字列を返します。  
  
このインスタンスが属するルートノードが、親要素が読み込まれていないモデルの場合、パス区切り文字で結合したパス文字列の先頭に「\\partial;」が付与されます。

[ModelUnit](_extension_api_NextDesign.Core_IModel_properties_ModelUnit.md)

このインスタンス情報を管理するユニット（物理ファイル）情報

[Name](_extension_api_NextDesign.Core_IModel_properties_Name.md)

名前

[Owner](_extension_api_NextDesign.Core_IModel_properties_Owner.md)

このインスタンスを直接所有するモデル  
このインスタンスが関連の場合、または、所有するモデルが存在しない場合は　null を返します。

[OwnerProject](_extension_api_NextDesign.Core_IModel_properties_OwnerProject.md)

このインスタンスを保持するプロジェクト

メソッド​
-----------------------------

名前

説明

[AddError](_extension_api_NextDesign.Core_IModel_methods_AddError.md)

このモデルに対するエラー情報を追加して、追加したエラー情報を返します。

[AddNewModel](_extension_api_NextDesign.Core_IModel_methods_AddNewModel.md)

このインスタンスの指定したフィールドに指定したクラスのインスタンスをフィールド値として追加します。  
インスタンスが、該当フィールドの末尾の要素として追加されます。  
指定したクラスが抽象クラスの場合でもインスタンス化を許容し、該当フィールドの末尾の要素として追加されます。  
指定するフィールドは、クラス型の所有フィールドでなければなりません。  
多重度が2以上のフィールドの多重度上限制約に違反しても例外はスローされません。  
  
また、指定するクラス名は、フィールドのデータ型と互換する型を指定する必要があります。  
なお、あいまい一致とするときに、一致するクラスが複数ある場合、一番最初に見つかった型互換のあるクラスのインスタンスを追加します。

[AddNewModelAt](_extension_api_NextDesign.Core_IModel_methods_AddNewModelAt.md)

このインスタンスの指定したフィールドに、追加位置を指定して指定したクラスのインスタンスをフィールド値として追加します。  
指定したクラスが抽象クラスの場合でもインスタンス化を許容し、指定した位置にフィールド値として追加します。  
指定するフィールドは、クラス型の所有フィールドでなければなりません。  
なお、多重度が2以上のフィールドの多重度上限制約に違反しても例外はスローされません。  
  
指定するクラス名は、フィールドのデータ型と互換する型を指定する必要があります。  
また、あいまい一致とするときに、一致するクラスが複数ある場合、一番最初に見つかった型互換のあるクラスのインスタンスを追加します。

[AddNewModelTo](_extension_api_NextDesign.Core_IModel_methods_AddNewModelTo.md)

このインスタンスの指定したフィールドに、追加位置を指定して指定したクラスのインスタンスをフィールド値として追加します。  
指定したクラスが抽象クラスの場合でもインスタンス化を許容し、指定された位置にフィールド値として追加します。  
指定するフィールドは、クラス型の所有フィールドでなければなりません。  
なお、多重度が2以上のフィールドの多重度上限制約に違反しても例外はスローされません。  
  
指定するクラス名は、フィールドのデータ型と互換する型を指定する必要があります。  
また、あいまい一致とするときに、一致するクラスが複数ある場合、一番最初に見つかった型互換のあるクラスのインスタンスを追加します。

[As(IClass)](_extension_api_NextDesign.Core_IModel_methods_As-3.md)

このインスタンスが指定したクラス型と互換するか調べます。  
このインスタンスが指定したクラス、またはそのサブクラスのインスタンスである場合に True を返します。

[As(IPackage,string,bool)](_extension_api_NextDesign.Core_IModel_methods_As-2.md)

このインスタンスが指定したクラス型と互換するか調べます。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
このインスタンスが指定したクラス、またはそのサブクラスのインスタンスである場合に True を返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[As(string,bool)](_extension_api_NextDesign.Core_IModel_methods_As-1.md)

このインスタンスが指定したクラス型と互換するか調べます。  
このインスタンスが指定したクラス、またはそのサブクラスのインスタンスである場合に True を返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[AsIn(IEnumerable<IClass>)](_extension_api_NextDesign.Core_IModel_methods_AsIn-5.md)

このインスタンスが指定したいずれかのクラス型と互換するか調べます。  
このインスタンスが指定したいずれかのクラス、またはそれらのサブクラスのインスタンスである場合に True を返します。

[AsIn(IEnumerable<string>,bool)](_extension_api_NextDesign.Core_IModel_methods_AsIn-3.md)

このインスタンスが指定したいずれかのクラス型と互換するか調べます。  
このインスタンスが指定したいずれかのクラス、またはそれらのサブクラスのインスタンスである場合に True を返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[AsIn(IPackage,IEnumerable<string>,bool)](_extension_api_NextDesign.Core_IModel_methods_AsIn-4.md)

このインスタンスが指定したいずれかのクラス型と互換するか調べます。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
このインスタンスが指定したいずれかのクラス、またはそれらのサブクラスのインスタンスである場合に True を返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[AsIn(IPackage,string,bool)](_extension_api_NextDesign.Core_IModel_methods_AsIn-2.md)

このインスタンスが指定したいずれかのクラス型と互換するか調べます。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
このインスタンスが指定したいずれかのクラス、またはそれらのサブクラスのインスタンスである場合に True を返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[AsIn(string,bool)](_extension_api_NextDesign.Core_IModel_methods_AsIn-1.md)

このインスタンスが指定したいずれかのクラス型と互換するか調べます。  
このインスタンスが指定したいずれかのクラス、またはそれらのサブクラスのインスタンスである場合に True を返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[AssignFeature](_extension_api_NextDesign.Core_IModel_methods_AssignFeature.md)

このモデルに指定したフィーチャを割り当てます。

[AssignFeatureByName](_extension_api_NextDesign.Core_IModel_methods_AssignFeatureByName.md)

このモデルに指定した名前のフィーチャを割り当てます。

[AssignFeatures](_extension_api_NextDesign.Core_IModel_methods_AssignFeatures.md)

このモデルに指定したすべてのフィーチャを割り当てます。

[AssignFeaturesByName](_extension_api_NextDesign.Core_IModel_methods_AssignFeaturesByName.md)

このモデルに指定した名前のすべてのフィーチャを割り当てます。

[CanChangeMetaclassTo](_extension_api_NextDesign.Core_IModel_methods_CanChangeMetaclassTo.md)

このモデルのクラスを指定したクラスに変更できるか判定します。  
  
モデルのクラス変更に伴い、親モデルがこのモデルを保持できなくなる場合、false を返します。  
このモデルが関連の場合は、関連端のモデルが保持できなくなる場合、false を返します。

[CanRelate](_extension_api_NextDesign.Core_IModel_methods_CanRelate.md)

このインスタンスの指定したフィールドで指定したモデルと関連づけできるか調べます。  
関連づけできる場合はTrueを返します。  
このメソッドでは、フィールドの型だけでなく、以下のフィールド制約についても評価します。  
\[評価する制約\]  
\- パス制約  
\- 型制約  
\- 多重度上限  
  
なお、自身、もしくは関連づけするモデルが削除済みモデル、一時プロキシの場合はFalseを返します。  
また、以下のフィールドが指定された場合もFalseを返します。  
\- プロダクトラインのフィーチャ割り当てフィールド  
\- System.Coreタグが付与されたフィールド  
\- 所有フィールド  
\- 無効なフィールド

[CanRelateAny](_extension_api_NextDesign.Core_IModel_methods_CanRelateAny.md)

このインスタンスを指定したモデルと関連づけできるか調べます。  
このインスタンスのいずれかの参照フィールドで関連づけることができる場合はTrueを返します。  
なお、自身、もしくは関連づけするモデルが削除済みモデル、一時プロキシの場合はFalseを返します。  
なお、所有のフィールドに関しては対象としません。

[ChangeMetaclassTo](_extension_api_NextDesign.Core_IModel_methods_ChangeMetaclassTo.md)

このモデルのクラスを指定したクラスに変更します。  
  
モデルのクラス変更に伴い、親モデルとの所有関連インスタンスのクラスが利用できなくなる場合、一番最初に見つけたクラスに所有関連インスタンスのクラスを変更します。  
また、変更したクラスで維持できないフィールドの値を削除します。  
  
変更後のクラスで維持できないシェイプは削除されます。

[Count](_extension_api_NextDesign.Core_IModel_methods_Count.md)

指定したフィールドの値件数を取得します。  
  
このメソッドは、IContextOption.PlModelAccessMode を評価しません。  

[CreateAsyncValidationContext](_extension_api_NextDesign.Core_IModel_methods_CreateAsyncValidationContext.md)

このモデルに対する非同期検証コンテキストを生成します。

[Delete](_extension_api_NextDesign.Core_IModel_methods_Delete.md)

このインスタンスを削除します。  
既に削除されたインスタンスに対してこのメソッドを呼び出した場合は何も行われません。  
  
このインスタンスを削除することにより、以下のようなモデルに対する変更が発生します。  
・このインスタンスと所有元（親要素）との所有関連が削除されます。  
・このインスタンスを関連端とする全ての参照関連が削除されます。  
・このインスタンスが所有するすべての子要素以下の要素、およびそれらの要素を関連端とする参照関連が削除されます。

[FindChildrenByClass(IClass,bool)](_extension_api_NextDesign.Core_IModel_methods_FindChildrenByClass-3.md)

このインスタンスの所有関係にあるインスタンスのうち指定したクラスのインスタンスを検索します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

[FindChildrenByClass(IPackage,string,bool,bool)](_extension_api_NextDesign.Core_IModel_methods_FindChildrenByClass-2.md)

このインスタンスの所有関係にあるインスタンスのうち指定したクラスのインスタンスを検索します。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
所有関係にあるインスタンスのうち、指定されたクラスのインスタンスのコレクションを返します。  
なお、クラス名に指定したクラスが見つからない場合は空のコレクションを返します。  
また、該当するインスタンスが存在しない場合も空のコレクションを返します。

[FindChildrenByClass(string,bool,bool)](_extension_api_NextDesign.Core_IModel_methods_FindChildrenByClass-1.md)

このインスタンスの所有関係にあるインスタンスのうち指定したクラスのインスタンスを検索します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
所有関係にあるインスタンスのうち、指定されたクラスのインスタンスのコレクションを返します。  
なお、クラス名に指定したクラスが見つからない場合は空のコレクションを返します。  
また、該当するインスタンスが存在しない場合も空のコレクションを返します。

[FindChildrenByClassDisplayName](_extension_api_NextDesign.Core_IModel_methods_FindChildrenByClassDisplayName.md)

このインスタンスの所有関係にあるインスタンスのうち指定した表示名をもつクラスのインスタンスを検索します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
所有関係にあるインスタンスのうち、指定された表示名をもつクラスのインスタンスのコレクションを返します。  
該当するインスタンスが存在しない場合は空のコレクションを返します。

[FindChildrenByClassTag](_extension_api_NextDesign.Core_IModel_methods_FindChildrenByClassTag.md)

このインスタンスの所有関係にあるインスタンスのうち指定したタグが付与されたクラスのインスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
所有関係にあるインスタンスのうち、指定されたタグが付与されたクラスのインスタンスのコレクションを返します。  
該当するインスタンスが存在しない場合は空のコレクションを返します。

[FindChildrenByTag](_extension_api_NextDesign.Core_IModel_methods_FindChildrenByTag.md)

このインスタンスの所有関係にあるインスタンスのうち指定したタグが付与されたインスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
所有関係にあるインスタンスのうち、指定されたタグが付与されたインスタンスのコレクションを返します。  
該当するインスタンスが存在しない場合は空のコレクションを返します。

[FindOwnerByClass(IClass)](_extension_api_NextDesign.Core_IModel_methods_FindOwnerByClass-3.md)

このインスタンスを保持する指定クラスの最初の所有元インスタンスを取得します。  
  
このインスタンスを所有するインスタンスを親方向へ辿り、最初に見つかった指定クラスのインスタンスを返します。  
最上位の親要素まで探索しても、該当するインスタンスが見つからなかった場合は null を返します。

[FindOwnerByClass(IPackage,string,bool)](_extension_api_NextDesign.Core_IModel_methods_FindOwnerByClass-2.md)

このインスタンスを保持する指定クラスの最初の所有元インスタンスを取得します。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
  
このインスタンスを所有するインスタンスを親方向へ辿り、最初に見つかった指定クラスのインスタンスを返します。  
最上位の親要素まで探索しても、該当するインスタンスが見つからなかった場合は null を返します。  
  
なお、クラス名に指定したクラスが見つからない場合はnullを返します。  
また、あいまい一致とするときに、一致するクラスが複数ある場合、一番最初に見つかったクラスのインスタンスを返します。

[FindOwnerByClass(string,bool)](_extension_api_NextDesign.Core_IModel_methods_FindOwnerByClass-1.md)

このインスタンスを保持する指定クラスの最初の所有元インスタンスを取得します。  
  
このインスタンスを所有するインスタンスを親方向へ辿り、最初に見つかった指定クラスのインスタンスを返します。  
最上位の親要素まで探索しても、該当するインスタンスが見つからなかった場合は null を返します。  
  
なお、クラス名に指定したクラスが見つからない場合はnullを返します。  
また、あいまい一致とするときに、一致するクラスが複数ある場合、一番最初に見つかったクラスのインスタンスを返します。

[FindRelatableModels](_extension_api_NextDesign.Core_IModel_methods_FindRelatableModels.md)

このモデルと指定した参照フィールドで関連付けできるモデルを取得します。  
  
探索範囲の基点モデルが未指定の場合は、プロジェクト全体から探索します。  
  
探索範囲の基点モデルを指定した場合は、そのモデル要素以下のモデルから関連付け可能なモデルを探索します。  
この場合、取得結果のモデルには関連は含まれません。関連を参照するフィールドの場合は、探索範囲の基点モデルを未指定で呼び出してください。  
  
なお、指定したフィールドが参照フィールドでない場合は空のコレクションを返します。

[GetAllChildren](_extension_api_NextDesign.Core_IModel_methods_GetAllChildren.md)

このインスタンスから所有関係の深さ優先探索で探索できるすべての所有先インスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
このインスタンスを基点として所有関係の深さ優先探索で探索できるすべての所有先インスタンスのコレクションを返します。  
該当するインスタンスが存在しない場合は空のコレクションを返します。

[GetAllErrorsWithChildren](_extension_api_NextDesign.Core_IModel_methods_GetAllErrorsWithChildren.md)

このモデルが所有する子要素以下の要素も含めた全てのエラー情報を取得します。  
エラー情報が存在しない場合は、空のコレクションを返します。  
  
エラー情報にはサマリー情報を含みます。

[GetAssignedFeatures](_extension_api_NextDesign.Core_IModel_methods_GetAssignedFeatures.md)

このモデルに割り当てられているすべてのフィーチャを取得します。

[GetChangeableMetaclasses](_extension_api_NextDesign.Core_IModel_methods_GetChangeableMetaclasses.md)

このモデルを変換可能なすべてのクラスを取得します。  
変換可能なクラスが存在しない場合は空のコレクションを返します。  
  
親モデルが維持可能なモデルのクラスを返します。  
このモデルが関連の場合は、関連端のモデルが維持可能な関連クラスを返します。

[GetChildren](_extension_api_NextDesign.Core_IModel_methods_GetChildren.md)

このインスタンスの直接の所有関係にある所有先インスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
このインスタンスと直接の所有関係にある所有先インスタンスのコレクションを返します。  
該当するインスタンスが存在しない場合は空のコレクションを返します。

[GetDerivedFromRelationsOf](_extension_api_NextDesign.Core_IModel_methods_GetDerivedFromRelationsOf.md)

このモデルが指定したモデルから導出した要素であった場合、その全ての導出関連を取得します。  
例えば、このモデルが{要素A}から導出した要素であった場合に、引数に{要素A}を指定することで、その導出関連を取得することができます。  
  
これは、次のような処理に利用することができます。  
・要求オブジェクトと仕様オブジェクトに導出関係がある場合に、仕様オブジェクト側から、要求オブジェクトに対する関連を取得する  
  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetDerivedModels(IPackage,string,bool)](_extension_api_NextDesign.Core_IModel_methods_GetDerivedModels-2.md)

指定した関連クラスにより、このインスタンスから導出したインスタンスを取得します。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
以下のケースに該当する場合は、このメソッドは空のコレクションを返します。  
\- 該当する導出関連で関連づけられた導出先インスタンスが存在しない  
\- 指定された関連クラスが見つからない  
\- 指定された関連クラスが導出関連でない  
\- 指定された導出関連クラスによる導出先への参照フィールドが見つからない  

[GetDerivedModels(IRelationshipClass)](_extension_api_NextDesign.Core_IModel_methods_GetDerivedModels-3.md)

指定した関連クラスにより、このインスタンスから導出したインスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

[GetDerivedModels(string,bool)](_extension_api_NextDesign.Core_IModel_methods_GetDerivedModels-1.md)

指定した関連クラスにより、このインスタンスから導出したインスタンスを取得します。  
関連クラスが未指定（null）の場合は、全ての導出したインスタンスを取得します。  
  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
以下のケースに該当する場合は、このメソッドは空のコレクションを返します。  
\- 該当する導出関連で関連づけられた導出先インスタンスが存在しない  
\- 指定された関連クラスが見つからない  
\- 指定された関連クラスが導出関連でない  
\- 指定された導出関連クラスによる導出先への参照フィールドが見つからない  

[GetDerivedToRelationsOf](_extension_api_NextDesign.Core_IModel_methods_GetDerivedToRelationsOf.md)

指定したモデルが、このモデルから導出した要素であった場合、その全ての導出関連を取得します。  
例えば、このモデルから導出した{要素B}があった場合に、引数に{要素B}を指定することで、その導出関連を取得することができます。  
  
これは、次のような処理に利用することができます。  
・要求オブジェクトと仕様オブジェクトに導出関係がある場合に、要求オブジェクト側から、仕様オブジェクトに対する関連を取得する  
  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetDeriveRelationsOf](_extension_api_NextDesign.Core_IModel_methods_GetDeriveRelationsOf.md)

指定したモデルとの全ての導出関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。  
なお、このメソッドでは導出方向を評価しません。

[GetDerivingModels(IPackage,string,bool)](_extension_api_NextDesign.Core_IModel_methods_GetDerivingModels-2.md)

指定した関連クラスにより、このインスタンスの導出元インスタンスを取得します。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
以下のケースに該当する場合は、このメソッドは空のコレクションを返します。  
\- 該当する導出関連で関連づけられた導出元インスタンスが存在しない  
\- 指定された関連クラスが見つからない  
\- 指定された関連クラスが導出関連でない  
\- 指定された導出関連クラスによる導出元への参照フィールドが見つからない  

[GetDerivingModels(IRelationshipClass)](_extension_api_NextDesign.Core_IModel_methods_GetDerivingModels-3.md)

指定した関連クラスにより、このインスタンスの導出元インスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

[GetDerivingModels(string,bool)](_extension_api_NextDesign.Core_IModel_methods_GetDerivingModels-1.md)

指定した関連クラスにより、このインスタンスの導出元インスタンスを取得します。  
関連クラスが未指定（null）の場合は、全ての導出元インスタンスを取得します。  
  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
以下のケースに該当する場合は、このメソッドは空のコレクションを返します。  
\- 該当する導出関連で関連づけられた導出元インスタンスが存在しない  
\- 指定された関連クラスが見つからない  
\- 指定された関連クラスが導出関連でない  
\- 指定された導出関連クラスによる導出元への参照フィールドが見つからない  

[GetEditor](_extension_api_NextDesign.Core_IModel_methods_GetEditor.md)

このインスタンスに対応する指定した名前のエディタを取得します。  
このメソッドは、IContextOption.EditorAccessModeを評価します。  
  
該当するエディタが見つからない場合は、null を返します。  
  
EditorAccessModeの指定によっては、最新のモデルの変更が同期されません。  
そのため、モデルを変更してから一度も表示していないエディタ等では、正しい情報が取得できない場合があることに注意してください。

[GetEditors](_extension_api_NextDesign.Core_IModel_methods_GetEditors.md)

このインスタンスに対応するすべてのエディタを取得します。  
このメソッドは、IContextOption.EditorAccessModeを評価します。  
  
対応するエディタが存在しない場合は、空のコレクションを返します。  
  
EditorAccessModeの指定によっては、最新のモデルの変更が同期されません。  
そのため、モデルを変更してから一度も表示していないエディタ等では、正しい情報が取得できない場合があることに注意してください。

[GetField](_extension_api_NextDesign.Core_IModel_methods_GetField.md)

このインスタンスの指定したフィールドの値を取得します。  
指定したフィールドの多重度が2以上の場合は、該当フィールドの先頭要素を取得します。

[GetFieldAt](_extension_api_NextDesign.Core_IModel_methods_GetFieldAt.md)

このインスタンスの指定したフィールドの指定したインデックス位置の値を取得します。

[GetFieldString](_extension_api_NextDesign.Core_IModel_methods_GetFieldString.md)

このインスタンスの指定したフィールドの値を文字列形式で取得します。  
指定したフィールドがクラス型の場合、そのフィールドの先頭のインスタンス名を取得します。  
なお、多重度が2以上の場合は、該当フィールドの先頭要素を取得します。

[GetFieldStringAt](_extension_api_NextDesign.Core_IModel_methods_GetFieldStringAt.md)

このインスタンスの指定したフィールドの指定したインデックス位置の値を文字列形式で取得します。  
指定したインデックス位置のフィールドがクラス型の場合、そのフィールドの先頭のインスタンス名を取得します。

[GetFieldValues](_extension_api_NextDesign.Core_IModel_methods_GetFieldValues.md)

指定したフィールドの値コレクションを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当するフィールド値が存在しない場合は、空のコレクションを返します。  
なお、このメソッドで指定するフィールドのデータ型はクラス型でなければなりません。

[GetFieldValuesByFieldTag](_extension_api_NextDesign.Core_IModel_methods_GetFieldValuesByFieldTag.md)

指定したタグが付与されたフィールドの値の列挙を取得します。  
該当タグが付与されたフィールドが複数存在する場合は、そのすべてのフィールドの値の列挙を結合して返します。  
このメソッドは、IContextOption.PlModelAccessMode を評価しません。プロダクトで無効となる要素も返される点に注意してください。  
  
該当するフィールド値が存在しない場合は、空のコレクションを返します。  
  
なお、このメソッドが対象とするタグを付与するフィールドのデータ型は任意です。したがって、このメソッドが返す列挙は object 型となります。  
利用の際には、最適なデータ型に変換する必要があります。

[GetOwnerField](_extension_api_NextDesign.Core_IModel_methods_GetOwnerField.md)

このモデルの所有元モデルがこのモデルを保持するフィールドを取得します。  
このモデルが関連の場合、または所有元モデルが存在しない場合は null を返します。

[GetOwnerRelationship](_extension_api_NextDesign.Core_IModel_methods_GetOwnerRelationship.md)

このモデルの所有元モデルとの関連を取得します。  
このモデルが関連の場合、または所有元モデルが存在しない場合は null を返します。

[GetOwners](_extension_api_NextDesign.Core_IModel_methods_GetOwners.md)

このインスタンスに対して所有関係で探索できる全ての所有元インスタンスを取得します。  
このインスタンスを所有するインスタンスの親方向へのインスタンスのコレクションを返します。  
親要素が存在しない場合は空のコレクションを返します。

[GetProductApplyCondition](_extension_api_NextDesign.Core_IModel_methods_GetProductApplyCondition.md)

このモデルのプロダクト適用条件式を取得します。  
  
プロダクト適用条件式は、"フィーチャ名変数"を真偽値型変数として扱う論理式です。  
コンフィグレーションにおいて、フィーチャが選択状態にある場合、該当の"フィーチャ名変数"を真として評価します。  
"フィーチャ名変数"は、"\["および"\]"でフィーチャ名、またはフィーチャユニーク名を囲む書式により記述します。  
また、プロダクト適用条件式では、次の論理演算子、および計算順序を指定する"(",")"を使用できます。  
\- AND : 論理積  
\- OR : 論理和  
\- NOT : 否定  
  
例：  
・次のプロダクト適用条件式は、コンフィグレーションにおいて、フィーチャ名が"追従走行"、または"前方カメラ"が選択されている場合に真として評価される条件式です。  
\[追従走行\] OR \[前方カメラ\]  
  
・次のプロダクト適用条件式は、コンフィグレーションにおいて、フィーチャ名が"定速走行"が選択されておらず、かつ"ミリ波レーダー"が選択されている場合に真として評価される条件式です。  
NOT \[定速走行\] AND \[ミリ波レーダー\]

[GetReferenceFieldsOf](_extension_api_NextDesign.Core_IModel_methods_GetReferenceFieldsOf.md)

このインスタンスのクラスが持つ参照フィールドのうち、指定したモデルを格納可能な型の参照フィールドを取得します。  
このメソッドで取得できるフィールドは、Model.Metaclass.GetReferenceFieldOf(model.Metaclass) と同じ結果となります。  
また、多重度、パス制約等のフィールドの制約については評価されません。

[GetRefRelatedModels(IPackage,string,bool)](_extension_api_NextDesign.Core_IModel_methods_GetRefRelatedModels-2.md)

指定した関連クラスにより、このインスタンスと参照関係にあるインスタンスを取得します。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
以下のケースに該当する場合は、このメソッドは空のコレクションを返します。  
\- 該当する参照関連で関連づけられたインスタンスが存在しない  
\- 指定された関連クラスが見つからない  
\- 指定された関連クラスによる参照フィールドが見つからない  
  
なお、指定された関連クラスが、自己参照関連の場合は、関連元、関連先のいずれの関係も評価します。  

[GetRefRelatedModels(IRelationshipClass)](_extension_api_NextDesign.Core_IModel_methods_GetRefRelatedModels-3.md)

指定した関連クラスにより、このインスタンスと参照関係にあるインスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
なお、指定された関連クラスが、自己参照関連の場合は、関連元、関連先のいずれの関係も評価します。  

[GetRefRelatedModels(string,bool)](_extension_api_NextDesign.Core_IModel_methods_GetRefRelatedModels-1.md)

指定した関連クラスにより、このインスタンスと参照関係にあるインスタンスを取得します。  
関連クラスが未指定（null）の場合は、全ての参照関係にあるインスタンスを取得します。  
  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
以下のケースに該当する場合は、このメソッドは空のコレクションを返します。  
\- 該当する参照関連で関連づけられたインスタンスが存在しない  
\- 指定された関連クラスが見つからない  
\- 指定された関連クラスによる参照フィールドが見つからない  
  
なお、指定された関連クラスが、自己参照関連の場合は、関連元、関連先のいずれの関係も評価します。  

[GetRelatableFieldsOf](_extension_api_NextDesign.Core_IModel_methods_GetRelatableFieldsOf.md)

このインスタンスと指定したモデルを関連づけることができる参照フィールドを取得します。  
このメソッドで取得できるフィールドは、フィールドの制約について評価します。  
そのため、次の制約を満たさないフィールドは除外されます。  
\[評価する制約\]  
\- パス制約  
\- 多重度上限

[GetRelatingFieldsOf](_extension_api_NextDesign.Core_IModel_methods_GetRelatingFieldsOf.md)

このインスタンスのクラスが持つ全てのフィールドのうち、指定したモデルをフィールド値として格納するものを取得します。  
このメソッドで取得できるフィールドは、所有/参照に関係なく取得します。

[GetRelation](_extension_api_NextDesign.Core_IModel_methods_GetRelation.md)

指定したフィールドの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
指定したフィールドが有効な関連を持たない場合は、null を返します。  
なお、指定したフィールドの多重度が2以上の場合は、該当フィールドの先頭要素への関連を取得します。

[GetRelationAt](_extension_api_NextDesign.Core_IModel_methods_GetRelationAt.md)

指定したフィールドの指定位置の関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。

[GetRelations](_extension_api_NextDesign.Core_IModel_methods_GetRelations.md)

指定したフィールドの関連コレクションを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetRelationsByClassTag](_extension_api_NextDesign.Core_IModel_methods_GetRelationsByClassTag.md)

このインスタンスのすべてのフィールドから指定したタグが付与された関連クラスのインスタンスを取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetRelationsByClassTagOf](_extension_api_NextDesign.Core_IModel_methods_GetRelationsByClassTagOf.md)

このモデルのすべてのフィールドから指定したタグが付与された関連クラスによって関連づけられた指定したモデルとの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetRelationsByFieldOf](_extension_api_NextDesign.Core_IModel_methods_GetRelationsByFieldOf.md)

指定したフィールドにおける指定したモデルとの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetRelationsByFieldTag](_extension_api_NextDesign.Core_IModel_methods_GetRelationsByFieldTag.md)

指定したタグが付与されたフィールドの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetRelationsByFieldTagOf](_extension_api_NextDesign.Core_IModel_methods_GetRelationsByFieldTagOf.md)

指定したタグが付与されたフィールドから、指定したモデルとの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetRelationsByTag](_extension_api_NextDesign.Core_IModel_methods_GetRelationsByTag.md)

指定したフィールドにおける指定したタグが付与された関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。

[GetRelationsOf](_extension_api_NextDesign.Core_IModel_methods_GetRelationsOf.md)

指定したモデルとの全ての関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。  
なお、このメソッドは、所有関連/参照関連に関係なく、全ての関連を取得します。

[GetRelationsOfWhere](_extension_api_NextDesign.Core_IModel_methods_GetRelationsOfWhere.md)

このインスタンスの指定した条件に合致する指定したモデルとの関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。  
  
取得対象の関連は評価関数により、任意に決定することができます。

[GetRelationsWhere](_extension_api_NextDesign.Core_IModel_methods_GetRelationsWhere.md)

このインスタンスの指定した条件に合致する全ての関連を取得します。  
このメソッドは、IContextOption.PlModelAccessMode を評価します。  
  
該当する関連が存在しない場合は、空のコレクションを返します。  
  
取得対象の関連は評価関数により、任意に決定することができます。

[GetRepresentationsInEditor](_extension_api_NextDesign.Core_IModel_methods_GetRepresentationsInEditor.md)

指定したエディタ内でこのインスタンスに対応するすべての表現情報を取得します。  
このメソッドは、IContextOption.EditorAccessModeを評価します。  
  
該当する要素がない場合は、空のコレクションを返します。  
  
EditorAccessModeの指定によっては、最新のモデルの変更が同期されません。  
そのため、モデルを変更してから一度も表示していないエディタ等では、正しい情報が取得できない場合があることに注意してください。

[GetRichTextField](_extension_api_NextDesign.Core_IModel_methods_GetRichTextField.md)

指定したリッチテキストフィールドのフォーマットの値を取得します。  
指定したフォーマットに値がない場合はnullを返します。

[GetRichTextFieldCustomData](_extension_api_NextDesign.Core_IModel_methods_GetRichTextFieldCustomData.md)

指定したリッチテキストフィールドのカスタムフォーマットの値を取得します。  
指定したフォーマットが見つからない場合はnullを返します。

[GetRichTextFieldFormats](_extension_api_NextDesign.Core_IModel_methods_GetRichTextFieldFormats.md)

指定したリッチテキストフィールドの値が設定されているフォーマットの一覧を取得します。  
一覧の順序は不定です。値が空文字のフォーマットも返します。

[Is(IClass)](_extension_api_NextDesign.Core_IModel_methods_Is-3.md)

このインスタンスが指定したクラスのインスタンスであるか調べます。  
指定したクラスのインスタンスの場合はTrueを返します。

[Is(IPackage,string,bool)](_extension_api_NextDesign.Core_IModel_methods_Is-2.md)

このインスタンスが指定したクラスのインスタンスであるか調べます。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
指定したクラスのインスタンスの場合はTrueを返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[Is(string,bool)](_extension_api_NextDesign.Core_IModel_methods_Is-1.md)

このインスタンスが指定したクラスのインスタンスであるか調べます。  
指定したクラスのインスタンスの場合はTrueを返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[IsAppliedItem](_extension_api_NextDesign.Core_IModel_methods_IsAppliedItem.md)

このモデルがカレントのプロダクトで有効か調べます。

[IsAppliedItemTo](_extension_api_NextDesign.Core_IModel_methods_IsAppliedItemTo.md)

このモデルが指定したプロダクトで有効か調べます。

[IsAppliedItemToByName](_extension_api_NextDesign.Core_IModel_methods_IsAppliedItemToByName.md)

このモデルが指定した名前のプロダクトで有効か調べます。

[IsIn(IEnumerable<IClass>)](_extension_api_NextDesign.Core_IModel_methods_IsIn-5.md)

このインスタンスが指定したいずれかのクラスのインスタンスであるか調べます。  
指定したいずれかのクラスのインスタンスの場合はTrueを返します。

[IsIn(IEnumerable<string>,bool)](_extension_api_NextDesign.Core_IModel_methods_IsIn-3.md)

このインスタンスが指定したいずれかのクラスのインスタンスであるか調べます。  
指定したいずれかのクラスのインスタンスの場合はTrueを返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[IsIn(IPackage,IEnumerable<string>,bool)](_extension_api_NextDesign.Core_IModel_methods_IsIn-4.md)

このインスタンスが指定したいずれかのクラスのインスタンスであるか調べます。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
指定したいずれかのクラスのインスタンスの場合はTrueを返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[IsIn(IPackage,string,bool)](_extension_api_NextDesign.Core_IModel_methods_IsIn-2.md)

このインスタンスが指定したいずれかのクラスのインスタンスであるか調べます。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
指定したいずれかのクラスのインスタンスの場合はTrueを返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[IsIn(string,bool)](_extension_api_NextDesign.Core_IModel_methods_IsIn-1.md)

このインスタンスが指定したいずれかのクラスのインスタンスであるか調べます。  
指定したいずれかのクラスのインスタンスの場合はTrueを返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IModel の説明を参照してください。

[IsRelatedAtFieldTo](_extension_api_NextDesign.Core_IModel_methods_IsRelatedAtFieldTo.md)

このインスタンスが指定したフィールドで指定したモデルと参照関連を持つか調べます。  
関連を持つ場合はTrueを返します。

[IsRelatedTo](_extension_api_NextDesign.Core_IModel_methods_IsRelatedTo.md)

このインスタンスが指定したモデルと参照関連を持つか調べます。  
関連を持つ場合はTrueを返します。

[MoveTo](_extension_api_NextDesign.Core_IModel_methods_MoveTo.md)

このインスタンスを指定したモデルの子要素となるように移動します。  
移動先の親要素、およびフィールドが、現在の親要素、およびフィールドと同一の場合は、指定フィールドにおける要素の順序を変更します。  
なお、移動先フィールドの多重度上限制約が違反しても例外はスローされません。

[NotifyFieldChanged](_extension_api_NextDesign.Core_IModel_methods_NotifyFieldChanged.md)

指定したフィールドの値変更を通知します。

[Relate](_extension_api_NextDesign.Core_IModel_methods_Relate.md)

このインスタンスの指定したフィールドの末尾で指定したモデルを関連づけて、追加した関連インスタンスを返します。

[RelateAll](_extension_api_NextDesign.Core_IModel_methods_RelateAll.md)

このインスタンスの指定したモデルと関連付けが可能な全ての参照フィールドで指定したモデルを関連づけて、追加したすべての関連インスタンスのコレクションを返します。  
関連づけするモデルに削除されたモデル、一時プロキシが指定された場合、関連づけは行われません。  
関連づけが行われなかった場合は、空のコレクションを返します。  
  
このメソッドでは、次の制約を満たさないフィールドは関連付け対象から除外されます。  
\[評価する制約\]  
\- パス制約  
\- 型制約  
\- 多重度上限  
\- 操作可否

[RelateAllDerivedFrom](_extension_api_NextDesign.Core_IModel_methods_RelateAllDerivedFrom.md)

このインスタンスの指定したモデルを導出元として関連付けが可能な全てのフィールドで指定したモデルを導出元として関連づけて、追加したすべての関連インスタンスのコレクションを返します。  
関連づけするモデルに削除されたモデル、一時プロキシが指定された場合、関連づけは行われません。  
関連づけが行われなかった場合は、空のコレクションを返します。  
  
このメソッドでは、次の制約を満たさないフィールドは関連付け対象から除外されます。  
\[評価する制約\]  
\- パス制約  
\- 型制約  
\- 多重度上限  
\- 操作可否

[RelateAllDerivedTo](_extension_api_NextDesign.Core_IModel_methods_RelateAllDerivedTo.md)

このインスタンスの指定したモデルを導出先として関連付けが可能な全てのフィールドで指定したモデルを導出先として関連づけて、追加したすべての関連インスタンスのコレクションを返します。  
関連づけするモデルに削除されたモデル、一時プロキシが指定された場合、関連づけは行われません。  
関連づけが行われなかった場合は、空のコレクションを返します。  
  
このメソッドでは、次の制約を満たさないフィールドは関連付け対象から除外されます。  
\[評価する制約\]  
\- パス制約  
\- 型制約  
\- 多重度上限  
\- 操作可否

[RelateAt](_extension_api_NextDesign.Core_IModel_methods_RelateAt.md)

このインスタンスの指定したフィールドで、追加位置を指定して指定したモデルを関連づけて、追加した関連インスタンスを返します。

[RelateByClassTag](_extension_api_NextDesign.Core_IModel_methods_RelateByClassTag.md)

このインスタンスの指定したタグが付与された関連クラスによって関連づけ可能な参照フィールドで指定したモデルを関連づけて、追加したすべての関連インスタンスのコレクションを返します。  
関連づけするモデルに削除されたモデル、一時プロキシが指定された場合、関連づけは行われません。  
関連づけが行われなかった場合は、空のコレクションを返します。  
  
このメソッドでは、次の制約を満たさないフィールドは関連付け対象から除外されます。  
\[評価する制約\]  
\- パス制約  
\- 型制約  
\- 多重度上限  
\- 操作可否

[RelateByFieldTag](_extension_api_NextDesign.Core_IModel_methods_RelateByFieldTag.md)

このインスタンスの指定したタグが付与された参照フィールドで指定したモデルを関連づけて、追加したすべての関連インスタンスのコレクションを返します。  
関連づけするモデルに削除されたモデル、一時プロキシが指定された場合、関連づけは行われません。  
関連づけが行われなかった場合は、空のコレクションを返します。  
  
このメソッドでは、次の制約を満たさないフィールドは関連付け対象から除外されます。  
\[評価する制約\]  
\- パス制約  
\- 型制約  
\- 多重度上限  
\- 操作可否

[RelateWhere](_extension_api_NextDesign.Core_IModel_methods_RelateWhere.md)

このインスタンスの指定した条件に合致する全ての参照フィールドで指定したモデルを関連づけて、追加したすべての関連インスタンスのコレクションを返します。  
関連づけするモデルに削除されたモデル、一時プロキシが指定された場合、関連づけは行われません。  
関連づけが行われなかった場合は、空のコレクションを返します。  
  
関連づけする参照フィールドは、評価関数により、任意に決定することができます。  
ただし、評価関数に合致しても、以下の条件に該当するフィールドの場合は、関連づけは行われず正常終了します。  
・フィールドのパス制約を違反する場合  
・フィールドの型が与えられたモデルと互換しない場合  
・フィールドの多重度を超える場合  
・条件に合致するフィールドが以下の操作不可フィールドであった場合  
\- プロダクトラインのフィーチャ割り当てフィールド  
\- System.Coreタグが付与されているフィールド

[ReleaseAllAssignedFeatures](_extension_api_NextDesign.Core_IModel_methods_ReleaseAllAssignedFeatures.md)

このモデルに割り当てられたすべてのフィーチャとの割り当てを解除します。  
この呼び出しでフィーチャとの割り当てを解除した場合はプロダクト適用条件式も削除されます。

[ReleaseAssignedFeature](_extension_api_NextDesign.Core_IModel_methods_ReleaseAssignedFeature.md)

指定したフィーチャのこのモデルへの割り当てを解除します。

[ReleaseAssignedFeatureByName](_extension_api_NextDesign.Core_IModel_methods_ReleaseAssignedFeatureByName.md)

指定した名前のフィーチャについて、このモデルへの割り当てを解除します。

[ReleaseAssignedFeatures](_extension_api_NextDesign.Core_IModel_methods_ReleaseAssignedFeatures.md)

指定したすべてのフィーチャのこのモデルへの割り当てを解除します。

[ReleaseAssignedFeaturesByName](_extension_api_NextDesign.Core_IModel_methods_ReleaseAssignedFeaturesByName.md)

指定した名前のすべてのフィーチャについて、このモデルへの割り当てを解除します。

[RemoveError](_extension_api_NextDesign.Core_IModel_methods_RemoveError.md)

このモデルに対して追加されているエラー情報を削除します。  
削除対象のエラー情報に null または、このモデルに含まれないエラー情報を指定した場合は、何も行われず正常終了します。

[RemoveField](_extension_api_NextDesign.Core_IModel_methods_RemoveField.md)

このインスタンスの指定したフィールドの値を削除します。  
指定したフィールドが所有フィールドの場合は、指定したモデルを削除します。  
指定したフィールドが参照フィールドの場合は、参照関連のみを削除し、モデルは維持されます。  
  
なお、削除対象として指定されたモデルが、指定フィールドに含まれない場合は、何も行われず正常終了します。

[RemoveFieldAt](_extension_api_NextDesign.Core_IModel_methods_RemoveFieldAt.md)

このインスタンスの指定した位置のフィールド値を削除します。  
指定したフィールドが所有フィールドの場合は、指定した位置のモデルを削除します。  
指定したフィールドが参照フィールドの場合は、指定した位置の参照関連のみを削除し、モデルは維持されます。

[SetField](_extension_api_NextDesign.Core_IModel_methods_SetField.md)

このインスタンスの指定したフィールドに、指定した値を設定します。  
指定したフィールドに設定できない値を指定した場合は、例外がスローされます。  
なお、指定したフィールドの多重度が2以上の場合は、該当フィールドの先頭要素を設定します。

[SetFieldAt](_extension_api_NextDesign.Core_IModel_methods_SetFieldAt.md)

このインスタンスの指定したフィールドの指定したインデックス位置に、指定した値を設定します。  
指定したフィールドに設定できない値を指定した場合は、例外がスローされます。  
なお、フィールドの多重度上限制約、パス制約に違反しても例外はスローされません。

[SetInitField](_extension_api_NextDesign.Core_IModel_methods_SetInitField.md)

このインスタンスの指定したフィールドに、メタモデルで定義するフィールドの初期値を設定します。  
メタモデルでフィールドの初期値を指定していない場合は、フィールド型のデフォルト値を設定します。  
  
クラス型の所有フィールドに対してこのメソッドを呼び出した場合、フィールド値として設定されていたIModelは削除されます。  
また、クラス型の参照フィールドに対してこのメソッドを呼び出した場合、フィールド値として設定されていたIModelとの関連が削除されます（フィールド値のIModelは削除されずに維持されます）。

[SetProductApplyCondition](_extension_api_NextDesign.Core_IModel_methods_SetProductApplyCondition.md)

このモデルのプロダクト適用条件式を設定します。  
なお、プロダクト適用条件式内において、このモデルに未割り当てのフィーチャ名を使用していた場合は、自動的にフィーチャが割り当てられます。  
また、条件式で、このモデルに割り当て済みのフィーチャ名が使用されなかった場合は、自動的にフィーチャとの割り当てが解除されます。  
条件式に空の文字列を指定した場合は、全てのフィーチャ割り当てが解除されます。

[SetRichTextField(string,string,string,string)](_extension_api_NextDesign.Core_IModel_methods_SetRichTextField-3.md)

指定したリッチテキストフィールドのフォーマットに値を設定し、同時にテキスト値も設定します。  
"html"フォーマットを指定した場合は、textValueの指定は無視し、valueをテキスト値に設定します。  
本APIの処理では若干のオーバーヘッドが生じます。速度性能が求められる場合は、本APIの代わりにSetRichTextFieldValues()を利用ください。

[SetRichTextField(string,string,string)](_extension_api_NextDesign.Core_IModel_methods_SetRichTextField-2.md)

指定したリッチテキストフィールドのフォーマットに値を設定します。  
"html"フォーマットを指定した場合は、"text"フォーマットの値も自動生成して同時に設定します。  
その際は処理時間に若干のオーバーヘッドが生じます。速度性能が求められる場合は、本APIの代わりにSetRichTextFieldValues()を利用ください。

[SetRichTextField(string,string)](_extension_api_NextDesign.Core_IModel_methods_SetRichTextField-1.md)

指定したリッチテキストフィールドにテキスト値を設定します。

[SetRichTextFieldCustomData](_extension_api_NextDesign.Core_IModel_methods_SetRichTextFieldCustomData.md)

指定したリッチテキストフィールドのカスタムフォーマットに値を設定します。  
  
カスタムフォーマットの値はUI上には表示されません。APIでのみ取得・設定ができます。

[SetRichTextFieldValues](_extension_api_NextDesign.Core_IModel_methods_SetRichTextFieldValues.md)

指定したリッチテキストフィールドにHtml値とテキスト値を設定します。

[Take](_extension_api_NextDesign.Core_IModel_methods_Take.md)

このインスタンスの指定したフィールドへ指定したモデルを移動します。  
移動対象のモデルの親要素がこのインスタンスとなります。  
なお、移動先フィールドの多重度上限制約が違反しても例外はスローされません。

[UnRelate](_extension_api_NextDesign.Core_IModel_methods_UnRelate.md)

このインスタンスの指定したフィールドで指定したモデルとの参照関連づけを解除します。  
該当フィールドにおいて、複数の関連づけがある場合は、そのすべての関連付けを解除します。  
指定したモデルとの関連が存在しなかった場合は、何も行われず正常終了します。

[UnRelateAll](_extension_api_NextDesign.Core_IModel_methods_UnRelateAll.md)

このインスタンスの指定したモデルとの全ての参照関連づけを解除します。  
指定したモデルとの関連が存在しなかった場合は、何も行われず正常終了します。  
また、以下の条件に該当するフィールドへの関連づけも解除されず正常終了します。  
・プロダクトラインサポート向けフィールドでの関連付け  
・System.Coreタグが付与されているフィールドでの関連付け

[UnRelateAllDerivedFrom](_extension_api_NextDesign.Core_IModel_methods_UnRelateAllDerivedFrom.md)

このインスタンスの指定したモデルを導出元とする全ての関連づけを解除します。  
指定したモデルとの関連が存在しなかった場合は、何も行われず正常終了します。  
また、以下の条件に該当するフィールドへの関連づけも解除されず正常終了します。  
・プロダクトラインサポート向けフィールドでの関連付け  
・System.Coreタグが付与されているフィールドでの関連付け

[UnRelateAllDerivedTo](_extension_api_NextDesign.Core_IModel_methods_UnRelateAllDerivedTo.md)

このインスタンスの指定したモデルを導出先とする全ての関連づけを解除します。  
指定したモデルとの関連が存在しなかった場合は、何も行われず正常終了します。  
また、以下の条件に該当するフィールドへの関連づけも解除されず正常終了します。  
・プロダクトラインサポート向けフィールドでの関連付け  
・System.Coreタグが付与されているフィールドでの関連付け

[UnRelateByClassTag](_extension_api_NextDesign.Core_IModel_methods_UnRelateByClassTag.md)

このインスタンスの指定したタグが付与された関連クラスによって関連づけ可能な参照フィールドで指定したモデルとの関連づけを解除します。  
指定したモデルとの関連が存在しなかった場合は、何も行われず正常終了します。  
また、以下の条件に該当するフィールドへの関連づけも解除されず正常終了します。  
・プロダクトラインサポート向けフィールドでの関連付け  
・System.Coreタグが付与されているフィールドでの関連付け

[UnRelateByFieldTag](_extension_api_NextDesign.Core_IModel_methods_UnRelateByFieldTag.md)

このインスタンスの指定したタグが付与された参照フィールドで指定したモデルとの関連づけを解除します。  
指定したモデルとの関連が存在しなかった場合は、何も行われず正常終了します。  
また、以下の条件に該当するフィールドへの関連づけも解除されず正常終了します。  
・プロダクトラインサポート向けフィールドでの関連付け  
・System.Coreタグが付与されているフィールドでの関連付け

[UnRelateWhere](_extension_api_NextDesign.Core_IModel_methods_UnRelateWhere.md)

このインスタンスの指定したモデルとの指定した条件に合致する全ての参照関連づけを解除します。  
  
関連づけを解除する対象は評価関数により、任意に決定することができます。  
ただし、以下の条件に該当する関連づけは解除されずに正常終了します。  
・条件に合致する関連がプロダクトラインサポート向けの関連であった場合  
・条件に合致する関連端のフィールドにSystem.Coreタグが付与されている場合

[Validate](_extension_api_NextDesign.Core_IModel_methods_Validate-1.md)

このモデルを検証します。  
このモデル、およびこのモデルが所有する子要素以下の全ての要素を再帰的に検証します。  
このメソッドは、実行時に以前のエラー情報がすべてクリアされます。  
  
\[プロジェクト（IProject）に対してこのメソッドを実行した場合\]  
・プロジェクトに未ロードのモデルファイルがあればその旨のエラー情報を設計モデルの基点となるモデル(モデルナビゲータのルートに表示するモデル)に追加します。これは、未ロードのモデルファイルがあるため、プロジェクトの全てのモデルに対し検証ができていないことを呼び出し元に知らせるためです。  
・プロジェクトに未ロードのモデルファイルがあることによりプロジェクトの所有関係で到達できないモデル(親が未ロードのモデル)も含みます。  
  
\[検証内容\]  
・アプリケーションが既定する標準の検証  
・エクステンションの検証イベントにより拡張した検証

[Validate(ValidationOptions)](_extension_api_NextDesign.Core_IModel_methods_Validate-2.md)

このモデルを検証します。  
オプションの指定により、このモデルのみ検証、または、このモデル、およびこのモデルが所有する子要素以下の全ての要素を再帰的に検証します。  
このメソッドは、実行時に以前のエラー情報がすべてクリアされます。  
  
\[プロジェクト（IProject）に対してこのメソッドを実行した場合\]  
・プロジェクトに未ロードのモデルファイルがあればその旨のエラー情報を設計モデルの基点となるモデル(モデルナビゲータのルートに表示するモデル)に追加します。これは、未ロードのモデルファイルがあるため、プロジェクトの全てのモデルに対し検証ができていないことを呼び出し元に知らせるためです。  
・プロジェクトに未ロードのモデルファイルがあることによりプロジェクトの所有関係で到達できないモデル(親が未ロードのモデル)も含みます。  
  
\[検証内容\]  
・オプションで有効としたアプリケーションが既定する標準の検証  
・オプションでモデル検証時イベントの発行が指定されている場合は、エクステンションの検証イベントにより拡張した検証

拡張メソッド​
-----------------------------------

名前

説明

[IsDesignModel](_extension_api_NextDesign.Core_ModelExtensions_methods_IsDesignModel.md)

このモデルが設計モデルルート (モデルナビゲータのルートモデル) であるか調べます。

[IsProductLineElement](_extension_api_NextDesign.Core_ModelExtensions_methods_IsProductLineElement.md)

このモデルがプロダクトライン要素であるか調べます。

[IsReadonly](_extension_api_NextDesign.Core_ModelExtensions_methods_IsReadonly.md)

このモデルが読み取り専用であるか調べます。  
このモデルに対応するモデルユニットが取得できない場合はFalseを返します。

関連項目​
-----------------------------

名前

説明

モデルを編集する

APIを通してNextDesignの各種モデル情報を編集します。