// Experimental unit import. Shape/schema assumptions come from the public sample;
// successful rendering on the target runtime still requires a real-PC test.
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NextDesign.Core;
using NextDesign.Desktop;

public void CreateSequenceMap(ICommandContext context, ICommandParams parameters) { SequenceMappedUpdate.Run(context.App, true); }
public void UpdateMappedSequence(ICommandContext context, ICommandParams parameters) { SequenceMappedUpdate.Run(context.App, false); }
public void CreateMinimalSequence(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App); }
public void ImportPlantUml(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, true); }
public void ReplaceSequence(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, true, false, true); }
public void ProbeSequenceDelta(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, false, true, false, true); }
public void ProbeSequenceUpdate(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, false, true); }
public void ShowSequenceResult(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Show(context.App); }
public void ShowSequenceDetails(ICommandContext context, ICommandParams parameters) { context.App.Window.UI.ShowInformationDialog(SequenceExperiment.Details, SequenceExperiment.Title); }

public static class SequenceExperiment
{
    public const string Title = "シーケンス生成実験 / 0.7.0";
    public static string Summary = "シーケンス図を開き「PlantUMLを取り込む」または「最小図を生成」を押してください。";
    public static string Details = "まだ実行していません。";
    public static void Show(IApplication app) { app.Window.UI.ShowInformationDialog(Summary, Title); }

    public static void Run(IApplication app) { Run(app, false); }
    public static void Run(IApplication app, bool fromPlantUml, bool updateProbe=false, bool replaceExisting=false, bool deltaProbe=false)
    {
        string stage = "事前検査", directory = null, rootId = null;
        bool called = false, committed = false, rolledBack = false;
        string apiState = "未取得";
        int apiIssues = 0;
        var detail = new StringBuilder();
        IUndoTransaction transaction = null;
        var completion = new SequenceCompletion();
        SequenceReplacement replacement=null;
        bool mutationStarted=false;
        try
        {
            var project = app.Workspace.CurrentProject;
            var diagram = app.Workspace.CurrentEditor as ISequenceDiagram;
            var sample = diagram == null ? null : diagram.Model as IInteraction;
            if (project == null || sample == null) throw new InvalidOperationException("E101: シーケンス図をメインエディタに開いてください。");
            PumlPlan plan = null;
            string pumlText = null;
            if (fromPlantUml)
            {
                var path = app.Window.UI.ShowOpenFileDialog("取り込むPlantUMLファイル", "PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
                if (string.IsNullOrEmpty(path)) return;
                if (new FileInfo(path).Length > 300000) throw new InvalidOperationException("E120: 入力は300KB以下にしてください。");
                pumlText = File.ReadAllText(path, new UTF8Encoding(false, true));
                detail.AppendLine("PlantUML file: " + path);
                try { plan = PumlPlan.Parse(pumlText); }
                catch (InvalidOperationException parseError)
                {
                    var match=Regex.Match(parseError.Message,@"E120: (\d+)行目:");
                    int row;
                    if(match.Success && int.TryParse(match.Groups[1].Value,out row))
                    {
                        var inputLines=pumlText.Replace("\r\n","\n").Replace('\r','\n').Split('\n');
                        for(int i=Math.Max(0,row-3);i<Math.Min(inputLines.Length,row+2);i++)
                            detail.AppendLine((i+1)+": "+inputLines[i]);
                    }
                    throw;
                }
            }
            var owner = sample.Owner;
            var ownerField = sample.GetOwnerField();
            if (owner == null || ownerField == null || !owner.IsEditable || owner.IsDeleted || owner.IsProxy)
                throw new InvalidOperationException("E102: 新しい図を置く親モデルを取得できないか、編集できません。");
            if (!app.Window.UI.ShowConfirmDialog(replaceExisting ? "コピーのプロジェクトで実行してください。\n現在の図をPlantUMLの内容で置き換えます。図自体のIDは維持します。\n配下の要素と手作業の配置は作り直します。子要素と外部モデルとの関連は引き継ぎません。自動保存はしません。" : deltaProbe ? "コピーのプロジェクトで実行してください。\n一時図で名前変更・メッセージ1件の差分追加と削除を検証し、最後に取り消します。\n削除中だけSDKの編集可否検査を一時停止する実験です。既存図は更新せず、自動保存もしません。" : updateProbe ? "コピーのプロジェクトで実行してください。\n一時図を作り、同じIDでメッセージ名を再取り込みします。\n最後に一時図を含む操作を取り消します。既存図を更新する検証ではありません。\n自動保存はしません。" : "実プロジェクトのコピーを開いていますか？\n新しい検証用シーケンス図を同じ親に追加する実験です。\n既存図の内容は入力にコピーしません。自動保存しません。\n失敗時はトランザクションの取消を試みますが、実機での復元動作は未確認です。", Title)) return;
            var folder = app.Window.UI.ShowSelectFolderDialog("会社PC内の実験結果の保存先");
            if (string.IsNullOrEmpty(folder)) return;
            directory = Path.Combine(folder, "sequence_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0,8));
            Directory.CreateDirectory(directory);
            string projectId = project.Id, ownerId = owner.Id, sampleId = sample.Id;
            string projectPath = project.Path;
            if (string.IsNullOrEmpty(projectPath) || !Path.IsPathRooted(projectPath))
                throw new InvalidOperationException("E103: コピーしたプロジェクトを保存してから試してください。");
            stage = "型情報の取得";
            var message = sample.Messages.Cast<IMessage>().FirstOrDefault(m => m.Kind == "sync"
                && m.Sender != null && m.Receiver != null && m.Sender.Id != m.Receiver.Id
                && m.SendPort is IExecutionSpecification && m.ReceivePort is IExecutionSpecification);
            if (message == null || sample.Frame == null)
                throw new InvalidOperationException("E104: 異なるライフライン間の同期メッセージがある図を開いてください。");
            IModel[] sources = { sample, sample.Frame, message.Sender, message.Receiver,
                (IModel)message.SendPort, (IModel)message.ReceivePort, message };
            if (sources.Any(m => m.IsDeleted || m.IsProxy)) throw new InvalidOperationException("E105: 見本の一部が削除済み・未読込です。");
            // Verify the standard relationship IDs against this profile before writing.
            var relationIds = new HashSet<string>(sources.SelectMany(m => m.Metaclass.GetFields().Cast<IField>())
                .Where(f => f.RelationshipClass != null).Select(f => f.RelationshipClass.Id));
            foreach (string id in SequencePayload.RelationTypes)
                if (!relationIds.Contains(SequencePayload.Prefix + id))
                    throw new InvalidOperationException("E106: 標準の構造関連が見つかりません: " + id);
            var sort = message.Metaclass.GetFields().Cast<IField>().FirstOrDefault(f => f.Name == "MessageSort");
            if (sort == null) throw new InvalidOperationException("E107: メッセージ種別フィールドが未対応です。");
            var kindValue = message.GetField("MessageSort");
            detail.AppendLine("MessageSort runtime type=" + (kindValue == null ? "null" : kindValue.GetType().FullName)
                + "; value=" + (kindValue == null ? "null" : kindValue.ToString()));
            if (kindValue == null || !string.Equals(kindValue.ToString(), "Sync", StringComparison.Ordinal))
                throw new InvalidOperationException("E108: 同期メッセージの保存値が想定と異なります。");
            string schema = "13.0";
            bool fromFile = false;
            // Read only the header; never change the project file. SQLite falls back to
            // the schema seen in the official sample, explicitly recorded as a hypothesis.
            using (var reader = new StreamReader(projectPath, Encoding.UTF8, true))
            {
                char[] header = new char[4096]; int n = reader.Read(header, 0, header.Length);
                var match = Regex.Match(new string(header, 0, n), "\"SchemaVersion\"\\s*:\\s*\"([0-9]+\\.[0-9]+)\"");
                if (match.Success) { schema = match.Groups[1].Value; fromFile = true; }
            }
            if(replaceExisting)replacement=SequenceReplacement.Capture(sample,diagram);
            var payload = SequencePayload.Build(sources.Select(m => m.Metaclass.Id).ToArray(), diagram.EditorDefinition.Id, schema);
            if (plan != null)
            {
                stage = "PlantUML生成データの構築";
                payload = PumlBuild.Build(plan, PumlRuntime.Profile(diagram, sources, plan), diagram.EditorDefinition.Id, schema, replacement==null?null:replacement.Identity);
                Write(Path.Combine(directory, "source.puml"), pumlText);
            }
            rootId = payload.Ids[0];
            detail.AppendLine("schema=" + schema + "; source=" + (fromFile ? "project header" : "public sample hypothesis"));
            detail.AppendLine("SDK=" + typeof(IProject).Assembly.FullName);
            detail.AppendLine("parent=" + ownerId + "; field=" + ownerField.Name + "; source=" + sampleId);
            stage = "生成データの記録";
            Write(Path.Combine(directory, "input.json"), payload.Json);
            Write(Path.Combine(directory, "before.txt"), detail.ToString()+(replacement==null?"":"\n更新前の構造:\n"+replacement.Before));
            if (!app.Window.UI.ShowConfirmDialog(replaceExisting ? "現在の図「"+sample.Name+"」を「"+payload.Name+"」へ更新します。\n"+plan.Summary()+"\n旧子要素: "+(replacement.AllIds.Length-2)+"件を置換します。\n図IDは維持し、子要素IDは変わります。続けますか？" : updateProbe ? "同じIDへの再取り込みを一時図で検証します。\nprobe()をupdatedProbe()へ変更したデータを再取り込みし、取消後に一時モデルが消えたことを確認します。\n続けますか？" : "新しい図「" + payload.Name + "」を追加します。\n" + (plan == null ? "A → B : probe()\nライフライン2本・同期メッセージ1本" : plan.Summary()) + "\n入力データの記録: 済み\n続けますか？", Title))
            { Summary = "キャンセル / インポートAPI呼出: なし"; Show(app); return; }
            var current = app.Workspace.CurrentProject;
            if (current == null || current.Id != projectId || !string.Equals(current.Path, projectPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("E109: 入力中にプロジェクトが切り替わりました。");
            owner = current.GetModelById(ownerId);
            var fresh = current.GetModelById(sampleId);
            if (owner == null || owner.IsDeleted || owner.IsProxy || !owner.IsEditable || fresh == null || fresh.IsDeleted
                || fresh.Owner == null || fresh.Owner.Id != ownerId || fresh.GetOwnerField().Name != ownerField.Name)
                throw new InvalidOperationException("E110: 作成先が変わりました。");
            if(replaceExisting)replacement.CheckUnchanged(fresh as IInteraction,app.Workspace.CurrentEditor as ISequenceDiagram);
            foreach (string id in payload.Ids)
                if (!(replaceExisting && (id==replacement.Identity.Root || id==replacement.Identity.Frame)) && current.GetModelById(id) != null) throw new InvalidOperationException("E111: 生成IDが既存モデルと衝突しました。");
            var originalChildren=new HashSet<string>(owner.GetChildren().Select(m=>m.Id));
            stage = "トランザクション開始";
            transaction = current.BeginUndoTransaction(false);
            if (transaction == null) throw new InvalidOperationException("E117: トランザクションを開始できませんでした。");
            if(replaceExisting)
            {
                stage="既存図の子要素削除"; mutationStarted=true;
                replacement.DeleteChildren(current);
            }
            stage = "JSONインポート";
            called = true;
            var result = current.ImportUnitFromJson(payload.Json, replaceExisting?null:owner, replaceExisting?null:ownerField.Name);
            if (result == null) throw new InvalidOperationException("E112: API結果がnullです。");
            apiState = result.State;
            apiIssues = result.Errors.Count();
            detail.AppendLine("state=" + result.State);
            foreach (var error in result.Errors) detail.AppendLine(error.Kind + ": " + error.Message);
            if (result.State != "success" || result.Errors.Any(e => e.Kind != UnitImportErrorKind.Info))
                throw new InvalidOperationException("E113: APIが失敗または警告を返しました。");
            stage = "読戻し照合";
            if (plan != null)
            {
                detail.AppendLine("Expected model metadata:");
                foreach(var e in payload.Expected)
                {
                    var actualModel=current.GetModelById(e.Id);
                    detail.AppendLine(e.Kind+" id="+e.Id+" metaclass="+(actualModel==null?"missing":actualModel.Metaclass.Id));
                }
                PumlRuntime.Verify(current, result, payload, ownerId);
                if(replaceExisting)replacement.Verify(current,result,payload);
            }
            else
            {
            var created = current.GetModelById(rootId) as IInteraction;
            if (created == null || created.Name != payload.Name || created.Owner == null || created.Owner.Id != ownerId
                || created.Lifelines.Count() != 2 || created.Messages.Count() != 1)
                throw new InvalidOperationException("E114: 新しいモデルの所属・要素数が一致しません。");
            var actual = created.Messages.First();
            if (!created.Lifelines.Any(l => l.Id == payload.Ids[2] && l.Name == "A")
                || !created.Lifelines.Any(l => l.Id == payload.Ids[3] && l.Name == "B"))
                throw new InvalidOperationException("E118: ライフラインの名前が一致しません。");
            if (actual.Kind != "sync" || actual.Name != "probe()" || actual.Sender == null || actual.Receiver == null
                || actual.Sender.Id != payload.Ids[2] || actual.Receiver.Id != payload.Ids[3])
                throw new InvalidOperationException("E115: メッセージの種別・名前・送受信先が一致しません。");
            var importedDiagram = result.ImportedEditors.OfType<ISequenceDiagram>().SingleOrDefault(e => e.ModelId == rootId);
            if (importedDiagram == null || importedDiagram.Lifelines.Count() != 2 || importedDiagram.Messages.Count() != 1)
                throw new InvalidOperationException("E116: 表示用シェイプの数が一致しません。");
            }
            if(updateProbe)
            {
                stage="同一ID再インポート";
                string updateJson=SequenceUpdateProbe.Payload(payload.Json);
                Write(Path.Combine(directory,"update-input.json"),updateJson);
                var updated=current.ImportUnitFromJson(updateJson,null,null);
                if(updated==null)throw new InvalidOperationException("E130: 再取り込み結果がnullです。");
                apiState=updated.State; apiIssues=updated.Errors.Count();
                detail.AppendLine("update state="+updated.State);
                foreach(var error in updated.Errors)detail.AppendLine(error.Kind+": "+error.Message);
                if(updated.State!="success" || updated.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))
                    throw new InvalidOperationException("E131: 同じIDへの再取り込みが拒否されました。");
                var changed=current.GetModelById(payload.Ids[6]) as IMessage;
                var sameRoot=current.GetModelById(rootId) as IInteraction;
                if(changed==null || changed.Name!="updatedProbe()" || sameRoot==null || sameRoot.Owner==null || sameRoot.Owner.Id!=ownerId || sameRoot.Messages.Count()!=1)
                    throw new InvalidOperationException("E132: 同じIDのモデルへ変更が反映されませんでした。");
                var updatedDiagram=updated.ImportedEditors.OfType<ISequenceDiagram>().SingleOrDefault(e=>e.ModelId==rootId);
                if(updatedDiagram==null || updatedDiagram.Messages.Count()!=1 || updatedDiagram.Messages.Single().Model.Id!=changed.Id || updatedDiagram.Messages.Single().Model.Name!="updatedProbe()")
                    throw new InvalidOperationException("E133: 再取り込み後の図形を確認できませんでした。");
                if(updated.ImportedModels.Any(m=>!payload.Ids.Contains(m.Id)))throw new InvalidOperationException("E134: 別IDのモデルが生成されました。");
                string addedId=null;
                if(deltaProbe)
                {
                    stage="差分追加・削除の検証";
                    apiState="未取得（差分追加）"; apiIssues=0;
                    addedId=SequenceDeltaProbe.Run(current,payload,updatedDiagram,schema,directory,detail,delegate(string state,int issues){apiState=state;apiIssues=issues;});
                }
                stage="検証操作の取消";
                completion.Cancel(delegate { transaction.Rollback(); }); rolledBack=true;
                if((addedId!=null && current.GetModelById(addedId)!=null) || payload.Ids.Any(id=>current.GetModelById(id)!=null) || !originalChildren.SetEquals(owner.GetChildren().Select(m=>m.Id)))
                    throw new InvalidOperationException("E135: 一時モデルの削除または親配下の復元を確認できませんでした。");
                detail.AppendLine("same-ID rename and temporary model removal: verified");
                Summary="ケース: UPDATE001 / 同じIDへの名称更新: 一致\n一時図・一時モデル: 取消後の除去を確認\n既存図への更新: 未実施 / プロジェクト保存: していません\n要素追加・削除・図形置換・参照保持は未検証です。\nこの結果画面を撮影してください。";
                if(deltaProbe)Summary="ケース: UPDATE003 / 一時図での差分追加・削除: 一致\n既存モデル・関連・図形IDの保持: 一致\n一時図・モデル: 取消後の除去を確認\n既存図の差分更新: 未実装 / 保存・Git差分: 未確認\nこの結果と診断表示を撮影してください。";
            }
            else
            {
            // Require a successful journal write before committing; failures enter rollback.
            Write(Path.Combine(directory, "checked.txt"), detail + "\nモデル・送受信・シェイプ照合: 一致\ncommit: 未実行");
            stage = "確定";
            completion.Commit(delegate { transaction.Commit(); }); committed = true;
            Summary = "ケース: " + (replaceExisting ? "UPDATE002" : updateProbe ? (deltaProbe ? "UPDATE003" : "UPDATE001") : fromPlantUml ? "IMPORT001" : "CREATE001") + " / モデル・シェイプ照合: 一致\n"+(replaceExisting?"更新した図: ":"新しい図: ") + payload.Name
                + "\n" + (plan == null ? "ライフライン: 2 / メッセージ: 1" : plan.Summary()) + "\n確定: 済み / プロジェクト保存: していません\n図を開き直し、図とこの画面を撮影してください。\n図表示・Undo/Redo・再読込: 未確認";
            }
        }
        catch (Exception ex)
        {
            detail.AppendLine(ex.ToString());
            if (transaction != null && !committed)
            {
                try { completion.Cancel(delegate { transaction.Rollback(); }); rolledBack = true; }
                catch (Exception rollbackError) { detail.AppendLine("ROLLBACK: " + rollbackError); }
            }
            if(rolledBack && replacement!=null)
            {
                try { replacement.CheckUnchanged(app.Workspace.CurrentProject.GetModelById(replacement.Identity.Root) as IInteraction,app.Workspace.CurrentEditor as ISequenceDiagram); detail.AppendLine("Rollback structure and shape IDs: verified"); }
                catch(Exception restoreError) { detail.AppendLine("Rollback verification: "+restoreError); }
            }
            Summary = "ケース: " + (replaceExisting ? "UPDATE002" : updateProbe ? (deltaProbe ? "UPDATE003" : "UPDATE001") : fromPlantUml ? "IMPORT001" : "CREATE001") + " / 停止段階: " + stage
                + "\nインポートAPI呼出: " + (called ? "あり" : "なし")
                + "\nAPI結果: " + apiState + " / 診断件数: " + apiIssues
                + "\n取消API: " + (rolledBack ? "正常終了（復元は未確認）" : transaction == null ? "未呼出" : "未確認・失敗")
                + "\n理由: " + (ex.Message.StartsWith("E1", StringComparison.Ordinal) ? ex.Message : ex.GetType().Name)
                + "\nこの画面を撮影してください。詳細は「診断表示」で確認できます。"
                + (called || mutationStarted ? "\n再実行前にコピーを開き直してください。" : "\nこのコマンドによるモデル変更はありません。");
        }
        finally
        {
            // Commit/Rollback are explicit terminal operations. Do not call Dispose:
            // with autoCommit=false it may attempt another rollback after completion.
            Details = "会社PC内の診断情報（モデルID・属性名を含む場合があります）\n" + detail.ToString();
            if (directory != null)
            {
                detail.AppendLine("newRoot=" + rootId + "; committed=" + committed + "; rollbackReturned=" + rolledBack);
                try { Write(Path.Combine(directory, "result.txt"), Summary + "\n\n" + detail); Summary += "\n記録: 保存済み"; }
                catch { Summary += "\n記録: 保存失敗"; }
            }
        }
        Show(app);
    }
    public static void Write(string path, string text)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(true))) writer.Write(text);
    }
}

