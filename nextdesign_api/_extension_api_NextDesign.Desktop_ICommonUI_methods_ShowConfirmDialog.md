ICommonUI.ShowConfirmDialog メソッド
================================

名前空間: NextDesign.Desktop

説明​
-----------------------

確認ダイアログを表示します。  
ダイアログの操作結果(OK=true / Cancel=false)を返します。

引数​
-----------------------

名前

型

説明

message

string

メッセージ  
null は指定できません。

caption

string

キャプション（省略した場合はアプリケーション名を使用します）

戻り値​
--------------------------

*   bool

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

message に nullを指定した場合