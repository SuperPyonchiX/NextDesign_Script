IWorkspace.OpenDiff メソッド
========================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定した差分比較情報から差分比較を表示します。

引数​
-----------------------

名前

型

説明

comparison

IModelComparison

比較処理単位情報です。

titleBefore

string

差分ビューのタイトルです。省略時は、"変更前"となります。

titleAfter

string

差分表示時のカレントビューのタイトルです。省略時は、"変更後"となります。

tooltipBefore

string

差分ビューのタイトルのツールチップテキストです。省略時は titleBefore と同じとなります。

tooltipAfter

string

差分表示時のカレントビューのタイトルのツールチップテキストです。省略時は titleAfter と同じとなります。

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

サポート外

ExtensionNotSupportedException

アプリケーションの現在のエディションが対応していない場合

引数不正

ExtensionArgumentException

comparison の AfterProject にカレントプロジェクト以外が設定されていた場合