public static class SequenceDeltaProbe
{
    static string[] Relations(IModel model)
    { return model.GetRelationsWhere((r,f)=>true).Select(r=>r.Id+":"+r.Source.Id+":"+r.Target.Id).Distinct().OrderBy(x=>x).ToArray(); }
    public static string Run(IProject project,SequencePayload seed,ISequenceDiagram diagram,string schema,string directory,StringBuilder log,Action<string,int> report)
    {
        var prior=seed.Ids.Select(project.GetModelById).ToArray();
        var priorNames=prior.Select(m=>m.Name).ToArray();
        var priorRelations=prior.Select(Relations).ToArray();
        var priorShapes=diagram.Shapes.Select(s=>s.Id+":"+s.ModelId).OrderBy(x=>x).ToArray();
        var message=prior[6] as IMessage;
        var input=SequenceDeltaInput.Build(seed,message.Metaclass.Id,schema);
        SequenceExperiment.Write(Path.Combine(directory,"delta-input.json"),input.Json);
        log.AppendLine("delta addition id="+input.Ids[0]);
        var result=project.ImportUnitFromJson(input.Json,null,null);
        if(result==null)throw new InvalidOperationException("E150: 差分追加の結果がnullです。");
        report(result.State,result.Errors.Count());
        log.AppendLine("delta import state="+result.State);
        foreach(var error in result.Errors)log.AppendLine(error.Kind+": "+error.Message);
        if(result.State!="success" || result.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))throw new InvalidOperationException("E151: 差分追加が失敗しました。");
        var added=project.GetModelById(input.Ids[0]) as IMessage;
        var view=result.ImportedEditors.OfType<ISequenceDiagram>().SingleOrDefault(d=>d.Id==diagram.Id);
        if(added==null || added.Name!="deltaProbe()" || added.Sender==null || added.Receiver==null || added.Sender.Id!=seed.Ids[2] || added.Receiver.Id!=seed.Ids[3]
           || view==null || view.Messages.Count()!=2 || !priorShapes.All(x=>view.Shapes.Any(sh=>sh.Id+":"+sh.ModelId==x)))
            throw new InvalidOperationException("E152: 追加または既存図形の保持を確認できませんでした。");
        for(int i=0;i<prior.Length;i++)
            if(project.GetModelById(seed.Ids[i])==null || prior[i].Name!=priorNames[i] || !priorRelations[i].All(r=>Relations(prior[i]).Contains(r)))
                throw new InvalidOperationException("E153: 差分追加で既存モデル・関連が変化しました。");
        log.AppendLine("sparse import and existing identities: verified");
        // Limit the documented verification suspension to deleting our own temporary message.
        using(project.SuspendModelVerification()) { added.Delete(); }
        var remaining=project.GetModelById(input.Ids[0]);
        var root=project.GetModelById(seed.Ids[0]) as IInteraction;
        view=root==null?null:root.GetEditors().OfType<ISequenceDiagram>().SingleOrDefault(d=>d.Id==diagram.Id);
        log.AppendLine("after delete model="+(remaining==null?"absent":remaining.IsDeleted?"deleted":"live")+" root messages="+(root==null?"missing":root.Messages.Count().ToString()));
        if(view==null)throw new InvalidOperationException("E154: 削除後の図を再取得できませんでした。");
        var afterShapes=view.Shapes.Select(sh=>sh.Id+":"+sh.ModelId).OrderBy(x=>x).ToArray();
        log.AppendLine("after delete shape messages="+view.Messages.Count()+" missing shapes="+string.Join(",",priorShapes.Except(afterShapes))+" extra shapes="+string.Join(",",afterShapes.Except(priorShapes)));
        if((remaining!=null && !remaining.IsDeleted) || root.Messages.Any(m=>m.Id==input.Ids[0]))
            throw new InvalidOperationException("E154: 削除対象のメッセージモデルが残っています。");
        if(!priorShapes.SequenceEqual(afterShapes))
        {
            // Model deletion need not update the stored editor. Do not repair unrelated differences.
            if(priorShapes.Except(afterShapes).Any() || afterShapes.Except(priorShapes).Any(x=>!x.EndsWith(":"+input.Ids[0],StringComparison.Ordinal)))
                throw new InvalidOperationException("E154: 削除で既存図形に差異が発生しました。");
            string editorJson=SequenceDeltaInput.RestoreEditor(seed,schema);
            SequenceExperiment.Write(Path.Combine(directory,"delta-delete-editor.json"),editorJson);
            report("未取得（削除後の図形反映）",0);
            var refreshed=project.ImportUnitFromJson(editorJson,null,null);
            if(refreshed==null)throw new InvalidOperationException("E156: 削除後の図形反映結果がnullです。");
            report(refreshed.State,refreshed.Errors.Count());
            log.AppendLine("delete editor import state="+refreshed.State);
            foreach(var error in refreshed.Errors)log.AppendLine(error.Kind+": "+error.Message);
            if(refreshed.State!="success" || refreshed.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))throw new InvalidOperationException("E156: 削除後の図形反映に失敗しました。");
            view=root.GetEditors().OfType<ISequenceDiagram>().SingleOrDefault(d=>d.Id==diagram.Id);
        }
        remaining=project.GetModelById(input.Ids[0]);
        log.AppendLine("final delete model="+(remaining==null?"absent":remaining.IsDeleted?"deleted":"live")+" root messages="+root.Messages.Count()+" shape messages="+(view==null?"missing":view.Messages.Count().ToString()));
        if(view!=null)log.AppendLine("final missing shapes="+string.Join(",",priorShapes.Except(view.Shapes.Select(sh=>sh.Id+":"+sh.ModelId)))+" extra shapes="+string.Join(",",view.Shapes.Select(sh=>sh.Id+":"+sh.ModelId).Except(priorShapes)));
        if((remaining!=null && !remaining.IsDeleted) || root.Messages.Count()!=1 || view==null || view.Messages.Count()!=1 || !priorShapes.SequenceEqual(view.Shapes.Select(sh=>sh.Id+":"+sh.ModelId).OrderBy(x=>x)))
            throw new InvalidOperationException("E157: 削除後のモデル・図形の最終照合が一致しません。");
        for(int i=0;i<prior.Length;i++)
            if(project.GetModelById(seed.Ids[i])==null || prior[i].Name!=priorNames[i] || !priorRelations[i].SequenceEqual(Relations(prior[i])))
                throw new InvalidOperationException("E155: 削除後の既存モデル・関連が一致しません。");
        log.AppendLine("single-message deletion with verification scope and preservation: verified");
        return input.Ids[0];
    }
}

public class SequenceReplacement
{
    public SequenceIdentity Identity;
    public string RootName,FrameName,Before;
    public string[] Children,AllIds,RootRelations,ShapeIds,ExternalIds;
    static IEnumerable<IModel> Tree(IModel model)
    { yield return model; foreach(var child in model.GetChildren())foreach(var nested in Tree(child))yield return nested; }
    static string[] Relations(IModel model)
    { return model.GetRelationsWhere((r,f)=>true).Select(r=>r.Id+":"+r.Source.Id+":"+r.Target.Id).Distinct().OrderBy(x=>x).ToArray(); }
    static string Fingerprint(IModel root)
    { return string.Join("\n",Tree(root).OrderBy(m=>m.Id).Select(m=>m.Id+"|"+m.Metaclass.Id+"|"+m.Name+"|"+string.Join(",",Relations(m)))); }
    public static SequenceReplacement Capture(IInteraction root,ISequenceDiagram diagram)
    {
        var models=Tree(root).ToArray(); var ids=new HashSet<string>(models.Select(m=>m.Id));
        if(models.Any(m=>m.IsDeleted || m.IsProxy || !m.IsEditable))throw new InvalidOperationException("E140: 更新対象に編集不可・未読込の要素があります。");
        // Owned children are replaced even when they reference external models.
        // Keep the external endpoints themselves and verify they survive deletion.
        var externalIds=models.Where(m=>m.Id!=root.Id && m.Id!=root.Frame.Id)
            .SelectMany(m=>m.GetRelationsWhere((r,f)=>true))
            .SelectMany(r=>new[]{r.Source.Id,r.Target.Id}).Where(id=>!ids.Contains(id)).Distinct().ToArray();
        return new SequenceReplacement {
            Identity=new SequenceIdentity{Root=root.Id,Frame=root.Frame.Id,FrameRelation=root.Frame.GetOwnerRelationship().Id,Editor=diagram.Id,FrameShape=diagram.Frame.Id},
            RootName=root.Name,FrameName=root.Frame.Name,Before=Fingerprint(root),ExternalIds=externalIds,
            Children=root.GetChildren().Where(m=>m.Id!=root.Frame.Id).Select(m=>m.Id).ToArray(),
            AllIds=models.Select(m=>m.Id).ToArray(),RootRelations=Relations(root).Where(r=>!models.Where(m=>m.Id!=root.Id && m.Id!=root.Frame.Id).Any(m=>r.Contains(m.Id))).ToArray(),
            ShapeIds=diagram.Shapes.Select(x=>x.Id).OrderBy(x=>x).ToArray()
        };
    }
    public void CheckUnchanged(IInteraction root,ISequenceDiagram diagram)
    {
        if(root==null || root.Id!=Identity.Root || diagram==null || diagram.Id!=Identity.Editor || Fingerprint(root)!=Before || !ShapeIds.SequenceEqual(diagram.Shapes.Select(x=>x.Id).OrderBy(x=>x)))
            throw new InvalidOperationException("E143: 確認中に更新対象が変わりました。やり直してください。");
    }
    public void DeleteChildren(IProject project)
    { foreach(string id in Children){ var model=project.GetModelById(id); if(model!=null && !model.IsDeleted)model.Delete(); } }
    public void Verify(IProject project,IUnitImportResult result,SequencePayload payload)
    {
        if(ExternalIds.Any(id=>project.GetModelById(id)==null || project.GetModelById(id).IsDeleted))
            throw new InvalidOperationException("E148: 関連先の外部モデルが失われました。");
        var root=project.GetModelById(Identity.Root) as IInteraction;
        var diagram=result.ImportedEditors.OfType<ISequenceDiagram>().SingleOrDefault(d=>d.Id==Identity.Editor && d.ModelId==Identity.Root);
        if(root==null || root.Frame==null || root.Frame.Id!=Identity.Frame || root.Frame.GetOwnerRelationship().Id!=Identity.FrameRelation || diagram==null || diagram.Frame.Id!=Identity.FrameShape)
            throw new InvalidOperationException("E144: 図・フレーム・エディタのIDを維持できませんでした。");
        if(!RootRelations.All(r=>Relations(root).Contains(r)))throw new InvalidOperationException("E145: 図本体の関連を維持できませんでした。");
        if(AllIds.Where(id=>id!=Identity.Root && id!=Identity.Frame).Any(id=>project.GetModelById(id)!=null))
            throw new InvalidOperationException("E146: 更新前の子要素が残っています。");
        if(!new HashSet<string>(Tree(root).Select(m=>m.Id)).SetEquals(payload.Ids) || diagram.Shapes.Any(x=>x.Model!=null && !payload.Ids.Contains(x.Model.Id)))
            throw new InvalidOperationException("E147: 更新後の要素・図形に余剰があります。");
    }
}

