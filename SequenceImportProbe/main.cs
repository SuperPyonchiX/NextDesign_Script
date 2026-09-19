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

public void CreateMinimalSequence(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App); }
public void ImportPlantUml(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Run(context.App, true); }
public void ShowSequenceResult(ICommandContext context, ICommandParams parameters) { SequenceExperiment.Show(context.App); }
public void ShowSequenceDetails(ICommandContext context, ICommandParams parameters) { context.App.Window.UI.ShowInformationDialog(SequenceExperiment.Details, SequenceExperiment.Title); }

public static class SequenceExperiment
{
    public const string Title = "シーケンス生成実験 / 0.3.3";
    public static string Summary = "シーケンス図を開き「PlantUMLを取り込む」または「最小図を生成」を押してください。";
    public static string Details = "まだ実行していません。";
    public static void Show(IApplication app) { app.Window.UI.ShowInformationDialog(Summary, Title); }

    public static void Run(IApplication app) { Run(app, false); }
    public static void Run(IApplication app, bool fromPlantUml)
    {
        string stage = "事前検査", directory = null, rootId = null;
        bool called = false, committed = false, rolledBack = false;
        string apiState = "未取得";
        int apiIssues = 0;
        var detail = new StringBuilder();
        IUndoTransaction transaction = null;
        var completion = new SequenceCompletion();
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
                plan = PumlPlan.Parse(pumlText);
            }
            var owner = sample.Owner;
            var ownerField = sample.GetOwnerField();
            if (owner == null || ownerField == null || !owner.IsEditable || owner.IsDeleted || owner.IsProxy)
                throw new InvalidOperationException("E102: 新しい図を置く親モデルを取得できないか、編集できません。");
            if (!app.Window.UI.ShowConfirmDialog("実プロジェクトのコピーを開いていますか？\n新しい検証用シーケンス図を同じ親に追加する実験です。\n既存図の内容は入力にコピーしません。自動保存しません。\n失敗時はトランザクションの取消を試みますが、実機での復元動作は未確認です。", Title)) return;
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
            var payload = SequencePayload.Build(sources.Select(m => m.Metaclass.Id).ToArray(), diagram.EditorDefinition.Id, schema);
            if (plan != null)
            {
                stage = "PlantUML生成データの構築";
                payload = PumlBuild.Build(plan, PumlRuntime.Profile(diagram, sources, plan), diagram.EditorDefinition.Id, schema);
                Write(Path.Combine(directory, "source.puml"), pumlText);
            }
            rootId = payload.Ids[0];
            detail.AppendLine("schema=" + schema + "; source=" + (fromFile ? "project header" : "public sample hypothesis"));
            detail.AppendLine("SDK=" + typeof(IProject).Assembly.FullName);
            detail.AppendLine("parent=" + ownerId + "; field=" + ownerField.Name + "; source=" + sampleId);
            stage = "生成データの記録";
            Write(Path.Combine(directory, "input.json"), payload.Json);
            Write(Path.Combine(directory, "before.txt"), detail.ToString());
            if (!app.Window.UI.ShowConfirmDialog("新しい図「" + payload.Name + "」を追加します。\n" + (plan == null ? "A → B : probe()\nライフライン2本・同期メッセージ1本" : plan.Summary()) + "\n入力データの記録: 済み\n続けますか？", Title))
            { Summary = "キャンセル / インポートAPI呼出: なし"; Show(app); return; }
            var current = app.Workspace.CurrentProject;
            if (current == null || current.Id != projectId || !string.Equals(current.Path, projectPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("E109: 入力中にプロジェクトが切り替わりました。");
            owner = current.GetModelById(ownerId);
            var fresh = current.GetModelById(sampleId);
            if (owner == null || owner.IsDeleted || owner.IsProxy || !owner.IsEditable || fresh == null || fresh.IsDeleted
                || fresh.Owner == null || fresh.Owner.Id != ownerId || fresh.GetOwnerField().Name != ownerField.Name)
                throw new InvalidOperationException("E110: 作成先が変わりました。");
            foreach (string id in payload.Ids)
                if (current.GetModelById(id) != null) throw new InvalidOperationException("E111: 生成IDが既存モデルと衝突しました。");
            stage = "トランザクション開始";
            transaction = current.BeginUndoTransaction(false);
            if (transaction == null) throw new InvalidOperationException("E117: トランザクションを開始できませんでした。");
            stage = "JSONインポート";
            called = true;
            var result = current.ImportUnitFromJson(payload.Json, owner, ownerField.Name);
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
            // Require a successful journal write before committing; failures enter rollback.
            Write(Path.Combine(directory, "checked.txt"), detail + "\nモデル・送受信・シェイプ照合: 一致\ncommit: 未実行");
            stage = "確定";
            completion.Commit(delegate { transaction.Commit(); }); committed = true;
            Summary = "ケース: " + (fromPlantUml ? "IMPORT001" : "CREATE001") + " / モデル・シェイプ照合: 一致\n新しい図: " + payload.Name
                + "\n" + (plan == null ? "ライフライン: 2 / メッセージ: 1" : plan.Summary()) + "\n確定: 済み / プロジェクト保存: していません\n親モデルの配下で新しい図を開き、図とこの画面を撮影してください。\n図表示・Undo/Redo・再読込: 未確認";
        }
        catch (Exception ex)
        {
            detail.AppendLine(ex.ToString());
            if (transaction != null && !committed)
            {
                try { completion.Cancel(delegate { transaction.Rollback(); }); rolledBack = true; }
                catch (Exception rollbackError) { detail.AppendLine("ROLLBACK: " + rollbackError); }
            }
            Summary = "ケース: " + (fromPlantUml ? "IMPORT001" : "CREATE001") + " / 停止段階: " + stage
                + "\nインポートAPI呼出: " + (called ? "あり" : "なし")
                + "\nAPI結果: " + apiState + " / 診断件数: " + apiIssues
                + "\n取消API: " + (rolledBack ? "正常終了（復元は未確認）" : transaction == null ? "未呼出" : "未確認・失敗")
                + "\n理由: " + (ex.Message.StartsWith("E1", StringComparison.Ordinal) ? ex.Message : ex.GetType().Name)
                + "\nこの画面を撮影してください。詳細は「診断表示」で確認できます。"
                + (called ? "\n再実行前にコピーを開き直してください。" : "\nこのコマンドによるモデル変更はありません。");
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
        if(plan.All().Any(n=>n.Right=="]"))
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
        foreach(var e in p.Expected)
        {
            var model=project.GetModelById(e.Id); Require(model!=null && !model.IsDeleted,e.Kind+"モデル");
            if(e.Kind=="sync" || e.Kind=="async" || e.Kind=="reply")
            {
                var m=model as IMessage;
                Require(m!=null && m.Kind==e.Kind && m.Name==e.Text && (e.Left==null ? m.Sender==null && m.SendPortType=="Frame" : m.Sender!=null && m.Sender.Id==e.Left) && (e.Right==null ? m.Receiver==null && m.ReceivePortType=="MessageEnd" && m.IsLost : m.Receiver!=null && m.Receiver.Id==e.Right),"メッセージ種別・本文・送受信");
                Require(m.SendPort!=null && m.ReceivePort!=null && ((IModel)m.SendPort).Id==e.SendPort && ((IModel)m.ReceivePort).Id==e.ReceivePort,"実行区間・メッセージ端・フレームへの接続");
                var shape=d.Messages.SingleOrDefault(v=>v.Model.Id==e.Id);
                Require(shape!=null && Math.Abs(shape.SourceY-e.Y)<1 && Math.Abs(shape.TargetY-e.EndY)<1,"メッセージ位置・折返し");
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
public class SequencePayload
{
    public PumlProfile ImportProfile;
    public List<PumlExpected> Expected = new List<PumlExpected>();
    public const string Prefix = "System.Behavior.Interaction.";
    public static readonly string[] RelationTypes = { "___Interaction_Frame", "___Interaction_Lifeline", "___Interaction_ExecutionSpecification", "___Interaction_Message", "OwnedExecutionSpecification", "SendMessage", "ReceiveMessage" };
    public string[] Ids;
    public string Name, Json;
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
    public List<string> Aliases = new List<string>(), Names = new List<string>();
    public List<PumlNode> Nodes = new List<PumlNode>();
    public IEnumerable<PumlNode> All() { return Walk(Nodes); }
    public static IEnumerable<PumlNode> Walk(IEnumerable<PumlNode> nodes)
    { foreach (var n in nodes) { yield return n; foreach (var c in Walk(n.Children)) yield return c; } }
    public string Summary()
    {
        var a = All().ToList();
        return "ライフライン: " + Aliases.Count + " / 同期: " + a.Count(n => n.Kind == "sync") + " / 非同期: " + a.Count(n => n.Kind == "async") + " / 返信: " + a.Count(n=>n.Kind=="reply") + " / 図外宛て: " + a.Count(n=>n.Right=="]") + " / 図外から: " + a.Count(n=>n.Left=="[")
            + "\n複合フラグメント: " + a.Count(n => n.Kind == "fragment") + " / ref: " + a.Count(n => n.Kind == "ref") + " / Note: " + a.Count(n => n.Kind == "note")
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
    public static PumlPlan Parse(string input)
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
            var activity=Regex.Match(s,@"^(activate|deactivate)\s+([\p{L}\p{N}_]+)$");
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
        ValidateActivities(p.Nodes,new Dictionary<string,int>());
        return p;
    }
    static void ValidateActivities(IEnumerable<PumlNode> nodes,Dictionary<string,int> counts)
    {
        var initial=new Dictionary<string,int>(counts);
        foreach(var n in nodes)
        {
            if(n.Kind=="fragment")foreach(var branch in n.Children)ValidateActivities(branch.Children,new Dictionary<string,int>(counts));
            if(n.Kind!="activate" && n.Kind!="deactivate")continue;
            int value; counts.TryGetValue(n.Left,out value);
            int baseline; initial.TryGetValue(n.Left,out baseline);
            if(n.Kind=="deactivate" && value<=baseline)throw Error(n.Line,"対応するactivateが同じ図または分岐内にありません。");
            counts[n.Left]=value+(n.Kind=="activate"?1:-1);
        }
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
    private string Entity(string kind,string text,Dictionary<string,object> fields=null)
    {
        string id=Guid.NewGuid().ToString(); if(fields==null)fields=Obj("Name",text);
        entities.Add(Obj("Id",id,"EntityType",kind,"MetamodelId",profile.Types[kind],"Name",text,"Fields",fields)); return id;
    }
    private void Link(string kind,string source,string target,bool embed=false,int targetIndex=-1)
    {
        if(!profile.Relations.ContainsKey(kind))throw new InvalidOperationException("E121: 構造関連を取得できません: "+kind);
        string key=kind+source; int order; indexes.TryGetValue(key,out order); indexes[key]=order+1;
        relations.Add(Obj("Id",Guid.NewGuid().ToString(),"RelationType",embed?"Embed":"Ref","MetamodelId",profile.Relations[kind],"SourceId",source,"TargetId",target,"SourceIndex",order,"TargetIndex",targetIndex));
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
        executions[id]=Shape("ExecutionSpecifications",id,"X",x[alias]+8*(activities.ContainsKey(alias)?activities[alias].Count:0),"Y",start,"Length",40,"Height",40); return id;
    }
    private void Extend(string id,int at)
    { var s=executions[id]; int size=Math.Max((int)s["Length"],at-(int)s["Y"]+35); s["Length"]=size; s["Height"]=size; }
    private void Items(IEnumerable<PumlNode> nodes,string operand=null,int depth=0)
    {
        var items=nodes.ToList();
        for(int index=0;index<items.Count;index++)
        {
            var n=items[index];
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
                string send; if(incoming)send=frameId; else if(!active.TryGetValue(n.Left,out send))active[n.Left]=send=Execution(n.Left,y-20);
                bool outgoing=n.Right=="]"; bool self=n.Left==n.Right; int targetY=y+(self?24:0);
                string receive;
                bool beginsActivation=index+1<items.Count && items[index+1].Kind=="activate" && items[index+1].Left==n.Right;
                if(outgoing)
                {
                    receive=Entity("MessageEnd",""); Owned("MessageEnds",receive);
                    int endX=(int)executions[send]["X"]-60;
                    Shape("MessageEnds",receive,"X",endX,"Y",targetY-5,"Width",10,"Height",10);
                    payload.Expected.Add(new PumlExpected{Id=receive,Kind="messageEnd",Y=targetY-5,X=endX});
                }
                else if((n.Kind=="reply" || (!beginsActivation && activities.ContainsKey(n.Right) && activities[n.Right].Count>0)) && active.TryGetValue(n.Right,out receive)) { }
                else receive=Execution(n.Right,targetY);
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
                    y+=40+18*(branch.Text.Split('\n').Length-1); Items(branch.Children,oid,depth+1); active=saved; pendingAlias=null; y+=8;
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
    public static SequencePayload Build(PumlPlan plan,PumlProfile profile,string definition,string schema)
    {
        var b=new PumlBuild{profile=profile,payload=new SequencePayload()}; var p=b.payload; p.ImportProfile=profile;
        string root=b.Entity("Interaction",plan.Title); p.Ids=new[]{root}; p.Name=plan.Title;
        string frame=b.Entity("Frame",plan.Title); b.frameId=frame; b.Owned("Frame",frame);
        foreach(var alias in plan.Aliases)
        {
            int index=plan.Aliases.IndexOf(alias); string id=b.Entity("Lifeline",plan.Names[index]); b.lifelines[alias]=id; b.x[alias]=240+240*index;
            b.Owned("Lifelines",id); b.Shape("Lifelines",id,"X",b.x[alias]-50,"Width",100,"LeftPadding",index==0?190:140,"LaneLength",300);
            p.Expected.Add(new PumlExpected{Id=id,Kind="lifeline",Text=plan.Names[index]});
        }
        b.Items(plan.Nodes);
        foreach(var pair in b.executions)p.Expected.Add(new PumlExpected{Id=pair.Key,Kind="execution",Y=(int)pair.Value["Y"],EndY=(int)pair.Value["Y"]+(int)pair.Value["Length"]});
        foreach(var s in b.shapes["Lifelines"].Cast<Dictionary<string,object>>())s["LaneLength"]=b.y+40;
        var editor=Obj("Id",Guid.NewGuid().ToString(),"ViewType","SequenceDiagram","MetamodelId","DensoCreate.Indio.IMF.Extensions.Sequence.ViewInstance.SequenceDiagramViewInstance","DefinitionId",definition,"ModelId",root,"Frame",Obj("Id",Guid.NewGuid().ToString(),"ModelId",frame));
        foreach(var pair in b.shapes)editor.Add(pair.Key,pair.Value);
        p.Ids=b.entities.Cast<Dictionary<string,object>>().Select(e=>(string)e["Id"]).ToArray();
        p.Json=Json(Obj("Type","Model","SchemaVersion",schema,"TopElementId",root,"Entities",b.entities,"Relations",b.relations,"Editors",new[]{editor})); return p;
    }
}
