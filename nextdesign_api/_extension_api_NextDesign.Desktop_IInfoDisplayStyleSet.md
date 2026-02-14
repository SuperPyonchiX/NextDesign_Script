IInfoDisplayStyleSet インタフェース
============================

名前空間: NextDesign.Desktop

説明​
-----------------------

エラー情報/検索結果情報の表示用スタイルセットです。  
あらかじめ決定したスタイルに名前を付けて管理することができます。  
このスタイルセットは、ワークスペースによって管理され、全てのエクステンションで共有されます。

所属エリア​
--------------------------------

名前

説明

[検索・エラー・出力](_extension_api_overview_errors.md)

エラー情報や検索結果、出力ウィンドウにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Styles](_extension_api_NextDesign.Desktop_IInfoDisplayStyleSet_properties_Styles.md)

スタイル一覧

メソッド​
-----------------------------

名前

説明

[ClearAllStyles](_extension_api_NextDesign.Desktop_IInfoDisplayStyleSet_methods_ClearAllStyles.md)

このスタイルセットで管理しているすべてのスタイルをクリアします。

[ClearStyle](_extension_api_NextDesign.Desktop_IInfoDisplayStyleSet_methods_ClearStyle.md)

指定された名前のスタイルをクリアします。  
該当するスタイルが存在しない場合は何も行われません。

[CreateStyle](_extension_api_NextDesign.Desktop_IInfoDisplayStyleSet_methods_CreateStyle.md)

指定された名前のスタイルを作成します。  
ただし、該当する名前のスタイルが既に存在する場合は、そのスタイルを返します。

[GetStyle](_extension_api_NextDesign.Desktop_IInfoDisplayStyleSet_methods_GetStyle.md)

指定された名前のスタイルの取得します。  
該当する名前のスタイルが存在しない場合は null を返します。