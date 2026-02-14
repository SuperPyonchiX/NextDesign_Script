IWorkspace インタフェース
==================

名前空間: NextDesign.Desktop

説明​
-------------------------

アプリケーションの作業領域情報へのアクセス手段を提供します。

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

[CurrentEditor](_extension_api_NextDesign.Desktop_IWorkspace_properties_CurrentEditor.md)

現在選択されているエディタビューで表示中のエディタ情報  
エディタを表示していない場合は、null を返します。

[CurrentModel](_extension_api_NextDesign.Desktop_IWorkspace_properties_CurrentModel.md)

現在のプロジェクトで選択されているモデル要素  
選択されているモデルがない場合は、null を返します。

[CurrentProduct](_extension_api_NextDesign.Desktop_IWorkspace_properties_CurrentProduct.md)

カレントプロジェクトにおいて現在適用状態のプロダクト

[CurrentProject](_extension_api_NextDesign.Desktop_IWorkspace_properties_CurrentProject.md)

カレントプロジェクト  
アプリケーションのワークスペースで現在開いているプロジェクト情報を返します。  
アプリケーションでプロジェクトを開いていない場合は null を返します。

[Errors](_extension_api_NextDesign.Desktop_IWorkspace_properties_Errors.md)

エラー一覧  
このオブジェクトを用いて、現在登録中のエラー情報にアクセスすることができます。

[InfoDisplayStyleSet](_extension_api_NextDesign.Desktop_IWorkspace_properties_InfoDisplayStyleSet.md)

スタイルセットの定義  
このオブジェクトでエラーや検索結果の表示時に指定できるスタイルを管理することができます。

[MainEditor](_extension_api_NextDesign.Desktop_IWorkspace_properties_MainEditor.md)

現在のメインエディタビューで表示中のエディタ情報  
エディタを表示していない場合は、null を返します。

[Output](_extension_api_NextDesign.Desktop_IWorkspace_properties_Output.md)

出力  
このオブジェクトを用いて、アプリケーションの出力にアクセスすることができます。

[ProjectAutoReload](_extension_api_NextDesign.Desktop_IWorkspace_properties_ProjectAutoReload.md)

プロジェクトを再読み込みするか  
  
Next Design 以外からプロジェクトの変更を行った際、プロジェクトを自動で再読み込みするかを取得、または設定します。プロジェクトを自動で再読み込みする場合、確認ダイアログが表示されます。  
プロジェクトを再読み込みする場合は true、再読み込みしない場合は false を指定します。

[Scm](_extension_api_NextDesign.Desktop_IWorkspace_properties_Scm.md)

構成管理へのアクセスオブジェクト

[Search](_extension_api_NextDesign.Desktop_IWorkspace_properties_Search.md)

検索  
このオブジェクトを用いて、検索結果の一覧にアクセスすることができます。

[State](_extension_api_NextDesign.Desktop_IWorkspace_properties_State.md)

ワークスペース状態

[SubEditor](_extension_api_NextDesign.Desktop_IWorkspace_properties_SubEditor.md)

現在のサブエディタビューで表示中のエディタ情報  
エディタを表示していない場合は、null を返します。

メソッド​
-----------------------------

名前

説明

[CanRedo](_extension_api_NextDesign.Desktop_IWorkspace_methods_CanRedo.md)

取り消した編集操作を再実行可能か調べます。

[CanUndo](_extension_api_NextDesign.Desktop_IWorkspace_methods_CanUndo.md)

編集操作を取り消し可能か調べます。

[CleanUpProject](_extension_api_NextDesign.Desktop_IWorkspace_methods_CleanUpProject.md)

指定されたプロジェクトをクリーンアップします。  
プロジェクト未指定の場合は、現在アプリケーションで開いているカレントのプロジェクトをクリーンアップします。  
  
プロジェクトのクリーンアップ処理は、指定されたプロジェクトの管理対象ユニットのうち編集可能なユニットに対してのみ実施します。  
なお、対象プロジェクトが未保存の場合は、このメソッドの呼び出しは失敗します。

[CloseDiff](_extension_api_NextDesign.Desktop_IWorkspace_methods_CloseDiff.md)

差分比較を終了します。

[CloseProject](_extension_api_NextDesign.Desktop_IWorkspace_methods_CloseProject.md)

指定されたプロジェクトを閉じます。  
プロジェクト未指定の場合は、現在アプリケーションで開いているカレントのプロジェクトを閉じます。  
  
この呼び出しは、対象プロジェクトが保存されていない場合でも警告することなく、変更を破棄してプロジェクトを閉じます。

[CreateSearch](_extension_api_NextDesign.Desktop_IWorkspace_methods_CreateSearch.md)

\[Obsolete\] 検索オブジェクトを生成します。  
このオブジェクトを用いて、検索の開始～終了、および検索結果の登録を行うことができます。

[GenerateDocument](_extension_api_NextDesign.Desktop_IWorkspace_methods_GenerateDocument.md)

ドキュメントを生成します。  
モデルナビゲータの選択要素を出力対象の基点モデルとします。

