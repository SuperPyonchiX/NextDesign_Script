InformationOnDoubleClickEventParams インタフェース
===========================================

名前空間: NextDesign.Desktop

説明​
-----------------------

情報ウィンドウページ表示要素ダブルクリックイベントのパラメータです。

所属イベントエリア​
--------------------------------------------

名前

説明

[情報ウィンドウ](_extension_api_overview_events_informations.md)

情報ウィンドウ内のページ表示切替、表示要素選択変更を通知します。

継承元​
--------------------------

名前

説明

[ConsumableEventParams](_extension_api_NextDesign.Desktop_ConsumableEventParams.md)

イベント消費可能なイベントパラメータの基底クラスです。

[IInformationEventParams](_extension_api_NextDesign.Desktop_IInformationEventParams.md)

情報ビューの共通パラメータです。

プロパティ​
--------------------------------

名前

説明

[Entry](_extension_api_NextDesign.Desktop_InformationOnDoubleClickEventParams_properties_Entry.md)

ダブルクリックされた情報

[InfoView](_extension_api_NextDesign.Desktop_InformationOnDoubleClickEventParams_properties_InfoView.md)

情報ウィンドウ内の表示ページへのアクセスオブジェクト

[Item](_extension_api_NextDesign.Desktop_InformationOnDoubleClickEventParams_properties_Item.md)

ダブルクリックされた要素

[Model](_extension_api_NextDesign.Desktop_InformationOnDoubleClickEventParams_properties_Model.md)

ダブルクリックされた要素が関連するモデル

[Name](_extension_api_NextDesign.Desktop_InformationOnDoubleClickEventParams_properties_Name.md)

情報ビューの種類。  
\- エラー一覧 : "Error"  
\- 検索結果一覧 : "SearchResult"  
\- 出力 : "Output"

注釈​
-----------------------

エクステンションのイベント処理において、このイベントの、Consume() メソッドを呼び出すことで、アプリケーション本体の既定の処理（ダブルクリックしたエラーや検索結果を表示するビューへのジャンプ）を行わないように要求することができます。