IScmManager.GetEditPermissions メソッド
===================================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定されたプロジェクトで指定された全てのユニットの編集権限を取得します。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。

指定したユニットのうち既に編集権限を保持しているユニットは、権限取得は行わず成功したものとして扱います。  
対象ユニットのうち権限取得できたユニット、および権限取得できなかったユニットは戻り値の権限取得結果オブジェクトを確認することで識別することができます。

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

権限を取得するユニット

戻り値​
--------------------------

*   [IEditPermissionResult](_extension_api_NextDesign.Desktop_IEditPermissionResult.md)

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

取得対象のユニットにプロジェクト管理外のユニットが指定された場合

構成管理リポジトリ操作に失敗

ExtensionScmException

構成管理リポジトリ操作に失敗した場合