InformationOnDoubleClickEventParams.Model プロパティ
===============================================

名前空間: NextDesign.Desktop

説明​
-----------------------

ダブルクリックされた要素が関連するモデル

    IModel Model { get; }

型​
--------------------

*   [IModel](_extension_api_NextDesign.Core_IModel.md)

注釈​
-----------------------

以下の場合は、null が返ります。

*   情報ウィンドウ内の表示ページの種類が "Output" の場合
*   情報ウィンドウ内の表示ページの要素 (IInfoEntry) がモデルと対応づかない場合