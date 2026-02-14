IViewDefinitions インタフェース
========================

名前空間: NextDesign.Core

説明​
-----------------------

ビュー定義管理オブジェクトへのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[プロファイル](_extension_api_overview_profile.md)

プロファイルにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Editors](_extension_api_NextDesign.Core_IViewDefinitions_properties_Editors.md)

エディタ定義一覧

メソッド​
-----------------------------

名前

説明

[FindEditorDefByClass(IClass,string)](_extension_api_NextDesign.Core_IViewDefinitions_methods_FindEditorDefByClass-1.md)

指定したクラスに定義されたエディタのビュー定義を検索します。

[FindEditorDefByClass(string,bool,string)](_extension_api_NextDesign.Core_IViewDefinitions_methods_FindEditorDefByClass-2.md)

指定したクラスに定義されたエディタのビュー定義を検索します。

[FindElementDefByClass(IEditorDef,IClass,string)](_extension_api_NextDesign.Core_IViewDefinitions_methods_FindElementDefByClass-1.md)

指定したエディタ定義から指定したモデルクラスに対応するエディタ要素定義を検索します。

[FindElementDefByClass(IEditorDef,string,bool,string)](_extension_api_NextDesign.Core_IViewDefinitions_methods_FindElementDefByClass-2.md)

指定したエディタ定義から指定したモデルクラスに対応するエディタ要素定義を検索します。

[NewCustomEditorDef](_extension_api_NextDesign.Core_IViewDefinitions_methods_NewCustomEditorDef.md)

指定したモデルクラスのカスタムエディタ定義を生成します。

[NewCustomElementDef](_extension_api_NextDesign.Core_IViewDefinitions_methods_NewCustomElementDef.md)

指定したモデルクラスのカスタムエディタ要素定義を生成します。

[NewEditorDef](_extension_api_NextDesign.Core_IViewDefinitions_methods_NewEditorDef.md)

指定したモデルクラスのエディタ定義を生成します。

[NewElementDef](_extension_api_NextDesign.Core_IViewDefinitions_methods_NewElementDef.md)

指定したモデルクラスのエディタ要素定義を生成します。

