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
            + (StyleDirectives>0 ? "\n表示設定: "+StyleDirectives+"件はNext Designの既定表示を使用します。" : "");
    }
    // Lanes declared with "create participant": the export writes them where they are created,
    // so their place in the declarations says nothing about their place across the diagram.
    public HashSet<string> Created = new HashSet<string>();
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
            // A participant declared mid-diagram ("create participant ...", as the export writes a lane
            // created by a message) is read as declared there; any of PlantUML's participant kinds.
            var m = Regex.Match(s, "^(?:create\\s+)?(?:participant|actor|boundary|control|entity|database|collections|queue)\\s+(?:\"([^\"]+)\"\\s+as\\s+([\\p{L}\\p{N}_]+)|([\\p{L}\\p{N}_]+))$");
            if (m.Success) { string alias = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value; p.Participant(alias,m.Groups[1].Success?m.Groups[1].Value:alias,line,true); if(s.StartsWith("create")) p.Created.Add(alias); continue; }
            if (s.StartsWith("title ")) { p.Title = s.Substring(6); continue; }
            m = Regex.Match(s, @"^(alt|opt|loop|par|break|critical|group)\b\s*(.*)$");
            if (m.Success)
            {
                if (fragments.Count >= 32) throw Error(line, "入れ子は32段までです。");
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
    public string NoteField = "Body", NoteStorage = "String", Sync="Sync", Async="Async", Reply="Reply", Destroy=null, Create=null;
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
    public HashSet<string> unopened=new HashSet<string>();
    // Lanes declared with "create participant" that no message has touched yet.
    public HashSet<string> creating=new HashSet<string>();
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
    private string pendingSendAlias, pendingSendExecution; private bool pendingReply;
    // The bar that made the call each bar received, for the reply to go back to.
    private Dictionary<string,string> callerBar=new Dictionary<string,string>();
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
                string id=pendingAlias==n.Left?pendingExecution:pendingSendAlias==n.Left?pendingSendExecution:Execution(n.Left,y-20);
                if(pendingSendAlias==n.Left)pendingSendAlias=null;
                // A bar that opens with a send has its activate right before that send (see SequenceDocument.Parse).
                if(!(index+1<items.Count && (items[index+1].Kind=="sync" || items[index+1].Kind=="async" || items[index+1].Kind=="reply") && items[index+1].Left==n.Left))unopened.Add(id);
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
                // Another lane's deactivate between a message and its receiver's activate keeps the link.
                if(pendingAlias==n.Left || pendingReply)pendingAlias=null; continue;
            }
            // A note or ref between a message and its receiver's activate keeps the receive pending.
            if(n.Kind!="note" && n.Kind!="ref")pendingAlias=null;
            if(n.Kind=="sync" || n.Kind=="async" || n.Kind=="reply")
            {
                y+=18*(n.Text.Split('\n').Length-1);
                bool incoming=n.Left=="[";
                string claimed=null,ask;
                if(n.Kind=="reply" && !incoming && justEnded.TryGetValue(n.Left,out claimed) && !(caller.TryGetValue(claimed,out ask) && ask==n.Right))claimed=null;
                // Not when a bar the receiver called is still open on this lane (see SequenceDocument.Parse).
                if(claimed!=null)
                {
                    var open=new List<string>();string top0;
                    if(active.TryGetValue(n.Left,out top0) && top0!=null)open.Add(top0);
                    if(activities.ContainsKey(n.Left))open.AddRange(activities[n.Left].Where(x=>x!=null));
                    string who0;
                    if(open.Any(x=>!closed.Contains(x) && caller.TryGetValue(x,out who0) && who0==n.Right))claimed=null;
                }
                // A reply answers the bar its receiver called, below a bar another lane called on
                // top of it, which ends there (see SequenceDocument.Parse).
                string top,who;
                if(claimed==null && n.Kind=="reply" && !incoming && n.Right!="]" && active.TryGetValue(n.Left,out top) && top!=null && !closed.Contains(top)
                    && caller.TryGetValue(top,out ask) && ask!=n.Right && activities.ContainsKey(n.Left))
                {
                    var below=activities[n.Left].ToArray();
                    int k=Array.FindIndex(below,bar=>bar!=null && !closed.Contains(bar) && caller.TryGetValue(bar,out who) && who==n.Right);
                    if(k>=0)
                    {
                        closed.Add(top);
                        for(int i=0;i<k;i++)if(below[i]!=null)closed.Add(below[i]);
                        claimed=below[k];
                    }
                }
                string send; bool freshSend=false;
                if(incoming)send=null; else if(claimed!=null)send=claimed; else if(!active.TryGetValue(n.Left,out send) || closed.Contains(send)){freshSend=!active.ContainsKey(n.Left) || send==null;active[n.Left]=send=Execution(n.Left,y-20);}
                bool outgoing=n.Right=="]"; bool self=n.Left==n.Right; int targetY=y+(self?24:0);
                string receive;
                int nextAt=index+1;while(nextAt<items.Count && (items[nextAt].Kind=="note" || items[nextAt].Kind=="ref"))nextAt++;
                bool beginsActivation=nextAt<items.Count && items[nextAt].Kind=="activate" && items[nextAt].Left==n.Right;
                if(outgoing)
                {
                    receive=Entity("MessageEnd",""); Owned("MessageEnds",receive);
                    int endX=(int)executions[send]["X"]-60;
                    Shape("MessageEnds",receive,"X",endX,"Y",targetY,"Width",10,"Height",10);
                    payload.Expected.Add(new PumlExpected{Id=receive,Kind="messageEnd",Y=targetY,X=endX});
                }
                // A reply goes back to the bar that made the call it answers (see SequenceDocument.Parse).
                else if(n.Kind=="reply" && send!=null && callerBar.TryGetValue(send,out receive) && executionAliases.ContainsKey(receive) && executionAliases[receive]==n.Right) { }
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
                if(n.Kind=="sync" && !outgoing && !incoming && send!=null && !callerBar.ContainsKey(receive))callerBar[receive]=send;
                pendingAlias=outgoing?null:n.Right; pendingExecution=receive; pendingReply=n.Kind=="reply";
                // A lane with no bar open that sends and is then activated: that bar sent it (see
                // SequenceDocument.Parse); the activate takes the bar made for the send.
                pendingSendAlias=freshSend && !self && !incoming?n.Left:null; pendingSendExecution=send;
                if(!incoming)Extend(send,y); if(!outgoing)Extend(receive,targetY);
                foreach(var pair in activities)if(pair.Value.Count>0 && active.ContainsKey(pair.Key))Extend(active[pair.Key],targetY);
                bool creates=n.Kind=="sync" && creating.Contains(n.Right) && !creating.Contains(n.Left) && profile.Create!=null;
                creating.Remove(n.Left);creating.Remove(n.Right);
                string id=Entity("Message",n.Text,Obj("Name",n.Text,"MessageSort",n.Kind=="reply"?profile.Reply
                    :creates?profile.Create
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
        b.creating.UnionWith(plan.Created);
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
                // A bar that goes on to send is still the late one when it received nothing and
                // did not open with a send (see SequenceDocument.Parse).
                if(b.sent.Any(m=>m[2]==bar))continue;
                bool sends=b.sent.Any(m=>m[1]==bar);
                if(sends && !b.unopened.Contains(bar))continue;
                int barTop=(int)pair.Value["Y"];
                var last=b.sent.LastOrDefault(m=>wires.ContainsKey(m[0]) && (int)wires[m[0]]["SourceY"]<barTop && (laneOf(m[1])==lane || laneOf(m[2])==lane));
                if(last==null || last[3]=="reply" || laneOf(last[1])!=lane)continue;
                if(laneOf(last[2])!=lane)
                {
                    if(sends)continue;
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
                    string barLane=b.executionAliases[bar];
                    if(destroy!=null && b.sent.Any(m=>wire.ContainsKey(m[0]) && (int)wire[m[0]]["SourceY"]>point && (int)wire[m[0]]["SourceY"]<destroy.Y
                        && ((m[1]!=null && b.executionAliases.ContainsKey(m[1]) && b.executionAliases[m[1]]==barLane) || (m[2]!=null && b.executionAliases.ContainsKey(m[2]) && b.executionAliases[m[2]]==barLane))))destroy=null;
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
