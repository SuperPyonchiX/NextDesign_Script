IDocumentWriteStartParams.ReplaceAllWriteProcess プロパティ
======================================================

名前空間: NextDesign.Desktop.DocumentGenerator

説明​
-----------------------

ドキュメントの書き込み処理を完全に置き換えます。

trueの場合は一切のファイルの作成、コンテンツごとのドキュメント書き込みを行いません。  
生成されたコンテンツを元にカスタムなドキュメント出力機能を開発する場合にtrueにして下さい。  
初期値はfalseです。

    bool ReplaceAllWriteProcess { get; set; }

型​
--------------------

*   bool