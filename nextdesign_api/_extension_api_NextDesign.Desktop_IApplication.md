IApplication インタフェース
====================

名前空間: NextDesign.Desktop

説明​
-------------------------

エクステンション実行環境に与える共有変数です。  
スクリプトでは、この変数を通して、アプリケーションの様々な情報にアクセスすることができます。

所属エリア​
--------------------------------

名前

説明

[グローバル](_extension_api_overview_global.md)

エクステンションの実行環境や実行状態にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Commands](_extension_api_NextDesign.Desktop_IApplication_properties_Commands.md)

コマンドマネージャ

[CustomUI](_extension_api_NextDesign.Desktop_IApplication_properties_CustomUI.md)

独自拡張のユーザインタフェースのレジストリです。

[Diff](_extension_api_NextDesign.Desktop_IApplication_properties_Diff.md)

差分抽出オブジェクト

[EditionId](_extension_api_NextDesign.Desktop_IApplication_properties_EditionId.md)

エディション識別名

[EditionShortName](_extension_api_NextDesign.Desktop_IApplication_properties_EditionShortName.md)

エディション短縮名

[Env](_extension_api_NextDesign.Desktop_IApplication_properties_Env.md)

アプリケーション実行環境

[Errors](_extension_api_NextDesign.Desktop_IApplication_properties_Errors.md)

エラー一覧

[Extensions](_extension_api_NextDesign.Desktop_IApplication_properties_Extensions.md)

エクステンション管理

[FileUtil](_extension_api_NextDesign.Desktop_IApplication_properties_FileUtil.md)

ファイル操作ユーティリティオブジェクト

[Output](_extension_api_NextDesign.Desktop_IApplication_properties_Output.md)

出力

[Resources](_extension_api_NextDesign.Desktop_IApplication_properties_Resources.md)

リソース操作ユーティリティ

[Search](_extension_api_NextDesign.Desktop_IApplication_properties_Search.md)

検索マネージャ

[Util](_extension_api_NextDesign.Desktop_IApplication_properties_Util.md)

汎用ユーティリティオブジェクト

[Version](_extension_api_NextDesign.Desktop_IApplication_properties_Version.md)

アプリケーションのバージョン番号

[Window](_extension_api_NextDesign.Desktop_IApplication_properties_Window.md)

ワークスペースウィンドウのUI操作オブジェクト

[Workspace](_extension_api_NextDesign.Desktop_IApplication_properties_Workspace.md)

ワークスペース

メソッド​
-----------------------------

名前

説明

[CreateCommandParams](_extension_api_NextDesign.Desktop_IApplication_methods_CreateCommandParams.md)

コマンドパラメータを作成します。

[CreateScriptParams](_extension_api_NextDesign.Desktop_IApplication_methods_CreateScriptParams.md)

スクリプトパラメータを作成します。

[CreateSearch](_extension_api_NextDesign.Desktop_IApplication_methods_CreateSearch.md)

\[Obsolete\] 検索オブジェクトを生成します。

[ExecuteCommand](_extension_api_NextDesign.Desktop_IApplication_methods_ExecuteCommand.md)

指定された識別子のコマンドを実行します。

[ExecuteScript](_extension_api_NextDesign.Desktop_IApplication_methods_ExecuteScript.md)

指定されたスクリプトファイルを読み込んで実行します。

[ExecuteScriptCode(string,string,IScriptParams)](_extension_api_NextDesign.Desktop_IApplication_methods_ExecuteScriptCode-1.md)

与えられたスクリプトコードを実行します。

[ExecuteScriptCode(string,string,string,IScriptParams)](_extension_api_NextDesign.Desktop_IApplication_methods_ExecuteScriptCode-2.md)

与えられたスクリプトコードを実行します。  
  
basePathを指定することで、スクリプトコード内で使用する外部ファイルに相対パスを利用できるようになります。  
未指定の場合は、現在開いているプロジェクトが保存済みであれば、外部ファイルの探索起点にプロジェクトファイルのパスが使用されます。

[GetFeatureValue](_extension_api_NextDesign.Desktop_IApplication_methods_GetFeatureValue.md)

現在のエディションにおける、指定したフィーチャの指定したキー（属性）値を取得します。  
指定したフィーチャの有効/無効に関係なく値を取得できます。  
  
なお、指定したフィーチャが見つからない場合、値はnullを返します。  
また、指定したフィーチャキー名が見つからない場合、値はnullを返します。

[IsFeatureEnabled](_extension_api_NextDesign.Desktop_IApplication_methods_IsFeatureEnabled.md)

現在のエディションにおいて、指定したフィーチャが有効であるか調べます。

[Quit](_extension_api_NextDesign.Desktop_IApplication_methods_Quit.md)

アプリケーションを終了します。

[Restart](_extension_api_NextDesign.Desktop_IApplication_methods_Restart.md)

アプリケーションの再起動を要求します。  
再起動要求時にプロジェクトを開いていた場合は、再起動後に同じプロジェクトを開きます。  
なお、再起動に失敗した場合は false を返します。

[ThrowUserException](_extension_api_NextDesign.Desktop_IApplication_methods_ThrowUserException.md)

ユーザー例外をスローします。  
エクステンションの実行時例外処理において、明示的に例外を通知したい場合にこのメソッドを利用できます。

関連項目​
-----------------------------

名前

説明

共有変数

エクステンション実行環境に与える共有変数です。  
スクリプトでは、この変数を通して、アプリケーションの様々な情報にアクセスすることができます。