IDocumentContent インタフェース
========================

名前空間: NextDesign.Desktop.DocumentGenerator

説明​
-----------------------

ドキュメントのコンテンツです。

所属エリア​
--------------------------------

名前

説明

[ドキュメント生成](_extension_api_overview_documents.md)

ドキュメント生成にアクセスするAPI群です。

派生先​
--------------------------

名前

説明

[IPageContent](_extension_api_NextDesign.Desktop_IPageContent.md)

ドキュメントのページを表すコンテンツです。

[IViewItemContent](_extension_api_NextDesign.Desktop_IViewItemContent.md)

ドキュメントのビューの項目を表すコンテンツです。

[IControlContent](_extension_api_NextDesign.Desktop_IControlContent.md)

ドキュメントのUI部品を表すコンテンツです。

[IViewContent](_extension_api_NextDesign.Desktop_IViewContent.md)

ドキュメントのビューを表すコンテンツです。

プロパティ​
--------------------------------

名前

説明

[AddPageBreak](_extension_api_NextDesign.Desktop_IDocumentContent_properties_AddPageBreak.md)

このコンテンツの先頭に改ページを挿入するかを取得または設定します。

[ContentFactory](_extension_api_NextDesign.Desktop_IDocumentContent_properties_ContentFactory.md)

コンテンツを作成するファクトリです。  
追加でコンテンツを作成することができます。

[ContentType](_extension_api_NextDesign.Desktop_IDocumentContent_properties_ContentType.md)

ドキュメントのコンテンツの種類を取得します。

[Parent](_extension_api_NextDesign.Desktop_IDocumentContent_properties_Parent.md)

親コンテンツを取得します。

メソッド​
-----------------------------

名前

説明

[GetAllContents](_extension_api_NextDesign.Desktop_IDocumentContent_methods_GetAllContents.md)

このコンテンツから所有関係の深さ優先探索で探索できるすべてのコンテンツを取得します。

[GetContents](_extension_api_NextDesign.Desktop_IDocumentContent_methods_GetContents.md)

このコンテンツの直接の所有関係にあるコンテンツを取得します。