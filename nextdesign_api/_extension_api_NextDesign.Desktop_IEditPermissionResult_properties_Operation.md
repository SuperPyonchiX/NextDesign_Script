IEditPermissionResult.Operation プロパティ
=====================================

名前空間: NextDesign.Desktop

説明​
-----------------------

要求内容

    string Operation { get; }

型​
--------------------

*   string

注釈​
-----------------------

取得できる値域は以下になります。

*   "GetAllEditPermissions"：指定されたプロジェクトの全てのユニットの編集権限を取得
*   "GetEditPermission"：指定されたプロジェクトで指定されたユニットの編集権限を取得
*   "GetEditPermissions"：指定されたプロジェクトで指定された全てのユニットの編集権限を取得
*   "ReleaseAllEditPermissions"：指定されたプロジェクトの全てのユニットの編集権限を解放
*   "ReleaseEditPermission"：指定されたプロジェクトで指定されたユニットの編集権限を解放
*   "ReleaseEditPermissions"：指定されたプロジェクトで指定された全てのユニットの編集権限を解放