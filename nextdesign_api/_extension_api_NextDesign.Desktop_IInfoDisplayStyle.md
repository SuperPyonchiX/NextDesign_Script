IInfoDisplayStyle インタフェース
=========================

名前空間: NextDesign.Desktop

説明​
-----------------------

エラー情報、検索結果情報の表示スタイルへのアクセスオブジェクトです。  
エラー情報や検索結果情報を表示する際のスタイルを再定義する目的で利用できます。

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

[CardDisplayStyle](_extension_api_NextDesign.Desktop_IInfoDisplayStyle_properties_CardDisplayStyle.md)

カードの表示スタイル  
\- "None" : カードを表示しない  
\- "TitleOnly" : タイトルのみのカード  
\- "DetailOnly" : 詳細のみのカード  
\- "All" : タイトルと詳細のあるカード（詳細がなければタイトルのみ）  
  
既定値は、"All" です。

[Name](_extension_api_NextDesign.Desktop_IInfoDisplayStyle_properties_Name.md)

スタイル名

メソッド​
-----------------------------

名前

説明

[SetStyleSets](_extension_api_NextDesign.Desktop_IInfoDisplayStyle_methods_SetStyleSets.md)

ビュー上での表示スタイルを設定します。  
  
このメソッドで指定したスタイルは、ダイアグラム上でのみ有効となります。  
また、アプリケーションのビュー上で適用中のスタイルを更新しても、ビューの表示にはリアルタイムで反映されません（次回適用時に反映されます）。