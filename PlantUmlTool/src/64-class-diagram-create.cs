// ============================================================
//  Part 9 / PlantUML から新しいクラス図を作る
//
//    見本の図は使わない。クラス図グループ（または開いているクラス図の所有先）の
//    クラス図欄に図のモデルを作り、PlantUML の package "名前" { } をグループ配下の
//    既存モデル（Domain など）に対応させてクラスを置く。
//    ・そのモデルに同じ名前のクラスがあれば、既存のクラスを図に載せる
//    ・無ければ、そのモデルが持てるメタクラスのうちキーワード・ステレオタイプが合うもので作る
//    ノードの定義はプロファイルのビュー定義から引く（FindElementDefByClass）。
//    package と種類ごとに 1 つ（種）を先に作って図に置き、いったん保存してから、
//    残りのクラス・メンバ・関連を通常の「PlantUMLを反映」と同じ本体で足す。
//    計画（テキストの読み取りと所有先の決め方）は純粋部 ClassDiagramDraft にある。
// ============================================================

public static class ClassDiagramCreator
{
    // Ribbon entry: find where the diagram goes, pick the file, create, show the result.
    public static void Create(IApplication app)
    {
        var editor=app.Workspace.CurrentEditor;
        IModel owner;IField field;IClass diagramClass;string where;
        try { where=ResolveGroup(app,editor,out owner,out field,out diagramClass); }
        catch(Exception ex) { ClassExperiment.Summary=ex.Message;ClassExperiment.Details=ex.ToString();ClassExperiment.Show(app);return; }
        string path=app.Window.UI.ShowOpenFileDialog("新しいクラス図にするPlantUML","PlantUML (*.puml;*.plantuml)|*.puml;*.plantuml");
        if(string.IsNullOrEmpty(path))return;
        string pumlText;
        try { if(new FileInfo(path).Length>300000)throw new InvalidOperationException("C120: 入力は300KB以下にしてください。");pumlText=File.ReadAllText(path,new UTF8Encoding(false,true)); }
        catch(Exception ex) { ClassExperiment.Summary=ex.Message;ClassExperiment.Details=ex.ToString();ClassExperiment.Show(app);return; }
        var outcome=Run(app,owner,field,diagramClass,where,pumlText,path,Path.GetFileNameWithoutExtension(path),message=>app.Window.UI.ShowConfirmDialog(message,ClassExperiment.Title));
        ClassExperiment.Summary=outcome.Summary;
        string stem=ClassExperiment.SaveReport("create",outcome.Log,outcome.ReportJson,outcome.CurrentPuml);
        if(stem!=null)ClassExperiment.Summary+="\n診断保存先: "+stem+".txt";
        ClassExperiment.Details=outcome.Details;
        ClassExperiment.Show(app);
    }

    static string Name(IModel m) { return m==null?"":ClassText.Inline(ClassText.Normalize(m.Name)); }
    static List<IModel> Children(IModel m)
    {
        // Distinct by id: on the real project every class came back twice (2.4.2).
        try { return m.GetChildren().Cast<IModel>().Where(c=>c!=null && !c.IsDeleted).GroupBy(c=>c.Id).Select(g=>g.First()).ToList(); }
        catch(Exception) { return new List<IModel>(); }
    }
    static List<IEditor> Editors(IModel m)
    {
        try { return m.GetEditors().Cast<object>().OfType<IEditor>().ToList(); }
        catch(Exception) { return new List<IEditor>(); }
    }
    // Models 2..depth levels below m (the direct children are looked at by the caller).
    static List<IModel> Below(IModel m,int depth)
    {
        var result=new List<IModel>();var level=Children(m);
        for(int d=2;d<=depth && level.Count>0 && result.Count<2000;d++)
        {
            level=level.SelectMany(Children).ToList();
            result.AddRange(level);
        }
        return result.GroupBy(c=>c.Id).Select(g=>g.First()).ToList();
    }
    static bool HasClassDiagram(IModel m) { return Editors(m).Any(e=>ClassDiagramKind.Reject(e)==null); }
    static IEnumerable<IClass> Concrete(IClass declared)
    {
        var all=new List<IClass>{declared};
        try { all.AddRange(declared.GetAllSubClasses().Cast<IClass>()); } catch(Exception) { }
        return all.Where(c=>c!=null && !c.IsAbstract).GroupBy(c=>c.FullName).Select(g=>g.First());
    }

