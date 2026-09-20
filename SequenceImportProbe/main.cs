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

public void CommitReceiverStructure(ICommandContext context, ICommandParams parameters) { SequenceSyncRuntime.Preview(context.App,true,true,true,true); }
public void CommitUnusedExecutions(ICommandContext context, ICommandParams parameters) { SequenceSyncRuntime.Preview(context.App,true,true,true); }
public void TrialSequenceStructure(ICommandContext context, ICommandParams parameters) { SequenceSyncRuntime.Preview(context.App,true,true); }
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
public void ShowSequenceResult(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Show(context.App); }
public void ShowSequenceDetails(ICommandContext context, ICommandParams parameters) { foreach(var page in SequenceExperiment.Details.Split('\f')) context.App.Window.UI.ShowInformationDialog(page, SequenceExperiment.Title); }

public static class SequenceExperiment
{
    public const string Title = "シーケンス生成実験 / 0.8.25";
    public static string Summary = "シーケンス図を開き「PlantUMLを取り込む」または「最小図を生成」を押してください。";
    public static string Details = "まだ実行していません。";
    public static void Show(IApplication app) { app.Window.UI.ShowInformationDialog(Summary, Title); }

    public static void Run(IApplication app) { Run(app, false); }
    public static void Run(IApplication app, bool fromPlantUml, bool updateProbe=false, bool replaceExisting=false, bool deltaProbe=false, bool structureProbe=false)
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
        Action verifyTemporaryRollback=null;
        bool rollbackVerified=false;
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
            if (!app.Window.UI.ShowConfirmDialog(structureProbe ? "コピーのプロジェクトで実行してください。\n一時図の受信先変更と実行区間の削除・再作成を検証し、最後にすべて取り消します。\n削除中だけSDKの編集可否検査を一時停止します。自動保存はしません。" : replaceExisting ? "コピーのプロジェクトで実行してください。\n現在の図をPlantUMLの内容で置き換えます。図自体のIDは維持します。\n配下の要素と手作業の配置は作り直します。子要素と外部モデルとの関連は引き継ぎません。自動保存はしません。" : deltaProbe ? "コピーのプロジェクトで実行してください。\n一時図で名前変更・メッセージ1件の差分追加と削除を検証し、最後に取り消します。\n削除中だけSDKの編集可否検査を一時停止する実験です。既存図は更新せず、自動保存もしません。" : updateProbe ? "コピーのプロジェクトで実行してください。\n一時図を作り、同じIDでメッセージ名を再取り込みします。\n最後に一時図を含む操作を取り消します。既存図を更新する検証ではありません。\n自動保存はしません。" : "実プロジェクトのコピーを開いていますか？\n新しい検証用シーケンス図を同じ親に追加する実験です。\n既存図の内容は入力にコピーしません。自動保存しません。\n失敗時はトランザクションの取消を試みますが、実機での復元動作は未確認です。", Title)) return;
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
            if (!app.Window.UI.ShowConfirmDialog(structureProbe ? "一時図で受信先を送信側の実行区間へ変更し、受信実行区間を削除・再作成して接続を戻します。\n各段階を照合し、最後に一時図を取り消します。続けますか？" : replaceExisting ? "現在の図「"+sample.Name+"」を「"+payload.Name+"」へ更新します。\n"+plan.Summary()+"\n旧子要素: "+(replacement.AllIds.Length-2)+"件を置換します。\n図IDは維持し、子要素IDは変わります。続けますか？" : updateProbe ? "同じIDへの再取り込みを一時図で検証します。\nprobe()をupdatedProbe()へ変更したデータを再取り込みし、取消後に一時モデルが消えたことを確認します。\n続けますか？" : "新しい図「" + payload.Name + "」を追加します。\n" + (plan == null ? "A → B : probe()\nライフライン2本・同期メッセージ1本" : plan.Summary()) + "\n入力データの記録: 済み\n続けますか？", Title))
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
            var sourceSnapshot=structureProbe?SequenceReplacement.Capture((IInteraction)fresh,diagram):null;
            if(structureProbe)verifyTemporaryRollback=delegate {
                if(payload.Ids.Any(id=>current.GetModelById(id)!=null) || !originalChildren.SetEquals(owner.GetChildren().Select(m=>m.Id)))
                    throw new InvalidOperationException("E135: 一時モデルの除去・親配下の復元が不一致です。");
                var sourceRoot=current.GetModelById(sampleId) as IInteraction;
                sourceSnapshot.CheckUnchanged(sourceRoot,sourceRoot==null?null:sourceRoot.GetEditors().OfType<ISequenceDiagram>().SingleOrDefault(d=>d.Id==sourceSnapshot.Identity.Editor));
            };
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
                if(structureProbe)
                {
                    stage="接続・実行区間の検証";
                    SequenceStructureProbe.Run(current,payload,updatedDiagram,schema,directory,detail,
                        delegate(string value){stage=value;},delegate(string state,int issues){apiState=state;apiIssues=issues;});
                }
                stage="検証操作の取消";
                completion.Cancel(delegate { transaction.Rollback(); rolledBack=true; });
                if((addedId!=null && current.GetModelById(addedId)!=null) || payload.Ids.Any(id=>current.GetModelById(id)!=null) || !originalChildren.SetEquals(owner.GetChildren().Select(m=>m.Id)))
                    throw new InvalidOperationException("E135: 一時モデルの削除または親配下の復元を確認できませんでした。");
                if(verifyTemporaryRollback!=null){verifyTemporaryRollback();rollbackVerified=true;}
                detail.AppendLine("same-ID rename and temporary model removal: verified");
                Summary="ケース: UPDATE001 / 同じIDへの名称更新: 一致\n一時図・一時モデル: 取消後の除去を確認\n既存図への更新: 未実施 / プロジェクト保存: していません\n要素追加・削除・図形置換・参照保持は未検証です。\nこの結果画面を撮影してください。";
                if(structureProbe)Summary="ケース: UPDATE004 / 受信先変更・実行区間削除・再作成: 一致\nモデル・関連・図形IDの復元: 一致\n一時図・モデル: 取消後の除去を確認\n既存図への本反映・保存後再読込: 未検証\nこの結果と診断表示を撮影してください。";
                if(deltaProbe)Summary="ケース: UPDATE003 / 一時図での差分追加・削除: 一致\n既存モデル・関連・図形IDの保持: 一致\n一時図・モデル: 取消後の除去を確認\n既存図の差分更新: 未実装 / 保存・Git差分: 未確認\nこの結果と診断表示を撮影してください。";
            }
            else
            {
            // Require a successful journal write before committing; failures enter rollback.
            Write(Path.Combine(directory, "checked.txt"), detail + "\nモデル・送受信・シェイプ照合: 一致\ncommit: 未実行");
            stage = "確定";
            completion.Commit(delegate { transaction.Commit(); }); committed = true;
            Summary = "ケース: " + (structureProbe ? "UPDATE004" : replaceExisting ? "UPDATE002" : updateProbe ? (deltaProbe ? "UPDATE003" : "UPDATE001") : fromPlantUml ? "IMPORT001" : "CREATE001") + " / モデル・シェイプ照合: 一致\n"+(replaceExisting?"更新した図: ":"新しい図: ") + payload.Name
                + "\n" + (plan == null ? "ライフライン: 2 / メッセージ: 1" : plan.Summary()) + "\n確定: 済み / プロジェクト保存: していません\n図を開き直し、図とこの画面を撮影してください。\n図表示・Undo/Redo・再読込: 未確認";
            }
        }
        catch (Exception ex)
        {
            detail.AppendLine(ex.ToString());
            if (transaction != null && !committed)
            {
                try { completion.Cancel(delegate { transaction.Rollback(); rolledBack = true; }); }
                catch (Exception rollbackError) { detail.AppendLine("ROLLBACK: " + rollbackError); }
            }
            if(rolledBack && verifyTemporaryRollback!=null)
            {
                try { verifyTemporaryRollback();rollbackVerified=true;detail.AppendLine("temporary rollback: verified"); }
                catch(Exception restoreError){detail.AppendLine("temporary rollback verification: "+restoreError);}
            }
            if(rolledBack && replacement!=null)
            {
                try { replacement.CheckUnchanged(app.Workspace.CurrentProject.GetModelById(replacement.Identity.Root) as IInteraction,app.Workspace.CurrentEditor as ISequenceDiagram); detail.AppendLine("Rollback structure and shape IDs: verified"); }
                catch(Exception restoreError) { detail.AppendLine("Rollback verification: "+restoreError); }
            }
            Summary = "ケース: " + (structureProbe ? "UPDATE004" : replaceExisting ? "UPDATE002" : updateProbe ? (deltaProbe ? "UPDATE003" : "UPDATE001") : fromPlantUml ? "IMPORT001" : "CREATE001") + " / 停止段階: " + stage
                + "\nインポートAPI呼出: " + (called ? "あり" : "なし")
                + "\nAPI結果: " + apiState + " / 診断件数: " + apiIssues
                + "\n取消API: " + (rolledBack ? (rollbackVerified ? "正常終了・一時モデル除去確認済み" : "正常終了（復元は未確認）") : transaction == null ? "未呼出" : "未確認・失敗")
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

public static class SequenceStructureProbe
{
    static string[] Relations(IModel model)
    { return model.GetRelationsWhere((r,f)=>true).Select(r=>r.Id+":"+r.Source.Id+":"+r.Target.Id+":"+r.SourceIndex+":"+r.TargetIndex).Distinct().OrderBy(x=>x).ToArray(); }
    public static void Run(IProject project,SequencePayload seed,ISequenceDiagram diagram,string schema,string directory,StringBuilder log,Action<string> stage,Action<string,int> report)
    {
        var names=seed.Ids.Select(id=>project.GetModelById(id).Name).ToArray();
        var relations=seed.Ids.Select(id=>Relations(project.GetModelById(id))).ToArray();
        var shapes=diagram.Shapes.Select(sh=>sh.Id+":"+sh.ModelId).OrderBy(x=>x).ToArray();
        string editorId=diagram.Id;
        var message=(IMessage)project.GetModelById(seed.Ids[6]);
        var receiver=project.GetModelById(seed.Ids[5]);
        var link=message.GetRelationsWhere((r,f)=>r.Source.Id==receiver.Id && r.Target.Id==message.Id).Single();
        string linkId=link.Id;
        if(message.ReceivePort==null || ((IModel)message.ReceivePort).Id!=receiver.Id || message.SendPort==null || ((IModel)message.SendPort).Id!=seed.Ids[4])
            throw new InvalidOperationException("E160: 検証開始時のポートが一致しません。");
        stage("受信先を既存実行区間へ変更");
        Import(project,SequenceStructureInput.ReconnectReceiver(seed,linkId),directory,"structure-reconnect.json",log,report);
        CheckPorts(project,seed,seed.Ids[4],seed.Ids[2]);
        log.AppendLine("receiver reconnected to sender execution: verified");
        stage("受信実行区間を削除");
        using(project.SuspendModelVerification()){receiver.Delete();}
        string editor=SequenceStructureInput.WithoutReceiver(seed,schema);
        Import(project,editor,directory,"structure-delete-editor.json",log,report);
        var removed=project.GetModelById(seed.Ids[5]);
        var root=(IInteraction)project.GetModelById(seed.Ids[0]);
        var view=root.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==editorId);
        var expectedShapes=shapes.Where(x=>!x.EndsWith(":"+seed.Ids[5],StringComparison.Ordinal)).ToArray();
        if((removed!=null && !removed.IsDeleted) || root.GetChildren().OfType<IExecutionSpecification>().Count()!=1 || !expectedShapes.SequenceEqual(view.Shapes.Select(sh=>sh.Id+":"+sh.ModelId).OrderBy(x=>x)))
            throw new InvalidOperationException("E161: 受信実行区間の削除または残す図形の保持が不一致です。");
        CheckPorts(project,seed,seed.Ids[4],seed.Ids[2]);
        for(int i=0;i<seed.Ids.Length;i++)if(i!=5 && project.GetModelById(seed.Ids[i]).Name!=names[i])
            throw new InvalidOperationException("E162: 残すモデルの名前が変化しました。");
        log.AppendLine("receiver execution deletion and retained shape IDs: verified");
        stage("同じIDで実行区間を再作成・再接続");
        Import(project,SequenceUpdateProbe.Payload(seed.Json),directory,"structure-restore.json",log,report);
        CheckPorts(project,seed,seed.Ids[5],seed.Ids[3]);
        root=(IInteraction)project.GetModelById(seed.Ids[0]);
        view=root.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==editorId);
        if(root.GetChildren().OfType<IExecutionSpecification>().Count()!=2 || !shapes.SequenceEqual(view.Shapes.Select(sh=>sh.Id+":"+sh.ModelId).OrderBy(x=>x)))
            throw new InvalidOperationException("E163: 再作成後の実行区間数・図形IDが不一致です。");
        for(int i=0;i<seed.Ids.Length;i++)
        {
            var model=project.GetModelById(seed.Ids[i]);
            if(model==null || model.Name!=names[i] || !relations[i].SequenceEqual(Relations(model)))
                throw new InvalidOperationException("E164: 再作成後のモデル・関連IDまたは関連順序が不一致です。");
        }
        if(!Relations(project.GetModelById(seed.Ids[6])).Any(x=>x.StartsWith(linkId+":",StringComparison.Ordinal)))
            throw new InvalidOperationException("E165: 受信関連IDが変化しました。");
        log.AppendLine("execution recreation, ports, model/relationship/shape identities: verified");
    }
    static void CheckPorts(IProject project,SequencePayload seed,string receivePort,string receiverLifeline)
    {
        var root=(IInteraction)project.GetModelById(seed.Ids[0]);
        var message=project.GetModelById(seed.Ids[6]) as IMessage;
        if(message==null || root.Messages.Count()!=1 || root.Lifelines.Count()!=2 || message.Kind!="sync" || message.Sender==null || message.Sender.Id!=seed.Ids[2] || message.Receiver==null || message.Receiver.Id!=receiverLifeline
            || message.SendPort==null || ((IModel)message.SendPort).Id!=seed.Ids[4] || message.ReceivePort==null || ((IModel)message.ReceivePort).Id!=receivePort)
            throw new InvalidOperationException("E166: メッセージのID・種類・送受信先が不一致です。");
    }
    static void Import(IProject project,string json,string directory,string file,StringBuilder log,Action<string,int> report)
    {
        SequenceExperiment.Write(Path.Combine(directory,file),json);
        report("未取得（構造更新）",0);
        var result=project.ImportUnitFromJson(json,null,null);
        if(result==null)throw new InvalidOperationException("E167: 構造更新の結果がnullです。");
        report(result.State,result.Errors.Count());
        log.AppendLine(file+": "+result.State);
        foreach(var error in result.Errors)log.AppendLine(error.Kind+": "+error.Message);
        if(result.State!="success" || result.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))throw new InvalidOperationException("E168: 構造更新が失敗または警告を返しました。");
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

