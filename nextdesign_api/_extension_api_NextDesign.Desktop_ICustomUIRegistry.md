ICustomUIRegistry インタフェース
=========================

名前空間: NextDesign.Desktop

説明​
-----------------------

独自拡張のユーザインタフェースのレジストリです。

所属エリア​
--------------------------------

名前

説明

[カスタムUI](_extension_api_overview_custom-ui.md)

カスタムUIにアクセスするAPI群です。

メソッド​
-----------------------------

名前

説明

[RegisterCustomEditor<TClass, TViewClass>](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_RegisterCustomEditor_TClass__TViewClass_.md)

独自拡張したエディタを登録します。  
  
\- TClass : カスタムエディタの型  
\- TViewClass : カスタムエディタのビュー（xaml）の型

[RegisterCustomFinder<TClass>](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_RegisterCustomFinder_TClass_.md)

独自拡張したファインダを登録します。  
  
\- TClass : カスタムファインダの型

[RegisterCustomInspector<TClass, TViewClass>](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_RegisterCustomInspector_TClass__TViewClass_.md)

独自拡張したインスペクタを登録します。  
  
\- TClass : カスタムインスペクタの型  
\- TViewClass : カスタムインスペクタのビュー（xaml）の型

[RegisterCustomNavigator<TClass, TViewClass>](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_RegisterCustomNavigator_TClass__TViewClass_.md)

独自拡張したナビゲータを登録します。  
  
\- TClass : カスタムナビゲータの型  
\- TViewClass : カスタムナビゲータのビュー（xaml）の型

[UnRegisterAllCustomUIs](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_UnRegisterAllCustomUIs.md)

指定したエクステンションで登録した全ての独自拡張したインタフェースの登録を解除します。

[UnRegisterCustomEditor<TClass>](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_UnRegisterCustomEditor_TClass_.md)

独自拡張したエディタの登録を解除します。  
  
\- TClass : カスタムエディタの型

[UnRegisterCustomFinder<TClass>](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_UnRegisterCustomFinder_TClass_.md)

独自拡張したファインダの登録を解除します。  
  
\- TClass : カスタムファインダの型

[UnRegisterCustomInspector<TClass>](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_UnRegisterCustomInspector_TClass_.md)

独自拡張したインスペクタの登録を解除します。  
  
\- TClass : カスタムインスペクタの型

[UnRegisterCustomNavigator<TClass>](_extension_api_NextDesign.Desktop_ICustomUIRegistry_methods_UnRegisterCustomNavigator_TClass_.md)

独自拡張したナビゲータの登録を解除します。  
  
\- TClass : カスタムナビゲータの型