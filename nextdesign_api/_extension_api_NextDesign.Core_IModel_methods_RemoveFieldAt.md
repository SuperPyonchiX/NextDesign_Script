IModel.RemoveFieldAt メソッド
=========================

名前空間: NextDesign.Core

説明​
-----------------------

このインスタンスの指定した位置のフィールド値を削除します。  
指定したフィールドが所有フィールドの場合は、指定した位置のモデルを削除します。  
指定したフィールドが参照フィールドの場合は、指定した位置の参照関連のみを削除し、モデルは維持されます。

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

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

fieldName に null または空文字列を指定した場合

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

不正操作

ExtensionInvalidOperationException

フィールド名に操作不可のフィールドが指定された場合  
\- プロダクトラインのフィーチャ割り当てフィールド  
\- System.Coreタグが付与されているフィールド  
無効なフィールドが指定された場合