// BEGIN GENERATED SequenceSyncRuntime.cs
// Native reader for the semantic planner. It performs no project mutations.
public sealed class DiagramSnapshot
{
    public SequenceDocument Document=new SequenceDocument{HasTitle=true};
    public Dictionary<string,double> Y=new Dictionary<string,double>();
    public Dictionary<string,string> ShapeIds=new Dictionary<string,string>();
    public Dictionary<string,object> Geometry=new Dictionary<string,object>();
    public List<string> Limitations=new List<string>();
    public static DiagramSnapshot Read(ISequenceDiagram diagram, StringBuilder log)
    {
        var root=diagram.Model as IInteraction;
        if(root==null)throw new InvalidOperationException("S210: シーケンス図を開いてください。");
        var snapshot=new DiagramSnapshot();var doc=snapshot.Document;
        doc.Elements.Add(new SequenceElement{Id=root.Id,Kind="interaction",Text=root.Name});
        Action<ISequenceShape,string,string,double> add=(shape,kind,text,y)=>{
            if(shape.Model==null || shape.Model.IsDeleted)throw new InvalidOperationException("S210: モデルのない図形があります。");
            if(snapshot.ShapeIds.ContainsKey(shape.ModelId))throw new InvalidOperationException("S210: 同じモデルの図形が複数あります。");
            doc.Elements.Add(new SequenceElement{Id=shape.ModelId,Kind=kind,Text=text??"",Parent=root.Id});
            snapshot.ShapeIds.Add(shape.ModelId,shape.Id);snapshot.Y.Add(shape.ModelId,y);
            var node=shape as ISequenceNodeShape;
            if(node!=null)snapshot.Geometry[shape.ModelId]=PumlBuild.Obj("X",node.LocationX,"Y",node.LocationY,"Width",node.Width,"Height",node.Height);
            var message=shape as IMessageShape;
            if(message!=null)snapshot.Geometry[shape.ModelId]=PumlBuild.Obj("SourceY",message.SourceY,"TargetY",message.TargetY,"SelfloopBendsX",message.SelfloopBendsX);
        };
        foreach(var l in diagram.Lifelines.OrderBy(l=>l.LocationX).ThenBy(l=>l.Id,StringComparer.Ordinal))
            add(l,"participant",l.Text,l.LocationX);
        var fragmentRegions=new List<SequenceRegion>();var operandRegions=new List<SequenceRegion>();var annotationRegions=new List<SequenceRegion>();
        foreach(var f in diagram.Fragments)
        {
            add(f,"fragment","",f.LocationY);
            var element=doc.Elements.Last();
            string op=Convert.ToString(f.Model.GetField("Operator")).ToLowerInvariant();element.Attributes["operator"]=op;
            if(op=="group")element.Text=f.Model.Name;
            fragmentRegions.Add(new SequenceRegion{Id=f.ModelId,X=f.LocationX,Y=f.LocationY,Width=f.Width,Height=f.Height});
            var operands=f.Operands.OrderBy(o=>o.Position).ToArray();
            // Match the exporter convention; the SDK documents Position as an absolute Y.
            bool absolute=operands.All(o=>o.Position>=f.LocationY-0.00001 && o.Position<=f.LocationY+f.Height+0.00001);
            var positions=operands.Select(o=>absolute?(double)o.Position:f.LocationY+o.Position).ToArray();
            log.AppendLine("Fragment bounds: id="+f.ModelId+" x="+f.LocationX+" y="+f.LocationY+" width="+f.Width+" height="+f.Height+" operandPosition="+(absolute?"absolute":"relative"));
            if(!absolute)snapshot.Limitations.Add("オペランドPositionを相対座標として解釈（出力側と同じ規則）: "+f.ModelId);
            for(int i=0;i<operands.Length;i++)
            {
                var operand=operands[i];double top=positions[i],bottom=i+1<positions.Length?positions[i+1]:f.LocationY+f.Height;
                add(operand,"operand",i>0 && string.Equals(SequenceLabels.Fold(operand.Guard),"else",StringComparison.OrdinalIgnoreCase)?"":operand.Guard,i==0?f.LocationY:top);doc.Elements.Last().Parent=f.ModelId;
                log.AppendLine("Operand bounds: id="+operand.ModelId+" fragment="+f.ModelId+" top="+top+" bottom="+bottom+" rawPosition="+operand.Position);
                if(top<f.LocationY-0.00001 || bottom>f.LocationY+f.Height+0.00001 || bottom<=top)
                    throw new InvalidOperationException("S210: オペランドの境界が不正です: "+operand.ModelId);
                operandRegions.Add(new SequenceRegion{Id=operand.ModelId,Fragment=f.ModelId,X=f.LocationX,Y=top,Width=f.Width,Height=bottom-top});
            }
        }
        foreach(var e in diagram.ExecutionSpecifications)
        {
            add(e,"execution","",e.LocationY);
            if(e.Lifeline==null)throw new InvalidOperationException("S210: 実行区間のライフラインがありません。");
            doc.Elements.Last().Links["participant"]=new[]{e.Lifeline.ModelId};
        }
        foreach(var m in SequenceExportMatch.Order(diagram.Messages,m=>m.SourceY,m=>m.SendPort is ISequenceNodeShape?((ISequenceNodeShape)m.SendPort).LocationX:0,m=>m.Id))
        {
            add(m,"message",m.Text,m.SourceY);var e=doc.Elements.Last();var model=m.Model as IMessage;
            if(model==null)throw new InvalidOperationException("S210: メッセージの型が不正です。");
            e.Attributes["sort"]=model.Kind;
            e.Links["sender"]=m.Sender==null?new string[0]:new[]{m.Sender.ModelId};
            e.Links["receiver"]=m.Receiver==null?new string[0]:new[]{m.Receiver.ModelId};
            if(m.SendPort is IExecutionSpecificationShape)e.Links["sendExecution"]=new[]{m.SendPort.ModelId};
            if(m.ReceivePort is IExecutionSpecificationShape)e.Links["receiveExecution"]=new[]{m.ReceivePort.ModelId};
        }
        foreach(var n in diagram.Notes)
        {
            add(n,"note",n.Text,n.LocationY);var e=doc.Elements.Last();var targets=new List<string>();
            annotationRegions.Add(new SequenceRegion{Id=n.ModelId,X=n.LocationX,Y=n.LocationY,Width=n.Width,Height=n.Height});
            foreach(var anchor in n.NoteAnchors)
            {
                var other=anchor.Source.Id==n.Id?anchor.Target:anchor.Source;
                var l=other as ILifelineShape;
                if(l!=null)targets.Add(l.ModelId);
                else { e.Links["anchors"]=n.NoteAnchors.Select(a=>a.Source.Id==n.Id?a.Target.ModelId:a.Source.ModelId).Distinct().ToArray();snapshot.Limitations.Add("Noteの非ライフライン接続: "+n.ModelId); }
            }
            e.Links["targets"]=targets.Distinct().OrderBy(id=>snapshot.Y[id]).ToArray();
            e.Attributes["position"]="over";
            if(targets.Count==0) {e.Attributes["position"]="free";snapshot.Limitations.Add("自由配置Note（近傍ライフラインには結び付けない）: "+n.ModelId);}
            else
            {
                var lines=diagram.Lifelines.Where(l=>targets.Contains(l.ModelId)).ToArray();
                if(n.LocationX+n.Width<lines.Min(l=>l.LocationX))e.Attributes["position"]="left of";
                else if(n.LocationX>lines.Max(l=>l.LocationX+l.Width))e.Attributes["position"]="right of";
            }
        }
        foreach(var u in diagram.InteractionUses)
        {
            add(u,"ref",u.Text,u.LocationY);var e=doc.Elements.Last();
            annotationRegions.Add(new SequenceRegion{Id=u.ModelId,X=u.LocationX,Y=u.LocationY,Width=u.Width,Height=u.Height});
            e.Links["targets"]=u.Lifelines.OrderBy(l=>l.LocationX).Select(l=>l.ModelId).ToArray();
            var model=u.Model as IInteractionUse;
            e.Attributes["reference"]=model==null || model.RefersTo==null?"":model.RefersTo.Id;
        }
        foreach(var d in diagram.Destructions)
        {
            add(d,"destroy","",d.LocationY);
            doc.Elements.Last().Links["participant"]=new[]{d.Lifeline.ModelId};
        }
        var byId=doc.Elements.ToDictionary(e=>e.Id);
        // Model ownership is not operand membership: use the actual structural relationships.
        var relations=SequenceMappedUpdate.Tree(root).SelectMany(m=>m.GetRelationsWhere((r,f)=>true)).GroupBy(r=>r.Id).Select(g=>g.First()).ToArray();
        var memberships=new List<SequenceMembership>();
        foreach(var relation in relations)
        {
            if(!byId.ContainsKey(relation.Source.Id) || !byId.ContainsKey(relation.Target.Id))continue;
            if(relation.Metaclass.Id==SequencePayload.Prefix+"NestedInteractionFragment" || relation.Metaclass.Id==SequencePayload.Prefix+"OperandTargetMessage")
            {
                memberships.Add(new SequenceMembership{Child=relation.Target.Id,Parent=relation.Source.Id,
                    Evidence=relation.Metaclass.Id+" / "+relation.Id});
            }
        }
        foreach(var operand in diagram.Fragments.SelectMany(f=>f.Operands))foreach(var message in operand.Messages)
        {
            memberships.Add(new SequenceMembership{Child=message.ModelId,Parent=operand.ModelId,Evidence="SDK operand.Messages"});
        }
        memberships.AddRange(SequenceRegion.Nesting(operandRegions,fragmentRegions.Concat(annotationRegions)));
        SequenceMembership.Resolve(doc,memberships,line=>log.AppendLine(line));

        Func<double,double,string> containerAt=(x,y)=>{
            var candidates=operandRegions.Where(r=>x>=r.X && x<=r.X+r.Width && y>=r.Y && y<r.Y+r.Height-1.0).ToArray();
            var nearest=candidates.Where(r=>!candidates.Any(inner=>inner.Id!=r.Id && SequenceRegion.Contains(r,inner))).ToArray();
            if(nearest.Length>1) {snapshot.Limitations.Add("実行区間境界の所属候補が複数");return root.Id;}
            return nearest.Length==1?nearest[0].Id:root.Id;
        };
        foreach(var e in diagram.ExecutionSpecifications)
        {
            var item=byId[e.ModelId];
            var events=doc.Elements.Where(n=>n.Kind!="participant" && n.Kind!="interaction" && n.Kind!="execution").OrderBy(n=>snapshot.Y[n.Id]).ToArray();
            // The exporter snaps interval ends to neighbouring messages within ten pixels.
            Func<string,double,SequenceElement> endpoint=(role,y)=>{
                var candidates=doc.Elements.Where(n=>n.Kind=="message" && n.Links.ContainsKey(role) && n.Links[role].Contains(e.ModelId))
                    .Where(n=>Math.Abs(snapshot.Y[n.Id]-y)<=10.0).OrderBy(n=>Math.Abs(snapshot.Y[n.Id]-y)).ToArray();
                if(candidates.Length>1 && Math.Abs(Math.Abs(snapshot.Y[candidates[0].Id]-y)-Math.Abs(snapshot.Y[candidates[1].Id]-y))<0.00001)return null;
                return candidates.FirstOrDefault();
            };
            var trigger=endpoint("receiveExecution",e.LocationY);var origin=trigger==null?endpoint("sendExecution",e.LocationY):null;
            double start=trigger!=null?snapshot.Y[trigger.Id]:origin!=null?snapshot.Y[origin.Id]:e.LocationY;
            var closer=endpoint("sendExecution",e.LocationY+e.Length);
            double end=closer==null?e.LocationY+e.Length:snapshot.Y[closer.Id];
            var preceding=trigger??events.LastOrDefault(n=>snapshot.Y[n.Id]<start);
            var following=events.FirstOrDefault(n=>snapshot.Y[n.Id]>end);
            item.Parent=trigger!=null?trigger.Parent:origin!=null?origin.Parent:containerAt(e.LocationX,start);
            item.Links["startAfter"]=preceding==null?new string[0]:new[]{preceding.Id};
            item.Links["endBefore"]=following==null?new string[0]:new[]{following.Id};
            item.Links["endContainer"]=new[]{closer!=null?closer.Parent:containerAt(e.LocationX,end)};
            var parent=diagram.ExecutionSpecifications.Where(p=>p.ModelId!=e.ModelId && p.Lifeline!=null && p.Lifeline.ModelId==e.Lifeline.ModelId
                && p.LocationY<=e.LocationY+1.0 && p.LocationY+p.Length>=e.LocationY+e.Length-1.0
                && (Math.Abs(p.LocationY-e.LocationY)>1.0 || Math.Abs(p.Length-e.Length)>1.0 || p.LocationX<e.LocationX))
                .OrderBy(p=>p.Length).ThenByDescending(p=>p.LocationX).FirstOrDefault();
            if(parent!=null)item.Links["outer"]=new[]{parent.ModelId};
        }
        foreach(var group in doc.Elements.Where(e=>e.Parent!=null).GroupBy(e=>e.Parent))
        {
            int order=0;foreach(var e in group.OrderBy(e=>e.Kind=="participant"?0:1).ThenBy(e=>snapshot.Y[e.Id]).ThenBy(e=>e.Id,StringComparer.Ordinal))e.Order=order++;
        }
        var represented=new HashSet<string>(doc.Elements.Select(e=>e.Id));
        foreach(var m in SequenceMappedUpdate.Tree(root).Where(m=>!represented.Contains(m.Id) && !(m is IFrame) && !(m is IMessageEnd)))
            snapshot.Limitations.Add("共通構造に未収録のモデル: "+m.Id+" / "+m.Metaclass.Id);
        // A read-only report deliberately exposes inference gaps before enabling writes.
        if(diagram.ExecutionSpecifications.Any())snapshot.Limitations.Add("実行区間の境界・分岐跨ぎはSDK読取りとPlantUMLの比較を実機照合してください。");
        doc.Validate();return snapshot;
    }
}