public static class PumlRuntime
{
    static IField Field(IClass c,string name) { return c.GetFields().Cast<IField>().FirstOrDefault(f=>f.Name==name); }
    static string Literal(IClass c,string field,string value)
    {
        var f=Field(c,field);
        var found=f==null || f.TypeEnum==null ? null : f.TypeEnum.Literals.FirstOrDefault(l=>string.Equals(l.Name,value,StringComparison.OrdinalIgnoreCase));
        if(found==null)throw new InvalidOperationException("E121: 種別を取得できません: "+field+" / "+value);
        return found.Name;
    }
    static IClass Child(PumlProfile p,IClass c,string key,string field,string relation)
    {
        var f=c.GetFields().Cast<IField>().FirstOrDefault(v=>v.RelationshipClass!=null && v.RelationshipClass.Id==SequencePayload.Prefix+relation) ?? Field(c,field);
        if(f==null || f.TypeClass==null || f.RelationshipClass==null)throw new InvalidOperationException("E121: 所有フィールドを取得できません: "+field);
        p.Relations[key]=f.RelationshipClass.Id; return f.TypeClass;
    }
    static IClass Concrete(IEnumerable<IModel> models,IClass fallback,string label)
    {
        var types=models.Select(m=>m.Metaclass).GroupBy(c=>c.Id).Select(g=>g.First()).ToArray();
        if(types.Length>1)throw new InvalidOperationException("E121: 見本の"+label+"に複数の型があり、自動選択できません。");
        return types.Length==1?types[0]:fallback;
    }
    public static PumlProfile Profile(ISequenceDiagram diagram,IModel[] source,PumlPlan plan)
    {
        var p=new PumlProfile();
        string[] names={"Interaction","Frame","Lifeline","Lifeline","ExecutionSpecification","ExecutionSpecification","Message"};
        for(int i=0;i<source.Length;i++)p.Types[names[i]]=source[i].Metaclass.Id;
        string[] keys={"Frame","Lifelines","ExecutionSpecifications","Messages","OwnedExecutionSpecification","SendMessage","ReceiveMessage"};
        for(int i=0;i<keys.Length;i++)p.Relations[keys[i]]=SequencePayload.Prefix+SequencePayload.RelationTypes[i];
        p.Sync=Literal(source[6].Metaclass,"MessageSort","Sync");
        if(plan.All().Any(n=>n.Kind=="async"))p.Async=Literal(source[6].Metaclass,"MessageSort","Async");
        if(plan.All().Any(n=>n.Kind=="reply"))p.Reply=Literal(source[6].Metaclass,"MessageSort","Reply");
        var classes=new List<IClass>(source.Select(m=>m.Metaclass));
        if(plan.All().Any(n=>n.Kind=="destroy"))
        {
            var c=Child(p,source[0].Metaclass,"Destructions","Destructions","___Interaction_Destruction");
            var definitions=diagram.EditorDefinition.Elements
                .Where(e=>string.Equals(e.Type,"Destruction",StringComparison.OrdinalIgnoreCase) && e.ModelClass!=null)
                .Select(e=>e.ModelClass).GroupBy(t=>t.Id).Select(g=>g.First()).ToArray();
            var observed=diagram.Destructions.Select(e=>e.Model.Metaclass).GroupBy(t=>t.Id).Select(g=>g.First()).ToArray();
            string selected=PumlTypeSelection.Destruction(definitions.Select(t=>t.Id).ToArray(),observed.Select(t=>t.Id).ToArray());
            c=definitions.Concat(observed).First(t=>t.Id==selected);
            p.Types["Destruction"]=c.Id;
            Child(p,c,"DestructionTargetLifeline","Lifeline","DestructionTargetLifeline");
        }
        if(plan.All().Any(n=>n.Left=="[" || n.Right=="]"))
        {
            var c=Child(p,source[0].Metaclass,"MessageEnds","MessageEnds","___Interaction_MessageEnd");
            c=Concrete(diagram.MessageEnds.Select(e=>e.Model),c,"メッセージ端");
            p.Types["MessageEnd"]=c.Id; classes.Add(c);
        }
        if(plan.All().Any(n=>n.Kind=="fragment"))
        {
            var c=Child(p,source[0].Metaclass,"Fragments","CombinedFragments","___Interaction_CombinedFragment");
            c=Concrete(diagram.Fragments.Select(f=>f.Model),c,"複合フラグメント");
            p.Types["CombinedFragment"]=c.Id; classes.Add(c);
            var operand=Child(p,c,"Operands","Operands","___CombinedFragment_InteractionOperand");
            operand=Concrete(diagram.Fragments.Where(f=>f.Model.Metaclass.Id==c.Id).SelectMany(f=>f.Operands).Select(o=>o.Model),operand,"分岐");
            p.Types["InteractionOperand"]=operand.Id; classes.Add(operand);
            foreach(var op in plan.All().Where(n=>n.Kind=="fragment").Select(n=>n.Operator).Distinct())p.Operators[op]=Literal(c,"Operator",op);
        }
        if(plan.All().Any(n=>n.Kind=="ref"))
        {
            var c=Child(p,source[0].Metaclass,"InteractionUses","InteractionUses","___Interaction_InteractionUse");
            if(!diagram.InteractionUses.Any())throw new InvalidOperationException("E121: 型の見本が必要です。ref（相互作用の利用）がある既存の図を開いてから取り込んでください。");
            c=Concrete(diagram.InteractionUses.Select(f=>f.Model),c,"相互作用の利用");
            p.Types["InteractionUse"]=c.Id; classes.Add(c);
        }
        if(plan.All().Any(n=>n.Kind=="note"))
        {
            var c=Child(p,source[0].Metaclass,"Notes","Notes","___Interaction_InteractionNote");
            if(!diagram.Notes.Any())throw new InvalidOperationException("E121: 型の見本が必要です。Note（ノート）がある既存の図を開いてから取り込んでください。");
            c=Concrete(diagram.Notes.Select(n=>n.Model),c,"Note");
            p.Types["InteractionNote"]=c.Id; classes.Add(c);
            var f=Field(c,"Body") ?? Field(c,"Text") ?? Field(c,"Name");
            if(f==null)throw new InvalidOperationException("E121: Note本文フィールドを取得できません。");
            p.NoteField=f.Name; p.NoteStorage=f.Type;
            if(p.NoteStorage!="String" && p.NoteStorage!="RichText")throw new InvalidOperationException("E121: Note本文の型が未対応です。");
        }
        foreach(string key in new[]{"CrossingFragmentCoveredLifeline","OperandTargetMessage","NestedInteractionFragment"})
        {
            var f=classes.SelectMany(c=>c.GetFields().Cast<IField>()).FirstOrDefault(v=>v.RelationshipClass!=null && v.RelationshipClass.Id==SequencePayload.Prefix+key);
            if(f!=null)p.Relations[key]=f.RelationshipClass.Id;
        }
        return p;
    }
    static void Require(bool condition,string text) { if(!condition)throw new InvalidOperationException("E122: 読戻し不一致: "+text); }
    static string Normalize(string text) { return (text??"").Replace("\r\n","\n"); }
    public static void Verify(IProject project,IUnitImportResult result,SequencePayload p,string owner)
    {
        var root=project.GetModelById(p.Ids[0]) as IInteraction;
        Require(root!=null && root.Owner!=null && root.Owner.Id==owner && root.Name==p.Name,"図の所属・名前");
        var d=result.ImportedEditors.OfType<ISequenceDiagram>().SingleOrDefault(e=>e.ModelId==p.Ids[0]);
        Require(d!=null,"シーケンスエディタ");
        foreach(string id in p.Ids)Require(project.GetModelById(id)!=null,"生成モデルの存在");
        Require(root.Lifelines.Count()==p.Expected.Count(e=>e.Kind=="lifeline") && root.Messages.Count()==p.Expected.Count(e=>e.Kind=="sync" || e.Kind=="async" || e.Kind=="reply"),"相互作用の要素数");
        Require(d.Lifelines.Count()==p.Expected.Count(e=>e.Kind=="lifeline") && d.Messages.Count()==p.Expected.Count(e=>e.Kind=="sync" || e.Kind=="async" || e.Kind=="reply"),"ライフライン・メッセージ数");
        Require(d.Fragments.Count()==p.Expected.Count(e=>e.Kind=="fragment") && d.Notes.Count()==p.Expected.Count(e=>e.Kind=="note") && d.InteractionUses.Count()==p.Expected.Count(e=>e.Kind=="ref"),
            "シェイプ数（期待/取得）\n複合フラグメント: "+p.Expected.Count(e=>e.Kind=="fragment")+"/"+d.Fragments.Count()
            +" / ref: "+p.Expected.Count(e=>e.Kind=="ref")+"/"+d.InteractionUses.Count()
            +" / Note: "+p.Expected.Count(e=>e.Kind=="note")+"/"+d.Notes.Count());
        Require(root.MessageEnds.Count()==p.Expected.Count(e=>e.Kind=="messageEnd") && d.MessageEnds.Count()==p.Expected.Count(e=>e.Kind=="messageEnd"),"独立メッセージ端の数");
        Require(d.Destructions.Count()==p.Expected.Count(e=>e.Kind=="destruction"),"破棄点の数（期待/取得）: "+p.Expected.Count(e=>e.Kind=="destruction")+"/"+d.Destructions.Count());
        foreach(var e in p.Expected)
        {
            var model=project.GetModelById(e.Id); Require(model!=null && !model.IsDeleted,e.Kind+"モデル");
            if(e.Kind=="sync" || e.Kind=="async" || e.Kind=="reply")
            {
                var m=model as IMessage;
                Require(m!=null && m.Kind==e.Kind && m.Name==e.Text && (e.Left==null ? m.Sender==null && m.SendPortType=="MessageEnd" : m.Sender!=null && m.Sender.Id==e.Left) && (e.Right==null ? m.Receiver==null && m.ReceivePortType=="MessageEnd" && m.IsLost : m.Receiver!=null && m.Receiver.Id==e.Right),"メッセージ種別・本文・送受信");
                Require(m.SendPort!=null && m.ReceivePort!=null && ((IModel)m.SendPort).Id==e.SendPort && ((IModel)m.ReceivePort).Id==e.ReceivePort,"実行区間・メッセージ端・フレームへの接続");
                var shape=d.Messages.SingleOrDefault(v=>v.Model.Id==e.Id);
                Require(shape!=null && Math.Abs(shape.SourceY-e.Y)<1 && Math.Abs(shape.TargetY-e.EndY)<1,"メッセージ位置・折返し");
                if(e.Left==null || e.Right==null)
                {
                    var endpoint=d.MessageEnds.Single(v=>v.Model.Id==(e.Left==null?e.SendPort:e.ReceivePort));
                    Require(Math.Abs(shape.SourceY-shape.TargetY)<1 && Math.Abs(endpoint.LocationY-shape.SourceY)<1,"図外メッセージの水平配置");
                }
            }
            else if(e.Kind=="destruction")
            {
                var destruction=model as IDestruction;
                var shape=d.Destructions.SingleOrDefault(v=>v.Model.Id==e.Id);
                Require(destruction!=null && destruction.Lifeline!=null && destruction.Lifeline.Id==e.Left && shape!=null && Math.Abs(shape.LocationY-e.Y)<1,"破棄位置・ライフライン");
            }
            else if(e.Kind=="messageEnd")
            {
                var end=model as IMessageEnd;
                var shape=d.MessageEnds.SingleOrDefault(v=>v.Model.Id==e.Id);
                Require(end!=null && end.Message!=null && shape!=null && Math.Abs(shape.LocationX-e.X)<1 && Math.Abs(shape.LocationY-e.Y)<1,"独立メッセージ端の位置・接続");
            }
            else if(e.Kind=="execution")
            {
                var shape=d.ExecutionSpecifications.SingleOrDefault(v=>v.Model.Id==e.Id);
                Require(shape!=null && Math.Abs(shape.Length-(e.EndY-e.Y))<1,"実行区間の長さ");
            }
            else if(e.Kind=="lifeline")Require(model.Name==e.Text,"参加者名");
            else if(e.Kind=="fragment")Require(Convert.ToString(model.GetField("Operator"))==e.Operator,"複合フラグメント種別");
            else if(e.Kind=="operand")
            {
                var shape=d.Fragments.SelectMany(f=>f.Operands).SingleOrDefault(o=>o.Model.Id==e.Id);
                Require(shape!=null && Normalize(shape.Guard)==Normalize(e.Text) && shape.OwnerFragment.Model.Id==e.Owner,"条件・所属");
                var ids=new HashSet<string>(shape.Messages.Select(m=>m.Model.Id));
                Require(p.Expected.Where(m=>m.Owner==e.Id && (m.Kind=="sync" || m.Kind=="async" || m.Kind=="reply")).All(m=>ids.Contains(m.Id)),"分岐内メッセージ");
            }
            else if(e.Kind=="ref")
            {
                var shape=d.InteractionUses.SingleOrDefault(v=>v.Model.Id==e.Id);
                Require(shape!=null && Normalize(shape.Text)==Normalize(e.Text),"ref本文");
            }
            else if(e.Kind=="note")
            {
                if(p.ImportProfile.NoteStorage=="RichText")model.SetRichTextField(p.ImportProfile.NoteField,e.Text);
                var shape=d.Notes.SingleOrDefault(v=>v.Model.Id==e.Id);
                Require(shape!=null && Normalize(shape.Text)==Normalize(e.Text),"Note本文");
            }
        }
    }
}

// Pure JSON builder. Only newly generated entity IDs appear as relation endpoints.

