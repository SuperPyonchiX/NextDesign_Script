models イベントエリア
==============

説明​
-----------------------

モデルの作成・更新・移動・削除、および選択を通知します。

イベント​
-----------------------------

名前

定義名

説明

[モデル追加前イベント](_extension_api_overview_events_models_onBeforeNew.md)

onBeforeNew

モデルの追加前に通知されるイベントです。

[モデル追加後イベント](_extension_api_overview_events_models_onAfterNew.md)

onAfterNew

モデルの追加後に通知されるイベントです。

[フィールド値変更後イベント](_extension_api_overview_events_models_onFieldChanged.md)

onFieldChanged

フィールド値の変更後に通知されるイベントです。

[モデル削除前イベント](_extension_api_overview_events_models_onBeforeDelete.md)

onBeforeDelete

モデルの削除前に通知されるイベントです。

[モデル親変更前イベント](_extension_api_overview_events_models_onBeforeChangeOwner.md)

onBeforeChangeOwner

モデルの親を変更前に通知されるイベントです。

[モデル親変更後イベント](_extension_api_overview_events_models_onAfterChangeOwner.md)

onAfterChangeOwner

モデルの親を変更後に通知されるイベントです。

[モデル順序変更前イベント](_extension_api_overview_events_models_onBeforeChangeOrder.md)

onBeforeChangeOrder

モデルの順序変更前に通知されるイベントです。

[モデル順序変更後イベント](_extension_api_overview_events_models_onAfterChangeOrder.md)

onAfterChangeOrder

モデルの順序変更後に通知されるイベントです。

[関連追加前イベント](_extension_api_overview_events_models_onBeforeNewRelation.md)

onBeforeNewRelation

関連の追加前に通知されるイベントです。

[関連追加後イベント](_extension_api_overview_events_models_onAfterNewRelation.md)

onAfterNewRelation

関連の追加後に通知されるイベントです。

[モデル検証時イベント](_extension_api_overview_events_models_onValidate.md)

onValidate

モデルの検証時に通知されるイベントです。

[エラー追加時イベント](_extension_api_overview_events_models_onError.md)

onError

モデルへのエラー追加時に通知されるイベントです。

[モデル選択イベント](_extension_api_overview_events_models_onSelectionChanged.md)

onSelectionChanged

モデルを選択時に通知されるイベントです。

[モデル編集イベント](_extension_api_overview_events_models_onModelEdited.md)

onModelEdited

モデルを編集時に通知されるイベントです。

[アンドゥ・リドゥイベント](_extension_api_overview_events_models_onUndoRedo.md)

onUndoRedo

アンドゥ・リドゥを実行時に通知されるイベントです。

エリアに属するインタフェース​
-----------------------------------------------------------

名前

説明

[ModelBeforeNewEventParams](_extension_api_NextDesign.Desktop_ModelBeforeNewEventParams.md)

モデル追加前イベントのパラメータです。

[ModelAfterNewEventParams](_extension_api_NextDesign.Desktop_ModelAfterNewEventParams.md)

モデル追加後イベントのパラメータです。

[ModelFieldChangedEventParams](_extension_api_NextDesign.Desktop_ModelFieldChangedEventParams.md)

フィールド値変更後イベントのパラメータです。

[ModelBeforeChangeOwnerEventParams](_extension_api_NextDesign.Desktop_ModelBeforeChangeOwnerEventParams.md)

モデルの親変更前イベントのパラメータです。

[ModelAfterChangeOwnerEventParams](_extension_api_NextDesign.Desktop_ModelAfterChangeOwnerEventParams.md)

モデルの親変更後イベントのパラメータです。

[ModelBeforeChangeOrderEventParams](_extension_api_NextDesign.Desktop_ModelBeforeChangeOrderEventParams.md)

モデル順序変更前イベントのパラメータです。

[ModelAfterChangeOrderEventParams](_extension_api_NextDesign.Desktop_ModelAfterChangeOrderEventParams.md)

モデル順序変更後イベントのパラメータです。

[ModelBeforeDeleteEventParams](_extension_api_NextDesign.Desktop_ModelBeforeDeleteEventParams.md)

モデル削除前イベントのパラメータです。

[ModelBeforeNewRelationEventParams](_extension_api_NextDesign.Desktop_ModelBeforeNewRelationEventParams.md)

関連追加前イベントのパラメータです。

[ModelAfterNewRelationEventParams](_extension_api_NextDesign.Desktop_ModelAfterNewRelationEventParams.md)

関連追加後イベントのパラメータです。

[ModelSelectionChangedEventParams](_extension_api_NextDesign.Desktop_ModelSelectionChangedEventParams.md)

モデル選択変更後イベントのパラメータです。

[ModelOnValidateEventParams](_extension_api_NextDesign.Desktop_ModelOnValidateEventParams.md)

モデル評価イベントのパラメータです。

[ModelOnErrorEventParams](_extension_api_NextDesign.Desktop_ModelOnErrorEventParams.md)

モデルのエラー追加時イベントのパラメータです。

[ModelEditedEventParams](_extension_api_NextDesign.Desktop_ModelEditedEventParams.md)

モデル変更イベントのパラメータです。  
一度の編集操作によって何らかの変更が発生したすべてのモデル情報が通知されます。

[ModelUndoRedoEventParams](_extension_api_NextDesign.Desktop_ModelUndoRedoEventParams.md)

アンドゥ・リドゥ イベントのパラメータです。  
アンドゥ・リドゥ操作によって何らかの変更が発生したすべてのモデル情報が通知されます。

[IModelDictionary](_extension_api_NextDesign.Desktop_IModelDictionary.md)

モデルの辞書・目録です。

[IModelEditedEventParams](_extension_api_NextDesign.Desktop_IModelEditedEventParams.md)

モデル変更イベントの共通パラメータです。

[IModelEventParams](_extension_api_NextDesign.Desktop_IModelEventParams.md)

モデルイベントの共通パラメータです。