public static class SequenceSyncRuntime
{
    static string QualifiedName(IModel model)
    {
        var parts=new List<string>();var visited=new HashSet<string>();
        while(model!=null) {if(!visited.Add(model.Id))throw new InvalidOperationException("S210: モデルの所有関係が循環しています。");parts.Add(model.Name);model=model.Owner;}
        parts.Reverse();return string.Join("::",parts);
    }
    public static void Preview(IApplication app,bool prepare=false,bool trial=false,bool retain=false,bool reconnectCommit=false)
    {
        var log=new StringBuilder();string report=null;string screenshot=null;
        retain=retain||reconnectCommit;trial=trial||retain;prepare=prepare||trial;
        try
        {
            var diagram=app.Workspace.CurrentEditor as ISequenceDiagram;
            if(diagram==null)throw new InvalidOperationException("S210: シーケンス図を開いてください。");
            string path=app.Window.UI.ShowOpenFileDialog("図全体と比較するPlantUML","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
            if(string.IsNullOrEmpty(path))return;
            if(new FileInfo(path).Length>300000)throw new InvalidOperationException("S210: 入力は300KB以下にしてください。");
            var desired=SequenceDocument.Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
            var current=DiagramSnapshot.Read(diagram,log);
            // Resolve only unambiguous references for this non-mutating audit command.
            var project=app.Workspace.CurrentProject;
            var interactions=SequenceMappedUpdate.Tree(project.DesignModel).OfType<IInteraction>()
                .Select(m=>new SequenceReferenceCandidate{Id=m.Id,Name=m.Name,Path=QualifiedName(m)}).ToArray();
            foreach(var e in desired.Elements.Where(e=>e.Kind=="ref"))
            {
                var matches=SequenceReferenceResolver.Find(e.Text,interactions);
                e.Attributes["reference"]=matches.Length==1?matches[0].Id:"";
                if(matches.Length!=1)current.Limitations.Add("ref参照先 "+e.Line+"行: "+matches.Length+"候補");
            }
            var plan=SequenceNotePolicy.Build(current.Document,desired,()=>Guid.NewGuid().ToString());
            var preflight=SequenceStructurePreflight.Check(current.Document,plan);
            if(retain && !preflight.CanCommit(reconnectCommit))
                throw new InvalidOperationException(reconnectCommit?"S231: 既存区間への受信接続変更と区間削除だけの差分が必要です。":"S231: 確定できるのは未使用実行区間の削除だけです。差分を検証してください。");
            report="{\"version\":1,\"project\":"+SequencePayload.Q(project.Id)+",\"diagram\":"+SequencePayload.Q(diagram.Id)
                +",\"current\":"+current.Document.ToJson()+",\"desired\":"+desired.ToJson()+",\"plan\":"+plan.ToJson()
                +",\"structurePreflight\":"+preflight.ToJson()+",\"expected\":"+plan.Expected.ToJson()+",\"limitations\":"+PumlBuild.Json(current.Limitations.ToArray())
                +",\"shapes\":"+PumlBuild.Json(current.ShapeIds.ToDictionary(p=>p.Key,p=>(object)p.Value))
                +",\"geometry\":"+PumlBuild.Json(current.Geometry)+"}";
            foreach(var c in plan.Changes)log.AppendLine(c.Action+" "+c.Kind+" line="+c.Line+" id="+c.Id);
            foreach(var warning in current.Limitations)log.AppendLine("要照合: "+warning);
            screenshot=SequenceAudit.Reasons(current.Document,desired,plan)+"\f"+preflight.Summary();
            log.AppendLine(screenshot);
            SequenceExperiment.Summary=SequenceAudit.Summary(plan,current.Limitations.Count)+"\n構造更新の停止理由: "+preflight.Reasons.Count+"件（診断表示）";
            log.AppendLine("Scope: "+project.Id+" / "+diagram.ModelId+" / "+diagram.Id);
            if(prepare)
            {
                if(!preflight.Candidate)
                    SequenceExperiment.Summary="構造更新データ: 未作成 / 図への反映なし\n"+preflight.Summary();
                else
                {
                    var root=diagram.Model as IInteraction;
                    if(root==null || !root.IsEditable || root.IsProxy || root.IsDeleted || string.IsNullOrEmpty(project.Path))
                        throw new InvalidOperationException("S220: 保存済みで編集可能な図を開いてください。");
                    string exported=null;
                    SequenceEditorCapture.Read(project,root,diagram,log,delegate(string value){exported=value;});
                    var preparation=SequenceStructurePreparation.Build(exported,diagram.Id,current.Document,plan);
                    var raw=SequenceJson.Parse(exported);
                    var exportedRelations=new HashSet<string>(raw["Relations"].Items.Select(r=>SequenceEditorDocument.Value(r,"Id")));
                    foreach(string id in preflight.DeleteExecutions.Concat(preflight.ReconnectMessages))
                    {
                        var model=project.GetModelById(id);
                        if(model==null || model.IsDeleted || model.IsProxy || !model.IsEditable)
                            throw new InvalidOperationException("S220: 更新対象に編集不可のモデルがあります。");
                        if(preflight.DeleteExecutions.Contains(id) && model.GetRelationsWhere((r,f)=>true).Any(r=>!exportedRelations.Contains(r.Id)))
                            throw new InvalidOperationException("S220: 削除対象に退避範囲外の関連があります。");
                    }
                    if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=diagram.Id
                        || DiagramSnapshot.Read(diagram,new StringBuilder()).Document.ToJson()!=current.Document.ToJson())
                        throw new InvalidOperationException("S220: 準備中に対象の図が変化しました。");
                    string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NextDesign.SequenceSync","prepared",Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(directory);
                    // ready.json is written last. Partial directories must never be used as a package.
                    SequenceExperiment.Write(Path.Combine(directory,"snapshot.json"),exported);
                    SequenceExperiment.Write(Path.Combine(directory,"reconnect.json"),preparation.ReconnectJson);
                    SequenceExperiment.Write(Path.Combine(directory,"editor-after-delete.json"),preparation.EditorAfterDeleteJson);
                    SequenceExperiment.Write(Path.Combine(directory,"plan.json"),report);
                    SequenceExperiment.Write(Path.Combine(directory,"ready.json"),PumlBuild.Json(PumlBuild.Obj("Version",1,"Mode","prepare-only", "ProjectId",project.Id,"DiagramId",diagram.Id,
                        "DeleteExecutions",preparation.DeleteIds,"ExecutionOrder",new[]{"reconnect.json","delete listed execution models","editor-after-delete.json"})));
                    log.AppendLine("Prepared directory: "+directory);
                    screenshot+="\f構造更新データの準備: 完了\n受信接続変更: "+preflight.ReconnectMessages.Count+" / 実行区間削除: "+preparation.DeleteIds.Length
                        +"\n退避データ・接続変更JSON・削除後の図形JSONを保存しました。\n図への適用・Undo/Redo・保存再読込: 未実施";
                    SequenceExperiment.Summary="構造更新データの準備: 完了 / 図への反映なし\n受信接続変更: "+preflight.ReconnectMessages.Count+" / 実行区間削除: "+preparation.DeleteIds.Length
                        +"\n保存先: "+directory+"\n準備ファイルの手動インポートはしないでください。保存ファイルから適用する機能はありません。";
                    if(trial)
                    {
                        SequenceExperiment.Summary=SequenceStructureTrial.Run(app,project,diagram,preparation,plan,exported,directory,log,retain,reconnectCommit);
                        screenshot=SequenceExperiment.Summary+"\f会社PC内の試行診断\n"+log.ToString();
                    }
                }
            }
        }
        catch(Exception ex) {SequenceExperiment.Summary=(trial?"構造更新の試行を完了できませんでした。診断表示を確認してください。":prepare?"構造更新データの準備を完了できませんでした。図への反映なし。":"図全体の読取り検証を完了できませんでした。")+"\n"+ex.Message;log.AppendLine(ex.ToString());screenshot=null;}
        try
        {
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NextDesign.SequenceSync","reports");
            Directory.CreateDirectory(directory);
            string stem=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd_HHmmss")+"_"+Guid.NewGuid().ToString("N").Substring(0,8));
            File.WriteAllText(stem+".txt",log.ToString(),new UTF8Encoding(false));
            if(report!=null)File.WriteAllText(stem+".json",report,new UTF8Encoding(false));
            if(screenshot==null)SequenceExperiment.Summary+="\n診断保存先: "+stem+".txt";
        }
        catch(Exception ex) {log.AppendLine("診断の保存失敗: "+ex.Message);SequenceExperiment.Summary+="\n診断ファイルを保存できませんでした。診断表示で確認してください。";}
        SequenceExperiment.Details=screenshot??log.ToString();SequenceExperiment.Show(app);
    }
}

