ModelAfterNewRelationEventParams インタフェース
========================================

名前空間: NextDesign.Desktop

説明​
-----------------------

関連追加後イベントのパラメータです。

所属イベントエリア​
--------------------------------------------

名前

説明

[モデル](_extension_api_overview_events_models.md)

モデルの作成・更新・移動・削除、および選択を通知します。

継承元​
--------------------------

名前

説明

[CancelableEventParams](_extension_api_NextDesign.Desktop_CancelableEventParams.md)

キャンセル可能なイベントパラメータの基底クラスです。

[IModelEventParams](_extension_api_NextDesign.Desktop_IModelEventParams.md)

モデルイベントの共通パラメータです。

プロパティ​
--------------------------------

名前

説明

[Field](_extension_api_NextDesign.Desktop_ModelAfterNewRelationEventParams_properties_Field.md)

対象フィールド

[Index](_extension_api_NextDesign.Desktop_ModelAfterNewRelationEventParams_properties.md)

インデックス（コレクションでない場合は0）

[Model](_extension_api_NextDesign.Desktop_ModelAfterNewRelationEventParams_properties_Model.md)

対象モデル

[OppositeField](_extension_api_NextDesign.Desktop_ModelAfterNewRelationEventParams_properties_OppositeField.md)

新しい関連先側のフィールド

[OppositeIndex](_extension_api_NextDesign.Desktop_ModelAfterNewRelationEventParams_properties_OppositeIndex.md)

新しい関連先側のインデックス（コレクションでない場合は0）

[RelatingTo](_extension_api_NextDesign.Desktop_ModelAfterNewRelationEventParams_properties_RelatingTo.md)

新しい関連先

注釈​
-----------------------

**Ver.1.1 での API 仕様変更と移行方法について**

Ver.1.1 より、Model プロパティと RelatingTo プロパティで取得できるモデルが次のように変更され、UI上でのユーザー操作に関わらず、関連の方向に従ったモデルが取得できるようになりました。

プロパティ

変更前

変更後

Model

UI上の操作始点に対応するモデル

関連クラスのSource側クラスに対応するモデル

RelatingTo

UI上の操作終点に対応するモデル

関連クラスのTarget側クラスに対応するモデル

これらの API を利用している場合は、Ver.1.1 以降へのバージョンアップと合わせてエクステンション内の該当箇所を変更してください。