IGridRow インタフェース
================

名前空間: NextDesign.Core

説明​
-----------------------

フォームのグリッド行へのアクセス手段を提供します。

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

[IFormElement](_extension_api_NextDesign.Core_IFormElement.md)

フォーム要素情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[Cells](_extension_api_NextDesign.Core_IGridRow_properties_Cells.md)

このグリッド行のセル

メソッド​
-----------------------------

名前

説明

[GetSelectedCells](_extension_api_NextDesign.Core_IGridRow_methods_GetSelectedCells.md)

このグリッド行で選択されているセルを取得します。  
選択されたセルが存在しない場合は空のコレクションを返します。

注釈​
-----------------------

IGrowRowオブジェクトは、ビュー定義と関連づきません。  
そのため、以下のプロパティを取得した場合、すべて null が返される点に注意してください。

*   IRepresentation.ViewDefinition
*   IEditorElement.ElementDefinition
*   IEditorElement.Path