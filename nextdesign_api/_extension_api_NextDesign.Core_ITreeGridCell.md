ITreeGridCell インタフェース
=====================

名前空間: NextDesign.Core

説明​
-----------------------

ツリーグリッドノードのセルへのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[エディタ](_extension_api_overview_editors.md)

エディタにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Column](_extension_api_NextDesign.Core_ITreeGridCell_properties_Column.md)

列情報

[Model](_extension_api_NextDesign.Core_ITreeGridCell_properties_Model.md)

モデル

[Path](_extension_api_NextDesign.Core_ITreeGridCell_properties_Path.md)

パス  
Column.Pathと等価になります。

メソッド​
-----------------------------

名前

説明

[GetValue](_extension_api_NextDesign.Core_ITreeGridCell_methods_GetValue.md)

このセルの値を取得します。  
このメソッドの呼び出しは、this.Model.GetField(this.Path) と等価になります。