IDocumentWriteStartParams インタフェース
=================================

名前空間: NextDesign.Desktop.DocumentGenerator

説明​
-----------------------

ドキュメントの書き込み開始時のパラメータです。

所属エリア​
--------------------------------

名前

説明

[ドキュメント生成](_extension_api_overview_documents.md)

ドキュメント生成にアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IDocumentGenerationParamsBase](_extension_api_NextDesign.Desktop_IDocumentGenerationParamsBase.md)

パラメータオブジェクトです。

プロパティ​
--------------------------------

名前

説明

[DocumentContent](_extension_api_NextDesign.Desktop_IDocumentWriteStartParams_properties_DocumentContent.md)

書き込み対象のドキュメントコンテンツです。

[ReplaceAllWriteProcess](_extension_api_NextDesign.Desktop_IDocumentWriteStartParams_properties_ReplaceAllWriteProcess.md)

ドキュメントの書き込み処理を完全に置き換えます。  
  
trueの場合は一切のファイルの作成、コンテンツごとのドキュメント書き込みを行いません。  
生成されたコンテンツを元にカスタムなドキュメント出力機能を開発する場合にtrueにして下さい。  
初期値はfalseです。

メソッド​
-----------------------------

名前

説明

[GetDocumentContent<TContent>](_extension_api_NextDesign.Desktop_IDocumentWriteStartParams_methods_GetDocumentContent_TContent_.md)

型を指定してドキュメントのコンテンツを取得します。