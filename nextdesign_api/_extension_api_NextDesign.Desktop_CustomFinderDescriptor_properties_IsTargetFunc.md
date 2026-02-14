CustomFinderDescriptor.IsTargetFunc プロパティ
=========================================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

カスタムファインダを利用するか決定する評価関数。  
評価関数が指定されていない場合、常にtrueを返すとして扱います。

    Func<IModel, string, bool> IsTargetFunc { get; set; }

型​
--------------------

*   Func<IModel, string, bool>