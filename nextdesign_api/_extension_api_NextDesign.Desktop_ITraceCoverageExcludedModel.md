ITraceCoverageExcludedModel インタフェース
===================================

名前空間: NextDesign.Desktop

説明​
-----------------------

トレース除外対象のモデル情報  
トレース除外対象と設定されたモデルは、トレース網羅率の計算対象から除外されます。

所属エリア​
--------------------------------

名前

説明

[トレース](_extension_api_overview_traceability.md)

トーレス情報にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Direction](_extension_api_NextDesign.Desktop_ITraceCoverageExcludedModel_properties_Direction.md)

トレースの除外方向  
\- "Source" : 導出元への関連を除外する  
\- "Target" : 導出先への関連を除外する  
\- "Both" : 導出元、および導出先への関連を除外する

[Model](_extension_api_NextDesign.Desktop_ITraceCoverageExcludedModel_properties_Model.md)

除外対象のモデル  
該当モデルが削除済みの場合は、null を返します。  
また、該当モデルがプロキシ化されている場合は、モデルのプロキシ情報を返します。

[ModelId](_extension_api_NextDesign.Desktop_ITraceCoverageExcludedModel_properties_ModelId.md)

除外対象のモデルの識別子

[Reason](_extension_api_NextDesign.Desktop_ITraceCoverageExcludedModel_properties_Reason.md)

除外理由

メソッド​
-----------------------------

名前

説明

[Delete](_extension_api_NextDesign.Desktop_ITraceCoverageExcludedModel_methods_Delete.md)

このトレース除外対象のモデル情報を削除します。