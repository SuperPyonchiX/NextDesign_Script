IWorkspace.ReloadProject メソッド
=============================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定されたプロジェクトを再読み込みします。  
プロジェクト未指定の場合は、現在アプリケーションで開いているカレントのプロジェクトを再読み込みします。

引数​
-----------------------

名前

型

説明

project

IProject

プロジェクトです。  
  
null が指定された場合は、現在アプリケーション開いているカレントのプロジェクトをリロードします。

isSetReadOnly

bool

読み取り専用でプロジェクトを再読み込みするかを指定します。  
  
True が指定された場合、開いたプロジェクトは編集できません。  
既定値は False です。

戻り値​
--------------------------

*   [IProject](_extension_api_NextDesign.Core_IProject.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

カレントプロジェクトが未設定の際、 project に null を指定した場合

引数不正

ExtensionArgumentException

リロード対象のプロジェクトが新規作成されたプロジェクトの場合

ファイルが見つからない

ExtensionFileFoundException

project に対応するファイルが見つからない場合