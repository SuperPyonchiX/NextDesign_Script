IField.LowerBound プロパティ
=======================

名前空間: NextDesign.Core

説明​
-----------------------

多重度（下限）

    int LowerBound { get; set; }

型​
--------------------

*   int

例外​
-----------------------

名前

例外クラス

説明

範囲不正

ExtensionOutOfRangeException

負数、およびUpperBoundより大きい値が指定された場合

不正操作

ExtensionInvalidOperationException

フィールドの型がプリミティブ型(整数、実数、真偽値、文字列、リッチテキスト)、列挙型の場合