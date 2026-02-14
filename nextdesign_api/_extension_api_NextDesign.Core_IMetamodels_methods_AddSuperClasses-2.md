IMetamodels.AddSuperClasses(IClass,IPackage,string,bool) メソッド
=============================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスのスーパークラスを設定します。  
設定するスーパークラスは、スコープで指定したパッケージの配下から探索します。

クラス名の指定方法、およびあいまい一致オプションについては、IMetamodels の説明を参照してください。

引数​
-----------------------

名前

型

説明

target

IClass

対象クラス

scope

IPackage

スコープ(探索範囲の基点となるパッケージ)

superClassNames

string

スーパークラス名  
カンマ区切りで複数のクラス名を指定することができます。

fuzzy

bool

あいまい一致オプション  
既定値はtrueです。  
fuzzyにfalseを指定した場合、クラス名を完全修飾名で評価します。  
同名クラスが存在する場合は、fuzzyにfalseを指定し、superClassNamesに完全修飾名を指定することで期待するクラスを特定することができます。

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

指定されたクラスが見つからない場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合