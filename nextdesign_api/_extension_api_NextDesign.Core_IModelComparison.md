IModelComparison インタフェース
========================

名前空間: NextDesign.Core

説明​
-----------------------

比較処理単位情報へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[モデル差分・比較](_extension_api_overview_model-diff.md)

モデルの比較操作や差分情報にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[AfterProject](_extension_api_NextDesign.Core_IModelComparison_properties_AfterProject.md)

変更後のプロジェクト情報

[BeforeProject](_extension_api_NextDesign.Core_IModelComparison_properties_BeforeProject.md)

変更前のプロジェクト情報

[Differences](_extension_api_NextDesign.Core_IModelComparison_properties_Differences.md)

すべての差分情報

[Matches](_extension_api_NextDesign.Core_IModelComparison_properties_Matches.md)

すべての比較情報

メソッド​
-----------------------------

名前

説明

[GetDifferencedMatches](_extension_api_NextDesign.Core_IModelComparison_methods_GetDifferencedMatches.md)

モデル比較の結果、差分を抽出した比較情報を取得します。

[GetMatch](_extension_api_NextDesign.Core_IModelComparison_methods_GetMatch.md)

指定されたモデルの比較情報を取得します。

[RequestRecompute](_extension_api_NextDesign.Core_IModelComparison_methods_RequestRecompute.md)

指定されたモデルに対して、差分比較の再実行を要求します。  
このメソッドを呼び出しても、対象モデルの比較はすぐに実行されません。実際に比較が実行されるタイミングは、エクステンションによるコマンドやイベントの一連の処理が完了した後になります。

関連項目​
-----------------------------

名前

説明

モデルの比較結果を確認する

APIを通してNextDesignの各種モデル情報を比較した結果差分を確認します。