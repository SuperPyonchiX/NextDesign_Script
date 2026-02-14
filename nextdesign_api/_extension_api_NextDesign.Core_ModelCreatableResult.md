ModelCreatableResult インタフェース
============================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-------------------------

モデル作成時の選択対象の結果を表すオブジェクトです。

Next Designは、この結果オブジェクトを用いて選択対象を構築します。

※この結果にCapabilityResults.Failを設定してもエラーとはならず、モデル作成時の選択対象が空になります。

所属エリア​
--------------------------------

名前

説明

[EditingCapability](_extension_api_overview_editing-capability.md)

EditingCapabilityにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[ResultBase](_extension_api_NextDesign.Core_ResultBase.md)

編集支援機能で使用するパラメータオブジェクトの基底クラスです。

プロパティ​
--------------------------------

名前

説明

[this](_extension_api_NextDesign.Core_ModelCreatableResult_properties_this.md)

指定されたフィールドで追加できるクラス候補一覧（インデクサ）

メソッド​
-----------------------------

名前

説明

[AddCreatableClasses](_extension_api_NextDesign.Core_ModelCreatableResult_methods_AddCreatableClasses.md)

指定フィールドでクラス候補一覧を追加します。

[GetCreatableClasses](_extension_api_NextDesign.Core_ModelCreatableResult_methods_GetCreatableClasses.md)

指定フィールドでクラス候補一覧を取得します。

[RemoveCreatableClasses](_extension_api_NextDesign.Core_ModelCreatableResult_methods_RemoveCreatableClasses.md)

指定フィールドでクラス候補一覧を削除します。