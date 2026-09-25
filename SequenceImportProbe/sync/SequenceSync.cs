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
                    // A message sent from a lane with no bar open, then an activate of that lane:
                    // the export writes a bar that opens with a send after that send (its activate
                    // waits for the next message after a deactivate), so this bar sent it.
                    if(previousEvent!=null && previousEvent.Kind=="message" && !previousEvent.Links.ContainsKey("sendExecution")
                        && previousEvent.Links["sender"].SequenceEqual(new[]{aliases[n.Left]}) && e.Links.ContainsKey("outer")==false
                        && !(previousEvent.Links.ContainsKey("receiveExecution") && previousEvent.Links["receiveExecution"].Contains(e.Id)))
                    {
                        previousEvent.Links["sendExecution"]=new[]{e.Id};
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
                    // A reply answers the bar its receiver called, not a bar another lane called
                    // on top of it. The export ends a short receive-only bar where the lane's next
                    // send is, so that bar can still be open here; it ended before this reply.
                    if(n.Kind=="reply" && n.Left!="[" && n.Right!="]" && !item.Links.ContainsKey("sendExecution")
                        && active.ContainsKey(n.Left) && active[n.Left].Count>1)
                    {
                        var stack=active[n.Left].ToArray();string callee=aliases[n.Right];
                        Func<SequenceElement,string> callerOf=b=>{string c;return b.Attributes.TryGetValue("caller",out c)?c:null;};
                        if(!closed(stack[0]) && callerOf(stack[0])!=null && callerOf(stack[0])!=callee)
                        {
                            int k=Array.FindIndex(stack,b=>!closed(b) && callerOf(b)==callee);
                            if(k>0)
                            {
                                string at=n.Line.ToString(System.Globalization.CultureInfo.InvariantCulture);
                                for(int i=0;i<=k;i++)
                                    if(!closed(stack[i])){stack[i].Attributes["closed"]="1";stack[i].Attributes["endParent"]=parent;stack[i].Attributes["end"]=at;}
                                item.Links["sendExecution"]=new[]{stack[k].Id};
                                item.Attributes["answers"]="1";
                            }
                        }
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
    static string TrimLines(string text){return string.Join("\n",(text??"").Replace("\r\n","\n").Replace('\r','\n').Split('\n').Select(l=>l.TrimEnd()));}
    public static SyncPlan Build(SequenceDocument current,SequenceDocument desired,Func<string> newId)
    {
        var before=current.Copy();var input=desired.Copy();
        foreach(var e in before.Elements.Concat(input.Elements).Where(e=>e.Kind=="note"))
        {
            e.Links["targets"]=new string[0];e.Links.Remove("anchors");e.Attributes["position"]="free";
            // The export drops what trails each line, which PlantUML cannot hold anyway.
            e.Text=TrimLines(e.Text);
        }
        var plan=SyncPlan.Build(before,input,newId);
        var originals=current.Elements.Where(e=>e.Kind=="note").ToDictionary(e=>e.Id);
        foreach(var e in plan.Expected.Elements.Where(e=>e.Kind=="note"))
        {
            SequenceElement original;if(!originals.TryGetValue(e.Id,out original))continue;
            foreach(var role in new[]{"targets","anchors"})
            {string[] values;if(original.Links.TryGetValue(role,out values))e.Links[role]=values.ToArray();else e.Links.Remove(role);}
            string position;if(original.Attributes.TryGetValue("position",out position))e.Attributes["position"]=position;else e.Attributes.Remove("position");
            // The same text but for what trails its lines keeps the diagram's own.
            if(TrimLines(original.Text)==e.Text)e.Text=original.Text;
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
