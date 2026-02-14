IGridColumn.DataTypeName プロパティ
==============================

名前空間: NextDesign.Core

説明​
-----------------------

列データ型名

*   bool型 : "Boolean"
*   int型 : "Integer"
*   double型 : "Double"
*   文字列型 : "String"
*   リッチテキスト型 : "RichText"
*   列挙型 : IEnumの完全修飾名 （例："Package1.SubPackage1.UsecaseKind"）
*   クラス型（モデル参照） : IClassの完全修飾名 （例："Package1.SubPackage1.Usecase"）

    string DataTypeName { get; }

型​
--------------------

*   string