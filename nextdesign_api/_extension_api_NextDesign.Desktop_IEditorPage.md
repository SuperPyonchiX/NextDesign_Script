IEditorPage インタフェース
===================

名前空間: NextDesign.Desktop

説明​
-------------------------

エディタUIへのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[ユーザインタフェース](_extension_api_overview_interfaces.md)

エディタやナビゲータなどのUIにアクセスするAPI群です。

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

[ActiveInfoWindow](_extension_api_NextDesign.Desktop_IEditorPage_properties_ActiveInfoWindow.md)

\[Obsolete\]  
情報ペインのアクティブページ  
\- "Output" : 出力  
\- "Error" : エラー  
\- "SearchResult" : 検索結果

[ActiveInspector](_extension_api_NextDesign.Desktop_IEditorPage_properties_ActiveInspector.md)

インスペクタペインのアクティブなインスペクタ  
\- "Property" : プロパティ  
\- "Relationship" : 関連  
\- "ViewDefinition" : 以下のインスペクタにて取得または設定できます。  
\- ダイアグラム定義  
\- シェイプ定義  
\- ノード  
\- ポート  
\- コネクタ  
\- ライフライン  
\- ノート  
\- 複合フラグメント  
\- メッセージ  
\- ノートアンカ  
\- 実行仕様  
\- 破棄  
\- メッセージ端  
\- フォーム定義  
\- フォーム要素  
\- テキスト  
\- コンボボックス  
\- チェックボックス  
\- リッチテキスト  
\- モデル参照コントロール  
\- グループ  
\- グリッド  
\- リスト  
\- 行の定義  
\- ツリーグリッド行  
\- "Metamodel" : 以下のインスペクタにて取得または設定できます。  
\- パッケージ  
\- メタモデル  
\- クラス  
\- 関連クラス  
\- フィールド  
\- 列挙型

[ActiveNavigator](_extension_api_NextDesign.Desktop_IEditorPage_properties_ActiveNavigator.md)

ナビゲータペインのアクティブなナビゲータ  
\- "Model" : モデルナビゲータ  
\- "ProductLine" : プロダクトラインナビゲータ  
\- "Scm" : 構成管理ナビゲータ  
\- "Project" : プロジェクトナビゲータ  
\- "Profile" : プロファイルナビゲータ

[ActivePalette](_extension_api_NextDesign.Desktop_IEditorPage_properties_ActivePalette.md)

パレットペインのアクティブなパレット  
\- "Editor" : ツールボックス  
\- "Reference" : 参照  
\- "Derive" : 入力  
\- "Feature" : フィーチャパレット  
\- "ProductSelector" : プロダクトセレクタ  
\- "Class" : クラスツールボックス

[CurrentEditorView](_extension_api_NextDesign.Desktop_IEditorPage_properties_CurrentEditorView.md)

アクティブタブの現在アクティブなエディタビュー

[CurrentInfoView](_extension_api_NextDesign.Desktop_IEditorPage_properties_CurrentInfoView.md)

\[Obsolete\]  
情報ウィンドウ内の現在アクティブな表示ページ

[CurrentModel](_extension_api_NextDesign.Desktop_IEditorPage_properties_CurrentModel.md)

現在（モデルナビゲータで）選択されているモデル

[CurrentNavigator](_extension_api_NextDesign.Desktop_IEditorPage_properties_CurrentNavigator.md)

現在アクティブなナビゲータ

[CurrentOutputCategory](_extension_api_NextDesign.Desktop_IEditorPage_properties_CurrentOutputCategory.md)

\[Obsolete\]  
情報ペインのOutputのカレントカテゴリ

[IsDiffHighlightVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsDiffHighlightVisible.md)

変更差分比較モード

[IsDiffViewVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsDiffViewVisible.md)

差分ビューの表示状態

[IsErrorCardVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsErrorCardVisible.md)

エラーカード表示状態

[IsFeatureMarkVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsFeatureMarkVisible.md)

フィーチャーマーク表示状態

[IsIndicatorVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsIndicatorVisible.md)

インジケータ表示状態

[IsInformationPaneVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsInformationPaneVisible.md)

\[Obsolete\]  
情報ペイン表示状態

[IsInspectorPaneVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsInspectorPaneVisible.md)

インスペクタペイン表示状態

[IsNavigatorPaneVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsNavigatorPaneVisible.md)

ナビゲータペイン表示状態

[IsSubEditorVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsSubEditorVisible.md)

アクティブタブのサブエディタ表示状態

[IsTraceLineVisible](_extension_api_NextDesign.Desktop_IEditorPage_properties_IsTraceLineVisible.md)

エディタ間トレース表示状態

[MainEditorView](_extension_api_NextDesign.Desktop_IEditorPage_properties_MainEditorView.md)

アクティブタブのメインエディタビュー

[SubEditorMode](_extension_api_NextDesign.Desktop_IEditorPage_properties_SubEditorMode.md)

アクティブタブのサブエディタの表示モード

[SubEditorModeName](_extension_api_NextDesign.Desktop_IEditorPage_properties_SubEditorModeName.md)

アクティブタブのサブエディタの表示モード名  
  
SubEditorModeが「Custom」の場合、`Custom.\{ModelEditorCategory.Id\}` の形式となる。

[SubEditorView](_extension_api_NextDesign.Desktop_IEditorPage_properties_SubEditorView.md)

アクティブタブのサブエディタビュー

メソッド​
-----------------------------

名前

説明

[IsMainEditor](_extension_api_NextDesign.Desktop_IEditorPage_methods_IsMainEditor.md)

指定したカスタムエディタが現在アクティブタブのメインエディタに表示されているか否かを判定します。  
カスタムエディタの初期化処理 ICustomUI.OnInitialized() 実行時には正しく判定出来ず、常にfalseを返します。

[IsSubEditor](_extension_api_NextDesign.Desktop_IEditorPage_methods_IsSubEditor.md)

指定したカスタムエディタが現在アクティブタブのサブエディタに表示されているか否かを判定します。  
カスタムエディタの初期化処理 ICustomUI.OnInitialized() 実行時には正しく判定出来ず、常にfalseを返します。

[SetSubEditorMode(string,IModel)](_extension_api_NextDesign.Desktop_IEditorPage_methods_SetSubEditorMode-2.md)

アクティブタブのサブエディタの表示モードを string 型で指定します。

[SetSubEditorMode(SubEditorMode,IModel)](_extension_api_NextDesign.Desktop_IEditorPage_methods_SetSubEditorMode-1.md)

アクティブタブのサブエディタの表示モードを列挙型で指定します。

[UpdateEditors](_extension_api_NextDesign.Desktop_IEditorPage_methods_UpdateEditors.md)

このエディタページを最新に更新します。  
すべてのタブのメインエディタとサブエディタの両方を同時に更新します。  
ただし、リフレッシュ対象のエディタ上でシェイプやコネクタの位置やスタイルを編集していた場合、その編集内容が失われます。