public static class SequenceStructureTrial
{
    static string Port(IMessagePort value) {var m=value as IModel;return m==null?"":m.Id;}
    static string Number(double value){return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
    static SequenceTrialState Read(IInteraction root,ISequenceDiagram diagram)
    {
        var state=new SequenceTrialState();
        var tree=SequenceMappedUpdate.Tree(root).ToArray();
        Action<IModel> record=m=>state.Models[m.Id]=PumlBuild.Json(new[]{m.Metaclass.Id,m.Name,m.Owner==null?"":m.Owner.Id,m.IsDeleted.ToString()});
        foreach(var model in tree)
        {
            record(model);
            foreach(var r in model.GetRelationsWhere((relation,field)=>true))
            {
                state.Relations[r.Id]=new[]{r.Source.Id,r.Target.Id,r.SourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),r.TargetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                record(r.Source);record(r.Target);
            }
        }
        foreach(var m in root.Messages)
            state.Ports[m.Id]=new[]{Port(m.SendPort),Port(m.ReceivePort),m.Sender==null?"":m.Sender.Id,m.Receiver==null?"":m.Receiver.Id,m.Kind};
        foreach(var shape in diagram.Shapes)
        {
            var rows=new List<string>();var node=shape as ISequenceNodeShape;
            if(node!=null)rows.AddRange(new[]{Number(node.LocationX),Number(node.LocationY),Number(node.Width),Number(node.Height)});
            var message=shape as IMessageShape;
            if(message!=null)rows.AddRange(new[]{message.Text,Number(message.SourceY),Number(message.TargetY),Number(message.SelfloopBendsX)});
            var execution=shape as IExecutionSpecificationShape;if(execution!=null)rows.Add(Number(execution.Length));
            state.Shapes[shape.Id]=PumlBuild.Json(rows.ToArray());state.ShapeModels[shape.Id]=shape.ModelId;
        }
        foreach(var f in diagram.Fragments){state.Shapes[f.Id]+=f.Text;foreach(var o in f.Operands)state.Shapes[o.Id]+=PumlBuild.Json(new[]{o.Guard,Number(o.Position)});}
        foreach(var n in diagram.Notes)state.Shapes[n.Id]+=n.Text;
        foreach(var u in diagram.InteractionUses)state.Shapes[u.Id]+=u.Text;
        foreach(var l in diagram.Lifelines)state.Shapes[l.Id]+=Number(l.TimelineLength);
        return state;
    }
    static void Verify(SequenceTrialState expected,SequenceTrialState actual,string phase,StringBuilder log)
    {
        string differences=expected.DifferenceCounts(actual);log.AppendLine(phase+": "+differences);
        if(expected.Signature()!=actual.Signature())throw new InvalidOperationException("S230: "+phase+"の照合が不一致です。"+differences);
    }
    static void Import(IProject project,string json,StringBuilder log)
    {
        var result=project.ImportUnitFromJson(json,null,null);
        if(result==null)throw new InvalidOperationException("S230: インポート結果がありません。");
        log.AppendLine("trial import: "+result.State);
        foreach(var e in result.Errors)log.AppendLine(e.Kind+": "+e.Message);
        if(result.State!="success" || result.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))throw new InvalidOperationException("S230: インポートが失敗または警告を返しました。");
    }
    public static string Run(IApplication app,IProject project,ISequenceDiagram diagram,SequenceStructurePreparation prepared,SyncPlan plan,string exported,string directory,StringBuilder log,bool retain=false,bool reconnectCommit=false)
    {
        int reconnectCount=SequenceJson.Parse(prepared.ReconnectJson)["Relations"].Items.Count;
        if(reconnectCommit && !retain)throw new InvalidOperationException("S231: 確定モードが不正です。");
        if(retain && (prepared.DeleteIds.Length==0 || (reconnectCommit?reconnectCount==0:reconnectCount!=0)
            || plan.Changes.Any(c=>!(c.Action=="delete" && c.Kind=="execution") && !(reconnectCommit && c.Action=="update" && c.Kind=="message"))))
            throw new InvalidOperationException("S231: 確定モードの対象外の差分があります。");
        string caseId=reconnectCommit?"UPDATE007":retain?"UPDATE006":"UPDATE005";
        var root=diagram.Model as IInteraction;
        var before=Read(root,diagram);string original=before.Signature();
        var expectedReconnect=before.Expected(prepared,plan,false);
        var expectedFinal=before.Expected(prepared,plan,true);
        string rootId=root.Id,editorId=diagram.Id;
        Func<ISequenceDiagram> fresh=()=>{
            var model=project.GetModelById(rootId) as IInteraction;
            if(model==null)throw new InvalidOperationException("S230: 対象の図を取得できません。");
            return model.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==editorId);
        };
        string confirmation=retain
            ? "コピーのプロジェクトで実行してください。\n受信接続変更: "+reconnectCount+"件 / 実行区間削除: "+prepared.DeleteIds.Length+"件。照合成功時に変更を確定します。\n自動保存はしません。確定後はUndo/Redoと保存再読込を確認してください。実行しますか？"
            : "コピーのプロジェクトで実行してください。\n受信接続変更と実行区間削除を一時適用し、照合後に必ず取り消します。\n自動保存・変更の確定は行いません。試行しますか？";
        if(!app.Window.UI.ShowConfirmDialog(confirmation,SequenceExperiment.Title))
            return caseId+": キャンセル / 図への変更なし";
        if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=editorId
            || Read(root,fresh()).Signature()!=original)
            throw new InvalidOperationException("S230: 確認中に対象の図が変化しました。");
        // Serialized attributes are checked before starting; export is unavailable after a write.
        if(SequenceEditorCapture.Read(project,root,diagram,log).Fingerprint()!=SequenceEditorDocument.Read(exported,rootId,editorId).Fingerprint())
            throw new InvalidOperationException("S230: 確認中に表示設定が変化しました。");
        SequenceExperiment.Write(Path.Combine(directory,"trial-before-sdk.json"),original);
        SequenceExperiment.Write(Path.Combine(directory,"trial-expected-sdk.json"),expectedFinal.Signature());
        string stage="トランザクション開始";
        var transaction=project.BeginUndoTransaction(false);
        if(transaction==null)throw new InvalidOperationException("S230: トランザクションを開始できません。");
        Action apply=delegate {
            stage="受信接続の変更";Import(project,prepared.ReconnectJson,log);
            Verify(expectedReconnect,Read((IInteraction)project.GetModelById(rootId),fresh()),"接続変更後",log);
            log.AppendLine("receiver reconnection count: "+SequenceJson.Parse(prepared.ReconnectJson)["Relations"].Items.Count+"; SDK state verified");
            stage="不要実行区間の削除";
            using(project.SuspendModelVerification())foreach(string id in prepared.DeleteIds)project.GetModelById(id).Delete();
            stage="削除後のエディタ反映";Import(project,prepared.EditorAfterDeleteJson,log);
            foreach(string id in prepared.DeleteIds){var m=project.GetModelById(id);if(m!=null && !m.IsDeleted)throw new InvalidOperationException("S230: 削除対象が残っています。");}
            Verify(expectedFinal,Read((IInteraction)project.GetModelById(rootId),fresh()),"削除後",log);
            log.AppendLine("trial execution deletion and SDK state: verified");
        };
        Action rollback=delegate {transaction.Rollback();};
        Action verifyRestored=delegate {Verify(before,Read((IInteraction)project.GetModelById(rootId),fresh()),"取消後",log);};
        // Explicit completion only; Dispose may attempt a second rollback.
        string summary;
        if(retain)
        {
            var completion=new SequenceCommitTrial();
            completion.Run(apply,delegate {stage="変更の確定";transaction.Commit();},rollback,verifyRestored);
            foreach(var error in new[]{completion.ApplyError,completion.CommitError,completion.RollbackError,completion.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            summary="ケース: "+caseId+" / "+(completion.Committed?"構造更新・SDK照合・変更確定: 成功":"停止段階: "+stage)
                +(completion.Committed?"\nUndo/Redoと保存再読込を確認してください。":"\n取消API: "+(completion.RollbackReturned?"正常終了":"失敗・未確認")+" / 復元照合: "+(completion.Restored?"一致":"未確認・不一致"))
                +(!completion.Committed && !completion.Restored?"\n保存せずコピーを開き直してください。":"")
                +"\nプロジェクトの自動保存: していません\nUndo/Redo・スタイル読戻し・保存再読込: 未検証\nこの結果と診断表示を撮影してください。";
        }
        else
        {
            var trial=new SequenceRollbackTrial();trial.Run(apply,rollback,verifyRestored);
            foreach(var error in new[]{trial.ApplyError,trial.RollbackError,trial.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            summary="ケース: UPDATE005 / "+(trial.Applied?"一時適用・SDK読戻し照合: 一致":"停止段階: "+stage)
                +"\n取消API: "+(trial.RollbackReturned?"正常終了":"失敗・未確認")+" / 復元照合: "+(trial.Restored?"一致":"未確認・不一致")
                +"\n変更の確定・プロジェクト保存: していません\nスタイルの適用後読戻し・保存再読込: 未検証"
                +(trial.Restored?"":"\n保存せずコピーを開き直してください。")+"\nこの結果と診断表示を撮影してください。";
        }
        log.AppendLine(summary);
        try{SequenceExperiment.Write(Path.Combine(directory,"trial-result.txt"),summary+"\n"+log.ToString());}
        catch(Exception ex){log.AppendLine("trial result save: "+ex);summary+="\n試行結果の記録: 保存失敗";}
        return summary;
    }
}
// END GENERATED SequenceSyncRuntime.cs

// Pure JSON builder. Only newly generated entity IDs appear as relation endpoints.

public static class SequenceMappedUpdate
{
    public static IEnumerable<IModel> Tree(IModel root)
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
            // Sequence shape style getters are not reliable on the target SDK runtime.
            // Deletion preserves serialized styles in the full editor import payload.
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
        foreach(var e in diagram.ExecutionSpecifications.Where(e=>!removed.Contains(e.ModelId)).OrderBy(e=>e.Id))rows.Add(e.Id+":"+Number(e.Length));
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
                ids.Add("");continue; // Preserve the input position as an explicit unmatched row.
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
        coverage="\nPlantUML側だけの行（図側に対応先なし）: "+ids.Count(string.IsNullOrEmpty)+"件\n対象行: "+string.Join(", ",nodes.Where((n,i)=>string.IsNullOrEmpty(ids[i])).Select(n=>n.Line.ToString()))+"\n本文の差分を検出: "+renamed+"件（対応表作成では変更しません）\n対応表に含まれない図側の要素: 参加者 "+extraLines.Length+"件 / メッセージ "+extraMessages.Length+"件";
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
        IProject project=null;
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
                SequenceExperiment.Summary="対応表を作成しました。\n対応付け済みメッセージ: "+map.MessageIds.Count(id=>!string.IsNullOrEmpty(id))+"件"+coverage+"\n図とPlantUMLは変更していません。\n保存先: "+path;
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
                var missingLines=map.MissingLines(plan);
                if(missingLines.Length>0)throw new InvalidOperationException("E182: 図側にないメッセージの復元が必要です: "+missingLines.Length+"件（PlantUML行: "+string.Join(", ",missingLines)+"）。対応表には差分を記録済みですが、この版では復元を実装していません。図は変更していません。");
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
                var remainingMessages=Tree(root).OfType<IMessage>().Where(m=>!unmapped.Contains(m.Id)).ToArray();
                var usedPorts=remainingMessages.SelectMany(m=>new[]{m.SendPort,m.ReceivePort}).OfType<IModel>().Select(p=>p.Id)
                    .Concat(diagram.Messages.Where(m=>!unmapped.Contains(m.ModelId)).SelectMany(m=>new[]{m.SendPort,m.ReceivePort}).Where(p=>p!=null).Select(p=>p.ModelId)).ToArray();
                // Initialization/destruction executions and owners of child models have semantics beyond their messages.
                var candidates=Tree(root).OfType<IExecutionSpecification>().Where(e=>!e.IsInitialization && !e.IsDestruction && !e.GetChildren().Any()).ToArray();
                var protectedByRelations=candidates.Where(e=>e.GetRelationsWhere((r,f)=>true).Any(r=>
                    (r.Source is IMessage && !unmapped.Contains(r.Source.Id)) || (r.Target is IMessage && !unmapped.Contains(r.Target.Id)))).Select(e=>e.Id);
                var emptyBars=SequenceActivationCleanup.Unused(candidates.Select(e=>e.Id),usedPorts.Concat(protectedByRelations));
                var removedIds=unmapped.Concat(emptyBars).ToArray();
                detail.AppendLine("Unused activation bars="+string.Join(",",emptyBars));
                string coverage="\nPlantUMLにないメッセージの削除: "+unmapped.Length+"件\n接続メッセージのないアクティベーションバーの削除: "+emptyBars.Length+"件";
                detail.AppendLine("Unmapped messages="+string.Join(",",unmapped));
                detail.AppendLine("Name merge: requested="+requested.Count+", writes="+edits.Count+", PlantUML priority="+merge.Conflicts+", already matched="+merge.AlreadyMatched);
                if(edits.Count==0 && removedIds.Length==0 && plan.Retained.Length==map.MessageIds.Length && requested.All(e=>e.Before==e.After))
                {
                    SequenceExperiment.Summary="対応付け済みメッセージの本文差分なし。更新APIは呼び出していません。"+coverage+"\n追加・移動・実行区間の一般同期は未対応です。\n図・PlantUML・対応表は変更していません。";
                    detail.AppendLine("No-op; no transaction or model write.");
                }
                else
                {
                    var names=edits.ToDictionary(e=>map.MessageIds[e.Index],e=>e.After);
                    var removed=new HashSet<string>(removedIds);
                    var deletionModels=removedIds.Select(id=>project.GetModelById(id)).ToArray();
                    if(deletionModels.Any(m=>m==null || m.IsDeleted || !m.IsEditable || !Tree(root).Any(n=>n.Id==m.Id) || m.GetChildren().Any()))
                        throw new InvalidOperationException("E181: 削除対象が編集不可・別図所属・子要素ありのいずれかです。");
                    if(root.GetEditors().OfType<ISequenceDiagram>().Where(d=>d.Id!=diagram.Id).Any(d=>d.Shapes.Any(m=>removed.Contains(m.ModelId))))
                        throw new InvalidOperationException("E181: 削除対象が別のシーケンス図にも表示されています。この版は複数図の同時削除に未対応です。");
                    foreach(var id in names.Keys)if(!project.GetModelById(id).IsEditable)throw new InvalidOperationException("E169: 更新対象のメッセージを編集できません。");
                    // Relations incident to deleted messages may disappear; their other endpoint models must survive.
                    var external=deletionModels.SelectMany(m=>m.GetRelationsWhere((r,f)=>true)).SelectMany(r=>new[]{r.Source,r.Target})
                        .Where(m=>!removed.Contains(m.Id)).GroupBy(m=>m.Id).Select(g=>g.First()).ToDictionary(m=>m.Id,m=>m.Metaclass.Id+":"+m.Name);
                    SequenceEditorDocument editorBefore=null,editorAfter=null;
                    if(removedIds.Length>0)
                    {
                        editorBefore=SequenceEditorCapture.Read(project,root,diagram,detail);
                        editorAfter=editorBefore.Without(removedIds);
                    }
                    string preview=string.Join("\n",edits.Take(10).Select(e=>e.Line+"行目: "+e.Before+" → "+e.After));
                    preview+="\n"+string.Join("\n",deletionModels.Take(10).Select(m=>"削除: "+m.Name));
                    if(!app.Window.UI.ShowConfirmDialog("図「"+root.Name+"」をPlantUMLに合わせます。\n本文更新: "+edits.Count+"件 / メッセージ削除: "+unmapped.Length+"件 / 空のバー削除: "+emptyBars.Length+"件\n"+preview+"\n削除対象につながる関連も削除します。残す要素のID・配置を照合し、表示設定は元のデータを引き継ぎます。プロジェクトは自動保存しません。実行しますか？",SequenceExperiment.Title))throw new OperationCanceledException();
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
                    if(edits.Count>0 || removedIds.Length>0)
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
                    if(removedIds.Length>0)
                    {
                        using(project.SuspendModelVerification())foreach(var message in deletionModels)message.Delete();
                        foreach(string id in removedIds)
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
                    // ExportModelUnit rejects a project dirtied by this transaction.
                    // Verify the live SDK state here; never save or re-export to make verification pass.
                    if(editorAfter!=null)
                    {
                        if(!new HashSet<string>(editorAfter.Shapes().Select(n=>SequenceEditorDocument.Value(n,"Id")+":"+SequenceEditorDocument.Value(n,"ModelId")))
                            .SetEquals(fresh.Shapes.Select(n=>n.Id+":"+n.ModelId)))
                            throw new InvalidOperationException("E183: 削除後の図形IDが期待値と一致しません。");
                        foreach(string id in removedIds)
                        {
                            var deleted=project.GetModelById(id);
                            if((deleted!=null && !deleted.IsDeleted) || fresh.Shapes.Any(m=>m.ModelId==id))
                                throw new InvalidOperationException("E183: 削除対象のモデルまたは図形が再出現しました。");
                        }
                        detail.AppendLine("Deletion readback: live model/shape removal verified; serialized styles preserved in import payload, runtime style readback unavailable.");
                    }
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
                    SequenceExperiment.Summary="メッセージ本文の差分更新: "+edits.Count+"件\n図側の本文変更をPlantUMLに合わせた対象: "+merge.Conflicts+"件 / 本文一致: "+merge.AlreadyMatched+"件\nID・関連・配置の保持照合: 一致"+coverage+"\n追加・移動・実行区間の一般同期: 未対応\n対応表: 更新済み（前回分は .bak）\nプロジェクト保存: していません\n保存後のGit差分・Undo/Redo・再読込は別途確認してください。";
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
                    detail.AppendLine("Rollback API returned normally.");
                    var restored=root.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==diagram.Id);
                    rollbackRestored=Signature(root,restored)==original;
                    detail.AppendLine("Rollback live SDK snapshot restored="+rollbackRestored+"; serialized style readback not performed.");
                }
                catch(Exception failure){detail.AppendLine("Rollback or subsequent live readback failure: "+failure);}
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
    static void Observe(StringBuilder log,string id,string property,double actual,SequenceJson shape,ref int differences)
    {
        var value=shape[property];
        string serialized=value==null?"<omitted>":value.Raw;
        double number;
        bool equal=value!=null && double.TryParse(serialized,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out number)
            && !double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual-number)<=0.0000001;
        if(equal)return;
        differences++;
        if(differences<=20)log.AppendLine("Geometry representations differ: shape="+id+", property="+property+", SDK="+actual.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+", JSON="+serialized);
    }
    public static SequenceEditorDocument Read(IProject project,IInteraction root,ISequenceDiagram diagram,StringBuilder log,Action<string> capture=null)
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
            string exported=File.ReadAllText(path,new UTF8Encoding(false,true));
            var snapshot=SequenceEditorDocument.Read(exported,root.Id,diagram.Id);
            var shapes=snapshot.Shapes().ToDictionary(n=>SequenceEditorDocument.Value(n,"Id"));
            if(!new HashSet<string>(diagram.Shapes.Select(n=>n.Id+":"+n.ModelId)).SetEquals(shapes.Values.Select(n=>SequenceEditorDocument.Value(n,"Id")+":"+SequenceEditorDocument.Value(n,"ModelId"))))
                throw new InvalidOperationException("E180: 現在の図とエクスポートの図形IDが一致しません。");
            int differences=0;
            foreach(var message in diagram.Messages)
            {
                var shape=shapes[message.Id];
                Observe(log,message.Id,"SourceY",message.SourceY,shape,ref differences);
                Observe(log,message.Id,"TargetY",message.TargetY,shape,ref differences);
                Observe(log,message.Id,"SelfloopBendsX",message.SelfloopBendsX,shape,ref differences);
            }
            // SDK display coordinates and persisted values are separate representations.
            // Never rewrite one using the other. Signature verifies SDK values before/after;
            // the editor payload retains serialized values; post-update export is unavailable while dirty.
            foreach(var node in diagram.Shapes.OfType<ISequenceNodeShape>())
            {
                var shape=shapes[node.Id];
                if(shape["X"]!=null)Observe(log,node.Id,"X",node.LocationX,shape,ref differences);
                if(shape["Y"]!=null)Observe(log,node.Id,"Y",node.LocationY,shape,ref differences);
                if(shape["Width"]!=null)Observe(log,node.Id,"Width",node.Width,shape,ref differences);
                if(shape["Height"]!=null)Observe(log,node.Id,"Height",node.Height,shape,ref differences);
            }
            log.AppendLine("Editor snapshot: live identities verified, shapes="+shapes.Count+", cross-representation differences="+differences+" (first 20 logged; SDK preservation checked after mutation; serialized values retained in payload)");
            if(capture!=null)capture(exported);
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
    // The exporter prefixes note body lines with the opening indentation plus
    // one two-space level. Remove only that complete prefix, never common body
    // whitespace: intentional relative indentation and blank lines are content.
    private static string NoteBody(List<string> body,string opening)
    {
        string prefix=new string(opening.TakeWhile(c=>c==' ' || c=='\t').ToArray())+"  ";
        bool formatted=body.Any(s=>s.Length>0) && body.Where(s=>s.Length>0).All(s=>s.StartsWith(prefix,StringComparison.Ordinal));
        return string.Join("\n",body.Select(s=>formatted && s.Length>0?s.Substring(prefix.Length):s));
    }

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
            m = Regex.Match(s, @"^note\s+across(?:\s*:\s*(.*))?$");
            if(m.Success)
            {
                var n=new PumlNode{Kind="note",Operator="free",Line=line,Text=m.Groups[1].Value};
                if(!m.Groups[1].Success)
                {
                    var body=new List<string>();bool closed=false;
                    while(++i<lines.Length) {if(lines[i].Trim()=="end note") {closed=true;break;}body.Add(lines[i]);}
                    if(!closed)throw Error(line,"end noteが不足しています。");n.Text=NoteBody(body,lines[line-1]);
                }
                n.Text=n.Text.Replace("\\n","\n");lists.Peek().Add(n);continue;
            }
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
                    if (!closed) throw Error(line,"end "+n.Kind+"が不足しています。"); n.Text=n.Kind=="note"?NoteBody(body,lines[line-1]):string.Join("\n",body);
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
            int left=n.Targets.Count==0?x.Values.Min():n.Targets.Select(t=>x[t]).Min(),right=n.Targets.Count==0?x.Values.Max():n.Targets.Select(t=>x[t]).Max();
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

public static class SequenceStructureInput
{
    public static string ReconnectReceiver(SequencePayload seed,string relationId)
    {
        var document=SequenceJson.Parse(seed.Json);
        var link=document["Relations"].Items.Single(r=>r["Id"].StringValue()==relationId
            && r["MetamodelId"].StringValue()==SequencePayload.Prefix+"ReceiveMessage"
            && r["SourceId"].StringValue()==seed.Ids[5] && r["TargetId"].StringValue()==seed.Ids[6]);
        link.Properties["SourceId"]=SequenceJson.Parse(SequencePayload.Q(seed.Ids[4]));
        document["Entities"].Items.Clear();
        document["Relations"].Items.Clear();
        document["Relations"].Items.Add(link);
        return document.ToJsonString();
    }

    // Only for the generated two-execution probe; not a general diagram writer.
    public static string WithoutReceiver(SequencePayload seed,string schema)
    {
        var document=SequenceJson.Parse(SequenceDeltaInput.RestoreEditor(seed,schema));
        var bars=document["Editors"].Items.Single()["ExecutionSpecifications"].Items;
        if(bars.RemoveAll(sh=>sh["ModelId"].StringValue()==seed.Ids[5])!=1)
            throw new ArgumentException("Missing unique receiver execution shape");
        return document.ToJsonString();
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
    public int[] MissingLines(SequenceMessagePlan plan)
    { return plan.Targets.Where(t=>string.IsNullOrEmpty(MessageIds[t.Index])).Select(t=>t.Line).ToArray(); }
    public static string Hash(string text)
    { using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant(); }
    void Validate()
    {
        if(new[]{Project,Root,Editor,Fingerprint}.Any(string.IsNullOrEmpty) || Source==null || MessageIds==null || MessageIds.Any(id=>id==null) || MessageIds.Where(id=>id.Length>0).Distinct().Count()!=MessageIds.Count(id=>id.Length>0))
            throw new InvalidOperationException("E173: 対応表の必須項目またはIDが不正です。");
        if(Encoding.UTF8.GetByteCount(Source)>300000 || SequenceNameDiff.Messages(PumlPlan.ParseForMapping(Source)).Length!=MessageIds.Length)throw new InvalidOperationException("E173: 対応表の入力・件数が不正です。");
    }
    public string Serialize()
    {
        Validate();var doc=new System.Xml.XmlDocument();doc.XmlResolver=null;
        var root=doc.CreateElement("SequenceMap");doc.AppendChild(root);root.SetAttribute("version",MessageIds.Any(id=>id.Length==0)?"2":"1");
        string[] names={"Project","Root","Editor","Source","Fingerprint"};string[] values={Project,Root,Editor,Source,Fingerprint};
        for(int i=0;i<names.Length;i++){var element=doc.CreateElement(names[i]);element.InnerText=values[i];root.AppendChild(element);}
        var ids=doc.CreateElement("MessageIds");root.AppendChild(ids);
        foreach(string id in MessageIds){var element=doc.CreateElement("Id");element.InnerText=id;if(id.Length==0)element.SetAttribute("state","missing");ids.AppendChild(element);}
        root.SetAttribute("sha256",Hash(root.InnerXml));return doc.OuterXml;
    }
    public static SequenceMapFile Parse(string xml)
    {
        var settings=new System.Xml.XmlReaderSettings{DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2000000};
        var doc=new System.Xml.XmlDocument();doc.XmlResolver=null;doc.PreserveWhitespace=true;
        using(var reader=System.Xml.XmlReader.Create(new StringReader(xml),settings))doc.Load(reader);
        var root=doc.DocumentElement;
        if(root==null || root.Name!="SequenceMap" || (root.GetAttribute("version")!="1" && root.GetAttribute("version")!="2") || root.GetAttribute("sha256")!=Hash(root.InnerXml))throw new InvalidOperationException("E173: 対応表の形式または整合性が不正です。");
        Func<string,string> value=name=>{var nodes=root.SelectNodes(name);if(nodes.Count!=1)throw new InvalidOperationException("E173: 対応表の項目が不正です: "+name);return nodes[0].InnerText;};
        var map=new SequenceMapFile{Project=value("Project"),Root=value("Root"),Editor=value("Editor"),Source=value("Source"),Fingerprint=value("Fingerprint"),MessageIds=root.SelectNodes("MessageIds/Id").Cast<System.Xml.XmlNode>().Select(n=>n.InnerText).ToArray()};
        var entries=root.SelectNodes("MessageIds/Id").Cast<System.Xml.XmlElement>().ToArray();
        if(entries.Any(n=>n.InnerText.Length==0 ? root.GetAttribute("version")!="2" || n.GetAttribute("state")!="missing" : n.HasAttribute("state")))
            throw new InvalidOperationException("E173: 対応表の未対応行の形式が不正です。");
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
        foreach(string collection in new[]{"Messages","ExecutionSpecifications"})
        {
            var shapes=copy.Editor[collection];
            if(shapes==null)continue;
            if(shapes.Items==null)throw new InvalidOperationException("E180: 図形配列の形式が不正です: "+collection);
            for(int i=shapes.Items.Count-1;i>=0;i--)if(ids.Contains(Value(shapes.Items[i],"ModelId")))shapes.Items.RemoveAt(i);
        }
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

public static class SequenceActivationCleanup
{
    public static string[] Unused(IEnumerable<string> executions,IEnumerable<string> retainedMessagePorts)
    {
        var used=new HashSet<string>(retainedMessagePorts.Where(id=>!string.IsNullOrEmpty(id)));
        return executions.Where(id=>!used.Contains(id)).Distinct().OrderBy(id=>id,StringComparer.Ordinal).ToArray();
    }
}

// BEGIN GENERATED SequenceSync.cs
﻿// Pure semantic synchronization core. No SDK or filesystem dependencies.
public sealed class SequenceElement
{
    public string Id, Kind, Parent, Text = "";
    public int Order, Line;
    public Dictionary<string,string> Attributes = new Dictionary<string,string>(StringComparer.Ordinal);
    public Dictionary<string,string[]> Links = new Dictionary<string,string[]>(StringComparer.Ordinal);
    public SequenceElement Copy()
    {
        return new SequenceElement { Id=Id,Kind=Kind,Parent=Parent,Text=Text,Order=Order,Line=Line,
            Attributes=new Dictionary<string,string>(Attributes,StringComparer.Ordinal),
            Links=Links.ToDictionary(p=>p.Key,p=>p.Value.ToArray(),StringComparer.Ordinal) };
    }
}

public sealed class SequenceDocument
{
    public List<SequenceElement> Elements = new List<SequenceElement>();
    public bool HasTitle;
    public static readonly string[] Kinds = { "interaction","participant","message","execution",
        "fragment","operand","note","ref","create","destroy" };
    public void Validate()
    {
        if(Elements.Count>2000 || Elements.Any(e=>e==null || string.IsNullOrEmpty(e.Id) || !Kinds.Contains(e.Kind))
            || Elements.Select(e=>e.Id).Distinct().Count()!=Elements.Count)
            throw new InvalidOperationException("S201: 要素の型・ID・件数が不正です。");
        var index=Elements.ToDictionary(e=>e.Id);
        if(Elements.Count(e=>e.Kind=="interaction")!=1 || Elements.Any(e=>e.Kind=="interaction" ? e.Parent!=null : e.Parent==null || !index.ContainsKey(e.Parent)))
            throw new InvalidOperationException("S201: 相互作用の所有構造が不正です。");
        foreach(var e in Elements)
        {
            var path=new HashSet<string>();var at=e;
            while(at!=null) { if(!path.Add(at.Id))throw new InvalidOperationException("S201: 所有構造が循環しています。");at=at.Parent==null?null:index[at.Parent]; }
            if(e.Links.Values.SelectMany(v=>v).Any(id=>!index.ContainsKey(id)))
                throw new InvalidOperationException("S201: 接続先が図に存在しません。");
        }
    }
    public SequenceDocument Copy() { return new SequenceDocument{HasTitle=HasTitle,Elements=Elements.Select(e=>e.Copy()).ToList()}; }
    public string ToJson()
    {
        return PumlBuild.Json(PumlBuild.Obj("HasTitle",HasTitle,"Elements",Elements.Select(e=>PumlBuild.Obj(
            "Id",e.Id,"Kind",e.Kind,"Parent",e.Parent,"Text",e.Text,"Order",e.Order,"Line",e.Line,
            "Attributes",e.Attributes.ToDictionary(p=>p.Key,p=>(object)p.Value),
            "Links",e.Links.ToDictionary(p=>p.Key,p=>(object)p.Value))).ToArray()));
    }
    // IDs in this document are local parser keys, never Next Design model IDs.
    public static SequenceDocument Parse(string input)
    {
        var parsed=PumlPlan.ParseForMapping(input);
        var result=new SequenceDocument{HasTitle=Regex.IsMatch(input,@"(?m)^\s*title\s+")};
        result.Elements.Add(new SequenceElement{Id="root",Kind="interaction",Text=parsed.Title});
        var aliases=new Dictionary<string,string>();
        for(int i=0;i<parsed.Aliases.Count;i++)
        {
            string id="p"+i;aliases.Add(parsed.Aliases[i],id);
            result.Elements.Add(new SequenceElement{Id=id,Kind="participant",Parent="root",Order=i-1000,Text=parsed.Names[i]});
        }
        var active=new Dictionary<string,Stack<SequenceElement>>();int next=0;
        Action<IEnumerable<PumlNode>,string> visit=null;
        visit=(nodes,parent)=>{
            int order=0;SequenceElement previousEvent=null;
            var orderedNodes=nodes.ToArray();
            for(int nodeIndex=0;nodeIndex<orderedNodes.Length;nodeIndex++)
            {
                var n=orderedNodes[nodeIndex];
                if(n.Kind=="activate")
                {
                    var e=new SequenceElement{Id="e"+(next++),Kind="execution",Parent=parent,Order=order++,Line=n.Line};
                    e.Links["participant"]=new[]{aliases[n.Left]};e.Attributes["endParent"]=parent;
                    e.Attributes["start"]=n.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    result.Elements.Add(e);
                    if(!active.ContainsKey(n.Left))active[n.Left]=new Stack<SequenceElement>();
                    if(active[n.Left].Count>0)e.Links["outer"]=new[]{active[n.Left].Peek().Id};
                    active[n.Left].Push(e);
                    if(previousEvent!=null && previousEvent.Kind=="message" && previousEvent.Links["receiver"].SequenceEqual(new[]{aliases[n.Left]}))
                        previousEvent.Links["receiveExecution"]=new[]{e.Id};
                    continue;
                }
                if(n.Kind=="deactivate")
                {
                    if(!active.ContainsKey(n.Left) || active[n.Left].Count==0)
                        throw new InvalidOperationException("S202: "+n.Line+"行目のdeactivateに対応する開始がありません。");
                    var e=active[n.Left].Pop();e.Attributes["endParent"]=parent;previousEvent=null;
                    // Boundaries use neighbouring semantic elements below, not physical source lines.
                    e.Attributes["end"]=n.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);continue;
                }
                var item=new SequenceElement{Id="e"+(next++),Kind=SequenceNameDiff.IsMessage(n)?"message":n.Kind,
                    Parent=parent,Order=order++,Line=n.Line,Text=n.Text??""};
                if(item.Kind=="message")
                {
                    item.Attributes["sort"]=n.Kind;
                    item.Links["sender"]=n.Left=="["?new string[0]:new[]{aliases[n.Left]};
                    item.Links["receiver"]=n.Right=="]"?new string[0]:new[]{aliases[n.Right]};
                    foreach(var endpoint in new[]{new[]{"sendExecution",n.Left},new[]{"receiveExecution",n.Right}})
                        if(active.ContainsKey(endpoint[1]) && active[endpoint[1]].Count>0)item.Links[endpoint[0]]=new[]{active[endpoint[1]].Peek().Id};
                }
                // A self reply closing the innermost activation returns to its
                // caller. Keep the sender on the inner bar; do not pop until deactivate.
                if(n.Kind=="reply" && n.Left==n.Right && active.ContainsKey(n.Left) && active[n.Left].Count>1
                    && nodeIndex+1<orderedNodes.Length && orderedNodes[nodeIndex+1].Kind=="deactivate" && orderedNodes[nodeIndex+1].Left==n.Left)
                    item.Links["receiveExecution"]=new[]{active[n.Left].Skip(1).First().Id};
                // The exporter writes a destruction message as -> followed by
                // destroy of that receiver. Recover its kind, retaining the destroy event.
                if(item.Kind=="message" && n.Kind=="sync" && nodeIndex+1<orderedNodes.Length
                    && orderedNodes[nodeIndex+1].Kind=="destroy" && orderedNodes[nodeIndex+1].Left==n.Right)
                    item.Attributes["sort"]="destroy";
                if(n.Kind=="fragment") {item.Attributes["operator"]=n.Operator;if(n.Operator!="group")item.Text="";}
                if(n.Kind=="note" || n.Kind=="ref") { item.Links["targets"]=n.Targets.Select(t=>aliases[t]).ToArray();if(n.Kind=="note")item.Attributes["position"]=n.Operator; }
                if(n.Kind=="destroy" || n.Kind=="create")item.Links["participant"]=new[]{aliases[n.Left]};
                result.Elements.Add(item);visit(n.Children,item.Id);previousEvent=item;
            }
        };
        visit(parsed.Nodes,"root");
        foreach(var e in result.Elements.Where(e=>e.Kind=="execution"))
        {
            int start=int.Parse(e.Attributes["start"],System.Globalization.CultureInfo.InvariantCulture);
            int end=e.Attributes.ContainsKey("end")?int.Parse(e.Attributes["end"],System.Globalization.CultureInfo.InvariantCulture):int.MaxValue;
            var events=result.Elements.Where(n=>n.Kind!="participant" && n.Kind!="interaction" && n.Kind!="execution").OrderBy(n=>n.Line).ToArray();
            var preceding=events.LastOrDefault(n=>n.Line<start);var following=events.FirstOrDefault(n=>n.Line>end);
            e.Links["startAfter"]=preceding==null?new string[0]:new[]{preceding.Id};
            e.Links["endBefore"]=following==null?new string[0]:new[]{following.Id};
            string endParent=e.Attributes["endParent"];e.Links["endContainer"]=new[]{endParent};
            e.Attributes.Clear();
        }
        result.Validate();return result;
    }
}

public sealed class SequenceChange
{
    public string Action, Id, Kind;
    public int Line;
}

public sealed class SyncPlan
{
    public List<SequenceChange> Changes=new List<SequenceChange>();
    public SequenceDocument Expected;
    public Dictionary<string,string> Identities=new Dictionary<string,string>();
    public int Recreated;
    public bool IsEmpty { get { return Changes.Count==0; } }
    public string ToJson()
    {
        return PumlBuild.Json(PumlBuild.Obj("Recreated",Recreated,"Identities",Identities.ToDictionary(p=>p.Key,p=>(object)p.Value),
            "Changes",Changes.Select(c=>PumlBuild.Obj("Action",c.Action,"Id",c.Id,"Kind",c.Kind,"Line",c.Line)).ToArray()));
    }
    static string Text(string value) { return (value??"").Replace("\r\n","\n").Replace('\r','\n'); }
    static string Properties(SequenceElement e)
    { return SequencePayload.Q(e.Kind=="participant" || e.Kind=="interaction" || e.Kind=="ref" || e.Kind=="operand" ? SequenceLabels.Fold(e.Text) : Text(e.Text))+string.Join("",e.Attributes.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>SequencePayload.Q(p.Key)+SequencePayload.Q(p.Value))); }
    static string LinkKey(SequenceElement e,Dictionary<string,string> ids)
    {
        return string.Join("",e.Links.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>SequencePayload.Q(p.Key)+
            string.Join("",p.Value.Select(id=>SequencePayload.Q(ids!=null && ids.ContainsKey(id)?ids[id]:id)))));
    }
    static bool Comparable(SequenceElement a,SequenceElement b,Dictionary<string,string> ids)
    {
        // Execution boundary links are resolved after messages have been matched.
        if(a.Kind!=b.Kind)return false;
        if(a.Kind=="message")
            return new[]{"sender","receiver"}.All(k=>a.Links[k].Select(id=>ids.ContainsKey(id)?ids[id]:"?"+id).SequenceEqual(b.Links[k]));
        if(a.Kind=="note" || a.Kind=="ref" || a.Kind=="execution" || a.Kind=="destroy" || a.Kind=="create")
        {
            string key=a.Kind=="note" || a.Kind=="ref"?"targets":"participant";
            string[] left,right;
            if(!a.Links.TryGetValue(key,out left) || !b.Links.TryGetValue(key,out right))return false;
            return left.Select(id=>ids.ContainsKey(id)?ids[id]:"?"+id).SequenceEqual(right);
        }
        return true;
    }
    static Dictionary<string,string> Signatures(SequenceDocument doc)
    {
        var keys=new Dictionary<string,string>();
        Func<SequenceElement,string> get=null;
        get=e=>{
            string key;if(keys.TryGetValue(e.Id,out key))return key;
            key=e.Kind+Properties(e);
            if(e.Kind=="fragment" || e.Kind=="operand")
                key+="["+string.Join("",doc.Elements.Where(n=>n.Parent==e.Id && n.Kind!="execution").OrderBy(n=>n.Order).Select(n=>SequencePayload.Q(get(n))))+"]";
            keys.Add(e.Id,key);return key;
        };
        foreach(var e in doc.Elements)get(e);return keys;
    }
    static string BranchHeader(SequenceDocument doc,SequenceElement fragment)
    {
        return Properties(fragment)+"["+string.Join("",doc.Elements.Where(e=>e.Parent==fragment.Id && e.Kind=="operand")
            .OrderBy(e=>e.Order).Select(e=>SequencePayload.Q(Properties(e))))+"]";
    }
    public static SyncPlan Build(SequenceDocument current,SequenceDocument desired,Func<string> newId)
    {
        current.Validate();desired.Validate();
        var plan=new SyncPlan();var map=plan.Identities;var used=new HashSet<string>();
        var old=current.Elements.ToDictionary(e=>e.Id);
        var currentKeys=Signatures(current);var desiredKeys=Signatures(desired);
        Action<SequenceElement,SequenceElement> bind=(a,b)=>{map.Add(a.Id,b.Id);used.Add(b.Id);};
        bind(desired.Elements.Single(e=>e.Kind=="interaction"),current.Elements.Single(e=>e.Kind=="interaction"));
        // First establish unique exact anchors. Repeat after parents become known.
        bool progress=true;
        while(progress)
        {
            progress=false;
            foreach(var a in desired.Elements.Where(e=>e.Kind!="execution" && !map.ContainsKey(e.Id)).ToArray())
            {
                if(a.Parent==null || !map.ContainsKey(a.Parent))continue;
                // Resolve duplicate operators by their ordered branch headers,
                // independently of descendant edits. Require uniqueness both ways.
                if(a.Kind=="fragment")
                {
                    string header=BranchHeader(desired,a);
                    var peers=current.Elements.Where(b=>b.Kind=="fragment" && !used.Contains(b.Id) && b.Parent==map[a.Parent] && BranchHeader(current,b)==header).ToArray();
                    int inputs=desired.Elements.Count(b=>b.Kind=="fragment" && !map.ContainsKey(b.Id) && b.Parent==a.Parent && BranchHeader(desired,b)==header);
                    if(peers.Length==1 && inputs==1) {bind(a,peers[0]);progress=true;continue;}
                }
                if(a.Kind=="fragment" || a.Kind=="operand")
                {
                    var peers=current.Elements.Where(b=>!used.Contains(b.Id) && b.Parent==map[a.Parent] && b.Kind==a.Kind && Properties(b)==Properties(a)).ToArray();
                    int inputs=desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Parent==a.Parent && b.Kind==a.Kind && Properties(b)==Properties(a));
                    if(peers.Length==1 && inputs==1) {bind(a,peers[0]);progress=true;continue;}
                }
                var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && b.Parent==map[a.Parent] && Comparable(a,b,map) && desiredKeys[a.Id]==currentKeys[b.Id]).ToArray();
                int equivalent=desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Parent==a.Parent && b.Kind==a.Kind && desiredKeys[b.Id]==desiredKeys[a.Id]);
                if(candidates.Length==1 && equivalent==1) {bind(a,candidates[0]);progress=true;}
            }
        }
        Action align=()=>{
        // Match complete sibling sequences, including existing anchors, recursively.
        bool alignProgress=true;
        while(alignProgress)
        {
            int beforeCount=map.Count;
            foreach(var parent in desired.Elements.Where(e=>map.ContainsKey(e.Id)).ToArray())
            {
                var a=desired.Elements.Where(e=>e.Parent==parent.Id).OrderBy(e=>e.Order).ToArray();
                var b=current.Elements.Where(e=>e.Parent==map[parent.Id]).OrderBy(e=>e.Order).ToArray();
                Func<int,int,bool> equal=(i,j)=>map.ContainsKey(a[i].Id)?map[a[i].Id]==b[j].Id:
                    a[i].Kind!="execution" && !used.Contains(b[j].Id) && Comparable(a[i],b[j],map) && desiredKeys[a[i].Id]==currentKeys[b[j].Id];
                int[,] length=new int[a.Length+1,b.Length+1];
                for(int i=a.Length-1;i>=0;i--)for(int j=b.Length-1;j>=0;j--)
                    length[i,j]=equal(i,j)?1+length[i+1,j+1]:Math.Max(length[i+1,j],length[i,j+1]);
                int x=0,y=0;
                while(x<a.Length && y<b.Length)
                {
                    if(equal(x,y)) {if(!map.ContainsKey(a[x].Id))bind(a[x],b[y]);x++;y++;}
                    else if(length[x+1,y]>length[x,y+1])x++;else y++;
                }
            }
            alignProgress=map.Count>beforeCount;
        }
        };
        align();
        // Unique exact moves may cross containers; preserve IDs only when neither side is ambiguous.
        foreach(var a in desired.Elements.Where(e=>e.Kind!="execution" && !map.ContainsKey(e.Id)).ToArray())
        {
            var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && Comparable(a,b,map) && desiredKeys[a.Id]==currentKeys[b.Id]).ToArray();
            if(candidates.Length==1 && desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Kind==a.Kind && desiredKeys[b.Id]==desiredKeys[a.Id])==1)bind(a,candidates[0]);
        }
        // A single unmatched element of a kind in a corresponding container is an attribute edit.
        progress=true;
        while(progress)
        {
            progress=false;
            foreach(var a in desired.Elements.Where(e=>e.Kind!="execution" && !map.ContainsKey(e.Id)).ToArray())
            {
                if(a.Parent==null || !map.ContainsKey(a.Parent))continue;
                var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && b.Parent==map[a.Parent] && b.Kind==a.Kind).ToArray();
                if(candidates.Length==1 && desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Parent==a.Parent && b.Kind==a.Kind)==1)
                {bind(a,candidates[0]);progress=true;}
            }
        }
        align();
        Func<SequenceDocument,SequenceElement,bool,string> incident=(doc,execution,input)=>{
            var tokens=new List<string>();
            foreach(var message in doc.Elements.Where(e=>e.Kind=="message"))foreach(var role in new[]{"sendExecution","receiveExecution"})
            {
                string[] ids;if(!message.Links.TryGetValue(role,out ids) || !ids.Contains(execution.Id))continue;
                if(input && !map.ContainsKey(message.Id))return null;
                tokens.Add(role+":"+(input?map[message.Id]:message.Id));
            }
            return tokens.Count==0?null:string.Join("|",tokens.OrderBy(v=>v,StringComparer.Ordinal));
        };
        foreach(var a in desired.Elements.Where(e=>e.Kind=="execution" && !map.ContainsKey(e.Id)))
        {
            string key=incident(desired,a,true);if(key==null)continue;
            var candidates=current.Elements.Where(b=>b.Kind=="execution" && !used.Contains(b.Id) && Comparable(a,b,map) && incident(current,b,false)==key).ToArray();
            int inputs=desired.Elements.Count(b=>b.Kind=="execution" && !map.ContainsKey(b.Id) && incident(desired,b,true)==key);
            if(candidates.Length==1 && inputs==1)bind(a,candidates[0]);
        }
        // Unconnected bars still need a no-op identity: require all boundary references to resolve.
        progress=true;
        while(progress)
        {
            progress=false;
            foreach(var a in desired.Elements.Where(e=>e.Kind=="execution" && !map.ContainsKey(e.Id)).ToArray())
            {
                if(!map.ContainsKey(a.Parent) || a.Links.Values.SelectMany(v=>v).Any(id=>!map.ContainsKey(id)))continue;
                string key=LinkKey(a,map);
                var candidates=current.Elements.Where(b=>b.Kind=="execution" && !used.Contains(b.Id) && b.Parent==map[a.Parent] && Properties(a)==Properties(b) && LinkKey(b,null)==key).ToArray();
                int peers=desired.Elements.Count(b=>b.Kind=="execution" && !map.ContainsKey(b.Id) && b.Parent==a.Parent && b.Links.Values.SelectMany(v=>v).All(map.ContainsKey) && LinkKey(b,map)==key);
                if(candidates.Length==1 && peers==1) {bind(a,candidates[0]);progress=true;}
            }
        }
        foreach(var a in desired.Elements.Where(e=>!map.ContainsKey(e.Id)))
        {
            string id=newId();if(string.IsNullOrEmpty(id) || old.ContainsKey(id) || map.ContainsValue(id))throw new InvalidOperationException("S203: 新IDが重複しています。");
            map.Add(a.Id,id);
            if(current.Elements.Any(b=>!used.Contains(b.Id) && b.Kind==a.Kind))plan.Recreated++;
        }
        plan.Expected=desired.Copy();
        foreach(var e in plan.Expected.Elements)
        {
            string inputId=e.Id;e.Id=map[inputId];e.Parent=e.Parent==null?null:map[e.Parent];
            e.Links=e.Links.ToDictionary(p=>p.Key,p=>p.Value.Select(id=>map[id]).ToArray(),StringComparer.Ordinal);
        }
        foreach(var e in plan.Expected.Elements)
        {
            SequenceElement before;
            if(!old.TryGetValue(e.Id,out before)) {plan.Changes.Add(new SequenceChange{Action="add",Id=e.Id,Kind=e.Kind,Line=e.Line});continue;}
            if(e.Kind=="interaction" && !desired.HasTitle)e.Text=before.Text;
            if(Properties(e)!=Properties(before) || LinkKey(e,null)!=LinkKey(before,null))plan.Changes.Add(new SequenceChange{Action="update",Id=e.Id,Kind=e.Kind,Line=e.Line});
            // Absolute ordinal changes from insertions/deletions are not moves.
            var retained=new HashSet<string>(map.Values.Where(old.ContainsKey));
            var previous=plan.Expected.Elements.Where(n=>n.Kind!="execution" && n.Parent==e.Parent && n.Order<e.Order).OrderBy(n=>n.Order)
                .Select(n=>n.Id).Where(retained.Contains).ToArray();
            var oldPrevious=current.Elements.Where(n=>n.Kind!="execution" && n.Parent==before.Parent && n.Order<before.Order && retained.Contains(n.Id)).OrderBy(n=>n.Order).Select(n=>n.Id).ToArray();
            if(e.Parent!=before.Parent || (e.Kind!="execution" && !previous.SequenceEqual(oldPrevious)))plan.Changes.Add(new SequenceChange{Action="move",Id=e.Id,Kind=e.Kind,Line=e.Line});
        }
        foreach(var e in current.Elements.Where(e=>!map.ContainsValue(e.Id)))plan.Changes.Add(new SequenceChange{Action="delete",Id=e.Id,Kind=e.Kind});
        plan.Expected.Validate();return plan;
    }
}

