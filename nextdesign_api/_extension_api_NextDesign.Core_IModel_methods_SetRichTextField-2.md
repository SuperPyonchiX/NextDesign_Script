IModel.SetRichTextField(string,string,string) メソッド
==================================================

名前空間: NextDesign.Core

説明​
-----------------------

指定したリッチテキストフィールドのフォーマットに値を設定します。  
"html"フォーマットを指定した場合は、"text"フォーマットの値も自動生成して同時に設定します。  
その際は処理時間に若干のオーバーヘッドが生じます。速度性能が求められる場合は、本APIの代わりにSetRichTextFieldValues()を利用ください。

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

フィールドの値

format

string

フォーマット  
・"text","xaml","html"を指定します。（大文字小文字は区別しません。）  
・V4.0以降のバージョンでは、"xaml"を指定した場合、何も設定されません。

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
format に null、または空文字列 を指定した場合  
指定したフォーマットが存在しない場合  
フォーマットに"html"を指定した場合で、且つ、valueに指定したHTMLにbodyタグを含まない場合（但し、HTMLがnullの場合や空文字の場合は許容します。）

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