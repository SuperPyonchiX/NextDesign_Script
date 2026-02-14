IWorkspace.NewProject メソッド
==========================

名前空間: NextDesign.Desktop

説明​
-----------------------

新規プロジェクトを生成します。

引数​
-----------------------

名前

型

説明

projectName

string

プロジェクト名  
  
null、または空文字列は指定できません。

description

string

説明  
  
nullを含む任意の文字列を指定できます。

profilePath

string

プロファイルパス  
  
null が指定された場合、空のプロファイルのプロジェクトを生成します。

isSetCurrent

bool

生成したプロジェクトをカレントプロジェクトに設定するか  
Falseが指定された場合、生成したプロジェクトはカレントプロジェクトとして設定されません。  
既定値はTrueです。

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

projectName に null または空文字列を指定した場

無効なパス

ExtensionInvalidPathException

profilePath が null でないときに、指定されたパスが有効なパス文字列として解釈できない場合

不正ファイル指定

ExtensionInvalidFileException

profilePath が null でないときに、該当パスのファイル拡張子がプロファイルでない場合合  
profilePath に実行中の NextDesign でサポートしない保存形式のプロファイルパスを指定した場合

ファイルが見つからない

ExtensionFileFoundException

profilePath が null でないときに、該当パスのファイルが見つからない場合