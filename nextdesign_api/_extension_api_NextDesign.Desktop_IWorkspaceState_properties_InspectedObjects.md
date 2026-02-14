IWorkspaceState.InspectedObjects プロパティ
======================================

名前空間: NextDesign.Desktop

説明​
-----------------------

インスペクト対象の要素（複数）  
現在インスペクタの表示対象となっている要素（複数）です。  
インスペクタ表示対象として複数の要素がない場合、空のコレクションとなります。  
IWorkspaceState.InspectedObject {get;} で取得できる要素が必ずしも含まれるとは限りません。

    IEnumerable<object> InspectedObjects { get; }

型​
--------------------

*   IEnumerable<object>