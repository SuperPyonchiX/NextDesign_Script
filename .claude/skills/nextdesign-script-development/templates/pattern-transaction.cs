// トランザクションパターンテンプレート
// モデルを変更する場合は、アンドゥトランザクションで囲む
// autoCommit=true の場合、Commit/Rollbackを呼ばずにスコープを抜けると自動コミット

var project = CurrentProject;
if (project == null)
{
    UI.ShowInformationDialog("プロジェクトが開かれていません。", "エラー");
    return;
}

var transaction = project.BeginUndoTransaction(true);
try
{
    // --- モデル変更操作をここに記述 ---
    // 例：
    // var model = CurrentModel;
    // model.SetField("Name", "新しい名前");
    // var child = model.AddNewModel("Children", "ChildClassName");
    // child.SetField("Name", "子モデル名");

    transaction.Commit();
    Output.WriteLine("<カテゴリ>", "モデルを更新しました。");
}
catch (Exception ex)
{
    transaction.Rollback();
    Output.WriteLine("エラー", ex.Message);
    UI.ShowInformationDialog(ex.Message, "エラー");
}
