IEditorPage.CurrentInfoView プロパティ
=================================

名前空間: NextDesign.Desktop

説明​
-----------------------

\[Obsolete\]  
情報ウィンドウ内の現在アクティブな表示ページ

    IInfoView CurrentInfoView { get; }

型​
--------------------

*   [IInfoView](_extension_api_NextDesign.Desktop_IInfoView.md)

注釈​
-----------------------

**Ver.1.1 での API 仕様変更と移行方法について**

Ver.1.1 より、このプロパティは、IWorkspaceWindowに移行されました。このプロパティは将来のバージョンで削除されます。  
本 API を利用している場合は、Ver.1.1 以降へのバージョンアップと合わせてエクステンション内の該当箇所をIWorkspaceWindow.CurrentInfoViewを利用するように変更してください。