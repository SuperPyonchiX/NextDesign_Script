IWorkspace.GenerateDocument メソッド
================================

名前空間: NextDesign.Desktop

説明​
-----------------------

ドキュメントを生成します。  
モデルナビゲータの選択要素を出力対象の基点モデルとします。

引数​
-----------------------

名前

型

説明

options

DocumentGenerationOptions

ドキュメント生成オプション

戻り値​
--------------------------

*   [DocumentGenerationResult](_extension_api_NextDesign.Desktop_DocumentGenerationResult.md)

例外​
-----------------------

名前

例外クラス

説明

サポート外

ExtensionNotSupportedException

アプリケーションの現在のエディションが対応していない場合

引数不正

ExtensionArgumentException

options.Format に既定の文字列以外の文字列を指定した場合  
options.Scope に既定の文字列以外の文字列を指定した場合  
options.OutputPath に null または空文字列を指定した場合

無効なパス

ExtensionInvalidPathException

options.OutputPath が有効なパス文字列として解釈できない場合