// One dimensional insertion layout, used for both lane ordering and vertical event slots.
// Containers and connectors are consumers of these slots; this class never infers ownership.
public sealed class SequenceLayoutSlot
{
    public string Id;
    public double Size;
    public double? Existing;
}
public static class SequenceLocalLayout
{
    public static Dictionary<string,double> Arrange(IEnumerable<SequenceLayoutSlot> input,double start,double gap)
    {
        if(double.IsNaN(start) || double.IsInfinity(start) || double.IsNaN(gap) || double.IsInfinity(gap) || gap<0)
            throw new ArgumentException("Invalid layout bounds");
        var slots=input.ToArray();var result=new Dictionary<string,double>();double minimum=start;
        foreach(var slot in slots)
        {
            if(string.IsNullOrEmpty(slot.Id) || double.IsNaN(slot.Size) || double.IsInfinity(slot.Size) || slot.Size<0
                || (slot.Existing.HasValue && (double.IsNaN(slot.Existing.Value) || double.IsInfinity(slot.Existing.Value))))
                throw new ArgumentException("Invalid layout slot");
            double at=Math.Max(minimum,slot.Existing??minimum);result.Add(slot.Id,at);minimum=at+slot.Size+gap;
            if(double.IsInfinity(minimum))throw new ArgumentException("Layout overflow");
        }
        return result;
    }
}

