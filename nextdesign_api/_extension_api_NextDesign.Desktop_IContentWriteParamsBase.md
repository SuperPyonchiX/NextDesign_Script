IContentWriteParamsBase インタフェース
===============================

名前空間: NextDesign.Desktop.DocumentGenerator

説明​
-----------------------

ドキュメント書き込みのパラメータの基底インターフェースです。

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

派生先​
--------------------------

名前

説明

[IAfterContentWriteParams](_extension_api_NextDesign.Desktop_IAfterContentWriteParams.md)

コンテンツの書き込み後のパラメータです。

[IDocumentWriteFinishParams](_extension_api_NextDesign.Desktop_IDocumentWriteFinishParams.md)

ドキュメントの書き込み終了時のパラメータです。  
IDocumentWriterを用いてファイルへの書き込みが可能です。

[IBeforeContentWriteParams](_extension_api_NextDesign.Desktop_IBeforeContentWriteParams.md)

コンテンツを書き込む前のパラメータです。

プロパティ​
--------------------------------

名前

説明

[DocumentContent](_extension_api_NextDesign.Desktop_IContentWriteParamsBase_properties_DocumentContent.md)

書き込み対象のドキュメントコンテンツです。

[Writer](_extension_api_NextDesign.Desktop_IContentWriteParamsBase_properties_Writer.md)

ドキュメントを書き込むためのWriterオブジェクトです。

メソッド​
-----------------------------

名前

説明

[GetDocumentContent<TContent>](_extension_api_NextDesign.Desktop_IContentWriteParamsBase_methods_GetDocumentContent_TContent_.md)

型を指定してドキュメントのコンテンツを取得します。

[GetWriter<TWriter>](_extension_api_NextDesign.Desktop_IContentWriteParamsBase_methods_GetWriter_TWriter_.md)

型を指定してドキュメントを書き込むためのWriterオブジェクトを取得します。