ICustomEditorView インタフェース
=========================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

エクステンションにより独自拡張するカスタムエディタの実装用インタフェースです。  
Next Design では、このインタフェースを介してカスタムエディタとの情報の交換を行います。

所属エリア​
--------------------------------

名前

説明

[カスタムUI](_extension_api_overview_custom-ui.md)

カスタムUIにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[ICustomUI](_extension_api_NextDesign.Desktop_ICustomUI.md)

エクステンションにより独自拡張するユーザインタフェースに共通のプロパティ、メソッドを定義します。

プロパティ​
--------------------------------

名前

説明

[SelectedItem](_extension_api_NextDesign.Desktop_ICustomEditorView_properties_SelectedItem.md)

エディタで選択された要素を取得します。  
選択された要素がない場合は、null を返すように実装してください。

[SelectedItems](_extension_api_NextDesign.Desktop_ICustomEditorView_properties_SelectedItems.md)

エディタで選択された要素の列挙を取得します。  
選択された要素がない場合は、空の列挙を返すように実装してください。

[ViewDefinitionId](_extension_api_NextDesign.Desktop_ICustomEditorView_properties_ViewDefinitionId.md)

対応するビュー定義の識別子を取得します。

メソッド​
-----------------------------

名前

説明

[GetDocumentContent](_extension_api_NextDesign.Desktop_ICustomEditorView_methods_GetDocumentContent.md)

カスタムエディタのドキュメント生成結果を取得します。

[SetModel](_extension_api_NextDesign.Desktop_ICustomEditorView_methods_SetModel.md)

このエディタの表示対象のモデルを設定します。