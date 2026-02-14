IWorkspaceState インタフェース
=======================

名前空間: NextDesign.Desktop

説明​
-----------------------

ワークスペースの状態管理オブジェクトです。

所属エリア​
--------------------------------

名前

説明

[ワークスペース・プロジェクト](_extension_api_overview_workspace.md)

アプリケーションの作業領域やアプリケーションで開いているプロジェクトにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[ActiveEditorSelectedModel](_extension_api_NextDesign.Desktop_IWorkspaceState_properties_ActiveEditorSelectedModel.md)

アクティブなエディタの選択要素

[ActiveEditorSelectedModels](_extension_api_NextDesign.Desktop_IWorkspaceState_properties_ActiveEditorSelectedModels.md)

アクティブなエディタの選択要素（複数）

[CurrentModel](_extension_api_NextDesign.Desktop_IWorkspaceState_properties_CurrentModel.md)

現在のワークスペースのカレントモデル

[DisplayMode](_extension_api_NextDesign.Desktop_IWorkspaceState_properties_DisplayMode.md)

表示モード

[InspectedObject](_extension_api_NextDesign.Desktop_IWorkspaceState_properties_InspectedObject.md)

インスペクト対象の要素  
現在インスペクタの表示対象となっている要素です。  
表示対象の要素がない(つまり、インスペクタタブが表示されていない)場合、nullとなります。

[InspectedObjects](_extension_api_NextDesign.Desktop_IWorkspaceState_properties_InspectedObjects.md)

インスペクト対象の要素（複数）  
現在インスペクタの表示対象となっている要素（複数）です。  
インスペクタ表示対象として複数の要素がない場合、空のコレクションとなります。  
IWorkspaceState.InspectedObject {get;} で取得できる要素が必ずしも含まれるとは限りません。

メソッド​
-----------------------------

名前

説明

[SetActiveEditorSelectedModel](_extension_api_NextDesign.Desktop_IWorkspaceState_methods_SetActiveEditorSelectedModel.md)

アクティブなエディタの選択要素を設定します。  
このAPIは保持している状態を変更するのみで、画面上の選択状態は変わりません。  
IWorkspaceState.ActiveEditorSelectedModel {get;} で選択要素を取得すると、このAPIで変更した値を取得できます。  
メインエディタを対象に実行すると、サブエディタの表示モードが"詳細"モードの場合、サブエディタの表示対象モデルが切り替わります。  
編集しているプロジェクト以外のモデルを指定した場合は、何も行われず正常終了します。

[SetActiveEditorSelectedModels](_extension_api_NextDesign.Desktop_IWorkspaceState_methods_SetActiveEditorSelectedModels.md)

アクティブなエディタの選択要素（複数）を設定します。  
このAPIは保持している状態を変更するのみで、画面上の選択状態は変わりません。  
IWorkspaceState.ActiveEditorSelectedModels {get;} で選択要素を取得すると、このAPIで変更した値を取得できます。  
IWorkspaceState.ActiveEditorSelectedModel {get;} で取得できる選択要素が必ずしも含まれるとは限りません。  
編集しているプロジェクト以外のモデルを指定した場合は、選択要素から除外します。

[SetCurrentModel](_extension_api_NextDesign.Desktop_IWorkspaceState_methods_SetCurrentModel.md)

現在のワークスペースのカレントモデルを設定します。

[SetInspectedObject](_extension_api_NextDesign.Desktop_IWorkspaceState_methods_SetInspectedObject.md)

インスペクト対象の要素を設定します。

[SetInspectedObjects](_extension_api_NextDesign.Desktop_IWorkspaceState_methods_SetInspectedObjects.md)

インスペクト対象の要素（複数）を設定します。