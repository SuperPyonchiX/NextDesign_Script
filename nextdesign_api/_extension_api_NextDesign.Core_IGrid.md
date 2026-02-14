IGrid インタフェース
=============

名前空間: NextDesign.Core

説明​
-----------------------

フォームのグリッドへのアクセス手段を提供します。

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

[Columns](_extension_api_NextDesign.Core_IGrid_properties_Columns.md)

グリッドの列情報

[Rows](_extension_api_NextDesign.Core_IGrid_properties_Rows.md)

グリッドの行情報  
グリッドが行を持たない場合は空のコレクションを返します。

メソッド​
-----------------------------

名前

説明

[GetSelectedRows](_extension_api_NextDesign.Core_IGrid_methods_GetSelectedRows.md)

このグリッドで選択された行を取得します。  
選択要素のコレクションの順序は不定です。  
選択された行が存在しない場合は空のコレクションを返します。