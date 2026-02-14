IEditorPage.ActiveNavigator プロパティ
=================================

名前空間: NextDesign.Desktop

説明​
-----------------------

ナビゲータペインのアクティブなナビゲータ

*   "Model" : モデルナビゲータ
*   "ProductLine" : プロダクトラインナビゲータ
*   "Scm" : 構成管理ナビゲータ
*   "Project" : プロジェクトナビゲータ
*   "Profile" : プロファイルナビゲータ

    string ActiveNavigator { get; set; }

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

現在のアプリケーションの状態で選択できないナビゲータが指定された場合  
・プロファイル保護の状態でプロファイルナビゲータが指定された  
・プロダクトラインを開始していない状態でプロダクトラインナビゲータが指定された  
・チーム連携を行っていない状態で構成管理ナビゲータが指定された