public sealed class SequenceReferenceCandidate
{
    public string Id,Name,Path;
}
public static class SequenceReferenceResolver
{
    public static SequenceReferenceCandidate[] Find(string text,IEnumerable<SequenceReferenceCandidate> input)
    {
        var all=input.GroupBy(c=>c.Id).Select(g=>g.First()).ToArray();
        var qualified=all.Where(c=>!string.IsNullOrEmpty(c.Path) && c.Path==text).ToArray();
        var exact=qualified.Length>0?qualified:all.Where(c=>c.Name==text).ToArray();
        if(exact.Length>0)return exact.OrderBy(c=>c.Path,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal).ToArray();
        string folded=SequenceLabels.Fold(text);
        if(folded.Length==0)return new SequenceReferenceCandidate[0];
        var foldedPaths=all.Where(c=>!string.IsNullOrEmpty(c.Path) && SequenceLabels.Fold(c.Path)==folded).ToArray();
        return (foldedPaths.Length>0?foldedPaths:all.Where(c=>SequenceLabels.Fold(c.Name)==folded))
            .OrderBy(c=>c.Path,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal).ToArray();
    }
}

// Resolve transitive membership only after collecting every relationship and SDK observation.
public sealed class SequenceMembership
{
    public string Child, Parent, Evidence;
    public static void Resolve(SequenceDocument document,IEnumerable<SequenceMembership> observations,Action<string> log)
    {
        var index=document.Elements.ToDictionary(e=>e.Id);
        var root=document.Elements.Single(e=>e.Kind=="interaction").Id;
        var edges=observations.Concat(document.Elements.Where(e=>e.Parent!=null && e.Parent!=root)
            .Select(e=>new SequenceMembership{Child=e.Id,Parent=e.Parent,Evidence="shape container"})).ToArray();
        foreach(var edge in edges.OrderBy(e=>e.Child,StringComparer.Ordinal).ThenBy(e=>e.Parent,StringComparer.Ordinal))
        {
            log("Membership: child="+edge.Child+" parent="+edge.Parent+" source="+edge.Evidence);
            if(!index.ContainsKey(edge.Child) || !index.ContainsKey(edge.Parent))throw new InvalidOperationException("S210: 所属関連の端点が図にありません。");
        }
        var parents=edges.GroupBy(e=>e.Child).ToDictionary(g=>g.Key,g=>g.Select(e=>e.Parent).Distinct().ToArray());
        Func<string,string,bool> reaches=(start,target)=>{
            var visited=new HashSet<string>();var pending=new Stack<string>();pending.Push(start);
            while(pending.Count>0) {var at=pending.Pop();if(!visited.Add(at))continue;
                string[] next;if(!parents.TryGetValue(at,out next))continue;
                foreach(var id in next) {if(id==target)return true;pending.Push(id);}}
            return false;
        };
        foreach(var id in parents.Keys)if(reaches(id,id))throw new InvalidOperationException("S210: 所属関連が循環しています: "+id);
        var resolved=new Dictionary<string,string>();
        foreach(var pair in parents)
        {
            var nearest=pair.Value.Where(p=>!pair.Value.Any(other=>other!=p && reaches(other,p))).ToArray();
            if(nearest.Length!=1)throw new InvalidOperationException("S210: 包含関係で解決できない所属候補があります: "+pair.Key+" / "+string.Join(", ",nearest));
            resolved[pair.Key]=nearest[0];
            log("Membership resolved: child="+pair.Key+" parent="+nearest[0]+" candidates="+pair.Value.Length);
        }
        foreach(var pair in resolved)index[pair.Key].Parent=pair.Value;
    }
}

