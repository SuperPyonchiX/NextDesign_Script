IWorkspace.SetLoadMode メソッド
===========================

名前空間: NextDesign.Desktop

説明​
-----------------------

モデルユニットのロードモードを設定します。  
指定されたユニットが指定したプロジェクトの管理対象外の場合は無視されます。

引数​
-----------------------

名前

型

説明

project

IProject

プロジェクト

units

IEnumerable<IModelUnit>

モデルユニット

loadMode

string

ロードモード  
\- "Auto" : 自動ロード  
\- "Manual" : 手動ロード

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

project に null を指定した場合  
units に null を指定した場合  
loadMode に既定の文字列以外の文字列を指定した場合

サポート外

ExtensionNotSupportedException

アプリケーションの現在のエディションが対応していない場合