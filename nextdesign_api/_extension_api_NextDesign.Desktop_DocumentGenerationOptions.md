DocumentGenerationOptions インタフェース
=================================

名前空間: NextDesign.Desktop.DocumentGenerator

説明​
-----------------------

ドキュメント生成オプションです。

所属エリア​
---------------------------------

名前

説明

[ドキュメント生成](_extension_api_overview_documents.md)

ドキュメント生成にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Format](_extension_api_NextDesign.Desktop_DocumentGenerationOptions_properties_Format.md)

出力フォーマットです。  
\- "word" : Word形式  
\- "pdf" : PDF形式  
\- "html" : HTML形式

[OpenDocument](_extension_api_NextDesign.Desktop_DocumentGenerationOptions_properties_OpenDocument.md)

出力したファイルを開くかどうかを指定します。  
trueの場合はドキュメントの出力後に出力したファイルを既定のアプリケーションで開きます。  
初期値はtrueです。

[OutputPath](_extension_api_NextDesign.Desktop_DocumentGenerationOptions_properties_OutputPath.md)

出力するフォルダパスまたはファイルパスです。  
\- htmlの場合 : 出力フォルダを指定して下さい。  
\- word/pdfの場合 : 出力ファイルパスを指定して下さい。

[Scope](_extension_api_NextDesign.Desktop_DocumentGenerationOptions_properties_Scope.md)

対象範囲を指定します。  
\- "all" : すべてを出力します。  
\- "allchildren" : 選択要素から末端まで出力します。  
\- "current" : 選択要素のみ出力します。