IWorkspace.OpenProject(string,bool,bool) メソッド
=============================================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定されたプロジェクトを開きます。

引数​
-----------------------

名前

型

説明

projectPath

string

プロジェクトパス  
  
null、または空文字列は指定できません。

isSetCurrent

bool

ロードしたプロジェクトをカレントプロジェクトに設定するか  
Falseが指定された場合、ロードしたプロジェクトはカレントプロジェクトとして設定されません。  
既定値はTrueです。

excludeModelFiles

bool

モデルファイルを読み込まずにプロジェクトを開くか  
Trueが指定された場合、モデルファイルを読み込まずにプロジェクトを開きます。  
既定値はFalseです。

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

projectPath に null または空文字列を指定した場合

無効なパス

ExtensionInvalidPathException

指定されたパスが有効なパス文字列として解釈できない場合

不正ファイル指定

ExtensionInvalidFileException

projectPath のファイル拡張子がプロジェクトでない場合  
projectPath に実行中の NextDesign でサポートしない保存形式のプロジェクトパスを指定した場合

ファイルが見つからない

ExtensionFileFoundException

projectPath のファイルが見つからない場合