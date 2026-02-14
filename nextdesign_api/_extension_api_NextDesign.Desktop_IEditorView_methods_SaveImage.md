IEditorView.SaveImage メソッド
==========================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定したパスにエディタで表示するビューのビットマップイメージを保存します。  
イメージの保存形式はPNG, JPEG, BMP, GIF, XPS形式です。  
保存形式は指定された保存先のファイルパスの拡張子から判断し、拡張子から判断できない場合はPNG形式とします。

引数​
-----------------------

名前

型

説明

filePath

string

保存先のファイルパス  
null、または空文字列は指定できません。

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

filePath に null、もしくは空文字を指定した場合

無効なパス

ExtensionInvalidPathException

filePath に親ディレクトリが存在しないファイルパスを指定した場合

不正操作

ExtensionInvalidOperationException

現在のエディタからイメージが取得できなかった場合