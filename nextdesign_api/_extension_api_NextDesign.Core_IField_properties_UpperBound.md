IField.UpperBound プロパティ
=======================

名前空間: NextDesign.Core

説明​
-----------------------

多重度（上限）  
制限なしの場合は-1を指定します。

    int UpperBound { get; set; }

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

0以上の整数で、LowerBoundより小さい値が指定された場合

不正操作

ExtensionInvalidOperationException

フィールドの型がプリミティブ型(整数、実数、真偽値、文字列、リッチテキスト)、列挙型の場合