[RegisterCompartmentItemTextValueCallback(IElementDef,string,Func<INode, int, string, IModel, string>,Action<INode, int, string, IModel, string>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterCompartmentItemTextValueCallback-1.md)

指定したエディタ要素定義より生成されるエディタ要素の指定されたコンパートメントアイテムの値取得/設定コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・登録した値取得のコールバック関数が呼び出される契機は以下です。  
　・コンパートメントアイテムが表示された  
　・コンパートメントアイテムが参照するモデルのフィールド値が変更された

[RegisterCompartmentItemTextValueCallback(string,string,string,Func<INode, int, string, IModel, string>,Action<INode, int, string, IModel, string>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterCompartmentItemTextValueCallback-2.md)

指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素の指定されたコンパートメントアイテムの値取得/設定コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・登録した値取得のコールバック関数が呼び出される契機は以下です。  
　・コンパートメントアイテムが表示された  
　・コンパートメントアイテムが参照するモデルのフィールド値が変更された  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

[RegisterGetCompartmentItemTextStyleCallback](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetCompartmentItemTextStyleCallback_.md)

指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素の指定されたコンパートメントアイテムのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・登録したコールバック関数が呼び出される契機は以下です。  
　・コンパートメントアイテムが表示された  
　・コンパートメントアイテムが参照するモデルのフィールド値が変更された  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

[RegisterGetCompartmentItemTextStyleCallback(IElementDef,string,Func<INode, int, string, IModel, IStyleProperty, object>,StyleAttributes)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetCompartmentItemTextStyleCallback-2.md)

スタイル属性を指定して、指定したエディタ要素定義より生成されるエディタ要素の指定されたコンパートメントアイテムのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・登録したコールバック関数が呼び出される契機は以下です。  
　・コンパートメントアイテムが表示された  
　・コンパートメントアイテムが参照するモデルのフィールド値が変更された

[RegisterGetCompartmentItemTextStyleCallback(IElementDef,string,Func<INode, int, string, IModel, IStyleProperty, object>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetCompartmentItemTextStyleCallback-1.md)

指定したエディタ要素定義より生成されるエディタ要素の指定されたコンパートメントアイテムのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・登録したコールバック関数が呼び出される契機は以下です。  
　・コンパートメントアイテムが表示された  
　・コンパートメントアイテムが参照するモデルのフィールド値が変更された

[RegisterGetCompartmentItemTextStyleCallback(string,string,string,Func<INode, int, string, IModel, IStyleProperty, object>,StyleAttributes)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetCompartmentItemTextStyleCallback-3.md)

スタイル属性を指定して、指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素の指定されたコンパートメントアイテムのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・登録したコールバック関数が呼び出される契機は以下です。  
　・コンパートメントアイテムが表示された  
　・コンパートメントアイテムが参照するモデルのフィールド値が変更された  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

[RegisterGetStyleCallback(IElementDef,Func<IEditorElement, IModel, IStyleProperty, object>,StyleAttributes)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetStyleCallback-3.md)

スタイル属性を指定して、指定したエディタ要素定義より生成されるエディタ要素のスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、当メソッドのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された

[RegisterGetStyleCallback(IElementDef,Func<IEditorElement, IModel, IStyleProperty, object>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetStyleCallback-1.md)

指定したエディタ要素定義より生成されるエディタ要素のスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、当メソッドのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された

[RegisterGetStyleCallback(string,string,Func<IEditorElement, IModel, IStyleProperty, object>,StyleAttributes)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetStyleCallback-4.md)

スタイル属性を指定して、指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素のスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、当メソッドのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

[RegisterGetStyleCallback(string,string,Func<IEditorElement, IModel, IStyleProperty, object>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetStyleCallback-2.md)

指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素のスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、当メソッドのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

[RegisterGetTextStyleCallback(IElementDef,TextTypes,Func<IShape, TextTypes, string, IModel, IStyleProperty, object>,StyleAttributes)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetTextStyleCallback-3.md)

スタイル属性を指定して、指定したエディタ要素定義より生成されるエディタ要素の指定されたテキストのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、RegisterGetStyleCallbackのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された

[RegisterGetTextStyleCallback(IElementDef,TextTypes,Func<IShape, TextTypes, string, IModel, IStyleProperty, object>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetTextStyleCallback-1.md)

指定したエディタ要素定義より生成されるエディタ要素の指定されたテキストのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、RegisterGetStyleCallbackのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された

[RegisterGetTextStyleCallback(string,string,TextTypes,Func<IShape, TextTypes, string, IModel, IStyleProperty, object>,StyleAttributes)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetTextStyleCallback-4.md)

スタイル属性を指定して、指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素の指定されたテキストのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、RegisterGetStyleCallbackのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

[RegisterGetTextStyleCallback(string,string,TextTypes,Func<IShape, TextTypes, string, IModel, IStyleProperty, object>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterGetTextStyleCallback-2.md)

指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素の指定されたテキストのスタイル値取得コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・スタイル値は以下の優先順位で取得されます。  
　1. スタイル値取得のコールバック関数値  
　2. ビューインスタンスで設定されているスタイル値  
　3. ビュー定義で定義されているスタイル値  
・タイトルテキストの文字色を変更する場合は、RegisterGetStyleCallbackのコールバック関数にての文字色を再定義します。  
・登録したコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルのフィールド値が変更された  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

[RegisterTextValueCallback(IElementDef,TextTypes,Func<IShape, TextTypes, string, IModel, string>,Action<IShape, TextTypes, string, IModel, string>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterTextValueCallback-1.md)

指定したエディタ要素定義より生成されるエディタ要素の指定されたテキストの値取得/設定コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・登録した値取得のコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルで、ビュー定義のパスと一致するフィールド値が変更された

[RegisterTextValueCallback(string,string,TextTypes,Func<IShape, TextTypes, string, IModel, string>,Action<IShape, TextTypes, string, IModel, string>)](_extension_api_NextDesign.Core_IViewDefinitions_methods_RegisterTextValueCallback-2.md)

指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素の指定されたテキストの値取得/設定コールバック関数を登録します。  
・エクステンションのActivate時に呼び出すことを推奨します。  
・登録した値取得のコールバック関数が呼び出される契機は以下です。  
　・エディタ要素が表示された  
　・エディタ要素が参照するモデルで、ビュー定義のパスと一致するフィールド値が変更された  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。

[UnregisterStyleCallback(IElementDef)](_extension_api_NextDesign.Core_IViewDefinitions_methods_UnregisterStyleCallback-1.md)

指定したエディタ要素定義より生成されるエディタ要素の全てのコールバック関数を登録解除します。  
・エクステンションのDeactivate時に呼び出すことを推奨します。

[UnregisterStyleCallback(string,string)](_extension_api_NextDesign.Core_IViewDefinitions_methods_UnregisterStyleCallback-2.md)

指定したクラスから特定できるエディタ要素定義より生成されるエディタ要素の全てのコールバック関数を登録解除します。  
・エクステンションのDeactivate時に呼び出すことを推奨します。  
  
なお、指定したクラスからエディタ要素定義が特定できなかった場合は何も行われません。