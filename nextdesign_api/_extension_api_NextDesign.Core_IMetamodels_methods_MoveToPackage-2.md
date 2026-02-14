IMetamodels.MoveToPackage(IPackage,string,IPackage,bool) メソッド
=============================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスを指定したパッケージ管理下に移動します。

クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

引数​
-----------------------

名前

型

説明

scope

IPackage

スコープ(探索範囲の基点となるパッケージ)

classNames

string

移動するクラス名  
カンマ区切りで複数のクラス名を指定することができます。

newOwner

IPackage

移動先のパッケージ

fuzzy

bool

あいまい一致オプション  
既定値はtrueです。  
fuzzyにfalseを指定した場合、クラス名を完全修飾名で評価します。  
同名クラスが存在する場合は、fuzzyにfalseを指定し、classNamesに完全修飾名を指定することで期待するクラスを移動することができます。

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

クラスが見つからない

ExtensionTypeNotFoundException

移動するクラス名で指定されたクラスがひとつも見つからない場合

引数不正

ExtensionArgumentException

移動先パッケージが指定されていない場合

一意制約違反

ExtensionDuplicationException

指定したクラスを移動すると、移動先でクラス名、または列挙型名が重複する場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合