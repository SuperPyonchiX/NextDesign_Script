IMetamodels インタフェース
===================

名前空間: NextDesign.Core

説明​
-----------------------

メタモデル管理オブジェクトです。

所属エリア​
--------------------------------

名前

説明

[プロファイル](_extension_api_overview_profile.md)

プロファイルにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[AllClasses](_extension_api_NextDesign.Core_IMetamodels_properties_AllClasses.md)

クラス一覧

[AllEnums](_extension_api_NextDesign.Core_IMetamodels_properties_AllEnums.md)

列挙型一覧

[AllPackages](_extension_api_NextDesign.Core_IMetamodels_properties_AllPackages.md)

パッケージ一覧

メソッド​
-----------------------------

名前

説明

[AddLiteral](_extension_api_NextDesign.Core_IMetamodels_methods_AddLiteral.md)

指定した列挙型に、指定したリテラル文字列で新しい列挙型リテラルを追加します。

[AddPathConstraint(string,IClass,IField,string)](_extension_api_NextDesign.Core_IMetamodels_methods_AddPathConstraint-3.md)

指定したクラスの指定したフィールドにパス制約を追加します。  
なお、ここで設定するパス文字列に該当するパスの存在はチェックされません。誤ったパスを指定しても、このメソッドは正常終了します。

[AddPathConstraint(string,IPackage,string,string,string)](_extension_api_NextDesign.Core_IMetamodels_methods_AddPathConstraint-2.md)

指定したクラスの指定したフィールドにパス制約を追加します。  
指定クラスは、スコープで指定したパッケージの配下から探索します。  
  
なお、ここで設定するパス文字列に該当するパスの存在はチェックされません。誤ったパスを指定しても、このメソッドは正常終了します。

[AddPathConstraint(string,string,string,string)](_extension_api_NextDesign.Core_IMetamodels_methods_AddPathConstraint-1.md)

指定したクラスの指定したフィールドにパス制約を追加します。  
なお、ここで設定するパス文字列に該当するパスの存在はチェックされません。誤ったパスを指定しても、このメソッドは正常終了します。

[AddProperty](_extension_api_NextDesign.Core_IMetamodels_methods_AddProperty.md)

指定したクラスに新しいプロパティを追加します。

[AddSuperClasses(IClass,IEnumerable<IClass>)](_extension_api_NextDesign.Core_IMetamodels_methods_AddSuperClasses-3.md)

指定したクラスのスーパークラスを設定します。