public static class SequenceMappedUpdate
{
    static IEnumerable<IModel> Tree(IModel root)
    { yield return root; foreach(var child in root.GetChildren())foreach(var model in Tree(child))yield return model; }
    static string Name(IModel model,Dictionary<string,string> changes)
    { string name; return changes!=null && changes.TryGetValue(model.Id,out name)?name:model.Name; }
    static string Number(double value) { return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture); }
    static string Signature(IInteraction root,ISequenceDiagram diagram,Dictionary<string,string> changes=null,HashSet<string> removed=null)
    {
        var rows=new List<string>();
        removed=removed??new HashSet<string>();
        var owned=Tree(root).Where(m=>!removed.Contains(m.Id)).ToArray();var ownedIds=new HashSet<string>(owned.Select(m=>m.Id));
        foreach(var model in owned.OrderBy(m=>m.Id))
        {
            rows.Add(PumlBuild.Json(PumlBuild.Obj("id",model.Id,"class",model.Metaclass.Id,"name",Name(model,changes),"owner",model.Owner==null?null:model.Owner.Id)));
            foreach(var r in model.GetRelationsWhere((r,f)=>true).Where(r=>!removed.Contains(r.Source.Id) && !removed.Contains(r.Target.Id)).OrderBy(r=>r.Id))
            {
                rows.Add(r.Id+":"+r.Source.Id+":"+r.Target.Id);
                foreach(var endpoint in new[]{r.Source,r.Target}.Where(m=>!ownedIds.Contains(m.Id)))
                    rows.Add(PumlBuild.Json(PumlBuild.Obj("external",endpoint.Id,"class",endpoint.Metaclass.Id,"name",endpoint.Name,"deleted",endpoint.IsDeleted)));
            }
        }
        foreach(var shape in diagram.Shapes.Where(s=>!removed.Contains(s.ModelId)).OrderBy(s=>s.Id))
        {
            rows.Add("shape:"+shape.Id+":"+shape.ModelId);
            var visual=shape as IShape;
            if(visual!=null && visual.Style!=null)
                rows.Add(PumlBuild.Json(PumlBuild.Obj("styleOf",shape.Id,"back",visual.Style.BackColor,"fore",visual.Style.ForeColor,"border",visual.Style.BorderColor,"quick",visual.Style.QuickStyle,"thickness",visual.Style.BorderThickness,"line",visual.Style.BorderStyle)));
            var node=shape as ISequenceNodeShape;
            if(node!=null)rows.Add("bounds:"+Number(node.LocationX)+":"+Number(node.LocationY)+":"+Number(node.Width)+":"+Number(node.Height));
        }
        foreach(var m in diagram.Messages.Where(m=>!removed.Contains(m.ModelId)).OrderBy(m=>m.Id))
        {
            var model=m.Model as IMessage;
            if(model==null)throw new InvalidOperationException("E160: メッセージのモデルを取得できません。");
            rows.Add(m.Id+":"+model.Kind+":"+Number(m.SourceY)+":"+Number(m.TargetY)+":"+Number(m.SelfloopBendsX));
        }
        foreach(var f in diagram.Fragments.OrderBy(f=>f.Id))
        {
            rows.Add(f.Id+":"+f.Text);
            foreach(var o in f.Operands.OrderBy(o=>o.Id))rows.Add(o.Id+":"+o.Model.Id+":"+o.Guard+":"+Number(o.Position));
        }
        foreach(var n in diagram.Notes.OrderBy(n=>n.Id))rows.Add(n.Id+":"+n.Text);
        foreach(var u in diagram.InteractionUses.OrderBy(u=>u.Id))rows.Add(u.Id+":"+u.Text);
        foreach(var l in diagram.Lifelines.OrderBy(l=>l.Id))rows.Add(l.Id+":"+Number(l.TimelineLength));
        foreach(var e in diagram.ExecutionSpecifications.OrderBy(e=>e.Id))rows.Add(e.Id+":"+Number(e.Length));
        return SequenceMapFile.Hash(string.Join("\n",rows));
    }
    static string ReadInput(IApplication app,string title)
    {
        string path=app.Window.UI.ShowOpenFileDialog(title,"PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
        if(string.IsNullOrEmpty(path))throw new OperationCanceledException();
        if(new FileInfo(path).Length>300000)throw new InvalidOperationException("E160: PlantUMLは300KB以下にしてください。");
        return path;
    }
    static double MessageX(IMessageShape message)
    {
        var send=message.SendPort as ISequenceNodeShape;
        if(send!=null)return send.LocationX;
        var receive=message.ReceivePort as ISequenceNodeShape;
        return receive==null?0:receive.LocationX;
    }
    static SequenceMapFile Bind(IApplication app,IProject project,IInteraction root,ISequenceDiagram diagram,string source,string before,StringBuilder detail,out string coverage)
    {
        var plan=PumlPlan.ParseForMapping(source); var nodes=SequenceNameDiff.Messages(plan);
        coverage="";
        detail.AppendLine("Binding counts: input participants="+plan.Aliases.Count+", model participants="+root.Lifelines.Count()+", shape participants="+diagram.Lifelines.Count()+", input messages="+nodes.Length+", model messages="+root.Messages.Count()+", shape messages="+diagram.Messages.Count());
        var lines=new Dictionary<string,string>();
        for(int i=0;i<plan.Aliases.Count;i++)
        {
            var available=diagram.Lifelines.Where(l=>l.Model!=null && !lines.Values.Contains(l.Model.Id))
                .GroupBy(l=>l.Model.Id).Select(g=>g.First()).OrderBy(l=>l.LocationX).ToArray();
            var exact=available.Where(l=>l.Model.Name==plan.Names[i] || l.Text==plan.Names[i]).ToArray();
            var candidates=exact.Length>0?exact:available.Where(l=>SequenceParticipantMatch.Equivalent(l.Model.Name,plan.Names[i]) || SequenceParticipantMatch.Equivalent(l.Text,plan.Names[i])).ToArray();
            detail.AppendLine("Participant alias="+plan.Aliases[i]+", input="+PumlBuild.Json(plan.Names[i])+", exact="+exact.Length+", normalized="+(exact.Length==0?candidates.Length:0));
            string chosen=null;
            if(candidates.Length==1)chosen=candidates[0].Model.Id;
            else
            {
                // No guessing by declaration order, short class name, or substring.
                // Offer unmatched lifelines when labels are entirely different.
                var choices=candidates.Concat(available.Where(l=>!candidates.Any(c=>c.Model.Id==l.Model.Id))).ToArray();
                foreach(var candidate in choices)
                {
                    string context=string.Join("\n",diagram.Messages.Where(m=>{
                        var message=m.Model as IMessage;
                        return message!=null && ((message.Sender!=null && message.Sender.Id==candidate.Model.Id) || (message.Receiver!=null && message.Receiver.Id==candidate.Model.Id));
                    }).OrderBy(m=>m.SourceY).Take(4).Select(m=>m.Model.Name));
                    detail.AppendLine("Participant candidate id="+candidate.Model.Id+", name="+PumlBuild.Json(candidate.Model.Name)+", text="+PumlBuild.Json(candidate.Text));
                    if(app.Window.UI.ShowConfirmDialog("参加者の対応先を選んでください。\nPlantUML: "+plan.Names[i]+"\n別名: "+plan.Aliases[i]+"\n図の表示: "+candidate.Text+"\nモデル名: "+candidate.Model.Name+"\n図内X位置: "+Number(candidate.LocationX)+"\n接続メッセージ例:\n"+context+"\nこの参加者に対応付けますか？「いいえ」で次の候補。全候補を断るとキャンセルします。",SequenceExperiment.Title))
                    { chosen=candidate.Model.Id;break; }
                }
                if(chosen==null)throw new OperationCanceledException();
            }
            detail.AppendLine("Participant selected="+chosen);
            lines.Add(plan.Aliases[i],chosen);
        }
        var ordered=SequenceExportMatch.Order(diagram.Messages,m=>m.SourceY,m=>MessageX(m),m=>m.Id).ToArray();
        var inputKeys=nodes.Select(n=>SequenceExportMatch.Key(n.Kind,n.Left=="["?null:lines[n.Left],n.Right=="]"?null:lines[n.Right])).ToArray();
        var diagramKeys=ordered.Select(m=>SequenceExportMatch.Key((m.Model as IMessage)==null?null:((IMessage)m.Model).Kind,m.Sender==null?null:m.Sender.Model.Id,m.Receiver==null?null:m.Receiver.Model.Id)).ToArray();
        var correspondence=SequenceExportMatch.Align(inputKeys,nodes.Select(n=>n.Text).ToArray(),diagramKeys,ordered.Select(m=>m.Text).ToArray());
        var ids=new List<string>();int renamed=0;
        for(int index=0;index<nodes.Length;index++)
        {
            var node=nodes[index];int match=correspondence[index];
            if(match<0)
            {
                detail.AppendLine("Unmatched message line="+node.Line+", kind="+node.Kind+", left="+node.Left+", right="+node.Right+", text="+PumlBuild.Json(node.Text));
                throw new InvalidOperationException("E163: "+node.Line+"行目の送受信先・種別・並びに対応する図側メッセージがありません。図側の欠落または構造変更の可能性があります。本文の違いだけでは停止しません。");
            }
            var shape=ordered[match];string id=shape.Model.Id;
            if(ids.Contains(id))throw new InvalidOperationException("E168: 同じメッセージモデルを複数の入力行へ対応付けできません。");
            bool different=SequenceExportMatch.Text(node.Text)!=SequenceExportMatch.Text(shape.Text);
            if(different)renamed++;
            detail.AppendLine("Message auto-bound line="+node.Line+", model="+id+", shape="+shape.Id+", text difference="+different+", input="+PumlBuild.Json(node.Text)+", diagram="+PumlBuild.Json(shape.Text));
            ids.Add(id);
        }
        var extraMessages=SequenceExportMatch.Unmapped(root.Messages.Select(m=>m.Id).Concat(diagram.Messages.Select(m=>m.Model.Id)),ids);
        var extraLines=SequenceExportMatch.Unmapped(root.Lifelines.Select(m=>m.Id).Concat(diagram.Lifelines.Select(m=>m.Model.Id)),lines.Values);
        coverage="\n本文の差分を検出: "+renamed+"件（対応表作成では変更しません）\n対応表に含まれない図側の要素: 参加者 "+extraLines.Length+"件 / メッセージ "+extraMessages.Length+"件";
        if(extraLines.Length>0 || extraMessages.Length>0)coverage+="\nこの作成操作では削除しません。「メッセージを差分更新」で余剰メッセージを削除できます。参加者の削除は未対応です。";
        detail.AppendLine("Unmapped lifelines="+string.Join(",",extraLines));
        detail.AppendLine("Unmapped messages="+string.Join(",",extraMessages));
        return new SequenceMapFile{Project=project.Id,Root=root.Id,Editor=diagram.Id,Source=source,Fingerprint=before,MessageIds=ids.ToArray()};
    }
    static void CheckContext(IApplication app,IProject project,IInteraction root,ISequenceDiagram diagram,string signature)
    {
        if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=diagram.Id || root.IsDeleted || Signature(root,diagram)!=signature)
            throw new InvalidOperationException("E164: 確認中に図が変わりました。処理を中止します。");
    }
    public static void Run(IApplication app,bool initialize)
    {
        IUndoTransaction transaction=null; var completion=new SequenceCompletion(); bool committed=false,prepared=false,rollbackRestored=false;
        string pending=null,original=null; IInteraction root=null; ISequenceDiagram diagram=null;
        IProject project=null;SequenceEditorDocument originalEditor=null;
        var detail=new StringBuilder();
        try
        {
            project=app.Workspace.CurrentProject;
            diagram=app.Workspace.CurrentEditor as ISequenceDiagram;root=diagram==null?null:diagram.Model as IInteraction;
            if(project==null || root==null || root.IsDeleted || root.IsProxy)throw new InvalidOperationException("E160: 更新対象のシーケンス図を開いてください。");
            original=Signature(root,diagram);
            string input=ReadInput(app,initialize?"現在の図に対応する更新前のPlantUML":"更新後のPlantUML");
            string source=File.ReadAllText(input,new UTF8Encoding(false,true));
            PumlPlan.ParseForMapping(source);
            if(initialize)
            {
                string coverage;
                var map=Bind(app,project,root,diagram,source,original,detail,out coverage);
                string path=app.Window.UI.ShowSaveFileDialog("対応表の保存先（既存ファイルは上書きしません）","対応表 (*.ndmap.xml)|*.ndmap.xml",Path.ChangeExtension(input,"ndmap.xml"));
                if(string.IsNullOrEmpty(path))throw new OperationCanceledException();
                CheckContext(app,project,root,diagram,original);
                if(string.Equals(Path.GetFullPath(path),Path.GetFullPath(input),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("E165: PlantUMLと別の保存先を指定してください。");
                SequenceMapFile.WriteNew(path,map);
                SequenceExperiment.Summary="対応表を作成しました。\n対応付け済みメッセージ: "+map.MessageIds.Length+"件"+coverage+"\n図とPlantUMLは変更していません。\n保存先: "+path;
                detail.AppendLine("Mapping initialized; no model writes. "+path);
            }
            else
            {
                string path=app.Window.UI.ShowOpenFileDialog("この図の対応表","対応表 (*.xml;*.bak;*.pending)|*.xml;*.bak;*.pending");
                if(string.IsNullOrEmpty(path))throw new OperationCanceledException();
                var map=SequenceMapFile.Read(path);
                if(map.Project!=project.Id || map.Root!=root.Id || map.Editor!=diagram.Id)throw new InvalidOperationException("E166: 対応表が別のプロジェクトまたは図のものです。");
                // The saved fingerprint is historical. Normal diagram edits do not invalidate the map.
                detail.AppendLine("Diagram changed since baseline="+(map.Fingerprint!=original));
                var plan=SequenceMessagePlan.Build(map.Source,source);
                var requested=plan.Targets;
                var retainedIds=plan.Retained.Select(i=>map.MessageIds[i]).ToArray();
                var before=SequenceNameDiff.Messages(PumlPlan.ParseForMapping(map.Source));
                if(before.Length!=map.MessageIds.Length)throw new InvalidOperationException("E168: 対応表の件数が不正です。");
                var currentNames=new Dictionary<int,string>();
                foreach(var edit in requested)
                {
                    var m=project.GetModelById(map.MessageIds[edit.Index]) as IMessage;
                    if(m==null || m.IsDeleted || m.Interaction==null || m.Interaction.Id!=root.Id)
                        throw new InvalidOperationException("E168: "+edit.Line+"行目の更新対象は図側で削除または移動されています。この版の本文更新では再作成できません。");
                    var shapes=diagram.Messages.Where(s=>s.Model.Id==m.Id).ToArray();
                    if(shapes.Length!=1)throw new InvalidOperationException("E168: 更新対象のメッセージ図形を一意に取得できません。");
                    // Match the export-visible label, not the underlying Name field.
                    currentNames.Add(edit.Index,SequenceExportMatch.Text(shapes[0].Text));
                }
                var merge=SequenceNameMerge.Resolve(requested,currentNames);
                var edits=merge.Writes;
                var unmapped=SequenceExportMatch.Unmapped(root.Messages.Select(m=>m.Id).Concat(diagram.Messages.Select(m=>m.Model.Id)),retainedIds);
                string coverage="\nPlantUMLにないメッセージの削除: "+unmapped.Length+"件";
                detail.AppendLine("Unmapped messages="+string.Join(",",unmapped));
                detail.AppendLine("Name merge: requested="+requested.Count+", writes="+edits.Count+", PlantUML priority="+merge.Conflicts+", already matched="+merge.AlreadyMatched);
                if(edits.Count==0 && unmapped.Length==0 && plan.Retained.Length==map.MessageIds.Length && requested.All(e=>e.Before==e.After))
                {
                    SequenceExperiment.Summary="対応付け済みメッセージの本文差分なし。更新APIは呼び出していません。"+coverage+"\n追加・移動・実行区間等の同期は未対応です。\n図・PlantUML・対応表は変更していません。";
                    detail.AppendLine("No-op; no transaction or model write.");
                }
                else
                {
                    var names=edits.ToDictionary(e=>map.MessageIds[e.Index],e=>e.After);
                    var removed=new HashSet<string>(unmapped);
                    var deletionModels=unmapped.Select(id=>project.GetModelById(id) as IMessage).ToArray();
                    if(deletionModels.Any(m=>m==null || m.IsDeleted || !m.IsEditable || m.Interaction==null || m.Interaction.Id!=root.Id || m.GetChildren().Any()))
                        throw new InvalidOperationException("E181: 削除対象のメッセージが編集不可・別図所属・子要素ありのいずれかです。");
                    if(root.GetEditors().OfType<ISequenceDiagram>().Where(d=>d.Id!=diagram.Id).Any(d=>d.Messages.Any(m=>removed.Contains(m.ModelId))))
                        throw new InvalidOperationException("E181: 削除対象が別のシーケンス図にも表示されています。この版は複数図の同時削除に未対応です。");
                    foreach(var id in names.Keys)if(!project.GetModelById(id).IsEditable)throw new InvalidOperationException("E169: 更新対象のメッセージを編集できません。");
                    // Relations incident to deleted messages may disappear; their other endpoint models must survive.
                    var external=deletionModels.SelectMany(m=>m.GetRelationsWhere((r,f)=>true)).SelectMany(r=>new[]{r.Source,r.Target})
                        .Where(m=>!removed.Contains(m.Id)).GroupBy(m=>m.Id).Select(g=>g.First()).ToDictionary(m=>m.Id,m=>m.Metaclass.Id+":"+m.Name);
                    SequenceEditorDocument editorBefore=null,editorAfter=null;
                    if(unmapped.Length>0)
                    {
                        editorBefore=SequenceEditorCapture.Read(project,root,diagram,detail);originalEditor=editorBefore;
                        editorAfter=editorBefore.Without(unmapped);
                    }
                    string preview=string.Join("\n",edits.Take(10).Select(e=>e.Line+"行目: "+e.Before+" → "+e.After));
                    preview+="\n"+string.Join("\n",deletionModels.Take(10).Select(m=>"削除: "+m.Name));
                    if(!app.Window.UI.ShowConfirmDialog("図「"+root.Name+"」をPlantUMLに合わせます。\n本文更新: "+edits.Count+"件 / メッセージ削除: "+unmapped.Length+"件\n"+preview+"\n削除対象につながる関連も削除します。残す要素のID・配置・表示設定を照合します。プロジェクトは自動保存しません。実行しますか？",SequenceExperiment.Title))throw new OperationCanceledException();
                    CheckContext(app,project,root,diagram,original);
                    if(SequenceMapFile.Read(path).Serialize()!=map.Serialize())throw new InvalidOperationException("E175: 確認中に対応表が変更されました。");
                    if(editorBefore!=null && SequenceEditorCapture.Read(project,root,diagram,detail).Fingerprint()!=editorBefore.Fingerprint())
                        throw new InvalidOperationException("E164: 確認中に図の表示設定が変更されました。");
                    CheckContext(app,project,root,diagram,original);
                    string expected=Signature(root,diagram,names,removed);
                    pending=path+".pending";
                    var next=new SequenceMapFile{Project=map.Project,Root=map.Root,Editor=map.Editor,Source=source,Fingerprint=expected,MessageIds=retainedIds};
                    SequenceMapFile.WriteNew(pending,next);prepared=true;
                    detail.AppendLine("Prepared map: "+pending);
                    if(edits.Count>0 || unmapped.Length>0)
                    {
                        transaction=project.BeginUndoTransaction(false);
                        if(transaction==null)throw new InvalidOperationException("E174: トランザクションを開始できませんでした。");
                    }
                    foreach(var edit in edits)
                    {
                        string id=map.MessageIds[edit.Index];
                        project.GetModelById(id).SetField("Name",edit.After);
                        detail.AppendLine("Name updated: "+id);
                    }
                    if(unmapped.Length>0)
                    {
                        using(project.SuspendModelVerification())foreach(var message in deletionModels)message.Delete();
                        foreach(string id in unmapped)
                        {
                            var model=project.GetModelById(id);
                            if((model!=null && !model.IsDeleted) || root.Messages.Any(m=>m.Id==id))throw new InvalidOperationException("E182: 削除対象のモデルが残っています。");
                        }
                        var result=project.ImportUnitFromJson(editorAfter.ImportJson(),null,null);
                        if(result==null)throw new InvalidOperationException("E182: 削除後の図形反映結果がnullです。");
                        detail.AppendLine("Deletion editor import state="+result.State);
                        foreach(var error in result.Errors)detail.AppendLine(error.Kind+": "+error.Message);
                        if(result.State!="success" || result.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))throw new InvalidOperationException("E182: 削除後の図形反映に失敗しました。");
                    }
                    var fresh=root.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==diagram.Id);
                    if(editorAfter!=null && SequenceEditorCapture.Read(project,root,fresh,detail).Fingerprint()!=editorAfter.Fingerprint())
                        throw new InvalidOperationException("E183: 削除後の図形・配置・表示設定が期待値と一致しません。");
                    foreach(var pair in external)
                    {
                        var model=project.GetModelById(pair.Key);
                        if(model==null || model.IsDeleted)throw new InvalidOperationException("E183: 削除対象の関連先モデルが失われました。");
                        string expectedEndpoint=names.ContainsKey(pair.Key)?model.Metaclass.Id+":"+names[pair.Key]:pair.Value;
                        if(model.Metaclass.Id+":"+model.Name!=expectedEndpoint)throw new InvalidOperationException("E183: 削除対象の関連先モデルが変化しました。");
                    }
                    if(Signature(root,fresh)!=expected)throw new InvalidOperationException("E170: 更新後のID・関連・配置・内容が一致しません。");
                    foreach(var edit in edits)if(project.GetModelById(map.MessageIds[edit.Index]).Name!=edit.After)throw new InvalidOperationException("E170: 本文の読戻しが一致しません。");
                    foreach(var target in requested)
                    {
                        var shape=fresh.Messages.Single(s=>s.Model.Id==map.MessageIds[target.Index]);
                        if(SequenceExportMatch.Text(shape.Text)!=SequenceExportMatch.Text(target.After))
                            throw new InvalidOperationException("E170: 表示本文がPlantUMLに一致しません。Name以外から合成される表示には、この版の本文更新を適用できません。");
                    }
                    if(SequenceMapFile.Read(path).Serialize()!=map.Serialize())throw new InvalidOperationException("E175: 更新中に対応表が変更されました。");
                    if(transaction!=null)completion.Commit(delegate{transaction.Commit();});committed=true;
                    File.Replace(pending,path,path+".bak");pending=null;
                    SequenceExperiment.Summary="メッセージ本文の差分更新: "+edits.Count+"件\n図側の本文変更をPlantUMLに合わせた対象: "+merge.Conflicts+"件 / 本文一致: "+merge.AlreadyMatched+"件\nID・関連・配置の保持照合: 一致"+coverage+"\n追加・移動・実行区間等の同期: 未対応\n対応表: 更新済み（前回分は .bak）\nプロジェクト保存: していません\n保存後のGit差分・Undo/Redo・再読込は別途確認してください。";
                }
            }
        }
        catch(Exception ex)
        {
            detail.AppendLine(ex.ToString());
            if(transaction!=null && !committed)
            {
                try {
                    completion.Cancel(delegate{transaction.Rollback();});
                    var restored=root.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==diagram.Id);
                    rollbackRestored=Signature(root,restored)==original;
                    if(originalEditor!=null)rollbackRestored=rollbackRestored && SequenceEditorCapture.Read(project,root,restored,detail).Fingerprint()==originalEditor.Fingerprint();
                    detail.AppendLine("Rollback returned; snapshot restored="+rollbackRestored);
                }
                catch(Exception failure){detail.AppendLine("Rollback failure: "+failure);}
            }
            SequenceExperiment.Summary=(committed?"図の更新は確定しましたが、対応表の更新に失敗しました。\n残っている .pending を対応表として選択できます。\n":"差分更新を完了できませんでした。\n")+ex.Message+(transaction!=null && !committed && !rollbackRestored?"\n取消・復元を確認できませんでした。保存せずコピーを開き直してください。":"")+"\n詳細は診断表示で確認してください。";
        }
        finally
        {
            // After a failed model transaction the old map remains authoritative.
            if(prepared && pending!=null && !committed && (transaction==null || rollbackRestored) && File.Exists(pending))
                try{File.Delete(pending);}catch(Exception ex){detail.AppendLine("Pending cleanup: "+ex.Message);}
            SequenceExperiment.Details=detail.ToString();
        }
        SequenceExperiment.Show(app);
    }
}

