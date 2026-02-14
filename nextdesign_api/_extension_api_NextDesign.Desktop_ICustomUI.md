ICustomUI インタフェース
=================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

エクステンションにより独自拡張するユーザインタフェースに共通のプロパティ、メソッドを定義します。

所属エリア​
--------------------------------

名前

説明

[カスタムUI](_extension_api_overview_custom-ui.md)

カスタムUIにアクセスするAPI群です。

派生先​
--------------------------

名前

説明

[ICustomFinder](_extension_api_NextDesign.Desktop_ICustomFinder.md)

エクステンションにより独自拡張するカスタムファインダの実装用インタフェースです。  
Next Design では、このインタフェースを介してカスタムファインダとの情報の交換を行います。

[ICustomNavigator](_extension_api_NextDesign.Desktop_ICustomNavigator.md)

エクステンションにより独自拡張するカスタムナビゲータの実装用インタフェースです。  
Next Design では、このインタフェースを介してカスタムナビゲータとの情報の交換を行います。

[ICustomInspector](_extension_api_NextDesign.Desktop_ICustomInspector.md)

エクステンションにより独自拡張するカスタムインスペクタの実装用インタフェースです。  
Next Design では、このインタフェースを介してカスタムインスペクタとの情報の交換を行います。

[ICustomEditorView](_extension_api_NextDesign.Desktop_ICustomEditorView.md)

エクステンションにより独自拡張するカスタムエディタの実装用インタフェースです。  
Next Design では、このインタフェースを介してカスタムエディタとの情報の交換を行います。

プロパティ​
--------------------------------

名前

説明

[Descriptor](_extension_api_NextDesign.Desktop_ICustomUI_properties_Descriptor.md)

記述子を取得、または設定します。  
登録時に指定したDescriptorが生成時に設定されます。  
未設定の場合、カスタムUIは表示されません。

メソッド​
-----------------------------

名前

説明

[OnBeforeDispose](_extension_api_NextDesign.Desktop_ICustomUI_methods_OnBeforeDispose.md)

独自拡張するユーザインタフェースを破棄する前の処理  
Next Designは、独自拡張するユーザインタフェースを破棄する前にこのメソッドを呼び出します。  
拡張側で破棄前に実行したい処理がある場合はここで実装します。

[OnInitialized](_extension_api_NextDesign.Desktop_ICustomUI_methods_OnInitialized.md)

独自拡張するユーザインタフェースを初期化する際の処理  
Next Designは、独自拡張するユーザインタフェースを初期化する際にこのメソッドを呼び出します。  
拡張側で初期化時に実行したい処理がある場合はここで実装します。