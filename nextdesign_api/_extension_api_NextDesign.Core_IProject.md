IProject インタフェース
================

名前空間: NextDesign.Core

説明​
-----------------------

プロジェクト情報へのアクセス手段を提供します。

所属エリア​
--------------------------------

名前

説明

[ワークスペース・プロジェクト](_extension_api_overview_workspace.md)

アプリケーションの作業領域やアプリケーションで開いているプロジェクトにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[IModel](_extension_api_NextDesign.Core_IModel.md)

NextDesignの設計モデル情報へのアクセス手段を提供します。

プロパティ​
--------------------------------

名前

説明

[CanRedo](_extension_api_NextDesign.Core_IProject_properties_CanRedo.md)

リドゥ操作が実行可能であるか調べます。

[CanUndo](_extension_api_NextDesign.Core_IProject_properties_CanUndo.md)

アンドゥ操作が実行可能であるか調べます。

[DesignModel](_extension_api_NextDesign.Core_IProject_properties_DesignModel.md)

設計モデルルート (モデルナビゲータのルートモデル)

[EditingCapabilityProviderRegistry](_extension_api_NextDesign.Core_IProject_properties_EditingCapabilityProviderRegistry.md)

編集支援機能レジストリ

[IsProductLineSupported](_extension_api_NextDesign.Core_IProject_properties_IsProductLineSupported.md)

このプロジェクトでプロダクトライン開発がサポートされているか

[OutputModelPaths](_extension_api_NextDesign.Core_IProject_properties_OutputModelPaths.md)

永続化時に参照先モデルのパスを出力するか

[Path](_extension_api_NextDesign.Core_IProject_properties_Path.md)

プロジェクトのパス  
新規に作成したプロジェクトの場合は null となります。

[ProductLineModel](_extension_api_NextDesign.Core_IProject_properties_ProductLineModel.md)

プロダクトライン開発支援モデル

[Profile](_extension_api_NextDesign.Core_IProject_properties_Profile.md)

プロジェクトで使用されているプロファイル

[UnitManager](_extension_api_NextDesign.Core_IProject_properties_UnitManager.md)

プロジェクトユニット情報マネージャ

メソッド​
-----------------------------

名前

説明

[AddNewRootModel(IPackage,string,bool)](_extension_api_NextDesign.Core_IProject_methods_AddNewRootModel-2.md)

プロジェクトに指定したクラスの新しいモデルを追加します。  
指定クラスは、スコープで指定したパッケージ配下から特定します。  
指定したクラスが抽象クラスの場合でもインスタンス化を許容し、該当フィールドの末尾の要素として追加されます。  
追加されたモデルは、モデルナビゲータ上で表示されるプロジェクトノード以下(プロジェクト直下)の要素として保持されます。  
なお、あいまい一致とするときに、一致するクラスが複数ある場合、一番最初に見つかった型互換のあるクラスのインスタンスを追加します。  
また、指定されたクラスの「プロジェクト直下に配置できるか」がチェックされていなくても追加できます。

[AddNewRootModel(string,bool)](_extension_api_NextDesign.Core_IProject_methods_AddNewRootModel-1.md)

プロジェクトに指定したクラスの新しいモデルを追加します。  
指定したクラスが抽象クラスの場合でもインスタンス化を許容し、該当フィールドの末尾の要素として追加されます。  
追加されたモデルは、モデルナビゲータ上で表示されるプロジェクトノード以下(プロジェクト直下)の要素として保持されます。  
なお、あいまい一致とするときに、一致するクラスが複数ある場合、一番最初に見つかった型互換のあるクラスのインスタンスを追加します。  
また、指定されたクラスの「プロジェクト直下に配置できるか」がチェックされていなくても追加できます。

[BeginUndoTransaction](_extension_api_NextDesign.Core_IProject_methods_BeginUndoTransaction.md)

編集を開始して、アンドゥトランザクションを生成します。  
アンドゥトランザクション内で実施された編集内容は、1回のアンドゥ/リドゥ操作の対象となります。

