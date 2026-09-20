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
            int order=0;
            foreach(var n in nodes)
            {
                if(n.Kind=="activate")
                {
                    var e=new SequenceElement{Id="e"+(next++),Kind="execution",Parent=parent,Order=order++,Line=n.Line};
                    e.Links["participant"]=new[]{aliases[n.Left]};e.Attributes["endParent"]=parent;
                    e.Attributes["start"]=n.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    result.Elements.Add(e);
                    if(!active.ContainsKey(n.Left))active[n.Left]=new Stack<SequenceElement>();
                    if(active[n.Left].Count>0)e.Links["outer"]=new[]{active[n.Left].Peek().Id};
                    active[n.Left].Push(e);continue;
                }
                if(n.Kind=="deactivate")
                {
                    if(!active.ContainsKey(n.Left) || active[n.Left].Count==0)
                        throw new InvalidOperationException("S202: "+n.Line+"行目のdeactivateに対応する開始がありません。");
                    var e=active[n.Left].Pop();e.Attributes["endParent"]=parent;
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
                if(n.Kind=="fragment") {item.Attributes["operator"]=n.Operator;if(n.Operator!="group")item.Text="";}
                if(n.Kind=="note" || n.Kind=="ref") { item.Links["targets"]=n.Targets.Select(t=>aliases[t]).ToArray();if(n.Kind=="note")item.Attributes["position"]=n.Operator; }
                if(n.Kind=="destroy" || n.Kind=="create")item.Links["participant"]=new[]{aliases[n.Left]};
                result.Elements.Add(item);visit(n.Children,item.Id);
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
    { return SequencePayload.Q(Text(e.Text))+string.Join("",e.Attributes.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>SequencePayload.Q(p.Key)+SequencePayload.Q(p.Value))); }
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
                key+="["+string.Join("",doc.Elements.Where(n=>n.Parent==e.Id).OrderBy(n=>n.Order).Select(n=>SequencePayload.Q(get(n))))+"]";
            keys.Add(e.Id,key);return key;
        };
        foreach(var e in doc.Elements)get(e);return keys;
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
            foreach(var a in desired.Elements.Where(e=>!map.ContainsKey(e.Id)).ToArray())
            {
                if(a.Parent==null || !map.ContainsKey(a.Parent))continue;
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
                    !used.Contains(b[j].Id) && Comparable(a[i],b[j],map) && desiredKeys[a[i].Id]==currentKeys[b[j].Id];
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
        foreach(var a in desired.Elements.Where(e=>!map.ContainsKey(e.Id)).ToArray())
        {
            var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && Comparable(a,b,map) && desiredKeys[a.Id]==currentKeys[b.Id]).ToArray();
            if(candidates.Length==1 && desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Kind==a.Kind && desiredKeys[b.Id]==desiredKeys[a.Id])==1)bind(a,candidates[0]);
        }
        // A single unmatched element of a kind in a corresponding container is an attribute edit.
        progress=true;
        while(progress)
        {
            progress=false;
            foreach(var a in desired.Elements.Where(e=>!map.ContainsKey(e.Id)).ToArray())
            {
                if(a.Parent==null || !map.ContainsKey(a.Parent))continue;
                var candidates=current.Elements.Where(b=>!used.Contains(b.Id) && b.Parent==map[a.Parent] && b.Kind==a.Kind).ToArray();
                if(candidates.Length==1 && desired.Elements.Count(b=>!map.ContainsKey(b.Id) && b.Parent==a.Parent && b.Kind==a.Kind)==1)
                {bind(a,candidates[0]);progress=true;}
            }
        }
        align();
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
            var previous=plan.Expected.Elements.Where(n=>n.Parent==e.Parent && n.Order<e.Order).OrderBy(n=>n.Order)
                .Select(n=>n.Id).Where(retained.Contains).ToArray();
            var oldPrevious=current.Elements.Where(n=>n.Parent==before.Parent && n.Order<before.Order && retained.Contains(n.Id)).OrderBy(n=>n.Order).Select(n=>n.Id).ToArray();
            if(e.Parent!=before.Parent || !previous.SequenceEqual(oldPrevious))plan.Changes.Add(new SequenceChange{Action="move",Id=e.Id,Kind=e.Kind,Line=e.Line});
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
        return (qualified.Length>0?qualified:all.Where(c=>c.Name==text))
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
