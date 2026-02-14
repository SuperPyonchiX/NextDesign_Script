IModelUnit インタフェース
==================

名前空間: NextDesign.Core

説明​
-----------------------

モデルユニット情報のアクセスオブジェクトです。

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

[AbsolutePath](_extension_api_NextDesign.Core_IModelUnit_properties_AbsolutePath.md)

物理ファイルの絶対パス

[HasLoadError](_extension_api_NextDesign.Core_IModelUnit_properties_HasLoadError.md)

このユニットの読み込みエラーの有無を調べます。

[IsExternalUnit](_extension_api_NextDesign.Core_IModelUnit_properties_IsExternalUnit.md)

このユニットが外部ユニットであるか調べます。  
外部ユニットは、参照登録によって追加され、プロジェクトフォルダ外で管理されます。

[IsReadonly](_extension_api_NextDesign.Core_IModelUnit_properties_IsReadonly.md)

このユニットが読み取り専用ユニットであるか調べます。

[Loaded](_extension_api_NextDesign.Core_IModelUnit_properties_Loaded.md)

このユニットの内容がプロジェクトに読み込み済みであるか調べます。

[Name](_extension_api_NextDesign.Core_IModelUnit_properties_Name.md)

ユニット名

[PhysicalFileExits](_extension_api_NextDesign.Core_IModelUnit_properties_PhysicalFileExits.md)

物理ファイルが存在するか調べます。

[ScmStatus](_extension_api_NextDesign.Core_IModelUnit_properties_ScmStatus.md)

構成管理状態。  
プロジェクトが構成管理システムと連携していない場合は null を返します。

[TopElementId](_extension_api_NextDesign.Core_IModelUnit_properties_TopElementId.md)

このユニットにおける基点要素の識別子。  
基点要素がない場合は null を返します。  
  
通常、基点要素はユニット種別により次の要素となります。  
\- "Project" : プロジェクト  
\- "Model" : ユニットに分割したモデル  
\- "Profile" : プロファイル  
\- 上記以外 : なし

[Type](_extension_api_NextDesign.Core_IModelUnit_properties_Type.md)

ユニット種別  
\- "Project" : プロジェクトユニット  
\- "Model" : モデルユニット  
\- "Profile" : プロファイルユニット  
\- "Library" : ライブラリ  
\- "IndexCache" : インデックス  
\- "Unknown" : 不明

[UnitPath](_extension_api_NextDesign.Core_IModelUnit_properties_UnitPath.md)

ユニットパス。  
通常はプロジェクトフォルダからの相対パスとなります。  
参照登録によって追加されたプロジェクトフォルダ外のユニットの場合は絶対パスを返します。