// Pure semantic synchronization core. No SDK or filesystem dependencies.
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
    public List<string> AddExecutions=new List<string>();
    public bool Candidate { get { return Reasons.Count==0 && (ReconnectMessages.Count+DeleteExecutions.Count+AddExecutions.Count)>0; } }
    public bool CanCommit(bool reconnect)
    { return Candidate && AddExecutions.Count==0 && DeleteExecutions.Count>0 && (reconnect?ReconnectMessages.Count>0:ReconnectMessages.Count==0); }
    static string[] Link(SequenceElement e,string role)
    { string[] ids;return e.Links.TryGetValue(role,out ids)?ids:new string[0]; }
    static string Comparable(SequenceElement e)
    {
        var copy=e.Copy();copy.Links.Remove("receiveExecution");copy.Line=0;copy.Order=0;
        return new SequenceDocument{Elements=new List<SequenceElement>{copy}}.ToJson();
    }
    // An added execution is only describable when it is a plain receive bar on an
    // existing participant: owned by the interaction, optionally nested in one of that
    // participant's existing bars, and referenced by messages as receiveExecution only.
    static string AddReason(SequenceDocument current,SyncPlan plan,SequenceElement added)
    {
        var before=current.Elements.ToDictionary(e=>e.Id);
        string root=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
        if(added.Parent!=root)return "追加する実行区間の所有先が相互作用ではありません。";
        var participant=Link(added,"participant");
        if(participant.Length!=1 || !before.ContainsKey(participant[0]) || before[participant[0]].Kind!="participant")
            return "追加する実行区間の参加者が既存の参加者ではありません。";
        var outer=Link(added,"outer");
        if(outer.Length>1 || (outer.Length==1 && (!before.ContainsKey(outer[0]) || before[outer[0]].Kind!="execution"
            || !Link(before[outer[0]],"participant").SequenceEqual(participant))))
            return "追加する実行区間の入れ子先が同じ参加者の既存区間ではありません。";
        var known=new[]{"participant","outer","startAfter","endBefore","endContainer"};
        if(added.Links.Keys.Any(key=>!known.Contains(key)))return "追加する実行区間に未対応の接続があります。";
        if(!Link(added,"endContainer").SequenceEqual(new[]{root}))return "追加する実行区間の終了位置が相互作用の直下ではありません。";
        foreach(string key in new[]{"startAfter","endBefore"})
        {
            var anchor=Link(added,key);
            if(anchor.Length>1 || (anchor.Length==1 && !before.ContainsKey(anchor[0])))
                return "追加する実行区間の境界が既存要素を指していません。";
        }
        if(plan.Expected.Elements.Any(e=>e.Parent==added.Id))return "追加する実行区間が他の要素を所有しています。";
        int receivers=0;
        foreach(var e in plan.Expected.Elements)
            foreach(var pair in e.Links)
                if(pair.Value.Contains(added.Id))
                {
                    if(pair.Key!="receiveExecution" || e.Kind!="message")return "追加する実行区間が受信以外から参照されています。";
                    receivers++;
                }
        if(receivers==0)return "追加する実行区間を受信先にするメッセージがありません。";
        return null;
    }
    public static SequenceStructurePreflight Check(SequenceDocument current,SyncPlan plan)
    {
        current.Validate();plan.Expected.Validate();
        var result=new SequenceStructurePreflight();
        var before=current.Elements.ToDictionary(e=>e.Id);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        foreach(var change in plan.Changes.Where(c=>c.Action=="add" && c.Kind=="execution"))
        {
            SequenceElement added;
            if(before.ContainsKey(change.Id) || !after.TryGetValue(change.Id,out added))
            { result.Reasons.Add("L"+change.Line+" 追加する実行区間を期待状態から取得できません。");continue; }
            string why=AddReason(current,plan,added);
            if(why!=null){result.Reasons.Add("L"+change.Line+" "+why);continue;}
            result.AddExecutions.Add(change.Id);
        }
        foreach(var change in plan.Changes)
        {
            if(change.Action=="add" && change.Kind=="execution")continue;
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
            if(oldPorts.Length!=1 || newPorts.Length!=1 || !after.TryGetValue(newPorts[0],out port) || port.Kind!="execution"
                || !(before.ContainsKey(newPorts[0]) || result.AddExecutions.Contains(newPorts[0])))
            { result.Reasons.Add(row+"接続先は既存の実行区間か、この計画で追加する実行区間1件である必要があります。");continue; }
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
            +" / 実行区間追加候補: "+AddExecutions.Count
            +"\n"+(Reasons.Count>0?"全体を停止: "+Reasons.Count+"件の未対応条件":Candidate?"限定範囲の候補あり。既存図での適用・保持検証は未実施です。":"対象の変更なし")
            +"\n"+string.Join("\n",Reasons.Distinct());
    }
    public string ToJson()
    { return PumlBuild.Json(PumlBuild.Obj("Candidate",Candidate,"ReconnectMessages",ReconnectMessages.ToArray(),"DeleteExecutions",DeleteExecutions.ToArray(),
        "AddExecutions",AddExecutions.ToArray(),"Reasons",Reasons.ToArray())); }
}

