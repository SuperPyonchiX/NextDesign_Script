IModelAccessPolicy インタフェース
==========================

名前空間: NextDesign.Core

説明​
-----------------------

モデルアクセスのポリシーです。  
モデルやエディタの詳細情報(シェイプやフォーム要素)へのアクセス範囲を変更できます。

メソッド​
-----------------------------

名前

説明

[SetAllEditorAccess](_extension_api_NextDesign.Core_IModelAccessPolicy_methods_SetAllEditorAccess.md)

全てのエディタの詳細情報を取得可能とするか設定します。  
取得可能に設定した場合、エディタの内部構造を解析するため、エディタAPIの応答に解析時間が加算されます。

[SetAllProductAccess](_extension_api_NextDesign.Core_IModelAccessPolicy_methods_SetAllProductAccess.md)

すべてのモデル情報の取得・検索を可能にします。

[SetSpecifiedProductAccess](_extension_api_NextDesign.Core_IModelAccessPolicy_methods_SetSpecifiedProductAccess.md)

指定したプロダクトで有効なモデル情報のみ取得・検索を可能にします。