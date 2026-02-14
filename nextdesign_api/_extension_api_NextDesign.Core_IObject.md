IObject インタフェース
===============

名前空間: NextDesign.Core

説明​
-----------------------

識別可能なオブジェクトを表します。

所属エリア​
--------------------------------

名前

説明

[グローバル](_extension_api_overview_global.md)

エクステンションの実行環境や実行状態にアクセスするAPI群です。

派生先​
--------------------------

名前

説明

[INamedElement](_extension_api_NextDesign.Core_INamedElement.md)

名前付け可能要素を表します。

[IModel](_extension_api_NextDesign.Core_IModel.md)

NextDesignの設計モデル情報へのアクセス手段を提供します。

[IRepresentation](_extension_api_NextDesign.Core_IRepresentation.md)

表現情報へのアクセスオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[Id](_extension_api_NextDesign.Core_IObject_properties_Id.md)

識別子

[Tags](_extension_api_NextDesign.Core_IObject_properties_Tags.md)

タグ一覧

メソッド​
-----------------------------

名前

説明

[GetTag](_extension_api_NextDesign.Core_IObject_methods_GetTag.md)

指定されたタグ名に一致するタグを取得します。

[GetTagValue](_extension_api_NextDesign.Core_IObject_methods_GetTagValue.md)

指定されたタグ名に一致するタグの値を取得します。  
タグが存在しない場合はnullを返します。

[HasTags](_extension_api_NextDesign.Core_IObject_methods_HasTags.md)

指定されたタグ名に一致するタグが存在するか確認します。

[RemoveTag](_extension_api_NextDesign.Core_IObject_methods_RemoveTag.md)

指定されたタグ名に一致するタグを削除します。

[SetTag](_extension_api_NextDesign.Core_IObject_methods_SetTag.md)

指定されたタグ名／値でタグを設定します。  
\- タグ名に一致するタグが存在しない場合はタグを追加します  
\- タグ名に一致するタグが存在する場合は値を変更します