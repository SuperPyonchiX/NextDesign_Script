// Experimental unit import. Shape/schema assumptions come from the public sample;
// successful rendering on the target runtime still requires a real-PC test.
//
// DLL 拡張（Next Design V3.x）。SequenceImportProbe.csproj が次を記載順にビルドする。
//   src/00-extension.cs（エントリとハンドラ）/ src/10-experiment.cs / sync/SequenceSyncRuntime.cs /
//   PlantUmlTool/src/10-sequence-export.cs（出力エンジン。PlantUmlTool と同じ正本）/
//   src/20-mapped-update.cs / sync/SequenceSync.cs
// using は SequenceImportProbe.csproj の Using 項目（global using）にまとめる。

public partial class SequenceImportProbeExtension : IExtension
{
    public void Activate(IContext context)
    {
    }

    public void Deactivate(IContext context)
    {
    }

    public void CommitReceiverStructure(ICommandContext context, ICommandParams parameters) { SequenceSyncRuntime.Preview(context.App,true,true,true,true); }
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
    // The result summary comes first, so the last result can be read again from here.
    public void ShowSequenceDetails(ICommandContext context, ICommandParams parameters) { foreach(var page in new[]{SequenceExperiment.Summary}.Concat(SequenceExperiment.Details.Split('\f'))) context.App.Window.UI.ShowInformationDialog(page, SequenceExperiment.Title); }
}
