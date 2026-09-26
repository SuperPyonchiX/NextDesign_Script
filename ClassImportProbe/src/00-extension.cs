// ============================================================
//  Next Design エクステンション : クラス図同期実験（開発用）
//  エントリポイント（DLL / Next Design V3.x）
//
//  PlantUmlTool のクラス図同期（PlantUmlTool/src/60〜64）を直接ビルドし、開発と実機確認に
//  使うボタン（差分を検証・PlantUMLを反映・新規作成・診断表示・クラス図調査）を持つ。
//  PlantUmlTool は機能（反映・新規作成）だけを持ち、診断のボタンはここにある。
//  同期の本体を直すときは PlantUmlTool/src を直し、両方をビルドし直す。
// ============================================================

public partial class ClassImportProbeExtension : IExtension
{
    public void Activate(IContext context)
    {
    }

    public void Deactivate(IContext context)
    {
    }

    public void PreviewClassSync(ICommandContext context, ICommandParams commandParams) { ClassSyncRuntime.Preview(context.App); }
    public void ApplyClassSync(ICommandContext context, ICommandParams commandParams) { ClassSyncRuntime.Preview(context.App, true, true, true); }
    public void CreateClassDiagram(ICommandContext context, ICommandParams commandParams) { ClassDiagramCreator.Create(context.App); }
    // The last result's pages; results of PlantUmlTool's buttons are in its diagnostics files.
    public void ShowClassSyncDetails(ICommandContext context, ICommandParams commandParams) { foreach (var page in ClassExperiment.Details.Split((char)12)) context.App.Window.UI.ShowInformationDialog(page, ClassExperiment.Title); }

    public void ProbeNodePlacement(ICommandContext context, ICommandParams commandParams) { NodePlacementProbe.Run(context.App); }

    public void ProbeClassDiagram(ICommandContext context, ICommandParams commandParams)
    {
        try
        {
            var app = context.App;
            var editor = app.Workspace.CurrentEditor;
            var diagram = editor as IDiagram;
            if (diagram == null)
            {
                app.Window.UI.ShowInformationDialog(
                    "クラス図を開いた状態で実行してください。（EditorType = "
                    + (editor != null ? editor.EditorType : "エディタなし") + "）", ClassProbe.Category);
                return;
            }

            OutputPane.Show(app, ClassProbe.Category);

            ClassProbe.Run(app, diagram);
        }
        catch (Exception ex)
        {
            context.App.Output.WriteLine(ClassProbe.Category, "[error] " + ex.ToString());
            context.App.Window.UI.ShowInformationDialog(
                "クラス図調査に失敗しました。\n\n" + ex.Message, ClassProbe.Category);
        }
    }
}
