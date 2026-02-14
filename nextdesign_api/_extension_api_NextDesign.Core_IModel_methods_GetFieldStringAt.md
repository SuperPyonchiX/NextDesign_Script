IModel.GetFieldStringAt メソッド
============================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスの指定したフィールドの指定したインデックス位置の値を文字列形式で取得します。  
指定したインデックス位置のフィールドがクラス型の場合、そのフィールドの先頭のインスタンス名を取得します。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

index

int

位置  
先頭位置を0とするインデックスを指定します。

戻り値​
--------------------------

*   string

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

fieldName に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定されたフィールドがこのインスタンスのメタクラスで見つからない場合

不正フィールドアクセス

ExtensionIllegalFieldAccessException

多重度の上限が1のフィールドに対してこのメソッドを実行した場合

インデックス範囲不正

ExtensionOutOfRangeException

index に 負数が指定された場合  
または、index に該当フィールドの要素数以上の値が指定された場合