// Geometric containment supplements SDK membership (which includes ancestor operands).
public sealed class SequenceRegion
{
    public string Id, Fragment;
    public double X,Y,Width,Height;
    public static bool Contains(SequenceRegion outer,SequenceRegion inner)
    {
        const double eps=0.00001;
        if(new[]{outer.X,outer.Y,outer.Width,outer.Height,inner.X,inner.Y,inner.Width,inner.Height}
            .Any(v=>double.IsNaN(v)||double.IsInfinity(v)) || outer.Width<=0 || outer.Height<=0 || inner.Width<=0 || inner.Height<=0)return false;
        return outer.X<=inner.X+eps && outer.Y<=inner.Y+eps
            && outer.X+outer.Width>=inner.X+inner.Width-eps && outer.Y+outer.Height>=inner.Y+inner.Height-eps
            && (outer.Width>inner.Width+eps || outer.Height>inner.Height+eps);
    }
    public static IEnumerable<SequenceMembership> Nesting(IEnumerable<SequenceRegion> operands,IEnumerable<SequenceRegion> fragments)
    {
        foreach(var fragment in fragments)foreach(var operand in operands)
            if(operand.Fragment!=fragment.Id && Contains(operand,fragment))
                yield return new SequenceMembership{Child=fragment.Id,Parent=operand.Id,Evidence="diagram rectangle containment"};
    }
}

// Screenshot report: fixed vocabulary and counts only; never include design labels or IDs.
public static class SequenceAudit
{
    static string Fold(string text) { return System.Text.RegularExpressions.Regex.Replace(text??"",@"\s+"," ").Trim(); }
    static bool EqualLinks(SequenceElement a,SequenceElement b,string role)
    {
        string[] x,y;if(!a.Links.TryGetValue(role,out x))x=new string[0];if(!b.Links.TryGetValue(role,out y))y=new string[0];
        return x.SequenceEqual(y);
    }
    public static string Summary(SyncPlan plan,int limitations)
    {
        var lines=new List<string>{"読取り完了・差分候補（図への反映なし）","種類: 追加 / 削除 / 更新 / 移動"};
        foreach(string kind in SequenceDocument.Kinds)
        {
            var changes=plan.Changes.Where(c=>c.Kind==kind).ToArray();if(changes.Length==0)continue;
            lines.Add(kind+": "+string.Join(" / ",new[]{"add","delete","update","move"}.Select(a=>changes.Count(c=>c.Action==a).ToString())));
        }
        if(plan.IsEmpty)lines.Add("差分候補: 0件");
        lines.Add("再作成候補: "+plan.Recreated+" / 要照合: "+limitations);
        lines.Add("未編集で出力した入力の期待値: すべて0件");
        lines.Add("この画面と「診断表示」を撮影してください。");return string.Join("\n",lines);
    }
    public static string Reasons(SequenceDocument current,SequenceDocument desired,SyncPlan plan)
    {
        var counts=new Dictionary<string,int>();Action<string> hit=k=>{if(!counts.ContainsKey(k))counts[k]=0;counts[k]++;};
        var old=current.Elements.ToDictionary(e=>e.Id);
        foreach(var e in plan.Expected.Elements.Where(e=>old.ContainsKey(e.Id)))
        {
            var b=old[e.Id];
            if(e.Text!=b.Text)hit(Fold(e.Text)==Fold(b.Text)?"本文: 改行・空白のみ":"本文: 空白以外も相違");
            if(e.Parent!=b.Parent)hit("所属先の相違");
            if(new[]{"sender","receiver"}.Any(k=>!EqualLinks(e,b,k)))hit("メッセージの送受信先");
            if(new[]{"sendExecution","receiveExecution"}.Any(k=>!EqualLinks(e,b,k)))hit("メッセージの接続実行区間");
            foreach(var boundary in new[]{"startAfter","endBefore","endContainer","outer"})
                if(!EqualLinks(e,b,boundary))hit(boundary=="startAfter"?"実行区間: 開始位置":boundary=="endBefore"?"実行区間: 終了位置":boundary=="endContainer"?"実行区間: 終了分岐":"実行区間: 外側区間");
            if(!EqualLinks(e,b,"participant"))hit("実行区間・生成破棄の参加者");
            if(new[]{"targets","anchors"}.Any(k=>!EqualLinks(e,b,k)))hit("Note・refの接続先");
            foreach(var key in e.Attributes.Keys.Union(b.Attributes.Keys))
            {
                string aValue,bValue;e.Attributes.TryGetValue(key,out aValue);b.Attributes.TryGetValue(key,out bValue);
                if(aValue!=bValue)hit(key=="sort"?"メッセージ種別":key=="operator"?"フラグメント種別":key=="reference"?"ref参照先":key=="position"?"Note配置指定":"その他属性");
            }
        }
        var lines=new List<string>{"差分理由（対応付けできた要素・重複計上あり）"};
        lines.AddRange(counts.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+": "+p.Value));
        if(counts.Count==0)lines.Add("対応付け済み要素の属性・接続差: 0件");
        var normalized=current.Copy();var input=desired.Copy();
        foreach(var e in normalized.Elements.Concat(input.Elements).Where(e=>e.Kind=="participant"))e.Text=Fold(e.Text);
        int serial=0;var occupied=new HashSet<string>(current.Elements.Select(e=>e.Id));
        var simulated=SequenceNotePolicy.Build(normalized,input,()=>{string id;do{id="audit-"+(serial++);}while(!occupied.Add(id));return id;});
        lines.Add("参加者の改行・空白を揃えた比較実験（反映なし）");
        lines.Add("参加者 追加+削除: "+plan.Changes.Count(c=>c.Kind=="participant" && (c.Action=="add" || c.Action=="delete"))+" → "+simulated.Changes.Count(c=>c.Kind=="participant" && (c.Action=="add" || c.Action=="delete")));
        lines.Add("全種類 再作成候補: "+plan.Recreated+" → "+simulated.Recreated);
        lines.Add("全種類 差分操作数: "+plan.Changes.Count+" → "+simulated.Changes.Count);
        lines.Add("実行区間数 図/入力: "+current.Elements.Count(e=>e.Kind=="execution")+" / "+desired.Elements.Count(e=>e.Kind=="execution"));
        foreach(var role in new[]{"sendExecution","receiveExecution"})
            lines.Add((role=="sendExecution"?"送信":"受信")+"実行区間への接続数 図/入力: "+current.Elements.Count(e=>e.Kind=="message" && e.Links.ContainsKey(role))+" / "+desired.Elements.Count(e=>e.Kind=="message" && e.Links.ContainsKey(role)));
        lines.Add("本文の空白以外は一律に正規化していません。");
        lines.Add("本文・モデルID・パスはこの画面には表示しません。");
        return string.Join("\n",lines)+"\f"+Residuals(current,desired,plan);
    }
    static string SafeOperator(SequenceElement e)
    {
        string value;if(!e.Attributes.TryGetValue("operator",out value))return "-";
        return new[]{"alt","opt","loop","par","break","critical","group"}.Contains(value)?value:"その他";
    }
    static string Residuals(SequenceDocument current,SequenceDocument desired,SyncPlan plan)
    {
        var before=current.Elements.ToDictionary(e=>e.Id);var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        var tokens=new Dictionary<string,string>();int serial=0;
        foreach(var e in current.Elements.Concat(plan.Expected.Elements))if(!tokens.ContainsKey(e.Id))tokens[e.Id]=e.Kind+"#"+(++serial);
        Func<string,string> token=id=>tokens.ContainsKey(id)?tokens[id]:"?";
        var rows=new List<string>{"残差の内訳（#番号は今回だけの匿名番号）"};
        rows.Add("差分操作: "+plan.Changes.Count+"件");
        foreach(var change in plan.Changes)
            rows.Add("L"+change.Line+" "+token(change.Id)+" "+change.Action);
        foreach(var c in plan.Changes.Where(c=>c.Action=="update" || c.Action=="move"))
        {
            var a=after[c.Id];var b=before[c.Id];
            if(a.Parent!=b.Parent)rows.Add("L"+c.Line+" "+a.Kind+" 所属: "+token(b.Parent)+" → "+token(a.Parent));
            if(c.Action=="update" && a.Kind=="ref")
            {
                string x,y;b.Attributes.TryGetValue("reference",out x);a.Attributes.TryGetValue("reference",out y);
                if(x!=y)rows.Add("L"+c.Line+" ref参照先 図="+(string.IsNullOrEmpty(x)?"なし":"あり")+" 入力="+(string.IsNullOrEmpty(y)?"未解決":"解決済み")+" 一致=False");
            }
            if(c.Action=="update")foreach(var role in new[]{"sendExecution","receiveExecution","startAfter","endBefore","endContainer","outer"})
            {
                if(EqualLinks(a,b,role))continue;
                string[] x,y;b.Links.TryGetValue(role,out x);a.Links.TryGetValue(role,out y);
                rows.Add("L"+c.Line+" "+role+": "+string.Join(",",(x??new string[0]).Select(token))+" → "+string.Join(",",(y??new string[0]).Select(token)));
            }
        }
        foreach(var a in plan.Expected.Elements.Where(e=>(e.Kind=="note" || e.Kind=="ref") && !before.ContainsKey(e.Id)))
        {
            var candidates=current.Elements.Where(b=>b.Kind==a.Kind && !after.ContainsKey(b.Id)).ToArray();
            var text=candidates.Where(b=>Fold(b.Text)==Fold(a.Text)).ToArray();
            rows.Add("L"+a.Line+" "+a.Kind+" 未対応: 同種="+candidates.Length+" 空白正規化本文一致="+text.Length+" 接続先一致="+text.Count(b=>EqualLinks(a,b,"targets")));
            foreach(var b in text.Take(2))rows.Add("  接続数 図/入力="+(b.Links.ContainsKey("targets")?b.Links["targets"].Length:0)+"/"+(a.Links.ContainsKey("targets")?a.Links["targets"].Length:0)+" 所属一致="+(a.Parent==b.Parent));
        }
        // Show upstream container mismatches before diagnosing their dependent links.
        foreach(var a in plan.Expected.Elements.Where(e=>(e.Kind=="fragment" || e.Kind=="operand") && !before.ContainsKey(e.Id)))
        {
            var candidates=current.Elements.Where(b=>b.Kind==a.Kind && !after.ContainsKey(b.Id)).ToArray();
            rows.Add("L"+a.Line+" "+token(a.Id)+" 未対応 所属="+token(a.Parent)+" 子="+after.Values.Count(e=>e.Parent==a.Id)+" 演算子="+SafeOperator(a));
            rows.Add("  図側候補="+candidates.Length+" 本文一致="+candidates.Count(b=>Fold(b.Text)==Fold(a.Text))+" 所属一致="+candidates.Count(b=>b.Parent==a.Parent));
            foreach(var b in candidates.Take(4))
                rows.Add("  "+token(b.Id)+" 所属="+token(b.Parent)+" 子="+before.Values.Count(e=>e.Parent==b.Id)+" 演算子="+SafeOperator(b)+" 本文一致="+(Fold(b.Text)==Fold(a.Text)));
            if(candidates.Length>4)rows.Add("  残り候補="+(candidates.Length-4));
        }
        rows=rows.Distinct().ToList();
        if(plan.Changes.Count==0)rows.Add("所属・境界・未対応Note/refの残差なし");
        // Each page remains photographable even with a large diagram.
        return string.Join("\f",Enumerable.Range(0,(rows.Count+13)/14).Select(i=>string.Join("\n",rows.Skip(i*14).Take(14))));
    }
}

public static class SequenceLabels
{
    // Exporter folds whitespace in participant labels and the diagram title.
    public static string Fold(string value) { return Regex.Replace(value??"",@"\s+"," ").Trim(); }
}

// PlantUML note placement does not instruct creation/deletion of Next Design anchors.
public static class SequenceNotePolicy
{
    public static SyncPlan Build(SequenceDocument current,SequenceDocument desired,Func<string> newId)
    {
        var before=current.Copy();var input=desired.Copy();
        foreach(var e in before.Elements.Concat(input.Elements).Where(e=>e.Kind=="note"))
        {
            e.Links["targets"]=new string[0];e.Links.Remove("anchors");e.Attributes["position"]="free";
        }
        var plan=SyncPlan.Build(before,input,newId);
        var originals=current.Elements.Where(e=>e.Kind=="note").ToDictionary(e=>e.Id);
        foreach(var e in plan.Expected.Elements.Where(e=>e.Kind=="note"))
        {
            SequenceElement original;if(!originals.TryGetValue(e.Id,out original))continue;
            foreach(var role in new[]{"targets","anchors"})
            {string[] values;if(original.Links.TryGetValue(role,out values))e.Links[role]=values.ToArray();else e.Links.Remove(role);}
            string position;if(original.Attributes.TryGetValue("position",out position))e.Attributes["position"]=position;else e.Attributes.Remove("position");
        }
        plan.Expected.Validate();return plan;
    }
}

