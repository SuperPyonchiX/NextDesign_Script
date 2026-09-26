// Experimental unit import. Shape/schema assumptions come from the public sample;
// successful rendering on the target runtime still requires a real-PC test.
//
// DLL 拡張（Next Design V3.x）。SequenceImportProbe.csproj が次を記載順にビルドする。
//   src/00-extension.cs（エントリとハンドラ）/ src/30-dev-tools.cs（開発用）/ src/40-legacy-import.cs（旧取込とメタモデル調査）/
//   PlantUmlTool/src/10-sequence-export.cs（出力エンジン）/ 70〜73（同期の本体。どれも PlantUmlTool と同じ正本）
// using は SequenceImportProbe.csproj の Using 項目（global using）にまとめる。

public partial class SequenceImportProbeExtension : IExtension
{
    public void Activate(IContext context)
    {
        SequenceExperiment.Title="シーケンス生成実験 / 0.13.5";
    }

    public void Deactivate(IContext context)
    {
    }

    public void CommitReceiverStructure(ICommandContext context, ICommandParams parameters) { SequenceSyncRuntime.Preview(context.App,true,true,true,true); }
    public void ProbeUndoOverlay(ICommandContext context, ICommandParams parameters) { SequenceUndoProbe.Run(context.App); }
    public void ProbeDeleteUndo(ICommandContext context, ICommandParams parameters) { SequenceDeleteUndoProbe.Run(context.App); }
    public void RunScenarioBatch(ICommandContext context, ICommandParams parameters) { SequenceBatch.Run(context.App); }
    public void RunScenarioBatchFromSdk(ICommandContext context, ICommandParams parameters) { SequenceSyncRuntime.ForceSdkSnapshot=true; try { SequenceBatch.Run(context.App); } finally { SequenceSyncRuntime.ForceSdkSnapshot=false; } }
    public void CheckAllSequences(ICommandContext context, ICommandParams parameters) { SequenceBatch.Sweep(context.App, context); }
    public void ProbeUnsavedSnapshot(ICommandContext context, ICommandParams parameters) { SequenceSnapshotProbe.Run(context.App); }
    public void ProbeOmittedValues(ICommandContext context, ICommandParams parameters) { SequenceOmissionProbe.Run(context.App); }
    public void PrepareSequenceStructure(ICommandContext context, ICommandParams parameters) { SequenceSyncRuntime.Preview(context.App,true); }
    public void PreviewSequenceSync(ICommandContext context, ICommandParams parameters) { SequenceSyncRuntime.Preview(context.App); }
    public void CreateSequenceMap(ICommandContext context, ICommandParams parameters) { SequenceMappedUpdate.Run(context.App, true); }
    public void UpdateMappedSequence(ICommandContext context, ICommandParams parameters) { SequenceMappedUpdate.Run(context.App, false); }
    public void CreateMinimalSequence(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App); }
    public void ImportPlantUml(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, true); }
    public void ReplaceSequence(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, true, false, true); }
    public void ProbeSequenceDelta(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, false, true, false, true); }
    public void ProbeSequenceStructure(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, false, true, false, false, true); }
    public void ProbeSequenceUpdate(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, false, true); }
    // メタモデル調査: 開いているシーケンス図のメタモデル構造を出力ウィンドウに書く（旧 PlantUmlTool の診断）。
    public void ProbeMetamodel(ICommandContext context, ICommandParams parameters)
    {
        try
        {
            var app = context.App;
            var diagram = app.Workspace.CurrentEditor as ISequenceDiagram;
            if (diagram == null) { app.Window.UI.ShowInformationDialog("シーケンス図を開いた状態で実行してください。", MetaProbe.Category); return; }
            OutputPane.Show(app, MetaProbe.Category);
            MetaProbe.Run(app, diagram);
        }
        catch (Exception ex)
        {
            context.App.Output.WriteLine(MetaProbe.Category, "[error] " + ex.ToString());
            context.App.Window.UI.ShowInformationDialog("メタモデル調査に失敗しました。\n\n" + ex.Message, MetaProbe.Category);
        }
    }
    // The result summary comes first, so the last result can be read again from here.
    public void ShowSequenceDetails(ICommandContext context, ICommandParams parameters) { foreach(var page in new[]{SequenceExperiment.Summary}.Concat(SequenceExperiment.Details.Split('\f'))) context.App.Window.UI.ShowInformationDialog(page, SequenceExperiment.Title); }
}
