IEditorPage.ActivePalette プロパティ
===============================

名前空間: NextDesign.Desktop

説明​
-----------------------

パレットペインのアクティブなパレット

*   "Editor" : ツールボックス
*   "Reference" : 参照
*   "Derive" : 入力
*   "Feature" : フィーチャパレット
*   "ProductSelector" : プロダクトセレクタ
*   "Class" : クラスツールボックス

    string ActivePalette { get; set; }

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

値が値域外の場合

不正操作

ExtensionInvalidOperationException

現在のアプリケーションの状態において、選択できないパレットが指定された場合