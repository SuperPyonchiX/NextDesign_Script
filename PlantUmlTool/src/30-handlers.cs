// ============================================================
//  コマンドハンドラ（manifest.json の execFunc と名前を一致させる）
// ============================================================

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
        ExportRunner.ExportAll(context.App, context, new PlantUmlOptions(), settings);

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
                                        null, false, root, stateTargets, skipCount);

        if (classTargets.Count > 0)
            ClassExportRunner.ExportAll(context.App, context, new ClassPlantUmlOptions(), settings,
                                        null, false, root, classTargets, skipCount);
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(ExportRunner.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "PlantUML 出力に失敗しました。\n\n" + ex.Message, ExportRunner.Category);
    }
}

// ============================================================
//  Part 6 / 診断側のコマンドハンドラ
//  （manifest.json の execFunc と名前を一致させる）
//
//  注: PlantUML からの取り込み（ImportFromFile / ImportFromFolder）は
//      撤去済み。Part 1〜5 の旧ライターはリボンから到達しない。
//      汎用モデル更新だけでシーケンスの構造と表示を構築できるとは限らない。
//      V3全般の作成不可、純正PlantUMLImporterの提供は根拠未確認。
//      公開SDKのインポートAPI候補と未確認事項は
//      docs/sequence-import-api-research.md を参照する。
// ============================================================

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

public void ProbeMetamodel(ICommandContext context, ICommandParams commandParams)
{
    try
    {
        var app = context.App;
        var diagram = app.Workspace.CurrentEditor as ISequenceDiagram;
        if (diagram == null)
        {
            app.Window.UI.ShowInformationDialog(
                "シーケンス図を開いた状態で実行してください。", MetaProbe.Category);
            return;
        }

        OutputPane.Show(app, MetaProbe.Category);

        MetaProbe.Run(app, diagram);
    }
    catch (Exception ex)
    {
        context.App.Output.WriteLine(MetaProbe.Category, "[error] " + ex.ToString());
        context.App.Window.UI.ShowInformationDialog(
            "メタモデル調査に失敗しました。\n\n" + ex.Message, MetaProbe.Category);
    }
}


// ------------------------------------------------------------
//  クラス図同期（Part 9）。本体は ClassSyncRuntime（61-class-sync-runtime.cs）
// ------------------------------------------------------------

public void PreviewClassSync(ICommandContext context, ICommandParams commandParams) { ClassSyncRuntime.Preview(context.App); }
public void TrialClassSync(ICommandContext context, ICommandParams commandParams) { ClassSyncRuntime.Preview(context.App, true); }
public void ApplyClassSync(ICommandContext context, ICommandParams commandParams) { ClassSyncRuntime.Preview(context.App, true, true, true); }
public void ShowClassSyncDetails(ICommandContext context, ICommandParams commandParams) { foreach (var page in ClassExperiment.Details.Split((char)12)) context.App.Window.UI.ShowInformationDialog(page, ClassExperiment.Title); }
