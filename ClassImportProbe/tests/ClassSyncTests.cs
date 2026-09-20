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

        // Changing only the arrow or multiplicity of a link is an update, not delete plus add.
        var arrow = Plan(baseline, ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("Controller ..> Mode : Uses", "Controller --> Mode : Uses")));
        Check(arrow.Changes.Count == 1 && arrow.Changes[0].Action == "update" && arrow.Changes[0].Kind == "link" && arrow.Changes[0].Detail == "arrow", "arrow change: " + Describe(arrow));

        // Renaming a class keeps members and links attached to the same identity.
        var classRename = Plan(baseline, ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("\"制御部\"", "\"制御装置\"")));
        Check(classRename.Changes.Count == 1 && classRename.Changes[0].Action == "update" && classRename.Changes[0].Kind == "class" && classRename.Changes[0].Detail == "name", "class rename: " + Describe(classRename));

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