public static class SequenceEditorCapture
{
    static double Number(SequenceJson node,string key)
    { return node[key]==null?0:double.Parse(node[key].Raw,System.Globalization.CultureInfo.InvariantCulture); }
    static void Equal(double actual,double serialized)
    { if(Math.Abs(actual-serialized)>0.0000001)throw new InvalidOperationException("E180: 現在の図とエクスポートの配置が一致しません。削除は行いません。"); }
    public static SequenceEditorDocument Read(IProject project,IInteraction root,ISequenceDiagram diagram,StringBuilder log)
    {
        // Never read the previously saved unit: it may omit current unsaved edits.
        // Export to a fresh local temporary file through the public SDK instead.
        string directory=Path.Combine(Path.GetTempPath(),"SequenceEditor-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"snapshot.nmdl");
        try
        {
            if(root.ModelUnit==null)throw new InvalidOperationException("E180: 図のモデルユニットを取得できません。");
            log.AppendLine("Editor snapshot export: unit type="+root.ModelUnit.Type);
            project.UnitManager.ExportModelUnit(root.ModelUnit,path);
            if(!File.Exists(path) || new FileInfo(path).Length>100000000)throw new InvalidOperationException("E180: 図のエクスポートを取得できないか100MBを超えています。");
            var snapshot=SequenceEditorDocument.Read(File.ReadAllText(path,new UTF8Encoding(false,true)),root.Id,diagram.Id);
            var shapes=snapshot.Shapes().ToDictionary(n=>SequenceEditorDocument.Value(n,"Id"));
            if(!new HashSet<string>(diagram.Shapes.Select(n=>n.Id+":"+n.ModelId)).SetEquals(shapes.Values.Select(n=>SequenceEditorDocument.Value(n,"Id")+":"+SequenceEditorDocument.Value(n,"ModelId"))))
                throw new InvalidOperationException("E180: 現在の図とエクスポートの図形IDが一致しません。");
            foreach(var message in diagram.Messages)
            {
                var shape=shapes[message.Id];Equal(message.SourceY,Number(shape,"SourceY"));Equal(message.TargetY,Number(shape,"TargetY"));Equal(message.SelfloopBendsX,Number(shape,"SelfloopBendsX"));
            }
            // Compare documented persisted geometry as well as identities before using the snapshot.
            foreach(var node in diagram.Shapes.OfType<ISequenceNodeShape>())
            {
                var shape=shapes[node.Id];
                if(shape["X"]!=null)Equal(node.LocationX,Number(shape,"X"));
                if(shape["Y"]!=null)Equal(node.LocationY,Number(shape,"Y"));
                if(shape["Width"]!=null)Equal(node.Width,Number(shape,"Width"));
                if(shape["Height"]!=null)Equal(node.Height,Number(shape,"Height"));
            }
            log.AppendLine("Editor snapshot: live identities and geometry verified, shapes="+shapes.Count);
            return snapshot;
        }
        finally
        {
            // Only this invocation's known file; never recursively remove an export directory.
            try { if(File.Exists(path))File.Delete(path);if(!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);else log.AppendLine("Additional export files remain in: "+directory); }
            catch(Exception ex){log.AppendLine("Temporary export cleanup failed: "+ex.Message);}
        }
    }
}

public class SequencePayload
{
    public PumlProfile ImportProfile;
    public List<PumlExpected> Expected = new List<PumlExpected>();
    public const string Prefix = "System.Behavior.Interaction.";
    public static readonly string[] RelationTypes = { "___Interaction_Frame", "___Interaction_Lifeline", "___Interaction_ExecutionSpecification", "___Interaction_Message", "OwnedExecutionSpecification", "SendMessage", "ReceiveMessage" };
    public string[] Ids;
    public string Name, Json, EditorJson;
    public static string Q(string s)
    {
        if (s == null) throw new ArgumentNullException("s");
        var b = new StringBuilder("\"");
        foreach (char c in s) { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); }
        return b.Append('"').ToString();
    }
    public static SequencePayload Build(string[] types, string definition, string schema)
    {
        if (types == null || types.Length != 7 || types.Any(string.IsNullOrEmpty) || string.IsNullOrEmpty(definition)
            || !Regex.IsMatch(schema ?? "", "^[0-9]+\\.[0-9]+$")) throw new ArgumentException("Missing metadata");
        var p = new SequencePayload { Ids = Enumerable.Range(0,7).Select(i => Guid.NewGuid().ToString()).ToArray() };
        p.Name = "SEQ_PROBE_" + DateTime.Now.ToString("HHmmss") + "_" + p.Ids[0].Substring(0,8);
        var entities = new List<string>(); var relations = new List<string>();
        string[] kinds = { "Interaction", "Frame", "Lifeline", "Lifeline", "ExecutionSpecification", "ExecutionSpecification", "Message" };
        string[] names = { p.Name, p.Name, "A", "B", "", "", "probe()" };
        for (int i=0;i<7;i++) entities.Add("{\"Id\":" + Q(p.Ids[i]) + ",\"EntityType\":" + Q(kinds[i]) + ",\"MetamodelId\":" + Q(types[i])
            + ",\"Name\":" + Q(names[i]) + ",\"Fields\":{\"Name\":" + Q(names[i]) + (i==6 ? ",\"MessageSort\":\"Sync\"" : "") + "}}");
        Action<int,int,int,int> add = (type,source,target,index) => relations.Add("{\"Id\":" + Q(Guid.NewGuid().ToString())
            + ",\"RelationType\":" + Q(type<4 ? "Embed" : "Ref") + ",\"MetamodelId\":" + Q(Prefix+RelationTypes[type])
            + ",\"SourceId\":" + Q(p.Ids[source]) + ",\"TargetId\":" + Q(p.Ids[target]) + ",\"SourceIndex\":" + index + ",\"TargetIndex\":" + (type >= 5 ? 0 : -1) + "}");
        add(0,0,1,0); add(1,0,2,0); add(1,0,3,1); add(2,0,4,0); add(2,0,5,1); add(3,0,6,0);
        add(4,2,4,0); add(4,3,5,0); add(5,4,6,0); add(6,5,6,0);
        Func<int,string,string> shape = (i,properties) => "{\"Id\":"+Q(Guid.NewGuid().ToString())+",\"ModelId\":"+Q(p.Ids[i])+properties+"}";
        string editor = "{\"Id\":" + Q(Guid.NewGuid().ToString()) + ",\"ViewType\":\"SequenceDiagram\",\"MetamodelId\":\"DensoCreate.Indio.IMF.Extensions.Sequence.ViewInstance.SequenceDiagramViewInstance\",\"DefinitionId\":" + Q(definition)
            + ",\"ModelId\":"+Q(p.Ids[0])+",\"Frame\":"+shape(1,"")
            + ",\"Lifelines\":["+shape(2,",\"LeftPadding\":20,\"LaneLength\":240,\"X\":20,\"Width\":100")+","+shape(3,",\"LeftPadding\":100,\"LaneLength\":240,\"X\":220,\"Width\":100")+"]"
            + ",\"ExecutionSpecifications\":["+shape(4,",\"Length\":120,\"X\":70,\"Y\":50,\"Height\":120")+","+shape(5,",\"Length\":80,\"X\":270,\"Y\":80,\"Height\":80")+"]"
            + ",\"Messages\":["+shape(6,",\"SourceY\":80,\"TargetY\":80,\"IsRightAtFrame\":false,\"SelfloopBendsX\":0")+"]}";
        p.EditorJson=editor;
        p.Json = "{\"Type\":\"Model\",\"SchemaVersion\":"+Q(schema)+",\"TopElementId\":"+Q(p.Ids[0])
            + ",\"Entities\":["+string.Join(",",entities)+"],\"Relations\":["+string.Join(",",relations)+"],\"Editors\":["+editor+"]}";
        return p;
    }
}

public class SequenceCompletion
{
    private bool committed, cancelAttempted;
    public void Commit(Action commit)
    {
        if (committed || cancelAttempted) throw new InvalidOperationException("Transaction already completed");
        commit();
        committed = true;
    }
    public void Cancel(Action cancel)
    {
        if (committed || cancelAttempted) return;
        cancelAttempted = true;
        cancel();
    }
}