// Feasibility only. A candidate is not permission to write the live diagram.
public sealed class SequenceStructurePreflight
{
    public List<string> Reasons=new List<string>();
    public List<string> ReconnectMessages=new List<string>();
    public List<string> DeleteExecutions=new List<string>();
    public bool Candidate { get { return Reasons.Count==0 && (ReconnectMessages.Count+DeleteExecutions.Count)>0; } }
    public bool CanCommit(bool reconnect)
    { return Candidate && DeleteExecutions.Count>0 && (reconnect?ReconnectMessages.Count>0:ReconnectMessages.Count==0); }
    static string[] Link(SequenceElement e,string role)
    { string[] ids;return e.Links.TryGetValue(role,out ids)?ids:new string[0]; }
    static string Comparable(SequenceElement e)
    {
        var copy=e.Copy();copy.Links.Remove("receiveExecution");copy.Line=0;copy.Order=0;
        return new SequenceDocument{Elements=new List<SequenceElement>{copy}}.ToJson();
    }
    public static SequenceStructurePreflight Check(SequenceDocument current,SyncPlan plan)
    {
        current.Validate();plan.Expected.Validate();
        var result=new SequenceStructurePreflight();
        var before=current.Elements.ToDictionary(e=>e.Id);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        foreach(var change in plan.Changes)
        {
            string row="L"+change.Line+" ";
            SequenceElement old,next;
            if(change.Action=="delete" && change.Kind=="execution" && before.TryGetValue(change.Id,out old) && !after.ContainsKey(change.Id))
            {
                // Links include note anchors, nesting and boundary references, not just ports.
                if(plan.Expected.Elements.Any(e=>e.Parent==change.Id || e.Links.Values.SelectMany(v=>v).Contains(change.Id)))
                    result.Reasons.Add(row+"実行区間への参照が残るため削除できません。");
                else result.DeleteExecutions.Add(change.Id);
                continue;
            }
            if(change.Action!="update" || change.Kind!="message" || !before.TryGetValue(change.Id,out old) || !after.TryGetValue(change.Id,out next))
            { result.Reasons.Add(row+change.Kind+" "+change.Action+"は今回の構造更新対象外です。");continue; }
            var oldPorts=Link(old,"receiveExecution");var newPorts=Link(next,"receiveExecution");
            if(newPorts.Length==0)
            { result.Reasons.Add(row+"受信実行区間なしの書込み表現が未確定です。ライフライン直結や区間の自動補完は行いません。");continue; }
            SequenceElement port;
            if(oldPorts.Length!=1 || newPorts.Length!=1 || !before.ContainsKey(newPorts[0]) || !after.TryGetValue(newPorts[0],out port) || port.Kind!="execution")
            { result.Reasons.Add(row+"接続先は既存の実行区間1件である必要があります。");continue; }
            if(!Link(port,"participant").SequenceEqual(Link(next,"receiver")))
            { result.Reasons.Add(row+"受信参加者と接続先実行区間の所属が一致しません。");continue; }
            if(oldPorts.SequenceEqual(newPorts) || Comparable(old)!=Comparable(next))
            { result.Reasons.Add(row+"受信実行区間以外の変更を含むため対象外です。");continue; }
            result.ReconnectMessages.Add(change.Id);
        }
        // Keep candidates for diagnostics, but never permit applying a supported subset.
        return result;
    }
    public string Summary()
    {
        return "構造更新の事前判定（図への反映なし）\n受信接続変更候補: "+ReconnectMessages.Count+" / 実行区間削除候補: "+DeleteExecutions.Count
            +"\n"+(Reasons.Count>0?"全体を停止: "+Reasons.Count+"件の未対応条件":Candidate?"限定範囲の候補あり。既存図での適用・保持検証は未実施です。":"対象の変更なし")
            +"\n"+string.Join("\n",Reasons.Distinct());
    }
    public string ToJson()
    { return PumlBuild.Json(PumlBuild.Obj("Candidate",Candidate,"ReconnectMessages",ReconnectMessages.ToArray(),"DeleteExecutions",DeleteExecutions.ToArray(),"Reasons",Reasons.ToArray())); }
}

// Prepared files are diagnostic artifacts; they are never imported by this command.
public sealed class SequenceStructurePreparation
{
    public string ReconnectJson, EditorAfterDeleteJson;
    public string[] DeleteIds;
    static string V(SequenceJson n,string key) { return SequenceEditorDocument.Value(n,key); }
    static SequenceJson[] Array(SequenceJson n,string key)
    {
        if(n[key]==null || n[key].Items==null)throw new InvalidOperationException("S220: 退避データの配列が不足しています。");
        return n[key].Items.ToArray();
    }
    static void Require(bool condition,string reason)
    { if(!condition)throw new InvalidOperationException("S220: "+reason); }
    public static SequenceStructurePreparation Build(string exported,string editorId,SequenceDocument current,SyncPlan plan)
    {
        var gate=SequenceStructurePreflight.Check(current,plan);
        Require(gate.Candidate,"未対応の変更があるか、構造更新の候補がありません。");
        string root=current.Elements.Single(e=>e.Kind=="interaction").Id;
        var source=SequenceJson.Parse(exported);
        var editor=SequenceEditorDocument.Read(exported,root,editorId);
        var entities=Array(source,"Entities");var relations=Array(source,"Relations");
        Require(entities.All(e=>!string.IsNullOrEmpty(V(e,"Id"))) && entities.Select(e=>V(e,"Id")).Distinct().Count()==entities.Length,"モデルIDが不足または重複しています。");
        Require(relations.All(r=>!string.IsNullOrEmpty(V(r,"Id")) && V(r,"SourceId")!=null && V(r,"TargetId")!=null)
            && relations.Select(r=>V(r,"Id")).Distinct().Count()==relations.Length,"関連IDまたは関連端が不正です。");
        var byId=entities.ToDictionary(e=>V(e,"Id"));
        Require(byId.ContainsKey(root) && V(byId[root],"EntityType")=="Interaction","退避データに対象の相互作用がありません。");
        Require(Array(source,"Editors").Count(e=>V(e,"ModelId")==root)==1,"同じモデルに複数の図があります。");
        var before=current.Elements.ToDictionary(e=>e.Id);var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        var changed=new List<SequenceJson>();
        var changedIds=new HashSet<string>();
        Func<string,string,string,SequenceJson> find=(type,from,to)=>{
            var matches=relations.Where(r=>V(r,"MetamodelId")==SequencePayload.Prefix+type && V(r,"SourceId")==from && V(r,"TargetId")==to).ToArray();
            Require(matches.Length==1,"必要な構造関連を一意に取得できません。");return matches[0];
        };
        Action<string,string> checkPort=(id,participant)=>{
            Require(byId.ContainsKey(id) && V(byId[id],"EntityType")=="ExecutionSpecification","接続先は退避データ内の実行区間である必要があります。");
            find("___Interaction_ExecutionSpecification",root,id);
            find("OwnedExecutionSpecification",participant,id);
            Require(relations.Count(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"OwnedExecutionSpecification" && V(r,"TargetId")==id)==1,"実行区間の所属が一意ではありません。");
        };
        foreach(string id in gate.ReconnectMessages)
        {
            var a=before[id];var b=after[id];
            Require(byId.ContainsKey(id) && V(byId[id],"EntityType")=="Message","変更対象のメッセージが退避データにありません。");
            find("___Interaction_Message",root,id);
            string oldPort=a.Links["receiveExecution"].Single(),newPort=b.Links["receiveExecution"].Single();
            checkPort(oldPort,a.Links["receiver"].Single());checkPort(newPort,b.Links["receiver"].Single());
            var link=find("ReceiveMessage",oldPort,id);
            Require(relations.Count(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"ReceiveMessage" && V(r,"TargetId")==id)==1,"受信接続が一意ではありません。");
            var copy=SequenceJson.Parse(link.ToJsonString());
            copy.Properties["SourceId"]=SequenceJson.Parse(SequencePayload.Q(newPort));
            changed.Add(copy);changedIds.Add(V(link,"Id"));
        }
        foreach(string id in gate.DeleteExecutions)
        {
            checkPort(id,before[id].Links["participant"].Single());
            foreach(var relation in relations.Where(r=>V(r,"SourceId")==id || V(r,"TargetId")==id))
            {
                if(changedIds.Contains(V(relation,"Id")))continue;
                bool owned=V(relation,"TargetId")==id && (V(relation,"MetamodelId")==SequencePayload.Prefix+"___Interaction_ExecutionSpecification"
                    || V(relation,"MetamodelId")==SequencePayload.Prefix+"OwnedExecutionSpecification");
                Require(owned,"削除する実行区間に未対応の関連が残っています。");
            }
        }
        var affected=new HashSet<string>(gate.DeleteExecutions.Concat(gate.ReconnectMessages));
        var shapes=editor.Shapes();
        foreach(string id in affected)Require(shapes.Count(sh=>V(sh,"ModelId")==id)==1,"変更対象の図形を一意に取得できません。");
        foreach(var other in Array(source,"Editors").Where(e=>V(e,"Id")!=editorId))
            Require(!Mentions(other,affected),"変更対象を別のエディタも参照しています。");
        var patch=SequenceJson.Parse(editor.ImportJson());
        patch["Relations"].Items.AddRange(changed);
        return new SequenceStructurePreparation{ReconnectJson=patch.ToJsonString(),
            EditorAfterDeleteJson=editor.Without(gate.DeleteExecutions).ImportJson(),DeleteIds=gate.DeleteExecutions.ToArray()};
    }
    static bool Mentions(SequenceJson node,HashSet<string> ids)
    {
        if(node.Properties!=null)return node.Properties.Any(p=>p.Key=="ModelId" && ids.Contains(p.Value.StringValue()) || Mentions(p.Value,ids));
        return node.Items!=null && node.Items.Any(n=>Mentions(n,ids));
    }
}

// There is deliberately no commit callback. The native owner starts one transaction.
public sealed class SequenceRollbackTrial
{
    public bool Applied, RollbackReturned, Restored;
    public Exception ApplyError, RollbackError, VerifyError;
    public void Run(Action apply,Action rollback,Action verifyRestored)
    {
        try {apply();Applied=true;}
        catch(Exception ex){ApplyError=ex;}
        finally
        {
            try {rollback();RollbackReturned=true;}
            catch(Exception ex){RollbackError=ex;}
            if(RollbackReturned)
            {
                try {verifyRestored();Restored=true;}
                catch(Exception ex){VerifyError=ex;}
            }
        }
    }
}

// Commit only after verified application; failures get one rollback attempt.
public sealed class SequenceCommitTrial
{
    public bool Applied, Committed, RollbackReturned, Restored;
    public Exception ApplyError, CommitError, RollbackError, VerifyError;
    public void Run(Action apply,Action commit,Action rollback,Action verifyRestored)
    {
        try {apply();Applied=true;} catch(Exception ex){ApplyError=ex;}
        if(Applied) {try {commit();Committed=true;} catch(Exception ex){CommitError=ex;}}
        if(Committed)return;
        try {rollback();RollbackReturned=true;} catch(Exception ex){RollbackError=ex;}
        if(RollbackReturned) {try {verifyRestored();Restored=true;} catch(Exception ex){VerifyError=ex;}}
    }
}

public sealed class SequenceTrialState
{
    public Dictionary<string,string> Models=new Dictionary<string,string>(), Shapes=new Dictionary<string,string>(), ShapeModels=new Dictionary<string,string>();
    // Relations: source, target, source index, target index. Ports: send port, receive port, sender, receiver, kind.
    public Dictionary<string,string[]> Relations=new Dictionary<string,string[]>(), Ports=new Dictionary<string,string[]>();
    public string Signature()
    {
        return PumlBuild.Json(new object[]{Models.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key,p.Value}).ToArray(),
            Shapes.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key,p.Value}).ToArray(),
            ShapeModels.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key,p.Value}).ToArray(),
            Relations.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key}.Concat(p.Value).ToArray()).ToArray(),
            Ports.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key}.Concat(p.Value).ToArray()).ToArray()});
    }
    static int Differences(Dictionary<string,string> a,Dictionary<string,string> b)
    {return a.Keys.Union(b.Keys).Count(k=>!a.ContainsKey(k) || !b.ContainsKey(k) || a[k]!=b[k]);}
    public string DifferenceCounts(SequenceTrialState actual)
    {
        return "モデル="+Differences(Models,actual.Models)+" 関連="+Differences(Relations.ToDictionary(p=>p.Key,p=>PumlBuild.Json(p.Value)),actual.Relations.ToDictionary(p=>p.Key,p=>PumlBuild.Json(p.Value)))
            +" 図形="+Differences(Shapes,actual.Shapes)+" 図形所属="+Differences(ShapeModels,actual.ShapeModels)
            +" 送受信="+Differences(Ports.ToDictionary(p=>p.Key,p=>PumlBuild.Json(p.Value)),actual.Ports.ToDictionary(p=>p.Key,p=>PumlBuild.Json(p.Value)));
    }
    public SequenceTrialState Expected(SequenceStructurePreparation prepared,SyncPlan plan,bool delete)
    {
        var result=new SequenceTrialState{Models=new Dictionary<string,string>(Models),Shapes=new Dictionary<string,string>(Shapes),ShapeModels=new Dictionary<string,string>(ShapeModels),
            Relations=Relations.ToDictionary(p=>p.Key,p=>p.Value.ToArray()),Ports=Ports.ToDictionary(p=>p.Key,p=>p.Value.ToArray())};
        var patch=SequenceJson.Parse(prepared.ReconnectJson);
        foreach(var r in patch["Relations"].Items)
        {
            string id=r["Id"].StringValue(),source=r["SourceId"].StringValue(),target=r["TargetId"].StringValue();
            if(!result.Relations.ContainsKey(id) || result.Relations[id][1]!=target || !result.Ports.ContainsKey(target))throw new InvalidOperationException("S230: 変更前の受信関連が一致しません。");
            // The patch changes SourceId only. Export can omit indices; retain live SDK ordering.
            var previous=result.Relations[id];
            result.Relations[id]=new[]{source,target,previous[2],previous[3]};
            result.Ports[target][1]=source;
            result.Ports[target][3]=plan.Expected.Elements.Single(e=>e.Id==target).Links["receiver"].Single();
        }
        if(delete)
        {
            var removed=new HashSet<string>(prepared.DeleteIds);
            foreach(string id in removed)result.Models.Remove(id);
            foreach(string id in result.Relations.Where(p=>removed.Contains(p.Value[0]) || removed.Contains(p.Value[1])).Select(p=>p.Key).ToArray())result.Relations.Remove(id);
            foreach(string id in result.ShapeModels.Where(p=>removed.Contains(p.Value)).Select(p=>p.Key).ToArray()){result.Shapes.Remove(id);result.ShapeModels.Remove(id);}
            if(result.Ports.Values.Any(p=>removed.Contains(p[0]) || removed.Contains(p[1])))throw new InvalidOperationException("S230: 削除区間への接続が残っています。");
        }
        return result;
    }
}
// END GENERATED SequenceSync.cs
