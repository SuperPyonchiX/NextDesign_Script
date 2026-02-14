IMetamodels.AddPathConstraint(string,IClass,IField,string) メソッド
===============================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したクラスの指定したフィールドにパス制約を追加します。  
なお、ここで設定するパス文字列に該当するパスの存在はチェックされません。誤ったパスを指定しても、このメソッドは正常終了します。

引数​
-----------------------

名前

型

説明

name

string

制約名

scope

IClass

制約の有効範囲となるクラス

targetField

IField

制約の対象フィールド

paths

string

パス文字列  
複数のパスを指定する場合は';'（セミコロン）で区切ります。

戻り値​
--------------------------

*   [IConstraint](_extension_api_NextDesign.Core_IConstraint.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

制約名に null/空文字/使用不可文字 が指定された場合  
制約の有効範囲となるクラス、制約の対象フィールドが指定されていない場合

プロファイル編集不可

ExtensionEditProfileException

プロファイル編集操作に失敗した場合