public class PumlNode
{
    public string Kind, Text = "", Left, Right, Operator;
    public int Line;
    public List<string> Targets = new List<string>();
    public List<PumlNode> Children = new List<PumlNode>();
}
public class PumlPlan
{
    public string Title = "PlantUML";
    public int StyleDirectives;
    public List<int> IgnoredDestroyedActivities=new List<int>();
    public List<string> Aliases = new List<string>(), Names = new List<string>();
    public List<PumlNode> Nodes = new List<PumlNode>();
    public IEnumerable<PumlNode> All() { return Walk(Nodes); }
    public static IEnumerable<PumlNode> Walk(IEnumerable<PumlNode> nodes)
    { foreach (var n in nodes) { yield return n; foreach (var c in Walk(n.Children)) yield return c; } }
    public string Summary()
    {
        var a = All().ToList();
        return "ライフライン: " + Aliases.Count + " / 同期: " + a.Count(n => n.Kind == "sync") + " / 非同期: " + a.Count(n => n.Kind == "async") + " / 返信: " + a.Count(n=>n.Kind=="reply") + " / 破棄: " + a.Count(n=>n.Kind=="destroy") + " / 図外宛て: " + a.Count(n=>n.Right=="]") + " / 図外から: " + a.Count(n=>n.Left=="[")
            + "\n複合フラグメント: " + a.Count(n => n.Kind == "fragment") + " / ref: " + a.Count(n => n.Kind == "ref") + " / Note: " + a.Count(n => n.Kind == "note")
            + (IgnoredDestroyedActivities.Count>0 ? "\n破棄後の実行区間指定: "+IgnoredDestroyedActivities.Count+"件を除外（行: "+string.Join(",",IgnoredDestroyedActivities)+"）。参加者は再生成しません。" : "")
            + (StyleDirectives>0 ? "\n表示設定: "+StyleDirectives+"件はNext Designの既定表示を使用します。" : "")
            + (a.Any(n => n.Kind == "ref") ? "\nrefは表示枠として作成します。別の図への参照リンクは未設定です。" : "");
    }
    private void Participant(string alias, string name, int line, bool declared)
    {
        if (!Regex.IsMatch(alias, @"^[\p{L}\p{N}_]+$")) throw Error(line, "別名には文字・数字・_を使ってください。");
        int i = Aliases.IndexOf(alias);
        if (i >= 0) { if (declared) throw Error(line, "参加者の別名が重複しています。"); return; }
        Aliases.Add(alias); Names.Add(name);
        if (Aliases.Count > 50) throw Error(line, "ライフラインは50本までです。");
    }
    public static InvalidOperationException Error(int line, string message) { return new InvalidOperationException("E120: " + line + "行目: " + message); }
    public static PumlPlan Parse(string input) { return Parse(input,true); }
    public static PumlPlan ParseForMapping(string input) { return Parse(input,false); }
    static PumlPlan Parse(string input,bool forGeneration)
    {
        if (input == null || input.Length > 300000) throw Error(1, "入力サイズが上限を超えています。");
        var p = new PumlPlan(); var lists = new Stack<List<PumlNode>>(); lists.Push(p.Nodes);
        var fragments = new Stack<PumlNode>();
        var lines = input.Replace("\r\n", "\n").Replace('\r','\n').Split('\n');
        bool started = false, ended = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string s = lines[i].Trim(); int line = i+1;
            if (s.Length == 0 || s.StartsWith("'")) continue;
            if (ended) throw Error(line, "@endumlの後に入力があります。1ファイル1図にしてください。");
            if (Regex.IsMatch(s, @"^@startuml(?:\s+.*)?$"))
            { if (started || p.Nodes.Count != 0) throw Error(line, "開始位置が不正です。"); started = true; continue; }
            if (!started) throw Error(line,"@startumlより前に構文があります。");
            if (s == "@enduml") { if (fragments.Count != 0) throw Error(line, "endが不足しています。"); ended = true; continue; }
            if(Regex.IsMatch(s,@"^skinparam\s+(sequenceMessageAlign\s+(left|center|right)|maxMessageSize\s+[1-9][0-9]*|sequenceReferenceBackgroundColor\s+#[0-9a-fA-F]{6})$",RegexOptions.IgnoreCase))
            { p.StyleDirectives++; continue; }
            var activity=Regex.Match(s,@"^(activate|deactivate|destroy)\s+([\p{L}\p{N}_]+)$");
            if(activity.Success)
            {
                p.Participant(activity.Groups[2].Value,activity.Groups[2].Value,line,false);
                lists.Peek().Add(new PumlNode{Kind=activity.Groups[1].Value,Left=activity.Groups[2].Value,Line=line}); continue;
            }
            var m = Regex.Match(s, "^participant\\s+(?:\"([^\"]+)\"\\s+as\\s+([\\p{L}\\p{N}_]+)|([\\p{L}\\p{N}_]+))$");
            if (m.Success) { string alias = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value; p.Participant(alias,m.Groups[1].Success?m.Groups[1].Value:alias,line,true); continue; }
            if (s.StartsWith("title ")) { p.Title = s.Substring(6); continue; }
            m = Regex.Match(s, @"^(alt|opt|loop|par|break|critical|group)\b\s*(.*)$");
            if (m.Success)
            {
                if (fragments.Count >= 8) throw Error(line, "入れ子は8段までです。");
                var f = new PumlNode { Kind="fragment", Operator=m.Groups[1].Value, Text=m.Groups[2].Value, Line=line };
                var operand = new PumlNode { Kind="operand", Text=m.Groups[2].Value, Line=line };
                f.Children.Add(operand); lists.Peek().Add(f); fragments.Push(f); lists.Push(operand.Children); continue;
            }
            m = Regex.Match(s, @"^else(?:\s+(.*))?$");
            if (m.Success)
            {
                if (fragments.Count==0 || (fragments.Peek().Operator!="alt" && fragments.Peek().Operator!="par")) throw Error(line,"elseはalt/parの中に指定してください。");
                var operand = new PumlNode { Kind="operand", Text=m.Groups[1].Value, Line=line };
                lists.Pop(); fragments.Peek().Children.Add(operand); lists.Push(operand.Children); continue;
            }
            if (s=="end") { if (fragments.Count==0) throw Error(line,"対応する複合フラグメントがありません。"); fragments.Pop(); lists.Pop(); continue; }
            m = Regex.Match(s, @"^(note|ref)\s+(over|left of|right of)\s+([\p{L}\p{N}_]+(?:\s*,\s*[\p{L}\p{N}_]+)*)(?:\s*:\s*(.*))?$");
            if (m.Success)
            {
                if (m.Groups[1].Value=="ref" && m.Groups[2].Value!="over") throw Error(line,"refはoverで指定してください。");
                var n = new PumlNode { Kind=m.Groups[1].Value, Operator=m.Groups[2].Value, Line=line, Text=m.Groups[4].Value };
                n.Targets = m.Groups[3].Value.Split(',').Select(t=>t.Trim()).ToList();
                if (n.Targets.Distinct().Count()!=n.Targets.Count) throw Error(line,"対象の重複があります。");
                if (!m.Groups[4].Success)
                {
                    var body = new List<string>(); bool closed = false;
                    while (++i < lines.Length) { if (lines[i].Trim()=="end "+n.Kind) { closed=true; break; } body.Add(lines[i]); }
                    if (!closed) throw Error(line,"end "+n.Kind+"が不足しています。"); n.Text=string.Join("\n",body);
                }
                n.Text=n.Text.Replace("\\n","\n"); lists.Peek().Add(n); continue;
            }
            m = Regex.Match(s, @"^([\p{L}\p{N}_]+|\[)\s*(-->>|-->|->>|->)\s*([\p{L}\p{N}_]+|\])\s*:\s*(.*)$");
            if (m.Success)
            {
                if(m.Groups[1].Value=="[" && m.Groups[3].Value=="]")throw Error(line,"図外同士のメッセージは扱えません。");
                if(m.Groups[1].Value!="[")p.Participant(m.Groups[1].Value,m.Groups[1].Value,line,false);
                if(m.Groups[3].Value!="]")p.Participant(m.Groups[3].Value,m.Groups[3].Value,line,false);
                lists.Peek().Add(new PumlNode { Kind=m.Groups[2].Value.StartsWith("--")?"reply":m.Groups[2].Value=="->>"?"async":"sync", Left=m.Groups[1].Value,Right=m.Groups[3].Value,Text=m.Groups[4].Value.Replace("\\n","\n"),Line=line }); continue;
            }
            throw Error(line,"未対応の構文です。取り込みは実行しません。" );
        }
        if (fragments.Count!=0) throw Error(lines.Length,"endが不足しています。");
        if (!started || !ended) throw Error(1,"@startumlと@endumlが必要です。");
        if (p.Aliases.Count<1 || p.All().Count()>500) throw Error(1,"参加者は1本以上、要素は500件以下にしてください。");
        foreach (var n in p.All()) foreach (var target in n.Targets) if (!p.Aliases.Contains(target)) throw Error(n.Line,"note/refの参加者が未定義です。");
        // Mapping reads an existing diagram; it does not build execution intervals.
        // Preserve every activity node for structural comparison instead of applying
        // the generator's branch-local lifecycle restrictions or rewriting the input.
        if(forGeneration)
        {
            p.ValidateDestroyed(p.Nodes,new Dictionary<string,int>());
            ValidateActivities(p.Nodes,new Dictionary<string,int>());
        }
        return p;
    }
    void ValidateDestroyed(List<PumlNode> nodes,Dictionary<string,int> destroyed)
    {
        foreach(var n in nodes.ToArray())
        {
            if(n.Kind=="fragment")
            {
                var after=new Dictionary<string,int>(destroyed);
                foreach(var branch in n.Children)
                { var state=new Dictionary<string,int>(destroyed); ValidateDestroyed(branch.Children,state); foreach(var pair in state)after[pair.Key]=pair.Value; }
                foreach(var pair in after)destroyed[pair.Key]=pair.Value; continue;
            }
            string reused=n.Left!=null && destroyed.ContainsKey(n.Left)?n.Left:n.Right!=null && destroyed.ContainsKey(n.Right)?n.Right:null;
            if(reused!=null && (n.Kind=="activate" || n.Kind=="deactivate"))
            { IgnoredDestroyedActivities.Add(n.Line); nodes.Remove(n); continue; }
            if(reused!=null)
                throw Error(n.Line,"破棄済みの参加者を再利用しています。再生成は未対応です。\n対象: "+reused+"\n破棄行: "+destroyed[reused]+" / 使用構文: "+n.Kind);
            if(n.Kind=="destroy")destroyed[n.Left]=n.Line;
        }
    }
    static void ValidateActivities(IEnumerable<PumlNode> nodes,Dictionary<string,int> counts,bool balanced=true)
    {
        var initial=balanced?new Dictionary<string,int>(counts):new Dictionary<string,int>();
        foreach(var n in nodes)
        {
            if(n.Kind=="fragment")foreach(var branch in n.Children)
            { if(n.Operator=="loop")ValidateActivities(branch.Children,counts,false); else ValidateActivities(branch.Children,new Dictionary<string,int>(counts)); }
            if(n.Kind=="destroy")
            {
                int inherited; initial.TryGetValue(n.Left,out inherited);
                if(inherited>0)throw Error(n.Line,"分岐の外で開始した実行区間の破棄は未対応です。");
                counts[n.Left]=0; continue;
            }
            if(n.Kind!="activate" && n.Kind!="deactivate")continue;
            int value; counts.TryGetValue(n.Left,out value);
            int baseline; initial.TryGetValue(n.Left,out baseline);
            if(n.Kind=="deactivate" && value<=baseline)throw Error(n.Line,"対応するactivateが同じ図または分岐内にありません。");
            counts[n.Left]=value+(n.Kind=="activate"?1:-1);
        }
        if(!balanced)return;
        foreach(var pair in counts)
        {
            int value; initial.TryGetValue(pair.Key,out value);
            if(value!=pair.Value)throw Error(1,"activate/deactivateは図または各分岐内で対応させてください。");
        }
    }
}
public class PumlExpected
{
    public string Id, Kind, Text, Left, Right, Owner, Operator, SendPort, ReceivePort;
    public int X,Y,EndY;
    }
public class PumlProfile
{
    public Dictionary<string,string> Types = new Dictionary<string,string>();
    public Dictionary<string,string> Relations = new Dictionary<string,string>();
    public Dictionary<string,string> Operators = new Dictionary<string,string>();
    public string NoteField = "Body", NoteStorage = "String", Sync="Sync", Async="Async", Reply="Reply";
}
public class PumlBuild
{
    private PumlProfile profile; private SequencePayload payload;
    private List<object> entities=new List<object>(), relations=new List<object>();
    private Dictionary<string,List<object>> shapes=new Dictionary<string,List<object>>();
    private Dictionary<string,string> lifelines=new Dictionary<string,string>(), active=new Dictionary<string,string>();
    private Dictionary<string,int> x=new Dictionary<string,int>(), indexes=new Dictionary<string,int>();
    private Dictionary<string,Dictionary<string,object>> executions=new Dictionary<string,Dictionary<string,object>>();
    private Dictionary<string,Stack<string>> activities=new Dictionary<string,Stack<string>>();
    private string pendingAlias,pendingExecution,frameId;
    private Dictionary<string,string> executionAliases=new Dictionary<string,string>();
    private int y=40;
    public static Dictionary<string,object> Obj(params object[] values)
    { var d=new Dictionary<string,object>(); for(int i=0;i<values.Length;i+=2)d.Add((string)values[i],values[i+1]); return d; }
    public static string Json(object value)
    {
        if(value==null)return "null";
        var s=value as string; if(s!=null)return SequencePayload.Q(s);
        var d=value as Dictionary<string,object>; if(d!=null)return "{"+string.Join(",",d.Select(k=>SequencePayload.Q(k.Key)+":"+Json(k.Value)))+"}";
        var list=value as System.Collections.IEnumerable; if(list!=null)return "["+string.Join(",",list.Cast<object>().Select(Json))+"]";
        if(value is bool)return (bool)value?"true":"false";
        return Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
    }
    private string Entity(string kind,string text,Dictionary<string,object> fields=null,string fixedId=null)
    {
        string id=fixedId??Guid.NewGuid().ToString(); if(fields==null)fields=Obj("Name",text);
        entities.Add(Obj("Id",id,"EntityType",kind,"MetamodelId",profile.Types[kind],"Name",text,"Fields",fields)); return id;
    }
    private void Link(string kind,string source,string target,bool embed=false,int targetIndex=-1,string fixedId=null)
    {
        if(!profile.Relations.ContainsKey(kind))throw new InvalidOperationException("E121: 構造関連を取得できません: "+kind);
        string key=kind+source; int order; indexes.TryGetValue(key,out order); indexes[key]=order+1;
        relations.Add(Obj("Id",fixedId??Guid.NewGuid().ToString(),"RelationType",embed?"Embed":"Ref","MetamodelId",profile.Relations[kind],"SourceId",source,"TargetId",target,"SourceIndex",order,"TargetIndex",targetIndex));
    }
    private Dictionary<string,object> Shape(string list,string model,params object[] values)
    {
        var d=Obj(values); d.Add("Id",Guid.NewGuid().ToString()); d.Add("ModelId",model);
        if(!shapes.ContainsKey(list))shapes[list]=new List<object>(); shapes[list].Add(d); return d;
    }
    private void Owned(string kind,string id) { Link(kind,payload.Ids[0],id,true); }
    private string Execution(string alias,int start)
    {
        string id=Entity("ExecutionSpecification",""); Owned("ExecutionSpecifications",id); Link("OwnedExecutionSpecification",lifelines[alias],id);
        executions[id]=Shape("ExecutionSpecifications",id,"X",x[alias]+8*(activities.ContainsKey(alias)?activities[alias].Count:0),"Y",start,"Length",40,"Height",40); executionAliases[id]=alias; return id;
    }
    private void Extend(string id,int at)
    { var s=executions[id]; int size=Math.Max((int)s["Length"],at-(int)s["Y"]+35); s["Length"]=size; s["Height"]=size; }
    private void Items(IEnumerable<PumlNode> nodes,string operand=null,int depth=0)
    {
        var items=nodes.ToList();
        for(int index=0;index<items.Count;index++)
        {
            var n=items[index];
            if(n.Kind=="destroy")
            {
                int at=y-15;
                var live=new HashSet<string>();
                if(active.ContainsKey(n.Left))live.Add(active[n.Left]);
                if(activities.ContainsKey(n.Left))foreach(var parent in activities[n.Left])if(parent!=null)live.Add(parent);
                foreach(var execution in executions.Where(v=>executionAliases[v.Key]==n.Left))
                { var bar=execution.Value; int length=live.Contains(execution.Key)?at-(int)bar["Y"]:Math.Min((int)bar["Length"],at-(int)bar["Y"]); bar["Length"]=length; bar["Height"]=length; }
                active.Remove(n.Left); activities.Remove(n.Left); pendingAlias=null;
                string id=Entity("Destruction",""); Owned("Destructions",id); Link("DestructionTargetLifeline",id,lifelines[n.Left],false,0);
                Shape("Destructions",id,"X",x[n.Left],"Y",at,"Width",20,"Height",20);
                payload.Expected.Add(new PumlExpected{Id=id,Kind="destruction",Left=lifelines[n.Left],Y=at});
                y+=20; continue;
            }
            if(n.Kind=="activate")
            {
                string previous; active.TryGetValue(n.Left,out previous);
                if(!activities.ContainsKey(n.Left))activities[n.Left]=new Stack<string>();
                string id=pendingAlias==n.Left?pendingExecution:Execution(n.Left,y-20);
                // A receive followed by activate opens that receive execution, not a second bar.
                if(id==previous)previous=activities[n.Left].Count>0?activities[n.Left].Peek():null;
                activities[n.Left].Push(previous); active[n.Left]=id;
                pendingAlias=null; continue;
            }
            if(n.Kind=="deactivate")
            {
                string id=active[n.Left]; var bar=executions[id];
                int length=Math.Max(40,y-16-(int)bar["Y"]); bar["Length"]=length;bar["Height"]=length;
                string previous=activities[n.Left].Pop();
                if(previous==null)active.Remove(n.Left);else active[n.Left]=previous;
                pendingAlias=null; continue;
            }
            pendingAlias=null;
            if(n.Kind=="sync" || n.Kind=="async" || n.Kind=="reply")
            {
                y+=18*(n.Text.Split('\n').Length-1);
                bool incoming=n.Left=="[";
                string send; if(incoming)send=null; else if(!active.TryGetValue(n.Left,out send))active[n.Left]=send=Execution(n.Left,y-20);
                bool outgoing=n.Right=="]"; bool self=n.Left==n.Right; int targetY=y+(self?24:0);
                string receive;
                bool beginsActivation=index+1<items.Count && items[index+1].Kind=="activate" && items[index+1].Left==n.Right;
                if(outgoing)
                {
                    receive=Entity("MessageEnd",""); Owned("MessageEnds",receive);
                    int endX=(int)executions[send]["X"]-60;
                    Shape("MessageEnds",receive,"X",endX,"Y",targetY,"Width",10,"Height",10);
                    payload.Expected.Add(new PumlExpected{Id=receive,Kind="messageEnd",Y=targetY,X=endX});
                }
                else if((n.Kind=="reply" || (!beginsActivation && activities.ContainsKey(n.Right) && activities[n.Right].Count>0)) && active.TryGetValue(n.Right,out receive)) { }
                else receive=Execution(n.Right,targetY);
                if(incoming)
                {
                    send=Entity("MessageEnd",""); Owned("MessageEnds",send);
                    int endX=(int)executions[receive]["X"]-60;
                    Shape("MessageEnds",send,"X",endX,"Y",y,"Width",10,"Height",10);
                    payload.Expected.Add(new PumlExpected{Id=send,Kind="messageEnd",Y=y,X=endX});
                }
                // Keep explicit activation contexts until deactivate; an immediately following
                // activate may adopt this receiving execution.
                if(!outgoing && (!activities.ContainsKey(n.Right) || activities[n.Right].Count==0))active[n.Right]=receive;
                pendingAlias=outgoing?null:n.Right; pendingExecution=receive;
                if(!incoming)Extend(send,y); if(!outgoing)Extend(receive,targetY);
                foreach(var pair in activities)if(pair.Value.Count>0 && active.ContainsKey(pair.Key))Extend(active[pair.Key],targetY);
                string id=Entity("Message",n.Text,Obj("Name",n.Text,"MessageSort",n.Kind=="reply"?profile.Reply:n.Kind=="sync"?profile.Sync:profile.Async)); Owned("Messages",id);
                Link("SendMessage",send,id,false,0); Link("ReceiveMessage",receive,id,false,0);
                Shape("Messages",id,"SourceY",y,"TargetY",targetY,"IsRightAtFrame",false,"SelfloopBendsX",self?Math.Max((int)executions[send]["X"],(int)executions[receive]["X"])+80:0);
                if(operand!=null)Link("OperandTargetMessage",operand,id,false,0);
                payload.Expected.Add(new PumlExpected{Id=id,Kind=n.Kind,Text=n.Text,Left=incoming?null:lifelines[n.Left],Right=outgoing?null:lifelines[n.Right],Owner=operand,SendPort=send,ReceivePort=receive,Y=y,EndY=targetY});
                y=targetY+50; continue;
            }
            if(n.Kind=="fragment")
            {
                int top=y; string op=profile.Operators[n.Operator];
                string id=Entity("CombinedFragment",n.Text,Obj("Name",n.Text,"Operator",op)); Owned("Fragments",id);
                foreach(string line in lifelines.Values)Link("CrossingFragmentCoveredLifeline",id,line);
                if(operand!=null)Link("NestedInteractionFragment",operand,id);
                var expected=new PumlExpected{Id=id,Kind="fragment",Text=n.Text,Operator=op,Owner=operand}; payload.Expected.Add(expected);
                bool firstBranch=true;
                foreach(var branch in n.Children)
                {
                    y+=firstBranch?30:12; firstBranch=false; string oid=Entity("InteractionOperand","",Obj("Name","","Guard",branch.Text)); Link("Operands",id,oid,true);
                    Shape("Operands",oid,"Position",y-top);
                    payload.Expected.Add(new PumlExpected{Id=oid,Kind="operand",Text=branch.Text,Owner=id});
                    // Each branch starts with its own execution context.
                    var saved=new Dictionary<string,string>(active);
                    foreach(var key in active.Keys.ToArray())if(!activities.ContainsKey(key) || activities[key].Count==0)active.Remove(key);
                    // Leave room below the guard before placing messages or nested frames.
                    y+=40+18*(branch.Text.Split('\n').Length-1); Items(branch.Children,oid,depth+1); if(n.Operator!="loop")active=saved; pendingAlias=null; y+=8;
                }
                Shape("Fragments",id,"X",20+16*depth,"Y",top,"Width",x.Values.Max()+210-32*depth,"Height",y-top); y+=16; continue;
            }
            int left=n.Targets.Select(t=>x[t]).Min(),right=n.Targets.Select(t=>x[t]).Max();
            if(n.Kind=="ref")
            {
                string id=Entity("InteractionUse",n.Text); Owned("InteractionUses",id);
                foreach(string t in n.Targets)Link("CrossingFragmentCoveredLifeline",id,lifelines[t]);
                if(operand!=null)Link("NestedInteractionFragment",operand,id);
                Shape("InteractionUses",id,"X",left-55,"Y",y,"Width",Math.Max(150,right-left+110),"Height",Math.Max(48,16+20*n.Text.Split('\n').Length));
                payload.Expected.Add(new PumlExpected{Id=id,Kind="ref",Text=n.Text});
            }
            else if(n.Kind=="note")
            {
                var fields=Obj("Name",n.Text);
                if(profile.NoteField!="Name" && profile.NoteStorage=="String")fields.Add(profile.NoteField,n.Text);
                string id=Entity("InteractionNote",n.Text,fields); Owned("Notes",id);
                int width=Math.Max(160,right-left+100),sx=left-50;
                if(n.Operator=="left of")sx=left-width-30; if(n.Operator=="right of")sx=right+30;
                Shape("Notes",id,"X",sx,"Y",y,"Width",width,"Height",Math.Max(48,16+20*n.Text.Split('\n').Length));
                payload.Expected.Add(new PumlExpected{Id=id,Kind="note",Text=n.Text});
            }
            y+=Math.Max(48,16+20*n.Text.Split('\n').Length)+16;
        }
    }
    public static SequencePayload Build(PumlPlan plan,PumlProfile profile,string definition,string schema,SequenceIdentity identity=null)
    {
        var b=new PumlBuild{profile=profile,payload=new SequencePayload()}; var p=b.payload; p.ImportProfile=profile;
        if(identity!=null)identity.Validate();
        string root=b.Entity("Interaction",plan.Title,null,identity==null?null:identity.Root); p.Ids=new[]{root}; p.Name=plan.Title;
        string frame=b.Entity("Frame",plan.Title,null,identity==null?null:identity.Frame); b.frameId=frame; b.Link("Frame",root,frame,true,-1,identity==null?null:identity.FrameRelation);
        foreach(var alias in plan.Aliases)
        {
            int index=plan.Aliases.IndexOf(alias); string id=b.Entity("Lifeline",plan.Names[index]); b.lifelines[alias]=id; b.x[alias]=240+240*index;
            b.Owned("Lifelines",id); b.Shape("Lifelines",id,"X",b.x[alias]-50,"Width",100,"LeftPadding",index==0?190:140,"LaneLength",300);
            p.Expected.Add(new PumlExpected{Id=id,Kind="lifeline",Text=plan.Names[index]});
        }
        b.Items(plan.Nodes);
        foreach(var pair in b.executions)p.Expected.Add(new PumlExpected{Id=pair.Key,Kind="execution",Y=(int)pair.Value["Y"],EndY=(int)pair.Value["Y"]+(int)pair.Value["Length"]});
        foreach(var s in b.shapes["Lifelines"].Cast<Dictionary<string,object>>())s["LaneLength"]=b.y+40;
        var editor=Obj("Id",identity==null?Guid.NewGuid().ToString():identity.Editor,"ViewType","SequenceDiagram","MetamodelId","DensoCreate.Indio.IMF.Extensions.Sequence.ViewInstance.SequenceDiagramViewInstance","DefinitionId",definition,"ModelId",root,"Frame",Obj("Id",identity==null?Guid.NewGuid().ToString():identity.FrameShape,"ModelId",frame));
        foreach(var pair in b.shapes)editor.Add(pair.Key,pair.Value);
        p.Ids=b.entities.Cast<Dictionary<string,object>>().Select(e=>(string)e["Id"]).ToArray();
        p.Json=Json(Obj("Type","Model","SchemaVersion",schema,"TopElementId",root,"Entities",b.entities,"Relations",b.relations,"Editors",new[]{editor})); return p;
    }
}

