IForm インタフェース
=============

名前空間: NextDesign.Core

説明​
-----------------------

フォームエディタ情報へのアクセスオブジェクトです。  
IEditorのEditorTypeが"DocumentForm"の場合、このインタフェース型にキャストすることでフォーム固有の情報にアクセスすることができます。

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

[IEditor](_extension_api_NextDesign.Core_IEditor.md)

エディタ情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[Elements](_extension_api_NextDesign.Core_IForm_properties_Elements.md)

フォーム要素一覧

[RootControl](_extension_api_NextDesign.Core_IForm_properties_RootControl.md)

フォームのルート要素

メソッド​
-----------------------------

名前

説明

[GetSelectedControls](_extension_api_NextDesign.Core_IForm_methods_GetSelectedControls.md)

エディタで選択されているフォーム要素を取得します。  
選択要素のコレクションの順序は不定です。  
選択されたフォーム要素が存在しない場合は空のコレクションを返します。