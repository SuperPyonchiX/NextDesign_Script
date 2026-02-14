ICustomNavigator インタフェース
========================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

エクステンションにより独自拡張するカスタムナビゲータの実装用インタフェースです。  
Next Design では、このインタフェースを介してカスタムナビゲータとの情報の交換を行います。

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

[SelectedItem](_extension_api_NextDesign.Desktop_ICustomNavigator_properties_SelectedItem.md)

ナビゲータで選択された要素  
選択された要素がない場合は、null を返すように実装してください。

[SelectedItems](_extension_api_NextDesign.Desktop_ICustomNavigator_properties_SelectedItems.md)

ナビゲータで選択された要素の列挙  
選択された要素がない場合は、空の列挙を返すように実装してください。

メソッド​
-----------------------------

名前

説明

[OnHide](_extension_api_NextDesign.Desktop_ICustomNavigator_methods_OnHide.md)

このナビゲータを非表示にする際の処理  
Next Designは、独自拡張するナビゲータを隠す際にこのメソッドを呼び出します。  
拡張側で非表示時に実行したい処理がある場合はここで実装します。

[OnShow](_extension_api_NextDesign.Desktop_ICustomNavigator_methods_OnShow.md)

このナビゲータを表示する際の処理  
Next Designは、独自拡張するナビゲータを表示する際にこのメソッドを呼び出します。  
拡張側で表示時に実行したい処理がある場合はここで実装します。