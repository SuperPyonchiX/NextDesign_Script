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
    //  クラス図同期（Part 9）。本体は ClassSyncRuntime（61-class-sync-runtime.cs）
    // ------------------------------------------------------------

    public void ApplyClassSync(ICommandContext context, ICommandParams commandParams) { ClassSyncRuntime.Preview(context.App, true, true, true); }
    public void CreateClassDiagram(ICommandContext context, ICommandParams commandParams) { ClassDiagramCreator.Create(context.App); }

    // ------------------------------------------------------------
    //  シーケンス図同期（Part 10）。本体は 70〜73、入口は SequenceCommands（74-sequence-commands.cs）
    // ------------------------------------------------------------

    public void ApplySequenceSync(ICommandContext context, ICommandParams commandParams) { SequenceCommands.Apply(context.App); }
    public void CreateSequenceDiagram(ICommandContext context, ICommandParams commandParams) { SequenceCommands.Create(context.App); }
}