[AddSuperClasses(IClass,IPackage,string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_AddSuperClasses-2.md)

指定したクラスのスーパークラスを設定します。  
設定するスーパークラスは、スコープで指定したパッケージの配下から探索します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[AddSuperClasses(IClass,string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_AddSuperClasses-1.md)

指定したクラスのスーパークラスを設定します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[FindClassesByName(IEnumerable<string>,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_FindClassesByName-3.md)

指定したクラス名のクラスを検索します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[FindClassesByName(IPackage,IEnumerable<string>,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_FindClassesByName-4.md)

スコープで指定したパッケージの配下から指定したクラス名のクラスを検索します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[FindClassesByName(IPackage,string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_FindClassesByName-2.md)

スコープで指定したパッケージの配下から指定したクラス名のクラスを検索します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[FindClassesByName(string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_FindClassesByName-1.md)

指定したクラス名のクラスを検索します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[FindClassesByTag](_extension_api_NextDesign.Core_IMetamodels_methods_FindClassesByTag.md)

指定したタグが付与されたクラスを検索します。

[FindClassesWithField](_extension_api_NextDesign.Core_IMetamodels_methods_FindClassesWithField.md)

指定したフィールドをもつクラスを検索します。

[FindEnumsByTag](_extension_api_NextDesign.Core_IMetamodels_methods_FindEnumsByTag.md)

指定したタグが付与された列挙型を検索します。

[FindPackagesByName(IPackage,string)](_extension_api_NextDesign.Core_IMetamodels_methods_FindPackagesByName-2.md)

スコープで指定したパッケージの配下から指定した名前のパッケージを探索します。

[FindPackagesByName(string)](_extension_api_NextDesign.Core_IMetamodels_methods_FindPackagesByName-1.md)

指定した名前のパッケージを探索します。

[FindPackagesByTag](_extension_api_NextDesign.Core_IMetamodels_methods_FindPackagesByTag.md)

指定したタグが付与されたパッケージを検索します。

[GetClass(IPackage,string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_GetClass-2.md)

スコープで指定したパッケージの配下から指定した名前のクラスを取得します。  
同じ名前のクラスが複数定義されている場合、定義順で最初に見つかったクラスを返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[GetClass(string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_GetClass-1.md)

指定した名前のクラスを取得します。  
同じ名前のクラスが複数定義されている場合、定義順で最初に見つかったクラスを返します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[GetEnum(IPackage,string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_GetEnum-2.md)

スコープで指定したパッケージの配下から指定した名前の列挙型を取得します。  
同じ名前の列挙型が複数定義されている場合、定義順で最初に見つかった列挙型を返します。  
  
列挙型名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[GetEnum(string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_GetEnum-1.md)

指定した名前の列挙型を取得します。  
同じ名前の列挙型が複数定義されている場合、定義順で最初に見つかった列挙型を返します。  
  
列挙型名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[GetPackage](_extension_api_NextDesign.Core_IMetamodels_methods_GetPackage.md)

指定した完全修飾名のパッケージを取得します。  
パッケージが見つからない場合は null を返します。

[GetPackageById](_extension_api_NextDesign.Core_IMetamodels_methods_GetPackageById.md)

指定した識別子のパッケージを取得します。  
パッケージが見つからない場合は null を返します。

[GetSubClasses](_extension_api_NextDesign.Core_IMetamodels_methods_GetSubClasses.md)

指定したクラスのサブクラスを取得します。

[GetTypeById<T>](_extension_api_NextDesign.Core_IMetamodels_methods_GetTypeById.md)

指定した識別子の型を取得します。  
型が見つからない場合は null を返します。

[GetTypeByName<T>(IPackage,string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_GetTypeByName-2.md)

スコープで指定したパッケージの配下から指定した名前の型を取得します。  
同じ名前の型が複数定義されている場合、定義順で最初に見つかった型を返します。  
型が見つからない場合は null を返します。  
  
型名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[GetTypeByName<T>(string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_GetTypeByName-1.md)

指定した名前の型を取得します。  
同じ名前の型が複数定義されている場合、定義順で最初に見つかった型を返します。  
型が見つからない場合は null を返します。  
  
型名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[MoveToPackage(IEnumerable<IClass>,IPackage)](_extension_api_NextDesign.Core_IMetamodels_methods_MoveToPackage-3.md)

指定したクラスを指定したパッケージ管理下に移動します。

[MoveToPackage(IPackage,string,IPackage,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_MoveToPackage-2.md)

指定したクラスを指定したパッケージ管理下に移動します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[MoveToPackage(string,IPackage,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_MoveToPackage-1.md)

指定したクラスを指定したパッケージ管理下に移動します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[NewClass](_extension_api_NextDesign.Core_IMetamodels_methods_NewClass.md)

新しいクラスを生成します。

[NewEnum(string,IEnumerable<string>,IPackage)](_extension_api_NextDesign.Core_IMetamodels_methods_NewEnum-2.md)

新しい列挙型を生成します。

[NewEnum(string,string,IPackage)](_extension_api_NextDesign.Core_IMetamodels_methods_NewEnum-1.md)

新しい列挙型を生成します。

[NewPackage](_extension_api_NextDesign.Core_IMetamodels_methods_NewPackage.md)

新しいパッケージを生成します。

[Relate](_extension_api_NextDesign.Core_IMetamodels_methods_Relate.md)

指定したクラス間を関連づけます。

[RemoveClass](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveClass.md)

指定したクラスを削除します。

[RemoveConstraint](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveConstraint.md)

指定した制約を削除します。

[RemoveConstraints](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveConstraints.md)

指定した制約をすべて削除します。

[RemoveEnum](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveEnum.md)

指定した列挙型を削除します。

[RemoveLiteral](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveLiteral.md)

列挙型リテラルを削除します。

[RemovePathConstraint(IClass,IField)](_extension_api_NextDesign.Core_IMetamodels_methods_RemovePathConstraint-3.md)

指定したクラスの指定したフィールドのパス制約を削除します。

[RemovePathConstraint(IPackage,string,string)](_extension_api_NextDesign.Core_IMetamodels_methods_RemovePathConstraint-2.md)

指定したクラスの指定したフィールドのパス制約を削除します。

[RemovePathConstraint(string,string)](_extension_api_NextDesign.Core_IMetamodels_methods_RemovePathConstraint-1.md)

指定したクラスの指定したフィールドのパス制約を削除します。

[RemoveProperty](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveProperty.md)

指定したクラスのプロパティを削除します。

[RemoveSuperClasses(IClass,IEnumerable<IClass>)](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveSuperClasses-3.md)

指定したクラスのスーパークラスを削除します。  
指定したスーパークラスの列挙のうち、指定したクラスのスーパークラスに含まれないクラスはスキップされます。  
クラスの継承関係を削除すると継承先クラスのモデルも削除します。

[RemoveSuperClasses(IClass,IPackage,string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveSuperClasses-2.md)

指定したクラスのスーパークラスを削除します。  
削除するスーパークラスは、スコープで指定したパッケージの配下から探索します。  
指定したスーパークラス名の列挙のうち、指定したクラスのスーパークラスに含まれないクラスはスキップされます。  
クラスの継承関係を削除すると継承先クラスのモデルも削除します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[RemoveSuperClasses(IClass,string,bool)](_extension_api_NextDesign.Core_IMetamodels_methods_RemoveSuperClasses-1.md)

指定したクラスのスーパークラスを削除します。  
指定したスーパークラス名の列挙のうち、指定したクラスのスーパークラスに含まれないクラスはスキップされます。  
クラスの継承関係を削除すると継承先クラスのモデルも削除します。  
  
クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

[UnRelate](_extension_api_NextDesign.Core_IMetamodels_methods_UnRelate.md)

指定したクラス間の関連づけを削除します。