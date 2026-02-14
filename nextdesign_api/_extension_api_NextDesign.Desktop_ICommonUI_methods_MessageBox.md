ICommonUI.MessageBox メソッド
=========================

名前空間: NextDesign.Desktop

説明​
-----------------------

\[Obsolete\] メッセージを表示します。

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

*   void

例外​
-----------------------

名前

例外クラス

説明

引数不正

ExtensionArgumentException

message に nullを指定した場合

注釈​
-----------------------

**Ver.1.1 での API 仕様変更と移行方法について**

このメソッドは将来のバージョンで削除されます。Ver.1.1 以降では、ShowMessageBox()を使用してください。  
本 API を利用している場合は、Ver.1.1 以降へのバージョンアップと合わせてエクステンション内の該当箇所を変更する必要があります。

次の例を参考に変更してください。

**変更前**

    UI.MessageBox("any message...");

**変更後**

    UI.ShowMessageBox("any message...");