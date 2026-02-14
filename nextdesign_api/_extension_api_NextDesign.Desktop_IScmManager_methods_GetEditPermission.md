IScmManager.GetEditPermission メソッド
==================================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定されたプロジェクトで指定されたユニットの編集権限を取得します。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。

指定したユニットが既に編集権限を保持している場合は、権限取得は行わず成功したものとして扱います。  
ユニットが権限取得できたか否かは、戻り値の権限取得結果オブジェクトを確認することで識別することができます。

引数​
-----------------------

名前

型

説明

project

IProject

プロジェクト

unit

IModelUnit

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