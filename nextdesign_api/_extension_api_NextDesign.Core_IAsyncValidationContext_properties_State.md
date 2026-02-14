IAsyncValidationContext.State プロパティ
===================================

名前空間: NextDesign.Core

説明​
-----------------------

検証の実行状態を取得します。

    string State { get; }

型​
--------------------

*   string

注釈​
-----------------------

取得できる値域は以下になります。

*   "None"：未実施
*   "Running"：実行中
*   "Completed"：完了した
*   "Cancelled"：キャンセルされた
*   "Failed"：失敗した