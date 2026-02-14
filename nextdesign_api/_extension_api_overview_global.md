グローバル
=====

説明​
-----------------------

エクステンションの実行環境や実行状態にアクセスするAPI群です。

エリアに属するAPI​
-----------------------------------------------

名前

説明

[IApplication](_extension_api_NextDesign.Desktop_IApplication.md)

エクステンション実行環境に与える共有変数です。  
スクリプトでは、この変数を通して、アプリケーションの様々な情報にアクセスすることができます。

[IContext](_extension_api_NextDesign.Desktop_IContext.md)

エクステンションの実行時には、コンテキストが与えられます。  
エクステンションの実装では、このコンテキストを通して共有変数や実行中のエクステンション設定情報を参照することができます。

[IContextOption](_extension_api_NextDesign.Desktop_IContextOption.md)

コンテキストが有効な期間におけるオプション定義情報です。  
各種API呼び出し結果（APIの振る舞い）の変更や、APIの振る舞いに影響する一時的な設定情報を記録します。

[IObject](_extension_api_NextDesign.Core_IObject.md)

識別可能なオブジェクトを表します。

[IExtensionInfo](_extension_api_NextDesign.Desktop_IExtensionInfo.md)

エクステンション情報を提供します。

[IEnv](_extension_api_NextDesign.Desktop_IEnv.md)

アプリケーション実行環境へのアクセス手段を提供します。

[IExtensions](_extension_api_NextDesign.Desktop_IExtensions.md)

エクステンション情報一覧を提供します。

[IScriptParams](_extension_api_NextDesign.Desktop_IScriptParams.md)

スクリプトのパラメータを提供します。

[IExtension](_extension_api_NextDesign.Extension_IExtension.md)

エクステンションのエントリーポイント実装用のインタフェースです。

[IEventDispatcher](_extension_api_NextDesign.Extension_IEventDispatcher.md)

イベントの転送オブジェクトのインタフェースです。  
必要に応じて、エクステンション側でエントリポイント（IExtensionの実装クラス）に対して追加実装することができます。  
  
Next Design では、イベント処理が要求された際に、エントリポイントがこのインタフェースを実装している場合に限り、イベントのディスパッチを要求します。  
エントリポイントがこのインタフェースを実装しない場合は、従来通りエントリポイントからイベントに対応する関数を探索して呼び出します。

[ICommandDispatcher](_extension_api_NextDesign.Extension_ICommandDispatcher.md)

コマンド転送オブジェクトのインタフェースです。  
必要に応じて、エクステンション側でエントリポイント（IExtensionの実装クラス）に対して追加実装することができます。  
  
Next Design では、コマンド処理が要求された際に、エントリポイントがこのインタフェースを実装している場合に限り、コマンドのディスパッチを要求します。  
エントリポイントがこのインタフェースを実装しない場合は、従来通りエントリポイントからコマンドに対応する関数を探索して呼び出します。