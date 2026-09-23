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
    // A new frame drawn around messages that are already there, and those messages in
    // the order they end up in. The frame and its operands are also in AddFragments and
    // AddOperands; the messages move into it without being recreated.
    public List<string> DeleteNotes=new List<string>();
    public List<string> AddNotes=new List<string>();
    public List<string> DeleteRefs=new List<string>();
    public List<string> AddRefs=new List<string>();
    public List<string> WrapFragments=new List<string>();
    public List<string> MoveMessages=new List<string>();
    public int Targets { get { return ReconnectMessages.Count+DeleteExecutions.Count+AddExecutions.Count
        +AddParticipants.Count+DeleteParticipants.Count+DeleteMessages.Count+AddMessages.Count
        +DeleteFragments.Count+DeleteOperands.Count+AddFragments.Count+AddOperands.Count+MoveMessages.Count+DeleteNotes.Count+AddNotes.Count+DeleteRefs.Count+AddRefs.Count; } }
    public bool Candidate { get { return Reasons.Count==0 && Targets>0; } }
    // The deletion-only mode stays exactly as the product confirmed it. The other mode
    // covers a receiver change together with deletions, additions, or both.
    // One button commits every supported change, a deletion alone included.
    public bool CanCommit()
    {
        return Candidate && Targets>0;
    }
    // A fragment goes only as a whole: its operands and everything inside them have to
    // be leaving in the same plan, so nothing is left without a place to live.
    // Document order across owners, so "goes last" means the same thing for a message
    // at the top level and for one inside a new frame. Bars are stored, not sequenced.
    internal static string[] Flatten(SequenceDocument doc)
    {
        var order=new List<string>();
        Action<string> walk=null;
        walk=parent=>{
            foreach(var e in doc.Elements.Where(n=>n.Parent==parent && n.Kind!="participant" && n.Kind!="execution").OrderBy(n=>n.Order))
            {order.Add(e.Id);walk(e.Id);}
        };
        walk(doc.Elements.Single(e=>e.Kind=="interaction").Id);
        return order.ToArray();
    }
    static string Appended(SequenceDocument current,SyncPlan plan,string id,string what)
    {
        var order=Flatten(plan.Expected);
        int at=Array.IndexOf(order,id);
        if(at<0)return what+"が図の並びに現れません。";
        var existing=new HashSet<string>(current.Elements.Select(e=>e.Id));
        for(int i=at+1;i<order.Length;i++)
            if(existing.Contains(order[i]))return what+"が末尾ではありません。途中への挿入は後続の移動になるため対象外です。";
        return null;
    }
    // A new frame is appended whole: the frame, its operands and everything in them are
    // all new. Wrapping existing messages would move them into the frame, which is a
    // different change and not handled here.
    static string FragmentAddReason(SequenceDocument current,SyncPlan plan,SequenceElement added,HashSet<string> adding)
    {
        var before=current.Elements.ToDictionary(e=>e.Id);
        string root=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
        if(added.Parent!=root)return "追加するフラグメントの所有先が相互作用ではありません。入れ子のフラグメントは対象外です。";
        if(added.Links.Count>0)return "追加するフラグメントに未対応の接続があります。";
        var inside=new List<SequenceElement>();var pending=new List<string>{added.Id};
        for(int i=0;i<pending.Count;i++)
        {
            if(pending.Count>500)return "追加するフラグメントの入れ子が深すぎます。";
            foreach(var child in plan.Expected.Elements.Where(e=>e.Parent==pending[i])){inside.Add(child);pending.Add(child.Id);}
        }
        foreach(var child in inside)
        {
            if(before.ContainsKey(child.Id))
                return "追加するフラグメントの中に既存の要素があります。既存のメッセージを枠で囲む変更は対象外です。";
            if(child.Kind!="operand" && child.Kind!="message" && child.Kind!="execution")
                return "追加するフラグメントの中に"+child.Kind+"があるため対象外です。オペランド・メッセージ・実行区間だけを扱います。";
            if(!adding.Contains(child.Id))return "追加するフラグメントの中に、この計画で追加しない要素があります。";
        }
        // A frame needs at least one lane to span.
        if(!current.Elements.Any(e=>e.Kind=="participant"))return "図に参加者がないため枠を配置できません。";
        var operands=inside.Where(e=>e.Kind=="operand" && e.Parent==added.Id).ToArray();
        if(operands.Length==0)return "追加するフラグメントにオペランドがありません。";
        // The product cannot lay out a frame that encloses no message.
        foreach(var operand in operands)
            if(!plan.Expected.Elements.Any(e=>e.Kind=="message" && e.Parent==operand.Id))
                return "メッセージのないオペランドがあります。空の枠は図形を作れません。";
        return Appended(current,plan,added.Id,"追加するフラグメント");
    }
    static string OperandAddReason(SequenceDocument current,SyncPlan plan,SequenceElement added,HashSet<string> adding)
    {
        if(added.Parent==null || !adding.Contains(added.Parent))
            return "オペランド単独の追加は対象外です。フラグメントごと追加する場合だけ扱います。";
        SequenceElement owner;
        if(!plan.Expected.Elements.ToDictionary(e=>e.Id).TryGetValue(added.Parent,out owner) || owner.Kind!="fragment")
            return "追加するオペランドの所有先がフラグメントではありません。";
        if(added.Links.Count>0)return "追加するオペランドに未対応の接続があります。";
        return null;
    }
    static string FragmentReason(SequenceDocument current,SyncPlan plan,string id)
    {
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        var inside=new List<SequenceElement>();
        var pending=new List<string>{id};
        for(int i=0;i<pending.Count;i++)
        {
            if(pending.Count>500)return "フラグメントの入れ子が深すぎます。";
            foreach(var child in current.Elements.Where(e=>e.Parent==pending[i]))
            {inside.Add(child);pending.Add(child.Id);}
        }
        foreach(var child in inside)
        {
            if(after.ContainsKey(child.Id))return "フラグメントの中に残す要素があります。中身ごと消える場合だけ対象です。";
            // Executions live inside a frame too; they leave with it like anything else.
            if(child.Kind!="operand" && child.Kind!="message" && child.Kind!="execution")
                return "フラグメントの中に"+child.Kind+"があるため対象外です。オペランド・メッセージ・実行区間だけを扱います。";
        }
        return null;
    }
    static bool Referenced(SyncPlan plan,string id)
    {
        return plan.Expected.Elements.Any(e=>e.Parent==id || e.Links.Values.SelectMany(v=>v).Contains(id));
    }
    // A new message only goes after every existing one, into space the current bars
    // already cover. Inserting between messages would push the rest of the diagram down.
    static string MessageReason(SequenceDocument current,SyncPlan plan,SequenceElement added,List<string> addedExecutions,HashSet<string> adding)
    {
        var before=current.Elements.ToDictionary(e=>e.Id);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        string root=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
        // A message may also go into an operand that is already drawn. Its frame then has
        // to grow, so that is always an insertion, even at the very end of the diagram.
        bool intoFrame=added.Parent!=root && before.ContainsKey(added.Parent) && before[added.Parent].Kind=="operand"
            && after.ContainsKey(added.Parent) && after[added.Parent].Kind=="operand";
        if(added.Parent!=root && !intoFrame && !(adding.Contains(added.Parent) && after.ContainsKey(added.Parent) && after[added.Parent].Kind=="operand"))
            return "追加するメッセージの所有先が相互作用でも、既存のオペランドでも、この計画で追加するオペランドでもありません。";
        var known=new[]{"sender","receiver","sendExecution","receiveExecution"};
        if(added.Links.Keys.Any(key=>!known.Contains(key)))return "追加するメッセージに未対応の接続があります。";
        foreach(var e in plan.Expected.Elements)
        {
            if(e.Parent==added.Id)return "追加するメッセージが他の要素を所有しています。";
            foreach(var pair in e.Links)
                if(pair.Value.Contains(added.Id))
                {
                    // A bar added with the message names it as its own boundary. That is
                    // the frame being built, not an outside reference to the message.
                    if(adding.Contains(e.Id) && e.Kind=="execution" && (pair.Key=="startAfter" || pair.Key=="endBefore"))continue;
                    return "追加するメッセージを参照する要素があります。";
                }
        }
        foreach(string role in new[]{"sender","receiver"})
        {
            var ends=Link(added,role);
            if(ends.Length!=1 || !before.ContainsKey(ends[0]) || before[ends[0]].Kind!="participant")
                return "追加するメッセージの"+role+"が既存の参加者ではありません。図外との送受信は対象外です。";
        }
        foreach(string role in new[]{"sendExecution","receiveExecution"})
        {
            var ports=Link(added,role);
            if(ports.Length!=1)return "追加するメッセージの"+role+"が1件ではありません（"+ports.Length
                +"件）。省略した端点は既存メッセージからしか引き継げません。追加するメッセージは、その端点を含む activate の内側に書いてください。";
            if(!before.ContainsKey(ports[0]) && !addedExecutions.Contains(ports[0]))
                return "追加するメッセージの接続先は既存の実行区間か、この計画で追加する実行区間である必要があります。";
        }
        // Going last needs no room made for it. Anywhere else, everything below has to
        // move down: messages and bars move, a frame below moves whole, and a frame the
        // point falls inside grows, pushing its later operands down. That stays describable
        // for a message placed right after an existing one in the same container, between
        // bars that are already open across that point, so no new bar has to be placed.
        string why=Appended(current,plan,added.Id,"追加するメッセージ");
        if(why!=null || intoFrame)
        {
            if(added.Parent!=root && !intoFrame)return why;
            foreach(string role in new[]{"sendExecution","receiveExecution"})
                if(!before.ContainsKey(Link(added,role)[0]))
                    return "図の途中へ挿入するメッセージは、既に開いている実行区間につないでください。"
                        +"新しい実行区間を同時に作る挿入は対象外です。";
            var walk=Flatten(plan.Expected);
            int at=Array.IndexOf(walk,added.Id);
            // The room is made below the message just above. That only lands in the right
            // container when that message shares it: at the head of an operand, or just
            // after a frame closes, the point would fall on the wrong side of a boundary.
            SequenceElement previous;
            if(at<1 || !after.TryGetValue(walk[at-1],out previous) || previous.Kind!="message"
                || !before.ContainsKey(previous.Id) || previous.Parent!=added.Parent)
                return "挿入するメッセージの直前は、同じ所有先にある既存のメッセージにしてください。"
                    +"オペランドの先頭や、枠の直後への挿入は対象外です。";
            var below=walk.Skip(at+1).Where(before.ContainsKey).Select(id=>after[id]).ToArray();
            if(below.Any(e=>e.Kind!="message" && e.Kind!="fragment" && e.Kind!="operand"))
                return "挿入位置より下に"+below.First(e=>e.Kind!="message" && e.Kind!="fragment" && e.Kind!="operand").Kind
                    +"があります。メッセージと枠だけを下げる挿入に限ります。";
        }
        var order=Flatten(plan.Expected);
        var earlier=order.Take(Array.IndexOf(order,added.Id)).Select(id=>after[id]).Where(e=>e.Kind=="message").ToArray();
        if(earlier.Length==0)return "直前のメッセージがありません。最初のメッセージの追加は対象外です。";
        // Position comes from the message before it; the type and shape come from any
        // existing message of the same sort, so a reply after a call is still describable.
        if(!earlier.Any(e=>before.ContainsKey(e.Id) && Attribute(e)==Attribute(added)))
            return "同じ種別の既存メッセージがないため、見本にできません。";
        return null;
    }
    internal static string Attribute(SequenceElement e)
    { string value;return e.Attributes.TryGetValue("sort",out value)?value:""; }
    // A new lane only goes at the right end, where no existing lane has to move.
    static string ParticipantReason(SequenceDocument current,SyncPlan plan,SequenceElement added)
    {
        string root=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
        if(added.Parent!=root)return "追加する参加者の所有先が相互作用ではありません。";
        if(added.Links.Count>0)return "追加する参加者に未対応の接続があります。";
        if(Referenced(plan,added.Id))return "追加する参加者を参照する要素があります。メッセージや実行区間の追加は別途必要です。";
        var existing=new HashSet<string>(current.Elements.Where(e=>e.Kind=="participant").Select(e=>e.Id));
        if(existing.Count==0)return "既存の参加者がないため、新しい参加者を配置できません。";
        var lanes=plan.Expected.Elements.Where(e=>e.Kind=="participant").OrderBy(e=>e.Order).ToArray();
        if(lanes.Length==0 || lanes[lanes.Length-1].Id!=added.Id)
            return "追加する参加者が右端ではありません。途中への挿入は図全体の再配置になるため対象外です。";
        return null;
    }
    static string[] Link(SequenceElement e,string role)
    { string[] ids;return e.Links.TryGetValue(role,out ids)?ids:new string[0]; }
    // startAfter and endBefore name the neighbouring events; they have no model field of
    // their own. When they differ only because those neighbours are being deleted, the
    // execution itself is unchanged and nothing has to be written.
    static bool AnchorsOnly(SequenceElement old,SequenceElement next,Dictionary<string,SequenceElement> after)
    { return AnchorsOnly(old,next,after,null); }
    // An anchor may also move to a note this plan adds, which now sits between the bar and
    // the event it used to name.
    static bool AnchorsOnly(SequenceElement old,SequenceElement next,Dictionary<string,SequenceElement> after,HashSet<string> notes)
    {
        Func<SequenceElement,string> bare=e=>{
            var copy=e.Copy();copy.Links.Remove("startAfter");copy.Links.Remove("endBefore");copy.Line=0;copy.Order=0;
            return new SequenceDocument{Elements=new List<SequenceElement>{copy}}.ToJson();
        };
        if(bare(old)!=bare(next))return false;
        foreach(string role in new[]{"startAfter","endBefore"})
        {
            var was=Link(old,role);var now=Link(next,role);
            if(was.SequenceEqual(now))continue;
            if(notes!=null && now.Length==1 && notes.Contains(now[0]))continue;
            if(was.Any(id=>after.ContainsKey(id)))return false;
        }
        return true;
    }
    // A bar under a wrap keeps its lane and nesting. Its owner, where it ends and its
    // neighbouring events may change, but only to places that still exist: the
    // interaction or one of the new frame's operands.
    static bool FollowsWrap(SequenceElement old,SequenceElement next,Dictionary<string,SequenceElement> after,string frame)
    {
        Func<SequenceElement,string> bare=e=>{
            var copy=e.Copy();copy.Parent=null;copy.Line=0;copy.Order=0;
            foreach(string key in new[]{"startAfter","endBefore","endContainer"})copy.Links.Remove(key);
            return new SequenceDocument{Elements=new List<SequenceElement>{copy}}.ToJson();
        };
        if(bare(old)!=bare(next))return false;
        Func<string,bool> place=id=>id==old.Parent || (after.ContainsKey(id) && after[id].Kind=="operand" && after[id].Parent==frame);
        if(next.Parent==null || !place(next.Parent) || !Link(next,"endContainer").All(place))return false;
        return Link(next,"startAfter").Concat(Link(next,"endBefore")).All(after.ContainsKey);
    }
    static string Comparable(SequenceElement e)
    {
        var copy=e.Copy();copy.Links.Remove("receiveExecution");copy.Line=0;copy.Order=0;
        return new SequenceDocument{Elements=new List<SequenceElement>{copy}}.ToJson();
    }
    // An added execution is only describable when it is a plain receive bar on an
    // existing participant: owned by the interaction, optionally nested in one of that
    // participant's existing bars, and referenced by messages as receiveExecution only.
    static string AddReason(SequenceDocument current,SyncPlan plan,SequenceElement added,HashSet<string> adding)
    {
        var before=current.Elements.ToDictionary(e=>e.Id);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        string root=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
        // A bar is owned either by the interaction or by an operand of a frame this plan adds.
        Func<string,bool> container=id=>id==root
            || (adding.Contains(id) && after.ContainsKey(id) && after[id].Kind=="operand");
        if(!container(added.Parent))
            return "追加する実行区間の所有先が相互作用でも、この計画で追加するオペランドでもありません。";
        var participant=Link(added,"participant");
        if(participant.Length!=1 || !before.ContainsKey(participant[0]) || before[participant[0]].Kind!="participant")
            return "追加する実行区間の参加者が既存の参加者ではありません。";
        var outer=Link(added,"outer");
        if(outer.Length>1 || (outer.Length==1 && (!after.ContainsKey(outer[0]) || after[outer[0]].Kind!="execution"
            || !Link(after[outer[0]],"participant").SequenceEqual(participant))))
            return "追加する実行区間の入れ子先が同じ参加者の実行区間ではありません。";
        var known=new[]{"participant","outer","startAfter","endBefore","endContainer"};
        if(added.Links.Keys.Any(key=>!known.Contains(key)))return "追加する実行区間に未対応の接続があります。";
        var ends=Link(added,"endContainer");
        if(ends.Length!=1 || !container(ends[0]))
            return "追加する実行区間の終了位置が相互作用でも、この計画で追加するオペランドでもありません。";
        foreach(string key in new[]{"startAfter","endBefore"})
        {
            var anchor=Link(added,key);
            if(anchor.Length>1 || (anchor.Length==1 && !after.ContainsKey(anchor[0])))
                return "追加する実行区間の境界が期待状態の要素を指していません。";
        }
        if(plan.Expected.Elements.Any(e=>e.Parent==added.Id))return "追加する実行区間が他の要素を所有しています。";
        int links=0;
        foreach(var e in plan.Expected.Elements)
            foreach(var pair in e.Links)
                if(pair.Value.Contains(added.Id))
                {
                    if(pair.Key=="outer" && e.Kind=="execution" && adding.Contains(e.Id)){links++;continue;}
                    if(e.Kind!="message" || (pair.Key!="receiveExecution" && pair.Key!="sendExecution"))
                        return "追加する実行区間がメッセージの送受信以外から参照されています。";
                    // Moving an existing message onto a new bar is a reconnection, not an addition.
                    if(pair.Key=="sendExecution" && !adding.Contains(e.Id))
                        return "追加する実行区間を送信元にする既存メッセージがあります。既存メッセージの送信元は変えられません。";
                    links++;
                }
        if(links==0)return "追加する実行区間に接続するメッセージがありません。";
        return null;
    }
    // A new frame around messages already drawn. Only the plainest form: the frame sits
    // directly in the interaction, every operand is new and holds at least one message,
    // and what it holds is a run of existing top-level messages, in the same order, with
    // their bars. Nothing else changes in the same update, since the room this makes
    // moves everything below it.
    static string WrapReason(SequenceDocument current,SyncPlan plan,SequenceElement added,HashSet<string> adding,List<string> moved)
    {
        var before=current.Elements.ToDictionary(e=>e.Id);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        string root=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
        if(added.Parent!=root)return "既存のメッセージを囲む枠は相互作用の直下に置いてください。入れ子の枠で囲む変更は対象外です。";
        if(added.Links.Count>0)return "追加するフラグメントに未対応の接続があります。";
        var operands=plan.Expected.Elements.Where(e=>e.Parent==added.Id).OrderBy(e=>e.Order).ToArray();
        if(operands.Length==0 || operands.Any(o=>o.Kind!="operand" || !adding.Contains(o.Id)))
            return "既存のメッセージを囲む枠のオペランドは、すべて新しく追加するものに限ります。";
        foreach(var operand in operands)
        {
            var inside=plan.Expected.Elements.Where(e=>e.Parent==operand.Id).ToArray();
            foreach(var child in inside)
            {
                if(child.Kind=="execution" && before.ContainsKey(child.Id))continue;
                if(child.Kind!="message" || !before.ContainsKey(child.Id))
                    return "既存のメッセージを囲む枠の中に、新しい要素や"+child.Kind+"があります。囲むだけの変更に限ります。";
                if(before[child.Id].Parent!=root)return "囲むメッセージは相互作用の直下にあるものに限ります。";
            }
            var messages=Flatten(plan.Expected).Where(id=>after[id].Parent==operand.Id).ToArray();
            if(messages.Length==0)return "メッセージのないオペランドがあります。空の枠は図形を作れません。";
            moved.AddRange(messages);
        }
        // The run has to be contiguous and keep its order, or it is a reordering as well.
        var old=Flatten(current);
        int first=Array.IndexOf(old,moved[0]);
        for(int i=0;i<moved.Count;i++)
            if(first<0 || first+i>=old.Length || old[first+i]!=moved[i])
                return "囲むメッセージが元の図で連続していないか、順序が変わっています。囲むだけの変更に限ります。";
        foreach(string id in old.Skip(first+moved.Count))
            if(before[id].Kind!="message" && before[id].Kind!="fragment" && before[id].Kind!="operand")
                return "囲む範囲より下に"+before[id].Kind+"があります。メッセージと枠だけを下げる変更に限ります。";
        // The rest of the top level keeps its order around the new frame.
        var inRun=new HashSet<string>(moved);
        var kept=old.Where(id=>before[id].Parent==root && !inRun.Contains(id)).ToArray();
        var now=Flatten(plan.Expected).Where(id=>after[id].Parent==root && before.ContainsKey(id)).ToArray();
        if(!kept.SequenceEqual(now))return "枠の外の要素の順序も変わっています。囲むだけの変更に限ります。";
        return null;
    }
    // A new note goes right under an existing top-level message and pushes what is below
    // it down, as the generator lays one out. It spans every lane: a new note carries no
    // anchors (SequenceNotePolicy), so there is nothing narrower to place it by.
    // A ref is placed the same way; it spans the lanes it names instead.
    static string NoteAddReason(SequenceDocument current,SyncPlan plan,SequenceElement added)
    {
        var before=current.Elements.ToDictionary(e=>e.Id);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        string root=plan.Expected.Elements.Single(e=>e.Kind=="interaction").Id;
        if(added.Parent!=root)return "追加する"+added.Kind+"の所有先が相互作用ではありません。枠の中への追加は対象外です。";
        if(added.Kind=="ref" && Link(added,"targets").Any(id=>!before.ContainsKey(id) || before[id].Kind!="participant"))
            return "追加するrefの対象が既存の参加者ではありません。";
        // A bar's neighbouring-event anchors may now name the note; they are read from
        // positions and write nothing. Anything else pointing at it is a real reference.
        if(plan.Expected.Elements.Any(e=>e.Parent==added.Id
            || e.Links.Where(p=>!(e.Kind=="execution" && (p.Key=="startAfter" || p.Key=="endBefore"))).SelectMany(p=>p.Value).Contains(added.Id)))
            return "追加する"+added.Kind+"を参照する要素があります。";
        var walk=Flatten(plan.Expected);
        int at=Array.IndexOf(walk,added.Id);
        SequenceElement previous;
        if(at<1 || !after.TryGetValue(walk[at-1],out previous) || previous.Kind!="message" || !before.ContainsKey(previous.Id) || previous.Parent!=root)
            return "追加する"+added.Kind+"の直前は、相互作用直下の既存メッセージにしてください。図の先頭・枠の直後への追加は対象外です。";
        var below=walk.Skip(at+1).Where(before.ContainsKey).Select(id=>after[id]).ToArray();
        if(below.Any(e=>e.Kind!="message" && e.Kind!="fragment" && e.Kind!="operand" && e.Kind!="note"))
            return "追加するNoteより下に"+below.First(e=>e.Kind!="message" && e.Kind!="fragment" && e.Kind!="operand" && e.Kind!="note").Kind
                +"があります。メッセージ・枠・Noteだけを下げる変更に限ります。";
        return null;
    }
    public static SequenceStructurePreflight Check(SequenceDocument current,SyncPlan plan)
    {
        current.Validate();plan.Expected.Validate();
        var result=new SequenceStructurePreflight();
        var before=current.Elements.ToDictionary(e=>e.Id);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        var adding=new HashSet<string>(plan.Changes.Where(c=>c.Action=="add").Select(c=>c.Id));
        foreach(var change in plan.Changes.Where(c=>c.Action=="add" && c.Kind=="fragment"))
        {
            SequenceElement added;
            if(before.ContainsKey(change.Id) || !after.TryGetValue(change.Id,out added))
            { result.Reasons.Add("L"+change.Line+" 追加するフラグメントを期待状態から取得できません。");continue; }
            // A frame holding messages that are already there wraps them rather than adding new ones.
            var holds=new List<string>();var pending=new List<string>{added.Id};
            for(int i=0;i<pending.Count && pending.Count<500;i++)
                foreach(var child in plan.Expected.Elements.Where(e=>e.Parent==pending[i])){holds.Add(child.Id);pending.Add(child.Id);}
            if(holds.Any(before.ContainsKey))
            {
                var moved=new List<string>();
                string reason=WrapReason(current,plan,added,adding,moved);
                if(reason!=null){result.Reasons.Add("L"+change.Line+" "+reason);continue;}
                if(result.WrapFragments.Count>0){result.Reasons.Add("L"+change.Line+" 1回の更新で囲める枠は1つです。");continue;}
                result.AddFragments.Add(change.Id);result.WrapFragments.Add(change.Id);result.MoveMessages.AddRange(moved);
                continue;
            }
            string why=FragmentAddReason(current,plan,added,adding);
            if(why!=null){result.Reasons.Add("L"+change.Line+" "+why);continue;}
            result.AddFragments.Add(change.Id);
        }
        foreach(var change in plan.Changes.Where(c=>c.Action=="add" && c.Kind=="operand"))
        {
            SequenceElement added;
            if(before.ContainsKey(change.Id) || !after.TryGetValue(change.Id,out added))
            { result.Reasons.Add("L"+change.Line+" 追加するオペランドを期待状態から取得できません。");continue; }
            string why=OperandAddReason(current,plan,added,adding);
            if(why!=null){result.Reasons.Add("L"+change.Line+" "+why);continue;}
            result.AddOperands.Add(change.Id);
        }
        foreach(var change in plan.Changes.Where(c=>c.Action=="add" && c.Kind=="message"))
        {
            SequenceElement added;
            if(before.ContainsKey(change.Id) || !after.TryGetValue(change.Id,out added))
            { result.Reasons.Add("L"+change.Line+" 追加するメッセージを期待状態から取得できません。");continue; }
            string why=MessageReason(current,plan,added,
                plan.Changes.Where(c=>c.Action=="add" && c.Kind=="execution").Select(c=>c.Id).ToList(),adding);
            if(why!=null){result.Reasons.Add("L"+change.Line+" "+why);continue;}
            result.AddMessages.Add(change.Id);
        }
        foreach(var change in plan.Changes.Where(c=>c.Action=="add" && (c.Kind=="note" || c.Kind=="ref")))
        {
            SequenceElement added;
            if(before.ContainsKey(change.Id) || !after.TryGetValue(change.Id,out added))
            { result.Reasons.Add("L"+change.Line+" 追加する"+change.Kind+"を期待状態から取得できません。");continue; }
            string why=NoteAddReason(current,plan,added);
            if(why!=null){result.Reasons.Add("L"+change.Line+" "+why);continue;}
            (change.Kind=="note"?result.AddNotes:result.AddRefs).Add(change.Id);
        }
        foreach(var change in plan.Changes.Where(c=>c.Action=="add" && c.Kind=="participant"))
        {
            SequenceElement added;
            if(before.ContainsKey(change.Id) || !after.TryGetValue(change.Id,out added))
            { result.Reasons.Add("L"+change.Line+" 追加する参加者を期待状態から取得できません。");continue; }
            string why=ParticipantReason(current,plan,added);
            if(why!=null){result.Reasons.Add("L"+change.Line+" "+why);continue;}
            result.AddParticipants.Add(change.Id);
        }
        foreach(var change in plan.Changes.Where(c=>c.Action=="add" && c.Kind=="execution"))
        {
            SequenceElement added;
            if(before.ContainsKey(change.Id) || !after.TryGetValue(change.Id,out added))
            { result.Reasons.Add("L"+change.Line+" 追加する実行区間を期待状態から取得できません。");continue; }
            string why=AddReason(current,plan,added,adding);
            if(why!=null){result.Reasons.Add("L"+change.Line+" "+why);continue;}
            result.AddExecutions.Add(change.Id);
        }
        foreach(var change in plan.Changes)
        {
            if(change.Action=="add" && new[]{"execution","participant","message","fragment","operand","note","ref"}.Contains(change.Kind))continue;
            if(change.Action=="delete" && change.Kind=="participant" && before.ContainsKey(change.Id) && !after.ContainsKey(change.Id))
            {
                if(Referenced(plan,change.Id))result.Reasons.Add("L"+change.Line+" 参加者への参照が残るため削除できません。");
                else result.DeleteParticipants.Add(change.Id);
                continue;
            }
            if(change.Action=="delete" && change.Kind=="fragment" && before.ContainsKey(change.Id) && !after.ContainsKey(change.Id))
            {
                string why=FragmentReason(current,plan,change.Id);
                if(why!=null)result.Reasons.Add("L"+change.Line+" "+why);else result.DeleteFragments.Add(change.Id);
                continue;
            }
            if(change.Action=="delete" && change.Kind=="operand" && before.ContainsKey(change.Id) && !after.ContainsKey(change.Id))
            {
                string owner=before[change.Id].Parent;
                if(owner==null || after.ContainsKey(owner))result.Reasons.Add("L"+change.Line+" オペランド単独の削除は対象外です。フラグメントごと消える場合だけ扱います。");
                else result.DeleteOperands.Add(change.Id);
                continue;
            }
            // A note is annotation only: removing it leaves everything else where it is.
            if(change.Action=="delete" && (change.Kind=="note" || change.Kind=="ref") && before.ContainsKey(change.Id) && !after.ContainsKey(change.Id))
            {
                if(Referenced(plan,change.Id))result.Reasons.Add("L"+change.Line+" "+change.Kind+"への参照が残るため削除できません。");
                else if(change.Kind=="ref" && before[change.Id].Parent!=current.Elements.Single(e=>e.Kind=="interaction").Id)
                    result.Reasons.Add("L"+change.Line+" 枠の中のrefの削除は対象外です。");
                else (change.Kind=="note"?result.DeleteNotes:result.DeleteRefs).Add(change.Id);
                continue;
            }
            if(change.Action=="delete" && change.Kind=="message" && before.ContainsKey(change.Id) && !after.ContainsKey(change.Id))
            {
                if(Referenced(plan,change.Id))result.Reasons.Add("L"+change.Line+" メッセージへの参照が残るため削除できません。");
                else result.DeleteMessages.Add(change.Id);
                continue;
            }
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
            // Wrapping moves the messages into the new operands, and the bars on them follow
            // by position. The top-level elements around the frame only look moved because
            // their neighbours left; WrapReason already checked they keep their order.
            if(result.WrapFragments.Count>0 && (change.Action=="move" || (change.Action=="update" && change.Kind=="execution"))
                && before.TryGetValue(change.Id,out old) && after.TryGetValue(change.Id,out next))
            {
                if(change.Kind=="message" && change.Action=="move"
                    && (result.MoveMessages.Contains(change.Id) || old.Parent==next.Parent))continue;
                if(change.Kind=="execution" && FollowsWrap(old,next,after,result.WrapFragments[0]))continue;
                result.Reasons.Add(row+change.Kind+" "+change.Action+"は、枠で囲む変更と同時には扱えません。");continue;
            }
            if(change.Action=="update" && change.Kind=="execution"
                && before.TryGetValue(change.Id,out old) && after.TryGetValue(change.Id,out next))
            {
                if(AnchorsOnly(old,next,after,new HashSet<string>(result.AddNotes.Concat(result.AddRefs))))continue;
                result.Reasons.Add(row+"実行区間の境界以外の変更は今回の構造更新対象外です。");continue;
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
        // Each of these makes room by moving what is below; two in one update would have to
        // agree on where everything goes.
        if(result.AddNotes.Count+result.AddRefs.Count>1 || (result.AddNotes.Count+result.AddRefs.Count>0 && (result.AddMessages.Count+result.AddFragments.Count>0)))
            result.Reasons.Add("Note・refの追加は1件ずつ、メッセージや枠の追加とは分けて反映してください。");
        if(result.WrapFragments.Count>0)
        {
            string frame=result.WrapFragments[0];
            if(result.ReconnectMessages.Count+result.DeleteExecutions.Count+result.AddExecutions.Count+result.AddParticipants.Count
                +result.DeleteParticipants.Count+result.DeleteMessages.Count+result.AddMessages.Count+result.DeleteFragments.Count
                +result.DeleteOperands.Count+result.DeleteNotes.Count+result.AddNotes.Count+result.DeleteRefs.Count+result.AddRefs.Count>0 || result.AddFragments.Count!=1
                || result.AddOperands.Any(id=>after[id].Parent!=frame))
                result.Reasons.Add("既存のメッセージを枠で囲む変更は、ほかの変更と分けて1件ずつ反映してください。");
        }
        // Keep candidates for diagnostics, but never permit applying a supported subset.
        return result;
    }
    public string Summary()
    {
        return "構造更新の事前判定（図への反映なし）\n受信接続変更候補: "+ReconnectMessages.Count+" / 実行区間削除候補: "+DeleteExecutions.Count
            +" / 実行区間追加候補: "+AddExecutions.Count
            +" / 参加者追加候補: "+AddParticipants.Count+" / 参加者削除候補: "+DeleteParticipants.Count
            +" / メッセージ削除候補: "+DeleteMessages.Count+" / メッセージ追加候補: "+AddMessages.Count
            +" / フラグメント削除候補: "+DeleteFragments.Count+" / オペランド削除候補: "+DeleteOperands.Count
            +" / フラグメント追加候補: "+AddFragments.Count+" / オペランド追加候補: "+AddOperands.Count
            +" / 枠で囲むメッセージ候補: "+MoveMessages.Count+" / Note削除候補: "+DeleteNotes.Count+" / Note追加候補: "+AddNotes.Count
            +" / ref削除候補: "+DeleteRefs.Count+" / ref追加候補: "+AddRefs.Count
            +"\n"+(Reasons.Count>0?"全体を停止: "+Reasons.Count+"件の未対応条件":Candidate?"限定範囲の候補あり。既存図での適用・保持検証は未実施です。":"対象の変更なし")
            +"\n"+string.Join("\n",Reasons.Distinct());
    }
    public string ToJson()
    { return PumlBuild.Json(PumlBuild.Obj("Candidate",Candidate,"ReconnectMessages",ReconnectMessages.ToArray(),"DeleteExecutions",DeleteExecutions.ToArray(),
        "AddExecutions",AddExecutions.ToArray(),"AddParticipants",AddParticipants.ToArray(),
        "DeleteParticipants",DeleteParticipants.ToArray(),"DeleteMessages",DeleteMessages.ToArray(),
        "AddMessages",AddMessages.ToArray(),"DeleteFragments",DeleteFragments.ToArray(),
        "DeleteOperands",DeleteOperands.ToArray(),
        "AddFragments",AddFragments.ToArray(),"AddOperands",AddOperands.ToArray(),
        "WrapFragments",WrapFragments.ToArray(),"MoveMessages",MoveMessages.ToArray(),"DeleteNotes",DeleteNotes.ToArray(),"AddNotes",AddNotes.ToArray(),
        "DeleteRefs",DeleteRefs.ToArray(),"AddRefs",AddRefs.ToArray(),
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
    public string SendPort, ReceivePort, Sender, Receiver;
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
    public string[] DeleteParticipantIds=new string[0];
    public string[] DeleteMessageIds=new string[0];
    public string[] DeleteFrameIds=new string[0];
    public string[] DeleteNoteIds=new string[0];
    public string[] DeleteRefIds=new string[0];
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
        var changed=new List<SequenceJson>();
        var changedIds=new HashSet<string>();
        Func<string,string,string,SequenceJson> find=(type,from,to)=>{
            var matches=relations.Where(r=>V(r,"MetamodelId")==SequencePayload.Prefix+type
                && (from==null || V(r,"SourceId")==from) && V(r,"TargetId")==to).ToArray();
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
        // A relation with both ends leaving is going away too, so it never blocks. That
        // covers the links a frame holds to the messages inside it.
        var leaving=new HashSet<string>(gate.DeleteExecutions.Concat(gate.DeleteParticipants)
            .Concat(gate.DeleteMessages).Concat(gate.DeleteFragments).Concat(gate.DeleteOperands).Concat(gate.DeleteNotes).Concat(gate.DeleteRefs));
        Func<SequenceJson,string,bool> inside=(relation,id)=>
            leaving.Contains(V(relation,"SourceId")) && leaving.Contains(V(relation,"TargetId"));
        // Name the relation that blocked a deletion. Guessing which one it is has cost
        // several runs; the message can simply say.
        Func<SequenceJson,string,string> describe=(relation,id)=>{
            string other=V(relation,"SourceId")==id?V(relation,"TargetId"):V(relation,"SourceId");
            string kind=byId.ContainsKey(other)?V(byId[other],"EntityType"):"不明";
            return " 関連="+V(relation,"MetamodelId")+" 向き="+(V(relation,"TargetId")==id?"相手→対象":"対象→相手")
                +" 相手の型="+kind+(leaving.Contains(other)?"（削除対象）":"（残る）");
        };
        foreach(string id in gate.DeleteExecutions)
        {
            checkPort(id,before[id].Links["participant"].Single());
            foreach(var relation in relations.Where(r=>V(r,"SourceId")==id || V(r,"TargetId")==id))
            {
                if(changedIds.Contains(V(relation,"Id")))continue;
                bool owned=V(relation,"TargetId")==id && (V(relation,"MetamodelId")==SequencePayload.Prefix+"___Interaction_ExecutionSpecification"
                    || V(relation,"MetamodelId")==SequencePayload.Prefix+"OwnedExecutionSpecification");
                Require(owned || inside(relation,id),"削除する実行区間に未対応の関連が残っています。"+describe(relation,id));
            }
        }
        var affected=new HashSet<string>(gate.DeleteExecutions.Concat(gate.ReconnectMessages));
        var shapes=editor.Shapes();
        foreach(string id in affected)Require(shapes.Count(sh=>V(sh,"ModelId")==id)==1,"変更対象の図形を一意に取得できません。");
        foreach(var other in Array(source,"Editors").Where(e=>V(e,"Id")!=editorId))
            Require(!Mentions(other,affected),"変更対象を別のエディタも参照しています。");
        // A frame is built from the resolved metaclasses, not copied, so a diagram with no
        // frame at all can still get one. An existing frame is used only for its rectangle,
        // which is measured evidence of how wide a frame over these lanes should be.
        SequenceJson frameTemplate=null;
        if(gate.AddFragments.Count>0)
        {
            Require(types!=null && types.Complete(),"フラグメントの型情報が解決できていません。");
            var sampleFrames=entities.Where(e=>V(e,"MetamodelId")==types.Fragment).Select(e=>V(e,"Id")).ToArray();
            var frameShapes=editor.Shapes().Where(sh=>sampleFrames.Contains(V(sh,"ModelId"))
                && sh["X"]!=null && sh["Width"]!=null).ToArray();
            if(frameShapes.Length>0)frameTemplate=frameShapes[0];
        }
        Func<double,double> wrapMap=null,wrapInside=null;double wrapGrowth=0;
        var layout=gate.WrapFragments.Count>0?WrapLayout(gate,plan,editor,frameTemplate,current,out wrapMap,out wrapInside,out wrapGrowth)
            :gate.AddFragments.Count>0?FrameLayout(gate,plan,editor,frameTemplate,current)
            :new Dictionary<string,Dictionary<string,double>>(StringComparer.Ordinal);
        var additions=new List<SequenceAddedExecution>();
        var newEntities=new List<SequenceJson>();
        var newLaneShapes=new List<SequenceJson>();
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
            // The lifeline has to be in place first: adding the bar to the interaction makes
            // the product build its shape, and that lookup needs the owning lifeline.
            foreach(var origin in new[]{owned[0],ownerLink})
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
            Dictionary<string,double> geometry;
            if(layout.ContainsKey(id))
            {
                geometry=new Dictionary<string,double>(layout[id]);
                geometry["X"]=Read(laneShapes[0],"X")+Read(laneShapes[0],"Width")/2+8*Depth(wanted,after);
            }
            else geometry=Geometry(wanted,after,editor,laneShapes[0],templateShapes[0]);
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
        // Frames and their operands are built before the messages inside them, so the
        // ownership those messages need is already in the same import.
        var frames=new List<SequenceAddedFragment>();
        var branches=new List<SequenceAddedOperand>();
        var newFrameShapes=new List<SequenceJson>();
        var newOperandShapes=new List<SequenceJson>();
        var newNoteShapes=new List<SequenceJson>();
        var newRefShapes=new List<SequenceJson>();
        Func<string[],string,string,string,SequenceJson> relate=(row,relationId,from,to)=>
            SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",relationId,"RelationType",row[1],
                "MetamodelId",row[0],"SourceId",from,"TargetId",to)));
        foreach(string id in gate.AddFragments)
        {
            var wanted=after[id];
            Require(layout.ContainsKey(id),"追加するフラグメントの配置を決められません。");
            string name=wanted.Text??"";
            string operatorName;
            Require(wanted.Attributes.TryGetValue("operator",out operatorName),"追加するフラグメントに演算子がありません。");
            string operatorValue;
            Require(types.Operators.TryGetValue(operatorName,out operatorValue),
                "この図のプロファイルに演算子 "+operatorName+" がありません。");
            newEntities.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType","CombinedFragment",
                "MetamodelId",types.Fragment,"Name",name,"Fields",PumlBuild.Obj("Name",name,"Operator",operatorValue)))));
            var relationIds=new List<string>();var relationSources=new List<string>();
            var relationTargets=new List<string>();var relationFields=new List<string>();
            // Ownership first, then the lanes the frame spans, as the generator writes them.
            var wiring=new List<string[][]>{new[]{types.Owns,new[]{root,id}}};
            foreach(var lane in current.Elements.Where(e=>e.Kind=="participant"))
                wiring.Add(new[]{types.Crossing,new[]{id,lane.Id}});
            foreach(var pair in wiring)
            {
                string relationId=Guid.NewGuid().ToString();
                newRelations.Add(relate(pair[0],relationId,pair[1][0],pair[1][1]));
                relationIds.Add(relationId);relationSources.Add(pair[1][0]);
                relationTargets.Add(pair[1][1]);relationFields.Add(pair[0][2]);
            }
            string shapeId=Guid.NewGuid().ToString();
            var box=layout[id];
            newFrameShapes.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",shapeId,"ModelId",id,
                "X",Number(box["X"]),"Y",Number(box["Y"]),"Width",Number(box["Width"]),"Height",Number(box["Height"])))));
            frames.Add(new SequenceAddedFragment{ModelId=id,Metaclass=types.Fragment,Name=name,OwnerId=root,
                // The shape carries the operator as written, except for a group, which shows
                // its own name.
                ShapeId=shapeId,TemplateShapeId=frameTemplate==null?"":V(frameTemplate,"Id"),
                Text=operatorName=="group"?name:operatorName,
                Geometry=PumlBuild.Json(new[]{Number(box["X"]),Number(box["Y"]),Number(box["Width"]),Number(box["Height"])}),
                RelationIds=relationIds.ToArray(),RelationSources=relationSources.ToArray(),
                RelationTargets=relationTargets.ToArray(),RelationFields=relationFields.ToArray()});
        }
        foreach(string id in gate.AddOperands)
        {
            var wanted=after[id];
            Require(layout.ContainsKey(id),"追加するオペランドの配置を決められません。");
            Require(wanted.Parent!=null && gate.AddFragments.Contains(wanted.Parent),
                "追加するオペランドの所有先がこの計画のフラグメントではありません。");
            string guard=wanted.Text??"";
            newEntities.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType","InteractionOperand",
                "MetamodelId",types.Operand,"Name","","Fields",PumlBuild.Obj("Name","","Guard",guard)))));
            string relationId=Guid.NewGuid().ToString();
            newRelations.Add(relate(types.Branches,relationId,wanted.Parent,id));
            string shapeId=Guid.NewGuid().ToString();
            string position=Number(layout[id]["Position"]);
            newOperandShapes.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",shapeId,"ModelId",id,"Position",position))));
            branches.Add(new SequenceAddedOperand{ModelId=id,Metaclass=types.Operand,Name="",OwnerId=wanted.Parent,
                ShapeId=shapeId,TemplateShapeId="",Guard=guard,Position=position,
                RelationIds=new[]{relationId},RelationSources=new[]{wanted.Parent},RelationFields=new[]{types.Branches[2]}});
        }
        // Wrapped messages keep their model and their owner, the interaction. Only the
        // operand's reference to them is new, written after the operands exist.
        var movedMessages=new List<SequenceMovedMessage>();
        foreach(string id in gate.MoveMessages)
        {
            Require(byId.ContainsKey(id) && V(byId[id],"EntityType")=="Message","囲むメッセージが退避データにありません。");
            Require(!relations.Any(r=>V(r,"MetamodelId")==types.OperandMessage[0] && V(r,"TargetId")==id),"囲むメッセージが既にオペランドに属しています。");
            string relationId=Guid.NewGuid().ToString();
            newRelations.Add(relate(types.OperandMessage,relationId,after[id].Parent,id));
            movedMessages.Add(new SequenceMovedMessage{ModelId=id,OperandId=after[id].Parent,RelationId=relationId,Field=types.OperandMessage[2]});
        }
        // A message that has existing elements after it is an insertion: it needs room
        // made below, and the checks that keep an appended message off an occupied row
        // and inside the bars as they stand do not apply to it.
        string insertedId="";
        foreach(string id in gate.AddMessages)
        {
            var walk=SequenceStructurePreflight.Flatten(plan.Expected);
            // Going into an operand already drawn grows its frame even at the very end.
            bool intoFrame=after[id].Parent!=root && before.ContainsKey(after[id].Parent);
            if(!intoFrame && !walk.SkipWhile(e=>e!=id).Skip(1).Any(before.ContainsKey))continue;
            Require(insertedId.Length==0,"1回の更新で挿入できるメッセージは1件です。");
            insertedId=id;
        }
        var wires=new List<SequenceAddedMessage>();
        var newMessageShapes=new List<SequenceJson>();
        foreach(string id in gate.AddMessages)
        {
            var wanted=after[id];
            var walk=SequenceStructurePreflight.Flatten(plan.Expected);
            var earlier=walk.Take(System.Array.IndexOf(walk,id)).Where(after.ContainsKey).Select(e=>after[e])
                .Where(e=>e.Kind=="message" && byId.ContainsKey(e.Id)).ToArray();
            var sameSort=earlier.Where(e=>SequenceStructurePreflight.Attribute(e)==SequenceStructurePreflight.Attribute(wanted)).ToArray();
            Require(sameSort.Length>0,"同じ種別の既存メッセージがありません。");
            string template=sameSort[sameSort.Length-1].Id;
            Require(V(byId[template],"EntityType")=="Message","メッセージの見本を取得できません。");
            string send=wanted.Links["sendExecution"].Single(),receive=wanted.Links["receiveExecution"].Single();
            var shapes4=editor.Shapes();
            var templateShapes=shapes4.Where(sh=>V(sh,"ModelId")==template).ToArray();
            Require(templateShapes.Length==1,"メッセージの見本図形を一意に取得できません。");
            double y;
            if(layout.ContainsKey(id))y=layout[id]["Y"];
            else
            {
                Require(earlier.Length>0,"直前のメッセージを退避データから取得できません。");
                string previous=earlier[earlier.Length-1].Id;
                var previousShapes=shapes4.Where(sh=>V(sh,"ModelId")==previous).ToArray();
                Require(previousShapes.Length==1,"直前のメッセージの図形を一意に取得できません。");
                double at=Read(previousShapes[0],"TargetY");
                y=at+MessageSpacing;
                foreach(string port in new[]{send,receive})
                {
                    var bar=shapes4.Where(sh=>V(sh,"ModelId")==port).ToArray();
                    Require(bar.Length==1,"接続先の実行区間の図形を一意に取得できません。");
                    double top=Read(bar[0],"Y"),bottom=top+Read(bar[0],"Length");
                    // An appended message takes only space the bars already cover. An
                    // inserted one lands on a bar that is open across the point it goes in,
                    // and that bar grows with the room made below.
                    if(id==insertedId)
                        Require(top<=at && bottom>=at,"挿入位置をまたぐ実行区間につないでください。");
                    else Require(y>=top && y<=bottom,"追加するメッセージが既存の実行区間の範囲に収まりません。後続の移動は対象外です。");
                }
            }
            if(id!=insertedId)
                Require(shapes4.All(sh=>V(sh,"ModelId")==template || sh["TargetY"]==null || Read(sh,"TargetY")!=y),
                    "追加するメッセージの位置に既存の図形があります。");
            var entity=SequenceJson.Parse(byId[template].ToJsonString());
            entity.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(id));
            string name=wanted.Text??"";
            entity.Properties["Name"]=SequenceJson.Parse(SequencePayload.Q(name));
            if(entity["Fields"]!=null && entity["Fields"].Properties!=null && entity["Fields"]["Name"]!=null)
                entity["Fields"].Properties["Name"]=SequenceJson.Parse(SequencePayload.Q(name));
            newEntities.Add(entity);
            var relationIds=new List<string>();var relationSources=new List<string>();
            var templateIds=new List<string>();var relationFields=new List<string>();
            // Endpoints before membership, as the executions needed.
            var wiring=new List<string[]>{new[]{"SendMessage",send},new[]{"ReceiveMessage",receive},new[]{"___Interaction_Message",root}};
            // A message inside a frame is owned by the interaction and also pointed at by
            // the operand it sits in, the way the generator writes it.
            if(wanted.Parent!=root)wiring.Add(new[]{"OperandTargetMessage",wanted.Parent});
            // A reply is also tied to the bar it returns from, when the sample reply is and that
            // bar has no reply yet. Without it the product shrinks the bar on its next layout.
            if(relations.Any(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"ExecutionSpecificationReplyMessage" && V(r,"TargetId")==template)
                && !relations.Any(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"ExecutionSpecificationReplyMessage" && V(r,"SourceId")==send))
                wiring.Add(new[]{"ExecutionSpecificationReplyMessage",send});
            foreach(var pair in wiring)
            {
                string relationId=Guid.NewGuid().ToString();
                if(pair[0]=="OperandTargetMessage")
                {
                    Require(types!=null && types.Complete(),"オペランド所属の型情報が解決できていません。");
                    newRelations.Add(relate(types.OperandMessage,relationId,pair[1],id));
                    relationIds.Add(relationId);relationSources.Add(pair[1]);
                    templateIds.Add("");relationFields.Add(types.OperandMessage[2]);
                    continue;
                }
                var origin=find(pair[0],pair[0]=="___Interaction_Message"?root:V(find(pair[0],null,template),"SourceId"),template);
                var copy=SequenceJson.Parse(origin.ToJsonString());
                copy.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(relationId));
                copy.Properties["SourceId"]=SequenceJson.Parse(SequencePayload.Q(pair[1]));
                copy.Properties["TargetId"]=SequenceJson.Parse(SequencePayload.Q(id));
                copy.Properties.Remove("SourceIndex");copy.Properties.Remove("TargetIndex");
                newRelations.Add(copy);
                relationIds.Add(relationId);relationSources.Add(pair[1]);
                templateIds.Add(V(origin,"Id"));relationFields.Add("");
            }
            string wireShapeId=Guid.NewGuid().ToString();
            var wireShape=SequenceJson.Parse(templateShapes[0].ToJsonString());
            wireShape.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(wireShapeId));
            wireShape.Properties["ModelId"]=SequenceJson.Parse(SequencePayload.Q(id));
            wireShape.Properties["SourceY"]=SequenceJson.Parse(Number(y));
            wireShape.Properties["TargetY"]=SequenceJson.Parse(Number(y));
            newMessageShapes.Add(wireShape);
            wires.Add(new SequenceAddedMessage{ModelId=id,Metaclass=V(entity,"MetamodelId"),Name=name,OwnerId=root,
                ShapeId=wireShapeId,TemplateShapeId=V(templateShapes[0],"Id"),TemplateModelId=template,Y=Number(y),
                RelationIds=relationIds.ToArray(),RelationSources=relationSources.ToArray(),
                TemplateRelationIds=templateIds.ToArray(),RelationFields=relationFields.ToArray(),
                SendPort=send,ReceivePort=receive,Sender=wanted.Links["sender"].Single(),Receiver=wanted.Links["receiver"].Single()});
        }
        var lanes=new List<SequenceAddedParticipant>();
        foreach(string id in gate.AddParticipants)
        {
            var owned=relations.Where(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"___Interaction_Lifeline" && V(r,"SourceId")==root).ToArray();
            Require(owned.Length>0,"既存の参加者の所有関連を取得できません。");
            var shapes3=editor.Shapes();
            var laneShapes=owned.Select(r=>V(r,"TargetId"))
                .Select(target=>shapes3.SingleOrDefault(sh=>V(sh,"ModelId")==target)).Where(sh=>sh!=null).ToArray();
            Require(laneShapes.Length==owned.Length,"参加者の図形を一意に取得できません。");
            // Rightmost lane is the template: the new one sits one lane spacing further right.
            var rightmost=laneShapes.OrderBy(sh=>Read(sh,"X")+Read(sh,"Width")/2).Last();
            string template=V(rightmost,"ModelId");
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
            var link=SequenceJson.Parse(ownerLink.ToJsonString());
            link.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(relationId));
            link.Properties["TargetId"]=SequenceJson.Parse(SequencePayload.Q(id));
            link.Properties.Remove("SourceIndex");link.Properties.Remove("TargetIndex");
            newRelations.Add(link);
            string laneShapeId=Guid.NewGuid().ToString();
            var laneShape=SequenceJson.Parse(rightmost.ToJsonString());
            laneShape.Properties["Id"]=SequenceJson.Parse(SequencePayload.Q(laneShapeId));
            laneShape.Properties["ModelId"]=SequenceJson.Parse(SequencePayload.Q(id));
            double x=Read(rightmost,"X")+LaneSpacing;
            laneShape.Properties["X"]=SequenceJson.Parse(Number(x));
            newLaneShapes.Add(laneShape);
            lanes.Add(new SequenceAddedParticipant{ModelId=id,Metaclass=V(entity,"MetamodelId"),Name=name,OwnerId=root,
                ShapeId=laneShapeId,TemplateShapeId=V(rightmost,"Id"),RelationId=relationId,
                TemplateRelationId=V(ownerLink,"Id"),X=Number(x)});
        }
        foreach(var pair in gate.DeleteFragments.Select(f=>new[]{f,"___Interaction_CombinedFragment","CombinedFragment"})
            .Concat(gate.DeleteOperands.Select(o=>new[]{o,"___CombinedFragment_InteractionOperand","InteractionOperand"})))
        {
            Require(byId.ContainsKey(pair[0]) && V(byId[pair[0]],"EntityType")==pair[2],"削除対象が退避データ内の"+pair[2]+"ではありません。");
            foreach(var relation in relations.Where(r=>V(r,"SourceId")==pair[0] || V(r,"TargetId")==pair[0]))
                // A frame points at the lanes it spans. Removing the frame drops that
                // reference and leaves the lane itself untouched.
                Require(inside(relation,pair[0])
                    || (V(relation,"TargetId")==pair[0] && V(relation,"MetamodelId")==SequencePayload.Prefix+pair[1])
                    || (V(relation,"SourceId")==pair[0]
                        && V(relation,"MetamodelId")==SequencePayload.Prefix+"CrossingFragmentCoveredLifeline"),
                    "削除する"+pair[2]+"に未対応の関連が残っています。"+describe(relation,pair[0]));
            Require(editor.Shapes().Count(sh=>V(sh,"ModelId")==pair[0])==1,"削除する"+pair[2]+"の図形を一意に取得できません。");
        }
        foreach(string id in gate.DeleteMessages)
        {
            Require(byId.ContainsKey(id) && V(byId[id],"EntityType")=="Message","削除対象が退避データ内のメッセージではありません。");
            // A reply is also tied to the bar it returns from; that goes with the message.
            var allowed=new[]{"___Interaction_Message","SendMessage","ReceiveMessage","ExecutionSpecificationReplyMessage"};
            foreach(var relation in relations.Where(r=>V(r,"SourceId")==id || V(r,"TargetId")==id))
                Require(inside(relation,id)
                    || (V(relation,"TargetId")==id
                        && allowed.Any(kind=>V(relation,"MetamodelId")==SequencePayload.Prefix+kind)),
                    "削除するメッセージに未対応の関連が残っています。"+describe(relation,id));
            Require(editor.Shapes().Count(sh=>V(sh,"ModelId")==id)==1,"削除するメッセージの図形を一意に取得できません。");
        }
        foreach(string id in gate.DeleteNotes)
        {
            Require(byId.ContainsKey(id),"削除するNoteが退避データにありません。");
            // Only the interaction's ownership is expected. Anything else is named so the
            // next run can say what a note is tied to.
            foreach(var relation in relations.Where(r=>V(r,"SourceId")==id || V(r,"TargetId")==id))
                Require(inside(relation,id)
                    || (V(relation,"TargetId")==id && V(relation,"MetamodelId")==SequencePayload.Prefix+"___Interaction_InteractionNote"),
                    "削除するNoteに未対応の関連が残っています。"+describe(relation,id));
            Require(editor.Shapes().Count(sh=>V(sh,"ModelId")==id)==1,"削除するNoteの図形を一意に取得できません。");
        }
        foreach(string id in gate.DeleteRefs)
        {
            Require(byId.ContainsKey(id),"削除するrefが退避データにありません。");
            // What a ref points at, the lanes it covers and the interaction it refers to,
            // goes with it. Only the interaction's ownership may point at it.
            foreach(var relation in relations.Where(r=>V(r,"SourceId")==id || V(r,"TargetId")==id))
                Require(inside(relation,id) || V(relation,"SourceId")==id
                    || (V(relation,"TargetId")==id && V(relation,"MetamodelId")==SequencePayload.Prefix+"___Interaction_InteractionUse"),
                    "削除するrefに未対応の関連が残っています。"+describe(relation,id));
            Require(editor.Shapes().Count(sh=>V(sh,"ModelId")==id)==1,"削除するrefの図形を一意に取得できません。");
        }
        foreach(string id in gate.DeleteParticipants)
        {
            Require(byId.ContainsKey(id) && V(byId[id],"EntityType")=="Lifeline","削除対象が退避データ内の参加者ではありません。");
            foreach(var relation in relations.Where(r=>V(r,"SourceId")==id || V(r,"TargetId")==id))
                Require(inside(relation,id)
                    || (V(relation,"TargetId")==id && V(relation,"MetamodelId")==SequencePayload.Prefix+"___Interaction_Lifeline"),
                    "削除する参加者に未対応の関連が残っています。"+describe(relation,id));
            Require(editor.Shapes().Count(sh=>V(sh,"ModelId")==id)==1,"削除する参加者の図形を一意に取得できません。");
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
        if(newMessageShapes.Count>0)
        {
            var wireArray=patch["Editors"].Items.Single()["Messages"];
            Require(wireArray!=null && wireArray.Items!=null,"エディタにメッセージの図形配列がありません。");
            wireArray.Items.AddRange(newMessageShapes);
        }
        // A message that does not go last needs the room below it. Everything already
        // drawn at or under the point it goes in moves down by one message's spacing, and
        // a bar or frame open across that point grows instead of moving.
        var shifted=new List<SequenceShiftedShape>();
        if(insertedId.Length>0)
        {
            string id=insertedId;
            var wanted=after[id];
            var walk=SequenceStructurePreflight.Flatten(plan.Expected);
            var earlier=walk.TakeWhile(e=>e!=id).Where(before.ContainsKey)
                .Where(e=>after.ContainsKey(e) && after[e].Kind=="message").ToArray();
            Require(earlier.Length>0,"挿入位置の直前のメッセージを取得できません。");
            var previousShapes=editor.Shapes().Where(sh=>V(sh,"ModelId")==earlier[earlier.Length-1]).ToArray();
            Require(previousShapes.Length==1,"直前のメッセージの図形を一意に取得できません。");
            double at=Read(previousShapes[0],"TargetY");
            var ports=new HashSet<string>(new[]{"sendExecution","receiveExecution"}
                .Select(role=>wanted.Links[role].Single()));
            var shapesNow=editor.Shapes();
            Func<string,string> kindOf=model=>before.ContainsKey(model)?before[model].Kind:"";
            foreach(var shape in shapesNow)
            {
                string model=V(shape,"ModelId");
                var keys=new List<string>();var values=new List<string>();
                if(kindOf(model)=="fragment")
                {
                    // A frame below moves whole; one the point falls inside grows.
                    double top=Read(shape,"Y"),height=Read(shape,"Height");
                    if(top>at) {keys.Add("Y");values.Add(Number(top+MessageSpacing));}
                    else if(top+height>at) {keys.Add("Height");values.Add(Number(height+MessageSpacing));}
                }
                else if(kindOf(model)=="operand")
                {
                    // An operand has no rectangle, only its offset from the frame's top. It
                    // moves with a frame that moves, so only a later operand of a frame that
                    // grows needs a new offset.
                    string owner=before[model].Parent;
                    var boxes=shapesNow.Where(sh=>V(sh,"ModelId")==owner).ToArray();
                    Require(boxes.Length==1,"オペランドの枠の図形を一意に取得できません。");
                    double top=Read(boxes[0],"Y"),height=Read(boxes[0],"Height"),offset=Read(shape,"Position");
                    if(top<=at && top+height>at && top+offset>at)
                    {keys.Add("Position");values.Add(Number(offset+MessageSpacing));}
                }
                else if(shape["TargetY"]!=null && shape["SourceY"]!=null && Read(shape,"TargetY")>at)
                {
                    keys.Add("SourceY");values.Add(Number(Read(shape,"SourceY")+MessageSpacing));
                    keys.Add("TargetY");values.Add(Number(Read(shape,"TargetY")+MessageSpacing));
                }
                else if(shape["Length"]!=null && shape["Y"]!=null)
                {
                    double top=Read(shape,"Y"),bottom=top+Read(shape,"Length");
                    if(top>at) {keys.Add("Y");values.Add(Number(top+MessageSpacing));}
                    // Only a bar still open at the next step grows, or one the new message lands
                    // on. A bar that closes right after the message above ends before the new one.
                    else if(bottom>=at+MessageSpacing || ports.Contains(model))
                    {
                        // A bar carries its length as both Height and Length, and the
                        // product keeps the pair in step. Writing only one is ignored.
                        string grown=Number(Read(shape,"Length")+MessageSpacing);
                        keys.Add("Length");values.Add(grown);
                        if(shape["Height"]!=null) {keys.Add("Height");values.Add(grown);}
                    }
                }
                else if(shape["LaneLength"]!=null)
                {keys.Add("LaneLength");values.Add(Number(Read(shape,"LaneLength")+MessageSpacing));}
                if(keys.Count==0)continue;
                foreach(var node in patch["Editors"].Items.SelectMany(view=>view.Properties.Values)
                    .Where(array=>array!=null && array.Items!=null).SelectMany(array=>array.Items)
                    .Where(n=>V(n,"Id")==V(shape,"Id")))
                    for(int i=0;i<keys.Count;i++)node.Properties[keys[i]]=SequenceJson.Parse(values[i]);
                shifted.Add(new SequenceShiftedShape{ModelId=model,ShapeId=V(shape,"Id"),
                    Kind=ports.Contains(model)?"port":"other",Keys=keys.ToArray(),Values=values.ToArray()});
            }
            Require(ports.All(port=>shifted.Any(s=>s.ModelId==port)),
                "挿入位置をまたぐ実行区間がありません。既に開いているバーの間に挿入してください。");
        }
        // A new note goes one message step under the message above it, as tall as its text,
        // and what was below moves down by that height and one message step. A bar open
        // across the point grows; the lanes follow.
        var notes=new List<SequenceAddedNote>();
        foreach(string id in gate.AddNotes.Concat(gate.AddRefs))
        {
            var wanted=after[id];string text=wanted.Text??"";bool isRef=wanted.Kind=="ref";
            if(isRef)Require(refTypes!=null && refTypes.Owns!=null && refTypes.Crossing!=null,"refの型情報が解決できていません。");
            else Require(noteTypes!=null && noteTypes.Owns!=null && noteTypes.Owns.Length==3,"Noteの型情報が解決できていません。");
            var walk=SequenceStructurePreflight.Flatten(plan.Expected);
            string previous=walk[System.Array.IndexOf(walk,id)-1];
            var previousShapes=editor.Shapes().Where(sh=>V(sh,"ModelId")==previous).ToArray();
            Require(previousShapes.Length==1,"Noteの直前のメッセージの図形を一意に取得できません。");
            double at=Read(previousShapes[0],"TargetY"),top=at+MessageSpacing;
            double height=Math.Max(48,16+20*text.Replace("\r\n","\n").Split('\n').Length),room=height+MessageSpacing;
            var noteLaneIds=new HashSet<string>(current.Elements.Where(e=>e.Kind=="participant").Select(e=>e.Id));
            var noteLanes=editor.Shapes().Where(sh=>noteLaneIds.Contains(V(sh,"ModelId")) && sh["X"]!=null && sh["Width"]!=null).ToArray();
            Require(noteLanes.Length>0,"参加者の図形がないためNoteの幅を決められません。");
            // A ref covers the lanes it names; a new note names none and covers them all.
            var covered=isRef && wanted.Links.ContainsKey("targets")?wanted.Links["targets"]:new string[0];
            var spanned=covered.Length>0?noteLanes.Where(sh=>covered.Contains(V(sh,"ModelId"))).ToArray():noteLanes;
            double left=spanned.Min(sh=>Read(sh,"X")+Read(sh,"Width")/2),right=spanned.Max(sh=>Read(sh,"X")+Read(sh,"Width")/2);
            // The generator's margins: a note 50 out and at least 160 wide, a ref 55 out and 150.
            double x=isRef?left-55:left-50,width=isRef?Math.Max(150,right-left+110):Math.Max(160,right-left+100);
            var fields=new Dictionary<string,object>{{"Name",text}};
            // A rich-text body is shown from the name, as the generator writes it.
            if(!isRef && noteTypes.Field!="Name" && noteTypes.Storage=="String")fields[noteTypes.Field]=text;
            string metaclass=isRef?refTypes.Class:noteTypes.Class;
            // The patch is already assembled by now, so the new element goes straight into it.
            patch["Entities"].Items.Add(SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",id,"EntityType",isRef?"InteractionUse":"InteractionNote",
                "MetamodelId",metaclass,"Name",text,"Fields",fields))));
            var wiring=new List<object[]>{new object[]{isRef?refTypes.Owns:noteTypes.Owns,root,id}};
            foreach(string lane in covered)wiring.Add(new object[]{refTypes.Crossing,id,lane});
            string reference;
            if(isRef && refTypes.RefersTo!=null && wanted.Attributes.TryGetValue("reference",out reference) && !string.IsNullOrEmpty(reference))
                wiring.Add(new object[]{refTypes.RefersTo,id,reference});
            var added=new SequenceAddedNote{Kind=wanted.Kind,ModelId=id,Metaclass=metaclass,Name=text,OwnerId=root,Text=text,
                Geometry=PumlBuild.Json(new[]{Number(x),Number(top),Number(width),Number(height)})};
            foreach(var row in wiring)
            {
                var type=(string[])row[0];string relationId=Guid.NewGuid().ToString();
                patch["Relations"].Items.Add(relate(type,relationId,(string)row[1],(string)row[2]));
                added.RelationIds=added.RelationIds.Concat(new[]{relationId}).ToArray();
                added.RelationSources=added.RelationSources.Concat(new[]{(string)row[1]}).ToArray();
                added.RelationTargets=added.RelationTargets.Concat(new[]{(string)row[2]}).ToArray();
                added.RelationFields=added.RelationFields.Concat(new[]{type[2]}).ToArray();
            }
            string shapeId=Guid.NewGuid().ToString();added.ShapeId=shapeId;
            var shape=SequenceJson.Parse(PumlBuild.Json(PumlBuild.Obj("Id",shapeId,"ModelId",id,
                "X",Number(x),"Y",Number(top),"Width",Number(width),"Height",Number(height))));
            Collection(patch["Editors"].Items.Single(),isRef?"InteractionUses":"Notes").Items.Add(shape);
            (isRef?newRefShapes:newNoteShapes).Add(shape);
            notes.Add(added);
            var laneShapeIds=new HashSet<string>(noteLanes.Select(sh=>V(sh,"Id")));
            foreach(var existing in editor.Shapes())
            {
                string model=V(existing,"ModelId");
                string kind=before.ContainsKey(model)?before[model].Kind:"";
                var keys=new List<string>();var values=new List<string>();
                if(kind=="message" && Read(existing,"TargetY")>at)
                {
                    keys.Add("SourceY");values.Add(Number(Read(existing,"SourceY")+room));
                    keys.Add("TargetY");values.Add(Number(Read(existing,"TargetY")+room));
                }
                else if(kind=="execution")
                {
                    double y=Read(existing,"Y"),length=Read(existing,"Length");
                    if(y>at){keys.Add("Y");values.Add(Number(y+room));}
                    // A bar that closes right after the message above ends before the note.
                    else if(y+length>=at+MessageSpacing){keys.Add("Length");values.Add(Number(length+room));keys.Add("Height");values.Add(Number(length+room));}
                }
                else if((kind=="fragment" || kind=="note" || kind=="ref") && Read(existing,"Y")>at)
                {keys.Add("Y");values.Add(Number(Read(existing,"Y")+room));}
                else if(laneShapeIds.Contains(V(existing,"Id")) && existing["LaneLength"]!=null)
                {keys.Add("LaneLength");values.Add(Number(Read(existing,"LaneLength")+room));}
                if(keys.Count==0)continue;
                foreach(var node in patch["Editors"].Items.SelectMany(view=>view.Properties.Values)
                    .Where(array=>array!=null && array.Items!=null).SelectMany(array=>array.Items)
                    .Where(n=>V(n,"Id")==V(existing,"Id")))
                    for(int i=0;i<keys.Count;i++)node.Properties[keys[i]]=SequenceJson.Parse(values[i]);
                shifted.Add(new SequenceShiftedShape{ModelId=model,ShapeId=V(existing,"Id"),Kind=kind,Keys=keys.ToArray(),Values=values.ToArray()});
            }
        }
        // Wrapping makes room at the top of the run and below it. Every position moves by
        // the same rule, so a bar's two ends are mapped separately and its length follows.
        if(wrapMap!=null)
        {
            var laneIds=new HashSet<string>(current.Elements.Where(e=>e.Kind=="participant").Select(e=>e.Id));
            foreach(var shape in editor.Shapes())
            {
                string model=V(shape,"ModelId");
                string kind=before.ContainsKey(model)?before[model].Kind:"";
                var keys=new List<string>();var values=new List<string>();
                Action<string,double,double> put=(key,was,now)=>{if(Math.Abs(now-was)>1e-9){keys.Add(key);values.Add(Number(now));}};
                if(kind=="message")
                {
                    put("SourceY",Read(shape,"SourceY"),wrapMap(Read(shape,"SourceY")));
                    put("TargetY",Read(shape,"TargetY"),wrapMap(Read(shape,"TargetY")));
                }
                else if(kind=="execution")
                {
                    // A bar the input closes inside the new frame has to end inside it too,
                    // even when it used to reach further down than the run's last message.
                    string frameId=gate.WrapFragments[0];
                    bool closesInside=after.ContainsKey(model) && (after[model].Links.ContainsKey("endContainer")?after[model].Links["endContainer"]:new string[0])
                        .Any(id=>after.ContainsKey(id) && after[id].Kind=="operand" && after[id].Parent==frameId);
                    double top=Read(shape,"Y"),length=Read(shape,"Length"),moved=wrapMap(top);
                    double floor=layout[frameId]["Y"]+layout[frameId]["Height"]-8;
                    double grown=(closesInside?Math.Min(wrapInside(top+length),floor):wrapMap(top+length))-moved;
                    put("Y",top,moved);
                    // A bar carries its length as both Height and Length; writing one is ignored.
                    if(Math.Abs(grown-length)>1e-9){keys.Add("Length");values.Add(Number(grown));keys.Add("Height");values.Add(Number(grown));}
                }
                else if(kind=="fragment")
                {
                    double top=Read(shape,"Y"),height=Read(shape,"Height"),moved=wrapMap(top);
                    put("Y",top,moved);put("Height",height,wrapMap(top+height)-moved);
                }
                else if(laneIds.Contains(model) && shape["LaneLength"]!=null)
                    put("LaneLength",Read(shape,"LaneLength"),Read(shape,"LaneLength")+wrapGrowth);
                // The diagram's own frame, operands (offsets from their frame) and lanes
                // without a timeline stay. Anything else has to sit above the run.
                else if(kind=="" || kind=="operand" || kind=="interaction" || laneIds.Contains(model))continue;
                else Require(shape["Y"]==null || Math.Abs(wrapMap(Read(shape,"Y"))-Read(shape,"Y"))<1e-9,
                    "囲む範囲より下にある"+kind+"の位置を決められません。");
                if(keys.Count==0)continue;
                foreach(var node in patch["Editors"].Items.SelectMany(view=>view.Properties.Values)
                    .Where(array=>array!=null && array.Items!=null).SelectMany(array=>array.Items)
                    .Where(n=>V(n,"Id")==V(shape,"Id")))
                    for(int i=0;i<keys.Count;i++)node.Properties[keys[i]]=SequenceJson.Parse(values[i]);
                shifted.Add(new SequenceShiftedShape{ModelId=model,ShapeId=V(shape,"Id"),Kind=kind,Keys=keys.ToArray(),Values=values.ToArray()});
            }
        }
        var stretched=new List<SequenceStretchedLifeline>();
        if(layout.ContainsKey("") && layout[""]["Growth"]>0)
        {
            double growth=layout[""]["Growth"];
            var timelines=new HashSet<string>(current.Elements.Where(e=>e.Kind=="participant").Select(e=>e.Id));
            foreach(var shape in editor.Shapes().Where(sh=>timelines.Contains(V(sh,"ModelId"))))
            {
                Require(shape["LaneLength"]!=null,"参加者の図形にタイムラインの長さがありません。");
                string length=Number(Read(shape,"LaneLength")+growth);
                foreach(var node in patch["Editors"].Items.Single()["Lifelines"].Items.Where(n=>V(n,"Id")==V(shape,"Id")))
                    node.Properties["LaneLength"]=SequenceJson.Parse(length);
                stretched.Add(new SequenceStretchedLifeline{ModelId=V(shape,"ModelId"),ShapeId=V(shape,"Id"),Length=length});
            }
        }
        foreach(var pair in new[]{new object[]{"Fragments",newFrameShapes},new object[]{"Operands",newOperandShapes}})
        {
            var frameShapeList=(List<SequenceJson>)pair[1];
            if(frameShapeList.Count==0)continue;
            Collection(patch["Editors"].Items.Single(),(string)pair[0]).Items.AddRange(frameShapeList);
        }
        if(newLaneShapes.Count>0)
        {
            var laneArray=patch["Editors"].Items.Single()["Lifelines"];
            Require(laneArray!=null && laneArray.Items!=null,"エディタに参加者の図形配列がありません。");
            laneArray.Items.AddRange(newLaneShapes);
        }
        return new SequenceStructurePreparation{ReconnectJson=patch.ToJsonString(),ReconnectCount=changed.Count,
            EditorAfterDeleteJson=Deleted(editor,newShapes,newLaneShapes,newMessageShapes,
                newFrameShapes,newOperandShapes,newNoteShapes,newRefShapes,stretched,shifted,gate.DeleteExecutions,
                gate.DeleteParticipants.Concat(gate.DeleteMessages)
                    .Concat(gate.DeleteFragments).Concat(gate.DeleteOperands).Concat(gate.DeleteNotes).Concat(gate.DeleteRefs).ToList()),
            DeleteIds=gate.DeleteExecutions.ToArray(),
            AddedExecutions=additions.ToArray(),AddedParticipants=lanes.ToArray(),AddedMessages=wires.ToArray(),
            AddedFragments=frames.ToArray(),AddedOperands=branches.ToArray(),
            StretchedLifelines=stretched.ToArray(),CreatedCollections=Created.ToArray(),
            ShiftedShapes=shifted.ToArray(),InsertedMessageId=insertedId,MovedMessages=movedMessages.ToArray(),AddedNotes=notes.ToArray(),
            DeleteParticipantIds=gate.DeleteParticipants.ToArray(),DeleteMessageIds=gate.DeleteMessages.ToArray(),
            DeleteFrameIds=gate.DeleteFragments.Concat(gate.DeleteOperands).ToArray(),DeleteNoteIds=gate.DeleteNotes.ToArray(),DeleteRefIds=gate.DeleteRefs.ToArray(),
            ReceiveRelationIds=relations.Where(r=>V(r,"MetamodelId")==SequencePayload.Prefix+"ReceiveMessage").Select(r=>V(r,"Id")).ToArray()};
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
    // The generator places a bar at the lane centre and steps 8px right per nesting
    // level. Reuse that rule so an added bar lands where a generated one would.
    // A frame decides the position of everything inside it, so the whole block is laid
    // out in one pass: a bar's top is the first message it touches, and that message sits
    // where the frame puts it. Resolving those one at a time would be circular. The steps
    // are the ones the generator uses when it builds a diagram from scratch.
    internal static Dictionary<string,Dictionary<string,double>> FrameLayout(SequenceStructurePreflight gate,
        SyncPlan plan,SequenceEditorDocument editor,SequenceJson frameTemplate,SequenceDocument current)
    {
        var layout=new Dictionary<string,Dictionary<string,double>>(StringComparer.Ordinal);
        if(gate.AddFragments.Count==0)return layout;
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        double floor=0;
        foreach(var shape in editor.Shapes())
        {
            foreach(string key in new[]{"TargetY","SourceY"})
                if(shape[key]!=null)floor=Math.Max(floor,Read(shape,key));
            if(shape["Y"]==null)continue;
            double bottom=Read(shape,"Y");
            foreach(string key in new[]{"Length","Height"})
                if(shape[key]!=null)bottom=Math.Max(bottom,Read(shape,"Y")+Read(shape,key));
            floor=Math.Max(floor,bottom);
        }
        double x,width;
        if(frameTemplate!=null) {x=Read(frameTemplate,"X");width=Read(frameTemplate,"Width");}
        else
        {
            // No frame to measure: span every lane, the way a frame covers them all.
            var lanes=new HashSet<string>(current.Elements.Where(e=>e.Kind=="participant").Select(e=>e.Id));
            var laneShapes=editor.Shapes().Where(sh=>lanes.Contains(SequenceEditorDocument.Value(sh,"ModelId"))
                && sh["X"]!=null && sh["Width"]!=null).ToArray();
            if(laneShapes.Length==0)throw new InvalidOperationException("S220: 参加者の図形がないため枠の幅を決められません。");
            double left=laneShapes.Min(sh=>Read(sh,"X")),right=laneShapes.Max(sh=>Read(sh,"X")+Read(sh,"Width"));
            x=left-16;width=right-left+32;
        }
        double at=floor+MessageSpacing;
        var order=SequenceStructurePreflight.Flatten(plan.Expected);
        foreach(string frameId in order.Where(gate.AddFragments.Contains))
        {
            double top=at,y=at;bool first=true;
            foreach(string operandId in order.Where(id=>after.ContainsKey(id) && after[id].Kind=="operand" && after[id].Parent==frameId))
            {
                y+=first?30:12;first=false;
                var guard=after[operandId].Text??"";
                var slot=new Dictionary<string,double>();slot["Position"]=y-top;layout[operandId]=slot;
                y+=40+18*(guard.Replace("\r\n","\n").Split('\n').Length-1);
                foreach(string messageId in order.Where(id=>after.ContainsKey(id) && after[id].Kind=="message" && after[id].Parent==operandId))
                {
                    var row=new Dictionary<string,double>();row["Y"]=y;layout[messageId]=row;
                    y+=MessageSpacing;
                }
                y+=8;
            }
            var frame=new Dictionary<string,double>();
            frame["X"]=x;frame["Y"]=top;frame["Width"]=width;frame["Height"]=Math.Max(40,y-top);
            layout[frameId]=frame;
            at=y+16;
        }
        // How much taller the diagram got. The lifelines have to follow, or their timeline
        // stops above the frame. The empty key cannot collide with an element id.
        var span=new Dictionary<string,double>();span["Growth"]=Math.Max(0,at-floor);
        layout[""]=span;
        // Bars inside the block span the messages they touch.
        foreach(string barId in gate.AddExecutions)
        {
            var bar=after[barId];
            if(bar.Parent==null || !layout.ContainsKey(bar.Parent))continue;
            double top=double.MaxValue,bottom=double.MinValue;
            foreach(var message in plan.Expected.Elements.Where(e=>e.Kind=="message"))
                foreach(string role in new[]{"sendExecution","receiveExecution"})
                {
                    string[] ids;
                    if(!message.Links.TryGetValue(role,out ids) || !ids.Contains(barId) || !layout.ContainsKey(message.Id))continue;
                    top=Math.Min(top,layout[message.Id]["Y"]);bottom=Math.Max(bottom,layout[message.Id]["Y"]);
                }
            if(top==double.MaxValue)continue;
            var slot=new Dictionary<string,double>();
            slot["Y"]=top;slot["Length"]=Math.Max(40,bottom+16-top);slot["Height"]=slot["Length"];
            layout[barId]=slot;
        }
        return layout;
    }
    // A frame drawn around a run of messages already there. The run moves down to make
    // room for the frame's heading and each operand's guard, using the generator's steps:
    // the first guard 30 below the frame's top and its message 40 below that, a new
    // operand 8 + 12 under the last message's slot, the frame closing 8 under it.
    // Everything below moves down by what the run grew plus the frame's bottom margin.
    // A position above the run stays, so a bar opened before it keeps its top.
    internal static Dictionary<string,Dictionary<string,double>> WrapLayout(SequenceStructurePreflight gate,
        SyncPlan plan,SequenceEditorDocument editor,SequenceJson frameTemplate,SequenceDocument current,
        out Func<double,double> map,out Func<double,double> inside,out double growth)
    {
        var layout=new Dictionary<string,Dictionary<string,double>>(StringComparer.Ordinal);
        var after=plan.Expected.Elements.ToDictionary(e=>e.Id);
        string frameId=gate.WrapFragments.Single();
        var shapes=editor.Shapes();
        var ys=gate.MoveMessages.Select(id=>{
            var found=shapes.Where(sh=>SequenceEditorDocument.Value(sh,"ModelId")==id).ToArray();
            if(found.Length!=1)throw new InvalidOperationException("S220: 囲むメッセージの図形を一意に取得できません。");
            return Read(found[0],"TargetY");
        }).ToArray();
        for(int i=1;i<ys.Length;i++)
            if(ys[i]<=ys[i-1])throw new InvalidOperationException("S220: 囲むメッセージの縦位置が順序どおりではありません。");
        Func<string,int> lines=guard=>(guard??"").Replace("\r\n","\n").Split('\n').Length;
        double top=ys[0]-10;
        var offsets=new double[ys.Length];var placed=new double[ys.Length];
        for(int i=0;i<ys.Length;i++)
        {
            string operand=after[gate.MoveMessages[i]].Parent;
            bool opens=i==0 || operand!=after[gate.MoveMessages[i-1]].Parent;
            if(opens)
            {
                // The guard sits where the operand begins; the message goes under it.
                double guardAt=i==0?top+30:placed[i-1]+MessageSpacing+8+12;
                var slot=new Dictionary<string,double>();slot["Position"]=guardAt-top;layout[operand]=slot;
                placed[i]=guardAt+40+18*(lines(after[operand].Text)-1);
            }
            else placed[i]=ys[i]+offsets[i-1];
            offsets[i]=placed[i]-ys[i];
        }
        double bottom=placed[ys.Length-1]+MessageSpacing+8;
        double x,width;FrameSpan(editor,frameTemplate,current,out x,out width);
        var frame=new Dictionary<string,double>();
        frame["X"]=x;frame["Y"]=top;frame["Width"]=width;frame["Height"]=bottom-top;
        layout[frameId]=frame;
        // Below the run, a message that sat one step under its last one lands 30 under
        // the frame, the gap the generator leaves after a frame.
        double below=offsets[ys.Length-1]+38,split=ys[ys.Length-1]+MessageSpacing/2;
        growth=below;
        // Inside the run a position moves with the message just above it. A bar that
        // closes inside the frame uses that for its bottom too, wherever it ended before.
        inside=p=>{
            int at=0;while(at+1<ys.Length && ys[at+1]<=p)at++;
            return p+offsets[at];
        };
        var within=inside;
        map=p=>p<ys[0]-5?p:p>split?p+below:within(p);
        return layout;
    }
    // Where a frame over every lane goes across: the rectangle of an existing frame when
    // there is one, measured on the product, or the lanes' outer edges with a 16px margin.
    static void FrameSpan(SequenceEditorDocument editor,SequenceJson frameTemplate,SequenceDocument current,out double x,out double width)
    {
        if(frameTemplate!=null) {x=Read(frameTemplate,"X");width=Read(frameTemplate,"Width");return;}
        var lanes=new HashSet<string>(current.Elements.Where(e=>e.Kind=="participant").Select(e=>e.Id));
        var laneShapes=editor.Shapes().Where(sh=>lanes.Contains(SequenceEditorDocument.Value(sh,"ModelId"))
            && sh["X"]!=null && sh["Width"]!=null).ToArray();
        if(laneShapes.Length==0)throw new InvalidOperationException("S220: 参加者の図形がないため枠の幅を決められません。");
        double left=laneShapes.Min(sh=>Read(sh,"X")),right=laneShapes.Max(sh=>Read(sh,"X")+Read(sh,"Width"));
        x=left-16;width=right-left+32;
    }
    static int Depth(SequenceElement wanted,Dictionary<string,SequenceElement> after)
    {
        int depth=0;
        for(var at=wanted;;depth++)
        {
            var outer=at.Links.ContainsKey("outer")?at.Links["outer"]:new string[0];
            if(outer.Length==0)return depth;
            if(depth>32 || !after.ContainsKey(outer[0]))throw new InvalidOperationException("S220: 入れ子の階層を解決できません。");
            at=after[outer[0]];
        }
    }
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
    internal const double LaneSpacing=240;
    internal const double MessageSpacing=PumlBuild.MessagePitch;
    static string Deleted(SequenceEditorDocument editor,List<SequenceJson> addedShapes,List<SequenceJson> addedLanes,
        List<SequenceJson> addedWires,List<SequenceJson> addedFrames,List<SequenceJson> addedBranches,List<SequenceJson> addedNotes,List<SequenceJson> addedRefs,
        List<SequenceStretchedLifeline> stretched,List<SequenceShiftedShape> shifted,
        List<string> removed,List<string> removedLanes)
    {
        var json=SequenceJson.Parse(editor.ImportJson());
        var view=json["Editors"].Items.Single();
        var gone=new HashSet<string>(removed.Concat(removedLanes));
        // A connector drawn to a removed shape, such as a note's anchor, has no model of its
        // own; it goes when either end does.
        var goneShapes=new HashSet<string>(editor.Shapes().Where(sh=>gone.Contains(SequenceEditorDocument.Value(sh,"ModelId")))
            .Select(sh=>SequenceEditorDocument.Value(sh,"Id")));
        Func<SequenceJson,bool> dangling=node=>node.Properties!=null
            && node.Properties.Any(p=>p.Key!="Id" && p.Value!=null && p.Value.Raw!=null && p.Value.Raw.StartsWith("\"",StringComparison.Ordinal)
                && goneShapes.Contains(p.Value.StringValue()));
        foreach(var property in view.Properties)
        {
            var array=property.Value;
            if(array==null || array.Items==null)continue;
            for(int i=array.Items.Count-1;i>=0;i--)
                if(gone.Contains(SequenceEditorDocument.Value(array.Items[i],"ModelId")) || dangling(array.Items[i]))array.Items.RemoveAt(i);
        }
        Action<string,List<SequenceJson>> append=(collection,shapes)=>{
            if(shapes.Count==0)return;
            Collection(view,collection).Items.AddRange(shapes.Select(sh=>SequenceJson.Parse(sh.ToJsonString())));
        };
        append("ExecutionSpecifications",addedShapes);append("Lifelines",addedLanes);append("Messages",addedWires);
        append("Fragments",addedFrames);append("Operands",addedBranches);append("Notes",addedNotes);append("InteractionUses",addedRefs);
        // This editor is rebuilt from the original export, so the stretched timelines have
        // to be written here as well or the delete stage puts the old lengths back.
        if(stretched.Count>0)
        {
            var lanes=Collection(view,"Lifelines");
            foreach(var lane in stretched)
                foreach(var node in lanes.Items.Where(n=>SequenceEditorDocument.Value(n,"Id")==lane.ShapeId))
                    node.Properties["LaneLength"]=SequenceJson.Parse(lane.Length);
        }
        foreach(var move in shifted)
            foreach(var node in view.Properties.Values.Where(array=>array!=null && array.Items!=null)
                .SelectMany(array=>array.Items).Where(n=>SequenceEditorDocument.Value(n,"Id")==move.ShapeId))
                for(int i=0;i<move.Keys.Length;i++)
                    node.Properties[move.Keys[i]]=SequenceJson.Parse(move.Values[i]);
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
                    ?wire.RelationFields[i]:result.Field(wire.TemplateRelationIds[i]);
                string origin=wire.RelationSources[i];
                if(field.Length==0)throw new InvalidOperationException("S230: 追加するメッセージの関連の種別情報が不足しています。");
                int index=result.Relations.Count(pair=>pair.Value[0]==origin && result.Field(pair.Key)==field);
                result.Relations[wire.RelationIds[i]]=new[]{origin,wire.ModelId,
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),"0"};
                result.RelationFields[wire.RelationIds[i]]=field;
            }
            string sample;
            if(!result.Shapes.TryGetValue(wire.TemplateShapeId,out sample))throw new InvalidOperationException("S230: メッセージの見本図形がありません。");
            var measured=SequenceJson.Parse(sample);
            // A message signature ends with text, both ends and the selfloop offset.
            if(measured==null || measured.Items==null || measured.Items.Count<4)
                throw new InvalidOperationException("S230: メッセージの図形の項目数が想定と違います。");
            var rows=measured.Items.Select(item=>item.StringValue()).ToArray();
            rows[rows.Length-4]=wire.Name;rows[rows.Length-3]=wire.Y;rows[rows.Length-2]=wire.Y;
            result.Shapes[wire.ShapeId]=PumlBuild.Json(rows);
            result.ShapeModels[wire.ShapeId]=wire.ModelId;
            string[] pattern;
            if(!result.Ports.TryGetValue(wire.TemplateModelId,out pattern))throw new InvalidOperationException("S230: メッセージの見本の送受信がありません。");
            result.Ports[wire.ModelId]=new[]{wire.SendPort,wire.ReceivePort,wire.Sender,wire.Receiver,pattern[4]};
        }
        // Room made for an inserted message: a shape below it moved down, a bar open
        // across it grew, and a lane's timeline followed. The signatures the SDK reads
        // back put those numbers in fixed places, so the moved values go back in the
        // same places rather than being recomputed.
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
        // Only the timeline length changes on a stretched lane; the rectangle stays.
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
            {
                string field=frame.RelationFields[i];
                if(field.Length==0)throw new InvalidOperationException("S230: 追加するフラグメントの関連の種別情報が不足しています。");
                // A frame is the first addition whose relations point at something that was
                // already there, so the far end has a collection of its own to append to.
                int index=result.Relations.Count(pair=>pair.Value[0]==frame.RelationSources[i] && result.Field(pair.Key)==field);
                int reverse=result.Relations.Count(pair=>pair.Value[1]==frame.RelationTargets[i] && result.Field(pair.Key)==field);
                result.Relations[frame.RelationIds[i]]=new[]{frame.RelationSources[i],frame.RelationTargets[i],
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    reverse.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                result.RelationFields[frame.RelationIds[i]]=field;
            }
            var box=SequenceJson.Parse(frame.Geometry);
            if(box==null || box.Items==null || box.Items.Count!=4)
                throw new InvalidOperationException("S230: フラグメントの図形の項目数が想定と違います。");
            result.Shapes[frame.ShapeId]=frame.Geometry+frame.Text;
            result.ShapeModels[frame.ShapeId]=frame.ModelId;
            foreach(var branch in prepared.AddedOperands.Where(o=>o.OwnerId==frame.ModelId))
            {
                result.Models[branch.ModelId]=PumlBuild.Json(new[]{branch.Metaclass,branch.Name,branch.OwnerId,"False"});
                string field=branch.RelationFields[0];
                if(field.Length==0)throw new InvalidOperationException("S230: 追加するオペランドの関連の種別情報が不足しています。");
                int index=result.Relations.Count(pair=>pair.Value[0]==branch.OwnerId && result.Field(pair.Key)==field);
                int reverse=result.Relations.Count(pair=>pair.Value[1]==branch.ModelId && result.Field(pair.Key)==field);
                result.Relations[branch.RelationIds[0]]=new[]{branch.OwnerId,branch.ModelId,
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    reverse.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                result.RelationFields[branch.RelationIds[0]]=field;
                // An operand has no rectangle of its own: the product reads back an empty
                // geometry and keeps only the guard and the offset from the frame's top.
                result.Shapes[branch.ShapeId]=PumlBuild.Json(new string[0])
                    +PumlBuild.Json(new[]{branch.Guard,branch.Position});
                result.ShapeModels[branch.ShapeId]=branch.ModelId;
            }
        }
        foreach(var note in prepared.AddedNotes)
        {
            result.Models[note.ModelId]=PumlBuild.Json(new[]{note.Metaclass,note.Name,note.OwnerId,"False"});
            for(int i=0;i<note.RelationIds.Length;i++)
            {
                // A ref's lanes already hold other references, so both ends are counted.
                string source=note.RelationSources[i],target=note.RelationTargets[i],field=note.RelationFields[i];
                int index=result.Relations.Count(pair=>pair.Value[0]==source && result.Field(pair.Key)==field);
                int reverse=result.Relations.Count(pair=>pair.Value[1]==target && result.Field(pair.Key)==field);
                result.Relations[note.RelationIds[i]]=new[]{source,target,
                    index.ToString(System.Globalization.CultureInfo.InvariantCulture),reverse.ToString(System.Globalization.CultureInfo.InvariantCulture)};
                result.RelationFields[note.RelationIds[i]]=field;
            }
            // A note or ref reads back as its rectangle followed by its text.
            result.Shapes[note.ShapeId]=note.Geometry+note.Text;
            result.ShapeModels[note.ShapeId]=note.ModelId;
        }
        // A wrapped message gains only the operand's reference, appended in run order.
        foreach(var move in prepared.MovedMessages)
        {
            int index=result.Relations.Count(pair=>pair.Value[0]==move.OperandId && result.Field(pair.Key)==move.Field);
            int reverse=result.Relations.Count(pair=>pair.Value[1]==move.ModelId && result.Field(pair.Key)==move.Field);
            result.Relations[move.RelationId]=new[]{move.OperandId,move.ModelId,
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                reverse.ToString(System.Globalization.CultureInfo.InvariantCulture)};
            result.RelationFields[move.RelationId]=move.Field;
        }
        foreach(var lane in prepared.AddedParticipants)
        {
            result.Models[lane.ModelId]=PumlBuild.Json(new[]{lane.Metaclass,lane.Name,lane.OwnerId,"False"});
            string field=result.Field(lane.TemplateRelationId);
            if(field.Length==0)throw new InvalidOperationException("S230: 追加する参加者の関連の種別情報が不足しています。");
            int index=result.Relations.Count(pair=>pair.Value[0]==lane.OwnerId && result.Field(pair.Key)==field);
            result.Relations[lane.RelationId]=new[]{lane.OwnerId,lane.ModelId,
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),"0"};
            result.RelationFields[lane.RelationId]=field;
            string sample;
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
            if(prepared.AddedExecutions.Any(a=>a.RelationIds.Contains(id))
                || prepared.AddedParticipants.Any(a=>a.RelationId==id)
                || prepared.AddedMessages.Any(a=>a.RelationIds.Contains(id))
                || prepared.AddedFragments.Any(a=>a.RelationIds.Contains(id))
                || prepared.AddedOperands.Any(a=>a.RelationIds.Contains(id))
                || prepared.MovedMessages.Any(a=>a.RelationId==id)
                || prepared.AddedNotes.Any(a=>a.RelationIds.Contains(id)))continue;
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
            var removed=new HashSet<string>(prepared.DeleteIds.Concat(prepared.DeleteParticipantIds)
                .Concat(prepared.DeleteMessageIds).Concat(prepared.DeleteFrameIds).Concat(prepared.DeleteNoteIds).Concat(prepared.DeleteRefIds));
            foreach(string id in removed)result.Models.Remove(id);
            foreach(string id in prepared.DeleteMessageIds)result.Ports.Remove(id);
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
