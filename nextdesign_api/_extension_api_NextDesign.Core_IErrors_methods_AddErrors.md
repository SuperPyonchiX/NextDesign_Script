IErrors.AddErrors メソッド
======================

名前空間: NextDesign.Core

説明​
-----------------------

指定したエラー情報をモデルに追加します。

引数​
-----------------------

名前

型

説明

errors

IEnumerable<IError>

エラー情報の列挙。

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

errors に null が指定された場合

注釈​
-----------------------

注意：UI表示中はUIスレッドから呼び出す必要があります。

例：

    // エラーの列挙IEnumerable<IError> errors;// UIスレッドでメソッドを呼び出しますSystem.Windows.Application.Current.Dispatcher.Invoke(() => {Workspace.Errors.AddErrors(errors); });