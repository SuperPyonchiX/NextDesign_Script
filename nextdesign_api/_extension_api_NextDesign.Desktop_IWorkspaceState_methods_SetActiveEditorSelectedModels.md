IWorkspaceState.SetActiveEditorSelectedModels メソッド
==================================================

名前空間: NextDesign.Desktop

説明​
-----------------------

アクティブなエディタの選択要素（複数）を設定します。  
このAPIは保持している状態を変更するのみで、画面上の選択状態は変わりません。  
IWorkspaceState.ActiveEditorSelectedModels {get;} で選択要素を取得すると、このAPIで変更した値を取得できます。  
IWorkspaceState.ActiveEditorSelectedModel {get;} で取得できる選択要素が必ずしも含まれるとは限りません。  
編集しているプロジェクト以外のモデルを指定した場合は、選択要素から除外します。

引数​
-----------------------

名前

型

説明

models

IEnumerable<IModel>

モデルの列挙

戻り値​
--------------------------

*   void