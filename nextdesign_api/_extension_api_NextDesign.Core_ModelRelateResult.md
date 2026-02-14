ModelRelateResult インタフェース
=========================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

モデル関連付け可否の結果を表すオブジェクトです。

Next Designは、この結果オブジェクトを用いて関連付け可否を判定します。

※この結果にCapabilityResults.Failを設定してもエラーとはならず、CapabilityResults.Ignoreと同じ扱いになります。

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

[CanRelate](_extension_api_NextDesign.Core_ModelRelateResult_properties_CanRelate.md)

関連付け可否

[GuideText](_extension_api_NextDesign.Core_ModelRelateResult_properties_GuideText.md)

ガイドテキスト  
  
・nullを設定した場合、アプリケーション既定のガイドテキストを表示する  
　※関連付け可否の結果がfalse、かつガイドテキストがnullの場合、関連付けは行えないが、アプリケーション既定のガイドテキストが表示されることになる。  
・空文字を設定した場合、ガイドテキストは表示されない