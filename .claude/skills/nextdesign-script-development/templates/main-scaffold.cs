// main.cs スキャフォールドテンプレート
// Next Designエクステンションのエントリポイント
// クラス定義やnamespace宣言は不要

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NextDesign.Core;
using NextDesign.Desktop;

// ============================================================
// グローバルオブジェクト（スクリプト内で直接使用可能）
// ------------------------------------------------------------
// App            : IApplication  — アプリケーション全体
// Context        : IContext      — 実行コンテキスト
// Errors         : IErrors       — モデル検証エラー情報
// Output         : IOutput       — 出力ウィンドウ
// Search         : ISearchManager — 検索マネージャ
// Window         : IWorkspaceWindow — アプリケーションウィンドウ
// Workspace      : IWorkspace    — ワークスペース
// CurrentModel   : IModel        — 現在選択中のモデル
// CurrentProject : IProject      — 現在開いているプロジェクト
// EditorPage     : IEditorPage   — エディタページUI
// ViewDefinitions: IViewDefinitions — ビュー定義
// UI             : ICommonUI     — ダイアログなどの共通UI
// ============================================================

// --- コマンドハンドラ ---

public void <HandlerMethod1>(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var model = CurrentModel;
        if (model == null)
        {
            UI.ShowInformationDialog("モデルが選択されていません。", "<エクステンション名>");
            return;
        }

        // --- 処理を実装 ---

        Output.WriteLine("<カテゴリ>", "処理が完了しました。");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
        UI.ShowInformationDialog(ex.Message, "エラー");
    }
}

// --- イベントハンドラ（必要な場合） ---

public void <EventHandlerMethod>(ICommandContext context, ICommandParams parameters)
{
    try
    {
        var senderModel = context.SenderModel;
        if (senderModel == null) return;

        // --- イベント処理を実装 ---

        Output.WriteLine("<カテゴリ>", "イベント処理が完了しました。");
    }
    catch (Exception ex)
    {
        Output.WriteLine("エラー", ex.Message);
    }
}

// --- ヘルパーメソッド（必要な場合） ---

private string FormatModelInfo(IModel model)
{
    var name = model.GetField("Name") as string ?? "(名前なし)";
    return name;
}
