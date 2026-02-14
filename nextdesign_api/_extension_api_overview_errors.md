検索・エラー・出力
=========

説明​
-----------------------

エラー情報や検索結果、出力ウィンドウにアクセスするAPI群です。

エリアに属するAPI​
-----------------------------------------------

名前

説明

[IInfoEntry](_extension_api_NextDesign.Core_IInfoEntry.md)

エラー情報/検索結果情報の共通情報へのアクセスオブジェクトです。

[IInfoDisplayStyleSet](_extension_api_NextDesign.Desktop_IInfoDisplayStyleSet.md)

エラー情報/検索結果情報の表示用スタイルセットです。  
あらかじめ決定したスタイルに名前を付けて管理することができます。  
このスタイルセットは、ワークスペースによって管理され、全てのエクステンションで共有されます。

[IInfoDisplayStyle](_extension_api_NextDesign.Desktop_IInfoDisplayStyle.md)

エラー情報、検索結果情報の表示スタイルへのアクセスオブジェクトです。  
エラー情報や検索結果情報を表示する際のスタイルを再定義する目的で利用できます。

[ISearchResultEntry](_extension_api_NextDesign.Core_ISearchResultEntry.md)

検索結果情報へのアクセスオブジェクトです。  
検索対象がモデルの場合のみ Model、Fieldの値を取得できます。

[IError](_extension_api_NextDesign.Core_IError.md)

モデル検証によるエラー情報です。

[IErrors](_extension_api_NextDesign.Core_IErrors.md)

モデルの検証エラー情報へのアクセス手段を提供します。  
なおエラー情報の登録は、IModelに対して行います。

[IOutput](_extension_api_NextDesign.Desktop_IOutput.md)

出力サービスへのアクセス手段を提供します。

[ISearch](_extension_api_NextDesign.Core_ISearch.md)

検索サービスへのアクセス手段を提供します。  
現在のバージョンのアプリケーションにおける検索処理プロセスは、1プロセスのみ許容します。  
そのため、IsSearching が True を返す状態で、BeginSearch を呼び出した場合はエラーとなります。

[ISearchManager](_extension_api_NextDesign.Core_ISearchManager.md)

検索マネージャ