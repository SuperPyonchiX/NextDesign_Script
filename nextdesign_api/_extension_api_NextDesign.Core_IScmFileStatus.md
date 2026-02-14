IScmFileStatus インタフェース
======================

名前空間: NextDesign.Core

説明​
-----------------------

物理ファイルの構成管理状態へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[チーム開発](_extension_api_overview_scm.md)

チーム開発向けに構成管理システムとの連携操作を提供するAPI群です。

プロパティ​
--------------------------------

名前

説明

[HasLock](_extension_api_NextDesign.Core_IScmFileStatus_properties_HasLock.md)

物理ファイルで構成管理システムの排他ロックを取得しているか調べます。  
排他ロックを取得している場合、他のユーザはコミットできません。

[LockAccount](_extension_api_NextDesign.Core_IScmFileStatus_properties_LockAccount.md)

物理ファイルの構成管理システムの排他ロックを取得しているユーザ（アカウント）名

[ScmState](_extension_api_NextDesign.Core_IScmFileStatus_properties_ScmState.md)

物理ファイルの構成管理システム上のファイル状態  
\- "None":変更なし  
\- "Add":追加  
\- "Update":更新  
\- "Delete":削除  
\- "Lost":紛失  
\- "Conflict":競合  
\- "Unmanage":管理外  
\- "Ignore":管理外で無視  
\- "Unknown":不明