[GetModelUnitByLoadMode](_extension_api_NextDesign.Desktop_IWorkspace_methods_GetModelUnitByLoadMode.md)

指定したロードモードの設定になっているモデルユニットを取得します。

[LoadModelUnits(IProject,IEnumerable<IModelUnit>)](_extension_api_NextDesign.Desktop_IWorkspace_methods_LoadModelUnits-2.md)

指定されたプロジェクトで指定されたモデルユニットを追加読み込みします。  
指定されたユニットが既に読み込み済み、もしくは指定したプロジェクトの管理対象外の場合は無視されます。  
  
対象プロジェクトがカレントプロジェクトの場合、ビジーインジケータを表示して実行します。ただし、キャンセルはできません。  
なお、対象プロジェクトが未保存の場合は、このメソッドの呼び出しは失敗します。対象プロジェクトがカレントプロジェクトの場合は、プロジェクトナビゲータから実行した場合と同様にND上にメッセージダイアログが表示され、追加ロードを中断します。カレントプロジェクト以外の場合は例外が発生します。

[LoadModelUnits(IProject,IEnumerable<string>)](_extension_api_NextDesign.Desktop_IWorkspace_methods_LoadModelUnits-1.md)

指定されたプロジェクトで指定されたモデルユニットを追加読み込みします。  
指定されたユニットが既に読み込み済み、もしくは指定したプロジェクトの管理対象外の場合は無視されます。  
  
対象プロジェクトがカレントプロジェクトの場合、ビジーインジケータを表示して実行します。ただし、キャンセルはできません。  
なお、対象プロジェクトが未保存の場合は、このメソッドの呼び出しは失敗します。対象プロジェクトがカレントプロジェクトの場合は、プロジェクトナビゲータから実行した場合と同様にND上にメッセージダイアログが表示され、追加ロードを中断します。カレントプロジェクト以外の場合は例外が発生します。

[NewProject](_extension_api_NextDesign.Desktop_IWorkspace_methods_NewProject.md)

新規プロジェクトを生成します。

[OpenDiff](_extension_api_NextDesign.Desktop_IWorkspace_methods_OpenDiff.md)

指定した差分比較情報から差分比較を表示します。

[OpenProject(string,bool,bool)](_extension_api_NextDesign.Desktop_IWorkspace_methods_OpenProject-1.md)

指定されたプロジェクトを開きます。  
  

[OpenProject(string,OpenProjectOptions)](_extension_api_NextDesign.Desktop_IWorkspace_methods_OpenProject-2.md)

指定されたオプションを適用してプロジェクトを開きます。

[Redo](_extension_api_NextDesign.Desktop_IWorkspace_methods_Redo.md)

取り消した編集操作を再実行します。

[ReloadProject](_extension_api_NextDesign.Desktop_IWorkspace_methods_ReloadProject.md)

指定されたプロジェクトを再読み込みします。  
プロジェクト未指定の場合は、現在アプリケーションで開いているカレントのプロジェクトを再読み込みします。

[SaveAll](_extension_api_NextDesign.Desktop_IWorkspace_methods_SaveAll.md)

最新の状態に基づいてファイルを保存し直します。  
プロジェクトが未指定の場合は、カレントプロジェクトを保存します。  
  
指定されたプロジェクトに対してファイルが作られていない場合は例外をスローします。  
ロードされていないユニット、および、読み取り専用であるユニットは保存されません。

[SaveProject](_extension_api_NextDesign.Desktop_IWorkspace_methods_SaveProject.md)

指定されたプロジェクトを保存します。  
プロジェクト未指定の場合は、現在アプリケーションで開いているカレントのプロジェクトを保存します。  
正常に保存できた場合はTrueを返します。  
ファイルのアクセス権が取得できない、空き容量が足りない等の状況でこのメソッドを使用した場合、プロジェクトは保存されず、このメソッドはFalseを返します。  
  
プロジェクトの保存先は、IProject.Pathで取得できるパスとなります。  
したがって、新規作成後一度も保存していないプロジェクトに対してこのメソッドは実行できません。  
新規プロジェクトを保存する際には、SaveProjectAs()を利用してください。

[SaveProjectAs](_extension_api_NextDesign.Desktop_IWorkspace_methods_SaveProjectAs.md)

指定されたパスで、指定されたプロジェクトを保存します。  
プロジェクト未指定の場合は、現在アプリケーションで開いているカレントのプロジェクトを保存します。  
正常に保存できた場合はTrueを返します。  
  
この呼び出しは、指定されたパスに既にファイルが存在する場合、警告なく上書き保存を行います。  
保存が成功した場合、対象プロジェクトのPathは新しいパスに置き換わります。

[SetLoadMode](_extension_api_NextDesign.Desktop_IWorkspace_methods_SetLoadMode.md)

モデルユニットのロードモードを設定します。  
指定されたユニットが指定したプロジェクトの管理対象外の場合は無視されます。

[Undo](_extension_api_NextDesign.Desktop_IWorkspace_methods_Undo.md)

編集操作を取り消します。