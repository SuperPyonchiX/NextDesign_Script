ITracePage インタフェース
==================

名前空間: NextDesign.Desktop

説明​
-----------------------

トレースUIへのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[トレース](_extension_api_overview_traceability.md)

トーレス情報にアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IUIElement](_extension_api_NextDesign.Desktop_IUIElement.md)

UI要素へのアクセス手段を提供します。

プロパティ​
--------------------------------

名前

説明

[CurrentView](_extension_api_NextDesign.Desktop_ITracePage_properties_CurrentView.md)

現在表示しているトレースビュー

[CurrentViewType](_extension_api_NextDesign.Desktop_ITracePage_properties_CurrentViewType.md)

現在表示しているビューの種別

[ExcludedModels](_extension_api_NextDesign.Desktop_ITracePage_properties_ExcludedModels.md)

トレース除外対象のモデル一覧

[SelectedNodes](_extension_api_NextDesign.Desktop_ITracePage_properties_SelectedNodes.md)

現在のトレースビューで選択されているノード  
選択要素のコレクションの順序は不定です。

[TraceViews](_extension_api_NextDesign.Desktop_ITracePage_properties_TraceViews.md)

トレースビュー一覧

メソッド​
-----------------------------

名前

説明

[AddExcludedModel](_extension_api_NextDesign.Desktop_ITracePage_methods_AddExcludedModel.md)

新しいトレース除外対象のモデル情報を追加します。  
このメソッドでは、指定された識別子のモデルの存在を確認しません。

[AddTraceView](_extension_api_NextDesign.Desktop_ITracePage_methods_AddTraceView.md)

指定されたモデルを基点とするレーンをもつ新しいトレースビューを作成して、現在のトレースビューに設定します。

[ClearAllSelection](_extension_api_NextDesign.Desktop_ITracePage_methods_ClearAllSelection.md)

全てのレーンで選択された全てのノードの選択を解除します。  
ビューの種別が Matrix の場合は、セルの選択も解除されます。

[ClearSelection](_extension_api_NextDesign.Desktop_ITracePage_methods_ClearSelection.md)

指定されたレーンで選択された全てのノードの選択を解除します。  
現在表示しているトレースビューに、指定されたレーンが存在しない場合は何も行われません。

[DeleteExcludedModel](_extension_api_NextDesign.Desktop_ITracePage_methods_DeleteExcludedModel.md)

指定されたトレース除外対象のモデル情報を削除します。

[SelectCell](_extension_api_NextDesign.Desktop_ITracePage_methods_SelectCell.md)

表示しているビューの種別が Matrix の場合、指定されたセルを選択します。  
行、列で指定したノードが、それぞれのレーンに存在しない場合は何も行われません。  
また、レーン上のノードは選択されません。  
  
なお、表示しているビューの種類が Tree の場合は何も行いません。  

[SelectNode](_extension_api_NextDesign.Desktop_ITracePage_methods_SelectNode.md)

指定されたノードを選択します。  
現在表示しているトレースビューに、指定されたノードが所属するレーンが存在しない場合は何も行われません。  
  
なお、現在表示しているトレースビューにすでに選択済みのノードがある場合は次のように動作します。  
\- 指定したノードと異なるレーンで選択されていたノードの選択がすべて解除されます  
\- 指定したノードと同じレーンで選択されていたノードの選択は維持されます  

[SelectNodeByModel](_extension_api_NextDesign.Desktop_ITracePage_methods_SelectNodeByModel.md)

指定されたレーンの指定された全てのモデルに対応するノードを選択します。  
ビューの種別が Matrix の場合は、行または列に対応するレーンを指定することで、行または列のモデルに対応するノードを選択することができます。  
現在表示しているトレースビューに、指定されたレーンが存在しない場合、または指定されたレーンに指定されたモデルに対応するノードが存在しない場合は何も行われません。  
  
なお、選択するノードが決定できた場合、現在表示しているトレースビューにすでに選択済みのノードがある場合は次のように動作します。  
\- 指定したノードと異なるレーンで選択されていたノードの選択がすべて解除されます  
\- 指定したノードと同じレーンで選択されていたノードの選択は維持されます  

[SelectNodeByModels](_extension_api_NextDesign.Desktop_ITracePage_methods_SelectNodeByModels.md)

指定されたレーンの指定されたモデルに対応するノードを選択します。  
ビューの種別が Matrix の場合は、行または列に対応するレーンを指定することで、行または列のモデルに対応するノードを選択することができます。  
現在表示しているトレースビューに、指定されたレーンが存在しない場合は何も行われません。  
また、指定されたレーンに指定されたモデルに対応するノードが存在しない場合は、そのモデルの指定は無視されます。この結果選択できるノードが1件もない場合は、何も行われません。  
  
なお、選択するノードが決定できた場合、現在表示しているトレースビューにすでに選択済みのノードがある場合は次のように動作します。  
\- 指定したノードと異なるレーンで選択されていたノードの選択がすべて解除されます  
\- 指定したノードと同じレーンで選択されていたノードの選択は維持されます  

[Update](_extension_api_NextDesign.Desktop_ITracePage_methods_Update.md)

現在表示しているトレースビューを最新の状態に更新します。  
トレース情報はモデルのトレース関係の変更に対してリアルタイムで同期しません。モデルのトレース関係に変更があった場合は、このメソッドにより最新の状態に更新することができます。