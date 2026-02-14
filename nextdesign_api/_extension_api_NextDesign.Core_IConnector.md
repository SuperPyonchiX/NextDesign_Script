IConnector インタフェース
==================

名前空間: NextDesign.Core

説明​
-----------------------

コネクタ図形要素情報へのアクセスオブジェクトです。

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

[IShape](_extension_api_NextDesign.Core_IShape.md)

シェイプ情報へのアクセス手段を提供します。

プロパティ​
--------------------------------

名前

説明

[EndPoint](_extension_api_NextDesign.Core_IConnector_properties_EndPoint.md)

コネクタ終点に接続されたノード

[LineType](_extension_api_NextDesign.Core_IConnector_properties_LineType.md)

コネクタの線の種類  
  
線の種類は以下の通りです。  
\- "Straight" : 直線  
\- "Orthogonal" : 折れ線  
\- "Bezier1Dimension" : 1次ベジェ曲線  
\- "Bezier2Dimension" : 2次ベジェ曲線  
\- "Tree" : ツリー (※ツリーダイアグラムのコネクタの場合のみ)

[StartPoint](_extension_api_NextDesign.Core_IConnector_properties_StartPoint.md)

コネクタ始点に接続されたノード

メソッド​
-----------------------------

名前

説明

[AddBend](_extension_api_NextDesign.Core_IConnector_methods_AddBend.md)

コネクタに指定した座標でベンドを追加します。  
  
線の種類が1次ベジェ曲線、もしくは2次ベジェ曲線のコネクタに対し、UIで表示した状態で実行すると、表示が崩れる場合があります。  
また、コネクタを再表示した際、線の種類に対しベンド数が過不足していると、ベントが再作成・再配置されます。  
例) 2次ベジェ曲線にベンドが3つ存在する。

[AddBends](_extension_api_NextDesign.Core_IConnector_methods_AddBends.md)

コネクタに指定した座標の列挙でベンドを追加します。  
ベンドは、コネクタの接続元(StartPoint)から接続先(EndPoint)の方向に順番に追加します。  
  
線の種類が1次ベジェ曲線、もしくは2次ベジェ曲線のコネクタに対し、UIで表示した状態で実行すると、表示が崩れる場合があります。  
また、コネクタを再表示した際、線の種類に対しベンド数が過不足していると、ベントが再作成・再配置されます。  
例) 2次ベジェ曲線にベンドが3つ存在する。

[ClearBends](_extension_api_NextDesign.Core_IConnector_methods_ClearBends.md)

コネクタのベンドをクリアします。  
  
線の種類が1次ベジェ曲線、もしくは2次ベジェ曲線のコネクタに対し、UIで表示した状態で実行すると、表示が崩れる場合があります。  
また、コネクタを再表示した際、線の種類に対しベンド数が過不足していると、ベントが再作成・再配置されます。  
例) 2次ベジェ曲線にベンドが3つ存在する。

[GetBends](_extension_api_NextDesign.Core_IConnector_methods_GetBends.md)

コネクタのベンドを取得します。  
ベンドは、コネクタの接続元(StartPoint)から接続先(EndPoint)の方向にその位置(座標)を順番に列挙します。  
ベントが存在しない場合は、空の列挙を返します。

[SetLineType](_extension_api_NextDesign.Core_IConnector_methods_SetLineType.md)

コネクタの線の種類を設定します。