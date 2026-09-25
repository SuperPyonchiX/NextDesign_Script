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

public static class SequenceExperiment
{
    public const string Title = "シーケンス生成実験 / 0.11.8";
    public static string Summary = "新しい図は「PlantUML取込」、既存の図は「差分を検証」→「PlantUMLを反映」を使ってください。";
    public static string Details = "まだ実行していません。";
    // Set by the scenario batch: the input to import, no dialogs, and the new diagram's id.
    public static bool BatchMode;
    public static string BatchInput, LastRoot;
    public static void Show(IApplication app) { if(!BatchMode)app.Window.UI.ShowInformationDialog(Summary, Title); }

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
                var path = BatchInput ?? app.Window.UI.ShowOpenFileDialog("取り込むPlantUMLファイル", "PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
                if (string.IsNullOrEmpty(path)) return;
                if (new FileInfo(path).Length > 300000) throw new InvalidOperationException("E120: 入力は300KB以下にしてください。");
                pumlText = File.ReadAllText(path, new UTF8Encoding(false, true));
                detail.AppendLine("PlantUML file: " + path);
                try { plan = PumlPlan.Parse(pumlText); SequenceDocument.Parse(pumlText); }
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
            if (!BatchMode && !app.Window.UI.ShowConfirmDialog(structureProbe ? "コピーのプロジェクトで実行してください。\n一時図の受信先変更と実行区間の削除・再作成を検証し、最後にすべて取り消します。\n削除中だけSDKの編集可否検査を一時停止します。自動保存はしません。" : replaceExisting ? "コピーのプロジェクトで実行してください。\n現在の図をPlantUMLの内容で置き換えます。図自体のIDは維持します。\n配下の要素と手作業の配置は作り直します。子要素と外部モデルとの関連は引き継ぎません。自動保存はしません。" : deltaProbe ? "コピーのプロジェクトで実行してください。\n一時図で名前変更・メッセージ1件の差分追加と削除を検証し、最後に取り消します。\n削除中だけSDKの編集可否検査を一時停止する実験です。既存図は更新せず、自動保存もしません。" : updateProbe ? "コピーのプロジェクトで実行してください。\n一時図を作り、同じIDでメッセージ名を再取り込みします。\n最後に一時図を含む操作を取り消します。既存図を更新する検証ではありません。\n自動保存はしません。" : "実プロジェクトのコピーを開いていますか？\n新しい検証用シーケンス図を同じ親に追加する実験です。\n既存図の内容は入力にコピーしません。自動保存しません。\n失敗時はトランザクションの取消を試みますが、実機での復元動作は未確認です。", Title)) return;
            var folder = BatchMode?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NextDesign.SequenceSync","batch")
                :app.Window.UI.ShowSelectFolderDialog("実験結果の保存先");
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
            IClass[] sources;
            if (message != null && sample.Frame != null)
            {
                IModel[] samples = { sample, sample.Frame, message.Sender, message.Receiver,
                    (IModel)message.SendPort, (IModel)message.ReceivePort, message };
                if (samples.Any(m => m.IsDeleted || m.IsProxy)) throw new InvalidOperationException("E105: 見本の一部が削除済み・未読込です。");
                sources = samples.Select(m => m.Metaclass).ToArray();
            }
            else
            {
                // A diagram being built from nothing has no sample message. The view
                // definition lists the classes this editor places, so read them there.
                sources = PumlRuntime.BaseTypes(diagram);
                detail.AppendLine("Base types from view definition: " + string.Join(", ", sources.Select(c => c.Id)));
            }
            // Verify the standard relationship IDs against this profile before writing.
            var relationIds = new HashSet<string>(sources.SelectMany(c => c.GetFields().Cast<IField>())
                .Where(f => f.RelationshipClass != null).Select(f => f.RelationshipClass.Id));
            foreach (string id in SequencePayload.RelationTypes)
                if (!relationIds.Contains(SequencePayload.Prefix + id))
                    throw new InvalidOperationException("E106: 標準の構造関連が見つかりません: " + id);
            var sort = sources[6].GetFields().Cast<IField>().FirstOrDefault(f => f.Name == "MessageSort");
            if (sort == null) throw new InvalidOperationException("E107: メッセージ種別フィールドが未対応です。");
            if (message != null)
            {
                var kindValue = message.GetField("MessageSort");
                detail.AppendLine("MessageSort runtime type=" + (kindValue == null ? "null" : kindValue.GetType().FullName)
                    + "; value=" + (kindValue == null ? "null" : kindValue.ToString()));
                if (kindValue == null || !string.Equals(kindValue.ToString(), "Sync", StringComparison.Ordinal))
                    throw new InvalidOperationException("E108: 同期メッセージの保存値が想定と異なります。");
            }
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
            var payload = SequencePayload.Build(sources.Select(c => c.Id).ToArray(), diagram.EditorDefinition.Id, schema);
            if (plan != null)
            {
                stage = "PlantUML生成データの構築";
                var profile = PumlRuntime.Profile(diagram, sources, plan, project);
                if(profile.Relations.ContainsKey("RefersTo"))SequenceSyncRuntime.ResolveReferences(app,project,owner,plan.All().Where(n=>n.Kind=="ref"),profile.References,detail,!BatchMode);
                // Metaclass ids are fixed per profile, so one run is enough to pin them.
                detail.AppendLine("Resolved types (label / id / full name):");
                foreach (string row in profile.Resolved) detail.AppendLine("  " + row);
                payload = PumlBuild.Build(plan, profile, diagram.EditorDefinition.Id, schema, replacement==null?null:replacement.Identity);
                Write(Path.Combine(directory, "source.puml"), pumlText);
            }
            rootId = payload.Ids[0];
            detail.AppendLine("schema=" + schema + "; source=" + (fromFile ? "project header" : "public sample hypothesis"));
            detail.AppendLine("SDK=" + typeof(IProject).Assembly.FullName);
            detail.AppendLine("parent=" + ownerId + "; field=" + ownerField.Name + "; source=" + sampleId);
            stage = "生成データの記録";
            Write(Path.Combine(directory, "input.json"), payload.Json);
            Write(Path.Combine(directory, "before.txt"), detail.ToString()+(replacement==null?"":"\n更新前の構造:\n"+replacement.Before));
            if (!BatchMode && !app.Window.UI.ShowConfirmDialog(structureProbe ? "一時図で受信先を送信側の実行区間へ変更し、受信実行区間を削除・再作成して接続を戻します。\n各段階を照合し、最後に一時図を取り消します。続けますか？" : replaceExisting ? "現在の図「"+sample.Name+"」を「"+payload.Name+"」へ更新します。\n"+plan.Summary()+"\n旧子要素: "+(replacement.AllIds.Length-2)+"件を置換します。\n図IDは維持し、子要素IDは変わります。続けますか？" : updateProbe ? "同じIDへの再取り込みを一時図で検証します。\nprobe()をupdatedProbe()へ変更したデータを再取り込みし、取消後に一時モデルが消えたことを確認します。\n続けますか？" : "新しい図「" + payload.Name + "」を追加します。\n" + (plan == null ? "A → B : probe()\nライフライン2本・同期メッセージ1本" : plan.Summary()) + "\n入力データの記録: 済み\n続けますか？", Title))
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
            completion.Commit(delegate { transaction.Commit(); }); committed = true; LastRoot = rootId;
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
            Details = "診断情報\n" + detail.ToString();
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
    // An element already in the diagram is the surest sample, but a diagram being built
    // from nothing has none. The view definition names the classes the editor may place,
    // so read the concrete type from there instead of falling back to the abstract one.
    static IClass Resolve(ISequenceDiagram diagram,string[] definitionTypes,IEnumerable<IModel> observed,string label)
    { return Resolve(diagram,definitionTypes,observed,(IClass)null,label); }
    // Known concrete type ids, filled in once they have been read off a real profile.
    // Metaclass ids are fixed, so a value here removes the need for any sample at all.
    // Format: label, then the id. Leave a label out until its id is actually known.
    static readonly string[] Pinned = {
        // "分岐", "00000000-0000-0000-0000-000000000000",
    };
    static IClass Pin(IClass anchor,IClass declared,string label)
    {
        for(int i=0;i+1<Pinned.Length;i+=2)
            if(Pinned[i]==label)return ById(anchor,declared,Pinned[i+1]);
        return null;
    }
    static IClass ById(IClass anchor,IClass declared,string id)
    {
        if(anchor==null || declared==null || string.IsNullOrEmpty(id))return null;
        var root=anchor.Owner;
        for(int guard=0;root!=null && root.Parent!=null && guard<256;guard++)root=root.Parent;
        if(root==null)return null;
        var pending=new List<IPackage>{root};
        for(int i=0;i<pending.Count && i<4096;i++)
        {
            foreach(var k in pending[i].OwnedClasses.Cast<IClass>())
                if(k.Id==id)return !k.IsAbstract && Inherits(k,declared)?k:null;
            foreach(var sub in pending[i].SubPackages.Cast<IPackage>())pending.Add(sub);
        }
        return null;
    }
    // Whatever route found a type, remember it against this view definition so a project
    // that has no example of its own can still be filled in later.
    static string LearnedPath(ISequenceDiagram diagram)
    {
        string id=diagram==null || diagram.EditorDefinition==null?null:diagram.EditorDefinition.Id;
        if(string.IsNullOrEmpty(id) || id.IndexOfAny(Path.GetInvalidFileNameChars())>=0)return null;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NextDesign.SequenceSync","types",id+".txt");
    }
    static Dictionary<string,string> Learned(ISequenceDiagram diagram)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        string path=LearnedPath(diagram);
        try
        {
            if(path==null || !File.Exists(path))return result;
            foreach(string line in File.ReadAllLines(path,new UTF8Encoding(false)))
            {
                int split=line.IndexOf('\t');
                if(split>0)result[line.Substring(0,split)]=line.Substring(split+1);
            }
        }
        catch(Exception){}
        return result;
    }
    static void Learn(ISequenceDiagram diagram,string label,IClass type)
    {
        string path=LearnedPath(diagram);
        if(path==null || type==null)return;
        try
        {
            var known=Learned(diagram);
            if(known.ContainsKey(label) && known[label]==type.Id)return;
            known[label]=type.Id;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path,known.Select(pair=>pair.Key+"\t"+pair.Value),new UTF8Encoding(false));
        }
        catch(Exception){}
    }
    // A remembered id is only used when it still names a concrete class that really
    // derives from the declared one, checked against the metamodel this diagram uses.
    static IClass Remembered(ISequenceDiagram diagram,IClass anchor,IClass declared,string label)
    {
        string id;
        if(!Learned(diagram).TryGetValue(label,out id))return null;
        return ById(anchor,declared,id);
    }
    static bool Inherits(IClass candidate,IClass ancestor)
    {
        var seen=new HashSet<string>();var pending=new List<IClass>{candidate};
        for(int i=0;i<pending.Count && i<512;i++)
        {
            if(pending[i].Id==ancestor.Id)return true;
            foreach(var super in pending[i].SuperClasses.Cast<IClass>())if(seen.Add(super.Id))pending.Add(super);
        }
        return false;
    }
    // The surest sample is a real element, and the open diagram is not the only place to
    // look: one anywhere in the project settles the type the same way.
    static IClass Anywhere(IProject project,IClass declared)
    {
        if(project==null || declared==null || project.DesignModel==null)return null;
        var types=new List<IClass>();int scanned=0;
        foreach(var model in SequenceMappedUpdate.Tree(project.DesignModel))
        {
            if(++scanned>200000)break;
            var type=model.Metaclass;
            if(type==null || type.IsAbstract || type.Id==declared.Id)continue;
            if(!Inherits(type,declared))continue;
            if(!types.Any(k=>k.Id==type.Id))types.Add(type);
            if(types.Count>1)break;
        }
        return types.Count==1?types[0]:null;
    }
    // AllClasses covers the profile, but these classes live in the system metamodel that
    // ships with the product. Walk out from a class already resolved in that metamodel
    // to its root package and search the tree it belongs to.
    static IClass Sibling(IClass anchor,IClass declared)
    {
        if(anchor==null || declared==null)return null;
        var root=anchor.Owner;
        for(int guard=0;root!=null && root.Parent!=null && guard<256;guard++)root=root.Parent;
        if(root==null)return null;
        var found=new List<IClass>();var pending=new List<IPackage>{root};
        for(int i=0;i<pending.Count && i<4096;i++)
        {
            foreach(var k in pending[i].OwnedClasses.Cast<IClass>())
                if(!k.IsAbstract && k.Id!=declared.Id && Inherits(k,declared) && !found.Any(x=>x.Id==k.Id))found.Add(k);
            foreach(var sub in pending[i].SubPackages.Cast<IPackage>())pending.Add(sub);
            if(found.Count>1)break;
        }
        return found.Count==1?found[0]:null;
    }
    // Last resort for a kind the view definition does not list and whose owning field is
    // declared abstract: the profile itself holds exactly one concrete subclass.
    static IClass Descend(IProject project,IClass abstractClass,string label)
    {
        if(project==null || project.Profile==null || abstractClass==null)return null;
        var concrete=project.Profile.Metamodels.AllClasses.Cast<IClass>()
            .Where(k=>!k.IsAbstract && k.Id!=abstractClass.Id && Inherits(k,abstractClass))
            .GroupBy(k=>k.Id).Select(g=>g.First()).ToArray();
        if(concrete.Length==1)return concrete[0];
        if(concrete.Length>1)throw new InvalidOperationException("E121: "+label+"の具体型が複数あり、自動選択できません: "
            +string.Join(", ",concrete.Select(k=>k.FullName)));
        return null;
    }
    // Some kinds are not placeable on their own and so are absent from the view
    // definition; an operand only exists inside a fragment. For those the field on the
    // concrete owner class carries the type, as long as it is not the abstract one.
    static IClass Resolve(ISequenceDiagram diagram,string[] definitionTypes,IEnumerable<IModel> observed,IClass declared,string label)
    { return Resolve(diagram,definitionTypes,observed,()=>declared,label); }
    // The fallback searches can walk the whole project, so they run only when neither a
    // sample nor the view definition settles the type.
    static IClass Resolve(ISequenceDiagram diagram,string[] definitionTypes,IEnumerable<IModel> observed,Func<IClass> fallback,string label)
    {
        var seen=observed.Select(m=>m.Metaclass).GroupBy(c=>c.Id).Select(g=>g.First()).ToArray();
        if(seen.Length>1)throw new InvalidOperationException("E121: 見本の"+label+"に複数の型があり、自動選択できません。");
        if(seen.Length==1)return seen[0];
        var elements=diagram.EditorDefinition.Elements.Where(e=>e!=null && e.ModelClass!=null).ToArray();
        var defined=elements
            .Where(e=>definitionTypes.Any(name=>string.Equals(e.Type,name,StringComparison.OrdinalIgnoreCase)))
            .Select(e=>e.ModelClass).GroupBy(c=>c.Id).Select(g=>g.First()).ToArray();
        if(defined.Length==1)return defined[0];
        string available=string.Join(", ",elements.Select(e=>e.Type).Where(name=>!string.IsNullOrEmpty(name))
            .GroupBy(name=>name,StringComparer.OrdinalIgnoreCase).Select(g=>g.Key).OrderBy(name=>name,StringComparer.Ordinal));
        if(defined.Length>1)throw new InvalidOperationException("E121: ビュー定義の"+label+"に複数の型があり、自動選択できません。定義の種別: "+available);
        var declared=fallback==null?null:fallback();
        if(declared!=null && !declared.IsAbstract)return declared;
        if(declared!=null)available+=" / 宣言型: "+declared.FullName+"（抽象）";
        throw new InvalidOperationException("E121: "+label+"の具体型を決められません。"
            +label+"がある図を開いて取り込むか、この種別名を開発側へ伝えてください。定義の種別: "+available);
    }
    // The seven base classes, in the order the payload builder expects them.
    public static IClass[] BaseTypes(ISequenceDiagram diagram)
    {
        var interaction=diagram.Model==null?null:diagram.Model.Metaclass;
        if(interaction==null)throw new InvalidOperationException("E104: 図のモデル型を取得できません。");
        var frame=Resolve(diagram,new[]{"Frame","InteractionFrame"},
            diagram.Frame==null?new IModel[0]:new[]{diagram.Frame.Model},"枠");
        var lifeline=Resolve(diagram,new[]{"Lifeline","Lifelines"},diagram.Lifelines.Select(l=>l.Model),"ライフライン");
        var execution=Resolve(diagram,new[]{"ExecutionSpecification","ExecutionSpecifications","Execution"},
            diagram.ExecutionSpecifications.Select(e=>e.Model),"実行区間");
        var messageClass=Resolve(diagram,new[]{"Message","Messages"},diagram.Messages.Select(m=>m.Model),"メッセージ");
        return new[]{interaction,frame,lifeline,lifeline,execution,execution,messageClass};
    }
    // What the structure update needs to build a frame when the diagram holds none to
    // copy. The metaclasses come from the same resolution the generator uses; the field
    // ids are read off the metamodel, since the readback compares that pair.
    static string[] Wiring(IClass from,IClass to,string field,string relation,bool embed)
    {
        // A class can hold two ends of one relation: an operand has its own Fragments and, as a
        // fragment itself, the InteractionOperand that encloses it. The named field wins.
        var candidates=from.GetFields().Cast<IField>().Where(v=>v.RelationshipClass!=null
            && v.RelationshipClass.Id==SequencePayload.Prefix+relation).ToArray();
        var f=candidates.FirstOrDefault(v=>v.Name==field) ?? candidates.FirstOrDefault() ?? Field(from,field);
        if(f==null || f.RelationshipClass==null)throw new InvalidOperationException("E121: 関連フィールドを取得できません: "+field);
        var back=to==null?null:to.GetFields().Cast<IField>().FirstOrDefault(v=>v.RelationshipClass!=null
            && v.RelationshipClass.Id==f.RelationshipClass.Id && v.Id!=f.Id);
        return new[]{f.RelationshipClass.Id,embed?"Embed":"Ref",
            PumlBuild.Json(new[]{f.Id,back==null?"":back.Id})};
    }
    // What a new note is built from when the diagram may hold none to copy: the class the
    // view places, the interaction's ownership of it, and the field its text goes in.
    // Same resolution as the generator uses for a note.
    public static SequenceNoteTypes NoteTypes(ISequenceDiagram diagram,IProject project)
    {
        var source=BaseTypes(diagram);
        var interaction=source[0];
        var declaredNote=Child(new PumlProfile(),interaction,"Notes","Notes","___Interaction_InteractionNote");
        var note=Resolve(diagram,new[]{"InteractionNote","Note","Notes"},
            diagram.Notes.Select(n=>n.Model),()=>declaredNote!=null && declaredNote.IsAbstract
                ?(Anywhere(project,declaredNote) ?? Sibling(interaction,declaredNote)
                    ?? Pin(interaction,declaredNote,"Note") ?? Remembered(diagram,interaction,declaredNote,"Note") ?? Descend(project,declaredNote,"Note"))
                :declaredNote,"Note");
        Learn(diagram,"Note",note);
        var f=Field(note,"Body") ?? Field(note,"Text") ?? Field(note,"Name");
        if(f==null)throw new InvalidOperationException("E121: Note本文フィールドを取得できません。");
        if(f.Type!="String" && f.Type!="RichText")throw new InvalidOperationException("E121: Note本文の型が未対応です。");
        return new SequenceNoteTypes{Class=note.Id,Owns=Wiring(interaction,note,"Notes","___Interaction_InteractionNote",true),
            Field=f.Name,Storage=f.Type};
    }
    // What a new ref is built from, resolved the way the generator resolves one. The lane
    // coverage uses the same relation as a frame's; RefersTo is left out when the profile
    // has no such field, and the ref is then written without its target.
    // What a new destruction is built from, resolved the way the generator resolves one.
    public static SequenceDestroyTypes DestroyTypes(ISequenceDiagram diagram)
    {
        var source=BaseTypes(diagram);
        var declared=Child(new PumlProfile(),source[0],"Destructions","Destructions","___Interaction_Destruction");
        var definitions=diagram.EditorDefinition.Elements
            .Where(e=>string.Equals(e.Type,"Destruction",StringComparison.OrdinalIgnoreCase) && e.ModelClass!=null)
            .Select(e=>e.ModelClass).GroupBy(t=>t.Id).Select(g=>g.First()).ToArray();
        var observed=diagram.Destructions.Select(e=>e.Model.Metaclass).GroupBy(t=>t.Id).Select(g=>g.First()).ToArray();
        string selected=PumlTypeSelection.Destruction(definitions.Select(t=>t.Id).ToArray(),observed.Select(t=>t.Id).ToArray());
        var c=definitions.Concat(observed).First(t=>t.Id==selected);
        string[] message=null;
        try{message=Wiring(c,source[6],"DestroyMessage","DestroyMessage",false);}catch(InvalidOperationException){}
        return new SequenceDestroyTypes{Class=c.Id,Owns=Wiring(source[0],c,"Destructions","___Interaction_Destruction",true),
            Target=Wiring(c,source[2],"Lifeline","DestructionTargetLifeline",false),Message=message};
    }
    public static SequenceRefTypes RefTypes(ISequenceDiagram diagram,IProject project)
    {
        var source=BaseTypes(diagram);
        var interaction=source[0];
        var declaredUse=Child(new PumlProfile(),interaction,"InteractionUses","InteractionUses","___Interaction_InteractionUse");
        var use=Resolve(diagram,new[]{"InteractionUse","InteractionUses","Ref"},
            diagram.InteractionUses.Select(f=>f.Model),()=>declaredUse!=null && declaredUse.IsAbstract
                ?(Anywhere(project,declaredUse) ?? Sibling(interaction,declaredUse)
                    ?? Pin(interaction,declaredUse,"相互作用の利用") ?? Remembered(diagram,interaction,declaredUse,"相互作用の利用") ?? Descend(project,declaredUse,"相互作用の利用"))
                :declaredUse,"相互作用の利用");
        Learn(diagram,"相互作用の利用",use);
        var refers=Field(use,"RefersTo");
        return new SequenceRefTypes{Class=use.Id,
            Owns=Wiring(interaction,use,"InteractionUses","___Interaction_InteractionUse",true),
            Crossing=Wiring(use,source[2],"CoveredLifelines","CrossingFragmentCoveredLifeline",false),
            RefersTo=refers==null || refers.RelationshipClass==null?null
                :Wiring(use,interaction,"RefersTo",refers.RelationshipClass.Id.Substring(SequencePayload.Prefix.Length),false)};
    }
    public static SequenceFrameTypes FrameTypes(ISequenceDiagram diagram,IProject project)
    {
        var source=BaseTypes(diagram);
        var interaction=source[0];
        var declaredFragment=Child(new PumlProfile(),interaction,"Fragments","CombinedFragments","___Interaction_CombinedFragment");
        var fragment=Resolve(diagram,new[]{"CombinedFragment","Fragment","CombinedFragments"},
            diagram.Fragments.Select(f=>f.Model),()=>declaredFragment!=null && declaredFragment.IsAbstract
                ?(Anywhere(project,declaredFragment) ?? Sibling(interaction,declaredFragment)
                    ?? Pin(interaction,declaredFragment,"複合フラグメント") ?? Remembered(diagram,interaction,declaredFragment,"複合フラグメント")
                    ?? Descend(project,declaredFragment,"複合フラグメント"))
                :declaredFragment,"複合フラグメント");
        var declaredOperand=Child(new PumlProfile(),fragment,"Operands","Operands","___CombinedFragment_InteractionOperand");
        // The operand is not a class of its own: the product names it after the
        // interaction class with a suffix, which is why no class list ever held it.
        var sampleOperands=diagram.Fragments.Where(f=>f.Model.Metaclass.Id==fragment.Id)
            .SelectMany(f=>f.Operands).Select(o=>o.Model).ToArray();
        string operandId=sampleOperands.Length>0?sampleOperands[0].Metaclass.Id:interaction.Id+"_Operand";
        var types=new SequenceFrameTypes{Fragment=fragment.Id,Operand=operandId};
        var operatorField=Field(fragment,"Operator");
        if(operatorField==null || operatorField.TypeEnum==null)
            throw new InvalidOperationException("E121: フラグメントの演算子を取得できません。");
        foreach(var literal in operatorField.TypeEnum.Literals)types.Operators[literal.Name]=literal.Name;
        types.Owns=Wiring(interaction,fragment,"Fragments","___Interaction_CombinedFragment",true);
        types.Branches=Wiring(fragment,declaredOperand,"Operands","___CombinedFragment_InteractionOperand",true);
        types.Crossing=Wiring(fragment,source[2],"CoveredLifelines","CrossingFragmentCoveredLifeline",false);
        types.OperandMessage=Wiring(declaredOperand,source[6],"Messages","OperandTargetMessage",false);
        // A frame or ref inside a branch is also tied to that branch. Left out when the
        // profile has no such relation; the structure update then stops only if it needs one.
        try {types.Nested=Wiring(declaredOperand,fragment,"Fragments","NestedInteractionFragment",false);} catch(InvalidOperationException) {}
        return types;
    }
    // What the structure update builds messages, bars and free message ends from when the
    // diagram holds nothing of the kind to copy. Every row is resolved on its own; a row that
    // cannot be resolved stays empty and stops the update only where it is needed.
    public static SequenceBaseTypes SyncBaseTypes(ISequenceDiagram diagram,IProject project,bool ends)
    {
        var source=BaseTypes(diagram);
        var result=new SequenceBaseTypes{Message=source[6].Id,Execution=source[4].Id,Lifeline=source[2].Id};
        Func<Func<string[]>,string[]> tryRow=make=>{try{return make();}catch(InvalidOperationException){return null;}};
        result.OwnsMessage=tryRow(()=>Wiring(source[0],source[6],"Messages","___Interaction_Message",true));
        result.OwnsExecution=tryRow(()=>Wiring(source[0],source[4],"ExecutionSpecifications","___Interaction_ExecutionSpecification",true));
        result.OwnsLifeline=tryRow(()=>Wiring(source[0],source[2],"Lifelines","___Interaction_Lifeline",true));
        result.LaneExecution=tryRow(()=>Wiring(source[2],source[4],"ExecutionSpecifications","OwnedExecutionSpecification",false));
        result.SendFromBar=tryRow(()=>Wiring(source[4],source[6],"SendMessages","SendMessage",false));
        result.ReceiveFromBar=tryRow(()=>Wiring(source[4],source[6],"ReceiveMessages","ReceiveMessage",false));
        result.Reply=tryRow(()=>Wiring(source[4],source[6],"ReplyMessage","ExecutionSpecificationReplyMessage",false));
        if(ends)
        {
            var declaredEnd=Child(new PumlProfile(),source[0],"MessageEnds","MessageEnds","___Interaction_MessageEnd");
            var end=Resolve(diagram,new[]{"MessageEnd","MessageEnds"},diagram.MessageEnds.Select(e=>e.Model),()=>declaredEnd!=null && declaredEnd.IsAbstract
                    ?(Anywhere(project,declaredEnd) ?? Sibling(source[0],declaredEnd)
                        ?? Pin(source[0],declaredEnd,"メッセージ端") ?? Remembered(diagram,source[0],declaredEnd,"メッセージ端") ?? Descend(project,declaredEnd,"メッセージ端"))
                    :declaredEnd,"メッセージ端");
            Learn(diagram,"メッセージ端",end);
            result.MessageEnd=end.Id;
            result.OwnsMessageEnd=Wiring(source[0],end,"MessageEnds","___Interaction_MessageEnd",true);
            result.SendFromEnd=tryRow(()=>Wiring(end,source[6],"SendMessages","SendMessage",false));
            result.ReceiveFromEnd=tryRow(()=>Wiring(end,source[6],"ReceiveMessages","ReceiveMessage",false));
        }
        return result;
    }
    public static PumlProfile Profile(ISequenceDiagram diagram,IClass[] source,PumlPlan plan)
    { return Profile(diagram,source,plan,null); }
    public static PumlProfile Profile(ISequenceDiagram diagram,IClass[] source,PumlPlan plan,IProject project)
    {
        var p=new PumlProfile();
        p.Resolved.Add("相互作用\t"+source[0].Id);
        p.Resolved.Add("枠\t"+source[1].Id);
        p.Resolved.Add("ライフライン\t"+source[2].Id);
        p.Resolved.Add("実行区間\t"+source[4].Id);
        p.Resolved.Add("メッセージ\t"+source[6].Id);
        string[] names={"Interaction","Frame","Lifeline","Lifeline","ExecutionSpecification","ExecutionSpecification","Message"};
        for(int i=0;i<source.Length;i++)p.Types[names[i]]=source[i].Id;
        string[] keys={"Frame","Lifelines","ExecutionSpecifications","Messages","OwnedExecutionSpecification","SendMessage","ReceiveMessage"};
        for(int i=0;i<keys.Length;i++)p.Relations[keys[i]]=SequencePayload.Prefix+SequencePayload.RelationTypes[i];
        p.Sync=Literal(source[6],"MessageSort","Sync");
        if(plan.All().Any(n=>n.Kind=="async"))p.Async=Literal(source[6],"MessageSort","Async");
        if(plan.All().Any(n=>n.Kind=="reply"))
        {
            p.Reply=Literal(source[6],"MessageSort","Reply");
            // A reply drawn by hand is also tied to the bar it returns from. Without that
            // the product ends the bar at its minimum length the next time it lays the
            // diagram out, and the reply is left starting on the bare lifeline.
            Child(p,source[4],"ReplyMessage","ReplyMessage","ExecutionSpecificationReplyMessage");
        }
        var classes=new List<IClass>(source);
        if(plan.All().Any(n=>n.Kind=="destroy"))
        {
            var c=Child(p,source[0],"Destructions","Destructions","___Interaction_Destruction");
            var definitions=diagram.EditorDefinition.Elements
                .Where(e=>string.Equals(e.Type,"Destruction",StringComparison.OrdinalIgnoreCase) && e.ModelClass!=null)
                .Select(e=>e.ModelClass).GroupBy(t=>t.Id).Select(g=>g.First()).ToArray();
            var observed=diagram.Destructions.Select(e=>e.Model.Metaclass).GroupBy(t=>t.Id).Select(g=>g.First()).ToArray();
            string selected=PumlTypeSelection.Destruction(definitions.Select(t=>t.Id).ToArray(),observed.Select(t=>t.Id).ToArray());
            c=definitions.Concat(observed).First(t=>t.Id==selected);
            p.Types["Destruction"]=c.Id;
            Child(p,c,"DestructionTargetLifeline","Lifeline","DestructionTargetLifeline");
            // A destruction drawn by hand also points at the message that destroys the lane;
            // that is what makes the product read the message as a destroy message.
            try{Child(p,c,"DestroyMessage","DestroyMessage","DestroyMessage");}catch(InvalidOperationException){}
            // The sort the product gives a message drawn to a destruction, when the profile has one.
            var sortField=Field(source[6],"MessageSort");
            var destroyLiteral=sortField==null || sortField.TypeEnum==null?null:sortField.TypeEnum.Literals
                .FirstOrDefault(l=>l.Name.IndexOf("Destroy",StringComparison.OrdinalIgnoreCase)>=0 || l.Name.IndexOf("Delete",StringComparison.OrdinalIgnoreCase)>=0);
            // The sort is left as written: the import check compares it, and the destroy is carried
            // by the relation above, which is how the reader tells one.
            if(destroyLiteral!=null)p.Resolved.Add("破棄メッセージの種別候補\t"+destroyLiteral.Name);
        }
        if(plan.All().Any(n=>n.Left=="[" || n.Right=="]"))
        {
            var declaredEnd=Child(p,source[0],"MessageEnds","MessageEnds","___Interaction_MessageEnd");
            var c=Resolve(diagram,new[]{"MessageEnd","MessageEnds"},diagram.MessageEnds.Select(e=>e.Model),()=>declaredEnd!=null && declaredEnd.IsAbstract
                    ?(Anywhere(project,declaredEnd) ?? Sibling(source[0],declaredEnd)
                        ?? Pin(source[0],declaredEnd,"メッセージ端") ?? Remembered(diagram,source[0],declaredEnd,"メッセージ端") ?? Descend(project,declaredEnd,"メッセージ端"))
                    :declaredEnd,"メッセージ端");
            Learn(diagram,"メッセージ端",c); p.Resolved.Add("メッセージ端\t"+c.Id+"\t"+c.FullName);
            p.Types["MessageEnd"]=c.Id; classes.Add(c);
        }
        if(plan.All().Any(n=>n.Kind=="fragment"))
        {
            var declaredFragment=Child(p,source[0],"Fragments","CombinedFragments","___Interaction_CombinedFragment");
            var c=Resolve(diagram,new[]{"CombinedFragment","Fragment","CombinedFragments"},
                diagram.Fragments.Select(f=>f.Model),()=>declaredFragment!=null && declaredFragment.IsAbstract
                    ?(Anywhere(project,declaredFragment) ?? Sibling(source[0],declaredFragment)
                        ?? Pin(source[0],declaredFragment,"複合フラグメント") ?? Remembered(diagram,source[0],declaredFragment,"複合フラグメント") ?? Descend(project,declaredFragment,"複合フラグメント"))
                    :declaredFragment,"複合フラグメント");
            Learn(diagram,"複合フラグメント",c); p.Resolved.Add("複合フラグメント\t"+c.Id+"\t"+c.FullName);
            p.Types["CombinedFragment"]=c.Id; classes.Add(c);
            var declaredOperand=Child(p,c,"Operands","Operands","___CombinedFragment_InteractionOperand");
            // The operand is not a class of its own: the product names it after the
            // interaction class with a suffix, which is why no class list ever held it.
            string operandId=source[0].Id+"_Operand";
            var sampleOperands=diagram.Fragments.Where(f=>f.Model.Metaclass.Id==c.Id).SelectMany(f=>f.Operands).Select(o=>o.Model).ToArray();
            var operand=sampleOperands.Length>0?Resolve(diagram,new[]{"InteractionOperand","Operand","Operands"},sampleOperands,
                    ()=>declaredOperand!=null && declaredOperand.IsAbstract
                        ?(Anywhere(project,declaredOperand) ?? Sibling(c,declaredOperand)
                            ?? Pin(c,declaredOperand,"分岐") ?? Remembered(diagram,c,declaredOperand,"分岐"))
                        :declaredOperand,"分岐")
                :null;
            if(operand!=null)operandId=operand.Id;
            p.Resolved.Add("分岐\t"+operandId+"\t"+(operand==null?"（相互作用の型から導出）":operand.FullName));
            p.Types["InteractionOperand"]=operandId; if(operand!=null)classes.Add(operand);
            foreach(var op in plan.All().Where(n=>n.Kind=="fragment").Select(n=>n.Operator).Distinct())p.Operators[op]=Literal(c,"Operator",op);
        }
        if(plan.All().Any(n=>n.Kind=="ref"))
        {
            var declaredUse=Child(p,source[0],"InteractionUses","InteractionUses","___Interaction_InteractionUse");
            var c=Resolve(diagram,new[]{"InteractionUse","InteractionUses","Ref"},
                diagram.InteractionUses.Select(f=>f.Model),()=>declaredUse!=null && declaredUse.IsAbstract
                    ?(Anywhere(project,declaredUse) ?? Sibling(source[0],declaredUse)
                        ?? Pin(source[0],declaredUse,"相互作用の利用") ?? Remembered(diagram,source[0],declaredUse,"相互作用の利用") ?? Descend(project,declaredUse,"相互作用の利用"))
                    :declaredUse,"相互作用の利用");
            Learn(diagram,"相互作用の利用",c); p.Resolved.Add("相互作用の利用\t"+c.Id+"\t"+c.FullName);
            p.Types["InteractionUse"]=c.Id; classes.Add(c);
            // Double-clicking a ref opens what it refers to, through this relation.
            var refers=Field(c,"RefersTo");
            if(refers!=null && refers.RelationshipClass!=null)p.Relations["RefersTo"]=refers.RelationshipClass.Id;
        }
        if(plan.All().Any(n=>n.Kind=="note"))
        {
            var declaredNote=Child(p,source[0],"Notes","Notes","___Interaction_InteractionNote");
            var c=Resolve(diagram,new[]{"InteractionNote","Note","Notes"},
                diagram.Notes.Select(n=>n.Model),()=>declaredNote!=null && declaredNote.IsAbstract
                    ?(Anywhere(project,declaredNote) ?? Sibling(source[0],declaredNote)
                        ?? Pin(source[0],declaredNote,"Note") ?? Remembered(diagram,source[0],declaredNote,"Note") ?? Descend(project,declaredNote,"Note"))
                    :declaredNote,"Note");
            Learn(diagram,"Note",c); p.Resolved.Add("Note\t"+c.Id+"\t"+c.FullName);
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
                // A ref linked to an interaction may show that interaction's name instead of its own
                // text; either is the ref the input asked for. Anything else says what was read.
                var use=model as IInteractionUse;var target=use==null?null:use.RefersTo;
                var shown=new List<string>{Normalize(e.Text)};
                if(target!=null)
                {
                    shown.Add(Normalize(target.Name));
                    var parts=new List<string>();for(IModel at=target;at!=null && parts.Count<64;at=at.Owner)parts.Insert(0,at.Name);
                    shown.Add(Normalize(string.Join("::",parts)));
                }
                Require(shape!=null && shown.Contains(Normalize(shape.Text)),"ref本文（入力="+e.Text+" / 表示="+(shape==null?"図形なし":shape.Text)
                    +" / モデル名="+model.Name+" / 参照先="+(target==null?"なし":target.Name)+"）");
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
    // Where each bar starts and ends against the messages on it, in the diagram's own numbers,
    // to measure how Next Design lays bars out once the diagram is edited by hand. Lanes and
    // messages are numbered, never named.
    public string BarTimeline()
    {
        var lanes=Document.Elements.Where(e=>e.Kind=="participant").OrderBy(e=>e.Order).Select(e=>e.Id).ToList();
        var messages=Document.Elements.Where(e=>e.Kind=="message" && Geometry.ContainsKey(e.Id)).OrderBy(e=>Y[e.Id]).ToList();
        Func<string,string,double> at=(id,key)=>Convert.ToDouble(((Dictionary<string,object>)Geometry[id])[key],System.Globalization.CultureInfo.InvariantCulture);
        Func<string[],string> lane=l=>l==null || l.Length==0?"外":"L"+lanes.IndexOf(l[0]);
        var rows=new List<Tuple<double,int,string>>();
        for(int i=0;i<messages.Count;i++)
        {
            var m=messages[i];string[] from,to;m.Links.TryGetValue("sender",out from);m.Links.TryGetValue("receiver",out to);
            string sort;m.Attributes.TryGetValue("sort",out sort);
            double source=at(m.Id,"SourceY"),target=at(m.Id,"TargetY");
            rows.Add(Tuple.Create(source,1,"M"+i+" "+sort+" "+lane(from)+"→"+lane(to)+(Math.Abs(target-source)>1e-6?" 受信Y="+target.ToString("0.#"):"")));
        }
        var bars=Document.Elements.Where(e=>e.Kind=="execution" && Geometry.ContainsKey(e.Id)).OrderBy(e=>at(e.Id,"Y")).ToList();
        for(int i=0;i<bars.Count;i++)
        {
            var b=bars[i];string[] owner;b.Links.TryGetValue("participant",out owner);
            var uses=new List<string>();
            for(int k=0;k<messages.Count;k++)
            {
                string[] v;
                if(messages[k].Links.TryGetValue("sendExecution",out v) && v.Contains(b.Id))uses.Add("送M"+k);
                if(messages[k].Links.TryGetValue("receiveExecution",out v) && v.Contains(b.Id))uses.Add("受M"+k);
            }
            rows.Add(Tuple.Create(at(b.Id,"Y"),0,"┌E"+i+" "+lane(owner)+" X="+at(b.Id,"X").ToString("0.#")+" ["+string.Join(",",uses)+"]"));
            rows.Add(Tuple.Create(at(b.Id,"Y")+at(b.Id,"Height"),2,"└E"+i+" 長さ="+at(b.Id,"Height").ToString("0.#")));
        }
        return "実行区間とメッセージの縦位置（Y順）\n"+string.Join("\n",rows.OrderBy(r=>r.Item1).ThenBy(r=>r.Item2).Select(r=>r.Item1.ToString("0.#").PadLeft(7)+"  "+r.Item3));
    }
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
            // A message a destruction points back at is the one that destroys the lane, whatever
            // sort the profile records for it; the parser reads it the same way.
            if(model.GetRelationsWhere((r,f)=>r.Target!=null && r.Target.Id==model.Id && r.Metaclass!=null
                && r.Metaclass.Id==SequencePayload.Prefix+"DestroyMessage").Any())e.Attributes["sort"]="destroy";
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
        memberships.AddRange(SequenceRegion.Nesting(operandRegions,fragmentRegions,annotationRegions));
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
        doc.SettleExecutions(n=>snapshot.Y[n.Id]);
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
    // What each ref of a new diagram refers to, the way the update resolves it: an interaction
    // of the project named by the ref's text, when exactly one is. Keyed by the input line.
    public static void ResolveReferences(IApplication app,IProject project,IModel near,IEnumerable<PumlNode> refs,Dictionary<int,string> into,StringBuilder log,bool ask)
    {
        var wanted=refs.ToArray();
        if(wanted.Length==0)return;
        var interactions=Interactions(project);
        foreach(var n in wanted)
        {
            string chosen=PickReference(app,project,near,n.Text,n.Line,SequenceReferenceResolver.Find(n.Text,interactions),log,ask);
            if(chosen!=null)into[n.Line]=chosen;
        }
    }
    // The exported editor leaves out a size or place a shape has by default (a lane or frame
    // drawn by hand comes without its Width). The update lays shapes out from those numbers,
    // so what is missing is taken from the diagram as the SDK reads it. Nothing is changed.
    internal static string CompleteGeometry(string exported,ISequenceDiagram diagram,StringBuilder log)
    {
        var data=SequenceJson.Parse(exported);
        var editor=data["Editors"]==null?null:data["Editors"].Items.FirstOrDefault(v=>v["Id"]!=null && v["Id"].StringValue()==diagram.Id);
        if(editor==null || editor.Properties==null)return exported;
        var nodes=diagram.Shapes.OfType<ISequenceNodeShape>().ToDictionary(n=>n.Id);
        Func<double,SequenceJson> number=v=>SequenceJson.Parse(v.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        int filled=0;
        foreach(var list in editor.Properties.Values.Where(v=>v!=null && v.Items!=null))
            foreach(var sh in list.Items.Where(i=>i!=null && i.Properties!=null && i["Id"]!=null && i["Id"].Raw.StartsWith("\"")))
            {
                ISequenceNodeShape node;
                if(!nodes.TryGetValue(sh["Id"].StringValue(),out node))continue;
                var wanted=new List<KeyValuePair<string,double>>{new KeyValuePair<string,double>("X",node.LocationX),new KeyValuePair<string,double>("Width",node.Width)};
                if(!(node is ILifelineShape))
                {
                    wanted.Add(new KeyValuePair<string,double>("Y",node.LocationY));
                    var bar=node as IExecutionSpecificationShape;
                    wanted.Add(new KeyValuePair<string,double>("Height",bar!=null?bar.Length:node.Height));
                    if(bar!=null)wanted.Add(new KeyValuePair<string,double>("Length",bar.Length));
                }
                foreach(var pair in wanted)
                    if(sh[pair.Key]==null){sh.Properties[pair.Key]=number(pair.Value);filled++;}
            }
        if(filled==0)return exported;
        log.AppendLine("Editor geometry completed from the SDK: "+filled+" values");
        return data.ToJsonString();
    }
    internal static SequenceReferenceCandidate[] Interactions(IProject project)
    {
        return SequenceMappedUpdate.Tree(project.DesignModel).OfType<IInteraction>()
            .Select(m=>new SequenceReferenceCandidate{Id=m.Id,Name=m.Name,Path=QualifiedName(m)}).ToArray();
    }
    // One interaction for a ref. Several of the same name are for the designer to tell apart:
    // they are offered one by one, the one nearest the diagram in the model tree first, and
    // declining them all leaves the ref without a target. The batch never asks.
    internal static string PickReference(IApplication app,IProject project,IModel near,string text,int line,SequenceReferenceCandidate[] matches,StringBuilder log,bool ask)
    {
        if(matches.Length==1){log.AppendLine("ref参照先 "+line+"行: 解決");return matches[0].Id;}
        if(matches.Length==0){log.AppendLine("ref参照先 "+line+"行: 0候補（参照先なしで作成）");return null;}
        if(!ask || app==null){log.AppendLine("ref参照先 "+line+"行: "+matches.Length+"候補（一括のため参照先なしで作成）");return null;}
        Func<IModel,List<string>> chain=m=>{var ids=new List<string>();for(var at=m;at!=null && ids.Count<256;at=at.Owner)ids.Insert(0,at.Id);return ids;};
        var mine=near==null?new List<string>():chain(near);
        Func<SequenceReferenceCandidate,int> shared=c=>{var model=project.GetModelById(c.Id);if(model==null)return 0;var theirs=chain(model);int k=0;while(k<mine.Count && k<theirs.Count && mine[k]==theirs[k])k++;return k;};
        var ordered=matches.OrderByDescending(shared).ThenBy(c=>c.Path,StringComparer.Ordinal).ToArray();
        for(int i=0;i<ordered.Length;i++)
            if(app.Window.UI.ShowConfirmDialog("ref「"+text+"」（入力 "+line+"行目）の参照先の候補が "+ordered.Length+"件あります。\n\n候補 "+(i+1)+"/"+ordered.Length+":\n"+ordered[i].Path
                +"\n\nこの相互作用を参照先にしますか？\nOK: この相互作用を参照先にします。\nキャンセル: 次の候補を表示します（最後の候補でキャンセルすると参照先なしで作成します）。",SequenceExperiment.Title))
            {log.AppendLine("ref参照先 "+line+"行: "+ordered.Length+"候補から選択 "+(i+1)+"番目");return ordered[i].Id;}
        log.AppendLine("ref参照先 "+line+"行: "+ordered.Length+"候補（選択なし・参照先なしで作成）");
        return null;
    }
    // A ref linked to an interaction may show that interaction's name instead of its own text.
    // When the input's ref resolves to the same interaction, those texts are the same ref.
    internal static void AlignLinkedRefs(IProject project,SequenceDocument current,SequenceDocument desired)
    {
        foreach(var e in current.Elements.Where(e=>e.Kind=="ref"))
        {
            string reference;
            if(!e.Attributes.TryGetValue("reference",out reference) || string.IsNullOrEmpty(reference))continue;
            var target=project.GetModelById(reference);if(target==null)continue;
            var shown=new[]{target.Name,QualifiedName(target)}.Select(SequenceLabels.Fold).ToArray();
            if(!shown.Contains(SequenceLabels.Fold(e.Text)))continue;
            var texts=desired.Elements.Where(d=>d.Kind=="ref" && d.Attributes.ContainsKey("reference") && d.Attributes["reference"]==reference).Select(d=>d.Text).Distinct().ToArray();
            if(texts.Length==1)e.Text=texts[0];
        }
    }
    const string UnsavedAdvice="S220: 退避データを取得できません。プロジェクトを保存してから実行してください。"
        +"この操作は自動保存しません。";
    // What each message and bar is tied to, as the product holds it: the port shapes, every
    // relation with its fields and order, and the plain field values. Put next to one drawn
    // by hand, it shows what a generated diagram lacks. Ids are cut to their last four
    // characters, enough to pair the rows up.
    static string Connections(ISequenceDiagram diagram)
    {
        Func<string,string> tail=id=>string.IsNullOrEmpty(id)?"-":id.Length<=4?id:id.Substring(id.Length-4);
        Func<object,string> kind=port=>port==null?"なし":port is IExecutionSpecificationShape?"実行区間":port is ILifelineShape?"ライフライン":port.GetType().Name;
        Func<ISequenceShape,string> portId=shape=>shape==null?"-":tail(shape.ModelId);
        Func<IModel,string> describe=model=>{
            var text=new StringBuilder();
            foreach(var r in model.GetRelationsWhere((relation,field)=>true).OrderBy(r=>r.Metaclass.Id,StringComparer.Ordinal))
            {
                bool outgoing=r.Source.Id==model.Id;var other=outgoing?r.Target:r.Source;
                string name=r.Metaclass.Id.Substring(r.Metaclass.Id.LastIndexOf('.')+1);
                text.Append("\n    ").Append(outgoing?"→ ":"← ").Append(name)
                    .Append(" ").Append(other.ClassName).Append(":").Append(tail(other.Id))
                    .Append(" field=").Append(r.SourceField==null?"-":r.SourceField.Name).Append("/").Append(r.TargetField==null?"-":r.TargetField.Name)
                    .Append(" index=").Append(r.SourceIndex).Append("/").Append(r.TargetIndex);
            }
            if(model.Metaclass!=null)
                foreach(var f in model.Metaclass.GetFields().Cast<IField>().Where(f=>!f.IsEmbedded && !f.IsReference))
                {
                    // A field of an enumerated type has no string form; read the value itself.
                    string value=null;try{value=model.GetFieldString(f.Name);}catch(Exception){}
                    if(string.IsNullOrEmpty(value))try{var raw=model.GetField(f.Name);value=raw==null?null:Convert.ToString(raw);}catch(Exception){}
                    if(!string.IsNullOrEmpty(value))text.Append("\n    ").Append(f.Name).Append("=").Append(value.Length>24?value.Substring(0,24):value);
                }
            return text.ToString();
        };
        var lines=new StringBuilder("接続の実測（IDは末尾4文字）");
        foreach(var m in diagram.Messages.OrderBy(m=>m.SourceY))
            lines.Append("\nメッセージ ").Append(tail(m.ModelId)).Append(" Y=").Append(m.SourceY).Append("/").Append(m.TargetY)
                .Append(" 送信=").Append(kind(m.SendPort)).Append(":").Append(portId(m.SendPort as ISequenceShape))
                .Append(" 受信=").Append(kind(m.ReceivePort)).Append(":").Append(portId(m.ReceivePort as ISequenceShape))
                .Append(m.Model==null?"":describe(m.Model));
        foreach(var e in diagram.ExecutionSpecifications.OrderBy(e=>e.LocationY))
            lines.Append("\n実行区間 ").Append(tail(e.ModelId)).Append(" ").Append(e.Lifeline==null?"?":tail(e.Lifeline.ModelId))
                .Append(" Y=").Append(e.LocationY).Append(" 長さ=").Append(e.Length)
                .Append(e.Model==null?"":describe(e.Model));
        foreach(var d in diagram.Destructions.OrderBy(d=>d.LocationY))
            lines.Append("\n破棄 ").Append(tail(d.ModelId)).Append(" ").Append(d.Lifeline==null?"?":tail(d.Lifeline.ModelId))
                .Append(" Y=").Append(d.LocationY).Append(d.Model==null?"":describe(d.Model));
        foreach(var l in diagram.Lifelines.OrderBy(l=>l.LocationX))
            lines.Append("\nライフライン ").Append(tail(l.ModelId)).Append(" 長さ=").Append(l.TimelineLength);
        return lines.ToString();
    }
    // HasUnsavedChanges answers false when the dirty state holds nothing savable, but the
    // export still refuses, so ask the design model as well.
    // Set by a caller that may save the project before an update without asking, such as an
    // MCP tool acting for the user. Otherwise the user is asked each time.
    public static bool SaveBeforeUpdate;
    // Set by a caller that updates without saving and without asking, such as an MCP tool: the
    // snapshot is built from the SDK instead of the export (see SequenceSnapshotBuilder).
    public static bool UpdateWithoutSaving;
    // Every update takes the snapshot from the SDK, saved or not: to test that path in the batch.
    internal static bool ForceSdkSnapshot;
    static bool UseSdkSnapshot(IApplication app)
    {
        if(UpdateWithoutSaving)return true;
        if(SaveBeforeUpdate || Batch)return false;
        return app.Window.UI.ShowConfirmDialog("プロジェクトに未保存の変更があります。\n保存せずに反映しますか？（試験中）\n図の写しを SDK から組み立てて反映します。図形の色・形・参加者の余白などの見た目の細部が既定に戻ることがあります。反映後の照合で食い違えば元に戻して止まります。\n\nOK: 保存せずに反映する\nキャンセル: 保存して反映するかを次に確認する",SequenceExperiment.Title);
    }
    static bool SaveFirst(IApplication app,IProject project,StringBuilder log)
    {
        if(!SaveBeforeUpdate)
        {
            if(Batch)return false;
            if(!app.Window.UI.ShowConfirmDialog("プロジェクトに未保存の変更があります。\n反映の前に図の写しを取るため、保存が必要です（作業中の他の変更も一緒に保存されます）。\n\nOK: 保存して反映を続ける\nキャンセル: 中止する（何も変更しません）",SequenceExperiment.Title))return false;
        }
        if(!app.Workspace.SaveProject(project,false))throw new InvalidOperationException("S220: プロジェクトを保存できませんでした。");
        log.AppendLine("反映の前にプロジェクトを保存しました（"+(SaveBeforeUpdate?"呼び出し元が許可":"利用者が承認")+"）。");
        return true;
    }
    static bool Unsaved(IProject project)
    {
        try {return project.HasUnsavedChanges() || (project.DesignModel!=null && project.DesignModel.IsDirty);}
        catch(Exception) {return false;}
    }
    // Milliseconds per stage of the last run, shown with its result so a slow step can be
    // named from a measurement rather than guessed.
    internal static readonly List<string> Timings=new List<string>();
    // Set by the scenario batch: the diagram and input to use without asking, and what the
    // last run found.
    internal static bool Batch;
    internal static ISequenceDiagram BatchDiagram;
    internal static string BatchInput;
    internal static int LastChanges=-1;
    internal static bool LastCommitted;
    internal static string LastReasons="";
    static System.Diagnostics.Stopwatch clock;
    internal static void Lap(string stage)
    {
        if(clock==null)return;
        Timings.Add(stage+" "+clock.ElapsedMilliseconds);clock.Restart();
    }
    internal static string TimingLine()
    { return Timings.Count==0?"":"\n処理時間(ms): "+string.Join(" / ",Timings); }
    public static void Preview(IApplication app,bool prepare=false,bool trial=false,bool retain=false,bool reconnectCommit=false)
    {
        Timings.Clear();clock=null;
        var log=new StringBuilder();string report=null;string screenshot=null;
        retain=retain||reconnectCommit;trial=trial||retain;prepare=prepare||trial;
        try
        {
            LastChanges=-1;LastCommitted=false;LastReasons="";
            var diagram=Batch && BatchDiagram!=null?BatchDiagram:app.Workspace.CurrentEditor as ISequenceDiagram;
            if(diagram==null)throw new InvalidOperationException("S210: シーケンス図を開いてください。");
            string path=Batch && BatchInput!=null?BatchInput:app.Window.UI.ShowOpenFileDialog("図全体と比較するPlantUML","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
            if(string.IsNullOrEmpty(path))return;
            if(new FileInfo(path).Length>300000)throw new InvalidOperationException("S210: 入力は300KB以下にしてください。");
            clock=System.Diagnostics.Stopwatch.StartNew();
            var desired=SequenceDocument.Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
            var current=DiagramSnapshot.Read(diagram,log);
            Lap("図の読取り");
            // Resolve only unambiguous references for this non-mutating audit command. That
            // walks the whole design model, so only an input with a ref pays for it.
            var project=app.Workspace.CurrentProject;
            var interactions=desired.Elements.Any(e=>e.Kind=="ref")?Interactions(project):new SequenceReferenceCandidate[0];
            if(interactions.Length>0)Lap("ref参照先の探索");
            var drawn=current.Document.Elements.FirstOrDefault(e=>e.Kind=="interaction");
            var near=drawn==null?null:project.GetModelById(drawn.Id);
            // Candidates the diagram already refers to need no question: that is the one it means.
            var linked=new HashSet<string>(current.Document.Elements.Where(e=>e.Kind=="ref" && e.Attributes.ContainsKey("reference")).Select(e=>e.Attributes["reference"]));
            foreach(var e in desired.Elements.Where(e=>e.Kind=="ref"))
            {
                var matches=SequenceReferenceResolver.Find(e.Text,interactions);
                if(matches.Length>1 && matches.Count(m=>linked.Contains(m.Id))==1)matches=matches.Where(m=>linked.Contains(m.Id)).ToArray();
                string chosen=PickReference(app,project,near,e.Text,e.Line,matches,log,!Batch);
                e.Attributes["reference"]=chosen??"";
                if(chosen==null)current.Limitations.Add("ref参照先 "+e.Line+"行: "+matches.Length+"候補");
            }
            // What the diagram reads as, before a linked ref takes the input's text: the check that
            // nothing changed while preparing compares a fresh read with this.
            string readAs=current.Document.ToJson();
            AlignLinkedRefs(project,current.Document,desired);
            var plan=SequenceNotePolicy.Build(current.Document,desired,()=>Guid.NewGuid().ToString());
            LastChanges=plan.Changes.Count;
            var preflight=SequenceStructurePreflight.Check(current.Document,plan);
            Lap("差分計画");
            // The snapshot goes through ExportModelUnit, which refuses to run while the
            // project has unsaved changes. The project is saved only when the caller allows
            // it (SaveBeforeUpdate, for a caller such as an MCP tool) or the user agrees here;
            // otherwise the run stops before any work.
            bool mayRetry=false,fromSdk=prepare && ForceSdkSnapshot;
            if(prepare && !fromSdk && SequenceEditorCapture.BatchSource==null && Unsaved(project))
            {
                if(UseSdkSnapshot(app))fromSdk=true;
                else if(!SaveFirst(app,project,log))throw new InvalidOperationException(UnsavedAdvice);
            }
            else mayRetry=prepare && !fromSdk && SequenceEditorCapture.BatchSource==null;

            report="{\"version\":1,\"project\":"+SequencePayload.Q(project.Id)+",\"diagram\":"+SequencePayload.Q(diagram.Id)
                +",\"current\":"+current.Document.ToJson()+",\"desired\":"+desired.ToJson()+",\"plan\":"+plan.ToJson()
                +",\"structurePreflight\":"+preflight.ToJson()+",\"expected\":"+plan.Expected.ToJson()+",\"limitations\":"+PumlBuild.Json(current.Limitations.ToArray())
                +",\"shapes\":"+PumlBuild.Json(current.ShapeIds.ToDictionary(p=>p.Key,p=>(object)p.Value))
                +",\"geometry\":"+PumlBuild.Json(current.Geometry)+"}";
            foreach(var c in plan.Changes)log.AppendLine(c.Action+" "+c.Kind+" line="+c.Line+" id="+c.Id);
            foreach(var warning in current.Limitations)log.AppendLine("要照合: "+warning);
            screenshot=(trial?"適用前の比較結果（更新後の残差ではありません）\n":"現在の図と入力の比較結果\n")+SequenceAudit.Reasons(current.Document,desired,plan)+"\f"+preflight.Summary()+"\f"+current.BarTimeline();
            // Only the comparison shows it; applying already has enough pages.
            if(!prepare)
            {
                try {screenshot+="\f"+Connections(diagram);}
                catch(Exception ex) {screenshot+="\f接続の実測: 取得できません: "+ex.Message;}
            }
            log.AppendLine(screenshot);
            SequenceExperiment.Summary=SequenceAudit.Summary(plan,current.Limitations.Count)+"\n構造更新の停止理由: "+preflight.Reasons.Count+"件（診断表示）";
            log.AppendLine("Scope: "+project.Id+" / "+diagram.ModelId+" / "+diagram.Id);
            LastReasons=string.Join(" / ",preflight.Reasons.Distinct());
            // Checked after the comparison is logged, so the reasons reach the diagnostics.
            // Nothing differs: the diagram already is the input, which is not a failure.
            bool same=plan.Changes.Count==0;
            if(retain && same)SequenceExperiment.Summary="図は入力と一致しています。反映する差分はありません。\n"+SequenceAudit.Summary(plan,current.Limitations.Count);
            if(retain && !same && !preflight.CanCommit())
                throw new InvalidOperationException("S231: 反映できない差分が含まれています。"
                    +(preflight.Reasons.Count>0?"\n"+string.Join("\n",preflight.Reasons.Distinct()):"\n構造更新の対象がありません。"));
            if(prepare)
            {
                if(!preflight.Candidate)
                {
                    if(!same)SequenceExperiment.Summary="構造更新データ: 未作成 / 図への反映なし\n"+preflight.Summary();
                }
                else
                {
                    var root=diagram.Model as IInteraction;
                    if(root==null || !root.IsEditable || root.IsProxy || root.IsDeleted || string.IsNullOrEmpty(project.Path))
                        throw new InvalidOperationException("S220: 保存済みで編集可能な図を開いてください。");
                    string exported=null;
                    if(fromSdk)
                    {
                        exported=SequenceSnapshotBuilder.Build(project,root,diagram,SequenceSnapshotBuilder.Schema(project),log);
                        log.AppendLine("写し: SDK から組み立て（保存なし）");
                    }
                    else try {SequenceEditorCapture.Read(project,root,diagram,log,delegate(string value){exported=value;});}
                    catch(Exception ex)
                    {
                        // The export refuses on a dirty project even when nothing is savable,
                        // for instance right after a trial has rolled its changes back. Saving
                        // clears that; it is offered once, like above.
                        if(ex.Message.IndexOf("保存",StringComparison.Ordinal)<0)throw;
                        if(!mayRetry || !SaveFirst(app,project,log))throw new InvalidOperationException(UnsavedAdvice,ex);
                        SequenceEditorCapture.Read(project,root,diagram,log,delegate(string value){exported=value;});
                    }
                    Lap("エクスポート");
                    exported=CompleteGeometry(exported,diagram,log);
                    SequenceFrameTypes frameTypes=null;
                    string rootId=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
                    var byExpected=plan.Expected.Elements.ToDictionary(e=>e.Id);
                    if(preflight.AddFragments.Count>0 || preflight.AddOperands.Count>0 || preflight.MoveMessages.Count>0 || preflight.NestChanges.Count>0
                        || preflight.AddMessages.Concat(preflight.AddRefs).Any(id=>byExpected[id].Parent!=rootId))
                    {
                        // Resolve the metaclasses only when a frame is being added or a message
                        // goes into an operand, which needs the operand-to-message relation,
                        // so a diagram without them still runs every other change.
                        try {frameTypes=PumlRuntime.FrameTypes(diagram,project);}
                        catch(Exception ex) {throw new InvalidOperationException("S220: フラグメントの型を解決できません: "+ex.Message,ex);}
                        log.AppendLine("frame types: "+frameTypes.ToJson());
                    }
                    // Literals for message sorts, so a message of a sort the diagram has none of
                    // can still be added.
                    SequenceStructurePreparation.SortLiterals.Clear();
                    try
                    {
                        var messageClass=PumlRuntime.BaseTypes(diagram)[6];
                        var sortField=messageClass.GetFields().Cast<IField>().FirstOrDefault(f=>f.Name=="MessageSort");
                        if(sortField!=null && sortField.TypeEnum!=null)
                            foreach(string sort in new[]{"sync","async","reply"})
                            {
                                var literal=sortField.TypeEnum.Literals.FirstOrDefault(l=>string.Equals(l.Name,sort,StringComparison.OrdinalIgnoreCase));
                                if(literal!=null)SequenceStructurePreparation.SortLiterals[sort]=literal.Name;
                            }
                    }
                    catch(Exception ex){log.AppendLine("message sort literals: "+ex.Message);}
                    // Metaclasses and relation rows for building what the diagram holds nothing of to
                    // copy, and for free ends of messages to or from outside the diagram.
                    SequenceStructurePreparation.BaseTypes=null;
                    bool needsEnds=preflight.NeedsFreeEnds(plan);
                    try {SequenceStructurePreparation.BaseTypes=PumlRuntime.SyncBaseTypes(diagram,project,needsEnds);}
                    catch(Exception ex) {log.AppendLine("base types: "+ex.Message);if(needsEnds)throw new InvalidOperationException("S220: 図外の端の型を解決できません: "+ex.Message,ex);}
                    SequenceStructurePreparation.DestroyTypes=null;
                    if(preflight.AddDestroys.Count>0)
                    {
                        try {SequenceStructurePreparation.DestroyTypes=PumlRuntime.DestroyTypes(diagram);}
                        catch(Exception ex) {throw new InvalidOperationException("S220: 破棄の型を解決できません: "+ex.Message,ex);}
                    }
                    SequenceNoteTypes noteTypes=null;
                    if(preflight.AddNotes.Count>0)
                    {
                        try {noteTypes=PumlRuntime.NoteTypes(diagram,project);}
                        catch(Exception ex) {throw new InvalidOperationException("S220: Noteの型を解決できません: "+ex.Message,ex);}
                        log.AppendLine("note types: "+noteTypes.Class+" / "+PumlBuild.Json(noteTypes.Owns)+" / "+noteTypes.Field+":"+noteTypes.Storage);
                    }
                    SequenceRefTypes refTypes=null;
                    if(preflight.AddRefs.Count>0 || preflight.RefTargetChanges.Count>0)
                    {
                        try {refTypes=PumlRuntime.RefTypes(diagram,project);}
                        catch(Exception ex) {throw new InvalidOperationException("S220: refの型を解決できません: "+ex.Message,ex);}
                        log.AppendLine("ref types: "+refTypes.Class+" / "+PumlBuild.Json(refTypes.Owns)+" / "+PumlBuild.Json(refTypes.Crossing)
                            +" / "+(refTypes.RefersTo==null?"no RefersTo":PumlBuild.Json(refTypes.RefersTo)));
                    }
                    Lap("型の解決");
                    var preparation=SequenceStructurePreparation.Build(exported,diagram.Id,current.Document,plan,frameTypes,noteTypes,refTypes);
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
                    if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || (!Batch && (app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=diagram.Id))
                        || DiagramSnapshot.Read(diagram,new StringBuilder()).Document.ToJson()!=readAs)
                        throw new InvalidOperationException("S220: 準備中に対象の図が変化しました。");
                    Lap("準備");
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
                        screenshot=SequenceExperiment.Summary+"\f試行診断\n"+log.ToString();
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
        SequenceExperiment.Details=screenshot??log.ToString();
        if(!Batch)SequenceExperiment.Show(app);
    }
}

public static class SequenceStructureTrial
{
    static string Port(IMessagePort value) {var m=value as IModel;return m==null?"":m.Id;}
    static string FieldId(IField value) {return value==null?"":value.Id;}
    static string Number(double value){return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
    // The editor keeps its old drawing until the page is refreshed, so switching diagrams
    // by hand was the only way to see a result. Refresh it here instead.
    static void Refresh(IApplication app,StringBuilder log)
    {
        try {app.Window.EditorPage.UpdateEditors();}
        catch(Exception ex) {log.AppendLine("editor refresh failed: "+ex.Message);}
    }
    static bool Matches(Action verify,StringBuilder log)
    {
        try {verify();return true;} catch(Exception ex) {log.AppendLine(ex.ToString());return false;}
    }
    static SequenceTrialState Rounded(IProject project,string rootId,Func<ISequenceDiagram> fresh,string[] newShapes)
    {
        var state=Read((IInteraction)project.GetModelById(rootId),fresh());
        state.Round(newShapes);return state;
    }
    static SequenceTrialState Read(IInteraction root,ISequenceDiagram diagram)
    {
        var state=new SequenceTrialState();
        var tree=SequenceMappedUpdate.Tree(root).ToArray();
        var inTree=new HashSet<string>(tree.Select(m=>m.Id));
        Action<IModel> record=m=>state.Models[m.Id]=PumlBuild.Json(new[]{m.Metaclass.Id,m.Name,m.Owner==null?"":m.Owner.Id,m.IsDeleted.ToString()});
        foreach(var model in tree)
        {
            record(model);
            foreach(var r in model.GetRelationsWhere((relation,field)=>true))
            {
                state.Relations[r.Id]=new[]{r.Source.Id,r.Target.Id,r.SourceIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),r.TargetIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                state.RelationFields[r.Id]=PumlBuild.Json(new[]{FieldId(r.SourceField),FieldId(r.TargetField)});
                // Only models of this diagram: an operation a message refers to is not part of it,
                // and goes out of reach once the message is deleted.
                if(inTree.Contains(r.Source.Id))record(r.Source);if(inTree.Contains(r.Target.Id))record(r.Target);
            }
        }
        foreach(var m in root.Messages)
        {
            // A message a destruction points back at reads as a destroy message, as the snapshot reads it.
            bool destroying=m.GetRelationsWhere((r,f)=>r.Target!=null && r.Target.Id==m.Id && r.Metaclass!=null
                && r.Metaclass.Id==SequencePayload.Prefix+"DestroyMessage").Any();
            state.Ports[m.Id]=new[]{Port(m.SendPort),Port(m.ReceivePort),m.Sender==null?"":m.Sender.Id,m.Receiver==null?"":m.Receiver.Id,destroying?"destroy":m.Kind};
        }
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
    // What the last failed check found, for the summary: a batch shows only that.
    internal static string LastMismatch="";
    static void Verify(SequenceTrialState expected,SequenceTrialState actual,string phase,StringBuilder log)
    {
        string differences=expected.DifferenceCounts(actual);log.AppendLine(phase+": "+differences);
        string relationDetails=expected.RelationDifferences(actual);
        if(relationDetails.Length>0)log.AppendLine("\f関連の照合内訳 / "+phase+"\n"+relationDetails+"\f");
        string shapeDetails=expected.ShapeDifferences(actual);
        if(shapeDetails.Length>0)log.AppendLine("\f図形の照合内訳 / "+phase+"\n"+shapeDetails+"\f");
        if(expected.Signature()!=actual.Signature())
        {
            var ports=expected.Ports.Keys.Union(actual.Ports.Keys).Where(k=>!expected.Ports.ContainsKey(k) || !actual.Ports.ContainsKey(k)
                || PumlBuild.Json(expected.Ports[k])!=PumlBuild.Json(actual.Ports[k])).Take(6)
                .Select(k=>k+" "+(expected.Ports.ContainsKey(k)?PumlBuild.Json(expected.Ports[k]):"-")+" / "+(actual.Ports.ContainsKey(k)?PumlBuild.Json(actual.Ports[k]):"-"));
            var models=expected.Models.Keys.Union(actual.Models.Keys).Where(k=>!expected.Models.ContainsKey(k) || !actual.Models.ContainsKey(k) || expected.Models[k]!=actual.Models[k]).Take(6)
                .Select(k=>k+" "+(expected.Models.ContainsKey(k)?expected.Models[k]:"-")+" / "+(actual.Models.ContainsKey(k)?actual.Models[k]:"-"));
            LastMismatch=phase+": "+differences+"\n"+relationDetails+"\n"+shapeDetails+"\n送受信(期待/実測): "+string.Join(" ; ",ports)+"\nモデル(期待/実測): "+string.Join(" ; ",models);
        }
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
        int reconnectCount=prepared.ReconnectCount;
        if(reconnectCommit && !retain)throw new InvalidOperationException("S231: 確定モードが不正です。");
        // The preflight has already sorted every change into something the package writes;
        // a package that writes nothing at all is the only thing left to refuse.
        int touched=prepared.DeleteIds.Length+prepared.AddedExecutions.Length
            +prepared.AddedParticipants.Length+prepared.DeleteParticipantIds.Length
            +prepared.DeleteMessageIds.Length+prepared.AddedMessages.Length
            +prepared.DeleteFrameIds.Length+prepared.AddedFragments.Length+prepared.AddedOperands.Length+reconnectCount
            +prepared.MovedMessages.Length+prepared.DeleteNoteIds.Length+prepared.AddedNotes.Length+prepared.DeleteRefIds.Length+prepared.DeleteDestroyIds.Length
            +prepared.ShiftedShapes.Length+prepared.Renamed.Length+prepared.UnrelateIds.Length+prepared.SortChanged.Length+prepared.ShapeTexts.Length+prepared.DeleteEndIds.Length;
        if(retain && (touched==0 || !reconnectCommit))
            throw new InvalidOperationException("S231: 確定モードの対象外の差分があります。");
        string caseId=reconnectCommit?"UPDATE007":retain?"UPDATE006":"UPDATE005";
        LastMismatch="";
        var root=diagram.Model as IInteraction;
        var newShapes=prepared.AddedExecutions.Select(a=>a.ShapeId)
            .Concat(prepared.AddedParticipants.Select(a=>a.ShapeId))
            .Concat(prepared.AddedMessages.Select(a=>a.ShapeId))
            .Concat(prepared.AddedFragments.Select(a=>a.ShapeId))
            .Concat(prepared.AddedOperands.Select(a=>a.ShapeId))
            .Concat(prepared.AddedNotes.Where(a=>a.ShapeId!=null).Select(a=>a.ShapeId))
            // Written this run too, so the same rounding applies to them.
            .Concat(prepared.StretchedLifelines.Select(a=>a.ShapeId))
            .Concat(prepared.ShiftedShapes.Select(a=>a.ShapeId)).ToArray();
        var removedModels=prepared.DeleteIds.Concat(prepared.DeleteParticipantIds)
            .Concat(prepared.DeleteMessageIds).Concat(prepared.DeleteFrameIds).Concat(prepared.DeleteNoteIds).Concat(prepared.DeleteRefIds).Concat(prepared.DeleteDestroyIds)
            .Concat(prepared.DeleteEndIds).ToArray();
        var before=Read(root,diagram);before.Round(newShapes);string original=before.Signature();
        var expectedReconnect=before.Expected(prepared,plan,false);
        var expectedFinal=before.Expected(prepared,plan,true);
        var deletionOwners=before.DeletionOwners(prepared);
        expectedReconnect.Round(newShapes);expectedFinal.Round(newShapes);
        string rootId=root.Id,editorId=diagram.Id;
        Func<ISequenceDiagram> fresh=()=>{
            var model=project.GetModelById(rootId) as IInteraction;
            if(model==null)throw new InvalidOperationException("S230: 対象の図を取得できません。");
            return model.GetEditors().OfType<ISequenceDiagram>().Single(d=>d.Id==editorId);
        };
        string confirmation=retain
            ? "PlantUMLの差分を図へ反映します。照合が一致したときだけ確定し、一致しなければ取り消します。\n"
                +"メッセージやフラグメントを追加する更新は確定後にUndoできません（製品側の不具合）。取り消すときは保存せずに開き直してください。\n"
                +"自動保存はしません。実行しますか？"
            : "コピーのプロジェクトで実行してください。\n受信接続変更と実行区間削除を一時適用し、照合後に必ず取り消します。\n自動保存・変更の確定は行いません。試行しますか？";
        SequenceSyncRuntime.Lap("照合の用意");
        if(!SequenceSyncRuntime.Batch && !app.Window.UI.ShowConfirmDialog(confirmation,SequenceExperiment.Title))
            return caseId+": キャンセル / 図への変更なし";
        SequenceSyncRuntime.Lap("確認画面（操作待ち）");
        if(app.Workspace.CurrentProject==null || app.Workspace.CurrentProject.Id!=project.Id || (!SequenceSyncRuntime.Batch && (app.Workspace.CurrentEditor==null || app.Workspace.CurrentEditor.Id!=editorId))
            || Rounded(project,rootId,fresh,newShapes).Signature()!=original)
            throw new InvalidOperationException("S230: 確認中に対象の図が変化しました。");
        // The confirmation is modal, so nothing can be edited while it is up, and the SDK
        // state above has just been compared again. Exporting the whole unit a second time
        // only to compare the editor's display settings cost as long as the first export.
        SequenceExperiment.Write(Path.Combine(directory,"trial-before-sdk.json"),original);
        SequenceExperiment.Write(Path.Combine(directory,"trial-expected-sdk.json"),expectedFinal.Signature());
        string stage="トランザクション開始";
        var transaction=project.BeginUndoTransaction(false);
        if(transaction==null)throw new InvalidOperationException("S230: トランザクションを開始できません。");
        Action apply=delegate {
            stage=prepared.AddedExecutions.Length>0?"実行区間の追加と受信接続の変更":"受信接続の変更";
            foreach(var wire in prepared.AddedMessages)
                log.AppendLine("add message payload: model="+wire.ModelId+" shape="+wire.ShapeId+" Y="+wire.Y);
            foreach(var lane in prepared.AddedParticipants)
                log.AppendLine("add participant payload: model="+lane.ModelId+" shape="+lane.ShapeId+" X="+lane.X);
            foreach(var entry in prepared.AddedExecutions)
                log.AppendLine("add execution payload: model="+entry.ModelId+" shape="+entry.ShapeId
                    +" geometry(X,Y,Length)="+entry.Geometry+" relation order="+PumlBuild.Json(entry.RelationSources));
            foreach(var frame in prepared.AddedFragments)
                log.AppendLine("add fragment payload: model="+frame.ModelId+" shape="+frame.ShapeId
                    +" geometry(X,Y,Width,Height)="+frame.Geometry+" relation order="+PumlBuild.Json(frame.RelationSources));
            foreach(var branch in prepared.AddedOperands)
                log.AppendLine("add operand payload: model="+branch.ModelId+" shape="+branch.ShapeId
                    +" position="+branch.Position+" owner="+branch.OwnerId);
            foreach(var lane in prepared.StretchedLifelines)
                log.AppendLine("stretch lifeline payload: model="+lane.ModelId+" shape="+lane.ShapeId
                    +" timeline="+lane.Length);
            foreach(var move in prepared.ShiftedShapes)
                log.AppendLine("shift payload: model="+move.ModelId+" shape="+move.ShapeId
                    +" "+PumlBuild.Json(move.Keys)+"="+PumlBuild.Json(move.Values));
            log.AppendLine("created shape collections: "+(prepared.CreatedCollections.Length==0?"none"
                :string.Join(",",prepared.CreatedCollections)));
            Import(project,prepared.ReconnectJson,log);
            // A reference relation is taken off where a message or frame leaves a branch that
            // stays, where a ref stops covering a lane, and where a bar's reply changes.
            if(prepared.UnrelateIds.Length>0)
            {
                stage="関連の解除";
                var tree=SequenceMappedUpdate.Tree(project.GetModelById(rootId)).ToArray();
                foreach(string id in prepared.UnrelateIds)
                {
                    var found=tree.SelectMany(m=>m.GetRelationsWhere((r,f)=>r.Id==id)).FirstOrDefault();
                    if(found==null)throw new InvalidOperationException("S230: 解除する関連が見つかりません。");
                    // The relation itself is taken off: SourceField names the field on the target side,
                    // so it cannot be handed to the source's UnRelate.
                    log.AppendLine("unrelate: "+found.Metaclass.Id+" "+found.Source.Id+" -> "+found.Target.Id);
                    found.UnRelate();
                }
            }
            var connected=Rounded(project,rootId,fresh,newShapes);expectedReconnect.Loosen(prepared.LooseShapeIds,connected);
            Verify(expectedReconnect,connected,"接続変更後",log);
            log.AppendLine("receiver reconnection count: "+prepared.ReconnectCount
                +"; added executions: "+prepared.AddedExecutions.Length
                +"; added participants: "+prepared.AddedParticipants.Length+"; SDK state verified");
            stage="不要な要素の削除";
            using(project.SuspendModelVerification())foreach(string id in removedModels)project.GetModelById(id).Delete();
            // With nothing deleted, that editor is the one just imported, so importing it
            // again only touches the same collections a second time. Skipping it does not
            // stop the product crashing when undoing an added message: that happens with a
            // single import too.
            stage="削除後のエディタ反映";
            if(removedModels.Length>0)Import(project,prepared.EditorAfterDeleteJson,log);
            else log.AppendLine("post-delete editor import skipped: nothing was deleted");
            foreach(string id in removedModels){var m=project.GetModelById(id);if(m!=null && !m.IsDeleted)throw new InvalidOperationException("S230: 削除対象が残っています。");}
            var afterDelete=Rounded(project,rootId,fresh,newShapes);
            if(deletionOwners.Length>0)
                log.AppendLine("\f所有関連の順序 / 削除段階\n削除前(*が消える関連)\n"+before.OrderReport(deletionOwners,prepared)
                    +"\n削除後 期待\n"+expectedFinal.OrderReport(deletionOwners,prepared)
                    +"\n削除後 実測\n"+afterDelete.OrderReport(deletionOwners,prepared)+"\f");
            expectedFinal.Loosen(prepared.LooseShapeIds,afterDelete);
            Verify(expectedFinal,afterDelete,"削除後",log);
            log.AppendLine("trial execution deletion and SDK state: verified");
        };
        Action rollback=delegate {transaction.Rollback();};
        Action verifyRestored=delegate {Verify(before,Rounded(project,rootId,fresh,newShapes),"取消後",log);};
        // Explicit completion only; Dispose may attempt a second rollback.
        string summary;
        if(retain)
        {
            var completion=new SequenceCommitTrial();
            completion.Run(apply,delegate {stage="変更の確定";transaction.Commit();},rollback,verifyRestored);
            SequenceSyncRuntime.LastCommitted=completion.Committed;
            foreach(var error in new[]{completion.ApplyError,completion.CommitError,completion.RollbackError,completion.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            Refresh(app,log);
            string cycle="";
            if(completion.Committed)
            {
                // This command runs inside the host's own transaction, so ours is nested and
                // its content only reaches the undo stack after the handler returns. Both
                // CanUndo answers are false here by design, not by failure.
                stage="Undo";
                log.AppendLine("undo availability: project="+project.CanUndo+" workspace="+app.Workspace.CanUndo());
                if(app.Workspace.CanUndo())
                {
                    app.Workspace.Undo();Refresh(app,log);
                    bool undone=Matches(delegate {Verify(before,Rounded(project,rootId,fresh,newShapes),"Undo後",log);},log);
                    stage="Redo";
                    bool available=app.Workspace.CanRedo(),redone=false;
                    if(available)
                    {
                        app.Workspace.Redo();Refresh(app,log);
                        redone=Matches(delegate {Verify(expectedFinal,Rounded(project,rootId,fresh,newShapes),"Redo後",log);},log);
                    }
                    cycle="\nUndo照合: "+(undone?"一致":"不一致")+" / Redo照合: "+(redone?"一致":available?"不一致":"実行できません");
                    if(!undone || !redone)cycle+="\n保存せずコピーを開き直してください。";
                }
                else cycle="\nUndo/Redo: このコマンドの実行中は履歴へ積まれないため自動確認できません。手で1回ずつ確認してください。";
            }
            // A stop says why: the exception, and what the check found when it was a mismatch.
            if(!completion.Committed)
            {
                var cause=completion.ApplyError??completion.CommitError;
                if(cause!=null)cycle+="\n原因: "+cause.GetType().Name+": "+cause.Message+(cause.InnerException!=null?" / "+cause.InnerException.Message:"");
                if(LastMismatch.Length>0)cycle+="\n照合の内訳:\n"+(LastMismatch.Length>3000?LastMismatch.Substring(0,3000)+"…":LastMismatch);
            }
            summary="ケース: "+caseId+" / "+(completion.Committed?"構造更新・SDK照合・変更確定: 成功":"停止段階: "+stage)
                +cycle
                +(completion.Committed?"\n保存して開き直し、見た目と差分0件を確認してください。":"\n取消API: "+(completion.RollbackReturned?"正常終了":"失敗・未確認")+" / 復元照合: "+(completion.Restored?"一致":"未確認・不一致"))
                +(!completion.Committed && !completion.Restored?"\n保存せずコピーを開き直してください。":"")
                +"\nプロジェクトの自動保存: していません\nスタイル読戻し・保存再読込: 未検証\nこの結果と診断表示を撮影してください。";
        }
        else
        {
            var trial=new SequenceRollbackTrial();trial.Run(apply,rollback,verifyRestored);
            Refresh(app,log);
            foreach(var error in new[]{trial.ApplyError,trial.RollbackError,trial.VerifyError})if(error!=null)log.AppendLine(error.ToString());
            summary="ケース: UPDATE005 / "+(trial.Applied?"一時適用・SDK読戻し照合: 一致":"停止段階: "+stage)
                +"\n取消API: "+(trial.RollbackReturned?"正常終了":"失敗・未確認")+" / 復元照合: "+(trial.Restored?"一致":"未確認・不一致")
                +"\n変更の確定・プロジェクト保存: していません\nスタイルの適用後読戻し・保存再読込: 未検証"
                +(trial.Restored?"":"\n保存せずコピーを開き直してください。")+"\nこの結果と診断表示を撮影してください。";
        }
        SequenceSyncRuntime.Lap("反映と照合");
        summary+=SequenceSyncRuntime.TimingLine();
        summary+="\n今回の対象: 受信接続変更 "+reconnectCount+"件 / 実行区間削除 "+prepared.DeleteIds.Length+"件"
            +" / 実行区間追加 "+prepared.AddedExecutions.Length+"件"
            +" / 参加者追加 "+prepared.AddedParticipants.Length+"件 / 参加者削除 "+prepared.DeleteParticipantIds.Length+"件"
            +" / メッセージ削除 "+prepared.DeleteMessageIds.Length+"件"
            +" / メッセージ追加 "+prepared.AddedMessages.Length+"件"
            +" / フラグメント関連の削除 "+prepared.DeleteFrameIds.Length+"件 / Note削除 "+prepared.DeleteNoteIds.Length+"件 / Note・ref追加 "+prepared.AddedNotes.Length+"件 / ref削除 "+prepared.DeleteRefIds.Length+"件 / 本文変更 "+prepared.Renamed.Length+"件"
            +" / フラグメント追加 "+prepared.AddedFragments.Length+"件 / オペランド追加 "+prepared.AddedOperands.Length+"件"
            +" / タイムラインを伸ばした参加者 "+prepared.StretchedLifelines.Length+"件"
            +(prepared.InsertedMessageId.Length>0?" / 途中への挿入で下げた図形 "+prepared.ShiftedShapes.Length+"件":"")
            +(prepared.MovedMessages.Length>0?" / 枠で囲んだメッセージ "+prepared.MovedMessages.Length+"件 / 下げた図形 "+prepared.ShiftedShapes.Length+"件":"")
            // Committing an addition works, but the product crashes undoing a message or a
            // frame added this way, so say so here rather than leaving it to be discovered.
            // Undo after adding only bars or participants has not been tried.
            +(prepared.AddedMessages.Length>0 || prepared.AddedFragments.Length>0
                ?"\n注意: メッセージやフラグメントを追加した更新はUndoできません。Undoすると製品が停止します（製品側の不具合）。"
                    +"取り消すときは保存せずに開き直してください。"
                :prepared.AddedExecutions.Length>0 || prepared.AddedParticipants.Length>0
                ?"\n注意: 実行区間・参加者の追加をUndoできるかは未確認です。取り消すときは保存せずに開き直してください。":"");
        log.AppendLine(summary);
        try{SequenceExperiment.Write(Path.Combine(directory,"trial-result.txt"),summary+"\n"+log.ToString());}
        catch(Exception ex){log.AppendLine("trial result save: "+ex);summary+="\n試行結果の記録: 保存失敗";}
        return summary;
    }
}

// Runs a list of before/after PlantUML pairs on a copy of a project: each before becomes a
// new diagram beside the one that is open, the after is applied to it, and the result is
// compared again. Saving between steps is what lets the export run; this command is the
// only one that saves. Run again after reopening the project to recheck every diagram.
public static class SequenceBatch
{
    static string Line(string text,int max)
    {
        string flat=(text??"").Replace("\r","").Replace("\n"," / ");
        return flat.Length>max?flat.Substring(0,max)+"…":flat;
    }
    static ISequenceDiagram DiagramOf(IProject project,string root)
    {
        var model=project.GetModelById(root) as IInteraction;
        if(model==null)throw new InvalidOperationException("図のモデルが見つかりません: "+root);
        return model.GetEditors().OfType<ISequenceDiagram>().Single();
    }
    static void Save(IApplication app,IProject project)
    {
        if(!app.Workspace.SaveProject(project,false))throw new InvalidOperationException("プロジェクトを保存できません。");
    }
    // After a project is opened, a diagram nobody has shown yet reads back with no shapes
    // at all. Selecting its model in the navigator is tried first so the product loads it;
    // if it still reads empty, that is reported instead of counted as a difference.
    static ISequenceDiagram Loaded(IApplication app,IProject project,string root,StringBuilder detail)
    {
        var model=project.GetModelById(root) as IInteraction;
        if(model==null)throw new InvalidOperationException("図のモデルが見つかりません: "+root);
        try {app.Window.EditorPage.CurrentNavigator.Select(model,false);}
        catch(Exception ex){detail.AppendLine("ナビゲータで選択できません: "+ex.Message);}
        var open=app.Workspace.CurrentEditor as ISequenceDiagram;
        var diagram=open!=null && open.Model!=null && open.Model.Id==root?open:DiagramOf(project,root);
        if(!diagram.Lifelines.Any() && model.Lifelines.Any())
            throw new InvalidOperationException("図が読み込まれていません（モデルには参加者がありますが図形が0件です）。この図を開いてから再検証してください。");
        return diagram;
    }
    static int Compare(IApplication app,ISequenceDiagram diagram,string after)
    {
        SequenceSyncRuntime.BatchDiagram=diagram;SequenceSyncRuntime.BatchInput=after;
        SequenceSyncRuntime.Preview(app);
        return SequenceSyncRuntime.LastChanges;
    }
    // PlantUmlTool names an exported file after its diagram, with these characters replaced.
    static string FileNameOf(string name)
    {
        var invalid=new HashSet<char>(Path.GetInvalidFileNameChars());
        var b=new StringBuilder();
        foreach(char c in name??"")b.Append(invalid.Contains(c) || c==' '?'_':c);
        string result=b.ToString().Trim('_','.');
        if(result.Length==0)result="sequence";
        return result.Length>100?result.Substring(0,100):result;
    }
    // Every PlantUML file in a folder exported by PlantUmlTool, compared unedited with the
    // diagram it came from. Nothing is written or saved, so it can run on a real project.
    static void RoundTrip(IApplication app,IProject project,string folder)
    {
        string title=SequenceExperiment.Title;
        var diagrams=SequenceMappedUpdate.Tree(project.DesignModel).OfType<IInteraction>()
            .Where(m=>m.GetEditors().OfType<ISequenceDiagram>().Any())
            .GroupBy(m=>FileNameOf(m.Name),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.OrdinalIgnoreCase);
        var files=Directory.GetFiles(folder,"*.puml").OrderBy(f=>f,StringComparer.OrdinalIgnoreCase).Take(300).ToArray();
        var rows=new List<string>();var detail=new StringBuilder();var clock=System.Diagnostics.Stopwatch.StartNew();
        int passed=0;
        try
        {
            SequenceExperiment.BatchMode=true;SequenceSyncRuntime.Batch=true;
            foreach(string file in files)
            {
                string name=Path.GetFileNameWithoutExtension(file),result;
                IInteraction[] found;
                if(!diagrams.TryGetValue(name,out found))result="対応する図なし（名前が重複して出力名にハッシュが付いたものを含む）";
                else if(found.Length>1)result="同じ名前の図が"+found.Length+"枚あり、対応を決められません";
                else
                {
                    try
                    {
                        int changes=Compare(app,Loaded(app,project,found[0].Id,detail),file);
                        if(changes==0){result="差分0件";passed++;}
                        else
                        {
                            result=changes<0?"照合できず: "+Line(SequenceExperiment.Summary,160):"差分 "+changes+"件";
                            detail.AppendLine("■ "+name+"\n"+string.Join("\n",SequenceExperiment.Details.Split('\f').Where(page=>!page.StartsWith("接続の実測",StringComparison.Ordinal)))+"\n");
                        }
                    }
                    catch(Exception ex){result="停止: "+Line(ex.Message,160);detail.AppendLine("■ "+name+"\n"+ex+"\n");}
                }
                rows.Add(name+" | "+result);
            }
        }
        finally {SequenceExperiment.BatchMode=false;SequenceSyncRuntime.Batch=false;SequenceSyncRuntime.BatchDiagram=null;SequenceSyncRuntime.BatchInput=null;}
        SequenceExperiment.Summary="既存図の往復確認（書込みなし）: "+passed+"/"+files.Length+"件 差分0件 / "+(clock.ElapsedMilliseconds/1000)+"秒\n"+string.Join("\n",rows);
        SequenceExperiment.Details=SequenceExperiment.Summary+"\f"+detail;
        app.Window.UI.ShowInformationDialog(SequenceExperiment.Summary.Length>3000?SequenceExperiment.Summary.Substring(0,3000)+"\n…（続きは診断表示）":SequenceExperiment.Summary,title);
    }
    // Every sequence diagram of the project: exported the way PlantUmlTool exports it, read back
    // and compared with the diagram it came from. Nothing is written, applied or saved, so it
    // runs on a real project. A diagram that differs or stops is what the update would get wrong.
    public static void Sweep(IApplication app,IContext context)
    {
        string title=SequenceExperiment.Title;
        var project=app.Workspace.CurrentProject;
        if(project==null){app.Window.UI.ShowInformationDialog("プロジェクトを開いてから実行してください。",title);return;}
        // Diagrams never opened have no shapes to read unless inactive editors are loaded.
        context.ContextOption.EditorAccessMode=EditorAccessMode.GetInactiveValue;
        var diagrams=new List<ISequenceDiagram>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var model in new[]{(IModel)project}.Concat(((IModel)project).GetAllChildren().Cast<IModel>()))
        {
            if(model==null || model.IsDeleted || model.IsProxy)continue;
            foreach(var editor in model.GetEditors())
            {
                var d=editor as ISequenceDiagram;
                if(d==null || editor.EditorType!="SequenceDiagram" || !seen.Add(d.Id))continue;
                diagrams.Add(d);
            }
        }
        if(diagrams.Count==0){app.Window.UI.ShowInformationDialog("シーケンス図がありません。",title);return;}
        if(!app.Window.UI.ShowConfirmDialog("プロジェクトのシーケンス図 "+diagrams.Count+" 枚を、PlantUML 出力と同じ変換で書き出して元の図と比べます。\n"
            +"図・モデル・プロジェクトには一切書き込みません（保存もしません）。\n\nOK: 実行 / キャンセル: 中止",title))return;
        string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NextDesign.SequenceSync","sweep",DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var rows=new List<string>();var detail=new StringBuilder();var clock=System.Diagnostics.Stopwatch.StartNew();
        int passed=0,index=0;var tally=new Dictionary<string,int>();
        // What kinds of difference and stop occur, in how many diagrams: the lines of the
        // residual breakdown with line numbers and anonymous numbers taken out. No names.
        var patterns=new Dictionary<string,List<int>>(StringComparer.Ordinal);
        Action<string,int> note=(pattern,at)=>{List<int> seenIn;if(!patterns.TryGetValue(pattern,out seenIn))patterns[pattern]=seenIn=new List<int>();if(!seenIn.Contains(at))seenIn.Add(at);};
        Func<string,IEnumerable<string>> breakdown=text=>text.Replace("\f","\n").Split('\n').Select(l=>l.Trim())
            .Where(l=>Regex.IsMatch(l,@"^L\d+ "))
            .Select(l=>Regex.Replace(Regex.Replace(l,@"^L\d+ ",""),@"#\d+",""))
            .Distinct();
        try
        {
            SequenceExperiment.BatchMode=true;SequenceSyncRuntime.Batch=true;
            foreach(var d in diagrams)
            {
                index++;
                var owner=d.Model;
                string label=owner==null?d.Id:(string.IsNullOrEmpty(owner.ModelPath)?owner.Name:owner.ModelPath);
                string result,kind;
                try
                {
                    var interaction=owner as IInteraction;
                    if(!d.Lifelines.Any()){result="参加者なし（対象外）";kind="対象外";}
                    else if(interaction!=null && interaction.Lifelines.Count()!=d.Lifelines.Count()){result="図形を読めない（一度開いてから再実行）";kind="図を読めない";}
                    else
                    {
                        string uml=new SequencePlantUmlExporter(d,new PlantUmlOptions()).Export();
                        string file=Path.Combine(directory,index.ToString("D4")+".puml");
                        File.WriteAllText(file,uml,new UTF8Encoding(false));
                        SequenceSyncRuntime.BatchDiagram=d;SequenceSyncRuntime.BatchInput=file;
                        SequenceSyncRuntime.Preview(app);
                        int changes=SequenceSyncRuntime.LastChanges;
                        if(changes==0){result="差分0件";kind="一致";passed++;}
                        else if(changes>0)
                        {
                            result="差分 "+changes+"件";kind="差分あり";
                            foreach(string pattern in breakdown(SequenceExperiment.Details))note(pattern,index);
                            detail.AppendLine("■ "+index+" "+label+"（"+Path.GetFileName(file)+"）\n"+string.Join("\n",SequenceExperiment.Details.Split('\f').Where(page=>!page.StartsWith("接続の実測",StringComparison.Ordinal) && !page.StartsWith("実行区間とメッセージの縦位置",StringComparison.Ordinal)))+"\n");
                        }
                        else
                        {
                            result="読取りで停止: "+Line(SequenceExperiment.Summary,200);kind="停止";
                            note("停止: "+Regex.Replace(Line(SequenceExperiment.Summary,60),@"[0-9a-f]{8}-[0-9a-f-]{27}","<id>"),index);
                            detail.AppendLine("■ "+index+" "+label+"（"+Path.GetFileName(file)+"）\n"+SequenceExperiment.Details+"\n");
                        }
                    }
                }
                catch(Exception ex){result="停止: "+Line(ex.Message,200);kind="停止";detail.AppendLine("■ "+index+" "+label+"\n"+ex+"\n");note("停止: "+Regex.Replace(Line(ex.Message,60),@"[0-9a-f]{8}-[0-9a-f-]{27}","<id>"),index);}
                int n;tally.TryGetValue(kind,out n);tally[kind]=n+1;
                rows.Add(index+"\t"+label+"\t"+result);
            }
        }
        finally {SequenceExperiment.BatchMode=false;SequenceSyncRuntime.Batch=false;SequenceSyncRuntime.BatchDiagram=null;SequenceSyncRuntime.BatchInput=null;}
        File.WriteAllText(Path.Combine(directory,"result.tsv"),string.Join("\n",rows)+"\n",new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory,"detail.txt"),detail.ToString(),new UTF8Encoding(false));
        var ranked=patterns.OrderByDescending(p=>p.Value.Count).ThenBy(p=>p.Key,StringComparer.Ordinal).ToList();
        string kinds="ずれの種類（図の枚数順・図の名前なし。例の番号は result.tsv の番号）\n"
            +string.Join("\n",ranked.Take(40).Select(p=>p.Value.Count+"枚: "+p.Key+"  例 "+string.Join(",",p.Value.Take(3))));
        File.WriteAllText(Path.Combine(directory,"kinds.txt"),kinds+"\n\n"+string.Join("\n",ranked.Select(p=>p.Value.Count+"\t"+p.Key+"\t"+string.Join(",",p.Value))),new UTF8Encoding(false));
        string summary="全図チェック（書込みなし）: "+diagrams.Count+"枚 / "+(clock.ElapsedMilliseconds/1000)+"秒\n"
            +string.Join(" / ",tally.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+" "+p.Value))+"\n"
            +"結果: "+directory+"\n\n"
            +string.Join("\n",rows.Where(r=>!r.EndsWith("\t差分0件",StringComparison.Ordinal)).Take(40).Select(r=>r.Replace('\t',' ')));
        SequenceExperiment.Summary=summary;
        SequenceExperiment.Details=kinds+"\f"+summary+"\f"+detail;
        string head="全図チェック（書込みなし）: "+diagrams.Count+"枚 / "+(clock.ElapsedMilliseconds/1000)+"秒 / "
            +string.Join(" / ",tally.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+" "+p.Value))+"\n\n"+kinds;
        app.Window.UI.ShowInformationDialog(head.Length>3000?head.Substring(0,3000)+"\n…（続きは結果フォルダの kinds.txt）":head,title);
    }
    public static void Run(IApplication app)
    {
        string title=SequenceExperiment.Title;
        var project=app.Workspace.CurrentProject;
        if(project==null || !(app.Workspace.CurrentEditor is ISequenceDiagram))
        {app.Window.UI.ShowInformationDialog("シーケンス図を開いてから実行してください。新しい図はその図と同じ親に作ります。",title);return;}
        string list=app.Window.UI.ShowOpenFileDialog("シナリオ一覧、または出力済み PlantUML のどれか1つ",
            "シナリオ一覧・出力済み PlantUML (*.txt;*.puml)|*.txt;*.puml");
        if(string.IsNullOrEmpty(list))return;
        if(string.Equals(Path.GetExtension(list),".puml",StringComparison.OrdinalIgnoreCase)){RoundTrip(app,project,Path.GetDirectoryName(list));return;}
        string folder=Path.GetDirectoryName(list),resultPath=Path.ChangeExtension(list,".result.tsv");
        var scenarios=new List<string[]>();
        foreach(var raw in File.ReadAllLines(list,new UTF8Encoding(false,true)))
        {
            string line=raw.Trim();
            if(line.Length==0 || line.StartsWith("#",StringComparison.Ordinal))continue;
            var parts=line.Split('|').Select(p=>p.Trim()).ToArray();
            if(parts.Length!=3){app.Window.UI.ShowInformationDialog("シナリオの行の形が違います（名前 | before | after）: "+line,title);return;}
            scenarios.Add(new[]{parts[0],Path.Combine(folder,parts[1]),Path.Combine(folder,parts[2])});
        }
        bool apply=app.Window.UI.ShowConfirmDialog("シナリオ "+scenarios.Count+"件。\n"
            +"「OK」: 実験用のコピーのプロジェクトで、各シナリオの図を新しく作り、反映して照合します。途中でプロジェクトを自動保存します。\n"
            +"「キャンセル」: 前回の実行で作った図を、保存せずに再検証します（開き直した後に使います）。",title);
        var rows=new List<string>();var detail=new StringBuilder();var created=new List<string>();
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var previous=new Dictionary<string,string>();
        if(!apply)
        {
            if(!File.Exists(resultPath)){app.Window.UI.ShowInformationDialog("前回の実行結果がありません: "+resultPath,title);return;}
            foreach(var row in File.ReadAllLines(resultPath,new UTF8Encoding(false)))
            {var cells=row.Split('\t');if(cells.Length>=2 && cells[1].Length>0)previous[cells[0]]=cells[1];}
        }
        try
        {
            SequenceExperiment.BatchMode=true;SequenceSyncRuntime.Batch=true;
            var roots=new Dictionary<string,string>();
            if(apply)
            {
                // Every diagram first, then one save: importing does not need the export,
                // so each scenario then costs one export and one save instead of two saves.
                var importClock=System.Diagnostics.Stopwatch.StartNew();
                foreach(var s in scenarios)
                {
                    SequenceExperiment.BatchInput=s[1];SequenceExperiment.LastRoot=null;
                    try{SequenceExperiment.Run(app,true);}catch(Exception ex){detail.AppendLine("■ "+s[0]+" 取込\n"+ex+"\n");}
                    if(SequenceExperiment.LastRoot!=null)roots[s[0]]=SequenceExperiment.LastRoot;
                    else detail.AppendLine("■ "+s[0]+" 取込: "+SequenceExperiment.Summary+"\n");
                }
                long imported=importClock.ElapsedMilliseconds;
                var saveClock=System.Diagnostics.Stopwatch.StartNew();
                Save(app,project);
                long saved=saveClock.ElapsedMilliseconds;
                // One export for every scenario: each diagram is cut out of it in its turn,
                // so no save is needed between them.
                var exportClock=System.Diagnostics.Stopwatch.StartNew();
                var host=(app.Workspace.CurrentEditor as ISequenceDiagram).Model;
                string file=Path.Combine(Path.GetTempPath(),"SequenceBatch-"+Guid.NewGuid().ToString("N")+".nmdl");
                try
                {
                    if(!SequenceSyncRuntime.ForceSdkSnapshot)
                    {
                        project.UnitManager.ExportModelUnit(host.ModelUnit,file);
                        SequenceEditorCapture.BatchSource=SequenceJson.Parse(File.ReadAllText(file,new UTF8Encoding(false,true)));
                    }
                }
                finally{try{File.Delete(file);}catch(Exception){}}
                foreach(var pair in roots)
                {
                    var made=project.GetModelById(pair.Value);
                    if(made==null || made.ModelUnit==null || !ReferenceEquals(made.ModelUnit,host.ModelUnit) && made.ModelUnit.TopElementId!=host.ModelUnit.TopElementId)
                        throw new InvalidOperationException("作った図が開いている図と別のモデルユニットにあります: "+pair.Key);
                }
                rows.Add("（取込 "+roots.Count+"/"+scenarios.Count+"件 "+(imported/1000)+"秒 / 保存 "+(saved/1000)+"秒 / 書き出し "+(exportClock.ElapsedMilliseconds/1000)+"秒）");
            }
            else roots=previous;
            int failedInRow=0;bool anyPassed=false;
            foreach(var s in scenarios)
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();
                string root,result,timing="";bool failed=false;
                try
                {
                    if(!roots.TryGetValue(s[0],out root))throw new InvalidOperationException(apply?"取込に失敗しました（診断表示）。":"前回の実行で図が作られていません。");
                    if(apply)
                    {
                        SequenceSyncRuntime.BatchDiagram=DiagramOf(project,root);SequenceSyncRuntime.BatchInput=s[2];
                        SequenceSyncRuntime.Preview(app,true,true,true,true);
                        bool committed=SequenceSyncRuntime.LastCommitted;
                        string reasons=SequenceSyncRuntime.LastReasons,summary=SequenceExperiment.Summary;
                        timing=" / 反映 "+(watch.ElapsedMilliseconds/1000)+"秒";
                        detail.AppendLine("■ "+s[0]+"\n"+summary+"\n");
                        // The whole summary goes on: the row picks the mismatch out of it.
                        if(!committed)throw new InvalidOperationException("反映: "+(reasons.Length>0?reasons:summary));
                    }
                    int changes=Compare(app,apply?DiagramOf(project,root):Loaded(app,project,root,detail),s[2]);
                    // What the comparison found goes to the details, so a difference can be read.
                    if(changes!=0)detail.AppendLine("■ "+s[0]+" 比較\n"+string.Join("\n",SequenceExperiment.Details.Split('\f').Where(page=>!page.StartsWith("接続の実測",StringComparison.Ordinal)))+"\n");
                    // Applied but still different is that scenario's own problem, not the batch's.
                    failed=changes<0;
                    result=changes==0?"成功":changes<0?"照合できず: "+Line(SequenceExperiment.Summary,160):"差分 "+changes+"件";
                    // Applying the same input again has to find nothing to do and write nothing.
                    if(apply && changes==0)
                    {
                        var diagram=DiagramOf(project,root);
                        string shapesBefore=string.Join("|",diagram.Shapes.Select(sh=>sh.Id).OrderBy(x=>x,StringComparer.Ordinal));
                        SequenceSyncRuntime.BatchDiagram=diagram;SequenceSyncRuntime.BatchInput=s[2];
                        SequenceSyncRuntime.Preview(app,true,true,true,true);
                        bool again=SequenceSyncRuntime.LastCommitted;
                        string shapesAfter=string.Join("|",DiagramOf(project,root).Shapes.Select(sh=>sh.Id).OrderBy(x=>x,StringComparer.Ordinal));
                        if(again || SequenceSyncRuntime.LastChanges!=0 || shapesBefore!=shapesAfter)
                        {
                            result="2回目の反映で変化: 差分 "+SequenceSyncRuntime.LastChanges+"件 / 確定="+again;
                            detail.AppendLine("■ "+s[0]+" 2回目の反映\n"+SequenceExperiment.Summary+"\n");
                        }
                        else result+="（2回目: 変化なし）";
                    }
                }
                catch(Exception ex)
                {
                    failed=true;
                    // A mismatch says where in its breakdown; that part is what the row has room for.
                    int at=ex.Message.IndexOf("relation=",StringComparison.Ordinal);
                    if(at<0)at=ex.Message.IndexOf("shape=",StringComparison.Ordinal);
                    result="停止: "+(at>=0?Line(ex.Message.Substring(0,Math.Min(80,ex.Message.Length)),80)+" … "+Line(ex.Message.Substring(at),420):Line(ex.Message,200));
                    detail.AppendLine("■ "+s[0]+"\n"+ex+"\n");
                }
                rows.Add(s[0]+" | "+result+" | "+(watch.ElapsedMilliseconds/1000)+"秒"+timing);
                string kept;roots.TryGetValue(s[0],out kept);created.Add(s[0]+"\t"+(kept??""));
                // Two failures in a row almost always share a cause in the batch itself;
                // running the rest would only repeat it.
                failedInRow=failed?failedInRow+1:0;
                // Only a batch that has not got one scenario through is broken as a whole; after
                // that, a failure is that scenario's own and the rest still run.
                if(!failed)anyPassed=true;
                if(apply && !anyPassed && failedInRow>=2 && scenarios.IndexOf(s)<scenarios.Count-1)
                {rows.Add("2件続けて失敗したため、残り "+(scenarios.Count-1-scenarios.IndexOf(s))+"件を実行せずに中断しました。");break;}
            }
        }
        catch(Exception ex){rows.Add("中断: "+Line(ex.Message,200));detail.AppendLine(ex.ToString());}
        finally {SequenceEditorCapture.BatchSource=null;SequenceSyncRuntime.ForceSdkSnapshot=false;SequenceExperiment.BatchMode=false;SequenceExperiment.BatchInput=null;SequenceSyncRuntime.Batch=false;SequenceSyncRuntime.BatchDiagram=null;SequenceSyncRuntime.BatchInput=null;}
        if(apply)
        {
            try{File.WriteAllLines(resultPath,created,new UTF8Encoding(false));}
            catch(Exception ex){rows.Add("前回結果の保存に失敗: "+ex.Message);}
            // Saved once at the end, with what the product says before and after, since the
            // last run's changes did not come back after reopening.
            try
            {
                bool dirtyBefore=project.HasUnsavedChanges();
                var saveClock=System.Diagnostics.Stopwatch.StartNew();
                Save(app,project);
                rows.Add("（最後の保存 "+(saveClock.ElapsedMilliseconds/1000)+"秒 / 保存前の未保存変更="+dirtyBefore+" 保存後="+project.HasUnsavedChanges()+"）");
            }
            catch(Exception ex){rows.Add("最後の保存に失敗: "+ex.Message);}
        }
        int passed=rows.Count(r=>r.Contains(" | 成功 | ") || r.Contains(" | 成功（2回目: 変化なし） | "));
        SequenceExperiment.Summary=(apply?"シナリオ一括検証（反映）":"シナリオ一括検証（再検証）")+": "+passed+"/"+scenarios.Count+"件成功 / "+(clock.ElapsedMilliseconds/1000)+"秒\n"
            +string.Join("\n",rows)+(apply?"\n\nプロジェクトを閉じて開き直し、もう一度このボタンで「キャンセル」（再検証）を選んでください。":"");
        SequenceExperiment.Details=SequenceExperiment.Summary+"\f"+detail;
        app.Window.UI.ShowInformationDialog(SequenceExperiment.Summary,title);
    }
}

// Research for updating without saving. The update takes the diagram's snapshot through
// ExportModelUnit, which refuses while the project has unsaved changes. This rebuilds that
// snapshot from what the SDK reads live and compares it, key by key, with the exported one:
// what the SDK cannot give is what an unsaved update would have to do without. Read only.
public static class SequenceSnapshotProbe
{
    static string Num(double v){return v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
    static SequenceJson Value(object v)
    {
        if(v==null)return null;
        if(v is bool)return SequenceJson.Parse((bool)v?"true":"false");
        if(v is int || v is long || v is short)return SequenceJson.Parse(Convert.ToInt64(v).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(v is double || v is float || v is decimal)return SequenceJson.Parse(Num(Convert.ToDouble(v)));
        if(v is string)return SequenceJson.Parse(SequencePayload.Q((string)v));
        var model=v as IModel;if(model!=null)return SequenceJson.Parse(SequencePayload.Q(model.Id));
        return SequenceJson.Parse(SequencePayload.Q(v.ToString()));
    }
    // A value as it can be shown on screen: numbers and literals as they are, text only by its
    // form, so no names or bodies appear.
    static string Shape(SequenceJson v)
    {
        if(v==null)return "なし";
        if(v.Raw==null)return v.Items!=null?"配列"+v.Items.Count:"オブジェクト";
        if(!v.Raw.StartsWith("\""))return v.Raw;
        string s=v.StringValue();
        return "文字"+s.Length+(s.Contains("\r\n")?"・CRLF":s.Contains("\n")?"・LF":"")+(s!=s.Trim()?"・前後空白":"")+(s.Contains("  ")?"・連続空白":"");
    }
    static SequenceJson Obj(){return new SequenceJson{Properties=new Dictionary<string,SequenceJson>(StringComparer.Ordinal)};}
    static void Put(SequenceJson o,string key,object v){var j=Value(v);if(j!=null)o.Properties[key]=j;}
    // What the SDK gives of each model, relation and shape.
    public static Dictionary<string,SequenceJson> Synthesize(IInteraction root,ISequenceDiagram diagram,StringBuilder log)
    {
        var all=new Dictionary<string,SequenceJson>(StringComparer.Ordinal);
        foreach(var m in SequenceMappedUpdate.Tree(root))
        {
            var e=Obj();Put(e,"Id",m.Id);Put(e,"MetamodelId",m.Metaclass==null?null:m.Metaclass.Id);Put(e,"Name",m.Name);
            if(m.Metaclass!=null)
            {
                var c=Obj();Put(c,"Id",m.Metaclass.Id);Put(c,"FullName",m.Metaclass.FullName);Put(c,"Name",m.Metaclass.Name);Put(c,"ClassName",m.ClassName);
                e.Properties["(候補)"]=c;
            }
            var fields=Obj();
            if(m.Metaclass!=null)
                foreach(var f in m.Metaclass.GetFields().Cast<IField>().Where(f=>f.RelationshipClass==null))
                {
                    try {Put(fields,f.Name,m.GetField(f.Name));}
                    catch(Exception ex){log.AppendLine("field "+f.Name+": "+ex.GetType().Name);}
                }
            e.Properties["Fields"]=fields;
            all["E:"+m.Id]=e;
            foreach(var r in m.GetRelationsWhere((relation,field)=>true))
            {
                if(all.ContainsKey("R:"+r.Id))continue;
                var o=Obj();Put(o,"Id",r.Id);Put(o,"MetamodelId",r.Metaclass==null?null:r.Metaclass.Id);
                Put(o,"SourceId",r.Source.Id);Put(o,"TargetId",r.Target.Id);Put(o,"SourceIndex",r.SourceIndex);Put(o,"TargetIndex",r.TargetIndex);
                var c=Obj();
                if(r.Metaclass!=null){Put(c,"Id",r.Metaclass.Id);Put(c,"FullName",r.Metaclass.FullName);Put(c,"Name",r.Metaclass.Name);}
                Put(c,"Embed",r.IsEmbedded?"Embed":"Ref");Put(c,"IsDerivation",r.IsDerivation);
                Put(c,"TargetIndexIfField",r.TargetField==null?-1:r.TargetIndex);Put(c,"TargetIndexIfUpper",r.TargetField==null || r.TargetField.UpperBound==1?-1:r.TargetIndex);
                Put(c,"TargetField",r.TargetField==null?"none":"field");
                o.Properties["(候補)"]=c;
                all["R:"+r.Id]=o;
            }
        }
        foreach(var sh in diagram.Shapes)
        {
            var o=Obj();Put(o,"Id",sh.Id);Put(o,"ModelId",sh.ModelId);
            var node=sh as ISequenceNodeShape;
            if(node!=null){Put(o,"X",node.LocationX);Put(o,"Y",node.LocationY);Put(o,"Width",node.Width);Put(o,"Height",node.Height);}
            var bar=sh as IExecutionSpecificationShape;if(bar!=null)Put(o,"Length",bar.Length);
            var wire=sh as IMessageShape;if(wire!=null){Put(o,"SourceY",wire.SourceY);Put(o,"TargetY",wire.TargetY);Put(o,"SelfloopBendsX",wire.SelfloopBendsX);}
            var branch=sh as IOperandShape;if(branch!=null)Put(o,"Position",branch.Position);
            var lane=sh as ILifelineShape;if(lane!=null)Put(o,"LaneLength",lane.TimelineLength);
            IShapeStyle style=null;
            try {style=sh.Style;} catch(Exception ex){log.AppendLine("style: "+ex.GetType().Name);}
            if(style!=null)
            {
                var st=Obj();
                foreach(var read in new KeyValuePair<string,Func<object>>[]{
                    new KeyValuePair<string,Func<object>>("BackColor",()=>style.BackColor),new KeyValuePair<string,Func<object>>("BorderColor",()=>style.BorderColor),
                    new KeyValuePair<string,Func<object>>("BorderStyle",()=>style.BorderStyle),new KeyValuePair<string,Func<object>>("BorderThickness",()=>style.BorderThickness),
                    new KeyValuePair<string,Func<object>>("ForeColor",()=>style.ForeColor),new KeyValuePair<string,Func<object>>("QuickStyle",()=>style.QuickStyle)})
                {
                    try {Put(st,read.Key,read.Value());} catch(Exception){}
                }
                o.Properties["Style"]=st;
            }
            all["S:"+sh.Id]=o;
        }
        return all;
    }
    static bool Same(SequenceJson a,SequenceJson b)
    {
        if(a==null || b==null)return a==b;
        if(a.Raw!=null && b.Raw!=null)
        {
            double x,y;var f=System.Globalization.NumberStyles.Float;var c=System.Globalization.CultureInfo.InvariantCulture;
            string ra=a.Raw.StartsWith("\"")?a.StringValue():a.Raw,rb=b.Raw.StartsWith("\"")?b.StringValue():b.Raw;
            if(double.TryParse(ra,f,c,out x) && double.TryParse(rb,f,c,out y))return Math.Abs(x-y)<=0.0000011;
            return a.Raw==b.Raw || string.Equals(ra,rb,StringComparison.OrdinalIgnoreCase);
        }
        return a.ToJsonString()==b.ToJsonString();
    }
    // Every object with an Id in the exported snapshot, keyed as Synthesize keys its own.
    static void Collect(SequenceJson node,string kind,Dictionary<string,SequenceJson> into)
    {
        if(node==null)return;
        if(node.Items!=null){foreach(var i in node.Items)Collect(i,kind,into);return;}
        if(node.Properties==null)return;
        if(node["Id"]!=null && node["Id"].Raw!=null && node["Id"].Raw.StartsWith("\""))
        {
            string id=node["Id"].StringValue();
            if(kind=="S" && node["ModelId"]!=null)into["S:"+id]=node;
            else if(kind!="S")into[kind+":"+id]=node;
        }
        if(kind=="S")foreach(var p in node.Properties.Values)Collect(p,kind,into);
    }
    public static void Run(IApplication app)
    {
        string title=SequenceExperiment.Title;var log=new StringBuilder();var report=new StringBuilder();
        try
        {
            var project=app.Workspace.CurrentProject;
            var diagram=app.Workspace.CurrentEditor as ISequenceDiagram;
            if(project==null || diagram==null){app.Window.UI.ShowInformationDialog("調べるシーケンス図を開いてください。",title);return;}
            var root=diagram.Model as IInteraction;
            string exported=null;
            try {SequenceEditorCapture.Read(project,root,diagram,log,delegate(string v){exported=v;});}
            catch(Exception ex){app.Window.UI.ShowInformationDialog("比べる元の写しが取れません。保存してから実行してください（調査のための比較元です）。\n"+ex.Message,title);return;}
            var data=SequenceJson.Parse(exported);
            var real=new Dictionary<string,SequenceJson>(StringComparer.Ordinal);
            Collect(data["Entities"],"E",real);Collect(data["Relations"],"R",real);
            var editor=data["Editors"].Items.FirstOrDefault(v=>v["Id"]!=null && v["Id"].StringValue()==diagram.Id);
            foreach(var p in editor.Properties.Values)Collect(p,"S",real);
            var made=Synthesize(root,diagram,log);
            // Per kind and key: how often it is missing from what the SDK gives, and how often it differs.
            var missing=new Dictionary<string,int>();var differs=new Dictionary<string,int>();var total=new Dictionary<string,int>();
            Action<Dictionary<string,int>,string> add=(d,k)=>{int n;d.TryGetValue(k,out n);d[k]=n+1;};
            var samples=new StringBuilder();var shown=new Dictionary<string,List<string>>(StringComparer.Ordinal);
            foreach(var pair in real)
            {
                string kind=pair.Key.Substring(0,1);
                SequenceJson mine;made.TryGetValue(pair.Key,out mine);
                if(mine==null){add(missing,kind+" (対象ごと)");continue;}
                Action<SequenceJson,SequenceJson,string> walk=null;
                walk=(a,b,path)=>{
                    foreach(var p in a.Properties)
                    {
                        string key=path+p.Key;add(total,kind+" "+key);
                        var other=b==null?null:b[p.Key];
                        if(p.Value!=null && p.Value.Properties!=null){walk(p.Value,other!=null && other.Properties!=null?other:null,key+".");continue;}
                        if(other==null){add(missing,kind+" "+key);continue;}
                        if(!Same(p.Value,other))
                        {
                            add(differs,kind+" "+key);if(samples.Length<20000)samples.AppendLine(kind+" "+key+" export="+p.Value.ToJsonString()+" sdk="+other.ToJsonString());
                            List<string> seen;if(!shown.TryGetValue(kind+" "+key,out seen))shown[kind+" "+key]=seen=new List<string>();
                            if(seen.Count<3)seen.Add(Shape(p.Value)+" / "+Shape(other));
                        }
                    }
                };
                walk(pair.Value,mine,"");
            }
            foreach(var pair in made.Where(p=>!real.ContainsKey(p.Key)))add(missing,pair.Key.Substring(0,1)+" (SDKだけにある対象)");
            // For each key the SDK does not give as such, which derived candidate equals the export.
            var matches=new Dictionary<string,int>();
            foreach(var pair in real)
            {
                SequenceJson mine;if(!made.TryGetValue(pair.Key,out mine) || mine["(候補)"]==null)continue;
                string kind=pair.Key.Substring(0,1);
                foreach(string key in new[]{"Metamodel","EntityType","RelationType","IsDerivation","TargetIndex"})
                {
                    var value=pair.Value[key];if(value==null)continue;
                    foreach(var cand in mine["(候補)"].Properties)
                        if(Same(value,cand.Value))add(matches,kind+" "+key+" = "+cand.Key);
                    if(key=="TargetIndex")add(matches,kind+" TargetIndex 相手フィールド="+mine["(候補)"]["TargetField"].StringValue()+" 写し="+value.Raw);
                }
            }
            // Editor-level values other than shapes.
            foreach(var p in editor.Properties.Where(p=>p.Value!=null && p.Value.Items==null && p.Value.Properties==null))add(total,"V "+p.Key);
            var keys=total.Keys.Union(missing.Keys).Union(differs.Keys).OrderBy(k=>k,StringComparer.Ordinal);
            report.AppendLine("保存なし反映の調査（読み取りのみ）: 図形 "+diagram.Shapes.Count()+" / モデル "+real.Keys.Count(k=>k.StartsWith("E:"))+" / 関連 "+real.Keys.Count(k=>k.StartsWith("R:")));
            report.AppendLine("E=モデル R=関連 S=図形 V=エディタ自体の値。 欠け=SDKから作れない 不一致=値が違う");
            foreach(string k in keys)
            {
                int t,m,d;total.TryGetValue(k,out t);missing.TryGetValue(k,out m);differs.TryGetValue(k,out d);
                if(m>0 || d>0 || k.StartsWith("V "))report.AppendLine(k+": 全"+t+" 欠け"+m+" 不一致"+d);
                List<string> seen;if(shown.TryGetValue(k,out seen))report.AppendLine("  例（写し / SDK）: "+string.Join(" ; ",seen));
            }
            report.AppendLine("（全件一致のキーは省略）");
            // The snapshot an unsaved update would use: every shape must sit where the export
            // keeps it, or re-importing the editor would drop it.
            {
                string built=SequenceSnapshotBuilder.Build(project,root,diagram,SequenceSnapshotBuilder.Schema(project),log);
                var builtEditor=SequenceJson.Parse(built)["Editors"].Items.Single();
                var want=SequenceSnapshotBuilder.Places(editor);var got=SequenceSnapshotBuilder.Places(builtEditor);
                var placeTally=new SortedDictionary<string,int>(StringComparer.Ordinal);
                foreach(var pair in want)
                {
                    string at;got.TryGetValue(pair.Key,out at);
                    string key=pair.Value+(at==null?" → 組み立てに無い":at==pair.Value?" 一致":" → "+at);
                    int n;placeTally.TryGetValue(key,out n);placeTally[key]=n+1;
                }
                foreach(var pair in got.Where(p=>!want.ContainsKey(p.Key))){string key="組み立てだけ: "+pair.Value;int n;placeTally.TryGetValue(key,out n);placeTally[key]=n+1;}
                report.AppendLine("図形の置き場所（写し → 組み立て）:");
                foreach(var pair in placeTally)report.AppendLine("  "+pair.Key+": "+pair.Value);
                var editorKeys=editor.Properties.Where(p=>p.Value!=null && p.Value.Raw!=null).Select(p=>p.Key+"="+(p.Key=="MetamodelId" || p.Key=="ViewType"?p.Value.Raw:"…")).ToList();
                report.AppendLine("エディタの値: "+string.Join(", ",editorKeys)+" / 組み立て: "+string.Join(", ",builtEditor.Properties.Where(p=>p.Value!=null && p.Value.Raw!=null).Select(p=>p.Key)));
                var builtData=SequenceJson.Parse(built);
                var builtEntities=builtData["Entities"].Items.ToDictionary(x=>x["Id"].StringValue());
                int typeSame=0,typeDiff=0;var typeExamples=new List<string>();
                foreach(var ent in data["Entities"].Items)
                {
                    SequenceJson mine;if(!builtEntities.TryGetValue(ent["Id"].StringValue(),out mine))continue;
                    if(ent["EntityType"]==null)continue;
                    if(Same(ent["EntityType"],mine["EntityType"]))typeSame++;
                    else {typeDiff++;if(typeExamples.Count<5)typeExamples.Add(ent["EntityType"].StringValue()+"/"+mine["EntityType"].StringValue());}
                }
                report.AppendLine("EntityType（図形の種類から導出）: 一致 "+typeSame+" 不一致 "+typeDiff+(typeExamples.Count>0?" 例 "+string.Join(" ; ",typeExamples):""));
            }
            report.AppendLine("導出の候補と写しの一致数:");
            foreach(var pair in matches.OrderBy(p=>p.Key,StringComparer.Ordinal))report.AppendLine("  "+pair.Key+": "+pair.Value);
            string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NextDesign.SequenceSync","snapshot-probe");
            Directory.CreateDirectory(directory);
            string stem=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            File.WriteAllText(stem+"-report.txt",report+"\n値の例（名前等を含む・ローカルのみ）\n"+samples+"\n"+log,new UTF8Encoding(false));
            File.WriteAllText(stem+"-export.json",exported,new UTF8Encoding(false));
            report.AppendLine("詳細: "+stem+"-report.txt");
        }
        catch(Exception ex){report.AppendLine("調査を完了できません: "+ex.Message);log.AppendLine(ex.ToString());}
        SequenceExperiment.Summary=report.ToString();SequenceExperiment.Details=report+"\f"+log;
        app.Window.UI.ShowInformationDialog(report.Length>3000?report.ToString().Substring(0,3000):report.ToString(),title);
    }
}

// Research, second part: does an editor import keep what it is not given? Takes the diagram's
// exported snapshot, removes from every shape the values the SDK cannot read, imports that
// editor back, saves, exports again and counts, per key, what stayed, changed or went. It
// writes to and saves the project, so it asks for a copy of the project first.
public static class SequenceOmissionProbe
{
    static readonly string[] Unreadable={"Style","LeftPadding","IsRightAtFrame"};
    public static void Run(IApplication app)
    {
        string title=SequenceExperiment.Title;var log=new StringBuilder();var report=new StringBuilder();
        try
        {
            var project=app.Workspace.CurrentProject;
            var diagram=app.Workspace.CurrentEditor as ISequenceDiagram;
            if(project==null || diagram==null){app.Window.UI.ShowInformationDialog("調べるシーケンス図を開いてください。",title);return;}
            if(!app.Window.UI.ShowConfirmDialog("【コピーのプロジェクトで実行してください】\n開いている図の写しから、SDK で読めない項目（Style・LeftPadding・IsRightAtFrame）を消して図に書き戻し、保存してから、項目が残ったかを調べます。\n図の見た目（色・形）が変わる可能性があります。\n\nOK: 実行 / キャンセル: 中止",title))return;
            var root=diagram.Model as IInteraction;
            string before=null;
            SequenceEditorCapture.Read(project,root,diagram,log,delegate(string v){before=v;});
            var data=SequenceJson.Parse(before);
            var editor=data["Editors"].Items.First(v=>v["Id"]!=null && v["Id"].StringValue()==diagram.Id);
            var original=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
            int removed=0;
            Action<SequenceJson> strip=null;
            strip=node=>{
                if(node==null)return;
                if(node.Items!=null){foreach(var i in node.Items)strip(i);return;}
                if(node.Properties==null)return;
                if(node["Id"]!=null && node["ModelId"]!=null && node["Id"].Raw.StartsWith("\""))
                {
                    var kept=new Dictionary<string,string>(StringComparer.Ordinal);
                    foreach(string key in Unreadable)if(node[key]!=null){kept[key]=node[key].ToJsonString();node.Properties.Remove(key);removed++;}
                    original[node["Id"].StringValue()]=kept;
                }
                foreach(var p in node.Properties.Values.ToList())strip(p);
            };
            foreach(var p in editor.Properties.Values.ToList())strip(p);
            var unit=SequenceJson.Parse(before);
            unit.Properties["Entities"]=SequenceJson.Parse("[]");unit.Properties["Relations"]=SequenceJson.Parse("[]");
            unit.Properties["Editors"]=new SequenceJson{Items=new List<SequenceJson>{editor}};
            var result=project.ImportUnitFromJson(unit.ToJsonString(),null,null);
            log.AppendLine("import: "+(result==null?"null":result.State));
            if(result!=null)foreach(var e in result.Errors)log.AppendLine(e.Kind+": "+e.Message);
            if(result==null || result.State!="success")throw new InvalidOperationException("書き戻しが失敗しました: "+(result==null?"結果なし":result.State));
            if(!app.Workspace.SaveProject(project,false))throw new InvalidOperationException("保存できませんでした。");
            var reread=app.Workspace.CurrentEditor as ISequenceDiagram ?? diagram;
            string after=null;
            SequenceEditorCapture.Read(project,root,reread,log,delegate(string v){after=v;});
            var afterEditor=SequenceJson.Parse(after)["Editors"].Items.First(v=>v["Id"]!=null && v["Id"].StringValue()==diagram.Id);
            var now=new Dictionary<string,SequenceJson>(StringComparer.Ordinal);
            Action<SequenceJson> collect=null;
            collect=node=>{
                if(node==null)return;
                if(node.Items!=null){foreach(var i in node.Items)collect(i);return;}
                if(node.Properties==null)return;
                if(node["Id"]!=null && node["ModelId"]!=null && node["Id"].Raw.StartsWith("\""))now[node["Id"].StringValue()]=node;
                foreach(var p in node.Properties.Values)collect(p);
            };
            foreach(var p in afterEditor.Properties.Values)collect(p);
            var tally=new SortedDictionary<string,int>(StringComparer.Ordinal);
            Action<string> add=k=>{int n;tally.TryGetValue(k,out n);tally[k]=n+1;};
            foreach(var pair in original)
                foreach(var kv in pair.Value)
                {
                    SequenceJson shape;now.TryGetValue(pair.Key,out shape);
                    var value=shape==null?null:shape[kv.Key];
                    add(kv.Key+(shape==null?": 図形なし":value==null?": 消えた":value.ToJsonString()==kv.Value?": 元のまま":": 変わった"));
                    if(value!=null && value.ToJsonString()!=kv.Value && kv.Key=="Style")
                        foreach(var sub in SequenceJson.Parse(kv.Value).Properties??new Dictionary<string,SequenceJson>())
                        {var got=value[sub.Key];add("  Style."+sub.Key+(got==null?": 消えた":got.ToJsonString()==sub.Value.ToJsonString()?": 元のまま":": 変わった"));}
                }
            report.AppendLine("書き戻しの調査: 消して書き戻した値 "+removed+"件（図形 "+original.Count+"）");
            foreach(var pair in tally)report.AppendLine(pair.Key+" "+pair.Value);
            report.AppendLine("「元のまま」なら、その項目は書き戻しで省いても製品が保つ。");
        }
        catch(Exception ex){report.AppendLine("調査を完了できません: "+ex.Message);log.AppendLine(ex.ToString());}
        SequenceExperiment.Summary=report.ToString();SequenceExperiment.Details=report+"\f"+log;
        app.Window.UI.ShowInformationDialog(report.ToString(),title);
    }
}

// The diagram's snapshot built from what the SDK reads live, for an update of a project with
// unsaved changes (ExportModelUnit refuses then). Laid out like the export and like the
// generator's payload. What the SDK cannot read is left out: part of Style, LeftPadding and
// IsRightAtFrame, which an editor import keeps or sets to their defaults (K217).
public static class SequenceSnapshotBuilder
{
    static string N(double v){return v.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
    static SequenceJson J(object v)
    {
        if(v==null)return null;
        if(v is bool)return SequenceJson.Parse((bool)v?"true":"false");
        if(v is int || v is long || v is short)return SequenceJson.Parse(Convert.ToInt64(v).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if(v is double || v is float || v is decimal)return SequenceJson.Parse(N(Convert.ToDouble(v)));
        if(v is string)return SequenceJson.Parse(SequencePayload.Q((string)v));
        var model=v as IModel;if(model!=null)return SequenceJson.Parse(SequencePayload.Q(model.Id));
        return SequenceJson.Parse(SequencePayload.Q(v.ToString()));
    }
    static SequenceJson O(){return new SequenceJson{Properties=new Dictionary<string,SequenceJson>(StringComparer.Ordinal)};}
    static void P(SequenceJson o,string k,object v){var j=J(v);if(j!=null)o.Properties[k]=j;}
    // The collection a shape is kept in, as the generator writes it.
    public static string Collection(ISequenceShape shape)
    {
        if(shape is IFrameShape)return "Frame";
        if(shape is ILifelineShape)return "Lifelines";
        if(shape is IExecutionSpecificationShape)return "ExecutionSpecifications";
        if(shape is IMessageShape)return "Messages";
        if(shape is IFragmentShape)return "Fragments";
        if(shape is IOperandShape)return "Operands";
        if(shape is IInteractionUseShape)return "InteractionUses";
        if(shape is INoteShape)return "Notes";
        if(shape is IMessageEndShape)return "MessageEnds";
        if(shape is IDestructionShape)return "Destructions";
        if(shape is INoteAnchorShape)return "NoteAnchors";
        return "?"+shape.GetType().Name;
    }
    static string EntityType(IModel m,Dictionary<string,ISequenceShape> shapeOf,string root)
    {
        if(m.Id==root)return "Interaction";
        ISequenceShape s;
        if(shapeOf.TryGetValue(m.Id,out s))
        {
            string c=Collection(s);
            switch(c)
            {
                case "Frame":return "Frame";case "Lifelines":return "Lifeline";case "ExecutionSpecifications":return "ExecutionSpecification";
                case "Messages":return "Message";case "Fragments":return "CombinedFragment";case "Operands":return "InteractionOperand";
                case "InteractionUses":return "InteractionUse";case "Notes":return "InteractionNote";case "MessageEnds":return "MessageEnd";case "Destructions":return "Destruction";
            }
        }
        // A model with no shape of its own (a guard's value, for instance) is exported as a plain entity.
        return "Entity";
    }
    public static string Build(IProject project,IInteraction root,ISequenceDiagram diagram,string schema,StringBuilder log)
    {
        var tree=SequenceMappedUpdate.Tree(root).ToList();
        var inTree=new HashSet<string>(tree.Select(m=>m.Id));
        var shapes=diagram.Shapes.ToList();
        var shapeOf=new Dictionary<string,ISequenceShape>(StringComparer.Ordinal);
        foreach(var s in shapes)if(s.ModelId!=null && !shapeOf.ContainsKey(s.ModelId))shapeOf[s.ModelId]=s;
        var entities=new List<SequenceJson>();var relations=new List<SequenceJson>();var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var m in tree)
        {
            var e=O();P(e,"Id",m.Id);P(e,"EntityType",EntityType(m,shapeOf,root.Id));P(e,"MetamodelId",m.Metaclass==null?null:m.Metaclass.Id);
            var fields=O();
            if(m.Metaclass!=null)
                foreach(var f in m.Metaclass.GetFields().Cast<IField>().Where(f=>f.RelationshipClass==null))
                {
                    try {P(fields,f.Name,m.GetField(f.Name));}
                    catch(Exception ex){log.AppendLine("snapshot field "+f.Name+": "+ex.GetType().Name);}
                }
            string name=m.Name;
            if(string.IsNullOrEmpty(name) && fields["Name"]!=null && fields["Name"].Raw.StartsWith("\""))name=fields["Name"].StringValue();
            P(e,"Name",name??"");
            e.Properties["Fields"]=fields;
            entities.Add(e);
            foreach(var r in m.GetRelationsWhere((relation,field)=>true))
            {
                if(!seen.Add(r.Id))continue;
                var o=O();P(o,"Id",r.Id);P(o,"RelationType",r.IsEmbedded?"Embed":"Ref");P(o,"MetamodelId",r.Metaclass==null?null:r.Metaclass.Id);
                P(o,"SourceId",r.Source.Id);P(o,"TargetId",r.Target.Id);P(o,"SourceIndex",r.SourceIndex);P(o,"TargetIndex",r.TargetIndex);
                if(r.IsDerivation)P(o,"IsDerivation",true);
                relations.Add(o);
            }
        }
        var editor=O();
        P(editor,"Id",diagram.Id);P(editor,"ViewType","SequenceDiagram");
        P(editor,"MetamodelId","DensoCreate.Indio.IMF.Extensions.Sequence.ViewInstance.SequenceDiagramViewInstance");
        P(editor,"DefinitionId",diagram.EditorDefinition==null?null:diagram.EditorDefinition.Id);P(editor,"ModelId",root.Id);
        var lists=new Dictionary<string,List<SequenceJson>>(StringComparer.Ordinal);
        foreach(var s in shapes)
        {
            var o=O();P(o,"Id",s.Id);P(o,"ModelId",s.ModelId);
            var node=s as ISequenceNodeShape;
            string c=Collection(s);
            if(node!=null && c!="Operands"){P(o,"X",node.LocationX);if(c!="Lifelines")P(o,"Y",node.LocationY);P(o,"Width",node.Width);if(c!="Lifelines")P(o,"Height",node.Height);}
            var bar=s as IExecutionSpecificationShape;if(bar!=null){P(o,"Length",bar.Length);P(o,"Height",bar.Length);}
            var wire=s as IMessageShape;if(wire!=null){P(o,"SourceY",wire.SourceY);P(o,"TargetY",wire.TargetY);P(o,"SelfloopBendsX",wire.SelfloopBendsX);}
            var branch=s as IOperandShape;if(branch!=null)P(o,"Position",branch.Position);
            var lane=s as ILifelineShape;if(lane!=null)P(o,"LaneLength",lane.TimelineLength);
            var anchor=s as INoteAnchorShape;if(anchor!=null){P(o,"TargetX",anchor.TargetX);P(o,"TargetY",anchor.TargetY);}
            if(c=="Frame"){editor.Properties["Frame"]=o;continue;}
            List<SequenceJson> list;if(!lists.TryGetValue(c,out list))lists[c]=list=new List<SequenceJson>();
            list.Add(o);
        }
        foreach(var pair in lists)editor.Properties[pair.Key]=new SequenceJson{Items=pair.Value};
        var top=O();P(top,"Type","Model");P(top,"SchemaVersion",schema);P(top,"TopElementId",root.Id);
        top.Properties["Entities"]=new SequenceJson{Items=entities};top.Properties["Relations"]=new SequenceJson{Items=relations};
        top.Properties["Editors"]=new SequenceJson{Items=new List<SequenceJson>{editor}};
        log.AppendLine("Snapshot built from the SDK: entities "+entities.Count+", relations "+relations.Count+", shapes "+shapes.Count);
        return top.ToJsonString();
    }
    // The schema the project file declares, as the import reads it.
    public static string Schema(IProject project)
    {
        try
        {
            using(var reader=new StreamReader(project.Path,Encoding.UTF8,true))
            {
                char[] header=new char[4096];int n=reader.Read(header,0,header.Length);
                var match=Regex.Match(new string(header,0,n),"\"SchemaVersion\"\\s*:\\s*\"([0-9]+\\.[0-9]+)\"");
                if(match.Success)return match.Groups[1].Value;
            }
        }
        catch(Exception){}
        return "13.0";
    }
    // Where every shape sits in an editor, as a path of collection names.
    public static Dictionary<string,string> Places(SequenceJson editor)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        Action<SequenceJson,string> walk=null;
        walk=(node,path)=>{
            if(node==null)return;
            if(node.Items!=null){foreach(var i in node.Items)walk(i,path);return;}
            if(node.Properties==null)return;
            if(node["Id"]!=null && node["ModelId"]!=null && node["Id"].Raw.StartsWith("\"") && path.Length>0)result[node["Id"].StringValue()]=path;
            foreach(var p in node.Properties)if(p.Value!=null && (p.Value.Items!=null || p.Value.Properties!=null))walk(p.Value,path.Length==0?p.Key:path+"/"+p.Key);
        };
        walk(editor,"");
        return result;
    }
}
// END GENERATED SequenceSyncRuntime.cs

// BEGIN GENERATED PlantUmlTool/src/10-sequence-export.cs (exporter)

// ------------------------------------------------------------
//  出力オプション
// ------------------------------------------------------------
public class PlantUmlOptions
{
    public bool IncludeTitle = true;          // 図名を title として出力する
    public bool UseAutonumber = false;        // autonumber を出力する
    public string Theme = null;               // !theme <name> を出力する
    public bool EmitNotes = true;             // ノートを出力する
    public bool EmitActivation = true;        // activate / deactivate を出力する
    public bool UseTypeKeywords = true;       // 型名から actor / boundary などを出し分ける
    public bool UseCreateParticipant = true;  // 生成メッセージを create で表現する
    public bool EmitTimestamp = false;        // 出力日時を埋め込む（差分安定化のため既定 false）
    public string IndentUnit = "  ";          // 入れ子のインデント
    public string NewLine = "\n";             // 改行は LF 固定
    public string AliasStyle = "Name";        // "Name" | "Id"
    public double BoundaryEpsilon = 1.0;      // フラグメント下端の判定誤差
    public double ActivationSnapTolerance = 10.0;  // 実行仕様の端をメッセージに吸着させる許容距離
    public string RefBackgroundColor = "#EFEFEF";  // ref（相互作用の利用）の背景色。空なら既定のまま

    // 複合フラグメントのテキスト先頭語 → PlantUML の演算子
    public Dictionary<string, string> OperatorMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "alt", "alt" }, { "opt", "opt" }, { "loop", "loop" },
        { "par", "par" }, { "break", "break" }, { "critical", "critical" },
        { "代替", "alt" }, { "選択", "alt" }, { "分岐", "alt" },
        { "条件", "opt" }, { "オプション", "opt" }, { "任意", "opt" },
        { "繰り返し", "loop" }, { "ループ", "loop" }, { "反復", "loop" },
        { "並行", "par" }, { "並列", "par" },
        { "中断", "break" },
        { "限界領域", "critical" }, { "クリティカル", "critical" },
    };

    // ライフラインの型名 → PlantUML の participant キーワード
    public Dictionary<string, string> TypeKeywordMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "Actor", "actor" }, { "アクター", "actor" }, { "利用者", "actor" }, { "ユーザ", "actor" },
        { "Boundary", "boundary" }, { "バウンダリ", "boundary" },
        { "Control", "control" }, { "コントロール", "control" },
        { "Entity", "entity" }, { "エンティティ", "entity" },
        { "Database", "database" }, { "データベース", "database" },
        { "Queue", "queue" }, { "キュー", "queue" },
    };
}

// ------------------------------------------------------------
//  文字列ユーティリティ
// ------------------------------------------------------------
public class PlantUmlText
{
    // 実行ごとに値が変わらないハッシュ（string.GetHashCode は使わない）
    public static string ShortHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            var t = s ?? "";
            for (var i = 0; i < t.Length; i++)
            {
                h ^= t[i];
                h *= 16777619;
            }
            return h.ToString("x8");
        }
    }

    // 連続する空白を 1 つに畳んで前後を除去する
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        var space = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!space && sb.Length > 0) sb.Append(' ');
                space = true;
            }
            else
            {
                sb.Append(ch);
                space = false;
            }
        }
        return sb.ToString().Trim();
    }

    // 改行を PlantUML のラベル用エスケープに変換する
    public static string Inline(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
    }

    public static string Quote(string s)
    {
        return "\"" + (s ?? "").Replace("\"", "'") + "\"";
    }

    // ASCII だけで別名を作る。作れない場合は空文字を返す
    public static string AsciiAlias(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? ""))
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                sb.Append(ch);
            else if (ch == '_' || ch == ' ' || ch == '-' || ch == '.')
                sb.Append('_');
        }
        var alias = sb.ToString().Trim('_');
        while (alias.Contains("__")) alias = alias.Replace("__", "_");
        if (alias.Length == 0) return "";
        if (alias[0] >= '0' && alias[0] <= '9') alias = "L" + alias;
        return alias;
    }

    // プロファイルが自動生成するシステム・匿名フィールド名（$ / ____ 始まり）か
    public static bool IsSystemName(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.StartsWith("$", StringComparison.Ordinal)
            || s.StartsWith("____", StringComparison.Ordinal)
            || s.StartsWith("___", StringComparison.Ordinal);
    }

    public static string SafeFileName(string s)
    {
        var invalid = new HashSet<char>(System.IO.Path.GetInvalidFileNameChars());
        var sb = new StringBuilder();
        foreach (var ch in (s ?? ""))
            sb.Append(invalid.Contains(ch) || ch == ' ' ? '_' : ch);
        return sb.ToString().Trim('_', '.');
    }
}

// ------------------------------------------------------------
//  内部用：出力イベントと開いているフラグメント
// ------------------------------------------------------------
public class SeqEvent
{
    public double Y;
    public int Priority;
    public double X;
    public double Rank;          // フラグメントは面積の大きい順に並べるため負値を入れる
    public string Id = "";
    public string Kind = "";
    public string FragmentId = "";   // operand イベントが属するフラグメント
    public IMessageShape Message;
    public IFragmentShape Fragment;
    public IOperandShape Operand;
    public IExecutionSpecificationShape Execution;
    public IInteractionUseShape Use;
    public IDestructionShape Destruction;
    public INoteShape Note;
}

public class OpenFragment
{
    public string Id = "";
    public double Bottom;
}

// ------------------------------------------------------------
//  変換本体
// ------------------------------------------------------------
public class SequencePlantUmlExporter
{
    private readonly ISequenceDiagram _d;
    private readonly PlantUmlOptions _o;
    private readonly StringBuilder _sb = new StringBuilder();

    private readonly List<ILifelineShape> _lifelines = new List<ILifelineShape>();
    private readonly Dictionary<string, string> _alias = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _label = new Dictionary<string, string>();
    private readonly Dictionary<string, string> _keyword = new Dictionary<string, string>();
    private readonly HashSet<string> _usedAlias = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _createdLater = new HashSet<string>();
    private readonly HashSet<string> _declared = new HashSet<string>();
    private readonly HashSet<string> _destroyed = new HashSet<string>();
    private readonly Dictionary<string, int> _activeCount = new Dictionary<string, int>();
    private readonly List<OpenFragment> _stack = new List<OpenFragment>();

    public SequencePlantUmlExporter(ISequenceDiagram diagram, PlantUmlOptions options)
    {
        _d = diagram;
        _o = options ?? new PlantUmlOptions();
    }

    public string Export()
    {
        PrepareLifelines();
        WriteHeader();
        WriteParticipants();
        WriteBody();
        DeactivateAll();
        CloseAllFragments();
        LineAt(0, "@enduml");
        return _sb.ToString();
    }

    public string DiagramName()
    {
        if (_d.Model != null && !string.IsNullOrEmpty(_d.Model.Name)) return _d.Model.Name;
        return string.IsNullOrEmpty(_d.ViewDefinitionName) ? "Sequence" : _d.ViewDefinitionName;
    }

    // ---------- 準備 ----------

    private void PrepareLifelines()
    {
        var ordered = _d.Lifelines.Cast<ILifelineShape>()
            .OrderBy(l => l.LocationX)
            .ThenBy(l => l.LocationY)
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var l in ordered)
        {
            _lifelines.Add(l);
            Register(l);
        }

        if (!_o.UseCreateParticipant) return;

        // 生成メッセージで作られるライフラインは create 宣言に回す
        foreach (var m in _d.Messages.Cast<IMessageShape>())
        {
            if (KindOf(m) != "create") continue;
            if (m.Receiver == null) continue;
            _createdLater.Add(m.Receiver.Id);
        }
    }

    private void Register(ILifelineShape l)
    {
        if (_alias.ContainsKey(l.Id)) return;

        var label = PlantUmlText.Normalize(l.Text);
        if (label.Length == 0 && l.TypeModel != null) label = PlantUmlText.Normalize(l.TypeModel.Name);
        if (label.Length == 0) label = "(unnamed)";
        _label[l.Id] = label;

        var alias = _o.AliasStyle == "Id" ? "" : PlantUmlText.AsciiAlias(label);
        if (alias.Length == 0) alias = "L" + PlantUmlText.ShortHash(l.Id);
        if (!_usedAlias.Add(alias))
        {
            alias = alias + "_" + PlantUmlText.ShortHash(l.Id);
            _usedAlias.Add(alias);
        }
        _alias[l.Id] = alias;
        _keyword[l.Id] = KeywordOf(l);
    }

    private string KeywordOf(ILifelineShape l)
    {
        if (!_o.UseTypeKeywords || l.TypeModel == null) return "participant";

        string keyword;
        var typeName = l.TypeModel.Name;
        if (!string.IsNullOrEmpty(typeName) && _o.TypeKeywordMap.TryGetValue(typeName, out keyword))
            return keyword;

        var className = l.TypeModel.ClassName;
        if (!string.IsNullOrEmpty(className) && _o.TypeKeywordMap.TryGetValue(className, out keyword))
            return keyword;

        return "participant";
    }

    private string AliasOf(ILifelineShape l)
    {
        if (l == null) return null;
        Register(l);
        return _alias[l.Id];
    }

    private string DeclarationOf(ILifelineShape l)
    {
        return _keyword[l.Id] + " " + PlantUmlText.Quote(_label[l.Id]) + " as " + _alias[l.Id];
    }

    private void EnsureDeclared(ILifelineShape l)
    {
        if (l == null) return;
        AliasOf(l);
        if (_declared.Add(l.Id)) Line(DeclarationOf(l));
    }

    // ---------- ヘッダとライフライン宣言 ----------

    private void WriteHeader()
    {
        LineAt(0, "@startuml");
        if (!string.IsNullOrEmpty(_o.Theme)) LineAt(0, "!theme " + _o.Theme);
        LineAt(0, "skinparam sequenceMessageAlign left");
        LineAt(0, "skinparam maxMessageSize 200");
        if (!string.IsNullOrEmpty(_o.RefBackgroundColor))
            LineAt(0, "skinparam sequenceReferenceBackgroundColor " + _o.RefBackgroundColor);
        if (_o.IncludeTitle) LineAt(0, "title " + PlantUmlText.Inline(DiagramName()));
        if (_o.EmitTimestamp) LineAt(0, "' exported at " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        if (_o.UseAutonumber) LineAt(0, "autonumber");
        Blank();
    }

    private void WriteParticipants()
    {
        var any = false;
        foreach (var l in _lifelines)
        {
            if (_createdLater.Contains(l.Id)) continue;
            Line(DeclarationOf(l));
            _declared.Add(l.Id);
            any = true;
        }
        if (any) Blank();
    }

    // ---------- 本体 ----------

    private void WriteBody()
    {
        var events = new List<SeqEvent>();

        foreach (var m in _d.Messages.Cast<IMessageShape>())
        {
            events.Add(new SeqEvent
            {
                Y = m.SourceY, Priority = 50, X = MessageX(m),
                Id = m.Id, Kind = "message", Message = m
            });
        }

        foreach (var f in _d.Fragments.Cast<IFragmentShape>())
        {
            events.Add(new SeqEvent
            {
                Y = f.LocationY, Priority = 10, X = f.LocationX,
                Rank = -((double)f.Width * (double)f.Height),
                Id = f.Id, Kind = "fragment", Fragment = f
            });

            var operands = OperandsOf(f);
            var operandYs = OperandYs(f, operands);
            for (var i = 1; i < operands.Count; i++)   // 先頭のガードはヘッダ行に出す
            {
                events.Add(new SeqEvent
                {
                    Y = operandYs[i], Priority = 20, X = f.LocationX,
                    Id = operands[i].Id, Kind = "operand", Operand = operands[i],
                    FragmentId = f.Id
                });
            }
        }

        if (_o.EmitActivation)
        {
            var messages = _d.Messages.Cast<IMessageShape>().ToList();
            var executions = _d.ExecutionSpecifications.Cast<IExecutionSpecificationShape>().ToList();

            foreach (var e in executions)
            {
                var lifelineId = e.Lifeline != null ? e.Lifeline.Id : null;
                var top = (double)e.LocationY;
                var bottom = top + e.Length;

                // PlantUML の入れ子は「トリガのメッセージ行の直後に activate」で決まるが、
                // 図形上はバー上端とメッセージの Y が数ピクセルずれうる。
                // 最寄りのメッセージに吸着させてから並べる
                var activateY = top;
                var activatePriority = 60;   // 受信メッセージ(50)の直後
                var trigger = NearestMessage(messages, lifelineId, top, true);
                if (trigger != null)
                {
                    activateY = trigger.SourceY;

                    // セルフメッセージでは送信元の外側バーと受信で立つ内側バーの
                    // 両方が上端一致する。他のバーに包含されない最外殻のバーは
                    // 送信元なので、activate をメッセージより前に出す
                    var isSelf = trigger.Sender != null && trigger.Receiver != null
                              && trigger.Sender.Id == trigger.Receiver.Id;
                    if (isSelf && !executions.Any(o => ContainsExecution(o, e)))
                        activatePriority = 45;
                }
                else
                {
                    // 受信で立たないバーは送信メッセージが起点。activate をその送信より先に出す
                    var origin = NearestMessage(messages, lifelineId, top, false);
                    if (origin != null) { activateY = origin.SourceY; activatePriority = 45; }
                }

                // 下端は戻りメッセージ(50)の直後・次の activate(60) より前
                var deactivateY = bottom;
                var closer = NearestMessage(messages, lifelineId, bottom, false);
                if (closer != null) deactivateY = closer.SourceY;

                events.Add(new SeqEvent
                {
                    Y = activateY, Priority = activatePriority, X = e.LocationX,
                    Id = e.Id, Kind = "activate", Execution = e
                });
                events.Add(new SeqEvent
                {
                    Y = deactivateY, Priority = 55, X = e.LocationX,
                    Id = e.Id, Kind = "deactivate", Execution = e
                });
            }
        }

        foreach (var u in _d.InteractionUses.Cast<IInteractionUseShape>())
        {
            events.Add(new SeqEvent
            {
                Y = u.LocationY, Priority = 30, X = u.LocationX,
                Id = u.Id, Kind = "use", Use = u
            });
        }

        foreach (var x in _d.Destructions.Cast<IDestructionShape>())
        {
            events.Add(new SeqEvent
            {
                Y = x.LocationY, Priority = 70, X = x.LocationX,
                Id = x.Id, Kind = "destruction", Destruction = x
            });
        }

        if (_o.EmitNotes)
        {
            foreach (var n in _d.Notes.Cast<INoteShape>())
            {
                events.Add(new SeqEvent
                {
                    Y = n.LocationY, Priority = 40, X = n.LocationX,
                    Id = n.Id, Kind = "note", Note = n
                });
            }
        }

        // Y → 種類 → X → 面積の大きい順 → Id の完全順序
        var ordered = events
            .OrderBy(e => e.Y)
            .ThenBy(e => e.Priority)
            .ThenBy(e => e.X)
            .ThenBy(e => e.Rank)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

        // PlantUML では activate/deactivate が「直前のメッセージ行」に束縛される。
        // 同じメッセージに deactivate 済みの参加者を、新しいメッセージを挟まずに
        // 再度 activate すると "Activate/Deactivate already done" になるため、
        // そうなる activate だけ次のメッセージの直後まで先送りする
        var deactivatedSinceMessage = new HashSet<string>(StringComparer.Ordinal);
        var pendingActivates = new List<SeqEvent>();

        foreach (var ev in ordered)
        {
            CloseFragmentsAbove(ev.Y);

            if (ev.Kind == "fragment") OnFragment(ev.Fragment);
            else if (ev.Kind == "operand") OnOperand(ev.Operand, ev.FragmentId);
            else if (ev.Kind == "message")
            {
                OnMessage(ev.Message);
                deactivatedSinceMessage.Clear();
                foreach (var pending in pendingActivates) OnActivate(pending.Execution);
                pendingActivates.Clear();
            }
            else if (ev.Kind == "activate")
            {
                var lifeline = ev.Execution.Lifeline;
                var alias = lifeline != null ? AliasOf(lifeline) : null;
                if (alias != null && deactivatedSinceMessage.Contains(alias))
                    pendingActivates.Add(ev);
                else
                    OnActivate(ev.Execution);
            }
            else if (ev.Kind == "deactivate")
            {
                // メッセージを 1 つも挟めなかったバーは activate/deactivate を対で捨てる
                var pendingIndex = pendingActivates.FindIndex(p => p.Id == ev.Id);
                if (pendingIndex >= 0)
                {
                    pendingActivates.RemoveAt(pendingIndex);
                }
                else if (OnDeactivate(ev.Execution))
                {
                    var lifeline = ev.Execution.Lifeline;
                    if (lifeline != null) deactivatedSinceMessage.Add(AliasOf(lifeline));
                }
            }
            else if (ev.Kind == "use") OnInteractionUse(ev.Use);
            else if (ev.Kind == "destruction") OnDestruction(ev.Destruction);
            else if (ev.Kind == "note") OnNote(ev.Note);
        }
        // 最後までメッセージが来なかった先送り分は出力しない
        // （対応する deactivate は _activeCount のガードで自然にスキップ済み）
    }

    private List<IOperandShape> OperandsOf(IFragmentShape f)
    {
        return f.Operands.Cast<IOperandShape>()
            .OrderBy(o => o.Position)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();
    }

    // Operand.Position は環境によって絶対 Y とフラグメント上端からの相対の両方が
    // ありうるため、フラグメントの範囲に収まるかどうかで判別して絶対 Y に揃える。
    // さらにフラグメント範囲内へクランプし、else 行が自分の枠から漏れないようにする
    private List<double> OperandYs(IFragmentShape f, List<IOperandShape> operands)
    {
        var top = (double)f.LocationY;
        var bottom = top + f.Height;
        var eps = _o.BoundaryEpsilon;

        var absolute = operands.Count > 0
            && operands.All(o => o.Position >= top - eps && o.Position <= bottom + eps);

        var result = new List<double>();
        foreach (var o in operands)
        {
            var y = absolute ? (double)o.Position : top + o.Position;
            if (y < top) y = top;
            if (y > bottom - 2 * eps) y = bottom - 2 * eps;
            result.Add(y);
        }
        return result;
    }

    private double MessageX(IMessageShape m)
    {
        var send = m.SendPort as ISequenceNodeShape;
        if (send != null) return send.LocationX;
        var receive = m.ReceivePort as ISequenceNodeShape;
        return receive != null ? receive.LocationX : 0;
    }

    // 指定 Y に最も近い、指定ライフラインが受信（wantReceiver=true）または
    // 送信するメッセージを許容誤差内で探す
    private IMessageShape NearestMessage(List<IMessageShape> messages, string lifelineId,
                                         double y, bool wantReceiver)
    {
        if (lifelineId == null) return null;

        IMessageShape best = null;
        var bestDistance = _o.ActivationSnapTolerance + 1e-9;
        foreach (var m in messages)
        {
            var end = wantReceiver ? m.Receiver : m.Sender;
            if (end == null || end.Id != lifelineId) continue;

            var distance = Math.Abs(m.SourceY - y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = m;
            }
        }
        return best;
    }

    // 同一ライフライン上で outer が inner を包含するか。
    // スパンがほぼ同一の場合は X の小さい方を外側とみなす
    private bool ContainsExecution(IExecutionSpecificationShape outer, IExecutionSpecificationShape inner)
    {
        if (outer == null || inner == null || outer.Id == inner.Id) return false;
        if (outer.Lifeline == null || inner.Lifeline == null) return false;
        if (outer.Lifeline.Id != inner.Lifeline.Id) return false;

        var eps = _o.BoundaryEpsilon;
        var outerTop = (double)outer.LocationY;
        var outerBottom = outerTop + outer.Length;
        var innerTop = (double)inner.LocationY;
        var innerBottom = innerTop + inner.Length;

        if (outerTop > innerTop + eps || outerBottom < innerBottom - eps) return false;

        var sameSpan = Math.Abs(outerTop - innerTop) <= eps && Math.Abs(outerBottom - innerBottom) <= eps;
        if (sameSpan) return outer.LocationX < inner.LocationX;
        return true;
    }

    // ---------- フラグメント ----------

    private void OnFragment(IFragmentShape f)
    {
        var op = OperatorOf(f);
        string header;
        if (op == "group")
        {
            var text = PlantUmlText.Inline(PlantUmlText.Normalize(f.Text));
            header = text.Length > 0 ? "group " + text : "group";
        }
        else
        {
            var guard = FirstGuardOf(f);
            header = guard.Length > 0 ? op + " " + guard : op;
        }
        Line(header);
        _stack.Add(new OpenFragment { Id = f.Id, Bottom = f.LocationY + f.Height });
    }

    private void OnOperand(IOperandShape o, string fragmentId)
    {
        var guard = PlantUmlText.Inline(PlantUmlText.Normalize(o.Guard));

        // 自分のフラグメントが開いていない位置で else を出すと構文エラーになる
        var index = _stack.FindLastIndex(s => s.Id == fragmentId);
        if (index < 0)
        {
            Line("' [warn] 分岐 '" + guard + "' の位置を特定できなかったため出力しません");
            return;
        }

        // 前の分岐の中で開いたままの内側フラグメントを閉じてから else を出す
        while (_stack.Count - 1 > index)
        {
            _stack.RemoveAt(_stack.Count - 1);
            Line("end");
        }

        // ガードが「else」そのものの分岐は素の else にする（"else else" を避ける）
        var line = guard.Length == 0 || string.Equals(guard, "else", StringComparison.OrdinalIgnoreCase)
                 ? "else" : "else " + guard;
        LineAt(_stack.Count - 1, line);
    }

    private string OperatorOf(IFragmentShape f)
    {
        var text = PlantUmlText.Normalize(f.Text);
        if (text.Length == 0) return "group";

        string op;
        if (_o.OperatorMap.TryGetValue(text, out op)) return op;

        var head = text.Split(new[] { ' ', '[', '(', '\u3000' }, StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault();
        if (!string.IsNullOrEmpty(head) && _o.OperatorMap.TryGetValue(head, out op)) return op;

        return "group";
    }

    private string FirstGuardOf(IFragmentShape f)
    {
        var first = OperandsOf(f).FirstOrDefault();
        if (first == null) return "";
        return PlantUmlText.Inline(PlantUmlText.Normalize(first.Guard));
    }

    private void CloseFragmentsAbove(double y)
    {
        while (_stack.Count > 0 && y > _stack[_stack.Count - 1].Bottom - _o.BoundaryEpsilon)
        {
            _stack.RemoveAt(_stack.Count - 1);
            Line("end");
        }
    }

    private void CloseAllFragments()
    {
        while (_stack.Count > 0)
        {
            _stack.RemoveAt(_stack.Count - 1);
            Line("end");
        }
    }

    // ---------- メッセージ ----------

    private void OnMessage(IMessageShape m)
    {
        var kind = KindOf(m);
        var sender = m.Sender;
        var receiver = m.Receiver;

        if (kind == "create" && receiver != null && _o.UseCreateParticipant && !_declared.Contains(receiver.Id))
        {
            AliasOf(receiver);
            Line("create " + DeclarationOf(receiver));
            _declared.Add(receiver.Id);
        }
        EnsureDeclared(sender);
        EnsureDeclared(receiver);

        var arrow = ArrowOf(kind);
        var label = PlantUmlText.Inline(PlantUmlText.Normalize(m.Text));
        var tail = label.Length > 0 ? " : " + label : "";

        if (sender == null && receiver != null)
            Line("[" + arrow + " " + AliasOf(receiver) + tail);              // 出現メッセージ
        else if (sender != null && receiver == null)
            Line(AliasOf(sender) + " " + arrow + "]" + tail);                // 消失メッセージ
        else if (sender != null && receiver != null)
            Line(AliasOf(sender) + " " + arrow + " " + AliasOf(receiver) + tail);
        else
            Line("' message : " + label);

        if (kind == "destroy" && receiver != null) DestroyLifeline(receiver);
    }

    private string KindOf(IMessageShape m)
    {
        var model = m.Model as IMessage;
        if (model == null) return "sync";
        var kind = model.Kind;
        return string.IsNullOrEmpty(kind) ? "sync" : kind.ToLowerInvariant();
    }

    private static string ArrowOf(string kind)
    {
        if (kind == "async") return "->>";
        if (kind == "reply") return "-->";
        return "->";
    }

    // ---------- 実行仕様・破棄 ----------

    private void OnActivate(IExecutionSpecificationShape e)
    {
        var l = e.Lifeline;
        if (l == null || _destroyed.Contains(l.Id)) return;
        EnsureDeclared(l);

        var alias = AliasOf(l);
        int count;
        _activeCount.TryGetValue(alias, out count);
        _activeCount[alias] = count + 1;
        Line("activate " + alias);
    }

    // 戻り値: deactivate 行を実際に出力したか
    private bool OnDeactivate(IExecutionSpecificationShape e)
    {
        var l = e.Lifeline;
        if (l == null || _destroyed.Contains(l.Id)) return false;

        var alias = AliasOf(l);
        int count;
        if (!_activeCount.TryGetValue(alias, out count) || count <= 0) return false;
        _activeCount[alias] = count - 1;
        Line("deactivate " + alias);
        return true;
    }

    private void DeactivateAll()
    {
        foreach (var entry in _activeCount.OrderBy(k => k.Key, StringComparer.Ordinal).ToList())
        {
            for (var i = 0; i < entry.Value; i++) Line("deactivate " + entry.Key);
            _activeCount[entry.Key] = 0;
        }
    }

    private void OnDestruction(IDestructionShape x)
    {
        var l = x.Lifeline;
        if (l == null) return;
        DestroyLifeline(l);
    }

    private void DestroyLifeline(ILifelineShape l)
    {
        if (!_destroyed.Add(l.Id)) return;
        var alias = AliasOf(l);
        // destroy が実行バーを終了する。後続イベントや末尾処理で再終了しない。
        _activeCount.Remove(alias);
        Line("destroy " + alias);
    }

    // ---------- 相互作用の利用・ノート ----------

    private void OnInteractionUse(IInteractionUseShape u)
    {
        var text = PlantUmlText.Inline(PlantUmlText.Normalize(u.Text));
        if (text.Length == 0) text = "ref";

        var aliases = u.Lifelines.Cast<ILifelineShape>()
            .OrderBy(l => l.LocationX)
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .Select(l => AliasOf(l))
            .Where(a => a != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (aliases.Count == 0)
        {
            var nearest = NearestAlias(u.LocationX + u.Width / 2.0);
            if (nearest != null) aliases.Add(nearest);
        }
        if (aliases.Count == 0)
        {
            Line("' ref : " + text);
            return;
        }
        Line("ref over " + string.Join(", ", aliases) + " : " + text);
    }

    private void OnNote(INoteShape n)
    {
        if (PlantUmlText.Normalize(n.Text).Length == 0) return;

        var target = AnchoredLifelineOf(n);
        var alias = target != null ? AliasOf(target) : NearestAlias(n.LocationX + n.Width / 2.0);
        if (alias == null)
        {
            Line("' note : " + PlantUmlText.Inline(PlantUmlText.Normalize(n.Text)));
            return;
        }

        Line("note over " + alias);
        foreach (var raw in n.Text.Replace("\r\n", "\n").Split('\n'))
            LineAt(_stack.Count + 1, raw.TrimEnd());
        Line("end note");
    }

    private ILifelineShape AnchoredLifelineOf(INoteShape n)
    {
        foreach (var anchor in n.NoteAnchors.Cast<INoteAnchorShape>()
                                            .OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var other = IsSame(anchor.Source, n) ? anchor.Target : anchor.Source;

            var lifeline = other as ILifelineShape;
            if (lifeline != null) return lifeline;

            var execution = other as IExecutionSpecificationShape;
            if (execution != null && execution.Lifeline != null) return execution.Lifeline;

            var message = other as IMessageShape;
            if (message != null) return message.Sender ?? message.Receiver;
        }
        return null;
    }

    private static bool IsSame(ISequenceShape a, ISequenceShape b)
    {
        return a != null && b != null && a.Id == b.Id;
    }

    private string NearestAlias(double x)
    {
        ILifelineShape best = null;
        var bestDistance = double.MaxValue;
        foreach (var l in _lifelines)
        {
            var distance = Math.Abs(l.LocationX + l.Width / 2.0 - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = l;
            }
        }
        return best == null ? null : _alias[best.Id];
    }

    // ---------- 出力 ----------

    private void Line(string text)
    {
        LineAt(_stack.Count, text);
    }

    private void LineAt(int depth, string text)
    {
        if (!string.IsNullOrEmpty(text))
            for (var i = 0; i < depth; i++) _sb.Append(_o.IndentUnit);
        _sb.Append(text);
        _sb.Append(_o.NewLine);
    }

    private void Blank()
    {
        _sb.Append(_o.NewLine);
    }
}

// ============================================================
//  実行部：対象の決定、ダイアログ、ファイル出力、ログ
// ============================================================
// END GENERATED PlantUmlTool/src/10-sequence-export.cs (exporter)

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
                    if(app.Window.UI.ShowConfirmDialog("参加者の対応先を選んでください。\nPlantUML: "+plan.Names[i]+"\n別名: "+plan.Aliases[i]+"\n図の表示: "+candidate.Text+"\nモデル名: "+candidate.Model.Name+"\n図内X位置: "+Number(candidate.LocationX)+"\n接続メッセージ例:\n"+context+"\nこの参加者に対応付けますか？\nOK: 対応付けます。\nキャンセル: 次の候補を表示します（全候補をキャンセルすると処理を中止します）。",SequenceExperiment.Title))
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
    // A whole unit exported once by the scenario batch, parsed, for every diagram it made:
    // none of them is touched until its own turn, so each can be cut out of it.
    public static SequenceJson BatchSource;
    internal static string Trim(string exported,HashSet<string> kept,StringBuilder log)
    { return Trim(SequenceJson.Parse(exported),exported.Length,kept,log); }
    internal static string Trim(SequenceJson document,long length,HashSet<string> kept,StringBuilder log)
    {
        if(document==null || document.Properties==null)throw new InvalidOperationException("E180: 図のエクスポートがJSONオブジェクトではありません。");
        var trimmed=new SequenceJson{Properties=new Dictionary<string,SequenceJson>(StringComparer.Ordinal)};
        int entities=0,relations=0,editors=0;
        foreach(var pair in document.Properties)
        {
            var value=pair.Value;
            if(value!=null && value.Items!=null && pair.Key=="Entities")
            {entities=value.Items.Count;value=new SequenceJson{Items=value.Items.Where(e=>kept.Contains(SequenceEditorDocument.Value(e,"Id")??"")).ToList()};}
            else if(value!=null && value.Items!=null && pair.Key=="Relations")
            {
                relations=value.Items.Count;
                value=new SequenceJson{Items=value.Items.Where(r=>kept.Contains(SequenceEditorDocument.Value(r,"SourceId")??"")
                    || kept.Contains(SequenceEditorDocument.Value(r,"TargetId")??"")).ToList()};
            }
            else if(value!=null && value.Items!=null && pair.Key=="Editors")
            {editors=value.Items.Count;value=new SequenceJson{Items=value.Items.Where(v=>Shows(v,kept)).ToList()};}
            trimmed.Properties[pair.Key]=value;
        }
        string result=trimmed.ToJsonString();
        log.AppendLine("Editor snapshot trimmed: entities "+entities+"→"+trimmed["Entities"].Items.Count+", relations "+relations+"→"+trimmed["Relations"].Items.Count
            +", editors "+editors+"→"+trimmed["Editors"].Items.Count+", chars "+length+"→"+result.Length);
        return result;
    }
    static bool Shows(SequenceJson node,HashSet<string> kept)
    {
        if(node==null)return false;
        if(node.Properties!=null)return node.Properties.Any(p=>(p.Key=="ModelId" && p.Value!=null && p.Value.Raw!=null
            && p.Value.Raw.StartsWith("\"",StringComparison.Ordinal) && kept.Contains(p.Value.StringValue())) || Shows(p.Value,kept));
        return node.Items!=null && node.Items.Any(n=>Shows(n,kept));
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
            var kept=new HashSet<string>(SequenceMappedUpdate.Tree(root).Select(m=>m.Id));
            string exported;
            if(BatchSource!=null)
            {
                log.AppendLine("Editor snapshot: cut from the batch's one export");
                exported=Trim(BatchSource,0,kept,log);
            }
            else
            {
                log.AppendLine("Editor snapshot export: unit type="+root.ModelUnit.Type);
                project.UnitManager.ExportModelUnit(root.ModelUnit,path);
                if(!File.Exists(path) || new FileInfo(path).Length>100000000)throw new InvalidOperationException("E180: 図のエクスポートを取得できないか100MBを超えています。");
                // The unit can hold far more than this diagram. Keep only the interaction's own
                // models, every relation touching them and the editors that show them, so each
                // later parse, check and saved file deals with this diagram alone.
                exported=Trim(File.ReadAllText(path,new UTF8Encoding(false,true)),kept,log);
            }
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
        if (p.Aliases.Count<1) throw Error(1,"参加者を1本以上書いてください。");
        // activate / deactivate are not elements of their own; the reading allows 2000 elements.
        if (p.All().Count(n=>n.Kind!="activate" && n.Kind!="deactivate")>2000) throw Error(1,"要素は2000件以下にしてください。");
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
            if(n.Kind=="fragment")
            {
                // A bar a branch closes is closed after the frame too, as the reading takes it.
                var after=new Dictionary<string,int>(counts);
                foreach(var branch in n.Children)
                {
                    if(n.Operator=="loop"){ValidateActivities(branch.Children,counts,false);continue;}
                    var mine=new Dictionary<string,int>(counts);ValidateActivities(branch.Children,mine);
                    foreach(var pair in mine)if(!after.ContainsKey(pair.Key) || pair.Value<after[pair.Key])after[pair.Key]=pair.Value;
                }
                if(n.Operator!="loop")foreach(var pair in after)counts[pair.Key]=pair.Value;
            }
            if(n.Kind=="destroy")
            {
                int inherited; initial.TryGetValue(n.Left,out inherited);
                if(inherited>0)throw Error(n.Line,"分岐の外で開始した実行区間の破棄は未対応です。");
                counts[n.Left]=0; continue;
            }
            if(n.Kind!="activate" && n.Kind!="deactivate")continue;
            int value; counts.TryGetValue(n.Left,out value);
            int baseline; initial.TryGetValue(n.Left,out baseline);
            // The exporter writes the deactivate of a bar opened before a frame after that bar's last
            // message, which can be inside a branch. Closing a bar opened outside the branch is
            // fine; closing one that is not open at all is not.
            if(n.Kind=="deactivate" && value<=0)throw Error(n.Line,"対応するactivateが同じ図または分岐内にありません。");
            counts[n.Left]=value+(n.Kind=="activate"?1:-1);
        }
        if(!balanced)return;
        foreach(var pair in counts)
        {
            int value; initial.TryGetValue(pair.Key,out value);
            // A branch may close bars opened before it, but must not leave its own open.
            if(pair.Value>value)throw Error(1,"activate/deactivateは図または各分岐内で対応させてください。");
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
    public string NoteField = "Body", NoteStorage = "String", Sync="Sync", Async="Async", Reply="Reply", Destroy=null;
    // The interaction each ref refers to, by its input line; a ref without one is drawn unlinked.
    public Dictionary<int,string> References = new Dictionary<int,string>();
    // Label, id and full name of every concrete type this run settled on.
    public List<string> Resolved = new List<string>();
}
public class PumlBuild
{
    // Rows between one message and the next. The structural sync places added and
    // inserted messages with the same step (SequenceStructurePreparation.MessageSpacing).
    public const int MessagePitch=40;
    // A bar holding no message of its own still has to end before the next row, or the
    // reader takes the next message for its end. 16 short of a row, as a closing bar ends.
    public const int MinimumBar=MessagePitch-16;
    private PumlProfile profile; private SequencePayload payload;
    // Every message in drawing order: {id, send port, receive port, kind}.
    private List<string[]> sent=new List<string[]>();
    // The last message each lane received, so a destroy right after it can point back.
    private Dictionary<string,string> lastReceived=new Dictionary<string,string>();
    // A message whose receiver is destroyed right after it, perhaps with an activate between.
    static bool DestroysNext(List<PumlNode> items,int index)
    {
        var n=items[index];int next=index+1;
        if(next<items.Count && items[next].Kind=="activate" && items[next].Left==n.Right)next++;
        return next<items.Count && items[next].Kind=="destroy" && items[next].Left==n.Right;
    }
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
    // Bars a reply has ended. Next Design refuses every edit to a diagram whose bar goes on
    // after a reply, so the lane goes on in a new bar from its next message.
    private HashSet<string> closed=new HashSet<string>();
    // How many bars are open on a lane right now: a bar a reply has ended does not count, so
    // a bar that follows it is not drawn as nested in it.
    private int Depth(string alias)
    {
        var open=new HashSet<string>();
        string current;
        if(active.TryGetValue(alias,out current) && current!=null && !closed.Contains(current))open.Add(current);
        if(activities.ContainsKey(alias))foreach(var id in activities[alias])if(id!=null && !closed.Contains(id))open.Add(id);
        return open.Count;
    }
    // A bar just deactivated, and who called it: a reply from that lane to that caller answers
    // the call and leaves from that bar, even when the input deactivates it first.
    private Dictionary<string,string> justEnded=new Dictionary<string,string>(), caller=new Dictionary<string,string>();
    private string Execution(string alias,int start)
    {
        string id=Entity("ExecutionSpecification",""); Owned("ExecutionSpecifications",id); Link("OwnedExecutionSpecification",lifelines[alias],id);
        executions[id]=Shape("ExecutionSpecifications",id,"X",x[alias]+8*Depth(alias),"Y",start,"Length",40,"Height",40); executionAliases[id]=alias; return id;
    }
    private void Extend(string id,int at)
    { if(closed.Contains(id))return; var s=executions[id]; int size=Math.Max((int)s["Length"],at-(int)s["Y"]+35); s["Length"]=size; s["Height"]=size; }
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
                live.ExceptWith(closed);
                foreach(var execution in executions.Where(v=>executionAliases[v.Key]==n.Left && !closed.Contains(v.Key)))
                { var bar=execution.Value; int length=live.Contains(execution.Key)?at-(int)bar["Y"]:Math.Min((int)bar["Length"],at-(int)bar["Y"]); bar["Length"]=length; bar["Height"]=length; }
                active.Remove(n.Left); activities.Remove(n.Left); pendingAlias=null;
                string id=Entity("Destruction",""); Owned("Destructions",id); Link("DestructionTargetLifeline",id,lifelines[n.Left],false,0);
                // The message just before, to the lane being destroyed, is its destroy message.
                string killer;
                int before=index-1;
                if(before>0 && items[before].Kind=="activate" && items[before].Left==n.Left)before--;
                if(profile.Relations.ContainsKey("DestroyMessage") && before>=0 && (items[before].Kind=="sync" || items[before].Kind=="async")
                    && items[before].Right==n.Left && lastReceived.TryGetValue(n.Left,out killer))
                    Link("DestroyMessage",id,killer,false,0);
                Shape("Destructions",id,"X",x[n.Left],"Y",at,"Width",20,"Height",20);
                payload.Expected.Add(new PumlExpected{Id=id,Kind="destruction",Left=lifelines[n.Left],Y=at});
                y+=20; continue;
            }
            if(n.Kind=="activate")
            {
                justEnded.Remove(n.Left);
                string previous; active.TryGetValue(n.Left,out previous);
                if(!activities.ContainsKey(n.Left))activities[n.Left]=new Stack<string>();
                string id=pendingAlias==n.Left?pendingExecution:Execution(n.Left,y-20);
                // A receive followed by activate opens that receive execution, not a second bar.
                if(id==previous)previous=activities[n.Left].Count>0?activities[n.Left].Peek():null;
                activities[n.Left].Push(previous); active[n.Left]=id;
                // Another lane's activate in between does not take the receive from this one, as the
                // reading has it; only this lane's activate or a deactivate does.
                if(pendingAlias==n.Left)pendingAlias=null; continue;
            }
            if(n.Kind=="deactivate")
            {
                string id=active[n.Left]; var bar=executions[id];
                if(!closed.Contains(id)){int length=Math.Max(MinimumBar,y-16-(int)bar["Y"]); bar["Length"]=length;bar["Height"]=length;justEnded[n.Left]=id;}
                string previous=activities[n.Left].Pop();
                if(previous==null)active.Remove(n.Left);else active[n.Left]=previous;
                pendingAlias=null; continue;
            }
            pendingAlias=null;
            if(n.Kind=="sync" || n.Kind=="async" || n.Kind=="reply")
            {
                y+=18*(n.Text.Split('\n').Length-1);
                bool incoming=n.Left=="[";
                string claimed=null,ask;
                if(n.Kind=="reply" && !incoming && justEnded.TryGetValue(n.Left,out claimed) && !(caller.TryGetValue(claimed,out ask) && ask==n.Right))claimed=null;
                string send; if(incoming)send=null; else if(claimed!=null)send=claimed; else if(!active.TryGetValue(n.Left,out send) || closed.Contains(send))active[n.Left]=send=Execution(n.Left,y-20);
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
                else if((n.Kind=="reply" || (!beginsActivation && activities.ContainsKey(n.Right) && activities[n.Right].Count>0)) && active.TryGetValue(n.Right,out receive) && !closed.Contains(receive)) { }
                else
                {
                    // An activation a reply has ended goes on in a new bar from here.
                    bool reopened=!beginsActivation && active.ContainsKey(n.Right) && closed.Contains(active[n.Right]);
                    receive=Execution(n.Right,targetY);
                    if(reopened)active[n.Right]=receive;
                }
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
                if(!outgoing && !incoming && !caller.ContainsKey(receive))caller[receive]=n.Left;
                pendingAlias=outgoing?null:n.Right; pendingExecution=receive;
                if(!incoming)Extend(send,y); if(!outgoing)Extend(receive,targetY);
                foreach(var pair in activities)if(pair.Value.Count>0 && active.ContainsKey(pair.Key))Extend(active[pair.Key],targetY);
                string id=Entity("Message",n.Text,Obj("Name",n.Text,"MessageSort",n.Kind=="reply"?profile.Reply
                    :profile.Destroy!=null && DestroysNext(items,index)?profile.Destroy
                    :n.Kind=="sync"?profile.Sync:profile.Async)); Owned("Messages",id);
                Link("SendMessage",send,id,false,0); Link("ReceiveMessage",receive,id,false,0);
                // Which reply closes its bar is only known once the bar has no later message;
                // the links are written after every item is placed.
                sent.Add(new[]{id,send,receive,n.Kind});
                justEnded.Remove(n.Left);justEnded.Remove(n.Right);
                // A reply ends the bar it leaves, a step under it, as a deactivate would.
                if(n.Kind=="reply" && send!=null && executions.ContainsKey(send) && (claimed!=null || (active.ContainsKey(n.Left) && active[n.Left]==send)))
                {
                    var ended=executions[send];int length=Math.Max(MinimumBar,targetY+MessagePitch-16-(int)ended["Y"]);
                    ended["Length"]=length;ended["Height"]=length;closed.Add(send);
                }
                Shape("Messages",id,"SourceY",y,"TargetY",targetY,"IsRightAtFrame",false,"SelfloopBendsX",self?Math.Max((int)executions[send]["X"],(int)executions[receive]["X"])+80:0);
                if(operand!=null)Link("OperandTargetMessage",operand,id,false,0);
                if(!outgoing)lastReceived[n.Right]=id;
                payload.Expected.Add(new PumlExpected{Id=id,Kind=n.Kind,Text=n.Text,Left=incoming?null:lifelines[n.Left],Right=outgoing?null:lifelines[n.Right],Owner=operand,SendPort=send,ReceivePort=receive,Y=y,EndY=targetY});
                y=targetY+MessagePitch; continue;
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
                string target;
                if(profile.Relations.ContainsKey("RefersTo") && profile.References.TryGetValue(n.Line,out target))Link("RefersTo",id,target);
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
            y+=Math.Max(48,16+20*n.Text.Split('\n').Length)+MessagePitch;
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
        // A bar is tied to the reply that closes it: the last message on that bar, when it is a
        // reply leaving it. Tied to a reply with more messages on the bar after it, the product
        // ends the bar there when it next lays the diagram out, and refuses every edit.
        // A bar no message uses has nothing to show, and Next Design fails laying one out
        // (FrameLayout.PlaceInducedExecutionSpecifications takes the first message of each bar).
        // Such bars go; the bars they held are drawn at their own depth again.
        // An empty bar right after a lane's call to itself, with nothing else on that lane in
        // between, is the bar that call arrived on (the PlantUML export writes it late; see
        // SequenceDocument.Parse).
        {
            var wires=b.shapes.ContainsKey("Messages")?b.shapes["Messages"].Cast<Dictionary<string,object>>().ToDictionary(m=>(string)m["ModelId"]):new Dictionary<string,Dictionary<string,object>>();
            Func<string,string> laneOf=bar=>bar!=null && b.executionAliases.ContainsKey(bar)?b.executionAliases[bar]:null;
            foreach(var pair in b.executions.OrderBy(v=>(int)v.Value["Y"]).ToList())
            {
                string bar=pair.Key,lane=laneOf(bar);
                if(b.sent.Any(m=>m[1]==bar || m[2]==bar))continue;
                int barTop=(int)pair.Value["Y"];
                var last=b.sent.LastOrDefault(m=>wires.ContainsKey(m[0]) && (int)wires[m[0]]["SourceY"]<barTop && (laneOf(m[1])==lane || laneOf(m[2])==lane));
                if(last==null || last[3]=="reply" || laneOf(last[1])!=lane)continue;
                if(laneOf(last[2])!=lane)
                {
                    // A bar the export writes after the send it opens with (see SequenceDocument.Parse).
                    foreach(var r in b.relations.Cast<Dictionary<string,object>>().Where(r=>(string)r["MetamodelId"]==profile.Relations["SendMessage"] && (string)r["SourceId"]==last[1] && (string)r["TargetId"]==last[0]))
                        r["SourceId"]=bar;
                    foreach(var expected in b.payload.Expected.Where(e=>e.Id==last[0]))expected.SendPort=bar;
                    last[1]=bar;
                    continue;
                }
                if(last[1]!=last[2])continue;
                foreach(var r in b.relations.Cast<Dictionary<string,object>>().Where(r=>(string)r["MetamodelId"]==profile.Relations["ReceiveMessage"] && (string)r["SourceId"]==last[2] && (string)r["TargetId"]==last[0]))
                    r["SourceId"]=bar;
                foreach(var expected in b.payload.Expected.Where(e=>e.Id==last[0]))expected.ReceivePort=bar;
                last[2]=bar;
            }
        }
        var used=new HashSet<string>(b.sent.SelectMany(m=>new[]{m[1],m[2]}).Where(x=>x!=null));
        foreach(string bar in b.executions.Keys.Where(k=>!used.Contains(k)).ToList())
        {
            b.entities.RemoveAll(e=>(string)((Dictionary<string,object>)e)["Id"]==bar);
            b.relations.RemoveAll(r=>(string)((Dictionary<string,object>)r)["SourceId"]==bar || (string)((Dictionary<string,object>)r)["TargetId"]==bar);
            b.shapes["ExecutionSpecifications"].Remove(b.executions[bar]);
            b.executions.Remove(bar);b.executionAliases.Remove(bar);
        }
        foreach(var group in b.relations.Cast<Dictionary<string,object>>().GroupBy(r=>(string)r["MetamodelId"]+"|"+(string)r["SourceId"]))
        {int order=0;foreach(var r in group)r["SourceIndex"]=order++;}
        // Draw each bar the way Next Design lays it out once the diagram is edited (K194), so the
        // first touch does not reshape it: from its first message to the reply that closes it,
        // or 20 under the later of its last message and the bars its calls opened. A bar the
        // destruction of its lane ends keeps that end.
        {
            var wire=b.shapes.ContainsKey("Messages")?b.shapes["Messages"].Cast<Dictionary<string,object>>().ToDictionary(m=>(string)m["ModelId"]):new Dictionary<string,Dictionary<string,object>>();
            // Where each lane is destroyed, by the lane's x.
            var ends=b.shapes.ContainsKey("Destructions")?b.shapes["Destructions"].Cast<Dictionary<string,object>>().Select(d=>new{X=(int)d["X"],Y=(int)d["Y"]}).ToList():new[]{new{X=0,Y=0}}.Take(0).ToList();
            // Each end a bar takes, a message to itself counting twice.
            var uses=b.executions.Keys.ToDictionary(k=>k,k=>b.sent.Where(m=>m[1]==k).Select(m=>new{Id=m[0],Send=true,Kind=m[3],At=(int)wire[m[0]]["SourceY"],Callee=m[2]})
                .Concat(b.sent.Where(m=>m[2]==k).Select(m=>new{Id=m[0],Send=false,Kind=m[3],At=(int)wire[m[0]]["TargetY"],Callee=(string)null})).OrderBy(u=>u.At).ToList());
            // A bar's end, and whether a destruction made it (such an end does not carry up to the
            // bar that called it; how the product treats that is not measured).
            var bottoms=new Dictionary<string,Tuple<int,bool>>();
            Func<string,int,Tuple<int,bool>> bottom=null;
            bottom=(bar,depth)=>{
                Tuple<int,bool> known;if(bottoms.TryGetValue(bar,out known))return known;
                var list=uses[bar];var last=list.Last();Tuple<int,bool> end;
                if(last.Send && last.Kind=="reply")end=Tuple.Create(last.At,false);
                else
                {
                    int point=last.At,reach=last.At;
                    foreach(var u in list.Where(u=>u.Send && u.Kind!="reply" && u.Callee!=null && u.Callee!=bar && uses.ContainsKey(u.Callee) && uses[u.Callee].Count>0 && uses[u.Callee][0].Id==u.Id))
                    {
                        if(depth>=64)continue;
                        var theirs=bottom(u.Callee,depth+1);
                        if(!theirs.Item2)reach=Math.Max(reach,theirs.Item1);
                    }
                    end=Tuple.Create(reach>point?reach+20:point+20,false);
                    int laneX=b.x[b.executionAliases[bar]];
                    var destroy=ends.Where(d=>d.X==laneX && d.Y>point).OrderBy(d=>d.Y).FirstOrDefault();
                    if(destroy!=null && !uses.Any(o=>o.Key!=bar && b.executionAliases[o.Key]==b.executionAliases[bar] && o.Value.Count>0 && o.Value[0].At>point && o.Value[0].At<destroy.Y))
                        end=Tuple.Create(destroy.Y,true);
                }
                bottoms[bar]=end;return end;
            };
            foreach(var pair in b.executions)
            {
                if(uses[pair.Key].Count==0)continue;
                int top=uses[pair.Key][0].At,end=bottom(pair.Key,0).Item1;
                pair.Value["Y"]=top;pair.Value["Length"]=end-top;pair.Value["Height"]=end-top;
            }
        }
        var opened=b.executions.Keys.ToList();
        foreach(var pair in b.executions)
        {
            string lane=b.executionAliases[pair.Key];int top=(int)pair.Value["Y"],bottom=top+(int)pair.Value["Length"];
            int depth=b.executions.Count(o=>o.Key!=pair.Key && b.executionAliases[o.Key]==lane && (int)o.Value["Y"]<=top && (int)o.Value["Y"]+(int)o.Value["Length"]>=bottom
                && ((int)o.Value["Y"]<top || (int)o.Value["Y"]+(int)o.Value["Length"]>bottom || opened.IndexOf(o.Key)<opened.IndexOf(pair.Key)));
            pair.Value["X"]=b.x[lane]+8*depth;
        }
        if(profile.Relations.ContainsKey("ReplyMessage"))
            foreach(string bar in b.executions.Keys)
            {
                var last=b.sent.LastOrDefault(m=>m[1]==bar || m[2]==bar);
                if(last!=null && last[3]=="reply" && last[1]==bar)b.Link("ReplyMessage",bar,last[0],false,0);
            }
        foreach(var pair in b.executions)p.Expected.Add(new PumlExpected{Id=pair.Key,Kind="execution",Y=(int)pair.Value["Y"],EndY=(int)pair.Value["Y"]+(int)pair.Value["Length"]});
        // A diagram with no lane yet has no timeline to stretch.
        if(b.shapes.ContainsKey("Lifelines"))foreach(var s in b.shapes["Lifelines"].Cast<Dictionary<string,object>>())s["LaneLength"]=b.y+40;
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
    // Next Design keeps no extent of its own for a bar: once the diagram is edited it lays each
    // bar out from its messages (measured, K194). A bar starts at its first message; a bar a
    // reply leaving it ends at that reply; a bar on a lane destroyed after its last message ends
    // at the destruction; any other bar ends just under the later of its last message and the
    // ends of the bars its calls opened. Where the bar starts and ends, what holds it, and the
    // bar around it all follow from that, for the input and for the diagram alike. `at` places
    // each element that is not a bar or participant on one vertical scale.
    public void SettleExecutions(Func<SequenceElement,double> at)
    {
        Func<SequenceElement,string,string[]> link=(e,key)=>{string[] v;return e.Links.TryGetValue(key,out v)?v:new string[0];};
        Func<SequenceElement,string> sort=e=>{string v;return e.Attributes.TryGetValue("sort",out v)?v:"";};
        var events=Elements.Where(n=>n.Kind!="participant" && n.Kind!="interaction" && n.Kind!="execution")
            .Select((n,i)=>new{n,i}).OrderBy(x=>at(x.n)).ThenBy(x=>x.i).Select(x=>x.n).ToList();
        var rank=events.Select((n,i)=>new{n,i}).ToDictionary(x=>x.n.Id,x=>(double)x.i);
        var bars=Elements.Where(e=>e.Kind=="execution").ToList();
        var uses=bars.ToDictionary(b=>b.Id,b=>events.Where(n=>n.Kind=="message"
            && (link(n,"sendExecution").Contains(b.Id) || link(n,"receiveExecution").Contains(b.Id))).ToList());
        var ends=new Dictionary<string,Tuple<SequenceElement,int>>();
        Func<SequenceElement,int,Tuple<SequenceElement,int>> end=null;
        end=(bar,depth)=>{
            Tuple<SequenceElement,int> known;if(ends.TryGetValue(bar.Id,out known))return known;
            var list=uses[bar.Id];var last=list.Last();
            Tuple<SequenceElement,int> result;
            if(link(last,"sendExecution").Contains(bar.Id) && sort(last)=="reply")result=Tuple.Create(last,0);
            else
            {
                var point=last;int extra=1;
                foreach(var u in list.Where(u=>link(u,"sendExecution").Contains(bar.Id) && sort(u)!="reply"))
                    foreach(var callee in bars.Where(c=>c.Id!=bar.Id && link(u,"receiveExecution").Contains(c.Id) && uses[c.Id].Count>0 && uses[c.Id][0]==u))
                    {
                        if(depth>64)continue;
                        var theirs=end(callee,depth+1);
                        // How far a destruction under the called bar reaches back up is not measured.
                        if(theirs.Item1.Kind!="message")continue;
                        if(rank[theirs.Item1.Id]>rank[point.Id] || (theirs.Item1==point && theirs.Item2+1>extra)){point=theirs.Item1;extra=theirs.Item2+1;}
                    }
                result=Tuple.Create(point,extra);
                string[] lane=link(bar,"participant");
                var destroy=events.FirstOrDefault(n=>n.Kind=="destroy" && lane.Length==1 && link(n,"participant").Contains(lane[0]) && rank[n.Id]>rank[point.Id]);
                if(destroy!=null && !bars.Any(o=>o.Id!=bar.Id && link(o,"participant").SequenceEqual(lane) && uses[o.Id].Count>0
                        && rank[uses[o.Id][0].Id]>rank[point.Id] && rank[uses[o.Id][0].Id]<rank[destroy.Id]))
                    result=Tuple.Create(destroy,0);
            }
            ends[bar.Id]=result;return result;
        };
        var span=new Dictionary<string,double[]>();
        foreach(var bar in bars.Where(b=>uses[b.Id].Count>0))
        {
            var first=uses[bar.Id][0];var close=end(bar,0);
            bool receives=link(first,"receiveExecution").Contains(bar.Id);
            int before=(int)rank[first.Id]-1;
            bar.Parent=first.Parent;
            bar.Links["startAfter"]=receives?new[]{first.Id}:before>=0?new[]{events[before].Id}:new string[0];
            int after=(int)rank[close.Item1.Id]+1;
            bar.Links["endBefore"]=after<events.Count?new[]{events[after].Id}:new string[0];
            bar.Links["endContainer"]=new[]{close.Item1.Parent};
            // A receive-first bar starts just under its message; ends reached through calls sit
            // a step lower per call, as the product adds its margin at each.
            span[bar.Id]=new[]{rank[first.Id]+(receives?0.001:0),rank[close.Item1.Id]+0.01*close.Item2};
        }
        foreach(var bar in bars.Where(b=>span.ContainsKey(b.Id)))
        {
            var mine=span[bar.Id];string[] lane=link(bar,"participant");
            var around=bars.Where(o=>o.Id!=bar.Id && span.ContainsKey(o.Id) && link(o,"participant").SequenceEqual(lane)
                    && span[o.Id][0]<=mine[0] && span[o.Id][1]>=mine[1] && (span[o.Id][0]<mine[0] || span[o.Id][1]>mine[1]))
                .OrderBy(o=>span[o.Id][1]-span[o.Id][0]).FirstOrDefault();
            if(around!=null)bar.Links["outer"]=new[]{around.Id};else bar.Links.Remove("outer");
        }
        // Order within each owner follows the same scale, a bar just ahead of its first message.
        Func<SequenceElement,double> place=e=>e.Kind=="execution"?(span.ContainsKey(e.Id)?span[e.Id][0]-0.5:double.MaxValue):rank.ContainsKey(e.Id)?rank[e.Id]:double.MaxValue;
        foreach(var group in Elements.Where(e=>e.Parent!=null && e.Kind!="participant").GroupBy(e=>e.Parent))
        {
            var members=group.ToList();int order=members.Min(e=>e.Order);
            foreach(var e in members.OrderBy(place).ThenBy(e=>e.Order).ToList())e.Order=order++;
        }
    }
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
        // A synchronous call blocks the bar that sent it until the bar it opened ends: Next Design
        // refuses every edit to a diagram where that bar sends again meanwhile. Keyed by the
        // sending bar: the bar the call opened and its lane.
        var waiting=new Dictionary<string,Tuple<SequenceElement,string>>();
        Action<SequenceElement> awaitAnswer=message=>{
            string[] from,to;
            if(message.Attributes["sort"]!="sync" || !message.Links.TryGetValue("sendExecution",out from) || !message.Links.TryGetValue("receiveExecution",out to))return;
            var opened=result.Elements.First(x=>x.Id==to[0]);
            waiting[from[0]]=Tuple.Create(opened,result.Elements.First(x=>x.Id==message.Links["receiver"][0]).Id);
        };
        Action<IEnumerable<PumlNode>,string> visit=null;
        visit=(nodes,parent)=>{
            int order=0;SequenceElement previousEvent=null;
            var orderedNodes=nodes.ToArray();
            // A reply ends the bar it leaves: Next Design refuses every edit to a diagram whose bar
            // goes on after a reply. An activation the input keeps open past a reply goes on in a
            // bar of its own from the next message on that lane, under the same outer bar.
            Func<SequenceElement,bool> closed=e=>e.Attributes.ContainsKey("closed");
            // A bar just deactivated, per lane: a reply to its caller answers that call and leaves
            // from it, even when the input deactivates the bar first.
            var justEnded=new Dictionary<string,SequenceElement>();
            Func<string,int,SequenceElement> reopen=(alias,line)=>{
                var was=active[alias].Pop();
                var e=new SequenceElement{Id="e"+(next++),Kind="execution",Parent=parent,Order=order++,Line=line};
                e.Links["participant"]=new[]{aliases[alias]};e.Attributes["endParent"]=parent;
                e.Attributes["start"]=line.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if(was.Links.ContainsKey("outer"))e.Links["outer"]=was.Links["outer"].ToArray();
                result.Elements.Add(e);active[alias].Push(e);
                return e;
            };
            for(int nodeIndex=0;nodeIndex<orderedNodes.Length;nodeIndex++)
            {
                var n=orderedNodes[nodeIndex];
                if(n.Kind=="activate")
                {
                    var e=new SequenceElement{Id="e"+(next++),Kind="execution",Parent=parent,Order=order++,Line=n.Line};
                    e.Links["participant"]=new[]{aliases[n.Left]};e.Attributes["endParent"]=parent;
                    e.Attributes["start"]=n.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    result.Elements.Add(e);justEnded.Remove(n.Left);
                    if(!active.ContainsKey(n.Left))active[n.Left]=new Stack<SequenceElement>();
                    // A bar a reply has ended holds nothing more, so nothing nests in it.
                    if(active[n.Left].Count>0 && !closed(active[n.Left].Peek()))e.Links["outer"]=new[]{active[n.Left].Peek().Id};
                    active[n.Left].Push(e);
                    if(previousEvent!=null && previousEvent.Kind=="message" && previousEvent.Links["receiver"].SequenceEqual(new[]{aliases[n.Left]}))
                    {
                        previousEvent.Links["receiveExecution"]=new[]{e.Id};awaitAnswer(previousEvent);
                        if(previousEvent.Links["sender"].Length==1)e.Attributes["caller"]=previousEvent.Links["sender"][0];
                    }
                    continue;
                }
                if(n.Kind=="deactivate")
                {
                    if(!active.ContainsKey(n.Left) || active[n.Left].Count==0)
                        throw new InvalidOperationException("S202: "+n.Line+"行目のdeactivateに対応する開始がありません。");
                    var e=active[n.Left].Pop();previousEvent=null;
                    // A bar a reply has already ended keeps that end.
                    if(closed(e))continue;
                    justEnded[n.Left]=e;
                    e.Attributes["endParent"]=parent;
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
                    // A receive the next line activates on gets that new bar instead; nothing reopens for it.
                    bool activates=nodeIndex+1<orderedNodes.Length && orderedNodes[nodeIndex+1].Kind=="activate" && orderedNodes[nodeIndex+1].Left==n.Right;
                    SequenceElement answered;
                    if(n.Kind=="reply" && n.Left!="[" && n.Right!="]" && justEnded.TryGetValue(n.Left,out answered)
                        && answered.Attributes.ContainsKey("caller") && answered.Attributes["caller"]==aliases[n.Right])
                    {
                        // The reply answers the call that bar received: it leaves from it and ends it.
                        item.Links["sendExecution"]=new[]{answered.Id};
                        answered.Attributes["closed"]="1";answered.Attributes["endParent"]=parent;
                        answered.Attributes["end"]=n.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        item.Attributes["answers"]="1";
                    }
                    foreach(var endpoint in new[]{new[]{"sendExecution",n.Left},new[]{"receiveExecution",n.Right}})
                    {
                        if(item.Links.ContainsKey(endpoint[0]))continue;
                        if(!active.ContainsKey(endpoint[1]) || active[endpoint[1]].Count==0)continue;
                        var top=active[endpoint[1]].Peek();
                        if(closed(top))
                        {
                            if(endpoint[0]=="receiveExecution" && activates)continue;
                            top=reopen(endpoint[1],n.Line);
                            // A bar opened by the message it receives starts after that message, as
                            // the reader takes the message it receives at its top.
                            if(endpoint[0]=="receiveExecution")top.Attributes["opener"]=item.Id;
                        }
                        item.Links[endpoint[0]]=new[]{top.Id};
                        if(endpoint[0]=="receiveExecution" && !top.Attributes.ContainsKey("caller") && item.Links["sender"].Length==1)top.Attributes["caller"]=item.Links["sender"][0];
                    }
                    Tuple<SequenceElement,string> blocked;string[] sending;
                    if(item.Links.TryGetValue("sendExecution",out sending) && waiting.TryGetValue(sending[0],out blocked))
                    {
                        string lane=aliases.First(a=>a.Value==blocked.Item2).Key;
                        if(active.ContainsKey(lane) && active[lane].Contains(blocked.Item1) && !closed(blocked.Item1))
                            throw new InvalidOperationException("S204: "+n.Line+"行目: 同期呼び出しの応答を待っている実行区間からは送れません（呼び出し先の実行区間が終わってから送ってください）。");
                        waiting.Remove(sending[0]);
                    }
                    string[] opening;
                    if(item.Links.TryGetValue("receiveExecution",out opening) && result.Elements.Any(x=>x.Id==opening[0] && x.Attributes.ContainsKey("opener") && x.Attributes["opener"]==item.Id))
                        awaitAnswer(item);
                    if(n.Left!="[")justEnded.Remove(n.Left);
                    if(n.Right!="]")justEnded.Remove(n.Right);
                }
                // A self reply closing the innermost activation returns to its
                // caller. Keep the sender on the inner bar; do not pop until deactivate.
                if(n.Kind=="reply" && n.Left==n.Right && active.ContainsKey(n.Left) && active[n.Left].Count>1
                    && nodeIndex+1<orderedNodes.Length && orderedNodes[nodeIndex+1].Kind=="deactivate" && orderedNodes[nodeIndex+1].Left==n.Left)
                    item.Links["receiveExecution"]=new[]{active[n.Left].Skip(1).First().Id};
                // The reply ends the bar it leaves.
                if(item.Attributes.ContainsKey("answers"))item.Attributes.Remove("answers");
                else if(n.Kind=="reply" && item.Links.ContainsKey("sendExecution") && active.ContainsKey(n.Left) && active[n.Left].Count>0
                    && active[n.Left].Peek().Id==item.Links["sendExecution"][0])
                {
                    var ended=active[n.Left].Peek();
                    ended.Attributes["closed"]="1";ended.Attributes["endParent"]=parent;
                    ended.Attributes["end"]=n.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                // The exporter writes a destruction message as -> followed by
                // destroy of that receiver. Recover its kind, retaining the destroy event.
                // An activate of the receiver may come between: the bar it opens is the one
                // the destroy ends.
                int after=nodeIndex+1;
                if(after<orderedNodes.Length && orderedNodes[after].Kind=="activate" && orderedNodes[after].Left==n.Right)after++;
                if(item.Kind=="message" && n.Kind=="sync" && after<orderedNodes.Length
                    && orderedNodes[after].Kind=="destroy" && orderedNodes[after].Left==n.Right)
                    item.Attributes["sort"]="destroy";
                if(n.Kind=="fragment") {item.Attributes["operator"]=n.Operator;if(n.Operator!="group")item.Text="";}
                if(n.Kind=="note" || n.Kind=="ref") { item.Links["targets"]=n.Targets.Select(t=>aliases[t]).ToArray();if(n.Kind=="note")item.Attributes["position"]=n.Operator; }
                if(n.Kind=="destroy" || n.Kind=="create")item.Links["participant"]=new[]{aliases[n.Left]};
                // Destroying a lane ends its open bars there, as the diagram draws them.
                if(n.Kind=="destroy" && active.ContainsKey(n.Left))
                    while(active[n.Left].Count>0)
                    {
                        var ending=active[n.Left].Pop();
                        if(closed(ending))continue;
                        ending.Attributes["endParent"]=parent;
                        ending.Attributes["end"]=n.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                result.Elements.Add(item);visit(n.Children,item.Id);previousEvent=item;
            }
        };
        visit(parsed.Nodes,"root");
        // A bar no message uses has nothing to show in Next Design, which fails laying one out;
        // the generator leaves it out, and so does the reading. What it held nests one level up.
        // The PlantUML export orders a bar by its top edge. The bar a call to itself opens starts
        // where that call arrives, a little under where it leaves, so another lane's message in
        // between pushes its activate/deactivate after that message, with nothing inside. Such an
        // empty bar is the one that call arrived on: the lane's last message before it is that
        // call to itself, and the lane has done nothing since.
        {
            Func<SequenceElement,string,string[]> links=(e,key)=>{string[] v;return e.Links.TryGetValue(key,out v)?v:new string[0];};
            var messages=result.Elements.Where(e=>e.Kind=="message").OrderBy(e=>e.Line).ToList();
            foreach(var bar in result.Elements.Where(e=>e.Kind=="execution").OrderBy(e=>int.Parse(e.Attributes["start"],System.Globalization.CultureInfo.InvariantCulture)).ToList())
            {
                string lane=links(bar,"participant").FirstOrDefault();
                if(lane==null || messages.Any(m=>links(m,"sendExecution").Contains(bar.Id) || links(m,"receiveExecution").Contains(bar.Id)))continue;
                int start=int.Parse(bar.Attributes["start"],System.Globalization.CultureInfo.InvariantCulture);
                var last=messages.LastOrDefault(m=>m.Line<start && (links(m,"sender").Contains(lane) || links(m,"receiver").Contains(lane)));
                if(last==null || !links(last,"sender").Contains(lane))continue;
                string sort;last.Attributes.TryGetValue("sort",out sort);
                if(sort=="reply")continue;
                if(!links(last,"receiver").Contains(lane))
                {
                    // The export also holds back the activate of a bar that opens with a send,
                    // when the lane has just deactivated another: it comes after that send. The
                    // empty bar is the one the send left from.
                    var left=links(last,"sendExecution");
                    if(left.Length==1 && left[0]==bar.Id)continue;
                    last.Links["sendExecution"]=new[]{bar.Id};
                    continue;
                }
                var arrived=links(last,"receiveExecution");
                // It arrived on a bar the lane already had, not one it opened.
                if(arrived.Length!=1 || arrived[0]==bar.Id || !links(last,"sendExecution").Contains(arrived[0]))continue;
                last.Links["receiveExecution"]=new[]{bar.Id};
                bar.Attributes["opener"]=last.Id;
            }
        }
        {
            var used=new HashSet<string>(result.Elements.Where(e=>e.Kind=="message")
                .SelectMany(e=>new[]{"sendExecution","receiveExecution"}.Where(e.Links.ContainsKey).SelectMany(r=>e.Links[r])));
            var empty=result.Elements.Where(e=>e.Kind=="execution" && !used.Contains(e.Id)).ToDictionary(e=>e.Id);
            foreach(var e in result.Elements.Where(e=>e.Kind=="execution" && !empty.ContainsKey(e.Id)))
            {
                string[] outer;
                while(e.Links.TryGetValue("outer",out outer) && outer.Length==1 && empty.ContainsKey(outer[0]))
                {
                    string[] up;
                    if(empty[outer[0]].Links.TryGetValue("outer",out up) && up.Length==1)e.Links["outer"]=up.ToArray();
                    else e.Links.Remove("outer");
                }
            }
            result.Elements.RemoveAll(e=>empty.ContainsKey(e.Id));
        }
        foreach(var e in result.Elements.Where(e=>e.Kind=="execution"))
        {
            // Placeholders; SettleExecutions below decides every bar's extent from its messages.
            int start=int.Parse(e.Attributes["start"],System.Globalization.CultureInfo.InvariantCulture);
            int end=e.Attributes.ContainsKey("end")?int.Parse(e.Attributes["end"],System.Globalization.CultureInfo.InvariantCulture):int.MaxValue;
            var events=result.Elements.Where(n=>n.Kind!="participant" && n.Kind!="interaction" && n.Kind!="execution").OrderBy(n=>n.Line).ToArray();
            var preceding=events.LastOrDefault(n=>n.Line<start);var following=events.FirstOrDefault(n=>n.Line>end);
            if(e.Attributes.ContainsKey("opener"))preceding=events.First(n=>n.Id==e.Attributes["opener"]);
            e.Links["startAfter"]=preceding==null?new string[0]:new[]{preceding.Id};
            e.Links["endBefore"]=following==null?new string[0]:new[]{following.Id};
            string endParent=e.Attributes["endParent"];e.Links["endContainer"]=new[]{endParent};
            e.Attributes.Clear();
        }
        result.SettleExecutions(e=>e.Line);
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
    // An input that says nothing about a message endpoint keeps whatever the diagram has.
    // InheritedPorts is {message, role, execution} in model ids; CarriedExecutions are the
    // bars that only the diagram knows about; InheritRefusals says where that was declined.
    public List<string[]> InheritedPorts=new List<string[]>();
    public List<string> CarriedExecutions=new List<string>();
    public List<string> InheritRefusals=new List<string>();
    public bool IsEmpty { get { return Changes.Count==0; } }
    public string ToJson()
    {
        return PumlBuild.Json(PumlBuild.Obj("Recreated",Recreated,"Identities",Identities.ToDictionary(p=>p.Key,p=>(object)p.Value),
            "InheritedPorts",InheritedPorts.Count,"CarriedExecutions",CarriedExecutions.Count,"InheritRefusals",InheritRefusals.Count,
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
        // Between two elements already matched, the unmatched ones of a kind on each side
        // pair up when there is exactly one on each side and it has the same shape
        // (for a message: sort, sender and receiver). That is an edit in place next to an
        // addition or removal elsewhere, which the single-candidate rule above cannot see.
        Func<SequenceElement,SequenceElement,bool> sameShape=(x,y)=>{
            if(x.Kind!=y.Kind)return false;
            if(x.Kind!="message")return true;
            string sx,sy;x.Attributes.TryGetValue("sort",out sx);y.Attributes.TryGetValue("sort",out sy);
            if(sx!=sy)return false;
            foreach(string role in new[]{"sender","receiver"})
            {
                string[] u,v;x.Links.TryGetValue(role,out u);y.Links.TryGetValue(role,out v);u=u??new string[0];v=v??new string[0];
                if(u.Length!=v.Length || u.Where((id,i)=>!map.ContainsKey(id) || map[id]!=v[i]).Any())return false;
            }
            return true;
        };
        foreach(var parent in desired.Elements.Where(e=>map.ContainsKey(e.Id)).ToArray())
        {
            var a=desired.Elements.Where(e=>e.Parent==parent.Id && e.Kind!="execution").OrderBy(e=>e.Order).ToArray();
            var b=current.Elements.Where(e=>e.Parent==map[parent.Id] && e.Kind!="execution").OrderBy(e=>e.Order).ToArray();
            int i=0,j=0;
            while(i<=a.Length && j<=b.Length)
            {
                int ni=i;while(ni<a.Length && !map.ContainsKey(a[ni].Id))ni++;
                int nj=ni<a.Length?Array.FindIndex(b,e=>e.Id==map[a[ni].Id]):b.Length;
                if(nj<j)break;
                var ua=a.Skip(i).Take(ni-i).ToArray();var ub=b.Skip(j).Take(nj-j).Where(e=>!used.Contains(e.Id)).ToArray();
                foreach(string kind in ua.Select(e=>e.Kind).Distinct().ToArray())
                {
                    var xa=ua.Where(e=>e.Kind==kind && !map.ContainsKey(e.Id)).ToArray();var xb=ub.Where(e=>e.Kind==kind && !used.Contains(e.Id)).ToArray();
                    // Changed messages side by side pair up in order when there are as many on each side
                    // and each pair runs between the same lanes the same way. Otherwise they are
                    // recreated: a new id, and whatever referred to the old one does not follow.
                    if(xa.Length==0 || xa.Length!=xb.Length || (xa.Length>1 && kind!="message") || xa.Where((x,k)=>!sameShape(x,xb[k])).Any())continue;
                    for(int k=0;k<xa.Length;k++)bind(xa[k],xb[k]);
                }
                i=ni+1;j=nj+1;
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
        // An input that never opens a bar on one side of a message says nothing about that
        // endpoint. Drop exactly those role tokens from the diagram side too, so the bar is
        // still identified by what the input did state. Tokens of deleted messages stay:
        // dropping them would hide a real change of connection.
        Func<SequenceElement,string> restricted=execution=>{
            var tokens=new List<string>();
            foreach(var message in current.Elements.Where(e=>e.Kind=="message"))foreach(var role in new[]{"sendExecution","receiveExecution"})
            {
                string[] ids;if(!message.Links.TryGetValue(role,out ids) || !ids.Contains(execution.Id))continue;
                var stated=desired.Elements.FirstOrDefault(e=>map.ContainsKey(e.Id) && map[e.Id]==message.Id);
                if(stated!=null && !stated.Links.ContainsKey(role))continue;
                tokens.Add(role+":"+message.Id);
            }
            return tokens.Count==0?null:string.Join("|",tokens.OrderBy(v=>v,StringComparer.Ordinal));
        };
        foreach(var a in desired.Elements.Where(e=>e.Kind=="execution" && !map.ContainsKey(e.Id)))
        {
            string key=incident(desired,a,true);if(key==null)continue;
            var candidates=current.Elements.Where(b=>b.Kind=="execution" && !used.Contains(b.Id) && Comparable(a,b,map) && restricted(b)==key).ToArray();
            int inputs=desired.Elements.Count(b=>b.Kind=="execution" && !map.ContainsKey(b.Id) && incident(desired,b,true)==key);
            if(candidates.Length==1 && inputs==1)bind(a,candidates[0]);
        }
        // A bar whose messages are split or joined keeps its id when it shares the most messages
        // with one bar of the diagram, and that bar shares the most with it: the product lays a
        // bar out from its messages, so the bar is what those messages hang on.
        {
            Func<string,HashSet<string>> tokens=key=>key==null?new HashSet<string>():new HashSet<string>(key.Split('|'));
            // The message a bar starts with, as a diagram message id: sharing it decides a tie.
            Func<SequenceDocument,SequenceElement,bool,string> head=(doc,bar,input)=>{
                var m=doc.Elements.FirstOrDefault(e=>e.Kind=="message" && new[]{"sendExecution","receiveExecution"}.Any(r=>e.Links.ContainsKey(r) && e.Links[r].Contains(bar.Id)));
                return m==null?null:input?(map.ContainsKey(m.Id)?map[m.Id]:null):m.Id;
            };
            var open=desired.Elements.Where(e=>e.Kind=="execution" && !map.ContainsKey(e.Id)).Select(e=>new{e,t=tokens(incident(desired,e,true)),h=head(desired,e,true)}).Where(x=>x.t.Count>0).ToList();
            var free=current.Elements.Where(b=>b.Kind=="execution" && !used.Contains(b.Id)).Select(b=>new{b,t=tokens(restricted(b)),h=head(current,b,false)}).Where(x=>x.t.Count>0).ToList();
            Func<HashSet<string>,string,HashSet<string>,string,int> score=(a,ah,b,bh)=>{int n=a.Count(b.Contains);return n==0?0:2*n+(ah!=null && ah==bh?1:0);};
            foreach(var a in open)
            {
                var best=free.Where(x=>!used.Contains(x.b.Id) && Comparable(a.e,x.b,map)).Select(x=>new{x.b,n=score(a.t,a.h,x.t,x.h)}).Where(x=>x.n>0).OrderByDescending(x=>x.n).ToList();
                if(best.Count==0 || (best.Count>1 && best[1].n==best[0].n))continue;
                var theirs=free.First(x=>x.b==best[0].b);
                if(open.Any(o=>o!=a && !map.ContainsKey(o.e.Id) && Comparable(o.e,theirs.b,map) && score(o.t,o.h,theirs.t,theirs.h)>=best[0].n))continue;
                bind(a.e,best[0].b);
            }
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
        // Decide, in model ids, which endpoints the input left unspecified. Only links that
        // already exist in the diagram are carried over; nothing is invented.
        var inherit=new List<string[]>();var carry=new HashSet<string>();
        Func<SequenceElement,string,string[]> link=(e,role)=>{string[] v;return e.Links.TryGetValue(role,out v)?v:new string[0];};
        foreach(var a in desired.Elements.Where(e=>e.Kind=="message" && map.ContainsKey(e.Id)))
        {
            SequenceElement before;if(!old.TryGetValue(map[a.Id],out before))continue;
            foreach(string role in new[]{"sendExecution","receiveExecution"})
            {
                if(a.Links.ContainsKey(role))continue;
                string[] source;if(!before.Links.TryGetValue(role,out source) || source.Length!=1)continue;
                var end=link(a,role=="sendExecution"?"sender":"receiver").Select(id=>map.ContainsKey(id)?map[id]:null).ToArray();
                if(end.Length!=1 || end[0]==null)continue;
                SequenceElement bar;
                if(!old.TryGetValue(source[0],out bar) || bar.Kind!="execution")continue;
                // Keeping a bar must not paper over a message that now starts or lands elsewhere.
                if(!link(bar,"participant").SequenceEqual(end))continue;
                inherit.Add(new string[]{map[a.Id],role,source[0]});
            }
        }
        // An input that leaves a bar unwritten says nothing about what nests inside it
        // either, so a kept bar takes its nesting from the diagram as well. If the input
        // does mention that outer bar and still drops the link, the change is real.
        foreach(var a in desired.Elements.Where(e=>e.Kind=="execution" && map.ContainsKey(e.Id)))
        {
            if(a.Links.ContainsKey("outer"))continue;
            SequenceElement before;
            if(!old.TryGetValue(map[a.Id],out before))continue;
            string[] source;
            if(!before.Links.TryGetValue("outer",out source) || source.Length!=1 || map.ContainsValue(source[0]))continue;
            SequenceElement bar;
            if(!old.TryGetValue(source[0],out bar) || bar.Kind!="execution")continue;
            if(!link(bar,"participant").SequenceEqual(link(before,"participant")))continue;
            // An outer bar no message uses only nests what the input now draws on its own; keeping
            // it would wrap the input's bars in one it never asked for.
            if(!current.Elements.Any(m=>m.Kind=="message" && (link(m,"sendExecution").Contains(bar.Id) || link(m,"receiveExecution").Contains(bar.Id))))continue;
            inherit.Add(new string[]{map[a.Id],"outer",source[0]});
        }
        Func<string,bool> stays=id=>map.ContainsValue(id) || carry.Contains(id);
        foreach(var row in inherit.ToArray())
        {
            var chain=new List<string>();string reason=null;
            for(string at=row[2];at!=null && !map.ContainsValue(at) && !carry.Contains(at);)
            {
                SequenceElement bar;
                if(!old.TryGetValue(at,out bar) || chain.Contains(at)) {reason="実行区間をたどれません";break;}
                chain.Add(at);
                var next=link(bar,"outer");
                at=next.Length==1?next[0]:null;
                if(next.Length>1)reason="入れ子の指定が1件ではありません";
            }
            if(reason==null)
                foreach(string id in chain)
                {
                    var bar=old[id];
                    foreach(string role in new[]{"participant","endContainer"})
                        if(link(bar,role).Any(target=>!stays(target) && !chain.Contains(target)))reason="保持する実行区間の"+role+"が残りません";
                    if(bar.Parent!=null && !stays(bar.Parent) && !chain.Contains(bar.Parent))reason="保持する実行区間の所有先が残りません";
                }
            if(reason!=null) {inherit.Remove(row);plan.InheritRefusals.Add(reason);continue;}
            foreach(string id in chain)carry.Add(id);
        }
        foreach(string id in carry)used.Add(id);
        plan.InheritedPorts.AddRange(inherit);plan.CarriedExecutions.AddRange(carry);
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
        if(carry.Count>0 || inherit.Count>0)
        {
            var expected=plan.Expected.Elements.ToDictionary(e=>e.Id);
            foreach(string id in carry)
            {
                var copy=old[id].Copy();
                // The anchors name neighbouring events, not model fields. Ones the input
                // removes simply go; nothing is substituted for them.
                foreach(string anchor in new[]{"startAfter","endBefore"})
                {
                    string[] targets;if(!copy.Links.TryGetValue(anchor,out targets))continue;
                    copy.Links[anchor]=targets.Where(target=>expected.ContainsKey(target) || carry.Contains(target)).ToArray();
                }
                plan.Expected.Elements.Add(copy);expected.Add(copy.Id,copy);
            }
            foreach(var row in inherit)
            {
                SequenceElement message;
                if(expected.TryGetValue(row[0],out message))message.Links[row[1]]=new string[]{row[2]};
            }
        }
        // A branch that moves to another frame, or a destruction that moves to another lane,
        // cannot be moved in place: what owns it is fixed. It is made again under a new id and
        // the old one goes, which the changes below then say.
        foreach(var e in plan.Expected.Elements.ToArray())
        {
            SequenceElement was;
            if(!old.TryGetValue(e.Id,out was))continue;
            string[] a,b;
            // A branch that something leaves for the top level is made again too: the product will
            // not untie a branch from a message or frame (the relation is system-defined), but it
            // drops those ties when the branch is deleted.
            string rootId=plan.Expected.Elements.Single(x=>x.Kind=="interaction").Id;
            bool left=e.Kind=="operand" && current.Elements.Any(x=>x.Parent==e.Id && (x.Kind=="message" || x.Kind=="fragment" || x.Kind=="ref")
                && plan.Expected.Elements.Any(y=>y.Id==x.Id && y.Parent==rootId));
            bool moved=(e.Kind=="operand" && was.Parent!=e.Parent) || left
                || (e.Kind=="destroy" && (!e.Links.TryGetValue("participant",out a) | !was.Links.TryGetValue("participant",out b) || !a.SequenceEqual(b)));
            if(!moved)continue;
            string fresh=newId(),stale=e.Id;
            if(string.IsNullOrEmpty(fresh) || old.ContainsKey(fresh) || plan.Expected.Elements.Any(x=>x.Id==fresh))throw new InvalidOperationException("S203: 新IDが重複しています。");
            foreach(var x in plan.Expected.Elements)
            {
                if(x.Id==stale)x.Id=fresh;
                if(x.Parent==stale)x.Parent=fresh;
                foreach(var key in x.Links.Keys.ToArray())x.Links[key]=x.Links[key].Select(id=>id==stale?fresh:id).ToArray();
            }
            foreach(var key in map.Keys.ToArray())if(map[key]==stale)map[key]=fresh;
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
        var kept=new HashSet<string>(plan.Expected.Elements.Select(e=>e.Id));
        foreach(var e in current.Elements.Where(e=>!kept.Contains(e.Id)))plan.Changes.Add(new SequenceChange{Action="delete",Id=e.Id,Kind=e.Kind});
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
    public static IEnumerable<SequenceMembership> Nesting(IEnumerable<SequenceRegion> operands,IEnumerable<SequenceRegion> fragments,IEnumerable<SequenceRegion> annotations=null)
    {
        // As the PlantUML export places them: by where the top edge falls, whatever the box
        // reaches past below, since the export enters a branch by Y. Across, it must sit inside
        // the branch, or frames side by side would each claim the other.
        foreach(var fragment in fragments)foreach(var operand in operands)
            if(operand.Fragment!=fragment.Id && fragment.Y>=operand.Y-0.5 && fragment.Y<operand.Y+operand.Height-0.5
                && fragment.X>=operand.X-0.5 && fragment.X+fragment.Width<=operand.X+operand.Width+0.5)
                yield return new SequenceMembership{Child=fragment.Id,Parent=operand.Id,Evidence="diagram top edge within branch"};
        if(annotations==null)yield break;
        // A note or ref goes into the branch its top edge falls in, however far it sticks out
        // sideways: the export enters branches by Y alone, and a note is often drawn beside
        // the frame. Only when branches of frames side by side both take that Y does the one
        // it overlaps, or else the nearest, win.
        foreach(var box in annotations)
        {
            var byY=operands.Where(o=>box.Y>=o.Y-0.5 && box.Y<o.Y+o.Height-0.5).ToList();
            var across=byY.Where(o=>box.X<o.X+o.Width && box.X+box.Width>o.X).ToList();
            IEnumerable<SequenceRegion> chosen=byY;
            var frames=byY.Select(o=>o.Fragment).Distinct().ToList();
            bool sideBySide=byY.Any(a=>byY.Any(b=>a.Fragment!=b.Fragment && !(a.X<=b.X+0.5 && a.X+a.Width>=b.X+b.Width-0.5) && !(b.X<=a.X+0.5 && b.X+b.Width>=a.X+a.Width-0.5)));
            if(sideBySide)
            {
                if(across.Count>0)chosen=across;
                else
                {
                    Func<SequenceRegion,double> gap=o=>box.X+box.Width<o.X?o.X-(box.X+box.Width):box.X-(o.X+o.Width);
                    var nearest=byY.OrderBy(gap).First();
                    chosen=byY.Where(o=>o==nearest || (o.X<=nearest.X+0.5 && o.X+o.Width>=nearest.X+nearest.Width-0.5));
                }
            }
            foreach(var operand in chosen)
                yield return new SequenceMembership{Child=box.Id,Parent=operand.Id,Evidence="diagram top edge within branch"};
        }
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
        if(plan.InheritedPorts.Count>0 || plan.CarriedExecutions.Count>0)
            lines.Add("実行区間の指定なし: 引き継ぎ "+plan.InheritedPorts.Count+" / 保持 "+plan.CarriedExecutions.Count);
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
        // An input that states nothing about an endpoint keeps the diagram's bar. Say so:
        // a difference that disappears silently cannot be told apart from one suppressed.
        lines.Add("実行区間の指定なし: 端点引き継ぎ "+plan.InheritedPorts.Count+"件 / 既存バー保持 "+plan.CarriedExecutions.Count+"件");
        if(plan.InheritedPorts.Count>0 || plan.CarriedExecutions.Count>0)
            foreach(var role in new[]{"sendExecution","receiveExecution"})
                lines.Add((role=="sendExecution"?"送信":"受信")+"実行区間への接続数 図/期待: "+current.Elements.Count(e=>e.Kind=="message" && e.Links.ContainsKey(role))
                    +" / "+plan.Expected.Elements.Count(e=>e.Kind=="message" && e.Links.ContainsKey(role)));
        if(plan.InheritRefusals.Count>0)
            lines.Add("引き継ぎを見送った端点: "+plan.InheritRefusals.Count+"件（"+string.Join("・",plan.InheritRefusals.Distinct().OrderBy(v=>v,StringComparer.Ordinal))+"）");
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
// Every change the input can express is placed by one layout (SequenceRelayout) and written
// by one builder, so this only sorts the changes for the builder and the summaries, and stops
// the few things that cannot be written at all.
public sealed class SequenceStructurePreflight
{
    public List<string> Reasons=new List<string>();
    public List<string> ReconnectMessages=new List<string>();
    public List<string> DeleteExecutions=new List<string>();
    public List<string> AddExecutions=new List<string>();
    public List<string> AddParticipants=new List<string>();
    public List<string> DeleteParticipants=new List<string>();
    public List<string> DeleteMessages=new List<string>();
    public List<string> AddMessages=new List<string>();
    public List<string> DeleteFragments=new List<string>();
    public List<string> DeleteOperands=new List<string>();
    public List<string> AddFragments=new List<string>();
    public List<string> AddOperands=new List<string>();
    public List<string> DeleteNotes=new List<string>();
    public List<string> AddNotes=new List<string>();
    public List<string> DeleteRefs=new List<string>();
    // A new frame around elements already drawn, and the existing messages that move into
    // an operand (in, out of, or between frames).
    public List<string> WrapFragments=new List<string>();
    public List<string> MoveMessages=new List<string>();
    // A frame taken away while some of what it held stays.
    public List<string> UnwrapFragments=new List<string>();
    // Elements that keep their container but change places with their neighbours.
    public List<string> ReorderMessages=new List<string>();
    // Elements whose text alone changed.
    public List<string> Renames=new List<string>();
    // A branch taken away from a frame that stays.
    public List<string> TrimOperands=new List<string>();
    public List<string> AddDestroys=new List<string>();
    public List<string> DeleteDestroys=new List<string>();
    public List<string> AddRefs=new List<string>();
    // Messages whose sort changes, and whose sending bar changes.
    public List<string> SortChanges=new List<string>();
    public List<string> ResendMessages=new List<string>();
    // Frames, refs and notes that move into, out of or between frames; lanes that change order;
    // frames whose operator changes; refs whose lanes change; bars that change lanes.
    public List<string> NestChanges=new List<string>();
    public List<string> LaneMoves=new List<string>();
    public List<string> OperatorChanges=new List<string>();
    public List<string> RefTargetChanges=new List<string>();
    // Anything else that only moves: bars whose boundaries change, notes that move.
    public List<string> Relayouts=new List<string>();
    public int Targets { get { return ReconnectMessages.Count+DeleteExecutions.Count+AddExecutions.Count
        +AddParticipants.Count+DeleteParticipants.Count+DeleteMessages.Count+AddMessages.Count
        +DeleteFragments.Count+DeleteOperands.Count+AddFragments.Count+AddOperands.Count+MoveMessages.Count+DeleteNotes.Count+AddNotes.Count+DeleteRefs.Count+AddRefs.Count
        +ReorderMessages.Count+Renames.Count+AddDestroys.Count+DeleteDestroys.Count+SortChanges.Count+ResendMessages.Count+NestChanges.Count+LaneMoves.Count
        +OperatorChanges.Count+RefTargetChanges.Count+Relayouts.Count; } }
    public bool Candidate { get { return Reasons.Count==0 && Targets>0; } }
    public bool CanCommit() { return Candidate; }
    // Whether any message this update writes goes to or comes from outside the diagram: new
    // ones, and ones already drawn whose sending or receiving end changes. Their free ends
    // need the MessageEnd type when the diagram has none to copy.
    public bool NeedsFreeEnds(SyncPlan plan)
    {
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        return AddMessages.Concat(ReconnectMessages).Concat(ResendMessages).Where(after.ContainsKey)
            .Any(id=>Link(after[id],"sender").Length==0 || Link(after[id],"receiver").Length==0);
    }
    // Document order across owners. Bars are stored, not sequenced.
    internal static string[] Flatten(SequenceDocument doc)
    {
        var order=new List<string>();
        var children=doc.Elements.Where(n=>n.Kind!="participant" && n.Kind!="execution" && n.Parent!=null)
            .GroupBy(n=>n.Parent).ToDictionary(g=>g.Key,g=>g.OrderBy(n=>n.Order).ToArray());
        Action<string> walk=null;
        walk=parent=>{
            SequenceElement[] list;if(!children.TryGetValue(parent,out list))return;
            foreach(var e in list){order.Add(e.Id);walk(e.Id);}
        };
        walk(doc.Elements.Single(e=>e.Kind=="interaction").Id);
        return order.ToArray();
    }
    internal static string Attribute(SequenceElement e)
    { string value;return e.Attributes.TryGetValue("sort",out value)?value:""; }
    static string Lines(string value) { return (value??"").Replace("\r\n","\n").Replace('\r','\n'); }
    static string[] Link(SequenceElement e,string role)
    { string[] ids;return e.Links.TryGetValue(role,out ids)?ids:new string[0]; }
    static string Attr(SequenceElement e,string key)
    { string value;return e.Attributes.TryGetValue(key,out value)?value:""; }
    public static SequenceStructurePreflight Check(SequenceDocument current,SyncPlan plan)
    {
        current.Validate();plan.Expected.Validate();
        var result=new SequenceStructurePreflight();
        var before=current.Elements.ToDictionary(e=>e.Id);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        string root=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
        Func<SequenceElement,int> line=e=>{var c=plan.Changes.FirstOrDefault(x=>x.Id==e.Id);return c==null?e.Line:c.Line;};
        Action<SequenceElement,string> stop=(e,reason)=>result.Reasons.Add("L"+line(e)+" "+reason);
        foreach(var e in plan.Expected.Elements.Where(e=>!before.ContainsKey(e.Id)))
        {
            switch(e.Kind)
            {
                case "fragment":
                    result.AddFragments.Add(e.Id);
                    if(plan.Expected.Elements.Any(x=>before.ContainsKey(x.Id) && x.Kind!="execution" && IsWithin(plan.Expected,x.Id,e.Id)))result.WrapFragments.Add(e.Id);
                    break;
                case "operand":
                    if(e.Parent==null || !after.ContainsKey(e.Parent) || after[e.Parent].Kind!="fragment"){stop(e,"追加するオペランドの所有先がフラグメントではありません。");break;}
                    result.AddOperands.Add(e.Id);break;
                case "message":result.AddMessages.Add(e.Id);break;
                case "note":result.AddNotes.Add(e.Id);break;
                case "ref":result.AddRefs.Add(e.Id);break;
                case "participant":result.AddParticipants.Add(e.Id);break;
                case "execution":result.AddExecutions.Add(e.Id);break;
                case "destroy":result.AddDestroys.Add(e.Id);break;
                default:stop(e,e.Kind+"の追加は対象外です。");break;
            }
        }
        foreach(var e in current.Elements.Where(e=>!after.ContainsKey(e.Id)))
        {
            switch(e.Kind)
            {
                case "execution":result.DeleteExecutions.Add(e.Id);break;
                case "participant":result.DeleteParticipants.Add(e.Id);break;
                case "message":result.DeleteMessages.Add(e.Id);break;
                case "fragment":
                    result.DeleteFragments.Add(e.Id);
                    if(current.Elements.Any(x=>after.ContainsKey(x.Id) && x.Kind!="execution" && IsWithin(current,x.Id,e.Id)))result.UnwrapFragments.Add(e.Id);
                    break;
                case "operand":
                    result.DeleteOperands.Add(e.Id);
                    if(e.Parent!=null && after.ContainsKey(e.Parent))result.TrimOperands.Add(e.Id);
                    break;
                case "note":result.DeleteNotes.Add(e.Id);break;
                case "ref":result.DeleteRefs.Add(e.Id);break;
                case "destroy":result.DeleteDestroys.Add(e.Id);break;
                default:stop(e,e.Kind+"の削除は対象外です。");break;
            }
        }
        // Elements on both sides: what changed about each.
        var oldWalk=Flatten(current);var newWalk=Flatten(plan.Expected);
        var kept=new HashSet<string>(oldWalk.Where(after.ContainsKey).Intersect(newWalk));
        var oldKept=oldWalk.Where(kept.Contains).ToArray();var newKept=newWalk.Where(kept.Contains).ToArray();
        var stable=Stable(oldKept,newKept);
        foreach(var e in plan.Expected.Elements.Where(e=>before.ContainsKey(e.Id) && e.Kind!="interaction"))
        {
            var old=before[e.Id];
            // The same test the plan uses: names folded for lanes, refs and guards, line ends
            // only for the rest. A difference the plan does not see is not written, so a name
            // the export put on one line keeps its line breaks in the diagram.
            bool folded=e.Kind=="participant" || e.Kind=="ref" || e.Kind=="operand";
            bool textChanged=folded?SequenceLabels.Fold(old.Text)!=SequenceLabels.Fold(e.Text):Lines(old.Text)!=Lines(e.Text);
            bool parentChanged=old.Parent!=e.Parent;
            bool orderChanged=kept.Contains(e.Id) && !stable.Contains(e.Id);
            switch(e.Kind)
            {
                case "message":
                    if(textChanged)result.Renames.Add(e.Id);
                    if(Attribute(old)!=Attribute(e) && Attribute(old)!="destroy" && Attribute(e)!="destroy")result.SortChanges.Add(e.Id);
                    if(!Link(old,"receiveExecution").SequenceEqual(Link(e,"receiveExecution")) || !Link(old,"receiver").SequenceEqual(Link(e,"receiver")))
                    {
                        if(Link(e,"receiver").Length>0 && Link(e,"receiveExecution").Length!=1){stop(e,NoBar("受信"));break;}
                        result.ReconnectMessages.Add(e.Id);
                    }
                    if(!Link(old,"sendExecution").SequenceEqual(Link(e,"sendExecution")) || !Link(old,"sender").SequenceEqual(Link(e,"sender")))
                    {
                        if(Link(e,"sender").Length>0 && Link(e,"sendExecution").Length!=1){stop(e,NoBar("送信"));break;}
                        result.ResendMessages.Add(e.Id);
                    }
                    if(parentChanged)result.MoveMessages.Add(e.Id);
                    else if(orderChanged)result.ReorderMessages.Add(e.Id);
                    break;
                case "participant":
                    if(textChanged)result.Renames.Add(e.Id);
                    if(old.Order!=e.Order && current.Elements.Where(x=>x.Kind=="participant" && after.ContainsKey(x.Id)).OrderBy(x=>x.Order).Select(x=>x.Id)
                        .SequenceEqual(plan.Expected.Elements.Where(x=>x.Kind=="participant" && before.ContainsKey(x.Id)).OrderBy(x=>x.Order).Select(x=>x.Id))==false)
                        result.LaneMoves.Add(e.Id);
                    break;
                case "fragment":
                    if(textChanged)result.Renames.Add(e.Id);
                    if(Attr(old,"operator")!=Attr(e,"operator"))result.OperatorChanges.Add(e.Id);
                    if(parentChanged)result.NestChanges.Add(e.Id);
                    else if(orderChanged)result.Relayouts.Add(e.Id);
                    break;
                case "operand":
                    if(textChanged)result.Renames.Add(e.Id);
                    if(parentChanged)stop(e,"分岐を別の枠へ移す変更は対象外です。分岐を削除して追加してください。");
                    break;
                case "note":
                    if(textChanged)result.Renames.Add(e.Id);
                    if(parentChanged || orderChanged)result.Relayouts.Add(e.Id);
                    break;
                case "ref":
                    if(textChanged)result.Renames.Add(e.Id);
                    if(!Link(old,"targets").SequenceEqual(Link(e,"targets")))result.RefTargetChanges.Add(e.Id);
                    if(Attr(old,"reference")!=Attr(e,"reference") && Attr(e,"reference").Length>0)result.RefTargetChanges.Add(e.Id);
                    if(parentChanged)result.NestChanges.Add(e.Id);
                    else if(orderChanged)result.Relayouts.Add(e.Id);
                    break;
                case "destroy":
                    if(!Link(old,"participant").SequenceEqual(Link(e,"participant")))stop(e,"破棄の参加者を替える変更は対象外です。破棄を削除して追加してください。");
                    else if(parentChanged || orderChanged)result.Relayouts.Add(e.Id);
                    break;
                case "execution":
                    if(!Link(old,"participant").SequenceEqual(Link(e,"participant")))result.NestChanges.Add(e.Id);
                    else if(Canonical(old)!=Canonical(e))result.Relayouts.Add(e.Id);
                    break;
                default:
                    if(Canonical(old)!=Canonical(e))stop(e,e.Kind+"の変更は対象外です。");
                    break;
            }
        }
        result.RefTargetChanges=result.RefTargetChanges.Distinct().ToList();
        // What the input says has to hang together before anything is laid out from it.
        var knownBarLinks=new[]{"participant","outer","startAfter","endBefore","endContainer"};
        foreach(var bar in plan.Expected.Elements.Where(e=>e.Kind=="execution"))
        {
            var lane=Link(bar,"participant");
            if(lane.Length!=1 || !after.ContainsKey(lane[0]) || after[lane[0]].Kind!="participant"){stop(bar,"実行区間の参加者が参加者ではありません。");continue;}
            var outer=Link(bar,"outer");
            if(outer.Length>1 || (outer.Length==1 && (!after.ContainsKey(outer[0]) || after[outer[0]].Kind!="execution" || !Link(after[outer[0]],"participant").SequenceEqual(lane))))
                stop(bar,"実行区間の入れ子先が同じ参加者の実行区間ではありません。");
            if(bar.Links.Keys.Any(key=>!knownBarLinks.Contains(key)))stop(bar,"実行区間に未対応の接続があります。");
            var ends=Link(bar,"endContainer");
            if(ends.Length>1 || (ends.Length==1 && ends[0]!=root && (!after.ContainsKey(ends[0]) || after[ends[0]].Kind!="operand")))
                stop(bar,"実行区間の終了位置が相互作用でも分岐でもありません。");
            if(bar.Parent!=root && (!after.ContainsKey(bar.Parent) || after[bar.Parent].Kind!="operand"))stop(bar,"実行区間の所有先が相互作用でも分岐でもありません。");
        }
        foreach(var message in plan.Expected.Elements.Where(e=>e.Kind=="message"))
            foreach(var pair in new[]{new[]{"sendExecution","sender"},new[]{"receiveExecution","receiver"}})
            {
                var port=Link(message,pair[0]);
                if(port.Length==1 && (!after.ContainsKey(port[0]) || after[port[0]].Kind!="execution" || !Link(after[port[0]],"participant").SequenceEqual(Link(message,pair[1]))))
                    stop(message,(pair[1]=="sender"?"送信":"受信")+"側の実行区間が"+(pair[1]=="sender"?"送信者":"受信者")+"の実行区間ではありません。");
            }
        // The product cannot draw a branch that holds nothing.
        foreach(var operand in plan.Expected.Elements.Where(e=>e.Kind=="operand"))
            if(!plan.Expected.Elements.Any(x=>x.Kind!="execution" && x.Parent==operand.Id)
                && (!before.ContainsKey(operand.Id) || current.Elements.Any(x=>x.Kind!="execution" && x.Parent==operand.Id)))
                stop(operand,"要素のない分岐になります。空の分岐は製品が図形を作れないため対象外です。");
        foreach(var message in plan.Expected.Elements.Where(e=>e.Kind=="message" && !before.ContainsKey(e.Id)))
        {
            if(Link(message,"sender").Length==0 && Link(message,"receiver").Length==0)stop(message,"送受信の両方が図外のメッセージは対象外です。");
            foreach(var pair in new[]{new[]{"sendExecution","sender","送信"},new[]{"receiveExecution","receiver","受信"}})
                if(Link(message,pair[1]).Length>0 && Link(message,pair[0]).Length!=1)stop(message,NoBar(pair[2]));
        }
        return result;
    }
    // A message has to land on a bar the input opens: bars the input does not have are not made up.
    static string NoBar(string side)
    {
        return side+"側の実行区間が1つに決まりません。入力にない実行区間は作りません。"
            +"その参加者で activate し、メッセージをその内側に書いてください。";
    }
    // The ids that keep their relative order: a longest common subsequence of the two walks.
    internal static HashSet<string> Stable(string[] a,string[] b)
    {
        var length=new int[a.Length+1,b.Length+1];
        for(int i=a.Length-1;i>=0;i--)for(int j=b.Length-1;j>=0;j--)
            length[i,j]=a[i]==b[j]?1+length[i+1,j+1]:Math.Max(length[i+1,j],length[i,j+1]);
        var result=new HashSet<string>();int x=0,y=0;
        while(x<a.Length && y<b.Length)
        {
            if(a[x]==b[y]){result.Add(a[x]);x++;y++;}
            else if(length[x+1,y]>=length[x,y+1])x++;else y++;
        }
        return result;
    }
    internal static bool IsWithin(SequenceDocument doc,string id,string container)
    {
        var byId=doc.Elements.ToDictionary(e=>e.Id);
        for(string at=byId.ContainsKey(id)?byId[id].Parent:null;at!=null && byId.ContainsKey(at);at=byId[at].Parent)if(at==container)return true;
        return false;
    }
    static string Canonical(SequenceElement e)
    {
        var copy=e.Copy();copy.Line=0;copy.Order=0;
        copy.Links=e.Links.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>p.Value.ToArray(),StringComparer.Ordinal);
        copy.Attributes=e.Attributes.OrderBy(p=>p.Key,StringComparer.Ordinal).ToDictionary(p=>p.Key,p=>p.Value,StringComparer.Ordinal);
        return new SequenceDocument{Elements=new List<SequenceElement>{copy}}.ToJson();
    }
    public string Summary()
    {
        return "構造更新の事前判定（図への反映なし）\n受信接続変更候補: "+ReconnectMessages.Count+" / 送信接続変更候補: "+ResendMessages.Count+" / 実行区間削除候補: "+DeleteExecutions.Count
            +" / 実行区間追加候補: "+AddExecutions.Count
            +" / 参加者追加候補: "+AddParticipants.Count+" / 参加者削除候補: "+DeleteParticipants.Count+" / 参加者の並べ替え: "+LaneMoves.Count
            +" / メッセージ削除候補: "+DeleteMessages.Count+" / メッセージ追加候補: "+AddMessages.Count
            +" / フラグメント削除候補: "+DeleteFragments.Count+" / オペランド削除候補: "+DeleteOperands.Count
            +" / フラグメント追加候補: "+AddFragments.Count+" / オペランド追加候補: "+AddOperands.Count
            +" / 所属を移すメッセージ候補: "+MoveMessages.Count+" / 所属を移す枠・ref・実行区間: "+NestChanges.Count+" / Note削除候補: "+DeleteNotes.Count+" / Note追加候補: "+AddNotes.Count
            +" / ref削除候補: "+DeleteRefs.Count+" / ref追加候補: "+AddRefs.Count+" / 外す枠候補: "+UnwrapFragments.Count+" / 順序を入れ替えるメッセージ候補: "+ReorderMessages.Count
            +" / 本文変更候補: "+Renames.Count+" / 種別変更候補: "+SortChanges.Count+" / 演算子変更候補: "+OperatorChanges.Count+" / ref対象変更候補: "+RefTargetChanges.Count
            +" / 配置だけの変更: "+Relayouts.Count+" / 破棄の追加候補: "+AddDestroys.Count+" / 破棄の削除候補: "+DeleteDestroys.Count
            +"\n"+(Reasons.Count>0?"全体を停止: "+Reasons.Count+"件の未対応条件":Candidate?"反映できる変更です。":"対象の変更なし")
            +"\n"+string.Join("\n",Reasons.Distinct());
    }
    public string ToJson()
    { return PumlBuild.Json(PumlBuild.Obj("Candidate",Candidate,"ReconnectMessages",ReconnectMessages.ToArray(),"ResendMessages",ResendMessages.ToArray(),"DeleteExecutions",DeleteExecutions.ToArray(),
        "AddExecutions",AddExecutions.ToArray(),"AddParticipants",AddParticipants.ToArray(),"LaneMoves",LaneMoves.ToArray(),
        "DeleteParticipants",DeleteParticipants.ToArray(),"DeleteMessages",DeleteMessages.ToArray(),
        "AddMessages",AddMessages.ToArray(),"DeleteFragments",DeleteFragments.ToArray(),
        "DeleteOperands",DeleteOperands.ToArray(),
        "AddFragments",AddFragments.ToArray(),"AddOperands",AddOperands.ToArray(),
        "WrapFragments",WrapFragments.ToArray(),"MoveMessages",MoveMessages.ToArray(),"NestChanges",NestChanges.ToArray(),"DeleteNotes",DeleteNotes.ToArray(),"AddNotes",AddNotes.ToArray(),
        "DeleteRefs",DeleteRefs.ToArray(),"AddRefs",AddRefs.ToArray(),"UnwrapFragments",UnwrapFragments.ToArray(),"TrimOperands",TrimOperands.ToArray(),"ReorderMessages",ReorderMessages.ToArray(),
        "Renames",Renames.ToArray(),"SortChanges",SortChanges.ToArray(),"OperatorChanges",OperatorChanges.ToArray(),"RefTargetChanges",RefTargetChanges.ToArray(),"Relayouts",Relayouts.ToArray(),
        "AddDestroys",AddDestroys.ToArray(),"DeleteDestroys",DeleteDestroys.ToArray(),
        "Reasons",Reasons.ToArray())); }
}
// One added execution, described so the expected state can be computed without
// reading the export again. Template ids point at an existing execution of the same
// participant; the live SDK values of those templates supply the bar width and the
// endpoint fields that the export does not name.
public sealed class SequenceAddedMessage
{
    public string ModelId, Metaclass, Name, OwnerId, ShapeId, TemplateShapeId, TemplateModelId, Y;
    public string[] RelationIds=new string[0], RelationSources=new string[0], TemplateRelationIds=new string[0],
        RelationFields=new string[0];
    public string SendPort, ReceivePort, Sender, Receiver, Sort, TargetY, Bend;
}

public sealed class SequenceAddedParticipant
{
    public string ModelId, Metaclass, Name, OwnerId, ShapeId, TemplateShapeId, RelationId, TemplateRelationId, X;
}

public sealed class SequenceAddedExecution
{
    public string ModelId, Metaclass, Name, OwnerId, ShapeId, TemplateShapeId, Geometry;
    public string[] RelationIds=new string[0], RelationSources=new string[0], TemplateRelationIds=new string[0];
}

// What a frame needs when the diagram holds none to copy: the metaclasses, the
// operator values, and for every relation its metaclass, whether it is owned, and
// the pair of field ids the readback compares. The SDK side resolves all of it.
public sealed class SequenceFrameTypes
{
    public string Fragment, Operand;
    public Dictionary<string,string> Operators=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    // Each row is {metaclass id, "Embed" or "Ref", field signature}.
    public string[] Owns, Branches, Crossing, OperandMessage;
    // A frame or ref inside a branch is also tied to that branch. Null when the profile has none.
    public string[] Nested;
    public bool Complete()
    {
        return !string.IsNullOrEmpty(Fragment) && !string.IsNullOrEmpty(Operand)
            && new[]{Owns,Branches,Crossing,OperandMessage}.All(r=>r!=null && r.Length==3 && !string.IsNullOrEmpty(r[0]));
    }
    public string ToJson()
    {
        return PumlBuild.Json(PumlBuild.Obj("Fragment",Fragment,"Operand",Operand,
            "Operators",Operators.ToDictionary(p=>p.Key,p=>(object)p.Value),
            "Owns",Owns,"Branches",Branches,"Crossing",Crossing,"OperandMessage",OperandMessage));
    }
}

// A frame and its operands. The frame shape carries its text after the numbers and an
// operand shape carries its guard, matching how the SDK side reads them back.
public sealed class SequenceAddedFragment
{
    public string ModelId, Metaclass, Name, OwnerId, ShapeId, TemplateShapeId, Geometry, Text;
    public string[] RelationIds=new string[0], RelationSources=new string[0], RelationTargets=new string[0],
        TemplateRelationIds=new string[0], RelationFields=new string[0];
}

public sealed class SequenceAddedOperand
{
    public string ModelId, Metaclass, Name, OwnerId, ShapeId, TemplateShapeId, Geometry, Guard, Position;
    public string[] RelationIds=new string[0], RelationSources=new string[0], TemplateRelationIds=new string[0],
        RelationFields=new string[0];
}

// A shape that had to move or grow to make room for a message inserted above it.
// Only the values named here change; everything else about the shape is left alone.
public sealed class SequenceShiftedShape
{
    public string ModelId, ShapeId, Kind;
    public string[] Keys=new string[0], Values=new string[0];
}

// A message already drawn that a new frame now holds: only the operand's reference to
// it is new. Its model, owner and ends stay as they are.
// What a new note is built from, resolved from the view and profile (PumlRuntime.NoteTypes).
// Owns is {relation metaclass, "Embed", field signature}.
public sealed class SequenceNoteTypes
{
    public string Class, Field, Storage;
    public string[] Owns;
}

// What a new ref is built from. Owns and Crossing are {relation metaclass, kind, field
// signature}; RefersTo is empty when the profile has no such field.
public sealed class SequenceRefTypes
{
    public string Class;
    public string[] Owns, Crossing, RefersTo;
}

// A note or ref this run adds, with every relation it is built with.
// What a new destruction is built from. Each row is {relation metaclass, kind, field signature}.
public sealed class SequenceDestroyTypes
{
    public string Class;
    public string[] Owns, Target, Message;
}

public sealed class SequenceAddedNote
{
    public string Kind, ModelId, Metaclass, Name, OwnerId, ShapeId, Geometry, Text;
    public string[] RelationIds=new string[0], RelationSources=new string[0], RelationTargets=new string[0], RelationFields=new string[0];
}

public sealed class SequenceMovedMessage
{
    public string ModelId, OperandId, RelationId, Field;
}

// A lifeline whose timeline was stretched so it still reaches the bottom of the
// diagram. Only its length changes; everything else about the lane is left alone.
public sealed class SequenceStretchedLifeline
{
    public string ModelId, ShapeId, Length;
}

// Prepared files are diagnostic artifacts; they are never imported by this command.
public sealed class SequenceStructurePreparation
{
    public string ReconnectJson, EditorAfterDeleteJson;
    public int ReconnectCount;
    public string[] DeleteIds;
    public string[] ReceiveRelationIds=new string[0];
    public SequenceAddedExecution[] AddedExecutions=new SequenceAddedExecution[0];
    public SequenceAddedParticipant[] AddedParticipants=new SequenceAddedParticipant[0];
    public SequenceAddedMessage[] AddedMessages=new SequenceAddedMessage[0];
    public SequenceAddedFragment[] AddedFragments=new SequenceAddedFragment[0];
    public SequenceAddedOperand[] AddedOperands=new SequenceAddedOperand[0];
    public SequenceStretchedLifeline[] StretchedLifelines=new SequenceStretchedLifeline[0];
    public string[] CreatedCollections=new string[0];
    public SequenceShiftedShape[] ShiftedShapes=new SequenceShiftedShape[0];
    public string InsertedMessageId="";
    public SequenceMovedMessage[] MovedMessages=new SequenceMovedMessage[0];
    public SequenceAddedNote[] AddedNotes=new SequenceAddedNote[0];
    // Model id and new text of each element renamed in place.
    public string[][] Renamed=new string[0][];
    public string[] DeleteParticipantIds=new string[0];
    public string[] DeleteMessageIds=new string[0];
    public string[] DeleteFrameIds=new string[0];
    public string[] DeleteNoteIds=new string[0];
    public string[] DeleteDestroyIds=new string[0];
    public string[] DeleteRefIds=new string[0];
    // Reference relations taken off without deleting either end: IRelationship.UnRelate.
    public string[] UnrelateIds=new string[0];
    // Free ends of removed messages, which go with them.
    public string[] DeleteEndIds=new string[0];
    // {message id, new sort} and {shape id, new text} for elements rewritten in place.
    public string[][] SortChanged=new string[0][], ShapeTexts=new string[0][];
    public string[] SendRelationIds=new string[0];
    // New shapes built without a sample: the product decides their size, so the check takes
    // what it reads back for them and only holds them to existing and belonging to their model.
    public string[] LooseShapeIds=new string[0];
    static string V(SequenceJson n,string key) { return SequenceEditorDocument.Value(n,key); }
    static SequenceJson[] Array(SequenceJson n,string key)
    {
        if(n[key]==null || n[key].Items==null)throw new InvalidOperationException("S220: 退避データの配列が不足しています。");
        return n[key].Items.ToArray();
    }
    static void Require(bool condition,string reason)
    { if(!condition)throw new InvalidOperationException("S220: "+reason); }
    public static SequenceStructurePreparation Build(string exported,string editorId,SequenceDocument current,SyncPlan plan)
    { return Build(exported,editorId,current,plan,null); }
    public static SequenceStructurePreparation Build(string exported,string editorId,SequenceDocument current,SyncPlan plan,SequenceFrameTypes types)
    { return Build(exported,editorId,current,plan,types,null); }
    public static SequenceStructurePreparation Build(string exported,string editorId,SequenceDocument current,SyncPlan plan,SequenceFrameTypes types,SequenceNoteTypes noteTypes)
    { return Build(exported,editorId,current,plan,types,noteTypes,null); }
    public static SequenceDestroyTypes DestroyTypes;
    public static SequenceBaseTypes BaseTypes;
    public static SequenceStructurePreparation Build(string exported,string editorId,SequenceDocument current,SyncPlan plan,SequenceFrameTypes types,SequenceNoteTypes noteTypes,SequenceRefTypes refTypes)
    {
        var gate=SequenceStructurePreflight.Check(current,plan);
        Require(gate.Candidate,"未対応の変更があるか、構造更新の候補がありません。");
        Created.Clear();
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
        var shapeOf=new Dictionary<string,SequenceJson>(StringComparer.Ordinal);
        foreach(var sh in editor.Shapes())
        {
            string model=V(sh,"ModelId");
            if(model==null)continue;
            Require(!shapeOf.ContainsKey(model),"同じモデルの図形が複数あります。");
            shapeOf[model]=sh;
        }
        Func<string,string> R=name=>SequencePayload.Prefix+name;
        Func<string,SequenceJson[]> typed=name=>relations.Where(r=>V(r,"MetamodelId")==R(name)).ToArray();
        Func<string,string,string,SequenceJson> find=(type,from,to)=>{
            var matches=relations.Where(r=>V(r,"MetamodelId")==R(type) && (from==null || V(r,"SourceId")==from) && V(r,"TargetId")==to).ToArray();
            Require(matches.Length==1,"必要な構造関連を一意に取得できません: "+type);return matches[0];
        };
        var endIds=new HashSet<string>(entities.Where(e=>V(e,"EntityType")=="MessageEnd").Select(e=>V(e,"Id")));
        Func<string,string,string> portOf=(role,message)=>{
            var found=relations.Where(r=>V(r,"MetamodelId")==R(role) && V(r,"TargetId")==message).ToArray();
            return found.Length==1?V(found[0],"SourceId"):null;
        };
        var patch=SequenceJson.Parse(editor.ImportJson());
        var view=patch["Editors"].Items.Single();
        var newEntities=new List<SequenceJson>();var newRelations=new List<SequenceJson>();var changed=new List<SequenceJson>();
        var changedIds=new HashSet<string>(StringComparer.Ordinal);var unrelate=new List<string>();
        Func<string[],string,string,string,SequenceJson> relate=(row,relationId,from,to)=>
            SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",relationId,"RelationType",row[1],"MetamodelId",row[0],"SourceId",from,"TargetId",to)));
        Func<SequenceJson,string,string,string,SequenceJson> copyRelation=(template,relationId,from,to)=>{
            var copy=SequenceJson.Parse(template.ToJsonString());
            copy.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(relationId));
            copy.Properties["SourceId"]=SequenceJson.Parse(SequencePayload.Q(from));
            copy.Properties["TargetId"]=SequenceJson.Parse(SequencePayload.Q(to));
            // Omitted order appends, which is what the product does with a new relation.
            copy.Properties.Remove("SourceIndex");copy.Properties.Remove("TargetIndex");
            return copy;
        };
        // A relation that keeps its id and changes one end is imported again with that end.
        Action<SequenceJson,string,string> resend=(relation,from,to)=>{
            var copy=SequenceJson.Parse(relation.ToJsonString());
            if(from!=null)copy.Properties["SourceId"]=SequenceJson.Parse(SequencePayload.Q(from));
            if(to!=null)copy.Properties["TargetId"]=SequenceJson.Parse(SequencePayload.Q(to));
            // The order belongs to the collection the relation is leaving; let the move append.
            copy.Properties.Remove("SourceIndex");
            changed.Add(copy);changedIds.Add(V(relation,"Id"));
        };

        // ---- Removal: every model the input no longer has, and the free ends of removed messages.
        var removedEnds=new List<string>();
        foreach(string id in gate.DeleteMessages)
            foreach(string role in new[]{"SendMessage","ReceiveMessage"})
            {string end=portOf(role,id);if(end!=null && endIds.Contains(end))removedEnds.Add(end);}
        var leaving=new HashSet<string>(gate.DeleteExecutions.Concat(gate.DeleteParticipants).Concat(gate.DeleteMessages)
            .Concat(gate.DeleteFragments).Concat(gate.DeleteOperands).Concat(gate.DeleteNotes).Concat(gate.DeleteRefs).Concat(gate.DeleteDestroys).Concat(removedEnds));
        var frameEntity=relations.Where(r=>V(r,"MetamodelId")==R("___Interaction_Frame") && V(r,"SourceId")==root).Select(r=>V(r,"TargetId")).ToArray();
        var inside=new HashSet<string>(current.Elements.Select(e=>e.Id).Concat(endIds).Concat(frameEntity));
        foreach(string id in leaving)
        {
            Require(byId.ContainsKey(id),"削除対象が退避データにありません: "+id);
            // What a model is tied to inside this diagram goes with it. So does a reference it
            // holds itself, such as the operation a message calls or the class a lane stands for
            // (its type): deleting it removes that link only, never what it points at. A tie
            // from anything outside, such as a trace link from elsewhere in the project, would
            // be lost silently, so that stops the update.
            foreach(var relation in relations.Where(r=>V(r,"SourceId")==id || V(r,"TargetId")==id))
            {
                string other=V(relation,"SourceId")==id?V(relation,"TargetId"):V(relation,"SourceId");
                if(V(relation,"SourceId")==id && V(relation,"RelationType")!="Embed")continue;
                Require(inside.Contains(other),"削除する要素が図の外のモデルと関連しています。 関連="+V(relation,"MetamodelId")
                    +" 相手の型="+(byId.ContainsKey(other)?V(byId[other],"EntityType"):"不明（退避範囲外）"));
            }
            Require(shapeOf.ContainsKey(id),"削除する要素の図形を一意に取得できません: "+id);
        }
        foreach(var other in Array(source,"Editors").Where(e=>V(e,"Id")!=editorId))
            Require(!Mentions(other,leaving),"削除する要素を別のエディタも参照しています。");

        // ---- Across: lanes.
        var oldLanes=current.Elements.Where(e=>e.Kind=="participant").OrderBy(e=>e.Order).Select(e=>e.Id).ToArray();
        var newLanes=plan.Expected.Elements.Where(e=>e.Kind=="participant").OrderBy(e=>e.Order).Select(e=>e.Id).ToArray();
        Require(oldLanes.All(shapeOf.ContainsKey),"参加者の図形を一意に取得できません。");
        var oldLaneX=oldLanes.ToDictionary(id=>id,id=>Read(shapeOf[id],"X"));
        var laneLayout=SequenceLaneLayout.Place(oldLanes,oldLaneX,newLanes,LaneSpacing,190);
        SequenceJson laneTemplate=oldLanes.Length==0?null:shapeOf[oldLanes.OrderBy(id=>oldLaneX[id]).Last()];
        Func<string,double> laneWidth=id=>shapeOf.ContainsKey(id)?Read(shapeOf[id],"Width"):laneTemplate!=null?Read(laneTemplate,"Width"):100;
        Func<string,double> center=id=>laneLayout.X[id]+laneWidth(id)/2;
        Func<string,double> laneShift=id=>before.ContainsKey(id) && oldLaneX.ContainsKey(id)?laneLayout.X[id]-oldLaneX[id]:0;

        // ---- Down: every row.
        var oldTokens=SequenceRelayout.Walk(current);
        Func<string,double> oldOperandTop=null;
        {
            var tops=new Dictionary<string,double>(StringComparer.Ordinal);
            foreach(var fragment in current.Elements.Where(e=>e.Kind=="fragment"))
            {
                Require(shapeOf.ContainsKey(fragment.Id),"枠の図形を一意に取得できません。");
                var box=shapeOf[fragment.Id];double fy=Read(box,"Y"),fh=Read(box,"Height");
                var operands=current.Elements.Where(e=>e.Parent==fragment.Id && e.Kind=="operand").ToArray();
                Require(operands.All(o=>shapeOf.ContainsKey(o.Id)),"分岐の図形を一意に取得できません。");
                // The reader takes positions inside the frame as absolute, anything else as offsets.
                bool absolute=operands.All(o=>Read(shapeOf[o.Id],"Position")>=fy-0.00001 && Read(shapeOf[o.Id],"Position")<=fy+fh+0.00001);
                foreach(var o in operands)tops[o.Id]=absolute?Read(shapeOf[o.Id],"Position"):fy+Read(shapeOf[o.Id],"Position");
            }
            oldOperandTop=id=>tops[id];
        }
        foreach(var t in oldTokens)
        {
            Require(shapeOf.ContainsKey(t.Id),"図形を一意に取得できません: "+t.Id);
            var sh=shapeOf[t.Id];
            switch(t.Kind)
            {
                case "M":t.Old=Read(sh,"SourceY");t.Drop=Read(sh,"TargetY")-t.Old;break;
                case "N":t.Old=Read(sh,"Y");t.Height=Read(sh,"Height");break;
                case "D":t.Old=Read(sh,"Y");break;
                case "FO":t.Old=Read(sh,"Y");break;
                case "FC":t.Old=Read(sh,"Y")+Read(sh,"Height");break;
                case "OO":t.Old=oldOperandTop(t.Id);break;
            }
        }
        var oldByKey=oldTokens.ToDictionary(t=>t.Key);
        var newTokens=SequenceRelayout.Walk(plan.Expected);
        Func<SequenceElement,bool> selfCall=m=>Link(m,"sender").Length==1 && Link(m,"sender").SequenceEqual(Link(m,"receiver"));
        foreach(var t in newTokens)
        {
            SequenceRelayout.Token old;
            if(oldByKey.TryGetValue(t.Key,out old)){t.Drop=old.Drop;t.Height=old.Height;continue;}
            if(t.Kind=="M")t.Drop=selfCall(after[t.Id])?24:0;
            if(t.Kind=="N")t.Height=BoxHeight(after[t.Id].Text);
        }
        var rows=SequenceRelayout.Place(oldTokens,newTokens);
        Func<string,double> messageY=id=>rows.Get("M:"+id).Y;
        Func<string,double> targetY=id=>{var t=rows.Get("M:"+id);return t.Y+t.Drop;};
        Func<string,int> depthOf=null;
        depthOf=id=>{int d=0;for(string at=after[id].Parent;at!=null && after.ContainsKey(at);at=after[at].Parent)if(after[at].Kind=="fragment")d++;return d;};
        Func<string,int> oldDepth=id=>{int d=0;for(string at=before[id].Parent;at!=null && before.ContainsKey(at);at=before[at].Parent)if(before[at].Kind=="fragment")d++;return d;};

        // ---- Frames, notes and refs: their boxes.
        var box2=new Dictionary<string,double[]>(StringComparer.Ordinal);   // left, top, right, bottom
        double baseLeft,baseRight;
        {
            var top=current.Elements.FirstOrDefault(e=>e.Kind=="fragment" && e.Parent==root && after.ContainsKey(e.Id) && after[e.Id].Parent==root);
            if(top!=null){baseLeft=laneLayout.Map(Read(shapeOf[top.Id],"X"));baseRight=laneLayout.Map(Read(shapeOf[top.Id],"X")+Read(shapeOf[top.Id],"Width"));}
            else if(newLanes.Length>0)
            {
                baseLeft=newLanes.Min(id=>laneLayout.X[id])-16;baseRight=newLanes.Max(id=>laneLayout.X[id]+laneWidth(id))+16;
            }
            else {baseLeft=20;baseRight=260;}
        }
        foreach(var f in plan.Expected.Elements.Where(e=>e.Kind=="fragment"))
        {
            double left,right;int d=depthOf(f.Id);
            if(before.ContainsKey(f.Id) && oldDepth(f.Id)==d)
            {left=laneLayout.Map(Read(shapeOf[f.Id],"X"));right=laneLayout.Map(Read(shapeOf[f.Id],"X")+Read(shapeOf[f.Id],"Width"));}
            else {left=baseLeft+16*d;right=baseRight-16*d;if(right-left<60)right=left+60;}
            box2[f.Id]=new[]{left,rows.Get("FO:"+f.Id).Y,right,rows.Get("FC:"+f.Id).Y};
        }
        foreach(var n in plan.Expected.Elements.Where(e=>e.Kind=="note" || e.Kind=="ref"))
        {
            var t=rows.Get("N:"+n.Id);double left,right;
            if(before.ContainsKey(n.Id))
            {left=laneLayout.Map(Read(shapeOf[n.Id],"X"));right=laneLayout.Map(Read(shapeOf[n.Id],"X")+Read(shapeOf[n.Id],"Width"));}
            else
            {
                // A ref covers the lanes it names; a new note names none and covers them all.
                var covered=n.Kind=="ref"?Link(n,"targets"):new string[0];
                var spanned=covered.Length>0?covered:newLanes;
                Require(spanned.Length>0,"参加者がないため"+n.Kind+"の幅を決められません。");
                double l=spanned.Min(center),r=spanned.Max(center);
                // The generator's margins: a note 50 out and at least 160 wide, a ref 55 out and 150.
                if(n.Kind=="ref"){left=l-55;right=left+Math.Max(150,r-l+110);}else{left=l-50;right=left+Math.Max(160,r-l+100);}
            }
            box2[n.Id]=new[]{left,t.Y,right,t.Y+t.Height};
        }
        // A frame holds what its branches hold, with room to spare; the deepest go first so a
        // frame that grows makes the one around it grow too.
        foreach(var f in plan.Expected.Elements.Where(e=>e.Kind=="fragment").OrderByDescending(e=>depthOf(e.Id)).ToArray())
        {
            var mine=box2[f.Id];
            foreach(var child in plan.Expected.Elements.Where(e=>(e.Kind=="fragment" || e.Kind=="note" || e.Kind=="ref") && e.Parent!=null
                && after.ContainsKey(e.Parent) && after[e.Parent].Kind=="operand" && after[e.Parent].Parent==f.Id))
            {
                var inner=box2[child.Id];double margin=child.Kind=="fragment"?16:8;
                mine[0]=Math.Min(mine[0],inner[0]-margin);mine[2]=Math.Max(mine[2],inner[2]+margin);
            }
        }
        var operandRegions=new List<SequenceRegion>();var operandTop=new Dictionary<string,double>(StringComparer.Ordinal);
        foreach(var f in plan.Expected.Elements.Where(e=>e.Kind=="fragment"))
        {
            var operands=plan.Expected.Elements.Where(e=>e.Parent==f.Id && e.Kind=="operand").OrderBy(e=>e.Order).ToArray();
            var b=box2[f.Id];
            for(int i=0;i<operands.Length;i++)
            {
                double top=i==0?b[1]:rows.Get("OO:"+operands[i].Id).Y,bottom=i+1<operands.Length?rows.Get("OO:"+operands[i+1].Id).Y:b[3];
                operandTop[operands[i].Id]=top;
                operandRegions.Add(new SequenceRegion{Id=operands[i].Id,Fragment=f.Id,X=b[0],Y=top,Width=b[2]-b[0],Height=bottom-top});
            }
        }
        Func<double,double,string> containerAt=(x,y)=>{
            var candidates=operandRegions.Where(r=>x>=r.X && x<=r.X+r.Width && y>=r.Y && y<r.Y+r.Height-1.0).ToArray();
            var nearest=candidates.Where(r=>!candidates.Any(inner=>inner.Id!=r.Id && SequenceRegion.Contains(r,inner))).ToArray();
            return nearest.Length==1?nearest[0].Id:root;
        };
        // The Y the reader orders an element by.
        Func<string,double> eventY=id=>{
            var e=after[id];
            switch(e.Kind)
            {
                case "message":return messageY(id);
                case "fragment":return box2[id][1];
                case "operand":return operandTop[id];
                default:return rows.Get((e.Kind=="destroy"?"D:":"N:")+id).Y;
            }
        };
        Func<string,string> tokenKey=id=>{
            var e=after[id];
            switch(e.Kind){case "message":return "M:"+id;case "fragment":return "FO:"+id;case "operand":return "OO:"+id;case "destroy":return "D:"+id;default:return "N:"+id;}
        };
        var events=plan.Expected.Elements.Where(e=>e.Kind!="participant" && e.Kind!="interaction" && e.Kind!="execution").Select(e=>e.Id).OrderBy(eventY).ToArray();
        Func<SequenceRelayout.Token,string,bool> within=(t,container)=>container==root || t.Container==container
            || SequenceStructurePreflight.IsWithin(plan.Expected,t.Container,container) || (t.Kind=="OO" && t.Id==container);

        // ---- Bars.
        var bars=new Dictionary<string,double[]>(StringComparer.Ordinal);  // x, top, bottom
        Func<string,int> barDepth=id=>{int d=0;for(var at=after[id];Link(at,"outer").Length==1 && d<32;at=after[Link(at,"outer")[0]])d++;return d;};
        Func<string,int> oldBarDepth=id=>{int d=0;for(var at=before[id];Link(at,"outer").Length==1 && d<32 && before.ContainsKey(Link(at,"outer")[0]);at=before[Link(at,"outer")[0]])d++;return d;};
        foreach(var b in plan.Expected.Elements.Where(e=>e.Kind=="execution").OrderBy(e=>barDepth(e.Id)))
        {
            string lane=Link(b,"participant").Single();
            double x;
            bool had=before.ContainsKey(b.Id) && shapeOf.ContainsKey(b.Id);
            if(had && Link(before[b.Id],"participant").SequenceEqual(new[]{lane}) && oldBarDepth(b.Id)==barDepth(b.Id))x=Read(shapeOf[b.Id],"X")+laneShift(lane);
            else x=center(lane)+8*barDepth(b.Id);
            var sends=plan.Expected.Elements.Where(m=>m.Kind=="message" && Link(m,"sendExecution").Contains(b.Id)).Select(m=>m.Id).ToArray();
            var receives=plan.Expected.Elements.Where(m=>m.Kind=="message" && Link(m,"receiveExecution").Contains(b.Id)).Select(m=>m.Id).ToArray();
            string startAfter=Link(b,"startAfter").FirstOrDefault(),endBefore=Link(b,"endBefore").FirstOrDefault();
            string endIn=Link(b,"endContainer").FirstOrDefault()??root,parent=b.Parent??root;
            // Top: the message that opens it, or 20 above the row after the event before it.
            double top;
            // A bar the reader opens on a message it receives (within 10 of its top) keeps that
            // message as its start, at the same distance.
            string opener=null;double openerGap=0;
            if(had)
            {
                double oldTop=Read(shapeOf[b.Id],"Y");
                var near=receives.Where(m=>before.ContainsKey(m) && shapeOf.ContainsKey(m) && Math.Abs(Read(shapeOf[m],"SourceY")-oldTop)<=10.0)
                    .OrderBy(m=>Math.Abs(Read(shapeOf[m],"SourceY")-oldTop)).FirstOrDefault();
                // Only when the input says nothing else about where the bar starts.
                if(near!=null && (!b.Links.ContainsKey("startAfter") || startAfter==near)){opener=near;openerGap=oldTop-Read(shapeOf[near],"SourceY");}
            }
            // A bar that says nothing about where it starts or ends (no such link at all, as
            // opposed to an empty one) keeps its place and only reaches over its messages.
            bool knowsStart=b.Links.ContainsKey("startAfter"),knowsEnd=b.Links.ContainsKey("endBefore");
            double firstMessage=sends.Select(messageY).Concat(receives.Select(messageY)).DefaultIfEmpty(double.MaxValue).Min();
            if(opener!=null)top=messageY(opener)+openerGap;
            else if(had && !knowsStart)top=Math.Min(rows.Map(Read(shapeOf[b.Id],"Y")),firstMessage);
            else if(startAfter!=null && receives.Contains(startAfter))top=targetY(startAfter);
            else
            {
                double gen;
                // A new bar that nothing comes before starts where the first message it receives
                // arrives, when nothing leaves it earlier; otherwise 20 above the first row.
                var firstReceive=receives.OrderBy(messageY).FirstOrDefault();
                if(startAfter==null && !had && firstReceive!=null && sends.All(m=>messageY(m)>=messageY(firstReceive)))gen=targetY(firstReceive);
                else if(startAfter==null)gen=rows.Start-20;
                else
                {
                    int k=rows.IndexOf(tokenKey(startAfter));
                    // Past frames that close after it, unless the bar opens inside them.
                    while(k+1<rows.Tokens.Count && rows.Tokens[k+1].Kind=="FC" && !(parent==rows.Tokens[k+1].Id
                        || SequenceStructurePreflight.IsWithin(plan.Expected,parent,rows.Tokens[k+1].Id)))k++;
                    gen=rows.Tokens[k].After-20;
                }
                top=gen;
                if(had)
                {
                    double mapped=rows.Map(Read(shapeOf[b.Id],"Y"));
                    double first=sends.Concat(receives).Select(messageY).DefaultIfEmpty(double.MaxValue).Min();
                    bool ok=(startAfter==null || mapped>eventY(startAfter)+1) && mapped<=first
                        && !receives.Any(m=>Math.Abs(messageY(m)-mapped)<=10.5) && containerAt(x,mapped)==parent;
                    if(ok)top=mapped;
                }
            }
            // Bottom: under the last row it holds, inside the container it ends in.
            int stop=endBefore==null?rows.Tokens.Count:rows.IndexOf(tokenKey(endBefore));
            int last=stop-1;
            while(last>=0 && !within(rows.Tokens[last],endIn))last--;
            double bottom;
            var ender=last>=0?rows.Tokens[last]:null;
            if(ender!=null && ender.Kind=="D" && Link(after[ender.Id],"participant").Contains(lane))bottom=ender.Y;
            else bottom=ender!=null?ender.After-16:top+PumlBuild.MinimumBar;
            double cover=sends.Select(messageY).Concat(receives.Select(targetY)).Select(v=>v+16).DefaultIfEmpty(top+PumlBuild.MinimumBar).Max();
            if(ender==null || ender.Kind!="D")bottom=Math.Max(bottom,Math.Max(top+PumlBuild.MinimumBar,cover));
            if(had && !knowsEnd)bottom=Math.Max(rows.Map(Read(shapeOf[b.Id],"Y")+Read(shapeOf[b.Id],"Length")),Math.Max(cover,top+PumlBuild.MinimumBar));
            // A new nested bar that says nothing about its end closes with the bar around it.
            else if(!had && !knowsEnd && Link(b,"outer").Length==1 && bars.ContainsKey(Link(b,"outer")[0]))
                bottom=Math.Max(bars[Link(b,"outer")[0]][2],Math.Max(cover,top+PumlBuild.MinimumBar));
            else if(had)
            {
                double mapped=rows.Map(Read(shapeOf[b.Id],"Y")+Read(shapeOf[b.Id],"Length"));
                double next=endBefore==null?double.MaxValue:eventY(endBefore);
                double lastEvent=events.Where(id=>eventY(id)<next && id!=b.Id).Select(eventY).DefaultIfEmpty(double.MinValue).Max();
                bool ok=mapped>=lastEvent+1 && mapped<=next-1 && mapped>=cover-16+1 && mapped-top>=PumlBuild.MinimumBar-1e-9
                    && containerAt(x,mapped)==endIn && !(ender!=null && ender.Kind=="D");
                if(ok)bottom=mapped;
            }
            bars[b.Id]=new[]{x,top,bottom};
        }
        // A bar reaches over every bar nested in it.
        foreach(var b in plan.Expected.Elements.Where(e=>e.Kind=="execution").OrderByDescending(e=>barDepth(e.Id)))
        {
            var outer=Link(b,"outer");
            if(outer.Length!=1 || !bars.ContainsKey(outer[0]))continue;
            var o=bars[outer[0]];var mine=bars[b.Id];
            o[1]=Math.Min(o[1],mine[1]);o[2]=Math.Max(o[2],mine[2]);
        }
        // Then every bar that holds a message goes where Next Design puts it once the diagram is
        // edited (K194), as the generator draws it and SettleExecutions reads it: from its first
        // message to the reply closing it, or 20 under the later of its last message and the
        // bars its calls opened; a lane's destruction after its last message ends it there.
        {
            var ends=new Dictionary<string,Tuple<double,bool>>(StringComparer.Ordinal);
            var uses=plan.Expected.Elements.Where(e=>e.Kind=="execution").ToDictionary(e=>e.Id,e=>
                plan.Expected.Elements.Where(m=>m.Kind=="message" && Link(m,"sendExecution").Contains(e.Id)).Select(m=>new{Id=m.Id,Send=true,At=messageY(m.Id)})
                .Concat(plan.Expected.Elements.Where(m=>m.Kind=="message" && Link(m,"receiveExecution").Contains(e.Id)).Select(m=>new{Id=m.Id,Send=false,At=targetY(m.Id)}))
                .OrderBy(u=>u.At).ToList(),StringComparer.Ordinal);
            Func<string,string> sortOf=id=>{string v;return after[id].Attributes.TryGetValue("sort",out v)?v:"";};
            var destructions=plan.Expected.Elements.Where(e=>e.Kind=="destroy").Select(e=>new{Lane=Link(e,"participant").FirstOrDefault(),Y=eventY(e.Id)}).ToList();
            Func<string,int,Tuple<double,bool>> end=null;
            end=(id,depth)=>{
                Tuple<double,bool> known;if(ends.TryGetValue(id,out known))return known;
                var held=uses[id];var last=held.Last();Tuple<double,bool> result;
                if(last.Send && sortOf(last.Id)=="reply")result=Tuple.Create(last.At,false);
                else
                {
                    double point=last.At,reach=last.At;
                    foreach(var u in held.Where(u=>u.Send && sortOf(u.Id)!="reply"))
                        foreach(var callee in Link(after[u.Id],"receiveExecution").Where(c=>c!=id && uses.ContainsKey(c) && uses[c].Count>0 && uses[c][0].Id==u.Id))
                        {
                            if(depth>=64)continue;
                            var theirs=end(callee,depth+1);
                            if(!theirs.Item2)reach=Math.Max(reach,theirs.Item1);
                        }
                    result=Tuple.Create(Math.Max(point,reach)+20,false);
                    string lane=Link(after[id],"participant").FirstOrDefault();
                    var destroy=destructions.Where(d=>d.Lane==lane && d.Y>point).OrderBy(d=>d.Y).FirstOrDefault();
                    if(destroy!=null && !uses.Any(o=>o.Key!=id && Link(after[o.Key],"participant").FirstOrDefault()==lane && o.Value.Count>0 && o.Value[0].At>point && o.Value[0].At<destroy.Y))
                        result=Tuple.Create(destroy.Y,true);
                }
                ends[id]=result;return result;
            };
            // Every bar that holds a message, those the update does not touch too: the placement
            // above would put back margins Next Design drops on the next edit. A bar already where
            // the product puts it gets the same numbers, so nothing is written for it.
            foreach(var pair in bars.Where(p=>uses.ContainsKey(p.Key) && uses[p.Key].Count>0).ToList())
            {
                pair.Value[1]=uses[pair.Key][0].At;
                pair.Value[2]=end(pair.Key,0).Item1;
            }
        }

        // ---- Shapes already drawn: what changes on each.
        var shifted=new List<SequenceShiftedShape>();
        Action<string,string,List<string>,List<string>> write=(model,kind,keys,values)=>{
            if(keys.Count==0)return;
            var sh=shapeOf[model];
            foreach(var node in view.Properties.Values.Where(a=>a!=null && a.Items!=null).SelectMany(a=>a.Items).Where(n=>V(n,"Id")==V(sh,"Id")))
                for(int i=0;i<keys.Count;i++)node.Properties[keys[i]]=SequenceJson.Parse(values[i]);
            shifted.Add(new SequenceShiftedShape{ModelId=model,ShapeId=V(sh,"Id"),Kind=kind,Keys=keys.ToArray(),Values=values.ToArray()});
        };
        Func<List<string>> list=()=>new List<string>();
        Action<List<string>,List<string>,SequenceJson,string,double> put=(keys,values,sh,key,value)=>{
            if(sh[key]==null)return;
            if(Math.Abs(Read(sh,key)-value)>1e-9){keys.Add(key);values.Add(Number(value));}
        };
        // Lanes run down to the lowest thing drawn, as far past it as they did before.
        double oldFloor=0,newFloor=0;
        foreach(var pair in shapeOf.Where(p=>before.ContainsKey(p.Key) || endIds.Contains(p.Key)))
        {
            var sh=pair.Value;
            foreach(string key in new[]{"TargetY","SourceY"})if(sh[key]!=null)oldFloor=Math.Max(oldFloor,Read(sh,key));
            if(sh["Y"]!=null)
            {
                double bottom=Read(sh,"Y");
                foreach(string key in new[]{"Length","Height"})if(sh[key]!=null)bottom=Math.Max(bottom,Read(sh,"Y")+Read(sh,key));
                oldFloor=Math.Max(oldFloor,bottom);
            }
        }
        foreach(var t in rows.Tokens)
            newFloor=Math.Max(newFloor,t.Kind=="M"?t.Y+t.Drop:t.Kind=="N"?t.Y+t.Height:t.Kind=="D"?t.Y+20:t.Y);
        foreach(var b in bars.Values)newFloor=Math.Max(newFloor,b[2]);
        double laneGrowth=newFloor-oldFloor;
        foreach(var e in plan.Expected.Elements.Where(e=>before.ContainsKey(e.Id) && e.Kind!="interaction"))
        {
            if(!shapeOf.ContainsKey(e.Id))continue;
            var sh=shapeOf[e.Id];var keys=list();var values=list();
            switch(e.Kind)
            {
                case "participant":
                    put(keys,values,sh,"X",laneLayout.X[e.Id]);
                    if(sh["LaneLength"]!=null)put(keys,values,sh,"LaneLength",Read(sh,"LaneLength")+laneGrowth);
                    break;
                case "message":
                {
                    put(keys,values,sh,"SourceY",messageY(e.Id));put(keys,values,sh,"TargetY",targetY(e.Id));
                    if(sh["SelfloopBendsX"]!=null && Read(sh,"SelfloopBendsX")!=0)
                    {
                        // The loop keeps its distance from the bars it runs between.
                        var ends=new[]{"sendExecution","receiveExecution"}.SelectMany(role=>Link(e,role)).Where(bars.ContainsKey).ToArray();
                        double bend=ends.Length>0?ends.Max(id=>bars[id][0])+80:laneLayout.Map(Read(sh,"SelfloopBendsX"));
                        if(ends.Length>0 && ends.All(id=>before.ContainsKey(id) && shapeOf.ContainsKey(id)))
                            bend=Read(sh,"SelfloopBendsX")+(ends.Max(id=>bars[id][0])-ends.Max(id=>Read(shapeOf[id],"X")));
                        put(keys,values,sh,"SelfloopBendsX",bend);
                    }
                    break;
                }
                case "execution":
                {
                    var b=bars[e.Id];
                    put(keys,values,sh,"X",b[0]);put(keys,values,sh,"Y",b[1]);
                    // A bar carries its length as both Height and Length; writing one is ignored.
                    if(Math.Abs(Read(sh,"Length")-(b[2]-b[1]))>1e-9){keys.Add("Length");values.Add(Number(b[2]-b[1]));keys.Add("Height");values.Add(Number(b[2]-b[1]));}
                    break;
                }
                case "fragment":
                {
                    var b=box2[e.Id];
                    put(keys,values,sh,"X",b[0]);put(keys,values,sh,"Y",b[1]);put(keys,values,sh,"Width",b[2]-b[0]);put(keys,values,sh,"Height",b[3]-b[1]);
                    break;
                }
                case "operand":
                {
                    double position=rows.Get("OO:"+e.Id).Y-box2[e.Parent][1];
                    if(Math.Abs(Read(sh,"Position")-position)>1e-9)
                    {
                        // An operand written as an absolute position stays absolute.
                        var owner=shapeOf[before[e.Id].Parent];
                        bool absolute=Read(sh,"Position")>=Read(owner,"Y")-0.00001 && Read(sh,"Position")<=Read(owner,"Y")+Read(owner,"Height")+0.00001
                            && current.Elements.Where(o=>o.Parent==before[e.Id].Parent && o.Kind=="operand").All(o=>Read(shapeOf[o.Id],"Position")>=Read(owner,"Y")-0.00001);
                        double value=absolute?rows.Get("OO:"+e.Id).Y:position;
                        if(Math.Abs(Read(sh,"Position")-value)>1e-9){keys.Add("Position");values.Add(Number(value));}
                    }
                    break;
                }
                case "note":
                case "ref":
                {
                    var b=box2[e.Id];
                    put(keys,values,sh,"X",b[0]);put(keys,values,sh,"Y",b[1]);put(keys,values,sh,"Width",b[2]-b[0]);
                    break;
                }
                case "destroy":
                {
                    string lane=Link(e,"participant").Single();
                    put(keys,values,sh,"X",Read(sh,"X")+laneShift(lane));put(keys,values,sh,"Y",rows.Get("D:"+e.Id).Y);
                    break;
                }
            }
            write(e.Id,e.Kind,keys,values);
        }
        // Free ends of messages already drawn follow their message.
        foreach(string end in endIds.Where(id=>shapeOf.ContainsKey(id) && !leaving.Contains(id)))
        {
            var message=relations.Where(r=>(V(r,"MetamodelId")==R("SendMessage") || V(r,"MetamodelId")==R("ReceiveMessage")) && V(r,"SourceId")==end)
                .Select(r=>new{Id=V(r,"TargetId"),Send=V(r,"MetamodelId")==R("SendMessage")}).FirstOrDefault();
            if(message==null || !after.ContainsKey(message.Id))continue;
            var sh=shapeOf[end];var keys=list();var values=list();
            var other=Link(after[message.Id],message.Send?"receiveExecution":"sendExecution").FirstOrDefault();
            put(keys,values,sh,"X",other!=null && bars.ContainsKey(other)?bars[other][0]-60:laneLayout.Map(Read(sh,"X")));
            put(keys,values,sh,"Y",message.Send?messageY(message.Id):targetY(message.Id));
            write(end,"messageEnd",keys,values);
        }

        // ---- Lanes the input adds.
        var lanes=new List<SequenceAddedParticipant>();var newLaneShapes=new List<SequenceJson>();
        // Shapes whose size the product decides when there is no sample to copy it from.
        var loose=new List<string>();
        foreach(string id in gate.AddParticipants.Where(p=>laneTemplate==null))
        {
            // A diagram with no lane at all: built from the profile, as the generator builds one.
            Require(BaseTypes!=null && BaseTypes.Lifeline!=null && BaseTypes.OwnsLifeline!=null,"参加者の型情報が解決できていません。");
            string name=after[id].Text??"";
            newEntities.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType","Lifeline","MetamodelId",BaseTypes.Lifeline,"Name",name,"Fields",PumlBuild.Obj("Name",name)))));
            string relationId=Guid.NewGuid().ToString();
            newRelations.Add(relate(BaseTypes.OwnsLifeline,relationId,root,id));
            string laneShapeId=Guid.NewGuid().ToString();
            newLaneShapes.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",laneShapeId,"ModelId",id,"X",laneLayout.X[id],"Width",100,
                "LeftPadding",System.Array.IndexOf(newLanes,id)==0?190:140,"LaneLength",newFloor+40))));
            loose.Add(laneShapeId);
            lanes.Add(new SequenceAddedParticipant{ModelId=id,Metaclass=BaseTypes.Lifeline,Name=name,OwnerId=root,
                ShapeId=laneShapeId,TemplateShapeId="",RelationId=relationId,TemplateRelationId="row:"+BaseTypes.OwnsLifeline[2],X=Number(laneLayout.X[id])});
        }
        foreach(string id in gate.AddParticipants.Where(p=>laneTemplate!=null))
        {
            var owned=typed("___Interaction_Lifeline").Where(r=>V(r,"SourceId")==root).ToArray();
            Require(owned.Length>0,"既存の参加者の所有関連を取得できません。");
            string template=V(laneTemplate,"ModelId");
            Require(byId.ContainsKey(template) && V(byId[template],"EntityType")=="Lifeline","参加者の見本を取得できません。");
            var ownerLink=find("___Interaction_Lifeline",root,template);
            var entity=SequenceJson.Parse(byId[template].ToJsonString());
            entity.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(id));
            string name=after[id].Text??"";
            entity.Properties["Name"]=SequenceJson.Parse(SequencePayload.Q(name));
            if(entity["Fields"]!=null && entity["Fields"].Properties!=null && entity["Fields"]["Name"]!=null)
                entity["Fields"].Properties["Name"]=SequenceJson.Parse(SequencePayload.Q(name));
            newEntities.Add(entity);
            string relationId=Guid.NewGuid().ToString();
            newRelations.Add(copyRelation(ownerLink,relationId,root,id));
            string laneShapeId=Guid.NewGuid().ToString();
            var laneShape=SequenceJson.Parse(laneTemplate.ToJsonString());
            laneShape.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(laneShapeId));
            laneShape.Properties["ModelId"]=SequenceJson.Parse(SequencePayload.Q(id));
            laneShape.Properties["X"]=SequenceJson.Parse(Number(laneLayout.X[id]));
            if(laneShape["LaneLength"]!=null)laneShape.Properties["LaneLength"]=SequenceJson.Parse(Number(Read(laneTemplate,"LaneLength")+laneGrowth));
            newLaneShapes.Add(laneShape);
            lanes.Add(new SequenceAddedParticipant{ModelId=id,Metaclass=V(entity,"MetamodelId"),Name=name,OwnerId=root,
                ShapeId=laneShapeId,TemplateShapeId=V(laneTemplate,"Id"),RelationId=relationId,
                TemplateRelationId=V(ownerLink,"Id"),X=Number(laneLayout.X[id])});
        }
        if(newLaneShapes.Count>0)Collection(view,"Lifelines").Items.AddRange(newLaneShapes);

        // ---- Bars the input adds, and bars a new message needs where the input opens none.
        var additions=new List<SequenceAddedExecution>();var newBarShapes=new List<SequenceJson>();
        Action<string,string,double,double,double> addBar=(id,lane,x,top,bottom)=>{
            var sameLane=typed("OwnedExecutionSpecification").Where(r=>V(r,"SourceId")==lane && byId.ContainsKey(V(r,"TargetId"))).ToArray();
            var anyBar=typed("OwnedExecutionSpecification").Where(r=>byId.ContainsKey(V(r,"TargetId")) && shapeOf.ContainsKey(V(r,"TargetId"))).ToArray();
            var ownedLink=sameLane.Length>0?sameLane[0]:anyBar.FirstOrDefault();
            SequenceJson entity;var relationIds=new List<string>();var relationSources=new List<string>();var templateIds=new List<string>();
            string templateShape="";SequenceJson shape;
            if(ownedLink!=null)
            {
                string template=V(ownedLink,"TargetId");
                entity=SequenceJson.Parse(byId[template].ToJsonString());
                var ownerLink=find("___Interaction_ExecutionSpecification",root,template);
                // The lifeline has to be in place first: adding the bar to the interaction makes
                // the product build its shape, and that lookup needs the owning lifeline.
                foreach(var pair in new[]{new object[]{ownedLink,lane},new object[]{ownerLink,root}})
                {
                    string relationId=Guid.NewGuid().ToString();
                    newRelations.Add(copyRelation((SequenceJson)pair[0],relationId,(string)pair[1],id));
                    relationIds.Add(relationId);relationSources.Add((string)pair[1]);templateIds.Add(V((SequenceJson)pair[0],"Id"));
                }
                templateShape=V(shapeOf[template],"Id");
                shape=SequenceJson.Parse(shapeOf[template].ToJsonString());
            }
            else
            {
                Require(BaseTypes!=null && BaseTypes.Execution!=null && BaseTypes.LaneExecution!=null && BaseTypes.OwnsExecution!=null,"実行区間の型情報が解決できていません。");
                entity=SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType","ExecutionSpecification","MetamodelId",BaseTypes.Execution,"Name","","Fields",PumlBuild.Obj("Name",""))));
                foreach(var pair in new[]{new object[]{BaseTypes.LaneExecution,lane},new object[]{BaseTypes.OwnsExecution,root}})
                {
                    string relationId=Guid.NewGuid().ToString();
                    newRelations.Add(relate((string[])pair[0],relationId,(string)pair[1],id));
                    relationIds.Add(relationId);relationSources.Add((string)pair[1]);templateIds.Add("row:"+((string[])pair[0])[2]);
                }
                shape=SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("X",0,"Y",0,"Length",0,"Height",0)));
            }
            entity.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(id));
            newEntities.Add(entity);
            string shapeId=Guid.NewGuid().ToString();
            shape.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(shapeId));
            shape.Properties["ModelId"]=SequenceJson.Parse(SequencePayload.Q(id));
            shape.Properties["X"]=SequenceJson.Parse(Number(x));shape.Properties["Y"]=SequenceJson.Parse(Number(top));
            shape.Properties["Length"]=SequenceJson.Parse(Number(bottom-top));shape.Properties["Height"]=SequenceJson.Parse(Number(bottom-top));
            newBarShapes.Add(shape);
            if(templateShape.Length==0)loose.Add(shapeId);
            additions.Add(new SequenceAddedExecution{ModelId=id,Metaclass=V(entity,"MetamodelId"),Name=V(entity,"Name")??"",
                OwnerId=root,ShapeId=shapeId,TemplateShapeId=templateShape,
                Geometry=PumlBuild.Json(new[]{Number(x),Number(top),Number(bottom-top)}),
                RelationIds=relationIds.ToArray(),RelationSources=relationSources.ToArray(),TemplateRelationIds=templateIds.ToArray()});
        };
        foreach(string id in gate.AddExecutions)
        {
            var b=bars[id];addBar(id,Link(after[id],"participant").Single(),b[0],b[1],b[2]);
        }
        // Bars the input does not open are never made up; the preflight stops such a message.
        var implicitPorts=new Dictionary<string,string>(StringComparer.Ordinal);
        if(newBarShapes.Count>0)Collection(view,"ExecutionSpecifications").Items.AddRange(newBarShapes);

        // ---- Frames and branches the input adds.
        var frames=new List<SequenceAddedFragment>();var branches=new List<SequenceAddedOperand>();
        var newFrameShapes=new List<SequenceJson>();var newOperandShapes=new List<SequenceJson>();
        if(gate.AddFragments.Count+gate.AddOperands.Count>0 || gate.AddMessages.Any(id=>after[id].Parent!=root))
            Require(types!=null && types.Complete(),"フラグメントの型情報が解決できていません。");
        foreach(string id in gate.AddFragments)
        {
            var wanted=after[id];string name=wanted.Text??"";string operatorName;
            Require(wanted.Attributes.TryGetValue("operator",out operatorName),"追加するフラグメントに演算子がありません。");
            string operatorValue;
            Require(types.Operators.TryGetValue(operatorName,out operatorValue),"この図のプロファイルに演算子 "+operatorName+" がありません。");
            newEntities.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType","CombinedFragment",
                "MetamodelId",types.Fragment,"Name",name,"Fields",PumlBuild.Obj("Name",name,"Operator",operatorValue)))));
            var added=new SequenceAddedFragment{ModelId=id,Metaclass=types.Fragment,Name=name,OwnerId=root,Text=operatorName=="group"?name:operatorName};
            // Ownership first, then the lanes the frame spans, as the generator writes them; a
            // frame inside a branch is also tied to that branch.
            var wiring=new List<object[]>{new object[]{types.Owns,root,id}};
            foreach(string lane in newLanes)wiring.Add(new object[]{types.Crossing,id,lane});
            if(wanted.Parent!=root)
            {
                Require(types.Nested!=null,"入れ子の枠の関連の型情報が解決できていません。");
                wiring.Add(new object[]{types.Nested,wanted.Parent,id});
            }
            foreach(var row in wiring)
            {
                string relationId=Guid.NewGuid().ToString();var type=(string[])row[0];
                newRelations.Add(relate(type,relationId,(string)row[1],(string)row[2]));
                added.RelationIds=added.RelationIds.Concat(new[]{relationId}).ToArray();
                added.RelationSources=added.RelationSources.Concat(new[]{(string)row[1]}).ToArray();
                added.RelationTargets=added.RelationTargets.Concat(new[]{(string)row[2]}).ToArray();
                added.RelationFields=added.RelationFields.Concat(new[]{type[2]}).ToArray();
            }
            var b=box2[id];
            string shapeId=Guid.NewGuid().ToString();added.ShapeId=shapeId;added.TemplateShapeId="";
            added.Geometry=PumlBuild.Json(new[]{Number(b[0]),Number(b[1]),Number(b[2]-b[0]),Number(b[3]-b[1])});
            newFrameShapes.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",shapeId,"ModelId",id,
                "X",Number(b[0]),"Y",Number(b[1]),"Width",Number(b[2]-b[0]),"Height",Number(b[3]-b[1])))));
            frames.Add(added);
        }
        foreach(string id in gate.AddOperands)
        {
            var wanted=after[id];string guard=wanted.Text??"";
            newEntities.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType","InteractionOperand",
                "MetamodelId",types.Operand,"Name","","Fields",PumlBuild.Obj("Name","","Guard",guard)))));
            string relationId=Guid.NewGuid().ToString();
            newRelations.Add(relate(types.Branches,relationId,wanted.Parent,id));
            string shapeId=Guid.NewGuid().ToString();
            string position=Number(rows.Get("OO:"+id).Y-box2[wanted.Parent][1]);
            // A frame already drawn with absolute positions keeps them absolute.
            if(before.ContainsKey(wanted.Parent))
            {
                var owner=shapeOf[wanted.Parent];
                var siblings=current.Elements.Where(o=>o.Parent==wanted.Parent && o.Kind=="operand").ToArray();
                if(siblings.Length>0 && siblings.All(o=>Read(shapeOf[o.Id],"Position")>=Read(owner,"Y")-0.00001 && Read(shapeOf[o.Id],"Position")<=Read(owner,"Y")+Read(owner,"Height")+0.00001))
                    position=Number(rows.Get("OO:"+id).Y);
            }
            newOperandShapes.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",shapeId,"ModelId",id,"Position",position))));
            branches.Add(new SequenceAddedOperand{ModelId=id,Metaclass=types.Operand,Name="",OwnerId=wanted.Parent,
                ShapeId=shapeId,TemplateShapeId="",Guard=guard,Position=position,
                RelationIds=new[]{relationId},RelationSources=new[]{wanted.Parent},RelationFields=new[]{types.Branches[2]}});
        }
        if(newFrameShapes.Count>0)Collection(view,"Fragments").Items.AddRange(newFrameShapes);
        if(newOperandShapes.Count>0)Collection(view,"Operands").Items.AddRange(newOperandShapes);

        // ---- Messages the input adds.
        var wires=new List<SequenceAddedMessage>();var newMessageShapes=new List<SequenceJson>();
        var notes=new List<SequenceAddedNote>();var newEndShapes=new List<SequenceJson>();
        var walkNew=SequenceStructurePreflight.Flatten(plan.Expected);
        Func<SequenceElement,string> written=e=>{string v=SequenceStructurePreflight.Attribute(e);return v=="destroy"?"sync":v;};
        var messageEntities=current.Elements.Where(e=>e.Kind=="message" && byId.ContainsKey(e.Id) && shapeOf.ContainsKey(e.Id)).ToArray();
        // A message to or from outside the diagram ends in a free end of its own, 60 left of
        // the bar at its other end, as the generator draws it.
        Func<double,double,string> makeEnd=(endX,endAt)=>{
            string endId=Guid.NewGuid().ToString();
            var endSample=entities.FirstOrDefault(e=>V(e,"EntityType")=="MessageEnd");
            SequenceJson endEntity;
            if(endSample!=null)endEntity=SequenceJson.Parse(endSample.ToJsonString());
            else
            {
                Require(BaseTypes!=null && BaseTypes.MessageEnd!=null && BaseTypes.OwnsMessageEnd!=null,"図外の端の型情報が解決できていません。");
                endEntity=SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("EntityType","MessageEnd","MetamodelId",BaseTypes.MessageEnd,"Name","","Fields",PumlBuild.Obj("Name",""))));
            }
            endEntity.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(endId));
            newEntities.Add(endEntity);
            var end=new SequenceAddedNote{Kind="messageEnd",ModelId=endId,Metaclass=V(endEntity,"MetamodelId"),Name=V(endEntity,"Name")??"",OwnerId=root,Text=""};
            var owns=endSample!=null?relations.FirstOrDefault(r=>V(r,"MetamodelId")==R("___Interaction_MessageEnd") && V(r,"TargetId")==V(endSample,"Id")):null;
            string ownsId=Guid.NewGuid().ToString();
            if(owns!=null)newRelations.Add(copyRelation(owns,ownsId,root,endId));
            else
            {
                Require(BaseTypes!=null && BaseTypes.OwnsMessageEnd!=null,"図外の端の所有関連の型情報が解決できていません。");
                newRelations.Add(relate(BaseTypes.OwnsMessageEnd,ownsId,root,endId));
            }
            end.RelationIds=new[]{ownsId};end.RelationSources=new[]{root};end.RelationTargets=new[]{endId};
            end.RelationFields=new[]{owns!=null?"relation:"+V(owns,"Id"):BaseTypes.OwnsMessageEnd[2]};
            string endShape=Guid.NewGuid().ToString();end.ShapeId=endShape;
            end.Geometry=PumlBuild.Json(new[]{Number(endX),Number(endAt),"10","10"});
            newEndShapes.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",endShape,"ModelId",endId,"X",Number(endX),"Y",Number(endAt),"Width",10,"Height",10))));
            notes.Add(end);
            return endId;
        };
        foreach(string id in gate.AddMessages)
        {
            var wanted=after[id];
            string sort=SequenceStructurePreflight.Attribute(wanted);
            var earlier=walkNew.Take(System.Array.IndexOf(walkNew,id)).Where(before.ContainsKey).Select(e=>after[e]).Where(e=>e.Kind=="message" && byId.ContainsKey(e.Id)).ToArray();
            var pool=earlier.Concat(messageEntities.Where(e=>after.ContainsKey(e.Id)).Select(e=>after[e.Id]).Except(earlier)).ToArray();
            var sameSort=pool.Where(e=>written(e)==written(wanted)).ToArray();
            SequenceElement template=sameSort.Length>0?(earlier.Where(e=>written(e)==written(wanted)).LastOrDefault()??sameSort[0]):pool.LastOrDefault();
            SequenceJson entity;
            if(template!=null)
            {
                entity=SequenceJson.Parse(byId[template.Id].ToJsonString());
                if(written(template)!=written(wanted))
                {
                    string literal;
                    Require(SortLiterals.TryGetValue(written(wanted),out literal),"メッセージ種別 "+written(wanted)+" の値をプロファイルから決められません。");
                    Require(entity["Fields"]!=null && entity["Fields"].Properties!=null,"メッセージの見本に種別の欄がありません。");
                    entity["Fields"].Properties["MessageSort"]=SequenceJson.Parse(SequencePayload.Q(literal));
                }
            }
            else
            {
                string literal=null;
                Require(BaseTypes!=null && BaseTypes.Message!=null && SortLiterals.TryGetValue(written(wanted),out literal),"メッセージの型情報が解決できていません。");
                entity=SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType","Message","MetamodelId",BaseTypes.Message,"Name","","Fields",PumlBuild.Obj("Name","","MessageSort",literal))));
            }
            entity.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(id));
            string name=wanted.Text??"";
            entity.Properties["Name"]=SequenceJson.Parse(SequencePayload.Q(name));
            if(entity["Fields"]!=null && entity["Fields"].Properties!=null && entity["Fields"]["Name"]!=null)
                entity["Fields"].Properties["Name"]=SequenceJson.Parse(SequencePayload.Q(name));
            newEntities.Add(entity);
            double y=messageY(id),endY=targetY(id);
            var added=new SequenceAddedMessage{ModelId=id,Metaclass=V(entity,"MetamodelId"),Name=name,OwnerId=root,Y=Number(y),TargetY=Number(endY),
                Sender=Link(wanted,"sender").FirstOrDefault()??"",Receiver=Link(wanted,"receiver").FirstOrDefault()??"",Sort=sort,TemplateModelId=template==null?"":template.Id};
            var relationIds=new List<string>();var relationSources=new List<string>();var templateIds=new List<string>();var fields=new List<string>();
            Action<string,string> ports=(role,port)=>{if(role=="send")added.SendPort=port;else added.ReceivePort=port;};
            foreach(string role in new[]{"send","receive"})
            {
                string kind=role=="send"?"SendMessage":"ReceiveMessage";
                string port=Link(wanted,role+"Execution").FirstOrDefault();
                string implicitBar;if(port==null && implicitPorts.TryGetValue(id+"|"+role,out implicitBar))port=implicitBar;
                string relationId=Guid.NewGuid().ToString();
                if(port!=null)
                {
                    ports(role,port);
                    var sample=template==null?null:relations.FirstOrDefault(r=>V(r,"MetamodelId")==R(kind) && V(r,"TargetId")==template.Id && !endIds.Contains(V(r,"SourceId")));
                    sample=sample??typed(kind).FirstOrDefault(r=>!endIds.Contains(V(r,"SourceId")));
                    if(sample!=null){newRelations.Add(copyRelation(sample,relationId,port,id));templateIds.Add(V(sample,"Id"));fields.Add("");}
                    else
                    {
                        var row=role=="send"?(BaseTypes==null?null:BaseTypes.SendFromBar):(BaseTypes==null?null:BaseTypes.ReceiveFromBar);
                        Require(row!=null,"メッセージの接続の型情報が解決できていません。");
                        newRelations.Add(relate(row,relationId,port,id));templateIds.Add("");fields.Add(row[2]);
                    }
                    relationIds.Add(relationId);relationSources.Add(port);
                    continue;
                }
                string otherBar=role=="send"?(Link(wanted,"receiveExecution").FirstOrDefault()??(implicitPorts.ContainsKey(id+"|receive")?implicitPorts[id+"|receive"]:null))
                    :(Link(wanted,"sendExecution").FirstOrDefault()??(implicitPorts.ContainsKey(id+"|send")?implicitPorts[id+"|send"]:null));
                Require(otherBar!=null && bars.ContainsKey(otherBar),"図外のメッセージの相手側の実行区間がありません。");
                string endId=makeEnd(bars[otherBar][0]-60,role=="send"?y:endY);ports(role,endId);
                var endRow=role=="send"?(BaseTypes==null?null:BaseTypes.SendFromEnd):(BaseTypes==null?null:BaseTypes.ReceiveFromEnd);
                var endLink=relations.FirstOrDefault(r=>V(r,"MetamodelId")==R(kind) && endIds.Contains(V(r,"SourceId")));
                if(endLink!=null){newRelations.Add(copyRelation(endLink,relationId,endId,id));templateIds.Add(V(endLink,"Id"));fields.Add("");}
                else
                {
                    Require(endRow!=null,"図外の端の接続の型情報が解決できていません。");
                    newRelations.Add(relate(endRow,relationId,endId,id));templateIds.Add("");fields.Add(endRow[2]);
                }
                relationIds.Add(relationId);relationSources.Add(endId);
            }
            {
                string relationId=Guid.NewGuid().ToString();
                var owner=template!=null?find("___Interaction_Message",root,template.Id):typed("___Interaction_Message").FirstOrDefault();
                if(owner!=null){newRelations.Add(copyRelation(owner,relationId,root,id));templateIds.Add(V(owner,"Id"));fields.Add("");}
                else
                {
                    Require(BaseTypes!=null && BaseTypes.OwnsMessage!=null,"メッセージの所有関連の型情報が解決できていません。");
                    newRelations.Add(relate(BaseTypes.OwnsMessage,relationId,root,id));templateIds.Add("");fields.Add(BaseTypes.OwnsMessage[2]);
                }
                relationIds.Add(relationId);relationSources.Add(root);
            }
            // A message inside a frame is owned by the interaction and also pointed at by the
            // operand it sits in, the way the generator writes it.
            if(wanted.Parent!=root)
            {
                string relationId=Guid.NewGuid().ToString();
                newRelations.Add(relate(types.OperandMessage,relationId,wanted.Parent,id));
                relationIds.Add(relationId);relationSources.Add(wanted.Parent);templateIds.Add("");fields.Add(types.OperandMessage[2]);
            }
            added.RelationIds=relationIds.ToArray();added.RelationSources=relationSources.ToArray();
            added.TemplateRelationIds=templateIds.ToArray();added.RelationFields=fields.ToArray();
            string wireShapeId=Guid.NewGuid().ToString();
            SequenceJson wireShape;
            if(template!=null){wireShape=SequenceJson.Parse(shapeOf[template.Id].ToJsonString());added.TemplateShapeId=V(shapeOf[template.Id],"Id");}
            else {wireShape=SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("SourceY",0,"TargetY",0,"IsRightAtFrame",false,"SelfloopBendsX",0)));added.TemplateShapeId="";}
            wireShape.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(wireShapeId));
            wireShape.Properties["ModelId"]=SequenceJson.Parse(SequencePayload.Q(id));
            wireShape.Properties["SourceY"]=SequenceJson.Parse(Number(y));
            wireShape.Properties["TargetY"]=SequenceJson.Parse(Number(endY));
            double bend=0;
            if(selfCall(wanted))
            {
                // The loop goes 80 right of the further of its two bars, as the generator draws it.
                bend=new[]{added.SendPort,added.ReceivePort}.Where(bars.ContainsKey).Select(p=>bars[p][0]).DefaultIfEmpty(center(added.Sender)).Max()+80;
                added.Bend=Number(bend);
            }
            if(wireShape["SelfloopBendsX"]!=null)wireShape.Properties["SelfloopBendsX"]=SequenceJson.Parse(Number(bend));
            else if(bend!=0)wireShape.Properties["SelfloopBendsX"]=SequenceJson.Parse(Number(bend));
            if(bend==0 && template!=null && shapeOf[template.Id]["SelfloopBendsX"]!=null && Read(shapeOf[template.Id],"SelfloopBendsX")!=0)added.Bend="0";
            added.ShapeId=wireShapeId;
            newMessageShapes.Add(wireShape);
            wires.Add(added);
        }
        if(newMessageShapes.Count>0)Collection(view,"Messages").Items.AddRange(newMessageShapes);

        // ---- Notes and refs the input adds.
        var newNoteShapes=new List<SequenceJson>();var newRefShapes=new List<SequenceJson>();
        foreach(string id in gate.AddNotes.Concat(gate.AddRefs))
        {
            var wanted=after[id];string text=wanted.Text??"";bool isRef=wanted.Kind=="ref";
            if(isRef)Require(refTypes!=null && refTypes.Owns!=null && refTypes.Crossing!=null,"refの型情報が解決できていません。");
            else Require(noteTypes!=null && noteTypes.Owns!=null && noteTypes.Owns.Length==3,"Noteの型情報が解決できていません。");
            var b=box2[id];
            var fields=new Dictionary<string,object>{{"Name",text}};
            // A rich-text body is shown from the name, as the generator writes it.
            if(!isRef && noteTypes.Field!="Name" && noteTypes.Storage=="String")fields[noteTypes.Field]=text;
            string metaclass=isRef?refTypes.Class:noteTypes.Class;
            newEntities.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType",isRef?"InteractionUse":"InteractionNote",
                "MetamodelId",metaclass,"Name",text,"Fields",fields))));
            var wiring=new List<object[]>{new object[]{isRef?refTypes.Owns:noteTypes.Owns,root,id}};
            if(isRef)foreach(string lane in Link(wanted,"targets"))wiring.Add(new object[]{refTypes.Crossing,id,lane});
            string reference;
            if(isRef && refTypes.RefersTo!=null && wanted.Attributes.TryGetValue("reference",out reference) && !string.IsNullOrEmpty(reference))
                wiring.Add(new object[]{refTypes.RefersTo,id,reference});
            if(isRef && wanted.Parent!=root)
            {
                Require(types!=null && types.Nested!=null,"枠の中のrefの関連の型情報が解決できていません。");
                wiring.Add(new object[]{types.Nested,wanted.Parent,id});
            }
            var added=new SequenceAddedNote{Kind=wanted.Kind,ModelId=id,Metaclass=metaclass,Name=text,OwnerId=root,Text=text,
                Geometry=PumlBuild.Json(new[]{Number(b[0]),Number(b[1]),Number(b[2]-b[0]),Number(b[3]-b[1])})};
            foreach(var row in wiring)
            {
                var type=(string[])row[0];string relationId=Guid.NewGuid().ToString();
                newRelations.Add(relate(type,relationId,(string)row[1],(string)row[2]));
                added.RelationIds=added.RelationIds.Concat(new[]{relationId}).ToArray();
                added.RelationSources=added.RelationSources.Concat(new[]{(string)row[1]}).ToArray();
                added.RelationTargets=added.RelationTargets.Concat(new[]{(string)row[2]}).ToArray();
                added.RelationFields=added.RelationFields.Concat(new[]{type[2]}).ToArray();
            }
            string shapeId=Guid.NewGuid().ToString();added.ShapeId=shapeId;
            // A ref linked to an interaction may show that interaction's name; take what it reads back.
            if(isRef && wanted.Attributes.TryGetValue("reference",out reference) && !string.IsNullOrEmpty(reference) && refTypes.RefersTo!=null)loose.Add(shapeId);
            var shape=SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",shapeId,"ModelId",id,
                "X",Number(b[0]),"Y",Number(b[1]),"Width",Number(b[2]-b[0]),"Height",Number(b[3]-b[1]))));
            (isRef?newRefShapes:newNoteShapes).Add(shape);
            notes.Add(added);
        }
        if(newNoteShapes.Count>0)Collection(view,"Notes").Items.AddRange(newNoteShapes);
        if(newRefShapes.Count>0)Collection(view,"InteractionUses").Items.AddRange(newRefShapes);

        // ---- Destructions the input adds: 25 under the message that destroys the lane.
        var newDestroyShapes=new List<SequenceJson>();
        Func<string,string> killerOf=id=>{
            int at=System.Array.IndexOf(walkNew,id);
            for(int i=at-1;i>=0;i--){var e=after[walkNew[i]];if(e.Kind=="message")return Link(e,"receiver").SequenceEqual(Link(after[id],"participant"))?e.Id:null;if(e.Kind!="execution")return null;}
            return null;
        };
        foreach(string id in gate.AddDestroys)
        {
            var types2=DestroyTypes;
            Require(types2!=null && types2.Owns!=null && types2.Target!=null,"破棄の型情報が解決できていません。");
            var wanted=after[id];string lane=Link(wanted,"participant").Single();
            double y=rows.Get("D:"+id).Y;double x=center(lane)-10;
            newEntities.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType","Destruction","MetamodelId",types2.Class,"Name","","Fields",PumlBuild.Obj("Name","")))));
            var added=new SequenceAddedNote{Kind="destroy",ModelId=id,Metaclass=types2.Class,Name="",OwnerId=root,Text="",
                Geometry=PumlBuild.Json(new[]{Number(x),Number(y),"20","20"})};
            var wiring=new List<object[]>{new object[]{types2.Owns,root,id},new object[]{types2.Target,id,lane}};
            string killer=killerOf(id);
            if(types2.Message!=null && killer!=null)wiring.Add(new object[]{types2.Message,id,killer});
            foreach(var row in wiring)
            {
                var type=(string[])row[0];string relationId=Guid.NewGuid().ToString();
                newRelations.Add(relate(type,relationId,(string)row[1],(string)row[2]));
                added.RelationIds=added.RelationIds.Concat(new[]{relationId}).ToArray();
                added.RelationSources=added.RelationSources.Concat(new[]{(string)row[1]}).ToArray();
                added.RelationTargets=added.RelationTargets.Concat(new[]{(string)row[2]}).ToArray();
                added.RelationFields=added.RelationFields.Concat(new[]{type[2]}).ToArray();
            }
            string shapeId=Guid.NewGuid().ToString();added.ShapeId=shapeId;
            newDestroyShapes.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",shapeId,"ModelId",id,"X",Number(x),"Y",Number(y),"Width",20,"Height",20))));
            notes.Add(added);
        }
        if(newDestroyShapes.Count>0)Collection(view,"Destructions").Items.AddRange(newDestroyShapes);
        // A destruction already drawn points at the message that now destroys its lane.
        foreach(var d in plan.Expected.Elements.Where(e=>e.Kind=="destroy" && before.ContainsKey(e.Id)))
        {
            var link=relations.Where(r=>V(r,"MetamodelId")==R("DestroyMessage") && V(r,"SourceId")==d.Id).ToArray();
            string killer=killerOf(d.Id);
            if(link.Length==1 && killer!=null && V(link[0],"TargetId")!=killer)resend(link[0],null,killer);
        }

        // ---- Existing messages whose ends, sort or container change.
        foreach(string id in gate.ReconnectMessages.Concat(gate.ResendMessages).Distinct())
        {
            Require(byId.ContainsKey(id) && V(byId[id],"EntityType")=="Message","変更対象のメッセージが退避データにありません。");
            foreach(string role in new[]{"send","receive"})
            {
                if(!(role=="send"?gate.ResendMessages:gate.ReconnectMessages).Contains(id))continue;
                string kind=role=="send"?"SendMessage":"ReceiveMessage";
                var link=relations.Where(r=>V(r,"MetamodelId")==R(kind) && V(r,"TargetId")==id).ToArray();
                Require(link.Length==1,(role=="send"?"送信":"受信")+"接続が一意ではありません。");
                string was=V(link[0],"SourceId");
                // The export has to agree with the diagram that was read, or it is stale.
                Require(endIds.Contains(was)?Link(before[id],role+"Execution").Length==0:Link(before[id],role+"Execution").SequenceEqual(new[]{was}),
                    "退避データの"+(role=="send"?"送信":"受信")+"接続が読み取った図と一致しません。");
                string port=Link(after[id],role+"Execution").FirstOrDefault();
                string made;
                if(port==null && implicitPorts.TryGetValue(id+"|"+role,out made))port=made;
                if(port==null)
                {
                    // The message now goes to or comes from outside the diagram: a free end of its own.
                    if(endIds.Contains(was))continue;
                    string otherBar=Link(after[id],role=="send"?"receiveExecution":"sendExecution").FirstOrDefault();
                    Require(otherBar!=null && bars.ContainsKey(otherBar),"図外のメッセージの相手側の実行区間がありません。");
                    port=makeEnd(bars[otherBar][0]-60,role=="send"?messageY(id):targetY(id));
                }
                // A free end the message no longer uses goes with it.
                if(endIds.Contains(was) && !removedEnds.Contains(was)){removedEnds.Add(was);leaving.Add(was);}
                if(was!=port)resend(link[0],port,null);
            }
        }
        var sortChanged=new List<string[]>();
        foreach(string id in gate.SortChanges)
        {
            string literal;string sort=SequenceStructurePreflight.Attribute(after[id]);
            Require(SortLiterals.TryGetValue(sort,out literal),"メッセージ種別 "+sort+" の値をプロファイルから決められません。");
            var entity=SequenceJson.Parse(byId[id].ToJsonString());
            Require(entity["Fields"]!=null && entity["Fields"].Properties!=null,"メッセージに種別の欄がありません。");
            entity["Fields"].Properties["MessageSort"]=SequenceJson.Parse(SequencePayload.Q(literal));
            newEntities.Add(entity);sortChanged.Add(new[]{id,sort});
        }
        var moved=new List<SequenceMovedMessage>();
        // What points at an element from the branch it sits in: re-pointed when it moves between
        // branches, added when it moves into one, taken off when it leaves for the top level.
        Action<string,string,string[]> rehome=(id,relationName,row)=>{
            string was=before[id].Parent,now=after[id].Parent;
            var link=relations.Where(r=>V(r,"MetamodelId")==R(relationName) && V(r,"TargetId")==id).ToArray();
            bool fromBranch=was!=root && before.ContainsKey(was) && before[was].Kind=="operand";
            bool toBranch=now!=root && after.ContainsKey(now) && after[now].Kind=="operand";
            if(fromBranch && link.Length==1)
            {
                if(toBranch){if(V(link[0],"SourceId")!=now)resend(link[0],now,null);}
                // A branch that is deleted takes the relation with it; only one that stays is untied.
                else if(!leaving.Contains(was))unrelate.Add(V(link[0],"Id"));
            }
            else if(toBranch && link.Length==0)
            {
                Require(row!=null,"所属の関連の型情報が解決できていません: "+relationName);
                string relationId=Guid.NewGuid().ToString();
                newRelations.Add(relate(row,relationId,now,id));
                moved.Add(new SequenceMovedMessage{ModelId=id,OperandId=now,RelationId=relationId,Field=row[2]});
            }
        };
        foreach(string id in gate.MoveMessages)rehome(id,"OperandTargetMessage",types==null?null:types.OperandMessage);
        foreach(string id in gate.NestChanges.Where(id=>after[id].Kind=="fragment" || after[id].Kind=="ref"))
            rehome(id,"NestedInteractionFragment",types==null?null:types.Nested);
        foreach(string id in gate.NestChanges.Where(id=>after[id].Kind=="execution"))
        {
            var link=relations.Where(r=>V(r,"MetamodelId")==R("OwnedExecutionSpecification") && V(r,"TargetId")==id).ToArray();
            Require(link.Length==1,"実行区間の参加者の関連が一意ではありません。");
            resend(link[0],Link(after[id],"participant").Single(),null);
        }
        foreach(string id in gate.RefTargetChanges)
        {
            var was=Link(before[id],"targets");var now=Link(after[id],"targets");
            foreach(var link in relations.Where(r=>V(r,"MetamodelId")==R("CrossingFragmentCoveredLifeline") && V(r,"SourceId")==id && !now.Contains(V(r,"TargetId"))))
                unrelate.Add(V(link,"Id"));
            foreach(string lane in now.Where(l=>!was.Contains(l)))
            {
                Require(refTypes!=null && refTypes.Crossing!=null,"refの型情報が解決できていません。");
                string relationId=Guid.NewGuid().ToString();
                newRelations.Add(relate(refTypes.Crossing,relationId,id,lane));
                notes.Add(new SequenceAddedNote{Kind="link",ModelId=id,RelationIds=new[]{relationId},RelationSources=new[]{id},RelationTargets=new[]{lane},RelationFields=new[]{refTypes.Crossing[2]}});
            }
            string reference;
            if(after[id].Attributes.TryGetValue("reference",out reference) && !string.IsNullOrEmpty(reference) && refTypes!=null && refTypes.RefersTo!=null)
            {
                var link=relations.Where(r=>V(r,"MetamodelId")==refTypes.RefersTo[0] && V(r,"SourceId")==id).ToArray();
                if(link.Length==1){if(V(link[0],"TargetId")!=reference)resend(link[0],null,reference);}
                else if(link.Length==0)
                {
                    string relationId=Guid.NewGuid().ToString();
                    newRelations.Add(relate(refTypes.RefersTo,relationId,id,reference));
                    notes.Add(new SequenceAddedNote{Kind="link",ModelId=id,RelationIds=new[]{relationId},RelationSources=new[]{id},RelationTargets=new[]{reference},RelationFields=new[]{refTypes.RefersTo[2]}});
                }
            }
        }
        var shapeTexts=new List<string[]>();
        foreach(string id in gate.OperatorChanges)
        {
            string operatorName=after[id].Attributes["operator"];string operatorValue=null;
            Require(types!=null && types.Operators.TryGetValue(operatorName,out operatorValue),"この図のプロファイルに演算子 "+operatorName+" がありません。");
            var entity=SequenceJson.Parse(byId[id].ToJsonString());
            Require(entity["Fields"]!=null && entity["Fields"].Properties!=null,"枠に演算子の欄がありません。");
            entity["Fields"].Properties["Operator"]=SequenceJson.Parse(SequencePayload.Q(operatorValue));
            newEntities.Add(entity);
            if(operatorName!="group")shapeTexts.Add(new[]{V(shapeOf[id],"Id"),operatorName});
        }
        // A rename re-imports the element with the same id and its new text; the product
        // updates it in place.
        var renamed=new List<string[]>();
        foreach(string id in gate.Renames)
        {
            Require(byId.ContainsKey(id),"本文を変える要素が退避データにありません。");
            string text=after[id].Text??"";
            var entity=newEntities.FirstOrDefault(e=>V(e,"Id")==id);
            if(entity==null){entity=SequenceJson.Parse(byId[id].ToJsonString());newEntities.Add(entity);}
            bool operand=before[id].Kind=="operand";
            if(!operand)entity.Properties["Name"]=SequenceJson.Parse(SequencePayload.Q(text));
            var fields=entity["Fields"];
            if(fields!=null && fields.Properties!=null)
                foreach(string key in operand?new[]{"Guard"}:new[]{"Name","Body","Text"})
                    if(fields[key]!=null && fields[key].Raw!=null && fields[key].Raw.StartsWith("\"",StringComparison.Ordinal))
                        fields.Properties[key]=SequenceJson.Parse(SequencePayload.Q(text));
            renamed.Add(new[]{id,text,before[id].Kind});
        }

        // A bar made again under a new id, where it was: every message end on it moves to the new
        // one, and the old one is deleted with whatever it was tied to.
        var remade=new List<string>();
        Func<string,string> remakeBar=old=>{
            string fresh=Guid.NewGuid().ToString();
            var g=bars[old];int count=newBarShapes.Count;
            addBar(fresh,Link(after[old],"participant").Single(),g[0],g[1],g[2]);
            Collection(view,"ExecutionSpecifications").Items.AddRange(newBarShapes.Skip(count));
            bars[fresh]=g;
            foreach(var r in relations.Where(r=>(V(r,"MetamodelId")==R("SendMessage") || V(r,"MetamodelId")==R("ReceiveMessage")) && V(r,"SourceId")==old && !leaving.Contains(V(r,"TargetId"))))
                if(!changedIds.Contains(V(r,"Id")))resend(r,fresh,null);
            foreach(var r in changed.Concat(newRelations).Where(r=>(V(r,"MetamodelId")==R("SendMessage") || V(r,"MetamodelId")==R("ReceiveMessage")) && V(r,"SourceId")==old))
                r.Properties["SourceId"]=SequenceJson.Parse(SequencePayload.Q(fresh));
            foreach(var w in wires)
            {
                if(w.SendPort==old)w.SendPort=fresh;
                if(w.ReceivePort==old)w.ReceivePort=fresh;
                w.RelationSources=w.RelationSources.Select(x=>x==old?fresh:x).ToArray();
            }
            leaving.Add(old);remade.Add(old);
            return fresh;
        };
        // ---- A bar is tied to the reply that closes it. Only bars this update touches are
        // checked, so a diagram drawn by hand keeps its own ties everywhere else.
        var replyLinks=typed("ExecutionSpecificationReplyMessage");
        if(replyLinks.Length>0 || (BaseTypes!=null && BaseTypes.Reply!=null))
        {
            var touchedMessages=new HashSet<string>(gate.AddMessages.Concat(gate.DeleteMessages).Concat(gate.ReconnectMessages).Concat(gate.ResendMessages)
                .Concat(gate.SortChanges).Concat(gate.MoveMessages).Concat(gate.ReorderMessages));
            var touched=new HashSet<string>(gate.AddExecutions.Concat(implicitPorts.Values));
            foreach(var m in current.Elements.Where(e=>e.Kind=="message" && touchedMessages.Contains(e.Id)))
                foreach(string role in new[]{"sendExecution","receiveExecution"})foreach(string b in Link(m,role))touched.Add(b);
            foreach(var m in plan.Expected.Elements.Where(e=>e.Kind=="message" && touchedMessages.Contains(e.Id)))
                foreach(string role in new[]{"sendExecution","receiveExecution"})foreach(string b in Link(m,role))touched.Add(b);
            foreach(string b0 in touched.Where(id=>after.ContainsKey(id) || implicitPorts.Values.Contains(id)).ToArray())
            {
                string b=b0;
                string closing=after.ContainsKey(b)?ClosingReply(plan.Expected,b):null;
                bool linked=replyLinks.Any(r=>V(r,"SourceId")==b && V(r,"TargetId")==closing);
                foreach(var link in replyLinks.Where(r=>V(r,"SourceId")==b).ToArray())
                {
                    if(V(link,"TargetId")==closing || leaving.Contains(V(link,"TargetId")))continue;
                    // The product will not untie it. It is pointed at the reply that now closes the
                    // bar; with none, the bar is made again and the old one goes, tie and all.
                    if(closing!=null && !linked){resend(link,null,closing);linked=true;continue;}
                    b=remakeBar(b);linked=false;break;
                }
                if(closing!=null && !linked)
                {
                    string relationId=Guid.NewGuid().ToString();
                    if(replyLinks.Length>0)
                    {
                        newRelations.Add(copyRelation(replyLinks[0],relationId,b,closing));
                        notes.Add(new SequenceAddedNote{Kind="link",ModelId=closing,RelationIds=new[]{relationId},RelationSources=new[]{b},RelationTargets=new[]{closing},RelationFields=new[]{"relation:"+V(replyLinks[0],"Id")}});
                    }
                    else
                    {
                        newRelations.Add(relate(BaseTypes.Reply,relationId,b,closing));
                        notes.Add(new SequenceAddedNote{Kind="link",ModelId=closing,RelationIds=new[]{relationId},RelationSources=new[]{b},RelationTargets=new[]{closing},RelationFields=new[]{BaseTypes.Reply[2]}});
                    }
                }
            }
        }

        // The product refuses to untie system-defined relations, which every sequence relation is.
        Require(unrelate.Count==0,"関連の解除が必要な変更です。製品がシーケンス図の関連の解除を受け付けません: "
            +string.Join(",",unrelate.Select(id=>V(relations.First(r=>V(r,"Id")==id),"MetamodelId").Replace(SequencePayload.Prefix,""))));
        if(newEndShapes.Count>0)Collection(view,"MessageEnds").Items.AddRange(newEndShapes);
        patch["Entities"].Items.AddRange(newEntities);
        patch["Relations"].Items.AddRange(newRelations);
        patch["Relations"].Items.AddRange(changed);
        // After the deletions the editor is imported again without their shapes, and without
        // connectors drawn to them, such as a note's anchor.
        var afterDelete=SequenceJson.Parse(patch.ToJsonString());
        afterDelete.Properties["Entities"]=SequenceJson.Parse("[]");afterDelete.Properties["Relations"]=SequenceJson.Parse("[]");
        {
            var cleaned=afterDelete["Editors"].Items.Single();
            var goneShapes=new HashSet<string>(editor.Shapes().Where(sh=>leaving.Contains(V(sh,"ModelId"))).Select(sh=>V(sh,"Id")));
            Func<SequenceJson,bool> dangling=node=>node.Properties!=null
                && node.Properties.Any(p=>p.Key!="Id" && p.Value!=null && p.Value.Raw!=null && p.Value.Raw.StartsWith("\"",StringComparison.Ordinal)
                    && goneShapes.Contains(p.Value.StringValue()));
            foreach(var property in cleaned.Properties)
            {
                var array=property.Value;
                if(array==null || array.Items==null)continue;
                for(int i=array.Items.Count-1;i>=0;i--)
                    if(leaving.Contains(V(array.Items[i],"ModelId")) || dangling(array.Items[i]))array.Items.RemoveAt(i);
            }
        }
        string inserted=gate.AddMessages.FirstOrDefault(id=>walkNew.Skip(System.Array.IndexOf(walkNew,id)+1).Any(before.ContainsKey))??"";
        return new SequenceStructurePreparation{ReconnectJson=patch.ToJsonString(),ReconnectCount=changed.Count,
            EditorAfterDeleteJson=afterDelete.ToJsonString(),
            DeleteIds=gate.DeleteExecutions.Concat(remade).ToArray(),
            AddedExecutions=additions.ToArray(),AddedParticipants=lanes.ToArray(),AddedMessages=wires.ToArray(),
            AddedFragments=frames.ToArray(),AddedOperands=branches.ToArray(),
            StretchedLifelines=new SequenceStretchedLifeline[0],CreatedCollections=Created.ToArray(),
            ShiftedShapes=shifted.ToArray(),InsertedMessageId=inserted,MovedMessages=moved.ToArray(),AddedNotes=notes.ToArray(),Renamed=renamed.ToArray(),
            DeleteParticipantIds=gate.DeleteParticipants.ToArray(),DeleteMessageIds=gate.DeleteMessages.ToArray(),
            DeleteFrameIds=gate.DeleteFragments.Concat(gate.DeleteOperands).ToArray(),DeleteNoteIds=gate.DeleteNotes.ToArray(),DeleteRefIds=gate.DeleteRefs.ToArray(),
            DeleteDestroyIds=gate.DeleteDestroys.ToArray(),DeleteEndIds=removedEnds.ToArray(),UnrelateIds=unrelate.Distinct().ToArray(),
            SortChanged=sortChanged.ToArray(),ShapeTexts=shapeTexts.ToArray(),LooseShapeIds=loose.ToArray(),
            ReceiveRelationIds=typed("ReceiveMessage").Select(r=>V(r,"Id")).ToArray(),SendRelationIds=typed("SendMessage").Select(r=>V(r,"Id")).ToArray()};
    }
    // A diagram that has never held a frame has no Fragments collection at all, so the
    // first one has to create it rather than append to something that is not there.
    // Records which collections had to be created, so a run says whether it took that
    // path at all.
    internal static readonly List<string> Created=new List<string>();
    static SequenceJson Collection(SequenceJson view,string name)
    {
        var array=view[name];
        if(array!=null && array.Items!=null)return array;
        if(!Created.Contains(name))Created.Add(name);
        Require(array==null,"エディタの"+name+"が配列ではありません。");
        array=SequenceJson.Parse("[]");
        if(view.Properties==null)view.Properties=new Dictionary<string,SequenceJson>(StringComparer.Ordinal);
        view.Properties[name]=array;
        return array;
    }
    static string Number(double value)
    { return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture); }
    static double Read(SequenceJson node,string key)
    {
        if(node==null || node[key]==null)throw new InvalidOperationException("S220: 図形に"+key+"がありません。");
        double value;
        if(!double.TryParse(node[key].Raw,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value))
            throw new InvalidOperationException("S220: 図形の"+key+"が数値ではありません。");
        return value;
    }
    static string[] Link(SequenceElement e,string role)
    { string[] ids;return e.Links.TryGetValue(role,out ids)?ids:new string[0]; }
    // The reply that closes a bar: the last message on it, in drawing order, when that is a
    // reply leaving the bar. Null when the bar ends on anything else.
    internal static string ClosingReply(SequenceDocument doc,string bar)
    {
        var last=SequenceStructurePreflight.Flatten(doc).Select(id=>doc.Elements.First(e=>e.Id==id))
            .LastOrDefault(e=>e.Kind=="message" && (Link(e,"sendExecution").Contains(bar) || Link(e,"receiveExecution").Contains(bar)));
        return last!=null && SequenceStructurePreflight.Attribute(last)=="reply" && Link(last,"sendExecution").Contains(bar)?last.Id:null;
    }
    // How tall the generator makes a note or ref for its text.
    internal static double BoxHeight(string text)
    { return Math.Max(48,16+20*(text??"").Replace("\r\n","\n").Split('\n').Length); }
    internal const double LaneSpacing=240;
    // The profile's literal for each message sort, set by the runtime before preparing.
    public static readonly Dictionary<string,string> SortLiterals=new Dictionary<string,string>(StringComparer.Ordinal);
    internal const double MessageSpacing=PumlBuild.MessagePitch;
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
    // RelationFields: the endpoint field pair. Index positions belong to one
    // source/field collection, so compaction has to be computed within it.
    public Dictionary<string,string> RelationFields=new Dictionary<string,string>();
    public Dictionary<string,string[]> Relations=new Dictionary<string,string[]>(), Ports=new Dictionary<string,string[]>();
    public string Field(string id)
    { string value;return RelationFields.TryGetValue(id,out value)?value:""; }
    public string RelationSignature(string id)
    { return PumlBuild.Json(Relations[id])+"|"+Field(id); }
    Dictionary<string,string> RelationRows()
    { return Relations.Keys.ToDictionary(id=>id,id=>RelationSignature(id)); }
    public string Signature()
    {
        return PumlBuild.Json(new object[]{Models.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key,p.Value}).ToArray(),
            Shapes.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key,p.Value}).ToArray(),
            ShapeModels.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key,p.Value}).ToArray(),
            Relations.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key,RelationSignature(p.Key)}).ToArray(),
            Ports.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new[]{p.Key}.Concat(p.Value).ToArray()).ToArray()});
    }
    static int Differences(Dictionary<string,string> a,Dictionary<string,string> b)
    {return a.Keys.Union(b.Keys).Count(k=>!a.ContainsKey(k) || !b.ContainsKey(k) || a[k]!=b[k]);}
    public string DifferenceCounts(SequenceTrialState actual)
    {
        return "モデル="+Differences(Models,actual.Models)+" 関連="+Differences(RelationRows(),actual.RelationRows())
            +" 図形="+Differences(Shapes,actual.Shapes)+" 図形所属="+Differences(ShapeModels,actual.ShapeModels)
            +" 送受信="+Differences(Ports.ToDictionary(p=>p.Key,p=>PumlBuild.Json(p.Value)),actual.Ports.ToDictionary(p=>p.Key,p=>PumlBuild.Json(p.Value)));
    }
    public string RelationDifferences(SequenceTrialState actual)
    {
        var lines=new List<string>();
        var fields=new[]{"SourceId","TargetId","SourceIndex","TargetIndex"};
        var changed=Relations.Keys.Union(actual.Relations.Keys).OrderBy(id=>id,StringComparer.Ordinal)
            .Where(id=>!Relations.ContainsKey(id) || !actual.Relations.ContainsKey(id) || RelationSignature(id)!=actual.RelationSignature(id)).ToArray();
        foreach(string id in changed.Take(12))
        {
            lines.Add("relation="+id);
            if(!Relations.ContainsKey(id)){lines.Add("unexpected actual="+PumlBuild.Json(actual.Relations[id]));continue;}
            if(!actual.Relations.ContainsKey(id)){lines.Add("missing actual; expected="+PumlBuild.Json(Relations[id]));continue;}
            for(int n=0;n<4;n++)if(Relations[id][n]!=actual.Relations[id][n])
                lines.Add(fields[n]+": expected="+Relations[id][n]+" actual="+actual.Relations[id][n]);
            if(Field(id)!=actual.Field(id))lines.Add("Field: expected="+Field(id)+" actual="+actual.Field(id));
            lines.Add("endpoints: "+actual.Relations[id][0]+" -> "+actual.Relations[id][1]);
        }
        if(changed.Length>12)lines.Add("additional changed relations="+(changed.Length-12));
        return string.Join("\n",lines);
    }
    // Deleting an execution removes one relation from each owner collection.
    // Whether the product compacts the surviving indices is unmeasured, so report the
    // order around the deletion instead of correcting it.
    public string[] DeletionOwners(SequenceStructurePreparation prepared)
    {
        var removed=new HashSet<string>(prepared.DeleteIds);
        return Relations.Values.Where(r=>removed.Contains(r[1]) && !removed.Contains(r[0])).Select(r=>r[0])
            .Distinct().OrderBy(id=>id,StringComparer.Ordinal).ToArray();
    }
    public string OrderReport(string[] owners,SequenceStructurePreparation prepared)
    {
        var removed=new HashSet<string>(prepared.DeleteIds);
        var lines=new List<string>();
        foreach(string owner in owners)
        {
            var rows=Relations.Where(p=>p.Value[0]==owner)
                .OrderBy(p=>int.Parse(p.Value[2],System.Globalization.CultureInfo.InvariantCulture))
                .ThenBy(p=>p.Key,StringComparer.Ordinal)
                .Select(p=>p.Value[2]+":"+p.Key+"/"+Field(p.Key)+(removed.Contains(p.Value[1])?"*":"")).ToArray();
            lines.Add("source="+owner+" "+(rows.Length==0?"(なし)":string.Join(" ",rows)));
        }
        return string.Join("\n",lines);
    }
    // A shape this run creates is predicted from the numbers we send, while the product
    // reports them back with the representation drift its export already shows on every
    // imported coordinate. Round both sides for those shapes only; existing shapes are
    // compared as the SDK reports them, unchanged.
    // A shape signature is one or more arrays followed by free text, and an operand puts
    // its position in the second one. Round every array, not only the first.
    static string RoundArrays(string value)
    {
        var output=new StringBuilder();
        int at=0;
        while(at<value.Length)
        {
            if(value[at]!='[') {output.Append(value[at]);at++;continue;}
            int start=at,depth=0;bool text=false;
            for(;at<value.Length;at++)
            {
                char c=value[at];
                if(text) { if(c=='\\')at++; else if(c=='"')text=false; continue; }
                if(c=='"') {text=true;continue;}
                if(c=='[') {depth++;continue;}
                if(c==']') {depth--;if(depth==0){at++;break;}}
            }
            string chunk=value.Substring(start,at-start);
            try
            {
                var node=SequenceJson.Parse(chunk);
                if(node==null || node.Items==null) {output.Append(chunk);continue;}
                var rows=new List<string>();
                foreach(var item in node.Items)
                {
                    string raw=item.StringValue();double number;
                    rows.Add(double.TryParse(raw,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out number)
                        ? Math.Round(number,3).ToString("R",System.Globalization.CultureInfo.InvariantCulture) : raw);
                }
                output.Append(PumlBuild.Json(rows.ToArray()));
            }
            catch(Exception) {output.Append(chunk);}
        }
        // A lifeline puts its timeline length after the arrays, bare, so round that too.
        string rounded=output.ToString();
        int last=rounded.LastIndexOf(']');
        string tail=last<0?rounded:rounded.Substring(last+1);
        double length;
        if(tail.Length>0 && double.TryParse(tail,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out length))
            rounded=rounded.Substring(0,last+1)+Math.Round(length,3).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        return rounded;
    }
    public void Round(IEnumerable<string> shapeIds)
    {
        foreach(string id in shapeIds)
        {
            string value;
            if(!Shapes.TryGetValue(id,out value))continue;
            Shapes[id]=RoundArrays(value);
        }
    }
    public string ShapeDifferences(SequenceTrialState actual)
    {
        var lines=new List<string>();
        var changed=Shapes.Keys.Union(actual.Shapes.Keys).OrderBy(id=>id,StringComparer.Ordinal)
            .Where(id=>!Shapes.ContainsKey(id) || !actual.Shapes.ContainsKey(id) || Shapes[id]!=actual.Shapes[id]
                || Model(id)!=actual.Model(id)).ToArray();
        foreach(string id in changed.Take(12))
        {
            lines.Add("shape="+id);
            if(!Shapes.ContainsKey(id)){lines.Add("unexpected actual="+actual.Shapes[id]+" model="+actual.Model(id));continue;}
            if(!actual.Shapes.ContainsKey(id)){lines.Add("missing actual; expected="+Shapes[id]+" model="+Model(id));continue;}
            if(Shapes[id]!=actual.Shapes[id])lines.Add("値: expected="+Shapes[id]+" actual="+actual.Shapes[id]);
            if(Model(id)!=actual.Model(id))lines.Add("所属: expected="+Model(id)+" actual="+actual.Model(id));
        }
        if(changed.Length>12)lines.Add("additional changed shapes="+(changed.Length-12));
        return string.Join("\n",lines);
    }
    public string Model(string shape)
    { string value;return ShapeModels.TryGetValue(shape,out value)?value:""; }
    // Where a value sits in a shape signature. A node shape starts with its rectangle;
    // a message follows with its text and both ends; a bar ends with its length.
    static int Slot(string key,int count)
    {
        if(key=="X")return 0;
        if(key=="Width")return 2;
        if(key=="SelfloopBendsX")return count-1;
        if(key=="Y")return 1;
        if(key=="Height")return 3;
        if(key=="Length")return count-1;
        if(key=="SourceY")return count-3;
        if(key=="TargetY")return count-2;
        return -1;
    }
    static double Coordinate(string value)
    { return double.Parse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture); }
    static string Coordinate(double value)
    { return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture); }
    // Takes the read-back values for shapes the product sizes itself, keeping the check that
    // they exist and belong to the model they were made for.
    public void Loosen(IEnumerable<string> shapeIds,SequenceTrialState actual)
    {
        foreach(string id in shapeIds)
        {
            string value;
            if(Shapes.ContainsKey(id) && actual.Shapes.TryGetValue(id,out value))Shapes[id]=value;
        }
    }
    static string Index(int value) { return value.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    static int Index(string value) { return int.Parse(value,System.Globalization.CultureInfo.InvariantCulture); }
    // A relation's field signature as recorded for the package: given directly, "row:" plus the
    // signature, or "relation:" plus the id of a relation already there whose field it shares.
    string FieldOf(string spec)
    {
        if(string.IsNullOrEmpty(spec))return "";
        if(spec.StartsWith("row:",StringComparison.Ordinal))return spec.Substring(4);
        if(spec.StartsWith("relation:",StringComparison.Ordinal))return Field(spec.Substring(9));
        return spec;
    }
    // A new relation goes last in its source's field collection, and last in the target's.
    void Append(string id,string source,string target,string field,bool reverse)
    {
        if(field.Length==0)throw new InvalidOperationException("S230: 追加する関連の種別情報が不足しています。");
        int index=Relations.Count(pair=>pair.Value[0]==source && Field(pair.Key)==field);
        int back=reverse?Relations.Count(pair=>pair.Value[1]==target && Field(pair.Key)==field):0;
        Relations[id]=new[]{source,target,Index(index),Index(back)};
        RelationFields[id]=field;
    }
    // Measured on the product: removing a relation closes the gap it leaves in the source's
    // collection for that field. Relations in other fields keep their index.
    // The target side closes up the same way, where it keeps an order at all: a reply tied
    // to a new bar before the old bar goes moves up when the old tie goes with it.
    void Remove(string id)
    {
        string source=Relations[id][0],target=Relations[id][1],field=Field(id);
        int gap=Index(Relations[id][2]);int back;
        bool ordered=int.TryParse(Relations[id][3],out back) && back>=0;
        Relations.Remove(id);RelationFields.Remove(id);
        foreach(string peer in Relations.Where(p=>p.Value[0]==source && Field(p.Key)==field).Select(p=>p.Key).ToArray())
        {
            int index=Index(Relations[peer][2]);
            if(index>gap)Relations[peer][2]=Index(index-1);
        }
        if(ordered)
            foreach(string peer in Relations.Where(p=>p.Value[1]==target && Field(p.Key)==field).Select(p=>p.Key).ToArray())
            {
                int index;
                if(int.TryParse(Relations[peer][3],out index) && index>back)Relations[peer][3]=Index(index-1);
            }
    }
    public SequenceTrialState Expected(SequenceStructurePreparation prepared,SyncPlan plan,bool delete)
    {
        var result=new SequenceTrialState{Models=new Dictionary<string,string>(Models),Shapes=new Dictionary<string,string>(Shapes),ShapeModels=new Dictionary<string,string>(ShapeModels),
            Relations=Relations.ToDictionary(p=>p.Key,p=>p.Value.ToArray()),Ports=Ports.ToDictionary(p=>p.Key,p=>p.Value.ToArray()),
            RelationFields=new Dictionary<string,string>(RelationFields)};
        foreach(var wire in prepared.AddedMessages)
        {
            result.Models[wire.ModelId]=PumlBuild.Json(new[]{wire.Metaclass,wire.Name,wire.OwnerId,"False"});
            for(int i=0;i<wire.RelationIds.Length;i++)
            {
                // A relation built rather than copied carries its own field signature.
                string field=i<wire.RelationFields.Length && wire.RelationFields[i].Length>0
                    ?result.FieldOf(wire.RelationFields[i]):result.FieldOf(wire.TemplateRelationIds[i].StartsWith("row:",StringComparison.Ordinal)?wire.TemplateRelationIds[i]:"relation:"+wire.TemplateRelationIds[i]);
                if(field.Length==0)throw new InvalidOperationException("S230: 追加するメッセージの関連の種別情報が不足しています。");
                string origin=wire.RelationSources[i];
                int index=result.Relations.Count(pair=>pair.Value[0]==origin && result.Field(pair.Key)==field);
                result.Relations[wire.RelationIds[i]]=new[]{origin,wire.ModelId,Index(index),"0"};
                result.RelationFields[wire.RelationIds[i]]=field;
            }
            string[] rows;
            string sample;
            if(!string.IsNullOrEmpty(wire.TemplateShapeId))
            {
                if(!result.Shapes.TryGetValue(wire.TemplateShapeId,out sample))throw new InvalidOperationException("S230: メッセージの見本図形がありません。");
                var measured=SequenceJson.Parse(sample);
                // A message signature ends with text, both ends and the selfloop offset.
                if(measured==null || measured.Items==null || measured.Items.Count<4)
                    throw new InvalidOperationException("S230: メッセージの図形の項目数が想定と違います。");
                rows=measured.Items.Select(item=>item.StringValue()).ToArray();
            }
            else rows=new[]{"","","","0"};
            rows[rows.Length-4]=wire.Name;rows[rows.Length-3]=wire.Y;rows[rows.Length-2]=wire.TargetY??wire.Y;
            if(wire.Bend!=null)rows[rows.Length-1]=wire.Bend;
            result.Shapes[wire.ShapeId]=PumlBuild.Json(rows);
            result.ShapeModels[wire.ShapeId]=wire.ModelId;
            string kind=wire.Sort;
            if(string.IsNullOrEmpty(kind))
            {
                string[] pattern;
                if(!result.Ports.TryGetValue(wire.TemplateModelId??"",out pattern))throw new InvalidOperationException("S230: メッセージの見本の送受信がありません。");
                kind=pattern[4];
            }
            result.Ports[wire.ModelId]=new[]{wire.SendPort,wire.ReceivePort,wire.Sender??"",wire.Receiver??"",kind};
        }
        // Shapes already drawn that move or grow. The signatures the SDK reads back put those
        // numbers in fixed places, so the moved values go back in the same places.
        foreach(var move in prepared.ShiftedShapes)
        {
            string measured;
            if(!result.Shapes.TryGetValue(move.ShapeId,out measured))
                throw new InvalidOperationException("S230: 下げる図形がありません。");
            // An operand reads back as an empty rectangle followed by [guard, offset].
            if(move.Keys.Length==1 && move.Keys[0]=="Position")
            {
                var branch=measured.StartsWith("[]",StringComparison.Ordinal)?SequenceJson.Parse(measured.Substring(2)):null;
                if(branch==null || branch.Items==null || branch.Items.Count!=2)
                    throw new InvalidOperationException("S230: 下げるオペランドの図形の形が想定と違います。");
                result.Shapes[move.ShapeId]="[]"+PumlBuild.Json(new[]{branch.Items[0].StringValue(),move.Values[0]});
                continue;
            }
            int close=measured.LastIndexOf(']');
            if(close<0)throw new InvalidOperationException("S230: 下げる図形の形が想定と違います。");
            string tail=measured.Substring(close+1);
            var node=SequenceJson.Parse(measured.Substring(0,close+1));
            if(node==null || node.Items==null)throw new InvalidOperationException("S230: 下げる図形を読み取れません。");
            var rows=node.Items.Select(item=>item.StringValue()).ToList();
            for(int i=0;i<move.Keys.Length;i++)
            {
                // A lane keeps its length after the arrays; everything else is positional.
                if(move.Keys[i]=="LaneLength") {tail=move.Values[i];continue;}
                int slot=Slot(move.Keys[i],rows.Count);
                if(slot<0 || slot>=rows.Count)
                    throw new InvalidOperationException("S230: 下げる図形に"+move.Keys[i]+"の位置がありません。");
                rows[slot]=move.Values[i];
            }
            result.Shapes[move.ShapeId]=PumlBuild.Json(rows.ToArray())+tail;
        }
        foreach(var lane in prepared.StretchedLifelines)
        {
            string measured;
            if(!result.Shapes.TryGetValue(lane.ShapeId,out measured))
                throw new InvalidOperationException("S230: 伸ばす参加者の図形がありません。");
            int close=measured.LastIndexOf(']');
            if(close<0)throw new InvalidOperationException("S230: 参加者の図形の形が想定と違います。");
            result.Shapes[lane.ShapeId]=measured.Substring(0,close+1)+lane.Length;
        }
        // A frame carries its text after the rectangle, and an operand its guard and
        // position, matching how the SDK side reads both back.
        foreach(var frame in prepared.AddedFragments)
        {
            result.Models[frame.ModelId]=PumlBuild.Json(new[]{frame.Metaclass,frame.Name,frame.OwnerId,"False"});
            for(int i=0;i<frame.RelationIds.Length;i++)
                result.Append(frame.RelationIds[i],frame.RelationSources[i],frame.RelationTargets[i],result.FieldOf(frame.RelationFields[i]),true);
            var box=SequenceJson.Parse(frame.Geometry);
            if(box==null || box.Items==null || box.Items.Count!=4)
                throw new InvalidOperationException("S230: フラグメントの図形の項目数が想定と違います。");
            result.Shapes[frame.ShapeId]=frame.Geometry+frame.Text;
            result.ShapeModels[frame.ShapeId]=frame.ModelId;
        }
        foreach(var branch in prepared.AddedOperands)
        {
            result.Models[branch.ModelId]=PumlBuild.Json(new[]{branch.Metaclass,branch.Name,branch.OwnerId,"False"});
            result.Append(branch.RelationIds[0],branch.OwnerId,branch.ModelId,result.FieldOf(branch.RelationFields[0]),true);
            // An operand has no rectangle of its own: the product reads back an empty
            // geometry and keeps only the guard and the offset from the frame's top.
            result.Shapes[branch.ShapeId]=PumlBuild.Json(new string[0])+PumlBuild.Json(new[]{branch.Guard,branch.Position});
            result.ShapeModels[branch.ShapeId]=branch.ModelId;
        }
        // Renamed elements: the model's name, and the text the shape reads back.
        foreach(var row in prepared.Renamed)
        {
            string id=row[0],text=row[1],kind=row[2];
            string model;
            if(kind!="operand" && result.Models.TryGetValue(id,out model))
            {
                var cells=SequenceJson.Parse(model);
                if(cells!=null && cells.Items!=null && cells.Items.Count==4)
                {
                    var values=cells.Items.Select(c=>c.StringValue()).ToArray();values[1]=text;
                    result.Models[id]=PumlBuild.Json(values);
                }
            }
            foreach(string shapeId in result.ShapeModels.Where(p=>p.Value==id).Select(p=>p.Key).ToArray())
            {
                string measured=result.Shapes[shapeId];
                if(kind=="message")
                {
                    var rows=SequenceJson.Parse(measured).Items.Select(c=>c.StringValue()).ToArray();
                    rows[rows.Length-4]=text;result.Shapes[shapeId]=PumlBuild.Json(rows);
                }
                else if(kind=="operand" && measured.StartsWith("[]",StringComparison.Ordinal))
                {
                    var rows=SequenceJson.Parse(measured.Substring(2)).Items.Select(c=>c.StringValue()).ToArray();
                    rows[0]=text;result.Shapes[shapeId]="[]"+PumlBuild.Json(rows);
                }
                else if(kind=="note" || kind=="ref" || kind=="fragment")
                {
                    int close=measured.IndexOf(']');
                    result.Shapes[shapeId]=measured.Substring(0,close+1)+text;
                }
            }
        }
        foreach(var row in prepared.ShapeTexts)
        {
            string measured;
            if(!result.Shapes.TryGetValue(row[0],out measured))throw new InvalidOperationException("S230: 文字を変える図形がありません。");
            int close=measured.IndexOf(']');
            result.Shapes[row[0]]=measured.Substring(0,close+1)+row[1];
        }
        foreach(var note in prepared.AddedNotes)
        {
            // A link only adds relations between elements that are already there.
            if(note.Kind!="link")result.Models[note.ModelId]=PumlBuild.Json(new[]{note.Metaclass,note.Name,note.OwnerId,"False"});
            for(int i=0;i<note.RelationIds.Length;i++)
                result.Append(note.RelationIds[i],note.RelationSources[i],note.RelationTargets[i],result.FieldOf(note.RelationFields[i]),true);
            if(note.Kind=="link")continue;
            // A note or ref reads back as its rectangle followed by its text.
            result.Shapes[note.ShapeId]=note.Geometry+note.Text;
            result.ShapeModels[note.ShapeId]=note.ModelId;
        }
        // A message moved into a branch gains only the branch's reference, appended in order.
        foreach(var move in prepared.MovedMessages)result.Append(move.RelationId,move.OperandId,move.ModelId,result.FieldOf(move.Field),true);
        foreach(var lane in prepared.AddedParticipants)
        {
            result.Models[lane.ModelId]=PumlBuild.Json(new[]{lane.Metaclass,lane.Name,lane.OwnerId,"False"});
            string field=lane.TemplateRelationId.StartsWith("row:",StringComparison.Ordinal)?lane.TemplateRelationId.Substring(4):result.Field(lane.TemplateRelationId);
            if(field.Length==0)throw new InvalidOperationException("S230: 追加する参加者の関連の種別情報が不足しています。");
            int index=result.Relations.Count(pair=>pair.Value[0]==lane.OwnerId && result.Field(pair.Key)==field);
            result.Relations[lane.RelationId]=new[]{lane.OwnerId,lane.ModelId,Index(index),"0"};
            result.RelationFields[lane.RelationId]=field;
            string sample;
            if(string.IsNullOrEmpty(lane.TemplateShapeId))
            {
                // The product decides the box; LooseShapeIds takes what it reads back.
                result.Shapes[lane.ShapeId]=PumlBuild.Json(new[]{lane.X,"","",""});result.ShapeModels[lane.ShapeId]=lane.ModelId;
                continue;
            }
            if(!result.Shapes.TryGetValue(lane.TemplateShapeId,out sample))throw new InvalidOperationException("S230: 参加者の見本図形がありません。");
            int close=sample.LastIndexOf(']');
            SequenceJson measured=close<0?null:SequenceJson.Parse(sample.Substring(0,close+1));
            if(measured==null || measured.Items==null || measured.Items.Count!=4)
                throw new InvalidOperationException("S230: 参加者の図形の項目数が想定と違います。");
            // Only X is ours; the vertical box and the timeline length come from the product.
            result.Shapes[lane.ShapeId]=PumlBuild.Json(new[]{lane.X,measured.Items[1].StringValue(),
                measured.Items[2].StringValue(),measured.Items[3].StringValue()})+sample.Substring(close+1);
            result.ShapeModels[lane.ShapeId]=lane.ModelId;
        }
        foreach(var add in prepared.AddedExecutions)
        {
            result.Models[add.ModelId]=PumlBuild.Json(new[]{add.Metaclass,add.Name,add.OwnerId,"False"});
            for(int i=0;i<add.RelationIds.Length;i++)
            {
                string template=add.TemplateRelationIds[i];
                string field=template.StartsWith("row:",StringComparison.Ordinal)?template.Substring(4):result.Field(template),origin=add.RelationSources[i];
                if(field.Length==0)throw new InvalidOperationException("S230: 追加する関連の種別情報が不足しています。");
                int index=result.Relations.Count(pair=>pair.Value[0]==origin && result.Field(pair.Key)==field);
                result.Relations[add.RelationIds[i]]=new[]{origin,add.ModelId,Index(index),"0"};
                result.RelationFields[add.RelationIds[i]]=field;
            }
            var wanted=SequenceJson.Parse(add.Geometry);
            string width="16";
            if(!string.IsNullOrEmpty(add.TemplateShapeId))
            {
                string sample;
                if(!result.Shapes.TryGetValue(add.TemplateShapeId,out sample))throw new InvalidOperationException("S230: 実行区間の見本図形がありません。");
                var measured=SequenceJson.Parse(sample);
                if(measured.Items==null || measured.Items.Count!=5)throw new InvalidOperationException("S230: 実行区間の図形の項目数が想定と違います。");
                // Width is not serialized for a bar, so take the one the product already uses.
                width=measured.Items[2].StringValue();
            }
            if(wanted.Items==null || wanted.Items.Count!=3)throw new InvalidOperationException("S230: 実行区間の図形の項目数が想定と違います。");
            result.Shapes[add.ShapeId]=PumlBuild.Json(new[]{wanted.Items[0].StringValue(),wanted.Items[1].StringValue(),
                width,wanted.Items[2].StringValue(),wanted.Items[2].StringValue()});
            result.ShapeModels[add.ShapeId]=add.ModelId;
        }
        // Relations already there that change an end: they leave one collection and join another.
        var patch=SequenceJson.Parse(prepared.ReconnectJson);
        var built=new HashSet<string>(prepared.AddedExecutions.SelectMany(a=>a.RelationIds).Concat(prepared.AddedParticipants.Select(a=>a.RelationId))
            .Concat(prepared.AddedMessages.SelectMany(a=>a.RelationIds)).Concat(prepared.AddedFragments.SelectMany(a=>a.RelationIds))
            .Concat(prepared.AddedOperands.SelectMany(a=>a.RelationIds)).Concat(prepared.MovedMessages.Select(a=>a.RelationId))
            .Concat(prepared.AddedNotes.SelectMany(a=>a.RelationIds)));
        foreach(var r in patch["Relations"].Items)
        {
            string id=r["Id"].StringValue(),source=r["SourceId"].StringValue(),target=r["TargetId"].StringValue();
            if(built.Contains(id))continue;
            if(!result.Relations.ContainsKey(id))throw new InvalidOperationException("S230: 変更する関連が図にありません。");
            var previous=result.Relations[id];string field=result.Field(id);
            bool receive=prepared.ReceiveRelationIds.Contains(id),send=prepared.SendRelationIds.Contains(id);
            if((receive || send) && !result.Ports.ContainsKey(target))throw new InvalidOperationException("S230: 変更前の送受信関連が一致しません。");
            // Peers share the source and the field, relations this package adds included. Only
            // when no field is known are the receive or send relations taken as one collection.
            Func<string,string[]> peers=origin=>result.Relations.Where(p=>p.Key!=id && p.Value[0]==origin
                && (field.Length>0?result.Field(p.Key)==field:receive?prepared.ReceiveRelationIds.Contains(p.Key):send && prepared.SendRelationIds.Contains(p.Key))).Select(p=>p.Key).ToArray();
            string sourceIndex=previous[2];
            if(previous[0]!=source)
            {
                int oldIndex=Index(previous[2]);
                foreach(string peer in peers(previous[0])){int index=Index(result.Relations[peer][2]);if(index>oldIndex)result.Relations[peer][2]=Index(index-1);}
                var newPeers=peers(source);
                int insertion=r["SourceIndex"]==null?newPeers.Length:Index(r["SourceIndex"].Raw);
                if(insertion<0 || insertion>newPeers.Length)throw new InvalidOperationException("S230: 受信関連の挿入順序が範囲外です。");
                foreach(string peer in newPeers){int index=Index(result.Relations[peer][2]);if(index>=insertion)result.Relations[peer][2]=Index(index+1);}
                sourceIndex=Index(insertion);
            }
            string targetIndex=previous[3];
            if(previous[1]!=target)targetIndex=Index(result.Relations.Count(p=>p.Key!=id && p.Value[1]==target && result.Field(p.Key)==field));
            result.Relations[id]=new[]{source,target,sourceIndex,targetIndex};
            var wanted=plan.Expected.Elements.FirstOrDefault(e=>e.Id==target);
            if(receive){result.Ports[target][1]=source;result.Ports[target][3]=wanted==null || !wanted.Links.ContainsKey("receiver")?"":wanted.Links["receiver"].FirstOrDefault()??"";}
            if(send){result.Ports[target][0]=source;result.Ports[target][2]=wanted==null || !wanted.Links.ContainsKey("sender")?"":wanted.Links["sender"].FirstOrDefault()??"";}
        }
        foreach(string id in prepared.UnrelateIds)
        {
            if(!result.Relations.ContainsKey(id))throw new InvalidOperationException("S230: 外す関連が図にありません。");
            result.Remove(id);
        }
        foreach(var row in prepared.SortChanged)
            if(result.Ports.ContainsKey(row[0]))result.Ports[row[0]][4]=row[1];
        if(delete)
        {
            var removed=new HashSet<string>(prepared.DeleteIds.Concat(prepared.DeleteParticipantIds)
                .Concat(prepared.DeleteMessageIds).Concat(prepared.DeleteFrameIds).Concat(prepared.DeleteNoteIds).Concat(prepared.DeleteRefIds)
                .Concat(prepared.DeleteDestroyIds).Concat(prepared.DeleteEndIds));
            foreach(string id in removed)result.Models.Remove(id);
            foreach(string id in prepared.DeleteMessageIds)result.Ports.Remove(id);
            foreach(string id in result.Relations.Where(p=>removed.Contains(p.Value[0]) || removed.Contains(p.Value[1])).Select(p=>p.Key).ToArray())
                if(result.Relations.ContainsKey(id))result.Remove(id);
            foreach(string id in result.ShapeModels.Where(p=>removed.Contains(p.Value)).Select(p=>p.Key).ToArray()){result.Shapes.Remove(id);result.ShapeModels.Remove(id);}
            if(result.Ports.Values.Any(p=>removed.Contains(p[0]) || removed.Contains(p[1])))throw new InvalidOperationException("S230: 削除区間への接続が残っています。");
        }
        return result;
    }
}


// Where everything goes after an update, worked out in one pass down the diagram and one
// across it. What is drawn keeps its place as long as the elements around it stay in the same
// order; a new element takes the generator's step below the one before it and pushes what
// follows down only as far as it has to; where something was removed, what follows closes up
// to the generator's step. Bars are fitted afterwards between the neighbours the input names.
public sealed class SequenceRelayout
{
    public sealed class Token
    {
        public string Key, Id, Kind, Container;
        public bool First;
        public int Lines=1;
        public double Old=double.NaN, Drop, Height, Y, After;
        public bool Stable;
    }
    public const double Pitch=PumlBuild.MessagePitch;
    public List<Token> Tokens=new List<Token>();
    public Dictionary<string,Token> ByKey=new Dictionary<string,Token>(StringComparer.Ordinal);
    public double Start;
    // Old anchor and new anchor of every token that kept its place in the order.
    public List<double[]> Pairs=new List<double[]>();
    public static int Lines(string text) { return (text??"").Replace("\r\n","\n").Split('\n').Length; }
    static string[] Link(SequenceElement e,string role)
    { string[] ids;return e.Links.TryGetValue(role,out ids)?ids:new string[0]; }
    // The drawing order of a document as tokens: a frame opens, each branch opens, and the
    // frame closes, around what they hold.
    public static List<Token> Walk(SequenceDocument doc)
    {
        var list=new List<Token>();
        var children=doc.Elements.Where(n=>n.Kind!="participant" && n.Kind!="execution" && n.Parent!=null)
            .GroupBy(n=>n.Parent).ToDictionary(g=>g.Key,g=>g.OrderBy(n=>n.Order).ToArray());
        Action<string> walk=null;
        walk=parent=>{
            SequenceElement[] items;if(!children.TryGetValue(parent,out items))return;
            foreach(var e in items)
            {
                if(e.Kind=="message")list.Add(new Token{Key="M:"+e.Id,Id=e.Id,Kind="M",Container=parent,Lines=Lines(e.Text)});
                else if(e.Kind=="note" || e.Kind=="ref")list.Add(new Token{Key="N:"+e.Id,Id=e.Id,Kind="N",Container=parent});
                else if(e.Kind=="destroy")list.Add(new Token{Key="D:"+e.Id,Id=e.Id,Kind="D",Container=parent});
                else if(e.Kind=="fragment")
                {
                    list.Add(new Token{Key="FO:"+e.Id,Id=e.Id,Kind="FO",Container=parent});
                    SequenceElement[] branches;
                    bool first=true;
                    if(children.TryGetValue(e.Id,out branches))
                        foreach(var o in branches.Where(o=>o.Kind=="operand"))
                        {
                            list.Add(new Token{Key="OO:"+o.Id,Id=o.Id,Kind="OO",Container=o.Id,First=first,Lines=Lines(o.Text)});first=false;
                            walk(o.Id);
                        }
                    list.Add(new Token{Key="FC:"+e.Id,Id=e.Id,Kind="FC",Container=parent});
                }
            }
        };
        walk(doc.Elements.Single(e=>e.Kind=="interaction").Id);
        return list;
    }
    // Where the generator puts a token with the row cursor at a given place, and where it
    // leaves the cursor after it.
    public static double Anchor(Token t,double cursor)
    {
        switch(t.Kind)
        {
            case "M":return cursor+18*(t.Lines-1);
            case "D":return cursor-15;
            case "OO":return cursor+(t.First?30:20);
            case "FC":return cursor+8;
            default:return cursor;
        }
    }
    public static double Next(Token t,double y)
    {
        switch(t.Kind)
        {
            case "M":return y+t.Drop+Pitch;
            case "N":return y+t.Height+Pitch;
            case "D":return y+35;
            case "OO":return y+40+18*(t.Lines-1);
            case "FC":return y+16;
            default:return y;
        }
    }
    // The row cursor a token was placed from: its anchor less the generator's step to it.
    static double Cursor(Token t)
    {
        switch(t.Kind)
        {
            case "M":return t.Old-18*(t.Lines-1);
            case "D":return t.Old+15;
            case "OO":return t.Old-(t.First?30:20);
            case "FC":return t.Old-8;
            default:return t.Old;
        }
    }
    // oldTokens carry their old anchors; newTokens carry the drop and height they will have.
    public static SequenceRelayout Place(List<Token> oldTokens,List<Token> newTokens)
    {
        var result=new SequenceRelayout{Tokens=newTokens};
        var a=oldTokens.Select(t=>t.Key).ToArray();var b=newTokens.Select(t=>t.Key).ToArray();
        var length=new int[a.Length+1,b.Length+1];
        for(int i=a.Length-1;i>=0;i--)for(int j=b.Length-1;j>=0;j--)
            length[i,j]=a[i]==b[j]?1+length[i+1,j+1]:Math.Max(length[i+1,j],length[i,j+1]);
        var match=new int[b.Length];for(int j=0;j<b.Length;j++)match[j]=-1;
        {
            int x=0,y=0;
            while(x<a.Length && y<b.Length)
            {
                if(a[x]==b[y]){match[y]=x;x++;y++;}
                else if(length[x+1,y]>=length[x,y+1])x++;else y++;
            }
        }
        double cursor=oldTokens.Count>0?Cursor(oldTokens[0]):40;
        result.Start=cursor;
        // An element that keeps its place in the order keeps its distance from the one that
        // kept its place before it, and goes down by what was put in between. Where something
        // between them went away, it takes the place of the first thing that went, but never
        // further down than the generator's step.
        // Distances are measured between row cursors, so a guard or a frame's bottom that takes
        // another kind of element's place keeps the generator's step to its own row.
        int previous=-1;double previousCursor=0,since=cursor,lastY=double.MinValue;
        for(int j=0;j<newTokens.Count;j++)
        {
            var t=newTokens[j];
            double gen=Anchor(t,cursor),at;
            if(match[j]>=0)
            {
                var old=oldTokens[match[j]];
                double first=Cursor(oldTokens[previous+1]);
                double place=previous<0?first:previousCursor+(first-Cursor(oldTokens[previous]));
                double inserted=cursor-since;
                double from=match[j]==previous+1?place+inserted:Math.Min(place+inserted,cursor);
                at=Anchor(t,from);
                if(at<lastY){at=Math.Max(gen,lastY);from=cursor;}
                previous=match[j];previousCursor=from;t.Old=old.Old;t.Stable=true;
                result.Pairs.Add(new[]{old.Old,at});
            }
            else at=gen;
            t.Y=at;cursor=Next(t,at);t.After=cursor;lastY=at;
            if(t.Stable)since=cursor;
            result.ByKey[t.Key]=t;
        }
        return result;
    }
    // Where an old position goes: with the last token at or above it that kept its place.
    public double Map(double p)
    {
        double[] last=null;
        foreach(var pair in Pairs){if(pair[0]<=p+1e-9)last=pair;else break;}
        if(last==null)return Pairs.Count>0?p+(Pairs[0][1]-Pairs[0][0]):p;
        return p+(last[1]-last[0]);
    }
    public Token Get(string key) { Token t;return ByKey.TryGetValue(key,out t)?t:null; }
    public int IndexOf(string key) { var t=Get(key);return t==null?-1:Tokens.IndexOf(t); }
}

// Where each lane goes across, by the same rule as the rows: lanes keep their place while
// their order holds, a new lane takes one lane spacing past the one before it, and lanes
// close up where one was removed.
public sealed class SequenceLaneLayout
{
    public Dictionary<string,double> X=new Dictionary<string,double>(StringComparer.Ordinal);
    public List<double[]> Pairs=new List<double[]>();
    public static SequenceLaneLayout Place(string[] oldOrder,Dictionary<string,double> oldX,string[] newOrder,double spacing,double start)
    {
        var result=new SequenceLaneLayout();
        var stable=SequenceStructurePreflight.Stable(oldOrder,newOrder);
        var oldIndex=oldOrder.Select((id,i)=>new{id,i}).ToDictionary(p=>p.id,p=>p.i);
        int previous=-1;double previousX=0;double? last=null;double inserted=0;
        foreach(string id in newOrder)
        {
            double gen=last.HasValue?last.Value+spacing:(oldOrder.Length>0?oldX[oldOrder[0]]:start);
            double at;
            if(stable.Contains(id))
            {
                int k=oldIndex[id];
                double first=oldX[oldOrder[previous+1]];
                double place=previous<0?first:previousX+(first-oldX[oldOrder[previous]]);
                at=k==previous+1?place+inserted:Math.Min(place+inserted,gen);
                if(last.HasValue && at<last.Value)at=Math.Max(gen,last.Value);
                previous=k;previousX=at;inserted=0;result.Pairs.Add(new[]{oldX[id],at});
            }
            else {at=gen;inserted+=spacing;}
            result.X[id]=at;last=at;
        }
        return result;
    }
    public double Map(double p)
    {
        double[] last=null;
        foreach(var pair in Pairs){if(pair[0]<=p+1e-9)last=pair;else break;}
        if(last==null)return Pairs.Count>0?p+(Pairs[0][1]-Pairs[0][0]):p;
        return p+(last[1]-last[0]);
    }
}

// Metaclasses and relation rows for building elements when the diagram holds nothing of the
// kind to copy. Each row is {relation metaclass, "Embed" or "Ref", field signature}. Resolved
// by the runtime (PumlRuntime.SyncBaseTypes); any of them may be missing.
public sealed class SequenceBaseTypes
{
    public string Message, Execution, Lifeline, MessageEnd;
    public string[] OwnsMessage, OwnsExecution, OwnsLifeline, OwnsMessageEnd, LaneExecution,
        SendFromBar, ReceiveFromBar, SendFromEnd, ReceiveFromEnd, Reply, Nested;
}
// END GENERATED SequenceSync.cs
