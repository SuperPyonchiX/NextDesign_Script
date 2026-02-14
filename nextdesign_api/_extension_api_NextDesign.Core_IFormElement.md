IFormElement インタフェース
====================

名前空間: NextDesign.Core

説明​
-----------------------

フォーム要素情報へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[エディタ](_extension_api_overview_editors.md)

エディタにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IEditorElement](_extension_api_NextDesign.Core_IEditorElement.md)

エディタ要素へのアクセスオブジェクトです。

派生先​
--------------------------

名前

説明

[IGridRow](_extension_api_NextDesign.Core_IGridRow.md)

フォームのグリッド行へのアクセス手段を提供します。

[IGrid](_extension_api_NextDesign.Core_IGrid.md)

フォームのグリッドへのアクセス手段を提供します。

プロパティ​
--------------------------------

名前

説明

[Controls](_extension_api_NextDesign.Core_IFormElement_properties_Controls.md)

フォーム子要素

[ControlType](_extension_api_NextDesign.Core_IFormElement_properties_ControlType.md)

このフォーム要素の種類を取得します。  
"TextBox" : テキストボックス  
"RichTextBox" : リッチテキストボックス  
"CheckBox" : チェックボックス  
"ComboBox" : コンボボックス  
"List" : リスト  
"Grid" : グリッド  
"ModelReferenceControl" : モデル参照コントロール  
"EntityControl" : エンティティコントロール  
"GroupControl" : グループコントロール  
"GridRow" : グリッド行  

注釈​
-----------------------

フォーム要素では、基底の制約に追加して、次のプロパティもサポートされません。  
・識別子（Id）  
これらのプロパティにアクセスした場合、例外がスローされます。