IEditPermissionResult.FailUnits プロパティ
=====================================

名前空間: NextDesign.Desktop

説明​
-----------------------

編集権限操作に失敗したユニット一覧  
単一ユニットに対する操作APIの呼び出しであっても、結果情報はコレクションで返します。

編集権限の取得時に他のユーザにより既に編集権限が取得されているユニットは、この一覧によって確認することができます。

    IModelUnitCollection FailUnits { get; }

型​
--------------------

*   IModelUnitCollection