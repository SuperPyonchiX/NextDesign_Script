IEditorPage.IsMainEditor メソッド
=============================

名前空間: NextDesign.Desktop

説明​
-----------------------

指定したカスタムエディタが現在アクティブタブのメインエディタに表示されているか否かを判定します。  
カスタムエディタの初期化処理 ICustomUI.OnInitialized() 実行時には正しく判定出来ず、常にfalseを返します。

引数​
-----------------------

名前

型

説明

editorView

ICustomEditorView

カスタムエディタ

戻り値​
--------------------------

*   bool