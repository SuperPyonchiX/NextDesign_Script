IScmManager.CreateScmRepositorySetting メソッド
===========================================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定された接続情報で新しい構成管理接続設定を生成します。

引数​
-----------------------

名前

型

説明

type

string

リポジトリの種類  
\- "Subversion"

name

string

リポジトリ名

account

string

接続アカウント名

password

string

接続パスワード

url

string

接続先URL

isManaged

bool

管理対象として登録するか

戻り値​
--------------------------

*   [IScmRepositorySetting](_extension_api_NextDesign.Desktop_IScmRepositorySetting.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

パラメータが不正な場合  
\- 値域外のリポジトリの種類が指定された

不正操作

ExtensionInvalidOperationException

既に同じ接続設定が登録済みの場合