// イベントハンドラテンプレート
// manifest.json の events.{area}[].execFunc に指定したメソッド名で定義する
// シグネチャはコマンドハンドラと同じ

public void <EventHandlerName>(ICommandContext context, ICommandParams parameters)
{
    try
    {
        // コンテキストからイベント情報を取得
        // context.App       — IApplication（アプリケーション全体へのアクセス）
        // context.Command   — ICommand（実行中のコマンド/イベント定義）
        // context.SenderModel — IModel（イベント発生元のモデル、ない場合はnull）

        var senderModel = context.SenderModel;
        if (senderModel == null) return;

        // --- ここにイベント処理を実装 ---

        Output.WriteLine("<カテゴリ>", "イベント処理が完了しました。");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
    }
}
