IEditPermissionResult.EditableUnits プロパティ
=========================================

名前空間: NextDesign.Desktop

説明​
-----------------------

編集権限が取得できているユニット一覧  
プロジェクト管理下の要求されたユニット以外のユニット、および削除（統合）されたユニットも含まれます。  
また、新規追加（分割）され、リポジトリにコミットされていないユニットも含まれます。

    IModelUnitCollection EditableUnits { get; }

型​
--------------------

*   IModelUnitCollection