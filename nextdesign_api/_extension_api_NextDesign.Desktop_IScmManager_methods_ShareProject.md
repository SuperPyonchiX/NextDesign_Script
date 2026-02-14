IScmManager.ShareProject メソッド
=============================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定されたプロジェクトを指定された構成管理リポジトリで共有します。

引数​
-----------------------

名前

型

説明

project

IProject

共有するプロジェクト

setting

IScmRepositorySetting

構成管理接続設定

comment

string

初回コミットコメント

remotePath

string

リモートパス  
null を指定した場合は、構成管理接続設定のBaseUrlの位置に追加されます。  
既定値は null です。

silent

bool

trueを指定した場合、進捗状況をプログレスバーで表示しません。  
既定値は false です。

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

操作不正

ExtensionInvalidOperationException

指定されたプロジェクトが保存されていない場合  
指定されたプロジェクトが構成管理システムと既に連携している場合

構成管理リポジトリ操作に失敗

ExtensionScmException

構成管理リポジトリ操作に失敗した場合