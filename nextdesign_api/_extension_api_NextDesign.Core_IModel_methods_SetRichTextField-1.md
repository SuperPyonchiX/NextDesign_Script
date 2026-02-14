IModel.SetRichTextField(string,string) メソッド
===========================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したリッチテキストフィールドにテキスト値を設定します。

引数​
-----------------------

名前

型

説明

fieldName

string

フィールド名  
null、または空文字列は指定できません。

value

string

フィールドのテキスト値

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

fieldName に null、または空文字列 を指定した場合

フィールドが見つからない

ExtensionFieldNotFoundException

指定したフィールドがこのインスタンスのメタクラスで見つからない場合

フィールドの型と一致しない

ExtensionInvalidTypeException

指定したフィールドのデータ型がリッチテキスト型でない場合

不正操作

ExtensionInvalidOperationException

自身が削除済みモデル、一時プロキシの場合  
フィールド名に操作不可のフィールドを指定した場合  
\- プロダクトラインのフィーチャ割り当てフィールド  
\- System.Coreタグが付与されているフィールド  
無効なフィールドが指定された場合