public static class PumlTypeSelection
{
    public static string Destruction(string[] definitions,string[] observed)
    {
        var candidates=definitions.Distinct().ToArray();
        var samples=observed.Distinct().ToArray();
        if(candidates.Length==1)
        {
            if(samples.Any(id=>id!=candidates[0]))throw new InvalidOperationException("E121: 破棄点のビュー定義と見本の型が一致しません。");
            return candidates[0];
        }
        if(samples.Length==1 && (candidates.Length==0 || candidates.Contains(samples[0])))return samples[0];
        throw new InvalidOperationException("E121: 破棄点の表示用の型を一意に取得できません。破棄点がある既存の図を開いて取り込んでください。");
    }
}

public static class SequenceUpdateProbe
{
    public static string Payload(string seed)
    {
        string before=SequencePayload.Q("probe()");
        if(string.IsNullOrEmpty(seed) || !seed.Contains(before))throw new ArgumentException("Probe seed label missing");
        return seed.Replace(before,SequencePayload.Q("updatedProbe()"));
    }
}

public class SequenceIdentity
{
    public string Root,Frame,FrameRelation,Editor,FrameShape;
    public void Validate()
    {
        var ids=new[]{Root,Frame,FrameRelation,Editor,FrameShape};
        if(ids.Any(string.IsNullOrEmpty) || ids.Distinct().Count()!=ids.Length)throw new ArgumentException("Incomplete replacement identity");
    }
}

public static class SequenceDeltaInput
{
    public static string RestoreEditor(SequencePayload seed,string schema)
    {
        if(seed==null || seed.Ids==null || seed.Ids.Length!=7 || string.IsNullOrEmpty(seed.EditorJson))throw new ArgumentException("Missing seed editor");
        return "{\"Type\":\"Model\",\"SchemaVersion\":"+SequencePayload.Q(schema)+",\"TopElementId\":"+SequencePayload.Q(seed.Ids[0])+",\"Entities\":[],\"Relations\":[],\"Editors\":["+seed.EditorJson+"]}";
    }

    public static SequencePayload Build(SequencePayload seed,string messageType,string schema)
    {
        var ids=seed==null?null:seed.Ids;
        if(ids==null || ids.Length!=7 || ids.Any(string.IsNullOrEmpty) || ids.Distinct().Count()!=7 || string.IsNullOrEmpty(messageType) || string.IsNullOrEmpty(seed.EditorJson) || !seed.EditorJson.EndsWith("]}",StringComparison.Ordinal))throw new ArgumentException("Missing delta metadata");
        string id=Guid.NewGuid().ToString();
        var p=new SequencePayload{Ids=new[]{id},Name="deltaProbe()"};
        var relations=new List<object>();
        int[] sources={0,4,5}; int[] types={3,5,6};
        for(int i=0;i<3;i++)relations.Add(PumlBuild.Obj("Id",Guid.NewGuid().ToString(),"RelationType",i==0?"Embed":"Ref","MetamodelId",SequencePayload.Prefix+SequencePayload.RelationTypes[types[i]],"SourceId",ids[sources[i]],"TargetId",id,"SourceIndex",1,"TargetIndex",i==0?-1:0));
        // This editor belongs to the temporary seed we generated, not an arbitrary existing diagram.
        // Replay every original shape with its original ID and values; append only the new message.
        string shape=PumlBuild.Json(PumlBuild.Obj("Id",Guid.NewGuid().ToString(),"ModelId",id,"SourceY",120,"TargetY",120,"IsRightAtFrame",false,"SelfloopBendsX",0));
        string editor=seed.EditorJson.Substring(0,seed.EditorJson.Length-2)+","+shape+"]}";
        string body=PumlBuild.Json(PumlBuild.Obj("Type","Model","SchemaVersion",schema,"TopElementId",ids[0],
            "Entities",new[]{PumlBuild.Obj("Id",id,"EntityType","Message","MetamodelId",messageType,"Name",p.Name,"Fields",PumlBuild.Obj("Name",p.Name,"MessageSort","Sync"))},
            "Relations",relations));
        p.Json=body.Substring(0,body.Length-1)+",\"Editors\":["+editor+"]}";
        return p;
    }
}

public class SequenceNameEdit
{
    public int Index,Line;
    public string Before,After;
}
public static class SequenceExportMatch
{
    public static string Key(string kind,string sender,string receiver)
    { return PumlBuild.Json(new[]{Kind(kind),sender,receiver}); }
    public static int[] Align(string[] inputKeys,string[] inputText,string[] diagramKeys,string[] diagramText)
    {
        if(inputKeys.Length!=inputText.Length || diagramKeys.Length!=diagramText.Length)throw new ArgumentException("Message sequence lengths differ");
        int n=inputKeys.Length,m=diagramKeys.Length;
        var cost=new int[n+1,m+1];
        for(int i=n;i>=0;i--)for(int j=m;j>=0;j--)
        {
            if(i==n){cost[i,j]=(m-j)*2;continue;}
            if(j==m){cost[i,j]=(n-i)*2;continue;}
            int best=Math.Min(2+cost[i+1,j],2+cost[i,j+1]);
            if(inputKeys[i]==diagramKeys[j])best=Math.Min(best,(Text(inputText[i])==Text(diagramText[j])?0:1)+cost[i+1,j+1]);
            cost[i,j]=best;
        }
        var result=Enumerable.Repeat(-1,n).ToArray();int a=0,b=0;
        while(a<n && b<m)
        {
            // Prefer the earliest occurrence on ties; no model can be used twice.
            if(inputKeys[a]==diagramKeys[b] && cost[a,b]==(Text(inputText[a])==Text(diagramText[b])?0:1)+cost[a+1,b+1])
            {result[a++]=b++;continue;}
            if(cost[a,b]==2+cost[a,b+1])b++;else a++;
        }
        return result;
    }
    public static string[] Unmapped(IEnumerable<string> existing,IEnumerable<string> mapped)
    { return existing.Except(mapped,StringComparer.Ordinal).OrderBy(id=>id,StringComparer.Ordinal).ToArray(); }
    public static IEnumerable<T> Order<T>(IEnumerable<T> messages,Func<T,double> y,Func<T,double> x,Func<T,string> id)
    { return messages.OrderBy(y).ThenBy(x).ThenBy(id,StringComparer.Ordinal); }
    // Same whitespace policy as PlantUmlTool.PlantUmlText.Normalize.
    public static string Text(string value)
    { return Regex.Replace(value??"",@"\s+"," ").Trim(); }
    public static string Kind(string value)
    {
        value=(value??"").ToLowerInvariant();
        return value=="async"?"async":value=="reply"?"reply":"sync";
    }
    public static bool Message(string kind,string label,string sender,string receiver,string desiredKind,string desiredLabel,string desiredSender,string desiredReceiver)
    { return Kind(kind)==desiredKind && Text(label)==Text(desiredLabel) && sender==desiredSender && receiver==desiredReceiver; }
}
public static class SequenceParticipantMatch
{
    public static string Normalize(string value)
    {
        if(value==null)return null;
        // Preserve words, case and colon count. Never equate ':' with '::'.
        string folded=Regex.Replace(value.Replace("\\n","\n").Replace("\\r","\r"),@"\s+"," ").Trim();
        return Regex.Replace(folded,@"\s*(:+)\s*","$1");
    }
    public static bool Equivalent(string left,string right)
    { return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) && Normalize(left)==Normalize(right); }
}
public class SequenceNameMerge
{
    public List<SequenceNameEdit> Writes=new List<SequenceNameEdit>();
    public int Conflicts,AlreadyMatched;
    // Compare every mapped message with the desired PlantUML text.
    // The previous input is used for identity and reporting, not as a write filter.
    public static SequenceNameMerge Resolve(IEnumerable<SequenceNameEdit> requested,IDictionary<int,string> current)
    {
        var result=new SequenceNameMerge();
        foreach(var edit in requested)
        {
            string value;
            if(!current.TryGetValue(edit.Index,out value))throw new InvalidOperationException("E168: 更新対象の現在値がありません。");
            if(SequenceExportMatch.Text(value)==SequenceExportMatch.Text(edit.After)){result.AlreadyMatched++;continue;}
            if(value!=edit.Before)result.Conflicts++;
            result.Writes.Add(new SequenceNameEdit{Index=edit.Index,Line=edit.Line,Before=value,After=edit.After});
        }
        return result;
    }
}
public class SequenceMessagePlan
{
    public List<SequenceNameEdit> Targets = new List<SequenceNameEdit>();
    public int[] Retained;
    // Non-message boundaries (including branch/activity boundaries) must remain identical.
    static string Layout(PumlPlan plan, List<string> keys)
    {
        var tokens=new List<string>();
        Action<IEnumerable<PumlNode>> walk=null;
        walk=nodes=>{foreach(var n in nodes){
            if(SequenceNameDiff.IsMessage(n))
                keys.Add(tokens.Count+":"+SequenceExportMatch.Key(n.Kind,n.Left,n.Right));
            else {
                tokens.Add(PumlBuild.Json(PumlBuild.Obj("kind",n.Kind,"text",n.Text,"left",n.Left,"right",n.Right,"operator",n.Operator,"targets",n.Targets)));
                walk(n.Children);tokens.Add("end");
            }
        }};
        walk(plan.Nodes);
        return PumlBuild.Json(PumlBuild.Obj("title",plan.Title,"aliases",plan.Aliases,"names",plan.Names,"tokens",tokens));
    }
    public static SequenceMessagePlan Build(string previous,string next)
    {
        var a=PumlPlan.ParseForMapping(previous);var b=PumlPlan.ParseForMapping(next);
        var ak=new List<string>();var bk=new List<string>();
        if(Layout(a,ak)!=Layout(b,bk))throw new InvalidOperationException("E171: メッセージ本文・削除以外の構造変更は未対応です。参加者・実行区間・分岐・Note等は維持してください。");
        var old=SequenceNameDiff.Messages(a);var desired=SequenceNameDiff.Messages(b);
        var indices=SequenceExportMatch.Align(bk.ToArray(),desired.Select(n=>n.Text).ToArray(),ak.ToArray(),old.Select(n=>n.Text).ToArray());
        if(indices.Any(i=>i<0))throw new InvalidOperationException("E171: メッセージ追加・送受信先・種別・所属変更は未対応です。");
        // Exact matching text elsewhere on the same route can indicate a move, not a rename.
        for(int i=0;i<indices.Length;i++)
            if(SequenceExportMatch.Text(old[indices[i]].Text)!=SequenceExportMatch.Text(desired[i].Text)
                && old.Where((n,j)=>j!=indices[i] && ak[j]==bk[i]).Any(n=>SequenceExportMatch.Text(n.Text)==SequenceExportMatch.Text(desired[i].Text)))
                throw new InvalidOperationException("E172: メッセージ移動と本文変更が混在しているため、この版では反映できません。");
        return new SequenceMessagePlan { Retained=indices, Targets=desired.Select((n,i)=>new SequenceNameEdit{Index=indices[i],Line=n.Line,Before=old[indices[i]].Text,After=n.Text}).ToList() };
    }
}

