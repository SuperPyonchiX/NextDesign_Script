public static class SequenceExperiment
{
    public const string Title = "シーケンス生成実験 / 0.12.1";
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
        // A lane declared with "create participant" is created by its first message (see SequenceDocument.Parse).
        if(plan.Created.Count>0)try{p.Create=Literal(source[6],"MessageSort","Create");}catch(InvalidOperationException){p.Create=null;}
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
