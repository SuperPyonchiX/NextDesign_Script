IExtensions インタフェース
===================

名前空間: NextDesign.Desktop

説明​
-----------------------

エクステンション情報一覧を提供します。

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

[AllExtensionInfos](_extension_api_NextDesign.Desktop_IExtensions_properties_AllExtensionInfos.md)

エクステンション情報一覧

メソッド​
-----------------------------

名前

説明

[GetExtensionInfo](_extension_api_NextDesign.Desktop_IExtensions_methods_GetExtensionInfo.md)

指定された識別子のエクステンション情報を取得します。

[Reload](_extension_api_NextDesign.Desktop_IExtensions_methods_Reload.md)

指定された識別子のエクステンションをリロードします。  
reloadManifest に true を指定するとマニフェストを再ロードして、リボン等のUIも再構築します。  
reloadManifest に false を指定した場合は、コード（ソースコード）、言語リソースなどのメモリ保持データをクリアして再ロードします。この時、リボン等のUIは更新されません。

[ReloadAll](_extension_api_NextDesign.Desktop_IExtensions_methods_ReloadAll.md)

すべてのエクステンションをリロードします。  
reloadManifest に true を指定するとマニフェストを再ロードして、リボン等のUIも再構築します。  
reloadManifest に false を指定した場合は、コード（ソースコード）、言語リソースなどのメモリ保持データをクリアして再ロードします。この時、リボン等のUIは更新されません。