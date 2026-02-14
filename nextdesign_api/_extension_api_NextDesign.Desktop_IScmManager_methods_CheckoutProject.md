IScmManager.CheckoutProject メソッド
================================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定されたプロジェクトパス（リモートリポジトリのパス）のプロジェクトを指定された作業領域にチェックアウトします。

引数​
-----------------------

名前

型

説明

projectPath

string

プロジェクトパス（リモートリポジトリのパス）

workDir

string

ローカルの作業領域（チェックアウト先）フォルダパス

setting

IScmRepositorySetting

構成管理接続設定

autoLoad

bool

チェックアウト後に自動的にプロジェクトを読み込み、カレントプロジェクトとして設定するか  
\- trueが指定されている場合、現在のカレントプロジェクトは編集状態を破棄して強制的に閉じられます（既定の動作）。  
\- falseが指定されている場合は、プロジェクトのチェックアウトのみが行われ、プロジェクトは読み込まれません。

silent

bool

trueを指定した場合、進捗状況をプログレスバーで表示しません。  
既定値は false です。

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

チェックアウト先のフォルダが存在しない場合  
不正なパラメータが指定された場合  
\- autoLoad に true を指定して、projectPath に実行中のNextDesignでサポートしない保存形式のプロジェクトパスを指定

構成管理リポジトリ操作に失敗

ExtensionScmException

チェックアウト先のフォルダが構成管理連携済みのプロジェクトフォルダの場合  
構成管理リポジトリ操作に失敗した場合