[CreateModelAccessPolicy](_extension_api_NextDesign.Core_IProject_methods_CreateModelAccessPolicy.md)

モデルアクセスのポリシーを作成します。  
ポリシーが有効な範囲において行われたモデルへのアクセスには、ポリシーの設定が適用されます。

[CreateProductLineModel](_extension_api_NextDesign.Core_IProject_methods_CreateProductLineModel.md)

このプロジェクトにプロダクトライン開発支援モデルを作成し、プロダクトライン開発可能とします。  
プロダクトライン開発支援モデルを作成することで、次のモデルが生成されます。  
\- プロダクトライン開発支援モデル  
\- 空のフィーチャモデル  
\- 空のコンフィグレーションモデル  
  
このプロジェクトが既にプロダクトライン開発をサポート済みの場合、このメソッドの呼び出しは無視されます。

[GetModelById](_extension_api_NextDesign.Core_IProject_methods_GetModelById.md)

このプロジェクトから指定された識別子のモデルを取得します。  
指定されたモデルが見つからない場合は null を返します。  
なお、この呼び出しでは、関連は取得できません。関連を取得する場合は、GetRelationshipById()を使用してください。  
  
この呼び出しでは、プロジェクト読み込み後に削除されたモデルも対象となります。  
取得したモデルが削除されているかは、IModel.IsDeleted で評価してください。

[GetModelByPath(string,string)](_extension_api_NextDesign.Core_IProject_methods_GetModelByPath-2.md)

指定された基点要素の識別子を持つモデルから、指定された相対パスのモデルを取得します。  
指定したモデル階層パスのモデルが存在しない場合は null を返します。  
  
なお、一致するモデル階層パスが複数ある場合、一番最初に見つかったモデルを返します。

[GetModelByPath(string)](_extension_api_NextDesign.Core_IProject_methods_GetModelByPath-1.md)

このプロジェクトから指定されたモデル階層パスのモデルを取得します。  
指定したモデル階層パスのモデルが存在しない場合は null を返します。  
  
なお、一致するモデル階層パスが複数ある場合、一番最初に見つかったモデルを返します。  
また、指定するモデル階層パスは IModel の ModelPath で規定する文字列を使用できます。

[GetRelationshipById](_extension_api_NextDesign.Core_IProject_methods_GetRelationshipById.md)

このプロジェクトから指定された識別子の関連を取得します。  
指定された関連が見つからない場合は null を返します。  
  
この呼び出しでは、プロジェクト読み込み後に削除された関連も対象となります。  
取得した関連が削除されているかは、IRelationship.IsDeleted を評価してください。

[GetUndoSuspendScope](_extension_api_NextDesign.Core_IProject_methods_GetUndoSuspendScope.md)

アンドゥをサスペンドするスコープを取得します。  
このスコープが有効な範囲において行われた編集操作はアンドゥスタックへ記録されません。

[HasUnsavedChanges](_extension_api_NextDesign.Core_IProject_methods_HasUnsavedChanges.md)

未保存の変更があるかを調べます。  
IsDirty と異なり、プロジェクトがダーティ状態であっても変更内容が保存対象外であればFalseを返します。

[ImportProfile](_extension_api_NextDesign.Core_IProject_methods_ImportProfile.md)

指定されたパスのプロファイルをインポートします。

[Redo](_extension_api_NextDesign.Core_IProject_methods_Redo.md)

直近で取り消された編集操作を再実行します。  
直近で取り消された編集操作がない場合は何もおこないません。

[Undo](_extension_api_NextDesign.Core_IProject_methods_Undo.md)

直近の編集操作を取り消します。  
直近の編集操作がない場合は何も行いません。

拡張メソッド​
-----------------------------------

名前

説明

[GetRootChildren](_extension_api_NextDesign.Core_ProjectExtensions_methods_GetRootChildren.md)

設計モデルルート (モデルナビゲータのルートモデル) の子一覧を取得します。