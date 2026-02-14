IWorkspaceState.SetCurrentModel メソッド
====================================

名前空間: NextDesign.Desktop

説明​
-----------------------

現在のワークスペースのカレントモデルを設定します。

引数​
-----------------------

名前

型

説明

model

IModel

モデル

戻り値​
--------------------------

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

model に null を指定した場合

無効なモデルを指定した

ExtensionInvalidModelException

model に削除されたモデルを指定した場合

編集しているプロジェクト以外のモデルを指定した

ExtensionInvalidModelException

model に編集しているプロジェクト以外のモデルを指定した場合