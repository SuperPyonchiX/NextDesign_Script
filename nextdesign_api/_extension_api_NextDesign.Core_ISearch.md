ISearch インタフェース
===============

名前空間: NextDesign.Core

説明​
-----------------------

検索サービスへのアクセス手段を提供します。  
現在のバージョンのアプリケーションにおける検索処理プロセスは、1プロセスのみ許容します。  
そのため、IsSearching が True を返す状態で、BeginSearch を呼び出した場合はエラーとなります。

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

[IsSearchCanceled](_extension_api_NextDesign.Core_ISearch_properties_IsSearchCanceled.md)

現在の検索にキャンセル要求が発行されているか調べます。  
キャンセルが要求されている場合はTrueを返します。

[IsSearching](_extension_api_NextDesign.Core_ISearch_properties_IsSearching.md)

検索中であるか調べます。  
検索処理プロセスが実行されている場合はTrueを返します。

[Name](_extension_api_NextDesign.Core_ISearch_properties_Name.md)

検索名

[SearchTargets](_extension_api_NextDesign.Core_ISearch_properties_SearchTargets.md)

検索対象モデルのコレクション

[ShowTotalCount](_extension_api_NextDesign.Core_ISearch_properties_ShowTotalCount.md)

検索結果の積み上げ値を表示するか

[Type](_extension_api_NextDesign.Core_ISearch_properties_Type.md)

検索種別

メソッド​
-----------------------------

名前

説明

[AddSearchResult(IModel,string,string)](_extension_api_NextDesign.Core_ISearch_methods_AddSearchResult-2.md)

検索結果を登録します。  
  
検索対象が特にモデルの場合は、このインタフェースを使用することで検索条件にヒットしたフィールド情報を付与することができます。

[AddSearchResult(object,string)](_extension_api_NextDesign.Core_ISearch_methods_AddSearchResult-1.md)

検索結果を登録します。  
  
モデル以外の情報を検索結果として扱う場合は、本メソッドにより登録することができます。

[AddSearchTarget](_extension_api_NextDesign.Core_ISearch_methods_AddSearchTarget.md)

検索対象を追加します。  
検索対象の情報は検索結果ウィンドウやエディタ、ナビゲータ等で表示されます。

[BeginSearch](_extension_api_NextDesign.Core_ISearch_methods_BeginSearch.md)

検索開始を要求します。

[CancelSearch](_extension_api_NextDesign.Core_ISearch_methods_CancelSearch.md)

検索のキャンセルを要求します。  
キャンセルが要求された以降は、検索結果の登録を無視します。

[ClearSearchResult](_extension_api_NextDesign.Core_ISearch_methods_ClearSearchResult.md)

現在の検索結果をクリアします。

[ClearSearchTarget](_extension_api_NextDesign.Core_ISearch_methods_ClearSearchTarget.md)

検索対象をクリアします。

[EndSearch](_extension_api_NextDesign.Core_ISearch_methods_EndSearch.md)

検索終了を要求します。  
現在実行中の検索処理を終了して、検索結果を確定します。  
確定された検索結果は、検索結果一覧で取得可能となります。