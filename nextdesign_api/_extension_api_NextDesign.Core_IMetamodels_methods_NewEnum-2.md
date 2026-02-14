IMetamodels.NewEnum(string,IEnumerable<string>,IPackage) メソッド
=============================================================

名前空間: NextDesign.Core

説明​
-----------------------

新しい列挙型を生成します。

引数​
-----------------------

名前

型

説明

name

string

列挙型名

values

IEnumerable<string>

リテラル名

owner

IPackage

所属するパッケージ  
既定値は null です。

戻り値​
--------------------------

*   [IEnum](_extension_api_NextDesign.Core_IEnum.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

列挙型名 に null/空文字/使用不可文字 が指定された場合  
リテラル名 がひとつも指定されていない場合  
リテラル名 が重複する場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合