IPackage インタフェース
================

名前空間: NextDesign.Core

説明​
-----------------------

パッケージへのアクセスオブジェクトです。

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

[INamedElement](_extension_api_NextDesign.Core_INamedElement.md)

名前付け可能要素を表します。

プロパティ​
--------------------------------

名前

説明

[FullName](_extension_api_NextDesign.Core_IPackage_properties_FullName.md)

完全修飾名  
Uriと同一の値を返します。

[OwnedClasses](_extension_api_NextDesign.Core_IPackage_properties_OwnedClasses.md)

管理クラス一覧

[OwnedEnums](_extension_api_NextDesign.Core_IPackage_properties_OwnedEnums.md)

管理する列挙型の一覧

[OwnedTypes](_extension_api_NextDesign.Core_IPackage_properties_OwnedTypes.md)

このパッケージが直接管理する型(IClass, IEnum)の一覧

[Parent](_extension_api_NextDesign.Core_IPackage_properties_Parent.md)

親パッケージ

[ProfileReference](_extension_api_NextDesign.Core_IPackage_properties_ProfileReference.md)

このパッケージが参照しているプロファイル参照情報  
このパッケージがプロファイル参照パッケージではない場合は null を返します。

[SubPackages](_extension_api_NextDesign.Core_IPackage_properties_SubPackages.md)

サブパッケージ一覧

[Uri](_extension_api_NextDesign.Core_IPackage_properties_Uri.md)

名前空間  
FullNameと同一の値を返します。

メソッド​
-----------------------------

名前

説明

[GetAllClasses](_extension_api_NextDesign.Core_IPackage_methods_GetAllClasses.md)

このパッケージを基点にネストするパッケージを含めて定義されているクラスの一覧を取得します。  
※このパッケージが直接管理するクラスも含まれます。

[GetAllEnums](_extension_api_NextDesign.Core_IPackage_methods_GetAllEnums.md)

このパッケージを基点にネストするパッケージを含めて定義されている列挙型の一覧を取得します。  
※このパッケージが直接管理する列挙型も含まれます。

[GetAllSubPackages](_extension_api_NextDesign.Core_IPackage_methods_GetAllSubPackages.md)

このパッケージを基点にネストする全てのサブパッケージを取得します。  
※このパッケージ自身は含まれません。

[GetAllTypes](_extension_api_NextDesign.Core_IPackage_methods_GetAllTypes.md)

このパッケージを基点にネストするパッケージを含めて定義されている型の一覧を取得します。  
※このパッケージが直接管理する型も含まれます。

[GetOwnerPackages](_extension_api_NextDesign.Core_IPackage_methods_GetOwnerPackages.md)

このパッケージを基点に親方向に探索できる全てのパッケージを取得します。  
パッケージの順序は、最も近い親を先頭にプロファイルのルートパッケージが末尾となります。

[GetTypeByName<T>](_extension_api_NextDesign.Core_IPackage_methods_GetTypeByName.md)

このパッケージ配下から指定した名前の型を取得します。  
型が見つからない場合は null を返します。  
  
recursive に`true`を指定した場合は、深さ優先探索でサブパッケージも探索の対象とします。  
この時、指定した名前の型が複数見つかった場合は、最初に見つかった型を返します。

[GetTypesByName<T>(IEnumerable<string>,bool)](_extension_api_NextDesign.Core_IPackage_methods_GetTypesByName-2.md)

このパッケージ配下から指定した名前のいずれかに一致する型を全て取得します。  
複数の型名を指定した場合は、そのいずれかに一致していれば対象となります。  
  
recursive に`true`を指定した場合は、深さ優先探索でサブパッケージも探索の対象とします。

[GetTypesByName<T>(string,bool)](_extension_api_NextDesign.Core_IPackage_methods_GetTypesByName-1.md)

このパッケージ配下から指定した名前のいずれかに一致する型を全て取得します。  
型名には、カンマ区切りで複数の型名を指定できます。  
複数の型名を指定した場合は、そのいずれかに一致していれば対象となります。  
  
recursive に`true`を指定した場合は、深さ優先探索でサブパッケージも探索の対象とします。