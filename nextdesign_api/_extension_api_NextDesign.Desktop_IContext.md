IContext インタフェース
================

名前空間: NextDesign.Desktop

説明​
-----------------------

エクステンションの実行時には、コンテキストが与えられます。  
エクステンションの実装では、このコンテキストを通して共有変数や実行中のエクステンション設定情報を参照することができます。

所属エリア​
--------------------------------

名前

説明

[グローバル](_extension_api_overview_global.md)

エクステンションの実行環境や実行状態にアクセスするAPI群です。

派生先​
--------------------------

名前

説明

[IEventContext](_extension_api_NextDesign.Desktop_IEventContext.md)

イベントの実行コンテキストへのアクセス手段を提供します。

[ICommandContext](_extension_api_NextDesign.Desktop_ICommandContext.md)

コマンドの実行コンテキストを提供します。

プロパティ​
--------------------------------

名前

説明

[App](_extension_api_NextDesign.Desktop_IContext_properties_App.md)

エクステンション実行環境の共有変数

[Application](_extension_api_NextDesign.Desktop_IContext_properties_Application.md)

\[Obsolete\] エクステンション実行環境の共有変数

[ContextOption](_extension_api_NextDesign.Desktop_IContext_properties_ContextOption.md)

コンテキストオプション  
このコンテキストが有効な期間におけるオプション定義

[ExtensionInfo](_extension_api_NextDesign.Desktop_IContext_properties_ExtensionInfo.md)

現在有効なエクステンション情報

メソッド​
-----------------------------

名前

説明

[GetProperties](_extension_api_NextDesign.Desktop_IContext_methods_GetProperties.md)

プロパティ一覧を取得します。

[GetProperty](_extension_api_NextDesign.Desktop_IContext_methods_GetProperty.md)

指定された識別名のプロパティ値を取得します。  
該当する識別名が存在しない場合は null を返します。

[GetPropertyNames](_extension_api_NextDesign.Desktop_IContext_methods_GetPropertyNames.md)

プロパティの識別名一覧を取得します。

[GetResourceString](_extension_api_NextDesign.Desktop_IContext_methods_GetResourceString.md)

指定されたリソースキーの文字列を取得します。  
リソースは、このコンテキストにおいて有効なエクステンションのリソースファイルから、以下の優先度でリソースを特定して検索します。  
1\. 現在のアプリケーションの実行言語  
2\. 日本語リソース  
3\. その他の最初に見つかった言語リソース  
  
なお、該当するリソースキーが存在しない場合、リソースキーとしては識別されず、指定されたキーがそのまま返ります。  
また、リソースキーに null または空文字列が指定された場合は、空文字列を返します。  

[GetResourceString1](_extension_api_NextDesign.Desktop_IContext_methods_GetResourceString1.md)

指定されたリソースキーの文字列を取得します。  
このときリソース文字列で定義された置換子"{0}"を、引数で指定された param1 の文字列表現に置き換えて返します。  
なお、リソースパラメータの置換についてはC#のString.Formatに準拠します。  
また、リソース文字列特定方法、およびリソースキーの指定の詳細は、GetResourceString()を参照してください。

[GetResourceString2](_extension_api_NextDesign.Desktop_IContext_methods_GetResourceString2.md)

指定されたリソースキーの文字列を取得します。  
このときリソース文字列で定義された置換子"{0},{1}"を、それぞれ引数で指定された param1, param2 の文字列表現に置き換えて返します。  
なお、リソースパラメータの置換についてはC#のString.Formatに準拠します。  
また、リソース文字列特定方法、およびリソースキーの指定の詳細は、GetResourceString()を参照してください。

[GetResourceString3](_extension_api_NextDesign.Desktop_IContext_methods_GetResourceString3.md)

指定されたリソースキーの文字列を取得します。  
このときリソース文字列で定義された置換子"{0},{1},{2}"を、それぞれ引数で指定された param1, param2, param3 の文字列表現に置き換えて返します。  
なお、リソースパラメータの置換についてはC#のString.Formatに準拠します。  
また、リソース文字列特定方法、およびリソースキーの指定の詳細は、GetResourceString()を参照してください。

[HasProperty](_extension_api_NextDesign.Desktop_IContext_methods_HasProperty.md)

指定された識別名のプロパティ値があるか調べます。  
指定された識別名のプロパティ値がある場合はTrueを返します。

[RemoveProperty](_extension_api_NextDesign.Desktop_IContext_methods_RemoveProperty.md)

指定された識別子名のプロパティ値を削除します。  
該当する識別名のプロパティ値が存在しない場合は、何も行われません。

[SetProperty](_extension_api_NextDesign.Desktop_IContext_methods_SetProperty.md)

指定された識別名のプロパティ値を設定します。  
既に存在する識別名が指定された場合は、プロパティ値を与えられた値で更新します。