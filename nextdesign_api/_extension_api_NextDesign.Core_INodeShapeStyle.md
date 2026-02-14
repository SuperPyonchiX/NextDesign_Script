INodeShapeStyle インタフェース
=======================

名前空間: NextDesign.Core

説明​
-----------------------

ノードシェイプのスタイル情報へのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[エディタ](_extension_api_overview_editors.md)

エディタにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IShapeStyle](_extension_api_NextDesign.Core_IShapeStyle.md)

シェイプのスタイル情報へのアクセス手段を提供します。

プロパティ​
--------------------------------

名前

説明

[Figure](_extension_api_NextDesign.Core_INodeShapeStyle_properties_Figure.md)

シェイプの図形を取得・設定します。  
大文字小文字は区別しません。  
値域外の値が指定された場合は図形は表示されません。  
nullまたは空文字を指定した場合はビュー定義で指定している図形となります。  
シェイプ定義で図形の変更が許可されていない場合は図形変更されず無視されます。

[IsSimplified](_extension_api_NextDesign.Core_INodeShapeStyle_properties_IsSimplified.md)

シェイプを簡易表示するかを取得・設定します。  
このAPIは、現在コンパートメントシェイプにのみ対応しています。  
他の種類のシェイプに対して値を設定しても、簡易表示に切り替わりません。

メソッド​
-----------------------------

名前

説明

[ClearImage](_extension_api_NextDesign.Core_INodeShapeStyle_methods_ClearImage.md)

シェイプの画像をクリアします。  
シェイプ定義で画像の貼り付けが許可されていない場合は実行しても画像クリアされず無視されます。

[SetImage](_extension_api_NextDesign.Core_INodeShapeStyle_methods_SetImage.md)

シェイプに画像を設定します。  
シェイプ定義で画像の貼り付けが許可されていない場合は実行しても画像変更されず無視されます。