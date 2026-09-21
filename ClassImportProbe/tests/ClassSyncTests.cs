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

        // Several renames in one class: an attribute and an operation (real-machine case), and
        // two attributes of different types, each resolve to updates rather than add/delete.
        var twoRenames = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("- state : int", "- state2 : int").Replace("+ start(mode : int)", "+ start2(mode : int)"));
        var twoPlan = Plan(baseline, twoRenames);
        Check(twoPlan.Changes.Count == 2 && twoPlan.Changes.All(c => c.Action == "update" && c.Detail == "name") && twoPlan.Changes.Count(c => c.Kind == "attribute") == 1 && twoPlan.Changes.Count(c => c.Kind == "operation") == 1, "attribute and operation renamed together: " + Describe(twoPlan));
        var twoAttributes = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("- state : int", "- state2 : int").Replace("{static} count : int", "{static} count2 : int"));
        var twoAttrPlan = Plan(baseline, twoAttributes);
        Check(twoAttrPlan.Changes.Count == 2 && twoAttrPlan.Changes.All(c => c.Action == "update" && c.Kind == "attribute" && c.Detail == "name"), "two attributes renamed together: " + Describe(twoAttrPlan));
        Check(ClassTextPreflight.Check(baseline, twoRenames, twoPlan).Edits.Count == 2, "two renames pass the preflight");

        // Text-update preflight: only member renames pass; everything else is a stop reason.
        var renameGate = ClassTextPreflight.Check(baseline, Load(samples, "rename-attribute.puml"), rename);
        Check(renameGate.Candidate && renameGate.Edits.Count == 1 && renameGate.Edits[0].NameChanged && !renameGate.Edits[0].TypeChanged && renameGate.Edits[0].OldText == "state" && renameGate.Edits[0].NewText == "status" && renameGate.Edits[0].Line == 7, "rename preflight: " + renameGate.Summary());
        Check(baseline.Elements.Any(e => e.Id == renameGate.Edits[0].CurrentId && e.Text == "state"), "rename preflight identity");
        var sameGate = ClassTextPreflight.Check(baseline, baseline, same);
        Check(!sameGate.Candidate && sameGate.Reasons.Count == 1, "no-change preflight: " + sameGate.Summary());
        // Links between existing classes pass the preflight as adds or deletes; anything else stops.
        var linkDelete = ClassTextPreflight.Check(baseline, Load(samples, "delete-link.puml"), deleted);
        Check(linkDelete.Candidate && linkDelete.Links.Count == 1 && linkDelete.Links[0].Action == "delete" && linkDelete.Links[0].Field == "Uses" && linkDelete.Links[0].FromAlias == "Controller" && linkDelete.Links[0].ToAlias == "Mode", "link delete preflight: " + linkDelete.Summary());
        var linkAdd = ClassTextPreflight.Check(Load(samples, "delete-link.puml"), baseline, restored);
        Check(linkAdd.Candidate && linkAdd.Links.Count == 1 && linkAdd.Links[0].Action == "add" && linkAdd.Links[0].Field == "Uses" && linkAdd.Links[0].Line == 26, "link add preflight: " + linkAdd.Summary());
        var newClassLink = ClassTextPreflight.Check(baseline, Load(samples, "add-class.puml"), added);
        Check(!newClassLink.Candidate && newClassLink.Links.Count == 0 && newClassLink.Reasons.Any(x => x.Contains("既存のクラス")), "link to a new class stops: " + newClassLink.Summary());
        var multGate = ClassTextPreflight.Check(baseline, ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("\"0..*\" IDriver", "\"1..*\" IDriver")), mult);
        Check(!multGate.Candidate && multGate.Reasons.Count == 1 && multGate.Reasons[0].Contains("多重度"), "multiplicity change stops: " + multGate.Summary());
        var anonymous = ClassDocument.Parse("@startuml\nclass \"A\" as A\nclass \"B\" as B\n\nA --> B\n\n@enduml\n");
        var anonymousGone = ClassDocument.Parse("@startuml\nclass \"A\" as A\nclass \"B\" as B\n@enduml\n");
        var anonymousGate = ClassTextPreflight.Check(anonymous, anonymousGone, Plan(anonymous, anonymousGone));
        Check(!anonymousGate.Candidate && anonymousGate.Reasons.Count == 1, "an unlabeled line parsed from text has no field to remove: " + anonymousGate.Summary());
        anonymous.Elements.Single(e => e.Kind == "link").Attributes["field"] = "___anonymous___owner_related_to_x";
        var systemFieldGate = ClassTextPreflight.Check(anonymous, anonymousGone, Plan(anonymous, anonymousGone));
        Check(systemFieldGate.Candidate && systemFieldGate.Links.Count == 1 && systemFieldGate.Links[0].Field == "___anonymous___owner_related_to_x", "a snapshot line keeps its system field name for the delete: " + systemFieldGate.Summary());
        var connectorOnly = ClassDocument.Parse("@startuml\nclass \"A\" as A\nclass \"B\" as B\n\nA -- B : line\n\n@enduml\n");
        connectorOnly.Elements.Single(e => e.Kind == "link").Attributes["field"] = "";
        var connectorGate = ClassTextPreflight.Check(connectorOnly, anonymousGone, Plan(connectorOnly, anonymousGone));
        Check(!connectorGate.Candidate && connectorGate.Reasons.Count == 1 && connectorGate.Reasons[0].Contains("フィールドに対応しない"), "connector-only line stops: " + connectorGate.Summary());
        var unlabeledAdd = ClassTextPreflight.Check(anonymousGone, anonymous, Plan(anonymousGone, anonymous));
        Check(!unlabeledAdd.Candidate && unlabeledAdd.Reasons.Count == 1 && unlabeledAdd.Reasons[0].Contains("ロール名"), "unlabeled add stops: " + unlabeledAdd.Summary());
        var renameAndLink = Load(samples, "delete-link.puml");
        renameAndLink.Elements.Single(e => e.Kind == "attribute" && e.Text == "state").Text = "status";
        var bothGate2 = ClassTextPreflight.Check(baseline, renameAndLink, Plan(baseline, renameAndLink));
        Check(bothGate2.Candidate && bothGate2.Edits.Count == 1 && bothGate2.Links.Count == 1, "rename and link delete together: " + bothGate2.Summary());

        foreach (var name in new[] { "add-class.puml", "reorder-member.puml", "move-class.puml" })
        {
            var doc = Load(samples, name);
            var gate = ClassTextPreflight.Check(baseline, doc, Plan(baseline, doc));
            Check(!gate.Candidate && gate.Reasons.Count > 0 && gate.Edits.Count == 0, "preflight must stop for " + name + ": " + gate.Summary());
        }
        var typeGate = ClassTextPreflight.Check(baseline, Load(samples, "change-type.puml"), type);
        Check(typeGate.Candidate && typeGate.Edits.Count == 1 && typeGate.Edits[0].TypeChanged && !typeGate.Edits[0].NameChanged && typeGate.Edits[0].OldType == "int" && typeGate.Edits[0].NewType == "long", "type preflight: " + typeGate.Summary());
        var visibility = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("- state : int", "+ state : int").Replace("# stop()", "- stop()"));
        var visibilityGate = ClassTextPreflight.Check(baseline, visibility, Plan(baseline, visibility));
        Check(visibilityGate.Candidate && visibilityGate.Edits.Count == 2 && visibilityGate.Edits.All(e => e.VisibilityChanged && !e.NameChanged) && visibilityGate.VisibilityCount == 2, "visibility preflight: " + visibilityGate.Summary());
        var both = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("- state : int", "+ state2 : long"));
        var bothGate = ClassTextPreflight.Check(baseline, both, Plan(baseline, both));
        Check(bothGate.Candidate && bothGate.Edits.Count == 1 && bothGate.Edits[0].NameChanged && bothGate.Edits[0].VisibilityChanged && bothGate.Edits[0].TypeChanged, "name, visibility and type together: " + bothGate.Summary());
        var defaultValue = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("[0..1] = 0", "[0..1] = 1"));
        var defaultGate = ClassTextPreflight.Check(baseline, defaultValue, Plan(baseline, defaultValue));
        Check(!defaultGate.Candidate && defaultGate.Reasons.Count == 1 && defaultGate.Reasons[0].Contains("default"), "default value is out of scope: " + defaultGate.Summary());
        var returnType = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("start(mode : int) : bool", "start(mode : int) : int"));
        var returnGate = ClassTextPreflight.Check(baseline, returnType, Plan(baseline, returnType));
        Check(!returnGate.Candidate && returnGate.Reasons.Count == 1 && returnGate.Reasons[0].Contains("returnType"), "operation return type is out of scope: " + returnGate.Summary());
        var noSymbol = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    - state : int", "    state : int"));
        var noSymbolGate = ClassTextPreflight.Check(baseline, noSymbol, Plan(baseline, noSymbol));
        Check(!noSymbolGate.Candidate && noSymbolGate.Reasons.Count == 1, "removing the visibility symbol stops: " + noSymbolGate.Summary());
        var mixed = Load(samples, "rename-attribute.puml");
        mixed.Elements.Single(e => e.Kind == "link" && e.Text == "Uses").Attributes["toMultiplicity"] = "1";
        var mixedGate = ClassTextPreflight.Check(baseline, mixed, Plan(baseline, mixed));
        Check(!mixedGate.Candidate && mixedGate.Edits.Count == 1 && mixedGate.Reasons.Count == 1, "mixed plan stops as a whole: " + mixedGate.Summary());
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

        // Member adds and deletes pass the preflight when the owner exists; arguments, return
        // types, multiplicity and default values stop.
        var addAttr = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    + {static} count : int\n", "    + {static} count : int\n    - extra : long\n"));
        var addAttrGate = ClassTextPreflight.Check(baseline, addAttr, Plan(baseline, addAttr));
        Check(addAttrGate.Candidate && addAttrGate.Members.Count == 1 && addAttrGate.Members[0].Action == "add" && addAttrGate.Members[0].Kind == "attribute" && addAttrGate.Members[0].Text == "extra" && addAttrGate.Members[0].Type == "long" && addAttrGate.Members[0].Visibility == "-" && addAttrGate.Members[0].OwnerAlias == "Controller" && addAttrGate.Members[0].Line == 9, "attribute add preflight: " + addAttrGate.Summary());
        var delAttrGate = ClassTextPreflight.Check(addAttr, baseline, Plan(addAttr, baseline));
        Check(delAttrGate.Candidate && delAttrGate.Members.Count == 1 && delAttrGate.Members[0].Action == "delete" && delAttrGate.Members[0].Text == "extra" && addAttr.Elements.Any(e => e.Id == delAttrGate.Members[0].CurrentId), "attribute delete preflight: " + delAttrGate.Summary());
        var addOp = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    # stop()\n", "    # stop()\n    + reset()\n"));
        var addOpGate = ClassTextPreflight.Check(baseline, addOp, Plan(baseline, addOp));
        Check(addOpGate.Candidate && addOpGate.Members.Count == 1 && addOpGate.Members[0].Kind == "operation" && addOpGate.Members[0].Text == "reset", "operation add preflight: " + addOpGate.Summary());
        var addOpArgs = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    # stop()\n", "    # stop()\n    + reset(mode : int)\n"));
        var addOpArgsGate = ClassTextPreflight.Check(baseline, addOpArgs, Plan(baseline, addOpArgs));
        Check(!addOpArgsGate.Candidate && addOpArgsGate.Reasons.Count == 1 && addOpArgsGate.Reasons[0].Contains("引数"), "operation with arguments stops: " + addOpArgsGate.Summary());
        var addDefault = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    + {static} count : int\n", "    + {static} count : int\n    - extra : long = 1\n"));
        var addDefaultGate = ClassTextPreflight.Check(baseline, addDefault, Plan(baseline, addDefault));
        Check(!addDefaultGate.Candidate && addDefaultGate.Reasons.Count == 1, "attribute with default stops: " + addDefaultGate.Summary());
        Check(!ClassTextPreflight.Check(baseline, Load(samples, "add-class.puml"), added).Candidate, "a new class still stops");

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
