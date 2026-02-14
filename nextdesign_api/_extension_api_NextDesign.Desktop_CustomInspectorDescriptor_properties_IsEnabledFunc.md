CustomInspectorDescriptor.IsEnabledFunc プロパティ
=============================================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

インスペクタが有効かの評価関数。  
評価関数が指定されていない場合、常にtrueを返すとして扱います。

    Func<object, bool> IsEnabledFunc { get; set; }

型​
--------------------

*   Func<object, bool>