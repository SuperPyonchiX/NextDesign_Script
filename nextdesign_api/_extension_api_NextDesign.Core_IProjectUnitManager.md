IProjectUnitManager インタフェース
===========================

名前空間: NextDesign.Core

説明​
-----------------------

プロジェクトの物理ファイル構成管理オブジェクトです。

所属エリア​
--------------------------------

名前

説明

[ワークスペース・プロジェクト](_extension_api_overview_workspace.md)

アプリケーションの作業領域やアプリケーションで開いているプロジェクトにアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[ModelUnits](_extension_api_NextDesign.Core_IProjectUnitManager_properties_ModelUnits.md)

このプロジェクトで管理するモデルユニット情報。  
プロジェクトの管理ユニットがない場合は空のコレクションを返します。  
プロジェクトユニットは含まれません。

[ProjectUnit](_extension_api_NextDesign.Core_IProjectUnitManager_properties_ProjectUnit.md)

このプロジェクトのユニット（物理ファイル）情報。  
プロジェクトを保存していない場合は null を返します。

メソッド​
-----------------------------

名前

説明

[AddExternalUnits](_extension_api_NextDesign.Core_IProjectUnitManager_methods_AddExternalUnits.md)

指定されたファイルをモデルユニットとして参照登録します。

[ExportModelUnit](_extension_api_NextDesign.Core_IProjectUnitManager_methods_ExportModelUnit.md)

指定されたモデルユニットを指定したファイルパスでエクスポートします。

[ExportModelUnits](_extension_api_NextDesign.Core_IProjectUnitManager_methods_ExportModelUnits.md)

指定された全てのモデルユニットを指定したフォルダパスにエクスポートします。  
エクスポート先のファイル名は元のユニットファイル名と同一となります。

[ImportModelUnits(IEnumerable<string>,string)](_extension_api_NextDesign.Core_IProjectUnitManager_methods_ImportModelUnits-2.md)

指定されたファイルをモデルユニットとしてインポートします。  
インポート先として指定されたフォルダが存在しない場合は、該当フォルダまでのフォルダを作成します。  
ユニットファイルが、インポート先として指定されたフォルダ直下に存在しない場合は、該当のファイルをそのフォルダ直下にコピーしてインポートします。  
ユニットファイルが、インポート先として指定されたフォルダ直下に存在する場合は、そのファイルをインポートします。

[ImportModelUnits(IEnumerable<string>)](_extension_api_NextDesign.Core_IProjectUnitManager_methods_ImportModelUnits-1.md)

指定されたファイルをモデルユニットとしてインポートします。  
ユニットファイルが、Modelsフォルダ直下に存在しない場合は、該当のファイルをModelsフォルダ直下にコピーしてインポートします。  
ユニットファイルが、Modelsフォルダ直下に存在する場合は、そのファイルをインポートします。

[SplitModelUnit(IModel,string,string)](_extension_api_NextDesign.Core_IProjectUnitManager_methods_SplitModelUnit-2.md)

指定されたモデルを指定された名前のユニットファイルに分割します。  
分割するユニットファイルは、指定されたフォルダへ追加されます。

[SplitModelUnit(IModel,string)](_extension_api_NextDesign.Core_IProjectUnitManager_methods_SplitModelUnit-1.md)

指定されたモデルを指定された名前のユニットファイルに分割します。

[SplitModelUnits(IEnumerable<IModel>,string)](_extension_api_NextDesign.Core_IProjectUnitManager_methods_SplitModelUnits-2.md)

指定された全てのモデルをユニットファイルに分割します。  
分割されたユニットファイルのファイル名は、モデル名を用いて重複しないファイル名を自動的に決定します。  
分割するユニットファイルは、指定されたフォルダへ追加されます。

[SplitModelUnits(IEnumerable<IModel>)](_extension_api_NextDesign.Core_IProjectUnitManager_methods_SplitModelUnits-1.md)

指定された全てのモデルをユニットファイルに分割します。  
分割されたユニットファイルのファイル名は、モデル名を用いて重複しないファイル名を自動的に決定します。

[UnifyModelUnit](_extension_api_NextDesign.Core_IProjectUnitManager_methods_UnifyModelUnit.md)

指定されたユニットを親ユニットに統合します。  
統合先の親ユニットは、指定したユニットの基点要素に対して、モデル構造上の親要素が格納されたユニットとなります。

[UnifyModelUnits](_extension_api_NextDesign.Core_IProjectUnitManager_methods_UnifyModelUnits.md)

指定されたすべてのユニットを親ユニットに統合します。