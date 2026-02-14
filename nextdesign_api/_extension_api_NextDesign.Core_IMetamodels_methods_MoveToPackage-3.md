IMetamodels.MoveToPackage(IEnumerable<IClass>,IPackage) メソッド
============================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスを指定したパッケージ管理下に移動します。

引数​
-----------------------

名前

型

説明

targets

IEnumerable<IClass>

移動するクラス

newOwner

IPackage

移動先のパッケージ

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

移動先パッケージが指定されていない場合

一意制約違反

ExtensionDuplicationException

指定したクラスを移動すると、移動先でクラス名、または列挙型名が重複する場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合  
－移動先で指定したクラス名が重複する