// One added execution, described so the expected state can be computed without
// reading the export again. Template ids point at an existing execution of the same
// participant; the live SDK values of those templates supply the bar width and the
// endpoint fields that the export does not name.
public sealed class SequenceAddedExecution
{
    public string ModelId, Metaclass, Name, OwnerId, ShapeId, TemplateShapeId, Geometry;
    public string[] RelationIds=new string[0], RelationSources=new string[0], TemplateRelationIds=new string[0];
}

// Prepared files are diagnostic artifacts; they are never imported by this command.
public sealed class SequenceStructurePreparation
{
    public string ReconnectJson, EditorAfterDeleteJson;
    public string[] DeleteIds;
    public string[] ReceiveRelationIds=new string[0];
    public SequenceAddedExecution[] AddedExecutions=new SequenceAddedExecution[0];
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
            checkPort(oldPort,a.Links["receiver"].Single());
            // A bar this plan adds is not in the export yet; the preflight vouched for it.
            if(!gate.AddExecutions.Contains(newPort))checkPort(newPort,b.Links["receiver"].Single());
            var link=find("ReceiveMessage",oldPort,id);
            Require(relations.Count(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"ReceiveMessage" && V(r,"TargetId")==id)==1,"受信接続が一意ではありません。");
            var copy=SequenceJson.Parse(link.ToJsonString());
            copy.Properties["SourceId"]=SequenceJson.Parse(SequencePayload.Q(newPort));
            // The order belongs to the collection the relation is leaving, so carrying it
            // over would ask for a position the destination may not have. Omit it and let
            // the move append, which is what the product does.
            copy.Properties.Remove("SourceIndex");
            // The order belongs to the collection the relation is leaving, so carrying it
            // over would ask for a position the destination may not have. Omit it and let
            // the move append, which is what the product does.
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
        var additions=new List<SequenceAddedExecution>();
        var newEntities=new List<SequenceJson>();
        var newRelations=new List<SequenceJson>();
        var newShapes=new List<SequenceJson>();
        foreach(string id in gate.AddExecutions)
        {
            var wanted=after[id];
            string participant=wanted.Links["participant"].Single();
            var owned=relations.Where(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"OwnedExecutionSpecification" && V(r,"SourceId")==participant).ToArray();
            Require(owned.Length>0,"追加先の参加者に既存の実行区間がないため、新しい区間を組み立てられません。");
            string template=V(owned[0],"TargetId");
            Require(byId.ContainsKey(template) && V(byId[template],"EntityType")=="ExecutionSpecification","実行区間の見本を取得できません。");
            var ownerLink=find("___Interaction_ExecutionSpecification",root,template);
            var entity=SequenceJson.Parse(byId[template].ToJsonString());
            entity.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(id));
            newEntities.Add(entity);
            var relationIds=new List<string>();var relationSources=new List<string>();var templateIds=new List<string>();
            foreach(var origin in new[]{ownerLink,owned[0]})
            {
                var copy=SequenceJson.Parse(origin.ToJsonString());
                string relationId=Guid.NewGuid().ToString();
                copy.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(relationId));
                copy.Properties["TargetId"]=SequenceJson.Parse(SequencePayload.Q(id));
                // Omitted order appends, which is what a new bar needs at both ends.
                copy.Properties.Remove("SourceIndex");copy.Properties.Remove("TargetIndex");
                newRelations.Add(copy);
                relationIds.Add(relationId);relationSources.Add(V(origin,"SourceId"));templateIds.Add(V(origin,"Id"));
            }
            var shapes2=editor.Shapes();
            var templateShapes=shapes2.Where(sh=>V(sh,"ModelId")==template).ToArray();
            Require(templateShapes.Length==1,"実行区間の見本図形を一意に取得できません。");
            var laneShapes=shapes2.Where(sh=>V(sh,"ModelId")==participant).ToArray();
            Require(laneShapes.Length==1,"参加者の図形を一意に取得できません。");
            string shapeId=Guid.NewGuid().ToString();
            var shape=SequenceJson.Parse(templateShapes[0].ToJsonString());
            shape.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(shapeId));
            shape.Properties["ModelId"]=SequenceJson.Parse(SequencePayload.Q(id));
            var geometry=Geometry(wanted,after,editor,laneShapes[0],templateShapes[0]);
            foreach(var pair in geometry)
            {
                Require(shape[pair.Key]!=null,"実行区間の図形に"+pair.Key+"がありません。");
                shape.Properties[pair.Key]=SequenceJson.Parse(Number(pair.Value));
            }
            newShapes.Add(shape);
            additions.Add(new SequenceAddedExecution{ModelId=id,Metaclass=V(entity,"MetamodelId"),Name=V(entity,"Name")??"",
                OwnerId=root,ShapeId=shapeId,TemplateShapeId=V(templateShapes[0],"Id"),
                Geometry=PumlBuild.Json(new[]{Number(geometry["X"]),Number(geometry["Y"]),Number(geometry["Length"])}),
                RelationIds=relationIds.ToArray(),RelationSources=relationSources.ToArray(),TemplateRelationIds=templateIds.ToArray()});
        }
        var patch=SequenceJson.Parse(editor.ImportJson());
        patch["Entities"].Items.AddRange(newEntities);
        patch["Relations"].Items.AddRange(newRelations);
        patch["Relations"].Items.AddRange(changed);
        if(newShapes.Count>0)
        {
            var bars=patch["Editors"].Items.Single()["ExecutionSpecifications"];
            Require(bars!=null && bars.Items!=null,"エディタに実行区間の図形配列がありません。");
            bars.Items.AddRange(newShapes);
        }
        return new SequenceStructurePreparation{ReconnectJson=patch.ToJsonString(),
            EditorAfterDeleteJson=Deleted(editor,newShapes,gate.DeleteExecutions),DeleteIds=gate.DeleteExecutions.ToArray(),
            AddedExecutions=additions.ToArray(),
            ReceiveRelationIds=relations.Where(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"ReceiveMessage").Select(r=>V(r,"Id")).ToArray()};
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
    // The generator places a bar at the lane centre and steps 8px right per nesting
    // level. Reuse that rule so an added bar lands where a generated one would.
    static Dictionary<string,double> Geometry(SequenceElement wanted,Dictionary<string,SequenceElement> after,
        SequenceEditorDocument editor,SequenceJson lane,SequenceJson templateShape)
    {
        int depth=0;
        for(var at=wanted;;depth++)
        {
            var outer=at.Links.ContainsKey("outer")?at.Links["outer"]:new string[0];
            if(outer.Length==0)break;
            if(depth>32 || !after.ContainsKey(outer[0]))throw new InvalidOperationException("S220: 入れ子の階層を解決できません。");
            at=after[outer[0]];
        }
        var shapes=editor.Shapes();
        Func<string,SequenceJson> shapeOf=id=>{
            var found=shapes.Where(sh=>SequenceEditorDocument.Value(sh,"ModelId")==id).ToArray();
            return found.Length==1?found[0]:null;
        };
        var receivers=after.Values.Where(e=>e.Kind=="message" && e.Links.ContainsKey("receiveExecution")
            && e.Links["receiveExecution"].Contains(wanted.Id)).ToArray();
        if(receivers.Length==0)throw new InvalidOperationException("S220: 追加する実行区間の開始位置を決められません。");
        double top=double.MaxValue;
        foreach(var message in receivers)
        {
            var shape=shapeOf(message.Id);
            if(shape==null)throw new InvalidOperationException("S220: 受信メッセージの図形を取得できません。");
            top=Math.Min(top,Read(shape,"TargetY"));
        }
        double bottom=top+Read(templateShape,"Length");
        var following=wanted.Links.ContainsKey("endBefore")?wanted.Links["endBefore"]:new string[0];
        if(following.Length==1)
        {
            var shape=shapeOf(following[0]);
            if(shape!=null && shape["SourceY"]!=null)bottom=Read(shape,"SourceY")-16;
        }
        else if(wanted.Links.ContainsKey("outer") && wanted.Links["outer"].Length==1)
        {
            var shape=shapeOf(wanted.Links["outer"][0]);
            if(shape!=null)bottom=Read(shape,"Y")+Read(shape,"Length");
        }
        double length=Math.Max(40,bottom-top);
        var result=new Dictionary<string,double>();
        result["X"]=Read(lane,"X")+Read(lane,"Width")/2+8*depth;
        result["Y"]=top;result["Length"]=length;result["Height"]=length;
        return result;
    }
    static string Deleted(SequenceEditorDocument editor,List<SequenceJson> addedShapes,List<string> removed)
    {
        var json=SequenceJson.Parse(editor.Without(removed).ImportJson());
        if(addedShapes.Count>0)
        {
            var bars=json["Editors"].Items.Single()["ExecutionSpecifications"];
            if(bars==null || bars.Items==null)throw new InvalidOperationException("S220: 削除後のエディタに実行区間の図形配列がありません。");
            bars.Items.AddRange(addedShapes.Select(sh=>SequenceJson.Parse(sh.ToJsonString())));
        }
        return json.ToJsonString();
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
    public SequenceTrialState Expected(SequenceStructurePreparation prepared,SyncPlan plan,bool delete)
    {
        var result=new SequenceTrialState{Models=new Dictionary<string,string>(Models),Shapes=new Dictionary<string,string>(Shapes),ShapeModels=new Dictionary<string,string>(ShapeModels),
            Relations=Relations.ToDictionary(p=>p.Key,p=>p.Value.ToArray()),Ports=Ports.ToDictionary(p=>p.Key,p=>p.Value.ToArray()),
            RelationFields=new Dictionary<string,string>(RelationFields)};
        foreach(var add in prepared.AddedExecutions)
        {
            result.Models[add.ModelId]=PumlBuild.Json(new[]{add.Metaclass,add.Name,add.OwnerId,"False"});
            for(int i=0;i<add.RelationIds.Length;i++)
            {
                string field=result.Field(add.TemplateRelationIds[i]),origin=add.RelationSources[i];
                if(field.Length==0)throw new InvalidOperationException("S230: 追加する関連の種別情報が不足しています。");
                int index=result.Relations.Count(pair=>pair.Value[0]==origin && result.Field(pair.Key)==field);
                result.Relations[add.RelationIds[i]]=new[]{origin,add.ModelId,
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),"0"};
                result.RelationFields[add.RelationIds[i]]=field;
            }
            string sample;
            if(!result.Shapes.TryGetValue(add.TemplateShapeId,out sample))throw new InvalidOperationException("S230: 実行区間の見本図形がありません。");
            var measured=SequenceJson.Parse(sample);var wanted=SequenceJson.Parse(add.Geometry);
            if(measured.Items==null || measured.Items.Count!=5 || wanted.Items==null || wanted.Items.Count!=3)
                throw new InvalidOperationException("S230: 実行区間の図形の項目数が想定と違います。");
            // Width is not serialized for a bar, so take the one the product already uses.
            result.Shapes[add.ShapeId]=PumlBuild.Json(new[]{wanted.Items[0].StringValue(),wanted.Items[1].StringValue(),
                measured.Items[2].StringValue(),wanted.Items[2].StringValue(),wanted.Items[2].StringValue()});
            result.ShapeModels[add.ShapeId]=add.ModelId;
        }
        var patch=SequenceJson.Parse(prepared.ReconnectJson);
        foreach(var r in patch["Relations"].Items)
        {
            string id=r["Id"].StringValue(),source=r["SourceId"].StringValue(),target=r["TargetId"].StringValue();
            if(prepared.AddedExecutions.Any(a=>a.RelationIds.Contains(id)))continue;
            if(!result.Relations.ContainsKey(id) || result.Relations[id][1]!=target || !result.Ports.ContainsKey(target))throw new InvalidOperationException("S230: 変更前の受信関連が一致しません。");
            // SourceIndex belongs to the source endpoint collection, not to the relationship identity.
            // Omitted indices append on import. An explicit index inserts at that position.
            var previous=result.Relations[id];
            if(!prepared.ReceiveRelationIds.Contains(id))throw new InvalidOperationException("S230: 受信関連の種別情報が不足しています。");
            var oldPeers=prepared.ReceiveRelationIds.Where(k=>result.Relations.ContainsKey(k) && k!=id && result.Relations[k][0]==previous[0]).ToArray();
            int oldIndex=int.Parse(previous[2],System.Globalization.CultureInfo.InvariantCulture);
            foreach(string peer in oldPeers)
            {int index=int.Parse(result.Relations[peer][2],System.Globalization.CultureInfo.InvariantCulture);if(index>oldIndex)result.Relations[peer][2]=(index-1).ToString(System.Globalization.CultureInfo.InvariantCulture);}
            var newPeers=prepared.ReceiveRelationIds.Where(k=>result.Relations.ContainsKey(k) && k!=id && result.Relations[k][0]==source).ToArray();
            int insertion=r["SourceIndex"]==null?newPeers.Length:int.Parse(r["SourceIndex"].Raw,System.Globalization.CultureInfo.InvariantCulture);
            if(insertion<0 || insertion>newPeers.Length)throw new InvalidOperationException("S230: 受信関連の挿入順序が範囲外です。");
            foreach(string peer in newPeers)
            {int index=int.Parse(result.Relations[peer][2],System.Globalization.CultureInfo.InvariantCulture);if(index>=insertion)result.Relations[peer][2]=(index+1).ToString(System.Globalization.CultureInfo.InvariantCulture);}
            result.Relations[id]=new[]{source,target,insertion.ToString(System.Globalization.CultureInfo.InvariantCulture),previous[3]};
            result.Ports[target][1]=source;
            result.Ports[target][3]=plan.Expected.Elements.Single(e=>e.Id==target).Links["receiver"].Single();
        }
        if(delete)
        {
            var removed=new HashSet<string>(prepared.DeleteIds);
            foreach(string id in removed)result.Models.Remove(id);
            // Measured on the product: deleting a model closes the gap it leaves in the
            // source/field collection that held it. Relations in other fields keep their index.
            foreach(string id in result.Relations.Where(p=>removed.Contains(p.Value[0]) || removed.Contains(p.Value[1])).Select(p=>p.Key).ToArray())
            {
                string source=result.Relations[id][0],field=result.Field(id);
                int gap=int.Parse(result.Relations[id][2],System.Globalization.CultureInfo.InvariantCulture);
                result.Relations.Remove(id);result.RelationFields.Remove(id);
                foreach(string peer in result.Relations.Where(p=>p.Value[0]==source && result.Field(p.Key)==field).Select(p=>p.Key).ToArray())
                {
                    int index=int.Parse(result.Relations[peer][2],System.Globalization.CultureInfo.InvariantCulture);
                    if(index>gap)result.Relations[peer][2]=(index-1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            foreach(string id in result.ShapeModels.Where(p=>removed.Contains(p.Value)).Select(p=>p.Key).ToArray()){result.Shapes.Remove(id);result.ShapeModels.Remove(id);}
            if(result.Ports.Values.Any(p=>removed.Contains(p[0]) || removed.Contains(p[1])))throw new InvalidOperationException("S230: 削除区間への接続が残っています。");
        }
        return result;
    }
}
