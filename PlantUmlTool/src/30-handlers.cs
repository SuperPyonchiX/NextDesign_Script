// ============================================================
//  コマンドハンドラ（manifest.json の execFunc と名前を一致させる）
// ============================================================

public partial class PlantUmlToolExtension
{
    public void ExportCurrentDiagram(ICommandContext context, ICommandParams commandParams)
    {
        try
        {
            var settings = new ExportSettings();
            var editor = context.App.Workspace.CurrentEditor;
            var stateOptions = new StatePlantUmlOptions();

            // シーケンス図 → 状態遷移図 → クラス図の順に判別する。
            // 状態遷移図の EditorType は ERDiagram 等と重なる可能性があるため、
            // クラス図判定より先にノード内容で判定する
            if (editor is ISequenceDiagram)
                ExportRunner.ExportCurrent(context.App, new PlantUmlOptions(), settings);
            else if (editor is IDiagram && StateExportRunner.IsStateDiagram((IDiagram)editor, stateOptions))
                StateExportRunner.ExportCurrent(context.App, stateOptions, settings);
            else if (ClassExportRunner.IsClassDiagramEditor(editor))
                ClassExportRunner.ExportCurrent(context.App, new ClassPlantUmlOptions(), settings);
            else
                ExportRunner.ExportCurrent(context.App, new PlantUmlOptions(), settings);   // 対象外の案内は従来どおり
        }
        catch (Exception ex)
        {
            context.App.Output.WriteLine(ExportRunner.Category, "[error] " + ex.ToString());
            context.App.Window.UI.ShowInformationDialog(
                "PlantUML 出力に失敗しました。\n\n" + ex.Message, ExportRunner.Category);
        }
    }

    public void ExportAllDiagrams(ICommandContext context, ICommandParams commandParams)
    {
        try
        {
            var settings = new ExportSettings();
            // 出力先はシーケンス図で選んだフォルダを状態遷移図・クラス図でも使う（種別ごとのフォルダに分かれる）
            var folder = ExportRunner.ExportAll(context.App, context, new PlantUmlOptions(), settings);

            // 状態遷移図・クラス図が 1 枚でもあれば続けて出力する。
            // どちらも無いプロジェクトでは従来と同じ操作感のまま何も起きない
            var root = ExportRunner.ResolveRoot(context.App);
            if (root == null) return;

            var stateOptions = new StatePlantUmlOptions();
            var skipCount = 0;
            List<ClassDiagramEntry> classTargets;
            List<ClassDiagramEntry> stateTargets;
            StateExportRunner.CollectSplit(root, settings.SkipEmptyDiagram, stateOptions,
                                           ref skipCount, out classTargets, out stateTargets);

            if (stateTargets.Count > 0)
                StateExportRunner.ExportAll(context.App, context, stateOptions, settings,
                                            folder, false, root, stateTargets, skipCount);

            if (classTargets.Count > 0)
                ClassExportRunner.ExportAll(context.App, context, new ClassPlantUmlOptions(), settings,
                                            folder, false, root, classTargets, skipCount);
        }
        catch (Exception ex)
        {
            context.App.Output.WriteLine(ExportRunner.Category, "[error] " + ex.ToString());
            context.App.Window.UI.ShowInformationDialog(
                "PlantUML 出力に失敗しました。\n\n" + ex.Message, ExportRunner.Category);
        }
    }

    // ------------------------------------------------------------
    //  反映・新規作成（シーケンス図とクラス図で共通のボタン）
    //    開いている図・モデルで図の種類を決める。本体はクラス図が Part 9
    //    （ClassSyncRuntime / ClassDiagramCreator）、シーケンス図が Part 10（SequenceCommands）
    // ------------------------------------------------------------

    const string SyncTitle = "PlantUML 連携";

    // 状態遷移図とクラス図はエディタ種別が重なることがあるので、出力と同じく状態遷移図を先に見分ける
    static bool IsStateDiagram(IEditor editor)
    {
        return editor is IDiagram && StateExportRunner.IsStateDiagram((IDiagram)editor, new StatePlantUmlOptions());
    }

    public void ApplyPlantUml(ICommandContext context, ICommandParams commandParams)
    {
        var app = context.App;
        var editor = app.Workspace.CurrentEditor;
        if (IsStateDiagram(editor)) app.Window.UI.ShowInformationDialog("状態遷移図への反映には対応していません。", SyncTitle);
        else if (editor is ISequenceDiagram) SequenceCommands.Apply(app);
        else if (editor != null && ClassDiagramKind.Reject(editor) == null) ClassSyncRuntime.Preview(app, true, true, true);
        else app.Window.UI.ShowInformationDialog("反映先のシーケンス図かクラス図を開いてから実行してください。", SyncTitle);
    }

    public void CreateFromPlantUml(ICommandContext context, ICommandParams commandParams)
    {
        var app = context.App;
        var editor = app.Workspace.CurrentEditor;
        if (IsStateDiagram(editor)) { app.Window.UI.ShowInformationDialog("状態遷移図の新規作成には対応していません。", SyncTitle); return; }
        if (editor is ISequenceDiagram) { SequenceCommands.Create(app); return; }
        if (editor != null && ClassDiagramKind.Reject(editor) == null) { ClassDiagramCreator.Create(app); return; }
        // A model is open or selected: the kinds of diagram it can hold decide.
        string sequenceReason, classReason = null;
        bool sequence = SequenceDiagramCreator.CanCreateHere(app, out sequenceReason);
        bool klass;
        try { IModel owner; IField field; IClass diagramClass; ClassDiagramCreator.ResolveGroup(app, editor, out owner, out field, out diagramClass); klass = true; }
        catch (Exception ex) { klass = false; classReason = ex.Message; }
        if (sequence && klass)
        {
            if (app.Window.UI.ShowConfirmDialog("ここにはシーケンス図とクラス図のどちらも作れます。\n\nOK: シーケンス図を作る\nキャンセル: クラス図を作る", SyncTitle))
                SequenceCommands.Create(app);
            else ClassDiagramCreator.Create(app);
        }
        else if (sequence) SequenceCommands.Create(app);
        else if (klass) ClassDiagramCreator.Create(app);
        else app.Window.UI.ShowInformationDialog("ここにはシーケンス図もクラス図も作れません。図を置くモデルを開くか選んでから実行してください。\n\nシーケンス図: " + sequenceReason + "\nクラス図: " + classReason, SyncTitle);
    }
}
