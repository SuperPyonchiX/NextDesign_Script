// コマンドハンドラテンプレート
// manifest.json の commands[].execFunc に指定したメソッド名で定義する

public void <HandlerMethodName>(ICommandContext context, ICommandParams parameters)
{
    try
    {
        // 現在のモデルを取得（必要に応じて CurrentProject も使用可）
        var model = CurrentModel;
        if (model == null)
        {
            UI.ShowInformationDialog("モデルが選択されていません。", "<エクステンション名>");
            return;
        }

        // --- ここに処理を実装 ---

        Output.WriteLine("<カテゴリ>", "処理が完了しました。");
        UI.ShowInformationDialog("処理が完了しました。", "<エクステンション名>");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}