    // The model that gets the diagram, the field that holds class diagrams and their metaclass.
    // With a class diagram open: the same as that diagram. Otherwise the open or selected model
    // (a class diagram group): the field whose existing children are class diagrams, or else the
    // one whose element type has a class-diagram editor in the profile.
    public static string ResolveGroup(IApplication app,IEditor editor,out IModel owner,out IField field,out IClass diagramClass)
    {
        owner=null;field=null;diagramClass=null;
        if(ClassDiagramKind.Reject(editor)==null)
        {
            var diagramModel=ClassDiagramKind.ModelOf(editor);
            if(diagramModel==null || diagramModel.Owner==null)throw new InvalidOperationException("C310: 開いている図のモデルの所有先を取得できません。");
            owner=diagramModel.Owner;diagramClass=diagramModel.Metaclass;
            try { field=diagramModel.GetOwnerField(); } catch(Exception) { }
            if(field==null)throw new InvalidOperationException("C310: 開いている図のモデルの所有フィールドを取得できません。");
            return "開いている図と同じ「"+Name(owner)+"」の下";
        }
        IModel parent=editor!=null?ClassDiagramKind.ModelOf(editor):null;
        if(parent==null) { try { parent=app.Window.EditorPage.CurrentModel; } catch(Exception) { } }
        if(parent==null)throw new InvalidOperationException("C310: クラス図を追加するモデル（クラス図グループなど）を開くか選んでから実行してください。");
        var fields=parent.Metaclass.GetFields().Cast<IField>().Where(f=>f.IsEmbedded && f.TypeClass!=null).ToList();
        // A class diagram already in the group shows which field and metaclass to use.
        foreach(var child in Children(parent))
        {
            if(!HasClassDiagram(child))continue;
            IField own=null;try { own=child.GetOwnerField(); } catch(Exception) { }
            if(own==null)continue;
            owner=parent;field=own;diagramClass=child.Metaclass;
            return "「"+Name(parent)+"」の下";
        }
        // No diagram yet: the profile's editor definitions.
        var views=app.Workspace.CurrentProject.Profile.ViewDefinitions;
        var found=new List<KeyValuePair<IField,IClass>>();
        foreach(var f in fields)
            foreach(var c in Concrete(f.TypeClass))
            {
                bool classEditor=false;
                try { classEditor=views.FindEditorDefByClass(c,null).Cast<IEditorDef>().Any(d=>ClassSyncOptions.ClassEditorTypes.Contains(d.Type)); } catch(Exception) { }
                if(classEditor)found.Add(new KeyValuePair<IField,IClass>(f,c));
            }
        if(found.Count==0)throw new InvalidOperationException("C310: '"+Name(parent)+"'（"+parent.ClassName+"）にはクラス図を追加できません。クラス図グループを開くか選んでから実行してください。");
        if(found.Count>1)throw new InvalidOperationException("C310: '"+Name(parent)+"' に追加できる図の種類が複数あり、クラス図を決められません: "+string.Join(", ",found.Select(p=>p.Key.Name+"/"+p.Value.Name).ToArray()));
        owner=parent;field=found[0].Key;diagramClass=found[0].Value;
        return "「"+Name(parent)+"」の下";
    }

    // Keyword and stereotype a class of this metaclass reads back as (the snapshot's rules).
    static string KeywordOf(IClass c,ClassSyncOptions o)
    {
        string keyword;
        if(o.KeywordMap.TryGetValue(c.Name,out keyword))return keyword;
        try { foreach(var s in c.GetAllSuperClasses().Cast<IClass>())if(o.KeywordMap.TryGetValue(s.Name,out keyword))return keyword; } catch(Exception) { }
        return "class";
    }
    static string StereotypeOf(IClass c,string keyword,ClassSyncOptions o)
    {
        if(!o.EmitStereotypes)return "";
        string stereotype;
        if(o.StereotypeMap.TryGetValue(c.Name,out stereotype))return ClassText.Normalize(stereotype);
        if(!string.Equals(keyword,"class",StringComparison.OrdinalIgnoreCase) || !o.EmitUnknownStereotype)return "";
        return ClassText.Normalize(c.Name);
    }

