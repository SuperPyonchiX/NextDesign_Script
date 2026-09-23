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
        // A new class with an operation and a link: the class, its member and the link all pass.
        var newClass = ClassTextPreflight.Check(baseline, Load(samples, "add-class.puml"), added);
        Check(newClass.Candidate && newClass.Classes.Count == 1 && newClass.Classes[0].Action == "add" && newClass.Classes[0].Text == "Logger" && newClass.Classes[0].SiblingAlias == "Controller" && newClass.Classes[0].ContainerAlias == "" && newClass.Members.Count == 1 && newClass.Links.Count == 1 && newClass.Links[0].ToAlias == "Logger",
            "new class preflight: candidate=" + newClass.Candidate + " classes=" + newClass.Classes.Count + " sibling=" + (newClass.Classes.Count > 0 ? newClass.Classes[0].SiblingAlias + "/" + newClass.Classes[0].ContainerAlias : "-") + " members=" + newClass.Members.Count + " links=" + newClass.Links.Count + " " + (newClass.Links.Count > 0 ? newClass.Links[0].ToAlias : "-") + " reasons=" + string.Join("|", newClass.Reasons.ToArray()));
        var removedClass = ClassTextPreflight.Check(Load(samples, "add-class.puml"), baseline, removed);
        Check(removedClass.Candidate && removedClass.Classes.Count == 1 && removedClass.Classes[0].Action == "delete" && removedClass.Members.Count == 0 && removedClass.Links.Count == 0, "class delete takes its members and links: " + removedClass.Summary());
        var renamedClass = ClassTextPreflight.Check(baseline, ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("\"制御部\"", "\"制御装置\"")), classRename);
        Check(renamedClass.Candidate && renamedClass.Edits.Count == 1 && renamedClass.Edits[0].Kind == "class" && renamedClass.Edits[0].NameChanged && renamedClass.Edits[0].NewText == "制御装置", "class rename passes: " + renamedClass.Summary());
        var rekeyed = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("class \"制御部\"", "abstract class \"制御部\""));
        var rekeyedGate = ClassTextPreflight.Check(baseline, rekeyed, Plan(baseline, rekeyed));
        Check(!rekeyedGate.Candidate && rekeyedGate.Reasons.Count == 1 && rekeyedGate.Reasons[0].Contains("キーワード"), "keyword change stops: " + rekeyedGate.Summary());
        var lonely = ClassDocument.Parse("@startuml\nclass \"A\" as A\n@enduml\n");
        var lonelyGate = ClassTextPreflight.Check(ClassDocument.Parse("@startuml\n@enduml\n"), lonely, Plan(ClassDocument.Parse("@startuml\n@enduml\n"), lonely));
        Check(!lonelyGate.Candidate && lonelyGate.Reasons.Count == 1 && lonelyGate.Reasons[0].Contains("既存のクラスがなく"), "new class without a sibling stops: " + lonelyGate.Summary());
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

        foreach (var name in new[] { "reorder-member.puml", "move-class.puml" })
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
        Check(defaultGate.Candidate && defaultGate.Edits.Count == 1 && defaultGate.Edits[0].DefaultChanged && defaultGate.Edits[0].NewDefault == "1", "default value update: " + defaultGate.Summary());
        var returnType = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("start(mode : int) : bool", "start(mode : int) : int <<NumericalType>>"));
        var returnGate = ClassTextPreflight.Check(baseline, returnType, Plan(baseline, returnType));
        Check(returnGate.Candidate && returnGate.Edits.Count == 1 && returnGate.Edits[0].ReturnTypeChanged && returnGate.Edits[0].NewReturnType == "int" && returnGate.Edits[0].ReturnTypeKind == "NumericalType", "return type update: " + returnGate.Summary());
        // One-sided attributes: an input without a return type or multiplicity never differs from a diagram that has one.
        var silent = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("start(mode : int) : bool", "start(mode : int)").Replace(" [0..1] = 0", " = 0"));
        Check(Plan(baseline, silent).Changes.Count == 0, "silent return type and multiplicity are not differences: " + Describe(Plan(baseline, silent)));
        Check(Plan(silent, baseline).Changes.Count == 2 && Plan(silent, baseline).Changes.All(c => c.Action == "update"), "stated return type and multiplicity are differences: " + Describe(Plan(silent, baseline)));
        var statedGate = ClassTextPreflight.Check(silent, baseline, Plan(silent, baseline));
        Check(statedGate.Candidate && statedGate.Edits.Count(e => e.MultiplicityChanged && e.NewMultiplicity == "0..1") == 1 && statedGate.Edits.Count(e => e.ReturnTypeChanged && e.NewReturnType == "bool") == 1, "multiplicity and return type edits: " + statedGate.Summary());
        var badMult = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("[0..1]", "[many]"));
        var badMultGate = ClassTextPreflight.Check(baseline, badMult, Plan(baseline, badMult));
        Check(!badMultGate.Candidate && badMultGate.Reasons.Count == 1 && badMultGate.Reasons[0].Contains("多重度"), "malformed multiplicity stops: " + badMultGate.Summary());
        var noSymbol = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    - state : int", "    state : int"));
        var noSymbolGate = ClassTextPreflight.Check(baseline, noSymbol, Plan(baseline, noSymbol));
        Check(!noSymbolGate.Candidate && noSymbolGate.Reasons.Count == 1, "removing the visibility symbol stops: " + noSymbolGate.Summary());
        var mixed = Load(samples, "rename-attribute.puml");
        mixed.Elements.Single(e => e.Kind == "link" && e.Text == "Uses").Attributes["toMultiplicity"] = "1";
        var mixedGate = ClassTextPreflight.Check(baseline, mixed, Plan(baseline, mixed));
        Check(!mixedGate.Candidate && mixedGate.Edits.Count == 1 && mixedGate.Reasons.Count == 1, "mixed plan stops as a whole: " + mixedGate.Summary());

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
        Check(addAttrGate.Members[0].InsertBeforeId == null, "attribute appended after the last attribute has no insert position: " + addAttrGate.Members[0].InsertBeforeId);
        var addFirst = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    - state : int [0..1] = 0\n", "    - first : long\n    - state : int [0..1] = 0\n"));
        var addFirstGate = ClassTextPreflight.Check(baseline, addFirst, Plan(baseline, addFirst));
        Check(addFirstGate.Candidate && addFirstGate.Members.Count == 1 && addFirstGate.Members[0].InsertBeforeId != null && baseline.Elements.Single(e => e.Id == addFirstGate.Members[0].InsertBeforeId).Text == "state", "attribute inserted before state: " + addFirstGate.Summary());
        var delAttrGate = ClassTextPreflight.Check(addAttr, baseline, Plan(addAttr, baseline));
        Check(delAttrGate.Candidate && delAttrGate.Members.Count == 1 && delAttrGate.Members[0].Action == "delete" && delAttrGate.Members[0].Text == "extra" && addAttr.Elements.Any(e => e.Id == delAttrGate.Members[0].CurrentId), "attribute delete preflight: " + delAttrGate.Summary());
        var addOp = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    # stop()\n", "    # stop()\n    + reset()\n"));
        var addOpGate = ClassTextPreflight.Check(baseline, addOp, Plan(baseline, addOp));
        Check(addOpGate.Candidate && addOpGate.Members.Count == 1 && addOpGate.Members[0].Kind == "operation" && addOpGate.Members[0].Text == "reset", "operation add preflight: " + addOpGate.Summary());
        var addOpArgs = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    # stop()\n", "    # stop()\n    + reset(mode, force : bool)\n"));
        var addOpArgsGate = ClassTextPreflight.Check(baseline, addOpArgs, Plan(baseline, addOpArgs));
        Check(addOpArgsGate.Candidate && addOpArgsGate.Members.Count == 1 && addOpArgsGate.Members[0].Parameters == "mode, force : bool", "operation with arguments passes: " + addOpArgsGate.Summary());
        Check(addOpArgs.Elements.Single(e => e.Kind == "operation" && e.Text == "reset").Attr("parameters") == "mode, force", "compared parameters are names only");
        var typedArgsSame = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("start(mode : int)", "start(mode : long <<PointerType>>)"));
        Check(Plan(baseline, typedArgsSame).Changes.Count == 0, "argument type text is not a difference: " + Describe(Plan(baseline, typedArgsSame)));
        Check(string.Join("|", ClassTextPreflight.ParameterNames("mode, force : bool")) == "mode|force" && string.Join("|", ClassTextPreflight.ParameterTypes("mode, force : bool")) == "|bool", "parameter parsing");
        var renameArg = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("start(mode : int)", "start(mode2 : int)"));
        var renameArgGate = ClassTextPreflight.Check(baseline, renameArg, Plan(baseline, renameArg));
        Check(renameArgGate.Candidate && renameArgGate.Edits.Count == 1 && renameArgGate.Edits[0].ParametersChanged && !renameArgGate.Edits[0].NameChanged, "argument rename is a parameters update: " + renameArgGate.Summary());
        var dupArg = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("start(mode : int)", "start(a, a)"));
        var dupArgGate = ClassTextPreflight.Check(baseline, dupArg, Plan(baseline, dupArg));
        Check(!dupArgGate.Candidate && dupArgGate.Reasons.Count == 1 && dupArgGate.Reasons[0].Contains("同じ名前"), "duplicate argument names stop: " + dupArgGate.Summary());
        var addDefault = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    + {static} count : int\n", "    + {static} count : int\n    - extra : long [1..*] = 1\n"));
        var addDefaultGate = ClassTextPreflight.Check(baseline, addDefault, Plan(baseline, addDefault));
        Check(addDefaultGate.Candidate && addDefaultGate.Members.Count == 1 && addDefaultGate.Members[0].Multiplicity == "1..*" && addDefaultGate.Members[0].Default == "1", "attribute with multiplicity and default: " + addDefaultGate.Summary());
        var addReturn = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("    # stop()\n", "    # stop()\n    + tick() : int\n"));
        var addReturnGate = ClassTextPreflight.Check(baseline, addReturn, Plan(baseline, addReturn));
        Check(addReturnGate.Candidate && addReturnGate.Members.Count == 1 && addReturnGate.Members[0].ReturnType == "int", "operation with return type: " + addReturnGate.Summary());

        // "<<Kind>>" after a type names the definition to create; it never counts as a difference.
        var kinded = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("- state : int [0..1] = 0", "- state : int <<StructureType>> [0..1] = 0"));
        var kindedState = kinded.Elements.Single(e => e.Kind == "attribute" && e.Text == "state");
        Check(kindedState.Attr("type") == "int" && kindedState.Attr("typeKind") == "StructureType", "type kind parsed: " + kindedState.Attr("type") + "/" + kindedState.Attr("typeKind"));
        Check(Plan(baseline, kinded).Changes.Count == 0, "type kind alone is not a difference: " + Describe(Plan(baseline, kinded)));
        var kindedNew = ClassDocument.Parse(File.ReadAllText(Path.Combine(samples, "roundtrip.puml"), new UTF8Encoding(false, true)).Replace("- state : int [0..1] = 0", "- state : Point <<StructureType>> [0..1] = 0"));
        var kindedGate = ClassTextPreflight.Check(baseline, kindedNew, Plan(baseline, kindedNew));
        Check(kindedGate.Candidate && kindedGate.Edits.Count == 1 && kindedGate.Edits[0].TypeChanged && kindedGate.Edits[0].NewType == "Point" && kindedGate.Edits[0].TypeKind == "StructureType", "type kind carried to the edit: " + kindedGate.Summary());
        Check(string.Join("|", ClassTextPreflight.ParameterTypes("a : T <<PointerType>>, b")) == "T|" && string.Join("|", ClassTextPreflight.ParameterTypeKinds("a : T <<PointerType>>, b")) == "PointerType|", "parameter type kinds");

        // Summary and reasons are counts and line numbers only.
        string summary = ClassAudit.Summary(added, 2);
        Check(summary.Contains("差分候補 3件") && summary.Contains("class") && !summary.Contains("Logger"), "summary text: " + summary);
        string reasons = ClassAudit.Reasons(added);
        Check(reasons.Contains("add class 入力16行") && !reasons.Contains("Logger"), "reasons text: " + reasons);
        Check(ClassAudit.Summary(same, 0).StartsWith("差分候補なし", StringComparison.Ordinal), "no-change summary");

        var json = same.ToJson();
        Check(json.Contains("\"Changes\":[]") && json.Contains("\"Expected\""), "plan json");

        DiagramDraft(baseline);
    }

    // A new diagram from PlantUML: seeds from the template, then an add-only plan.
    static void DiagramDraft(ClassDocument unused)
    {
        // The diagram once the boxes are on it: the Domain box, the existing 制御部 and the new
        // seed Logger, with the owner path above the box and the product's back-reference.
        string shown = "@startuml\ntitle クラス図 2\npackage \"Root\" {\npackage \"実装\" {\npackage \"OnBoardClientApp\" as OnBoardClientApp <<Domain_Impl>> {\n  class \"制御部\" as Controller <<Unit>> {\n    - state : int\n  }\n  class \"Logger\" as Logger <<Unit>>\n}\n}\n}\nLogger --> OnBoardClientApp\n@enduml\n";
        // Exported shape: owner path, then the box with an alias; 制御部 written without members.
        string exported = "@startuml\ntitle クラス図\npackage \"Root\" {\npackage \"実装\" {\npackage \"OnBoardClientApp\" as App <<Domain_Impl>> {\nclass \"制御部\" as C <<Unit>>\nclass \"Logger\" as L <<Unit>> {\n  + write(text : String)\n}\nclass \"Sink\" as S\n}\n}\n}\nC --> L : logger\nL --> S : sink\n@enduml\n";
        // Hand-written shape: one package block naming the box.
        string written = "@startuml\ntitle 新しい図\npackage \"OnBoardClientApp\" {\nclass \"制御部\" as C\nclass \"Logger\" as L {\n  + write(text : String)\n}\nclass \"Sink\" as S\n}\nC --> L : logger\nL --> S : sink\n@enduml\n";
        foreach (var input in new[] { exported, written })
        {
            var draft = ClassDiagramDraft.Plan(ClassDocument.Parse(input), "file");
            Check(draft.Reasons.Count == 0, "draft reasons: " + string.Join(" / ", draft.Reasons.ToArray()));
            var logger = draft.Items.Single(i => i.Name == "Logger");
            Check(string.Join("/", logger.Path) == (input == exported ? "Root/実装/OnBoardClientApp" : "OnBoardClientApp"), "draft path: " + string.Join("/", logger.Path));
            Check(draft.Items.Count(i => i.Container) == (input == exported ? 1 : 0), "draft boxes");
            // As the runtime resolves it: 制御部 exists, Logger is the new seed, Sink goes next to Logger.
            draft.Seeds.Add(new ClassDiagramDraft.Seed { Name = "制御部", Existing = true });
            draft.Seeds.Add(new ClassDiagramDraft.Seed { Name = "Logger" });
            draft.Anchors["制御部"] = "制御部"; draft.Anchors["Logger"] = "Logger"; draft.Anchors["Sink"] = "Logger";
            Check(draft.ExistingCount == 1 && draft.NewCount == 2, "draft counts: " + draft.ExistingCount + "/" + draft.NewCount);
            var current = ClassDocument.Parse(shown);
            var desired = ClassDocument.Parse(input);
            draft.Prepare(desired, current);
            desired.Validate();
            var plan = Plan(current, desired);
            Check(plan.Changes.All(c => c.Action == "add"), "draft plan adds only: " + Describe(plan));
            Check(plan.Changes.Count(c => c.Kind == "class") == 1 && plan.Changes.Count(c => c.Kind == "operation") == 1 && plan.Changes.Count(c => c.Kind == "link") == 2 && plan.Changes.Count == 4, "draft plan counts: " + Describe(plan));
            var gate = ClassTextPreflight.Check(current, desired, plan);
            Check(gate.Candidate && gate.Classes.Single().SiblingAlias == "L", "draft preflight: " + gate.Summary());
            Check(desired.Elements.Single(e => e.Kind == "class" && e.Text == "Sink").Attr("stereotype") == "Unit", "new class takes the anchor's stereotype");
        }
        Check(ClassDiagramDraft.Plan(ClassDocument.Parse("@startuml\nclass A\n@enduml\n"), "file").Reasons.Count == 1, "a class outside any package stops the draft");
        Check(ClassDiagramDraft.Plan(ClassDocument.Parse("@startuml\npackage \"P\" {\nclass A\n}\n@enduml\n"), "名前").Title == "名前", "file name as title");
    }
}
