IConstraint インタフェース
===================

名前空間: NextDesign.Core

説明​
-----------------------

制約へのアクセスオブジェクトです。

所属エリア​
--------------------------------

名前

説明

[プロファイル](_extension_api_overview_profile.md)

プロファイルにアクセスするAPI群です。

継承元​
--------------------------

名前

説明

[INamedElement](_extension_api_NextDesign.Core_INamedElement.md)

名前付け可能要素を表します。

プロパティ​
--------------------------------

名前

説明

[Condition](_extension_api_NextDesign.Core_IConstraint_properties_Condition.md)

制約の内容を表す文字列  
パス制約の場合は、パス文字列が設定されます。

[ConstrainedElements](_extension_api_NextDesign.Core_IConstraint_properties_ConstrainedElements.md)

制約適用対象の要素

[Key](_extension_api_NextDesign.Core_IConstraint_properties_Key.md)

制約の内容を解釈する方法の種類  
\- "System.PathConstraint"：パス制約

[Scope](_extension_api_NextDesign.Core_IConstraint_properties_Scope.md)

制約が有効となる範囲

メソッド​
-----------------------------

名前

説明

[IsSatisfiedWith](_extension_api_NextDesign.Core_IConstraint_methods_IsSatisfiedWith.md)

指定されたモデルを基点に、与えられたオブジェクトがこの制約に適合するか調べます。  
制約を満たす場合は、true を返します。  
  
なお、制約評価の基点となるモデルに、この制約と無関係のモデルが指定された場合は、常に true を返すように評価されます。