ITraceNode インタフェース
==================

名前空間: NextDesign.Desktop

説明​
-----------------------

トレース対象のモデルに対応するノード情報へのアクセス手段を提供します。

所属エリア​
----------------------------------

名前

説明

[トレース](_extension_api_overview_traceability.md)

トーレス情報にアクセスするAPI群です。

プロパティ​
--------------------------------

名前

説明

[Children](_extension_api_NextDesign.Desktop_ITraceNode_properties_Children.md)

このノードのツリー階層上の子ノードのコレクションを取得します。  
モデルフィルタや関連フィルタが適用されている場合、フィルタが適用された状態で返します。

[ExcludedDirection](_extension_api_NextDesign.Desktop_ITraceNode_properties_ExcludedDirection.md)

トレース対象のモデルに設定されたトレース除外の方向  
\- "Source" : 導出元への関連を除外する  
\- "Target" : 導出先への関連を除外する  
\- "Both" : 導出元、および導出先への関連を除外する  
\- "None" : 除外しない

[Expanded](_extension_api_NextDesign.Desktop_ITraceNode_properties_Expanded.md)

トレースビューにおけるツリーの展開状態  
ツリーが展開されている場合は、true を返します。  
  
未展開のノードに対して、展開状態を true に設定した場合、すべての親ノードの展開状態を true に設定します。

[IsSelected](_extension_api_NextDesign.Desktop_ITraceNode_properties_IsSelected.md)

このノードの選択状態

[Model](_extension_api_NextDesign.Desktop_ITraceNode_properties_Model.md)

トレース対象のモデル

[Parent](_extension_api_NextDesign.Desktop_ITraceNode_properties_Parent.md)

このノードのツリー階層上の親ノード

[SourceNodes](_extension_api_NextDesign.Desktop_ITraceNode_properties_SourceNodes.md)

トレース対象のモデルの導出元(関連先)要素が対応するトレース元レーンのノードのコレクションを取得します。  
モデルフィルタや関連フィルタが適用されている場合、フィルタが適用された状態で返します。

[TargetNodes](_extension_api_NextDesign.Desktop_ITraceNode_properties_TargetNodes.md)

トレース対象のモデルの導出先(関連元)要素が対応するトレース先レーンのノードのコレクションを取得します。  
モデルフィルタや関連フィルタが適用されている場合、フィルタが適用された状態で返します。

[TraceLane](_extension_api_NextDesign.Desktop_ITraceNode_properties_TraceLane.md)

このノードが含まれるレーン（系列）情報