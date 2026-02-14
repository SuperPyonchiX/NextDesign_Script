IErrors インタフェース
===============

名前空間: NextDesign.Core

説明​
-----------------------

モデルの検証エラー情報へのアクセス手段を提供します。  
なおエラー情報の登録は、IModelに対して行います。

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

[AllErrors](_extension_api_NextDesign.Core_IErrors_properties_AllErrors.md)

すべてのエラー情報のコレクション  
エラー登録がない場合は空のコレクションを返します。

[Errors](_extension_api_NextDesign.Core_IErrors_properties_Errors.md)

エラー種別がerrorのエラー情報  
該当エラーがない場合は空のコレクションを返します。

[Informations](_extension_api_NextDesign.Core_IErrors_properties_Informations.md)

エラー種別がinformationのエラー情報  
該当エラーがない場合は空のコレクションを返します。

[Summaries](_extension_api_NextDesign.Core_IErrors_properties_Summaries.md)

エラー種別がsummaryのエラー情報  
該当エラーがない場合は空のコレクションを返します。

[Warnings](_extension_api_NextDesign.Core_IErrors_properties_Warnings.md)

エラー種別がwarningのエラー情報  
該当エラーがない場合は空のコレクションを返します。

メソッド​
-----------------------------

名前

説明

[AddErrors](_extension_api_NextDesign.Core_IErrors_methods_AddErrors.md)

指定したエラー情報をモデルに追加します。

[ClearErrors](_extension_api_NextDesign.Core_IErrors_methods_ClearErrors.md)

すべてのエラー情報をクリアします。

[ClearErrorsAt](_extension_api_NextDesign.Core_IErrors_methods_ClearErrorsAt.md)

指定されたモデルの全てのエラー情報をクリアします。

[FindErrorByCategory](_extension_api_NextDesign.Core_IErrors_methods_FindErrorByCategory.md)

指定されたカテゴリのエラー情報を検索します。  
検索結果のエラー情報にはサマリー情報も含みます。  
該当するエラー情報がない場合は空のコレクションを返します。

[FindErrorOfModelByCategory](_extension_api_NextDesign.Core_IErrors_methods_FindErrorOfModelByCategory.md)

与えられたモデルの指定されたカテゴリのエラー情報を検索します。  
検索結果のエラー情報にはサマリー情報も含みます。  
該当するエラー情報がない場合は空のコレクションを返します。

[RemoveError](_extension_api_NextDesign.Core_IErrors_methods_RemoveError.md)

指定したエラー情報を削除します。  
既に削除済みのエラー情報を指定した場合は何も行いません。

[RemoveErrors](_extension_api_NextDesign.Core_IErrors_methods_RemoveErrors.md)

指定したエラー情報をすべて削除します。  
既に削除済みのエラー情報が含まれる場合、そのエラー情報を無視します。