CustomNavigatorDescriptor.IsEnabledFunc プロパティ
=============================================

名前空間: NextDesign.Desktop.CustomUI

説明​
-----------------------

ナビゲータが表示対象であるかの評価関数。  
評価関数が指定されていない場合、常にtrueを返すとして扱います。

    Func<IProject, bool> IsEnabledFunc { get; set; }

型​
--------------------

*   Func<IProject, bool>