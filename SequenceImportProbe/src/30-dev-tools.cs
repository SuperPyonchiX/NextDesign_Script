// SequenceImportProbe だけの開発用処理: シナリオ一括検証（SequenceBatch）・全図チェック
// （SequenceBatch.Sweep）・保存なし反映の調査・書き戻し調査。同期の本体は PlantUmlTool/src/7x。

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
                // Only the one export needs a saved project; snapshots built from the SDK do not.
                if(!SequenceSyncRuntime.ForceSdkSnapshot)Save(app,project);
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
        // The details are too long for the dialog: the whole text goes next to the scenario list.
        string detailPath=Path.ChangeExtension(list,".detail.txt");
        try{File.WriteAllText(detailPath,SequenceExperiment.Summary+"\n\n"+detail,new UTF8Encoding(false));SequenceExperiment.Summary+="\n\n診断の全文: "+detailPath;}
        catch(Exception ex){SequenceExperiment.Summary+="\n\n診断の書き出しに失敗: "+ex.Message;}
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
    static readonly SortedDictionary<string,int> FieldTypes=new SortedDictionary<string,int>(StringComparer.Ordinal);
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
                    try
                    {
                        var got=m.GetField(f.Name);Put(fields,f.Name,got);
                        string key="型 "+(m.Metaclass==null?"":m.Metaclass.Name)+"."+f.Name+" 宣言="+f.Type+" 値="+(got==null?"null":got.GetType().Name);
                        int n;FieldTypes.TryGetValue(key,out n);FieldTypes[key]=n+1;
                    }
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
                        if(p.Value.Raw!=null && other.Raw!=null && p.Value.Raw.StartsWith("\"")!=other.Raw.StartsWith("\""))
                            add(differs,kind+" "+key+"（JSONの型: 写し="+(p.Value.Raw.StartsWith("\"")?"文字":"数値等")+" / SDK="+(other.Raw.StartsWith("\"")?"文字":"数値等")+"）");
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
            // What GetField hands back for each field that is not text: a number field read as text
            // or as another object is what the import refuses.
            report.AppendLine("項目の値の型（文字列以外、または宣言が文字列以外のもの）:");
            foreach(var pair in FieldTypes.Where(p=>!p.Key.EndsWith(" 宣言=String 値=String",StringComparison.Ordinal)))report.AppendLine("  "+pair.Key+": "+pair.Value);
            FieldTypes.Clear();
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

