// ============================================================
//  Part 9 / PlantUML から新しいクラス図を作る
//
//    開いているクラス図を雛形にする。雛形の図と同じ所有先・ビュー定義で図のモデルを
//    作り、PlantUML のうち雛形の図にあるクラスはそのまま載せ、無いクラスは種類ごとに
//    1 つだけ雛形のクラスと同じ所有先・メタクラスで作る（種）。いったん保存してから、
//    残りのクラス・メンバ・関連を通常の「PlantUMLを反映」と同じ本体で足す。
//    計画（種と所有先の決め方）は純粋部 ClassDiagramDraft にある。
// ============================================================

public static class ClassDiagramCreator
{
    // Ribbon entry: pick the file, create the diagram, show the result. With a class diagram
    // open, the new one goes next to it; with any other model open or selected (a class diagram
    // group), it goes under that model, taking the first class diagram found below it (or
    // anywhere in the project) as the template.
    public static void Create(IApplication app)
    {
        var editor=app.Workspace.CurrentEditor;
        IEditor template;IModel owner;IField ownerField;string where;
        try { where=ResolvePlace(app,editor,out template,out owner,out ownerField); }
        catch(Exception ex) { ClassExperiment.Summary=ex.Message;ClassExperiment.Details=ex.ToString();ClassExperiment.Show(app);return; }
        string path=app.Window.UI.ShowOpenFileDialog("新しいクラス図にするPlantUML","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
        if(string.IsNullOrEmpty(path))return;
        string pumlText;
        try { if(new FileInfo(path).Length>300000)throw new InvalidOperationException("C120: 入力は300KB以下にしてください。");pumlText=File.ReadAllText(path,new UTF8Encoding(false,true)); }
        catch(Exception ex) { ClassExperiment.Summary=ex.Message;ClassExperiment.Details=ex.ToString();ClassExperiment.Show(app);return; }
        var outcome=Run(app,template,owner,ownerField,where,pumlText,path,Path.GetFileNameWithoutExtension(path),message=>app.Window.UI.ShowConfirmDialog(message,ClassExperiment.Title));
        ClassExperiment.Summary=outcome.Summary;
        string stem=ClassExperiment.SaveReport("create",outcome.Log,outcome.ReportJson,outcome.CurrentPuml);
        if(stem!=null)ClassExperiment.Summary+="\n診断保存先: "+stem+".txt";
        ClassExperiment.Details=outcome.Details;
        ClassExperiment.Show(app);
    }

    sealed class Placed { public ClassDiagramDraft.Seed Seed; public IModel Model, Template; public INode TemplateNode; public bool Created; }

    // Where the new diagram goes and which class diagram serves as its template. where says how
    // they were found, for the confirmation and the log.
    public static string ResolvePlace(IApplication app,IEditor editor,out IEditor template,out IModel owner,out IField ownerField)
    {
        template=null;owner=null;ownerField=null;
        if(ClassDiagramKind.Reject(editor)==null)
        {
            template=editor;var diagramModel=ClassDiagramKind.ModelOf(editor);
            if(diagramModel==null)throw new InvalidOperationException("C310: 開いている図のモデルを取得できません。");
            owner=diagramModel.Owner;try { ownerField=diagramModel.GetOwnerField(); } catch(Exception) { }
            if(owner==null || ownerField==null)throw new InvalidOperationException("C310: 開いている図のモデルの所有先を取得できません。");
            return "開いている図と同じ所有先";
        }
        IModel parent=editor!=null?ClassDiagramKind.ModelOf(editor):null;
        if(parent==null) { try { parent=app.Window.EditorPage.CurrentModel; } catch(Exception) { } }
        if(parent==null)throw new InvalidOperationException("C310: クラス図を追加するモデル（クラス図グループなど）をナビゲータで選ぶか、雛形にするクラス図を開いてから実行してください。");
        var trace=new StringBuilder();
        template=FindTemplate(parent,trace);
        if(template==null)throw new InvalidOperationException("C310: '"+ClassText.Normalize(parent.Name)+"' の直下に、雛形にできるクラス図（クラスが 1 つ以上載っているもの）がありません。雛形にするクラス図を開いてから実行してください。\n"+trace.ToString().TrimEnd());
        var metaclass=ClassDiagramKind.ModelOf(template).Metaclass;
        ownerField=parent.Metaclass.GetFields().Cast<IField>().FirstOrDefault(f=>f.IsEmbedded && f.TypeClass!=null && f.TypeClass.IsClassOf(metaclass));
        if(ownerField==null)throw new InvalidOperationException("C310: '"+ClassText.Normalize(parent.Name)+"'（"+parent.ClassName+"）にはクラス図（"+metaclass.Name+"）を追加できません。クラス図グループを選んでから実行してください。");
        owner=parent;
        return "選択中のモデルの下（雛形は直下の図）";
    }
    // Only the direct children are searched: walking the tree reads every model's editors and
    // took too long on a real project. trace collects what was seen, for the message when
    // nothing is found.
    static IEditor FindTemplate(IModel parent,StringBuilder trace)
    {
        int models=0,editors=0;var kinds=new List<string>();string firstError=null;
        List<IModel> children;
        try { children=parent.GetChildren().Cast<IModel>().Where(c=>c!=null && !c.IsDeleted).ToList(); }
        catch(Exception ex) { trace.AppendLine("GetChildren: "+ex.Message);return null; }
        foreach(var m in children)
        {
            models++;
            try
            {
                foreach(var e in m.GetEditors().Cast<object>().OfType<IEditor>())
                {
                    editors++;
                    if(ClassDiagramKind.Reject(e)!=null)continue;
                    bool shown=false;
                    try { shown=((IDiagram)e).Nodes.Cast<object>().OfType<INode>().Any(n=>ClassDiagramKind.ModelOf(n)!=null); }
                    catch(Exception ex) { if(firstError==null)firstError="Nodes: "+ex.Message; }
                    if(shown)return e;
                    if(kinds.Count<8)kinds.Add("'"+ClassText.Normalize(m.Name)+"' "+e.EditorType+"/"+e.ViewDefinitionName+" ノードなし");
                }
            }
            catch(Exception ex) { if(firstError==null)firstError="GetEditors('"+ClassText.Normalize(m.Name)+"'): "+ex.Message; }
        }
        trace.AppendLine("直下のモデル "+models+" / エディタ "+editors);
        if(kinds.Count>0)trace.AppendLine("クラス図と判定したエディタ: "+string.Join(" | ",kinds.ToArray()));
        if(firstError!=null)trace.AppendLine("最初の例外: "+firstError);
        return null;
    }

    public static ClassSyncRuntime.Outcome Run(IApplication app,IEditor template,IModel owner,IField ownerField,string where,string pumlText,string sourceLabel,string fallbackTitle,Func<string,bool> confirm)
    {
        var log=new StringBuilder();var outcome=new ClassSyncRuntime.Outcome();
        IModel diagramModel=null;var placed=new List<Placed>();bool saved=false;
        try
        {
            string reject=ClassDiagramKind.Reject(template);
            if(reject!=null)throw new InvalidOperationException(reject);
            var project=app.Workspace.CurrentProject;
            if(project==null || string.IsNullOrEmpty(project.Path))throw new InvalidOperationException("C310: 保存済みのプロジェクトで実行してください。");
            if(project.HasUnsavedChanges())throw new InvalidOperationException("C310: 未保存の変更があります。作成の途中でプロジェクトを保存するので、先に保存してから実行してください。");
            log.AppendLine("PlantUML source: "+sourceLabel);
            var input=new ClassPumlParser().Parse(pumlText);
            var templateDiagram=(IDiagram)template;
            var templateModel=ClassDiagramKind.ModelOf(template);
            if(templateModel==null)throw new InvalidOperationException("C310: 雛形の図のモデルを取得できません。");
            var read=ClassDiagramSnapshot.Read(templateDiagram,new ClassSyncOptions(),log);
            var draft=ClassDiagramDraft.Plan(input,read.Document,fallbackTitle);
            log.AppendLine("draft: title='"+draft.Title+"' existing="+draft.ExistingCount+" new="+draft.NewCount+" seeds="+string.Join(",",draft.Seeds.Select(s=>s.Name+(s.Existing?"(existing)":"(new)")).ToArray()));
            if(draft.Reasons.Count>0)throw new InvalidOperationException("C310: 新しい図を作れません。\n"+string.Join("\n",draft.Reasons.ToArray()));

            log.AppendLine("place: "+where+" owner="+(owner==null?"?":owner.ClassName+" '"+owner.Name+"'."+(ownerField==null?"?":ownerField.Name))+" template='"+templateModel.Name+"' id="+template.Id);
            if(owner==null || ownerField==null || !owner.IsEditable)throw new InvalidOperationException("C310: 図を追加する先のモデルが編集できません。");
            if(owner.GetFieldValues(ownerField.Name).Cast<object>().OfType<IModel>().Any(m=>!m.IsDeleted && m.ClassName==templateModel.ClassName && ClassText.Inline(ClassText.Normalize(m.Name))==draft.Title))
                throw new InvalidOperationException("C310: '"+ClassText.Normalize(owner.Name)+"' に同じ名前の図 '"+draft.Title+"' が既にあります。title を変えてください。");
            foreach(var seed in draft.Seeds)
            {
                string modelId;
                if(!read.ModelIds.TryGetValue(seed.TemplateId,out modelId))throw new InvalidOperationException("C310: 雛形のクラスのモデルを特定できません: "+seed.Name);
                var model=project.GetModelById(modelId);
                var node=templateDiagram.Nodes.Cast<object>().OfType<INode>().FirstOrDefault(n=>{var m=ClassDiagramKind.ModelOf(n);return m!=null && m.Id==modelId;});
                if(model==null || node==null)throw new InvalidOperationException("C310: 雛形のクラスのモデルかノードを取得できません: "+seed.Name);
                var p=new Placed{Seed=seed,Template=model,TemplateNode=node};
                if(seed.Existing)p.Model=model;
                else
                {
                    var classOwner=model.Owner;IField classField=null;
                    try { classField=model.GetOwnerField(); } catch(Exception) { }
                    if(classOwner==null || classField==null || !classOwner.IsEditable)throw new InvalidOperationException("C310: 雛形のクラス '"+ClassText.Normalize(model.Name)+"' の所有先にクラスを追加できません。");
                    if(classOwner.GetFieldValues(classField.Name).Cast<object>().OfType<IModel>().Any(m=>!m.IsDeleted && ClassText.Inline(ClassText.Normalize(m.Name))==seed.Name))
                        throw new InvalidOperationException("C310: '"+ClassText.Normalize(classOwner.Name)+"' に同じ名前のクラス '"+seed.Name+"' が既にあります。雛形の図に載っていないクラスは再利用できません。");
                    log.AppendLine("seed '"+seed.Name+"': new "+model.Metaclass.FullName+" under "+classOwner.ClassName+" '"+classOwner.Name+"'."+classField.Name+" (template '"+model.Name+"')");
                }
                placed.Add(p);
            }
            // The editor to clone when the new model has no editor of this view yet, and the line
            // to clone for relationships (the new diagram starts without one). Needs the saved project.
            var unit=ClassEditorCapture.ReadUnit(project,templateModel,template,log);
            if(unit.Editor==null || string.IsNullOrEmpty(unit.Schema))throw new InvalidOperationException("C310: 雛形の図の Editor JSON を取得できません。");
            var lines=unit.Editor["Connectors"];
            var connectorTemplate=lines!=null && lines.Items!=null && lines.Items.Count>0?lines.Items[0]:null;
            if(connectorTemplate==null && input.Elements.Any(e=>e.Kind=="link"))
                log.AppendLine("the template diagram has no connector; relationship lines cannot be added");

            string question="クラス図「"+draft.Title+"」を「"+ClassText.Normalize(owner.Name)+"」の下に新しく作ります。\n"
                +"雛形: 図「"+ClassText.Normalize(templateModel.Name)+"」（"+where+"。ビュー定義・線の形・新しいクラスの所有先と種類）\n"
                +"既存のクラス "+draft.ExistingCount+" 件を載せ、新しいクラス "+draft.NewCount+" 件を作ります。\n"
                +"途中でプロジェクトを保存し、そのあと反映の内容を確認します。";
            if(!confirm(question)) { outcome.Summary="新しい図の作成を中止しました。";outcome.Succeeded=true;return Finish(outcome,log); }

            var transaction=project.BeginUndoTransaction(false);
            IDiagram created;
            try
            {
                diagramModel=owner.AddNewModel(ownerField,templateModel.Metaclass);
                if(diagramModel==null)throw new InvalidOperationException("C320: 図のモデルを作成できませんでした。");
                diagramModel.SetField("Name",draft.Title);
                log.AppendLine("diagram model "+diagramModel.ClassName+" id="+diagramModel.Id+" name='"+diagramModel.Name+"'");
                created=EnsureEditor(project,diagramModel,template,unit,log);
                double width=placed.Max(x=>x.TemplateNode.Width),height=placed.Max(x=>x.TemplateNode.Height);
                int column=0;
                foreach(var p in placed)
                {
                    if(p.Model==null)
                    {
                        p.Model=p.Template.Owner.AddNewModel(p.Template.GetOwnerField(),p.Template.Metaclass);
                        if(p.Model==null)throw new InvalidOperationException("C320: クラスを作成できませんでした: "+p.Seed.Name);
                        p.Model.SetField("Name",p.Seed.Name);
                        if(ClassText.Inline(ClassText.Normalize(p.Model.Name))!=p.Seed.Name)throw new InvalidOperationException("C320: 作成したクラスの名前の読戻しが一致しません: "+p.Seed.Name);
                        p.Created=true;
                        log.AppendLine("created class "+p.Model.ClassName+" id="+p.Model.Id+" name='"+p.Model.Name+"'");
                    }
                    var def=(p.TemplateNode as IRepresentation)==null?null:(p.TemplateNode as IRepresentation).ViewDefinition as IElementDef;
                    try { created.AddNodeShape(p.Model,def); } catch(Exception ex) { log.AppendLine("AddNodeShape failed for '"+p.Seed.Name+"': "+ex.Message); }
                    var node=created.Nodes.Cast<object>().OfType<INode>().FirstOrDefault(n=>{var m=ClassDiagramKind.ModelOf(n);return m!=null && m.Id==p.Model.Id;});
                    if(node==null)throw new InvalidOperationException("C320: 新しい図にクラス '"+p.Seed.Name+"' のノードを置けませんでした。");
                    // Every other column stays free for the classes added next to this one.
                    node.SetLocationAt(40+column*2*(width+80),40);node.SetSizeAt(p.TemplateNode.Width,p.TemplateNode.Height);column++;
                    log.AppendLine("seed node "+node.Id+" '"+p.Seed.Name+"' at ("+node.LocationX+","+node.LocationY+") visible="+node.IsVisible);
                }
                transaction.Commit();
            }
            catch(Exception)
            {
                try { transaction.Rollback(); } catch(Exception rollbackError) { log.AppendLine("rollback failed: "+rollbackError.Message); }
                diagramModel=null;placed.Clear();
                throw;
            }
            // The sync exports the new diagram before writing (relationship lines need it), which
            // the product refuses while the project is dirty.
            saved=app.Workspace.SaveProject(project,false);
            log.AppendLine("save: "+saved+" unsaved after="+project.HasUnsavedChanges());
            if(!saved || project.HasUnsavedChanges())throw new InvalidOperationException("C320: 作成した図を保存できませんでした。");

            ClassSyncRuntime.ConnectorTemplate=connectorTemplate;
            ClassSyncRuntime.MemberTemplates=placed.Where(p=>p.Created).ToDictionary(p=>p.Model.Id,p=>p.Template.Id,StringComparer.Ordinal);
            var sync=ClassSyncRuntime.Run(app,(IEditor)created,pumlText,sourceLabel,true,true,true,confirm,(desired,snapshot,prepareLog)=>{
                draft.Prepare(desired,snapshot.Document);
                prepareLog.AppendLine("input adjusted for the new diagram: "+desired.Elements.Count+" elements");
            });
            log.AppendLine("---- sync ----").Append(sync.Log);
            outcome.ReportJson=sync.ReportJson;outcome.CurrentPuml=sync.CurrentPuml;
            outcome.Changes=sync.Changes;outcome.Limitations=sync.Limitations;outcome.StopReasons=sync.StopReasons;
            if(sync.Succeeded)
            {
                outcome.Succeeded=outcome.Applied=outcome.Committed=true;
                SelectInNavigator(app,diagramModel,log);
                outcome.Summary="クラス図「"+draft.Title+"」を作成しました（既存のクラス "+draft.ExistingCount+" 件 / 新しいクラス "+draft.NewCount+" 件）。\n"
                    +"反映の結果:\n"+sync.Summary+"\nナビゲータで新しい図を開いて確認してください。保存はしていません。";
                outcome.Details=outcome.Summary+"\f"+sync.Details;
                return Finish(outcome,log,true);
            }
            outcome.ErrorMessage=sync.ErrorMessage??"反映できませんでした";
            string removed=Remove(app,project,diagramModel,placed,log);
            outcome.Summary="新しい図に PlantUML を反映できませんでした。\n"+sync.Summary+"\n"+removed;
            outcome.Details=outcome.Summary+"\f"+sync.Details;
            return Finish(outcome,log,true);
        }
        catch(Exception ex)
        {
            outcome.ErrorMessage=ex.Message;
            log.AppendLine(ex.ToString());
            string removed=saved?Remove(app,app.Workspace.CurrentProject,diagramModel,placed,log):"";
            outcome.Summary="新しい図を作成できませんでした。\n"+ex.Message+(removed.Length>0?"\n"+removed:"");
            return Finish(outcome,log);
        }
        finally { ClassSyncRuntime.ConnectorTemplate=null;ClassSyncRuntime.MemberTemplates=null; }
    }

    static ClassSyncRuntime.Outcome Finish(ClassSyncRuntime.Outcome outcome,StringBuilder log,bool detailsSet=false)
    {
        outcome.Log=log.ToString();
        if(!detailsSet)outcome.Details=outcome.Summary+"\f"+outcome.Log;
        return outcome;
    }

    // The new model may already come with an editor of the template's view; otherwise the
    // template's editor entry (without its shapes) is imported for it.
    static IDiagram EnsureEditor(IProject project,IModel model,IEditor template,ClassEditorCapture.Unit unit,StringBuilder log)
    {
        Func<IDiagram> find=()=>model.GetEditors().Cast<object>().OfType<IEditor>()
            .Where(e=>e.ViewDefinitionName==template.ViewDefinitionName && e.EditorType==template.EditorType).OfType<IDiagram>().FirstOrDefault();
        var found=find();
        if(found!=null) { log.AppendLine("editor came with the model: "+((IEditor)found).Id);return found; }
        var entry=ClassJsonNode.Parse(unit.Editor.ToJsonString());
        entry.Properties.Remove("Nodes");entry.Properties.Remove("Connectors");
        entry.Properties["Id"]=new ClassJsonNode{Raw=ClassJson.Q(Guid.NewGuid().ToString())};
        entry.Properties["ModelId"]=new ClassJsonNode{Raw=ClassJson.Q(model.Id)};
        log.AppendLine("editor entry for import: keys="+string.Join(",",entry.Properties.Keys));
        string json="{\"Type\":\"Model\",\"SchemaVersion\":"+ClassJson.Q(unit.Schema)+",\"TopElementId\":"+ClassJson.Q(model.Id)
            +",\"Entities\":[],\"Relations\":[],\"Editors\":["+entry.ToJsonString()+"]}";
        var result=project.ImportUnitFromJson(json,null,null);
        if(result==null)throw new InvalidOperationException("C320: 図のエディタを作成できませんでした（インポート結果なし）。");
        log.AppendLine("editor import: "+result.State);
        foreach(var e in result.Errors)log.AppendLine(e.Kind+": "+e.Message);
        if(result.State!="success" || result.Errors.Any(e=>e.Kind!=UnitImportErrorKind.Info))throw new InvalidOperationException("C320: 図のエディタの作成が失敗または警告を返しました。");
        found=find();
        if(found==null)throw new InvalidOperationException("C320: 作成した図のエディタが見つかりません。");
        log.AppendLine("editor imported: "+((IEditor)found).Id);
        return found;
    }

    // Undo what the creator made when the sync did not go through. The saved file still holds
    // the diagram until the project is saved again.
    static string Remove(IApplication app,IProject project,IModel diagramModel,List<Placed> placed,StringBuilder log)
    {
        if(project==null || diagramModel==null)return "";
        var transaction=project.BeginUndoTransaction(false);
        try
        {
            foreach(var p in placed.Where(x=>x.Created && x.Model!=null && !x.Model.IsDeleted))p.Model.Delete();
            if(!diagramModel.IsDeleted)diagramModel.Delete();
            transaction.Commit();
            log.AppendLine("removed the new diagram and "+placed.Count(x=>x.Created)+" created classes");
            return "作成した図と、そのために作ったクラス "+placed.Count(x=>x.Created)+" 件を削除しました。保存済みのファイルには残っているので、上書き保存すると削除が確定します。";
        }
        catch(Exception ex)
        {
            try { transaction.Rollback(); } catch(Exception) { }
            log.AppendLine("removal failed: "+ex);
            return "作成した図「"+ClassText.Normalize(diagramModel.Name)+"」を削除できませんでした。不要なら手動で削除してください。";
        }
    }

    static void SelectInNavigator(IApplication app,IModel model,StringBuilder log)
    {
        try { app.Window.EditorPage.CurrentNavigator.Select(model,false);log.AppendLine("selected in navigator"); }
        catch(Exception ex) { log.AppendLine("navigator select failed: "+ex.Message); }
    }
}