public static class SequenceNameDiff
{
    public static List<SequenceNameEdit> Targets(string previous,string next)
    {
        Analyze(previous,next); // Retain the current name-only scope validation.
        var old=Messages(PumlPlan.ParseForMapping(previous));var desired=Messages(PumlPlan.ParseForMapping(next));
        return desired.Select((n,i)=>new SequenceNameEdit{Index=i,Line=n.Line,Before=old[i].Text,After=n.Text}).ToList();
    }
    public static bool IsMessage(PumlNode n) { return n.Kind=="sync" || n.Kind=="async" || n.Kind=="reply"; }
    public static PumlNode[] Messages(PumlPlan plan) { return plan.All().Where(IsMessage).ToArray(); }
    static object Node(PumlNode n)
    { return PumlBuild.Obj("kind",n.Kind,"text",IsMessage(n)?null:n.Text,"left",n.Left,"right",n.Right,"operator",n.Operator,"targets",n.Targets,"children",n.Children.Select(Node).ToArray()); }
    static string Structure(PumlPlan p)
    { return PumlBuild.Json(PumlBuild.Obj("title",p.Title,"aliases",p.Aliases,"names",p.Names,"nodes",p.Nodes.Select(Node).ToArray())); }
    public static List<SequenceNameEdit> Analyze(string previous,string next)
    {
        var a=PumlPlan.ParseForMapping(previous);var b=PumlPlan.ParseForMapping(next);
        if(Structure(a)!=Structure(b))throw new InvalidOperationException("E171: この版はメッセージ本文の変更だけに対応します。参加者・送受信先・種別・追加削除・順序・条件・Note等の変更は反映しません。");
        var old=Messages(a);var current=Messages(b);var edits=new List<SequenceNameEdit>();
        for(int i=0;i<old.Length;i++)
        {
            if(old[i].Text==current[i].Text)continue;
            // A name already belonging to another occurrence may indicate a move, not a rename.
            if(old.Where((n,j)=>j!=i).Any(n=>n.Kind==current[i].Kind && n.Left==current[i].Left && n.Right==current[i].Right && n.Text==current[i].Text))
                throw new InvalidOperationException("E172: "+current[i].Line+"行目は別メッセージの移動・重複と区別できません。本文更新として自動適用しません。");
            edits.Add(new SequenceNameEdit{Index=i,Line=current[i].Line,Before=old[i].Text,After=current[i].Text});
        }
        return edits;
    }
}
public class SequenceMapFile
{
    public string Project,Root,Editor,Source,Fingerprint;
    public string[] MessageIds;
    public static string Hash(string text)
    { using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant(); }
    void Validate()
    {
        if(new[]{Project,Root,Editor,Fingerprint}.Any(string.IsNullOrEmpty) || Source==null || MessageIds==null || MessageIds.Any(string.IsNullOrEmpty) || MessageIds.Distinct().Count()!=MessageIds.Length)
            throw new InvalidOperationException("E173: 対応表の必須項目またはIDが不正です。");
        if(Encoding.UTF8.GetByteCount(Source)>300000 || SequenceNameDiff.Messages(PumlPlan.ParseForMapping(Source)).Length!=MessageIds.Length)throw new InvalidOperationException("E173: 対応表の入力・件数が不正です。");
    }
    public string Serialize()
    {
        Validate();var doc=new System.Xml.XmlDocument();doc.XmlResolver=null;
        var root=doc.CreateElement("SequenceMap");doc.AppendChild(root);root.SetAttribute("version","1");
        string[] names={"Project","Root","Editor","Source","Fingerprint"};string[] values={Project,Root,Editor,Source,Fingerprint};
        for(int i=0;i<names.Length;i++){var element=doc.CreateElement(names[i]);element.InnerText=values[i];root.AppendChild(element);}
        var ids=doc.CreateElement("MessageIds");root.AppendChild(ids);
        foreach(string id in MessageIds){var element=doc.CreateElement("Id");element.InnerText=id;ids.AppendChild(element);}
        root.SetAttribute("sha256",Hash(root.InnerXml));return doc.OuterXml;
    }
    public static SequenceMapFile Parse(string xml)
    {
        var settings=new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2000000};
        var doc=new System.Xml.XmlDocument();doc.XmlResolver=null;doc.PreserveWhitespace=true;
        using(var reader=System.Xml.XmlReader.Create(new StringReader(xml),settings))doc.Load(reader);
        var root=doc.DocumentElement;
        if(root==null || root.Name!="SequenceMap" || root.GetAttribute("version")!="1" || root.GetAttribute("sha256")!=Hash(root.InnerXml))throw new InvalidOperationException("E173: 対応表の形式または整合性が不正です。");
        Func<string,string> value=name=>{var nodes=root.SelectNodes(name);if(nodes.Count!=1)throw new InvalidOperationException("E173: 対応表の項目が不正です: "+name);return nodes[0].InnerText;};
        var map=new SequenceMapFile{Project=value("Project"),Root=value("Root"),Editor=value("Editor"),Source=value("Source"),Fingerprint=value("Fingerprint"),MessageIds=root.SelectNodes("MessageIds/Id").Cast<System.Xml.XmlNode>().Select(n=>n.InnerText).ToArray()};
        map.Validate();return map;
    }
    public static SequenceMapFile Read(string path)
    { if(new FileInfo(path).Length>2000000)throw new InvalidOperationException("E173: 対応表が大きすぎます。");return Parse(File.ReadAllText(path,new UTF8Encoding(false,true))); }
    public static void WriteNew(string path,SequenceMapFile map)
    {
        string xml=map.Serialize();string temp=path+".writing-"+Guid.NewGuid().ToString("N");
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false))){writer.Write(xml);writer.Flush();stream.Flush(true);}
            if(Read(temp).Serialize()!=xml)throw new IOException("対応表の書戻し検証に失敗しました。");
            File.Move(temp,path);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

// Small lossless JSON tree: scalar spelling and unknown properties are preserved.
// No runtime JSON library reference is required by the Next Design script host.
public class SequenceJson
{
    public Dictionary<string,SequenceJson> Properties;
    public List<SequenceJson> Items;
    public string Raw;
    public SequenceJson this[string key] { get { SequenceJson value;return Properties!=null && Properties.TryGetValue(key,out value)?value:null; } }
    public string StringValue()
    {
        if(Raw==null || !Raw.StartsWith("\"",StringComparison.Ordinal))throw new InvalidOperationException("E180: JSON文字列が必要です。");
        var b=new StringBuilder();
        for(int i=1;i<Raw.Length-1;i++)
        {
            char c=Raw[i];if(c!='\\'){b.Append(c);continue;}
            c=Raw[++i];
            switch(c) {
                case '"':b.Append('"');break;case '\\':b.Append('\\');break;case '/':b.Append('/');break;
                case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;
                case 'u':b.Append((char)int.Parse(Raw.Substring(i+1,4),System.Globalization.NumberStyles.HexNumber,System.Globalization.CultureInfo.InvariantCulture));i+=4;break;
                default:throw new InvalidOperationException("E180: JSONエスケープが不正です。");
            }
        }
        return b.ToString();
    }
    public string ToJsonString()
    {
        if(Properties!=null)return "{"+string.Join(",",Properties.Select(p=>SequencePayload.Q(p.Key)+":"+p.Value.ToJsonString()))+"}";
        if(Items!=null)return "["+string.Join(",",Items.Select(n=>n.ToJsonString()))+"]";
        return Raw;
    }
    public static SequenceJson Parse(string text)
    {
        var reader=new Reader{Text=text};var result=reader.Read(0);reader.Space();
        if(reader.At!=text.Length)throw new InvalidOperationException("E180: JSONの末尾が不正です。");return result;
    }
    class Reader
    {
        public string Text;public int At;
        public void Space(){while(At<Text.Length && (Text[At]==' ' || Text[At]=='\t' || Text[At]=='\r' || Text[At]=='\n'))At++;}
        bool Take(char c){Space();if(At<Text.Length && Text[At]==c){At++;return true;}return false;}
        void Need(char c){if(!Take(c))throw new InvalidOperationException("E180: JSONの区切りが不正です。");}
        string Quoted()
        {
            Space();int start=At;Need('"');
            while(At<Text.Length)
            {
                char c=Text[At++];if(c=='"')return Text.Substring(start,At-start);
                if(c<32)break;
                if(c=='\\')
                {
                    if(At>=Text.Length)break;c=Text[At++];
                    if(c=='u') { if(At+4>Text.Length || !Regex.IsMatch(Text.Substring(At,4),"^[0-9a-fA-F]{4}$"))break;At+=4; }
                    else if("\"\\/bfnrt".IndexOf(c)<0)break;
                }
            }
            throw new InvalidOperationException("E180: JSON文字列が不正です。");
        }
        public SequenceJson Read(int depth)
        {
            if(depth>128)throw new InvalidOperationException("E180: JSONの入れ子が深すぎます。");
            Space();if(At>=Text.Length)throw new InvalidOperationException("E180: JSONが途中で終了しています。");
            if(Text[At]=='"')return new SequenceJson{Raw=Quoted()};
            if(Take('{')) {
                var result=new SequenceJson{Properties=new Dictionary<string,SequenceJson>(StringComparer.Ordinal)};
                if(Take('}'))return result;
                do { string key=new SequenceJson{Raw=Quoted()}.StringValue();Need(':');
                    if(result.Properties.ContainsKey(key))throw new InvalidOperationException("E180: JSONの属性名が重複しています。");
                    result.Properties.Add(key,Read(depth+1));if(Take('}'))return result;Need(',');
                }while(true);
            }
            if(Take('[')) {
                var result=new SequenceJson{Items=new List<SequenceJson>()};if(Take(']'))return result;
                do { result.Items.Add(Read(depth+1));if(Take(']'))return result;Need(','); }while(true);
            }
            int begin=At;
            while(At<Text.Length && Text[At]!=',' && Text[At]!=']' && Text[At]!='}' && !char.IsWhiteSpace(Text[At]))At++;
            string raw=Text.Substring(begin,At-begin);
            if(raw!="true" && raw!="false" && raw!="null" && !Regex.IsMatch(raw,@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$"))
                throw new InvalidOperationException("E180: JSONの値が不正です。");
            return new SequenceJson{Raw=raw};
        }
    }
}

// Retains every serialized editor property, including properties unknown to this extension.
// The parser uses only the script host's existing framework references.
public class SequenceEditorDocument
{
    public SequenceJson Editor;
    public string Schema;
    public static string Value(SequenceJson node,string key)
    { return node[key]==null?null:node[key].StringValue(); }
    public static SequenceEditorDocument Read(string json,string root,string editor)
    {
        var document=SequenceJson.Parse(json);
        if(document==null || document.Properties==null)throw new InvalidOperationException("E180: 図のエクスポートがJSONオブジェクトではありません。");
        var editors=document["Editors"];
        if(editors==null || editors.Items==null)throw new InvalidOperationException("E180: エクスポートにEditorsがありません。");
        var found=editors.Items.Where(e=>e!=null && Value(e,"Id")==editor && Value(e,"ModelId")==root).ToArray();
        if(found.Length!=1 || Value(found[0],"ViewType")!="SequenceDiagram")throw new InvalidOperationException("E180: 現在のシーケンス図をエクスポートから一意に取得できません。");
        string schema=Value(document,"SchemaVersion");
        if(!Regex.IsMatch(schema??"",@"^[0-9]+\.[0-9]+$"))throw new InvalidOperationException("E180: エクスポートのSchemaVersionが不正です。");
        var result=new SequenceEditorDocument{Schema=schema,Editor=SequenceJson.Parse(found[0].ToJsonString())};
        result.Messages();result.Shapes();return result;
    }
    public SequenceJson[] Messages()
    {
        var messages=Editor["Messages"];
        // Native serializers may omit an empty collection after the last deletion.
        // Capture still verifies the complete live shape set, so lost nonempty data is rejected.
        if(messages==null){messages=new SequenceJson{Items=new List<SequenceJson>()};Editor.Properties.Add("Messages",messages);}
        if(messages.Items==null)throw new InvalidOperationException("E180: メッセージ図形の配列がありません。");
        var result=messages.Items.ToArray();
        if(result.Any(n=>n==null || n.Properties==null || string.IsNullOrEmpty(Value(n,"Id")) || string.IsNullOrEmpty(Value(n,"ModelId")))
            || result.Select(n=>Value(n,"Id")).Distinct().Count()!=result.Length)
            throw new InvalidOperationException("E180: メッセージ図形の識別子が不正です。");
        return result;
    }
    public SequenceJson[] Shapes()
    {
        var shapes=new List<SequenceJson>();
        foreach(var property in Editor.Properties)
        {
            var array=property.Value;
            var items=array==null || array.Items==null?new[]{property.Value}:array.Items.ToArray();
            foreach(var n in items)
            {
                var o=n;
                if(o!=null && o.Properties!=null && o["Id"]!=null && o["ModelId"]!=null)shapes.Add(o);
            }
        }
        if(shapes.Select(n=>Value(n,"Id")).Distinct().Count()!=shapes.Count)throw new InvalidOperationException("E180: 図形IDが重複しています。");
        return shapes.ToArray();
    }
    public SequenceEditorDocument Without(IEnumerable<string> deleted)
    {
        var ids=new HashSet<string>(deleted);
        var copy=new SequenceEditorDocument{Schema=Schema,Editor=SequenceJson.Parse(Editor.ToJsonString())};
        var messages=copy.Editor["Messages"];
        for(int i=messages.Items.Count-1;i>=0;i--)if(ids.Contains(Value(messages.Items[i],"ModelId")))messages.Items.RemoveAt(i);
        return copy;
    }
    public string ImportJson()
    {
        return "{\"Type\":\"Model\",\"SchemaVersion\":"+SequencePayload.Q(Schema)+",\"TopElementId\":"+SequencePayload.Q(Value(Editor,"ModelId"))
            +",\"Entities\":[],\"Relations\":[],\"Editors\":["+Editor.ToJsonString()+"]}";
    }
    static string Canonical(SequenceJson node)
    {
        if(node==null)return "null";
        var obj=node;
        if(obj!=null && obj.Properties!=null)return "{"+string.Join(",",obj.Properties.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>SequencePayload.Q(p.Key)+":"+Canonical(p.Value)))+"}";
        var array=node;
        if(array!=null && array.Items!=null)return "["+string.Join(",",array.Items.Select(Canonical))+"]";
        string raw=node.ToJsonString();decimal number;
        if(raw.StartsWith("\"",StringComparison.Ordinal))return SequencePayload.Q(node.StringValue());
        if(decimal.TryParse(raw,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out number))return number.ToString("G29",System.Globalization.CultureInfo.InvariantCulture);
        return raw;
    }
    public string Fingerprint() { return SequenceMapFile.Hash(Canonical(Editor)); }
}
