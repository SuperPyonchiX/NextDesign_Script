public static class ClassSyncTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception("ClassSyncTests: " + message); }
    static ClassDocument Load(string samples, string name) { return ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, name), new UTF8Encoding(false, true))); }
    static ClassSyncPlan Plan(ClassDocument current, ClassDocument desired) { return ClassSyncPlan.Build(current, desired, () => Guid.NewGuid().ToString()); }
    static string Describe(ClassSyncPlan plan) { return string.Join("; ", plan.Changes.Select(c => c.Action + " " + c.Kind + " " + c.Detail + " line=" + c.Line).ToArray()); }
    public static void Run(string samples)
    {
        var baseline = Load(samples, "roundtrip.puml");
        var same = Plan(baseline, Load(samples, "roundtrip.puml"));
        Check(same.Changes.Count == 0, "identical input produced changes: " + Describe(same));
        Check(same.Identities.Count == baseline.Elements.Count && same.Identities.Values.All(id => baseline.Elements.Any(e => e.Id == id)), "identities not preserved");

        var rename = Plan(baseline, Load(samples, "rename-attribute.puml"));
        Check(rename.Changes.Count == 1 && rename.Changes[0].Action == "update" && rename.Changes[0].Kind == "attribute" && rename.Changes[0].Detail == "name", "rename: " + Describe(rename));
        Check(baseline.Elements.Any(e => e.Id == rename.Changes[0].Id && e.Text == "state"), "rename keeps the current identity");

        var type = Plan(baseline, Load(samples, "change-type.puml"));
        Check(type.Changes.Count == 1 && type.Changes[0].Action == "update" && type.Changes[0].Detail == "type", "type change: " + Describe(type));

        var added = Plan(baseline, Load(samples, "add-class.puml"));
        Check(added.Changes.All(c => c.Action == "add"), "add-class has non-add changes: " + Describe(added));
        Check(added.Changes.Count(c => c.Kind == "class") == 1 && added.Changes.Count(c => c.Kind == "operation") == 1 && added.Changes.Count(c => c.Kind == "link") == 1 && added.Changes.Count == 3, "add-class counts: " + Describe(added));
        Check(added.Changes.Single(c => c.Kind == "class").Line == 16, "add-class line number: " + Describe(added));
        var removed = Plan(Load(samples, "add-class.puml"), baseline);
        Check(removed.Changes.Count == 3 && removed.Changes.All(c => c.Action == "delete"), "reverse of add is delete: " + Describe(removed));

        var deleted = Plan(baseline, Load(samples, "delete-link.puml"));
        Check(deleted.Changes.Count == 1 && deleted.Changes[0].Action == "delete" && deleted.Changes[0].Kind == "link", "delete-link: " + Describe(deleted));
        var restored = Plan(Load(samples, "delete-link.puml"), baseline);
        Check(restored.Changes.Count == 1 && restored.Changes[0].Action == "add" && restored.Changes[0].Kind == "link", "reverse of delete-link: " + Describe(restored));

        var reorder = Plan(baseline, Load(samples, "reorder-member.puml"));
        Check(reorder.Changes.Count > 0 && reorder.Changes.All(c => c.Action == "move" && c.Kind == "attribute" && c.Detail == "order"), "reorder: " + Describe(reorder));

        // Moving a class between packages keeps its identity and reports a parent move.
        var moved = Plan(baseline, Load(samples, "move-class.puml"));
        Check(moved.Changes.Count == 1 && moved.Changes[0].Action == "move" && moved.Changes[0].Kind == "class" && moved.Changes[0].Detail == "parent", "move-class: " + Describe(moved));

        // The arrow is presentation derived from the field name in Next Design, so it is not a difference.
        var arrow = Plan(baseline, ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("Controller ..> Mode : Uses", "Controller --> Mode : Uses")));
        Check(arrow.Changes.Count == 0, "arrow change: " + Describe(arrow));
        var mult = Plan(baseline, ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("\"0..*\" IDriver", "\"1..*\" IDriver")));
        Check(mult.Changes.Count == 1 && mult.Changes[0].Action == "update" && mult.Changes[0].Kind == "link" && mult.Changes[0].Detail == "toMultiplicity", "multiplicity change: " + Describe(mult));

        // Renaming a class keeps members and links attached to the same identity.
        var classRename = Plan(baseline, ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("\"制御部\"", "\"制御装置\"")));
        Check(classRename.Changes.Count == 1 && classRename.Changes[0].Action == "update" && classRename.Changes[0].Kind == "class" && classRename.Changes[0].Detail == "name", "class rename: " + Describe(classRename));

        // A member whose name holds parentheses reads as an operation from text; the model side
        // says attribute. The rendered line is identical, so it is not a difference.
        var fromText = ClassDocument.Parse("@startuml\nclass \"A\" as A {\n  + 温度(℃) : float\n  + g() : int\n}\n@enduml\n");
        var fromModel = ClassDocument.Parse("@startuml\nclass \"A\" as A {\n  + t : float\n  + g() : int\n}\n@enduml\n");
        var t = fromModel.Elements.Single(e => e.Kind == "attribute");
        t.Text = "温度(℃)";
        Check(fromText.Elements.Single(e => e.Kind == "operation" && e.Text == "温度").Attr("parameters") == "℃", "text side reads an operation");
        var kinds = Plan(fromModel, fromText);
        Check(kinds.Changes.Count == 0, "kind-only difference: " + Describe(kinds));
        t.Attributes["type"] = "double";
        var kindsAndType = Plan(fromModel, fromText);
        Check(kindsAndType.Changes.Count == 1 && kindsAndType.Changes[0].Action == "update", "kind and text difference: " + Describe(kindsAndType));

        // Anonymous fields print the same line twice; equal counts pair up, unequal counts differ by the surplus.
        var twice = "@startuml\nclass \"A\" as A\nclass \"B\" as B\n\nA --> B\nA --> B\n\n@enduml\n";
        Check(Plan(ClassDocument.Parse(twice), ClassDocument.Parse(twice)).Changes.Count == 0, "identical anonymous lines");
        var once = Plan(ClassDocument.Parse(twice), ClassDocument.Parse(twice.Replace("A --> B\nA --> B", "A --> B")));
        Check(once.Changes.Count == 1 && once.Changes[0].Action == "delete" && once.Changes[0].Kind == "link", "surplus anonymous line: " + Describe(once));

        // Two same-direction lines with different arrows pair by label even when the other
        // side drew both with the default arrow (real-machine case: Children and SubClasses).
        var drawn = ClassDocument.Parse("@startuml\nclass \"A\" as A\nclass \"B\" as B\n\nA o-- \"0..*\" B : Children\nA <|-- \"0..*\" B : SubClasses\n\n@enduml\n");
        var plain = ClassDocument.Parse("@startuml\nclass \"A\" as A\nclass \"B\" as B\n\nA --> \"0..*\" B : Children\nA --> \"0..*\" B : SubClasses\n\n@enduml\n");
        Check(Plan(plain, drawn).Changes.Count == 0 && Plan(drawn, plain).Changes.Count == 0, "arrow-only difference on paired lines: " + Describe(Plan(plain, drawn)));

        // Text-update preflight: only member renames pass; everything else is a stop reason.
        var renameGate = ClassTextPreflight.Check(baseline, Load(samples, "rename-attribute.puml"), rename);
        Check(renameGate.Candidate && renameGate.Renames.Count == 1 && renameGate.Renames[0].OldText == "state" && renameGate.Renames[0].NewText == "status" && renameGate.Renames[0].Line == 7, "rename preflight: " + renameGate.Summary());
        Check(baseline.Elements.Any(e => e.Id == renameGate.Renames[0].CurrentId && e.Text == "state"), "rename preflight identity");
        var sameGate = ClassTextPreflight.Check(baseline, baseline, same);
        Check(!sameGate.Candidate && sameGate.Reasons.Count == 1, "no-change preflight: " + sameGate.Summary());
        foreach (var name in new[] { "change-type.puml", "add-class.puml", "delete-link.puml", "reorder-member.puml", "move-class.puml" })
        {
            var doc = Load(samples, name);
            var gate = ClassTextPreflight.Check(baseline, doc, Plan(baseline, doc));
            Check(!gate.Candidate && gate.Reasons.Count > 0 && gate.Renames.Count == 0, "preflight must stop for " + name + ": " + gate.Summary());
        }
        var mixed = Load(samples, "rename-attribute.puml");
        mixed.Elements.Single(e => e.Kind == "link" && e.Text == "Uses").Text = "Depends";
        var mixedGate = ClassTextPreflight.Check(baseline, mixed, Plan(baseline, mixed));
        Check(!mixedGate.Candidate && mixedGate.Renames.Count == 1 && mixedGate.Reasons.Count == 1, "mixed plan stops as a whole: " + mixedGate.Summary());
        var classRenameGate = ClassTextPreflight.Check(baseline, ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("\"制御部\"", "\"制御装置\"")), classRename);
        Check(!classRenameGate.Candidate && classRenameGate.Reasons.Count == 1, "class rename is out of scope: " + classRenameGate.Summary());

        // Trial state machines: apply failure still rolls back; commit failure rolls back once.
        var order = new List<string>();
        var rt = new ClassRollbackTrial();
        rt.Run(() => { order.Add("apply"); throw new Exception("boom"); }, () => order.Add("rollback"), () => order.Add("verify"));
        Check(!rt.Applied && rt.RollbackReturned && rt.Restored && string.Join(",", order.ToArray()) == "apply,rollback,verify", "rollback trial order");
        order.Clear();
        var ct = new ClassCommitTrial();
        ct.Run(() => order.Add("apply"), () => { order.Add("commit"); throw new Exception("boom"); }, () => order.Add("rollback"), () => order.Add("verify"));
        Check(ct.Applied && !ct.Committed && ct.RollbackReturned && ct.Restored && string.Join(",", order.ToArray()) == "apply,commit,rollback,verify", "commit trial order");
        order.Clear();
        ct = new ClassCommitTrial();
        ct.Run(() => order.Add("apply"), () => order.Add("commit"), () => order.Add("rollback"), () => order.Add("verify"));
        Check(ct.Committed && string.Join(",", order.ToArray()) == "apply,commit", "committed trial does not roll back");

        // Summary and reasons are counts and line numbers only.
        string summary = ClassAudit.Summary(added, 2);
        Check(summary.Contains("差分候補 3件") && summary.Contains("class") && !summary.Contains("Logger"), "summary text: " + summary);
        string reasons = ClassAudit.Reasons(added);
        Check(reasons.Contains("add class 入力16行") && !reasons.Contains("Logger"), "reasons text: " + reasons);
        Check(ClassAudit.Summary(same, 0).StartsWith("差分候補なし", StringComparison.Ordinal), "no-change summary");

        var json = same.ToJson();
        Check(json.Contains("\"Changes\":[]") && json.Contains("\"Expected\""), "plan json");
    }
}
