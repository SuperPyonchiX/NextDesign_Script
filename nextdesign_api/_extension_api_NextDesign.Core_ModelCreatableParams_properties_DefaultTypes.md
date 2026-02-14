ModelCreatableParams.DefaultTypes プロパティ
=======================================

名前空間: NextDesign.Core.EditingCapabilities

説明​
-----------------------

追加先フィールドと、そのフィールドで追加できるクラス候補一覧の辞書  
・サブクラスは候補に含まれる  
・抽象クラスは候補に含まれない

    IDictionary<IField, IEnumerable<IClass>> DefaultTypes { get; }

型​
--------------------

*   IDictionary<IField, IEnumerable<IClass>>