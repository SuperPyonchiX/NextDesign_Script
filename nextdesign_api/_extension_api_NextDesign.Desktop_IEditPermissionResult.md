IEditPermissionResult インタフェース
=============================

名前空間: NextDesign.Desktop

説明​
-----------------------

編集権限の取得/解放を要求した際の結果情報オブジェクトです。

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

[EditableUnits](_extension_api_NextDesign.Desktop_IEditPermissionResult_properties_EditableUnits.md)

編集権限が取得できているユニット一覧  
プロジェクト管理下の要求されたユニット以外のユニット、および削除（統合）されたユニットも含まれます。  
また、新規追加（分割）され、リポジトリにコミットされていないユニットも含まれます。

[FailUnits](_extension_api_NextDesign.Desktop_IEditPermissionResult_properties_FailUnits.md)

編集権限操作に失敗したユニット一覧  
単一ユニットに対する操作APIの呼び出しであっても、結果情報はコレクションで返します。  
  
編集権限の取得時に他のユーザにより既に編集権限が取得されているユニットは、この一覧によって確認することができます。

[Operation](_extension_api_NextDesign.Desktop_IEditPermissionResult_properties_Operation.md)

要求内容

[SuccessUnits](_extension_api_NextDesign.Desktop_IEditPermissionResult_properties_SuccessUnits.md)

編集権限操作に成功したユニット一覧  
単一ユニットに対する操作APIの呼び出しであっても、結果情報はコレクションで返します。

[UneditableUnits](_extension_api_NextDesign.Desktop_IEditPermissionResult_properties_UneditableUnits.md)

編集権限が取得できていないユニット一覧  
プロジェクト管理下の要求されたユニット以外のユニットも含まれます。