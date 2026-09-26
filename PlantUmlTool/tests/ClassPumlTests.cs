public static class ClassPumlTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception("ClassPumlTests: " + message); }
    static string Load(string samples, string name) { return File.ReadAllText(Path.Combine(samples, name), new UTF8Encoding(false, true)); }
    public static void Run(string samples)
    {
        // Every sample is written in the exporter's grammar, so writing it back must be byte-identical.
        foreach (var file in Directory.GetFiles(samples, "*.puml").OrderBy(f => f, StringComparer.Ordinal))
        {
            string text = File.ReadAllText(file, new UTF8Encoding(false, true)).Replace("\r\n", "\n");
            var doc = ClassDocument.Parse(text);
            string written = ClassPumlWriter.Write(doc);
            Check(written == text, "writer output differs from " + Path.GetFileName(file) + "\n--- expected\n" + text + "--- actual\n" + written);
            var again = ClassDocument.Parse(written);
            Check(ClassPumlWriter.Write(again) == written, "writer is not idempotent for " + Path.GetFileName(file));
            Check(ClassSyncPlan.Build(doc, again, () => Guid.NewGuid().ToString()).Changes.Count == 0, "reparse produced differences for " + Path.GetFileName(file));
        }

        var round = ClassDocument.Parse(Load(samples, "roundtrip.puml"));
        Check(round.HasTitle && round.Root.Text == "機能構成", "title");
        Check(round.Elements.Count(e => e.Kind == "class") == 4, "class count");
        Check(round.Elements.Count(e => e.Kind == "package") == 1, "package count");
        Check(round.Elements.Count(e => e.Kind == "attribute") == 2, "attribute count");
        Check(round.Elements.Count(e => e.Kind == "operation") == 3, "operation count");
        Check(round.Elements.Count(e => e.Kind == "literal") == 2, "literal count");
        Check(round.Elements.Count(e => e.Kind == "link") == 3, "link count");
        var state = round.Elements.Single(e => e.Kind == "attribute" && e.Text == "state");
        Check(state.Attr("visibility") == "-" && state.Attr("type") == "int" && state.Attr("multiplicity") == "0..1" && state.Attr("default") == "0" && state.Attr("static") == "", "attribute fields");
        var count = round.Elements.Single(e => e.Kind == "attribute" && e.Text == "count");
        Check(count.Attr("static") == "true" && count.Attr("visibility") == "+", "static attribute");
        var start = round.Elements.Single(e => e.Kind == "operation" && e.Text == "start");
        Check(start.Attr("parameters") == "mode" && start.Attr("parameterTypes") == "mode : int" && start.Attr("returnType") == "bool" && start.Attr("visibility") == "+", "operation fields");
        var stop = round.Elements.Single(e => e.Kind == "operation" && e.Text == "stop");
        Check(stop.Attr("parameters") == "" && stop.Attr("returnType") == "" && stop.Attr("visibility") == "#", "bare operation");
        // A return type with its own parentheses (decltype) must not swallow the parameter list (K061).
        var declType = new ClassPumlParser().Parse("@startuml\nclass \"A\" as A {\n  + Connect(transportInfo, appName) : decltype(Skeleton::Connect(transportInfo,appName))\n  + Raw(x) : uint8 (raw)\n  + width (mm) : int\n}\n@enduml\n");
        var connect = declType.Elements.Single(e => e.Kind == "operation" && e.Text == "Connect");
        Check(connect.Attr("parameters") == "transportInfo, appName" && connect.Attr("returnType") == "decltype(Skeleton::Connect(transportInfo,appName))" && connect.Attr("parameterTypes") == "", "decltype return type");
        Check(ClassPumlWriter.Render(connect) == "+ Connect(transportInfo, appName) : decltype(Skeleton::Connect(transportInfo,appName))", "decltype round trip");
        Check(declType.Elements.Single(e => e.Kind == "operation" && e.Text == "Raw").Attr("returnType") == "uint8 (raw)", "parenthesised return type");
        Check(declType.Elements.Single(e => e.Text == "width (mm)").Kind == "attribute", "space before the parenthesis keeps an attribute");
        // An operation whose name has its own parentheses: the last group is the parameter list.
        var named = new ClassPumlParser().Parse("@startuml\nclass \"A\" as A {\n  - CallbackCancelTxqueue()()\n  + StopTimer(EVENT_IDX_P2_EXTEND_CLIENT)(index)\n  + Get(a)() : int\n}\n@enduml\n");
        var cancel = named.Elements.Single(e => e.Text == "CallbackCancelTxqueue()");
        Check(cancel.Kind == "operation" && cancel.Attr("parameters") == "" && ClassPumlWriter.Render(cancel) == "- CallbackCancelTxqueue()()", "name ending in ()");
        var stopTimer = named.Elements.Single(e => e.Text == "StopTimer(EVENT_IDX_P2_EXTEND_CLIENT)");
        Check(stopTimer.Kind == "operation" && stopTimer.Attr("parameters") == "index" && ClassPumlWriter.Render(stopTimer) == "+ StopTimer(EVENT_IDX_P2_EXTEND_CLIENT)(index)", "name with an argument-like group");
        var getter = named.Elements.Single(e => e.Text == "Get(a)");
        Check(getter.Kind == "operation" && getter.Attr("returnType") == "int", "name with a group, then a return type");
        var controller = round.Elements.Single(e => e.Kind == "class" && e.Text == "制御部");
        Check(controller.Attr("alias") == "Controller" && controller.Attr("keyword") == "class" && round.Elements.Single(e => e.Id == controller.Parent).Kind == "package", "class placement");
        Check(round.Elements.Single(e => e.Kind == "class" && e.Text == "Base").Attr("keyword") == "abstract class", "abstract keyword");
        Check(round.Elements.Single(e => e.Kind == "class" && e.Text == "Mode").Attr("keyword") == "enum", "enum keyword");
        var drivers = round.Elements.Single(e => e.Kind == "link" && e.Text == "drivers");
        Check(drivers.Attr("arrow") == "-->" && drivers.Attr("toMultiplicity") == "0..*" && drivers.Link("from") == controller.Id, "link fields");
        var stereo = round.Elements.Single(e => e.Kind == "class" && e.Text == "IDriver");
        Check(stereo.Attr("keyword") == "interface" && stereo.Attr("stereotype") == "", "interface");

        // A merged two-way line is split into two directed links; the writer merges them back.
        var two = ClassDocument.Parse(Load(samples, "bidirectional.puml"));
        var links = two.Elements.Where(e => e.Kind == "link").OrderBy(e => e.Order).ToList();
        Check(links.Count == 4, "two-way split count");
        Check(links[0].Text == "owner" && links[0].Attr("arrow") == "-->" && links[0].Attr("toMultiplicity") == "1", "two-way first");
        Check(links[1].Text == "items" && links[1].Attr("arrow") == "-->" && links[1].Attr("toMultiplicity") == "0..*" && links[1].Link("from") == links[0].Link("to"), "two-way second");
        Check(links[2].Text == "Children" && links[3].Text == "Children" && links[2].Attr("arrow") == "o--" && links[3].Attr("toMultiplicity") == "1", "same-label two-way");

        var anonymousFirst = ClassDocument.Parse("@startuml\nclass \"A\" as A\nclass \"B\" as B\nA \"0..*\" -- \"0..*\" B : / Parent\n@enduml\n");
        var pair = anonymousFirst.Elements.Where(e => e.Kind == "link").OrderBy(e => e.Order).ToList();
        Check(pair.Count == 2 && pair[0].Text == "" && pair[1].Text == "Parent" && pair[1].Link("from") == pair[0].Link("to"), "anonymous first label");
        Check(ClassPumlWriter.Write(anonymousFirst).Contains("A \"0..*\" -- \"0..*\" B :  / Parent"), "anonymous first label written back as the exporter does");

        var nested = ClassDocument.Parse(Load(samples, "package-nested.puml"));
        var packages = nested.Elements.Where(e => e.Kind == "package").ToList();
        Check(packages.Count == 3 && packages.Count(p => p.Text == "P") == 1, "packages merged by name");
        var x = nested.Elements.Single(e => e.Kind == "class" && e.Text == "X");
        Check(nested.Elements.Single(e => e.Id == x.Parent).Text == "Q" && nested.Elements.Single(e => e.Id == nested.Elements.Single(e2 => e2.Id == x.Parent).Parent).Text == "P", "nested package parent");
        var chip = nested.Elements.Single(e => e.Kind == "class" && e.Text == "Chip");
        Check(nested.Elements.Single(e => e.Id == chip.Parent).Text == "Board", "container node child");

        var deside = ClassDocument.Parse(Load(samples, "deside-arrows.puml"));
        Check(deside.Elements.Count(e => e.Kind == "link") == 5, "deside arrows accepted");
        Check(deside.Elements.Any(e => e.Kind == "link" && e.Attr("arrow") == "<|--") && deside.Elements.Any(e => e.Kind == "link" && e.Attr("arrow") == "--*"), "left-pointing arrows kept");

        // Hand-written declarations without quotes or alias are accepted and get a derived alias.
        var loose = ClassDocument.Parse("@startuml\nclass Foo\nclass \"Bar Baz\"\nFoo -> Bar_Baz\n@enduml\n");
        Check(loose.Elements.Single(e => e.Kind == "class" && e.Text == "Foo").Attr("alias") == "Foo", "unquoted alias");
        Check(loose.Elements.Single(e => e.Kind == "link").Attr("arrow") == "-->", "single dash normalized");

        // Parentheses inside a type or name do not turn an attribute into an operation.
        var parens = ClassDocument.Parse("@startuml\nclass \"A\" as A {\n  + raw : uint8 (unsigned)\n  + data(old) : int [0..1] = 3\n  - f(a : int, b : int) : bool\n  + g()\n  Idle (default)\n}\n@enduml\n");
        var raw = parens.Elements.Single(e => e.Text == "raw");
        Check(raw.Kind == "attribute" && raw.Attr("type") == "uint8 (unsigned)", "type with parentheses");
        var data = parens.Elements.Single(e => e.Text == "data(old)");
        Check(data.Kind == "attribute" && data.Attr("type") == "int" && data.Attr("multiplicity") == "0..1" && data.Attr("default") == "3", "name with parentheses");
        Check(parens.Elements.Single(e => e.Text == "f").Kind == "operation" && parens.Elements.Single(e => e.Text == "g").Kind == "operation", "operations");
        Check(parens.Elements.Single(e => e.Text == "Idle (default)").Kind == "attribute", "bare name with parentheses in a class");
        Check(ClassPumlWriter.Write(parens) == ClassPumlWriter.Write(ClassDocument.Parse(ClassPumlWriter.Write(parens))), "parentheses round trip");

        // Unknown lines and undeclared aliases stop with the input line number.
        // A link to an undeclared alias is skipped and reported, not rejected (the class was removed).
        var removedParser = new ClassPumlParser();
        var removedDoc = removedParser.Parse("@startuml\nclass \"A\" as A\nA --> B\n@enduml\n");
        Check(removedDoc.Elements.Count(e => e.Kind == "link") == 0 && removedParser.Ignored.Count == 1 && removedParser.Ignored[0].Contains("B"), "undeclared alias line skipped: " + string.Join("|", removedParser.Ignored.ToArray()));
        Expect("@startuml\nclass \"A\" as A\nfoo bar\n@enduml\n", "3行目");
        Expect("@startuml\nclass \"A\" as A {\n+ x : int\n", "閉じ括弧");
        Expect("@startuml\nclass \"A\" as A\nclass \"B\" as A\n@enduml\n", "重複");
    }
    static void Expect(string input, string fragment)
    {
        try { ClassDocument.Parse(input); }
        catch (InvalidOperationException ex) { Check(ex.Message.StartsWith("E120: ", StringComparison.Ordinal) && ex.Message.Contains(fragment), "unexpected error text: " + ex.Message); return; }
        throw new Exception("ClassPumlTests: invalid input accepted: " + input);
    }
}
