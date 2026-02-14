IInfoDisplayStyle.CardDisplayStyle プロパティ
========================================

名前空間: NextDesign.Desktop

説明​
-----------------------

カードの表示スタイル

*   "None" : カードを表示しない
*   "TitleOnly" : タイトルのみのカード
*   "DetailOnly" : 詳細のみのカード
*   "All" : タイトルと詳細のあるカード（詳細がなければタイトルのみ）

既定値は、"All" です。

    string CardDisplayStyle { get; set; }

型​
--------------------

*   string

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

値域外の値が指定された場合