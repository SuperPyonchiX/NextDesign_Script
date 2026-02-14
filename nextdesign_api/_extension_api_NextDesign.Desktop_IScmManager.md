IScmManager インタフェース
===================

名前空間: NextDesign.Desktop

説明​
-----------------------

構成管理へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[チーム開発](_extension_api_overview_scm.md)

チーム開発向けに構成管理システムとの連携操作を提供するAPI群です。

メソッド​
-----------------------------

名前

説明

[CheckoutProject](_extension_api_NextDesign.Desktop_IScmManager_methods_CheckoutProject.md)

指定されたプロジェクトパス（リモートリポジトリのパス）のプロジェクトを指定された作業領域にチェックアウトします。

[CommitProject](_extension_api_NextDesign.Desktop_IScmManager_methods_CommitProject.md)

指定されたプロジェクトの変更を確定し、構成管理リポジトリにコミットします。  
指定されたプロジェクトに変更がない場合は何も行われません。  
また、指定されたプロジェクトが構成管理システムと未連携の場合も何も行われません。

[CommitUnits](_extension_api_NextDesign.Desktop_IScmManager_methods_CommitUnits.md)

指定されたプロジェクトの指定されたユニットの変更を確定し、構成管理リポジトリにコミットします。  
指定されたユニットに変更がない場合は何も行われません。  
また、指定されたプロジェクトが構成管理システムと未連携の場合も何も行われません。

[CreateScmRepositorySetting](_extension_api_NextDesign.Desktop_IScmManager_methods_CreateScmRepositorySetting.md)

指定された接続情報で新しい構成管理接続設定を生成します。

[GetAllEditPermissions](_extension_api_NextDesign.Desktop_IScmManager_methods_GetAllEditPermissions.md)

指定されたプロジェクトの全てのユニットの編集権限を取得します。  
プロファイルをユニット化している場合は、プロファイルのユニットも対象となります。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。  
  
対象ユニットのうち権限取得できたユニット、および権限取得できなかったユニットは戻り値の権限取得結果オブジェクトを確認することで識別することができます。

[GetChangedUnits](_extension_api_NextDesign.Desktop_IScmManager_methods_GetChangedUnits.md)

指定されたプロジェクトにおいて変更のあったユニットを取得します。  
指定されたプロジェクトが構成管理システムと未連携の場合は空のコレクションを返します。  
  
ユニット変更はプロジェクトを保存した際に確定します。  
プロジェクトが保存されていない場合は、前回の保存時の状態から変更のあったユニットを特定します。  
ユニットに対する変更内容は、IModelUnit.ScmStatus.ScmState プロパティを確認することで調べることができます。

[GetEditPermission](_extension_api_NextDesign.Desktop_IScmManager_methods_GetEditPermission.md)

指定されたプロジェクトで指定されたユニットの編集権限を取得します。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。  
  
指定したユニットが既に編集権限を保持している場合は、権限取得は行わず成功したものとして扱います。  
ユニットが権限取得できたか否かは、戻り値の権限取得結果オブジェクトを確認することで識別することができます。

[GetEditPermissions](_extension_api_NextDesign.Desktop_IScmManager_methods_GetEditPermissions.md)

指定されたプロジェクトで指定された全てのユニットの編集権限を取得します。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。  
  
指定したユニットのうち既に編集権限を保持しているユニットは、権限取得は行わず成功したものとして扱います。  
対象ユニットのうち権限取得できたユニット、および権限取得できなかったユニットは戻り値の権限取得結果オブジェクトを確認することで識別することができます。

[GetRemotePath](_extension_api_NextDesign.Desktop_IScmManager_methods_GetRemotePath.md)

指定されたプロジェクトで指定されたユニットのリモートパス（リポジトリのパス）を取得します。

[GetRepositorySetting(IProject)](_extension_api_NextDesign.Desktop_IScmManager_methods_GetRepositorySetting-1.md)

指定されたプロジェクトに対応する構成管理接続設定を取得します。

[GetRepositorySetting(string)](_extension_api_NextDesign.Desktop_IScmManager_methods_GetRepositorySetting-2.md)

指定された名前の構成管理接続設定を取得します。

[GetRepositorySettings](_extension_api_NextDesign.Desktop_IScmManager_methods_GetRepositorySettings.md)

定義済みのすべての構成管理接続設定を取得します。

[IsScmFolder](_extension_api_NextDesign.Desktop_IScmManager_methods_IsScmFolder.md)

指定されたパスが構成管理システムの作業フォルダであるか調べます。  
構成管理システムの作業フォルダの場合はtrueを返します。

[IsScmItem](_extension_api_NextDesign.Desktop_IScmManager_methods_IsScmItem.md)

指定されたプロジェクトが構成管理システムと連携済みであるか調べます。  
連携済みの場合はtrueを返します。

[ReleaseAllEditPermissions](_extension_api_NextDesign.Desktop_IScmManager_methods_ReleaseAllEditPermissions.md)

指定されたプロジェクトの全てのユニットの編集権限を解放します。  
プロファイルをユニット化している場合は、プロファイルのユニットも対象となります。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。  
  
対象ユニットのうち権限解放できたユニット、および権限解放できなかったユニットは戻り値の権限取得結果オブジェクトを確認することで識別することができます。

[ReleaseEditPermission](_extension_api_NextDesign.Desktop_IScmManager_methods_ReleaseEditPermission.md)

指定されたプロジェクトで指定されたユニットの編集権限を解放します。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。  
  
指定したユニットが編集権限を保持していない場合は、権限解放は行わず成功したものとして扱います。  
ユニットが権限解放できたか否かは、戻り値の権限取得結果オブジェクトを確認することで識別することができます。

[ReleaseEditPermissions](_extension_api_NextDesign.Desktop_IScmManager_methods_ReleaseEditPermissions.md)

指定されたプロジェクトで指定されたユニットの編集権限を解放します。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。  
  
指定したユニットのうち編集権限を保持していないユニットは、権限解放は行わず成功したものとして扱います。  
対象ユニットのうち権限解放できたユニット、および権限解放できなかったユニットは戻り値の権限取得結果オブジェクトを確認することで識別することができます。

[RevertProject](_extension_api_NextDesign.Desktop_IScmManager_methods_RevertProject.md)

指定されたプロジェクトの全てのユニットの変更を破棄します。  
指定されたプロジェクトに変更がない場合は何も行われません。  
また、指定されたプロジェクトが構成管理システムと未連携の場合も何も行われません。

[RevertUnits](_extension_api_NextDesign.Desktop_IScmManager_methods_RevertUnits.md)

指定されたプロジェクトで指定されたユニットの変更を破棄します。  
指定されたユニットに変更がない場合は何も行われません。  
また、指定されたプロジェクトが構成管理システムと未連携の場合も何も行われません。

[ShareProject](_extension_api_NextDesign.Desktop_IScmManager_methods_ShareProject.md)

指定されたプロジェクトを指定された構成管理リポジトリで共有します。

[UpdateProject](_extension_api_NextDesign.Desktop_IScmManager_methods_UpdateProject.md)

指定されたプロジェクトを構成管理リポジトリの最新の状態に更新します。  
指定されたプロジェクトが構成管理システムと未連携の場合は何も行われません。