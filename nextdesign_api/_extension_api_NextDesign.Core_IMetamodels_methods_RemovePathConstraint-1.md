IMetamodels.RemovePathConstraint(string,string) メソッド
====================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスの指定したフィールドのパス制約を削除します。

引数​
-----------------------

名前

型

説明

className

string

制約の有効範囲となるクラス名

targetFieldName

string

制約の対象フィールド名

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

指定された名前のクラスが見つからなかった場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定された名前のフィールドが見つからなかった場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合