    // A package path from the input: first below the group, then from the project root (the
    // full owner path the exporter writes).
    static IModel ResolvePath(IProject project,IModel group,string[] path,out string problem)
    {
        problem=null;
        // The exporter writes the owner path from the top, starting with the project name (seen
        // on a real export: "OnBoardClient/OnBoardClient/ソフトウェア詳細設計/..."), so the head is
        // not assumed: the path is taken after each place the group's (or the root's) name
        // appears in it, then as written (a hand-written block naming a model below the group).
        var starts=new List<KeyValuePair<IModel,string[]>>();
        var root=project.DesignModel;
        foreach(var from in new[]{group,root})
        {
            if(from==null)continue;
            for(int i=path.Length-1;i>=0;i--)
                if(path[i]==Name(from))starts.Add(new KeyValuePair<IModel,string[]>(from,path.Skip(i+1).ToArray()));
        }
        starts.Add(new KeyValuePair<IModel,string[]>(group,path));
        if(root!=null)starts.Add(new KeyValuePair<IModel,string[]>(root,path));
        foreach(var start in starts)
        {
            var at=start.Key;string missing=null;
            foreach(var segment in start.Value)
            {
                var next=Children(at).Where(c=>Name(c)==segment).ToList();
                if(next.Count!=1) { missing=next.Count==0?"'"+segment+"' が '"+Name(at)+"' の下にありません":"'"+Name(at)+"' の下に '"+segment+"' が複数あります";break; }
                at=next[0];
            }
            if(missing==null)return at;
            if(problem==null)problem=missing;
        }
        return null;
    }

    sealed class Kind { public IField Field; public IClass Class; public string Keyword, Stereotype; }
    sealed class Placed { public ClassDiagramDraft.Item Item; public IModel Package, Model; public Kind Kind; public bool Created, Container; }

