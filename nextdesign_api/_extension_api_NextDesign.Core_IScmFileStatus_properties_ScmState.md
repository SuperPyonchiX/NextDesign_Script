IScmFileStatus.ScmState プロパティ
=============================

名前空間: NextDesign.Core

説明​
-----------------------

物理ファイルの構成管理システム上のファイル状態

*   "None":変更なし
*   "Add":追加
*   "Update":更新
*   "Delete":削除
*   "Lost":紛失
*   "Conflict":競合
*   "Unmanage":管理外
*   "Ignore":管理外で無視
*   "Unknown":不明

    string ScmState { get; }

型​
--------------------

*   string