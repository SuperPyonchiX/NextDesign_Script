IInfoEntry インタフェース
==================

名前空間: NextDesign.Core

説明​
-----------------------

エラー情報/検索結果情報の共通情報へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[検索・エラー・出力](_extension_api_overview_errors.md)

エラー情報や検索結果、出力ウィンドウにアクセスするAPI群です。

派生先​
--------------------------

名前

説明

[ISearchResultEntry](_extension_api_NextDesign.Core_ISearchResultEntry.md)

検索結果情報へのアクセスオブジェクトです。  
検索対象がモデルの場合のみ Model、Fieldの値を取得できます。

[IError](_extension_api_NextDesign.Core_IError.md)

モデル検証によるエラー情報です。

プロパティ​
--------------------------------

名前

説明

[Category](_extension_api_NextDesign.Core_IInfoEntry_properties_Category.md)

カテゴリ

[Code](_extension_api_NextDesign.Core_IInfoEntry_properties_Code.md)

コード

[DetailMessage](_extension_api_NextDesign.Core_IInfoEntry_properties_DetailMessage.md)

詳細メッセージ

[DisplayStyleName](_extension_api_NextDesign.Core_IInfoEntry_properties_DisplayStyleName.md)

スタイル名  
（省略時はアプリケーションで既定のスタイルが適用されます）

[Fields](_extension_api_NextDesign.Core_IInfoEntry_properties_Fields.md)

エラー/検索対象のフィールド、複数フィールドが対象となった場合はカンマ区切り

[Index](_extension_api_NextDesign.Core_IInfoEntry_properties.md)

エラー/検索対象のフィールドのインデックス

[Message](_extension_api_NextDesign.Core_IInfoEntry_properties_Message.md)

メッセージ

[Model](_extension_api_NextDesign.Core_IInfoEntry_properties_Model.md)

エラー/検索結果のモデル

[NavigatingEditor](_extension_api_NextDesign.Core_IInfoEntry_properties_NavigatingEditor.md)

該当エラーの確認が推奨されるエディタ  
(省略時はアプリケーションが既定するデフォルトの挙動となります)

[NavigatingViewName](_extension_api_NextDesign.Core_IInfoEntry_properties_NavigatingViewName.md)

該当エラーの確認が推奨されるビュー定義名  
(省略時、存在しないビュー定義を指定時はアプリケーションが規定するデフォルトの挙動となります)

[Tags](_extension_api_NextDesign.Core_IInfoEntry_properties_Tags.md)

タグ。  
タグが未設定の場合は空の列挙を返します。

[Title](_extension_api_NextDesign.Core_IInfoEntry_properties_Title.md)

タイトル

[Type](_extension_api_NextDesign.Core_IInfoEntry_properties_Type.md)

エラー情報/検索結果情報のタイプ。  
  
エラーの場合、エラー登録時に指定するエラー種別を取得します。  
\- "Information" : 情報  
\- "Warning" : 警告  
\- "Error" : エラー  
\- "Summary" : サマリー  
  
検索結果の場合、検索時に指定する任意の検索種別を取得します。

メソッド​
-----------------------------

名前

説明

[GetTag(string)](_extension_api_NextDesign.Core_IInfoEntry_methods_GetTag-1.md)

指定したタグを取得します。タグが存在しない場合はnullを返します。

[GetTag<T>(string)](_extension_api_NextDesign.Core_IInfoEntry_methods_GetTag-2.md)

型を指定してタグを取得します。タグが存在しない場合は指定した型のデフォルトを返します。

[HasTag](_extension_api_NextDesign.Core_IInfoEntry_methods_HasTag.md)

指定したタグが存在するかどうかを検証します。

[RemoveTag](_extension_api_NextDesign.Core_IInfoEntry_methods_RemoveTag.md)

指定したタグを削除します。  
既に削除済みのタグを指定した場合は何も行いません。

[SetTag](_extension_api_NextDesign.Core_IInfoEntry_methods_SetTag.md)

指定したタグを設定します。