    public static ClassSyncRuntime.Outcome Run(IApplication app,IModel owner,IField field,IClass diagramClass,string where,string pumlText,string sourceLabel,string fallbackTitle,Func<string,bool> confirm)
    {
        var log=new StringBuilder();var outcome=new ClassSyncRuntime.Outcome();
        IModel diagramModel=null;var placed=new List<Placed>();bool saved=false;
        try
        {
            var project=app.Workspace.CurrentProject;
            if(project==null || string.IsNullOrEmpty(project.Path))throw new InvalidOperationException("C310: 保存済みのプロジェクトで実行してください。");
            if(project.HasUnsavedChanges())throw new InvalidOperationException("C310: 未保存の変更があります。作成の途中でプロジェクトを保存するので、先に保存してから実行してください。");
            if(!owner.IsEditable)throw new InvalidOperationException("C310: '"+Name(owner)+"' は編集できません。");
            log.AppendLine("PlantUML source: "+sourceLabel);
            log.AppendLine("place: "+where+" "+owner.ClassName+"."+field.Name+" as "+diagramClass.FullName);
            var input=new ClassPumlParser().Parse(pumlText);
            var draft=ClassDiagramDraft.Plan(input,fallbackTitle);
            if(draft.Reasons.Count>0)throw new InvalidOperationException("C310: 新しい図を作れません。\n"+string.Join("\n",draft.Reasons.ToArray()));
            // An exported diagram keeps its title; the copy gets the next free number.
            var taken=new HashSet<string>(Children(owner).Where(m=>m.ClassName==diagramClass.Name).Select(Name),StringComparer.Ordinal);
            if(taken.Contains(draft.Title))
            {
                int n=2;while(taken.Contains(draft.Title+" "+n))n++;
                log.AppendLine("title '"+draft.Title+"' is taken; using '"+draft.Title+" "+n+"'");
                draft.Title=draft.Title+" "+n;
            }

            // Owners and kinds, all before anything is written.
            var options=new ClassSyncOptions();var reasons=new List<string>();
            var packages=new Dictionary<string,IModel>(StringComparer.Ordinal);
            var kinds=new Dictionary<string,List<Kind>>(StringComparer.Ordinal);
            foreach(var item in draft.Items.Where(i=>i.Container))
            {
                // A package/component box: the owner the exporter showed on the diagram. It must exist.
                string problem;var box=ResolvePath(project,owner,item.Path.Concat(new[]{item.Name}).ToArray(),out problem);
                if(box==null) { reasons.Add("箱 '"+string.Join("/",item.Path.Concat(new[]{item.Name}).ToArray())+"' に対応するモデルがありません（"+problem+"）。package / component の新規作成は扱えません");continue; }
                log.AppendLine("box "+item.Name+" -> "+box.ClassName+" '"+box.Name+"' id="+box.Id);
                placed.Add(new Placed{Item=item,Package=box.Owner,Model=box,Container=true});
            }
            foreach(var item in draft.Items.Where(i=>!i.Container))
            {
                string key=string.Join("\u0001",item.Path);IModel package;
                if(!packages.TryGetValue(key,out package))
                {
                    string problem;package=ResolvePath(project,owner,item.Path,out problem);
                    if(package==null) { reasons.Add("package '"+string.Join("/",item.Path)+"' に対応するモデルがありません（"+problem+"）");packages[key]=null;continue; }
                    packages[key]=package;
                    log.AppendLine("package "+string.Join("/",item.Path)+" -> "+package.ClassName+" '"+package.Name+"' id="+package.Id);
                }
                if(package==null)continue;
                var p=new Placed{Item=item,Package=package};
                var same=Children(package).Where(c=>Name(c)==item.Name).ToList();
                // The exporter writes a class owned by another class at the depth of the nearest box.
                if(same.Count==0)same=Below(package,3).Where(c=>Name(c)==item.Name && c.Metaclass!=null && !ClassDocument.IsContainerKeyword(KeywordOf(c.Metaclass,options))).ToList();
                if(same.Count>1)
                {
                    // Same name, different models: the keyword and stereotype written in the input decide.
                    var fitting=same.Where(c=>c.Metaclass!=null && KeywordOf(c.Metaclass,options)==item.Keyword
                        && (item.Stereotype.Length==0 || StereotypeOf(c.Metaclass,item.Keyword,options)==item.Stereotype)).ToList();
                    if(fitting.Count==1)same=fitting;
                    else { reasons.Add("'"+Name(package)+"' に '"+item.Name+"' が複数あります: "+string.Join(", ",same.Select(c=>c.ClassName+" id="+c.Id).ToArray()));continue; }
                }
                if(same.Count==1) { p.Model=same[0];placed.Add(p);continue; }
                List<Kind> available;
                if(!kinds.TryGetValue(package.Id,out available))
                {
                    available=new List<Kind>();
                    foreach(var f in package.Metaclass.GetFields().Cast<IField>().Where(f=>f.IsEmbedded && f.TypeClass!=null))
                        foreach(var c in Concrete(f.TypeClass))
                        {
                            string k=KeywordOf(c,options);
                            if(ClassDocument.IsContainerKeyword(k))continue;
                            available.Add(new Kind{Field=f,Class=c,Keyword=k,Stereotype=StereotypeOf(c,k,options)});
                        }
                    kinds[package.Id]=available;
                }
                var fits=available.Where(k=>k.Keyword==item.Keyword && (item.Stereotype.Length==0 || k.Stereotype==item.Stereotype)).ToList();
                if(fits.Count>1)
                {
                    // Prefer the metaclass the package's classes already use.
                    var used=Children(package).GroupBy(c=>c.Metaclass==null?"":c.Metaclass.FullName).ToDictionary(g=>g.Key,g=>g.Count());
                    int best=fits.Max(k=>{int n;return used.TryGetValue(k.Class.FullName,out n)?n:0;});
                    if(best>0)fits=fits.Where(k=>{int n;return used.TryGetValue(k.Class.FullName,out n) && n==best;}).ToList();
                }
                if(fits.Count!=1)
                {
                    var choices=available.Where(k=>k.Keyword==item.Keyword).Select(k=>"<<"+k.Stereotype+">>").Distinct().ToArray();
                    reasons.Add("'"+item.Name+"' の種類を決められません（'"+Name(package)+"' に置ける "+item.Keyword+(fits.Count==0?": "+(choices.Length==0?"なし":string.Join(" ",choices)):" が複数: "+string.Join(" ",fits.Select(k=>"<<"+k.Stereotype+">>").ToArray()))+"）。ステレオタイプを書いてください");
                    continue;
                }
                p.Kind=fits[0];placed.Add(p);
            }
            if(reasons.Count>0)throw new InvalidOperationException("C310: 新しい図を作れません。\n"+string.Join("\n",reasons.ToArray()));
            // Seeds: every existing class, and the first new class of each package and kind that
            // has no existing class of that kind on the diagram to sit next to.
            var anchors=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var p in placed.Where(x=>x.Model!=null && !x.Container))
            {
                draft.Seeds.Add(new ClassDiagramDraft.Seed{Name=p.Item.Name,Existing=true});draft.Anchors[p.Item.Name]=p.Item.Name;
                string key=p.Package.Id+"|"+p.Model.Metaclass.FullName;if(!anchors.ContainsKey(key))anchors[key]=p.Item.Name;
            }
            foreach(var p in placed.Where(x=>x.Model==null))
            {
                string key=p.Package.Id+"|"+p.Kind.Class.FullName;string anchor;
                if(anchors.TryGetValue(key,out anchor)) { draft.Anchors[p.Item.Name]=anchor;continue; }
                anchors[key]=p.Item.Name;draft.Anchors[p.Item.Name]=p.Item.Name;
                draft.Seeds.Add(new ClassDiagramDraft.Seed{Name=p.Item.Name});p.Created=true;
                log.AppendLine("seed '"+p.Item.Name+"': new "+p.Kind.Class.FullName+" in '"+p.Package.Name+"'."+p.Kind.Field.Name);
            }
            var seeds=placed.Where(p=>p.Model!=null || p.Created).ToList();
            log.AppendLine("draft: title='"+draft.Title+"' existing="+draft.ExistingCount+" new="+draft.NewCount+" seeds="+seeds.Count);

            // The line to clone for relationships: from a class diagram already in the group, if any.
            ClassJsonNode connectorTemplate=null;
            if(input.Elements.Any(e=>e.Kind=="link"))connectorTemplate=FindConnectorTemplate(project,owner,log);

            string question="クラス図「"+draft.Title+"」を"+where+"に新しく作ります。\n"
                +"既存の箱 "+draft.ContainerCount+" 件・既存のクラス "+draft.ExistingCount+" 件を載せ、新しいクラス "+draft.NewCount+" 件を作ります（置き場は package で指定したモデル）。\n"
                +(input.Elements.Any(e=>e.Kind=="link") && connectorTemplate==null?"同じグループに線のあるクラス図が無いため、関連はモデルには作りますが図の線は表示されません。\n":"")
                +"途中でプロジェクトを保存し、そのあと反映の内容を確認します。";
            if(!confirm(question)) { outcome.Summary="新しい図の作成を中止しました。";outcome.Succeeded=true;return Finish(outcome,log); }

            var transaction=project.BeginUndoTransaction(false);
            IDiagram created;
            try
            {
                diagramModel=owner.AddNewModel(field,diagramClass);
                if(diagramModel==null)throw new InvalidOperationException("C320: 図のモデルを作成できませんでした。");
                diagramModel.SetField("Name",draft.Title);
                log.AppendLine("diagram model "+diagramModel.ClassName+" id="+diagramModel.Id+" name='"+diagramModel.Name+"'");
                var editors=Editors(diagramModel);
                log.AppendLine("editors of the new model: "+string.Join(", ",editors.Select(e=>e.EditorType+"/"+e.ViewDefinitionName).ToArray()));
                created=editors.Where(e=>ClassDiagramKind.Reject(e)==null).OfType<IDiagram>().FirstOrDefault();
                if(created==null)throw new InvalidOperationException("C320: 新しい図のモデルにクラス図のエディタがありません（"+string.Join(", ",editors.Select(e=>e.EditorType+"/"+e.ViewDefinitionName).ToArray())+"）。");
                var views=project.Profile.ViewDefinitions;var editorDef=((IEditor)created).EditorDefinition;
                double x=40,y=40,rowHeight=0;
                foreach(var p in seeds)
                {
                    if(p.Created)
                    {
                        p.Model=p.Package.AddNewModel(p.Kind.Field,p.Kind.Class);
                        if(p.Model==null)throw new InvalidOperationException("C320: クラスを作成できませんでした: "+p.Item.Name);
                        p.Model.SetField("Name",p.Item.Name);
                        if(Name(p.Model)!=p.Item.Name)throw new InvalidOperationException("C320: 作成したクラスの名前の読戻しが一致しません: "+p.Item.Name);
                        log.AppendLine("created class "+p.Model.ClassName+" id="+p.Model.Id+" name='"+p.Model.Name+"'");
                    }
                    IElementDef def=null;
                    try { def=views.FindElementDefByClass(editorDef,p.Model.Metaclass,null).Cast<IElementDef>().FirstOrDefault(); }
                    catch(Exception ex) { log.AppendLine("FindElementDefByClass failed for "+p.Model.ClassName+": "+ex.Message); }
                    log.AppendLine("node definition for "+p.Model.ClassName+": "+(def==null?"(none)":def.Type+" "+def.Path));
                    try { created.AddNodeShape(p.Model,def); } catch(Exception ex) { log.AppendLine("AddNodeShape failed for '"+p.Item.Name+"': "+ex.Message); }
                    var node=created.Nodes.Cast<object>().OfType<INode>().FirstOrDefault(n=>{var m=ClassDiagramKind.ModelOf(n);return m!=null && m.Id==p.Model.Id;});
                    if(node==null)throw new InvalidOperationException("C320: 新しい図にクラス '"+p.Item.Name+"' のノードを置けませんでした（ノード定義 "+(def==null?"なし":def.Path)+"）。");
                    // Every other column stays free for the classes added next to this one.
                    if(x>40 && x+node.Width>6000) { x=40;y+=rowHeight+120;rowHeight=0; }
                    node.SetLocationAt(x,y);x+=2*(node.Width+80);rowHeight=Math.Max(rowHeight,node.Height);
                    log.AppendLine("seed node "+node.Id+" '"+p.Item.Name+"' at ("+node.LocationX+","+node.LocationY+" "+node.Width+"x"+node.Height+") visible="+node.IsVisible);
                }
                transaction.Commit();
            }
            catch(Exception)
            {
                try { transaction.Rollback(); } catch(Exception rollbackError) { log.AppendLine("rollback failed: "+rollbackError.Message); }
                diagramModel=null;foreach(var p in placed.Where(x=>x.Created))p.Model=null;
                throw;
            }
            // The sync exports the new diagram before adding relationship lines, which the
            // product refuses while the project is dirty.
            saved=app.Workspace.SaveProject(project,false);
            log.AppendLine("save: "+saved+" unsaved after="+project.HasUnsavedChanges());
            if(!saved || project.HasUnsavedChanges())throw new InvalidOperationException("C320: 作成した図を保存できませんでした。");

            ClassSyncRuntime.ConnectorTemplate=connectorTemplate;
            ClassSyncRuntime.AllowHiddenLines=connectorTemplate==null;
            ClassSyncRuntime.MemberTemplates=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var p in placed.Where(x=>x.Created))
            {
                // A class of the same metaclass already in the package lends its member metaclasses.
                var lender=Children(p.Package).FirstOrDefault(c=>c.Id!=p.Model.Id && c.Metaclass!=null && c.Metaclass.FullName==p.Kind.Class.FullName);
                if(lender!=null)ClassSyncRuntime.MemberTemplates[p.Model.Id]=lender.Id;
            }
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
                    +(connectorTemplate==null && input.Elements.Any(e=>e.Kind=="link")?"関連の線は表示されていません（線の雛形なし）。\n":"")
                    +"反映の結果:\n"+sync.Summary+"\n保存はしていません。";
                outcome.Details=outcome.Summary+"\f"+sync.Details;
                return Finish(outcome,log,true);
            }
            outcome.ErrorMessage=sync.ErrorMessage??"反映できませんでした";
            string removed=Remove(project,diagramModel,placed,log);
            outcome.Summary="新しい図に PlantUML を反映できませんでした。\n"+sync.Summary+"\n"+removed;
            outcome.Details=outcome.Summary+"\f"+sync.Details;
            return Finish(outcome,log,true);
        }
        catch(Exception ex)
        {
            outcome.ErrorMessage=ex.Message;
            log.AppendLine(ex.ToString());
            string removed=saved?Remove(app.Workspace.CurrentProject,diagramModel,placed,log):"";
            outcome.Summary="新しい図を作成できませんでした。\n"+ex.Message+(removed.Length>0?"\n"+removed:"");
            return Finish(outcome,log);
        }
        finally { ClassSyncRuntime.ConnectorTemplate=null;ClassSyncRuntime.MemberTemplates=null;ClassSyncRuntime.AllowHiddenLines=false; }
    }

    static ClassSyncRuntime.Outcome Finish(ClassSyncRuntime.Outcome outcome,StringBuilder log,bool detailsSet=false)
    {
        outcome.Log=log.ToString();
        if(!detailsSet)outcome.Details=outcome.Summary+"\f"+outcome.Log;
        return outcome;
    }

    // A connector entry of a class diagram already under the group, from the group's unit as
    // saved. Null when there is none; the relationships are then made without visible lines.
    static ClassJsonNode FindConnectorTemplate(IProject project,IModel owner,StringBuilder log)
    {
        var diagrams=new HashSet<string>(Children(owner).Where(HasClassDiagram).Select(m=>m.Id),StringComparer.Ordinal);
        if(diagrams.Count==0) { log.AppendLine("connector template: no class diagram in the group");return null; }
        string directory=Path.Combine(Path.GetTempPath(),"ClassEditor-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(directory,"snapshot.nmdl");
        try
        {
            Directory.CreateDirectory(directory);
            if(owner.ModelUnit==null) { log.AppendLine("connector template: the group has no model unit");return null; }
            project.UnitManager.ExportModelUnit(owner.ModelUnit,path);
            var exported=ClassJsonNode.Parse(File.ReadAllText(path,new UTF8Encoding(false,true)));
            var editors=exported["Editors"];
            if(editors==null || editors.Items==null) { log.AppendLine("connector template: no Editors in the export");return null; }
            foreach(var e in editors.Items.Where(e=>diagrams.Contains(ClassJsonNode.Value(e,"ModelId")??"")))
            {
                var lines=e["Connectors"];
                if(lines!=null && lines.Items!=null && lines.Items.Count>0) { log.AppendLine("connector template: from editor "+ClassJsonNode.Value(e,"Id"));return lines.Items[0]; }
            }
            log.AppendLine("connector template: the group's class diagrams have no line");
            return null;
        }
        catch(Exception ex) { log.AppendLine("connector template: export failed: "+ex.Message);return null; }
        finally
        {
            try { if(File.Exists(path))File.Delete(path);if(Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory); }
            catch(Exception ex) { log.AppendLine("Temporary export cleanup failed: "+ex.Message); }
        }
    }

    // Undo what the creator made when the sync did not go through. The saved file still holds
    // the diagram until the project is saved again.
    static string Remove(IProject project,IModel diagramModel,List<Placed> placed,StringBuilder log)
    {
        if(project==null || diagramModel==null)return "";
        var made=placed.Where(x=>x.Created && x.Model!=null && !x.Model.IsDeleted).ToList();
        var transaction=project.BeginUndoTransaction(false);
        try
        {
            foreach(var p in made)p.Model.Delete();
            if(!diagramModel.IsDeleted)diagramModel.Delete();
            transaction.Commit();
            log.AppendLine("removed the new diagram and "+made.Count+" created classes");
            return "作成した図と、そのために作ったクラス "+made.Count+" 件を削除しました。保存済みのファイルには残っているので、上書き保存すると削除が確定します。";
        }
        catch(Exception ex)
        {
            try { transaction.Rollback(); } catch(Exception) { }
            log.AppendLine("removal failed: "+ex);
            return "作成した図「"+Name(diagramModel)+"」を削除できませんでした。不要なら手動で削除してください。";
        }
    }

    static void SelectInNavigator(IApplication app,IModel model,StringBuilder log)
    {
        try { app.Window.EditorPage.CurrentNavigator.Select(model,false);log.AppendLine("selected in navigator"); }
        catch(Exception ex) { log.AppendLine("navigator select failed: "+ex.Message); }
    }
}
