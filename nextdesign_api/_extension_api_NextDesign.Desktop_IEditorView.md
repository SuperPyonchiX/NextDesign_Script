IEditorView インタフェース
===================

名前空間: NextDesign.Desktop

説明​
-----------------------

メイン・サブエディタUIの共通インタフェースです。

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

[DiffEditor](_extension_api_NextDesign.Desktop_IEditorView_properties_DiffEditor.md)

差分エディタ  
エディタビューが差分エディタを持たない場合(※)は null を返します。  
  
(※)以下のビューが該当します。  
・コンフィグレーションエディタ  
・カスタムエディタ

[Editor](_extension_api_NextDesign.Desktop_IEditorView_properties_Editor.md)

現在のエディタ

[HasDiffEditor](_extension_api_NextDesign.Desktop_IEditorView_properties_HasDiffEditor.md)

このエディタビューで差分エディタを保持しているかを調べます。

[SelectedModels](_extension_api_NextDesign.Desktop_IEditorView_properties_SelectedModels.md)

エディタで選択されているモデル  
選択要素のコレクションの順序は不定です。  
選択されているモデルがない場合は、空のコレクションを返します。

[ViewDefinitions](_extension_api_NextDesign.Desktop_IEditorView_properties_ViewDefinitions.md)

このエディタで選択できるビュー定義の一覧  
選択できるビュー定義がない場合は空のコレクションを返します。

メソッド​
-----------------------------

名前

説明

[GetImage](_extension_api_NextDesign.Desktop_IEditorView_methods_GetImage.md)

エディタで表示するビューのビットマップイメージを取得します。  
イメージが取得できなかった場合は null を返します。

[SaveImage](_extension_api_NextDesign.Desktop_IEditorView_methods_SaveImage.md)

指定したパスにエディタで表示するビューのビットマップイメージを保存します。  
イメージの保存形式はPNG, JPEG, BMP, GIF, XPS形式です。  
保存形式は指定された保存先のファイルパスの拡張子から判断し、拡張子から判断できない場合はPNG形式とします。

[SelectViewDefinition](_extension_api_NextDesign.Desktop_IEditorView_methods_SelectViewDefinition.md)

エディタで表示するビューを切り替えます。  
ビュー定義には、ViewDefinitionsで取得できる、エディタのモデルに対応するビュー定義のみが指定できます。