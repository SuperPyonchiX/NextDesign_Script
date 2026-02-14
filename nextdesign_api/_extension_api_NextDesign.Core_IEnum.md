IEnum インタフェース
=============

名前空間: NextDesign.Core

説明​
-----------------------

列挙型へのアクセスオブジェクトです。

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

プロパティ​
--------------------------------

名前

説明

[FullName](_extension_api_NextDesign.Core_IEnum_properties_FullName.md)

完全修飾名  
値の変更により、パッケージ移動、および列挙名が変更できます。  
ただし、移動先のパッケージが存在しない場合は、例外がスローされます。

[Literals](_extension_api_NextDesign.Core_IEnum_properties_Literals.md)

リテラル一覧

[Owner](_extension_api_NextDesign.Core_IEnum_properties_Owner.md)

パッケージ  
値の変更により、パッケージを移動できますが、null を指定することはできません。

メソッド​
-----------------------------

名前

説明

[GetLiteralsByTag](_extension_api_NextDesign.Core_IEnum_methods_GetLiteralsByTag.md)

指定されたタグが付与された列挙型リテラルを取得します。

[Is](_extension_api_NextDesign.Core_IEnum_methods_Is.md)

指定したオブジェクトが列挙型のインスタンスかどうか調べます。  
列挙型のインスタンスの場合はtrueを返します。

[NameOf](_extension_api_NextDesign.Core_IEnum_methods_NameOf.md)

リテラル文字列に該当する列挙型リテラルを取得します。

[ValueOf](_extension_api_NextDesign.Core_IEnum_methods_ValueOf.md)

Enum値に該当する列挙型リテラルを取得します。