using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Lines;
using EtAlii.Adp.Specification.Fbl.Rules;

namespace EtAlii.Adp.Specification.Fbl.Planning;

/// <summary>
/// Plans a model change as one edit (FBL §6.4): the splices of every attribute set, element added
/// or removed, in the body's own conventions, or the reason the change cannot be made. Planning
/// writes nothing; <see cref="History.OpenBody"/> applies what it plans.
/// </summary>
internal static class EditPlanner
{
    public static PlanResult Plan(BodyReading reading, ModelChange change)
    {
        // A save writes the bytes that were read, which an unreadable body keeps (FBL §6.6, §7.5): nothing to splice.
        if (reading.Unreadable is not null) return change is ModelChange.Save ? new PlanResult.Planned(Edit.Empty) : new PlanResult.Refused("The file could not be read, so it is never written.");
        if (reading.Binding.ReadOnly is { } reason) return new PlanResult.Refused(reason.Length > 0 ? reason : "This file is read-only.");
        var plan = new Plan(reading);
        try
        {
            switch (change)
            {
                case ModelChange.Save:
                    break;
                case ModelChange.Set set:
                    PlanSet(plan, set);
                    break;
                case ModelChange.Add add:
                    PlanAdd(plan, add);
                    break;
                case ModelChange.AddAfter added:
                    PlanAddAfter(plan, added);
                    break;
                case ModelChange.Remove remove:
                    PlanRemove(plan, remove);
                    break;
                case ModelChange.Move move:
                    PlanMove(plan, move);
                    break;
                case ModelChange.StoreId store:
                    PlanStoreId(plan, store);
                    break;
                case ModelChange.Retype:
                    return new PlanResult.Refused("This file's binding cannot change an element's type; only a persistence plugin can.");
                case ModelChange.Place or ModelChange.Identify:
                    throw new ArgumentException("Placements and stored ids are edits of the registration, not of the body.", nameof(change));
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            var splices = plan.Ordered();
            return Governed(reading, splices) is { } governed ? new PlanResult.Refused(governed) : new PlanResult.Planned(new Edit(splices, plan.Snapshot));
        }
        catch (RefusedException refused)
        {
            return new PlanResult.Refused(refused.Message);
        }
    }

    /// <summary>
    /// FBL 0.17 §15.1: no splice touches the text of an entry whose rule uses a construct this library does not read, as a
    /// removal that takes it with another, or a move of what holds it, would; an insert right before or after it does not.
    /// The reason names the construct.
    /// </summary>
    private static string? Governed(BodyReading reading, IReadOnlyList<Splice> splices)
    {
        foreach (var element in reading.Elements)
        {
            if (element.Rule.Unread is not { } reason) continue;
            var own = element.Entry.Own;
            if (splices.Any(splice => splice.Start < own.End && own.Start < splice.End)) return reason;
        }
        return null;
    }

    private static ReadElement Element(BodyReading reading, string id) =>
        reading.Find(id) ?? throw new ArgumentException($"The model has no element or relation '{id}'.", nameof(id));

    private static void RefuseReadOnly(Rule rule, string gesture)
    {
        if (rule.ReadOnly is { } reason) Planning.Plan.Refuse(reason.Length > 0 ? reason : $"A {rule.Type} cannot be {gesture} in this file.");
    }

    // ---- set ----

    /// <summary>The id of an entry that has none, written where its rule reads it from.</summary>
    private static void PlanStoreId(Plan plan, ModelChange.StoreId store)
    {
        var reading = plan.Reading;
        var element = Element(reading, store.Id);
        RefuseReadOnly(element.Rule, "changed");
        if (element.Rule.Id?.From is not { } from)
        {
            Planning.Plan.Refuse($"A {element.Rule.Type} keeps no id of its own in this file.");
            return;
        }
        if (element.IdRead is { Present: true }) Planning.Plan.Refuse($"This {element.Rule.Type} has an id already.");
        if (store.NewId.Length == 0 || reading.Find(store.NewId) is not null) Planning.Plan.Refuse($"'{store.NewId}' cannot be the id of a {element.Rule.Type} here.");

        // The slot an id is read from, written as an attribute's is: the family puts it where an absent value goes.
        var binding = new AttributeBinding { Kind = from.Kind, Child = from.Child };
        reading.Family.Write(plan, element, [new SlotChange("id", binding, element.Rule, element.IdRead ?? SlotRead.Absent, store.NewId, false)]);
    }

    private static void PlanSet(Plan plan, ModelChange.Set set)
    {
        var reading = plan.Reading;
        var element = Element(reading, set.Id);
        RefuseReadOnly(element.Rule, "changed");
        var changes = new List<SlotChange>();
        string? newKey = null;
        foreach ((string attribute, object? requested) in set.Attributes)
        {
            var value = requested;
            Rule rule;
            AttributeBinding? binding;
            SlotRead read;
            if (element.IsRelation && attribute is "source" or "target")
            {
                binding = attribute == "source" ? element.Rule.Source : element.Rule.Target;
                var end = Element(reading, NewText.Plain(value, null));
                value = end.Key;
                read = (attribute == "source" ? element.SourceRead : element.TargetRead) ?? SlotRead.Absent;
                rule = element.Rule;
            }
            else
            {
                (rule, binding) = Binding(reading, element, attribute);
                if (binding is null && attribute == "id" && element.Rule.Id?.From is { } from)
                {
                    // An id read from the entry (FBL §3.3) is rewritten where it was read.
                    binding = new AttributeBinding { Kind = from.Kind, Child = from.Child };
                    read = element.IdRead ?? SlotRead.Absent;
                }
                else read = binding is null ? SlotRead.Absent : rule == element.Rule && element.Slots.TryGetValue(attribute, out var known) ? known : reading.ReadSlot(element.Candidate with { Rule = rule }, binding, element);
            }
            if (binding is null)
            {
                Planning.Plan.Refuse($"A {element.Rule.Type} keeps no '{attribute}' in this file.");
                return;
            }
            if (binding.Kind is SlotKind.Value or SlotKind.Parent || !read.Writable)
            {
                Planning.Plan.Refuse(read.Reason is { Length: > 0 } reason ? reason : $"The {attribute} of a {element.Rule.Type} cannot be changed in this file.");
            }
            // A value the binding's map writes as the empty text sets the slot to its empty value (FBL 5.2, empty and map),
            // as the Wardley map's inertia is cleared by false (fixture wardley-trailer/lenient-trailer).
            var empty = NewText.IsEmpty(value) || (binding.Flag && value is false) || (binding.Map is not null && NewText.Wire(binding, value, null) == "");
            if (empty && binding is { Flag: false, Empty: EmptyRule.Refuse }) Planning.Plan.Refuse(binding.EmptyRefusal ?? $"The {attribute} of a {element.Rule.Type} cannot be empty.");

            // FBL 0.10 §5.2: a value read from the secondary slot is written to the attribute's own slot, and the
            // secondary is removed in the same edit, whether the value was read from it or not.
            var secondary = Secondary(reading, element, rule, binding);
            read = read.Own ?? read;
            if (!read.Present && !empty && !binding.Flag && binding.Absent.TryGetValue(reading.Family.FamilyName, out var absent) && absent == AbsentRule.Refuse)
            {
                Planning.Plan.Refuse(binding.AbsentRefusal ?? $"The {reading.Binding.Name} file has no \"{SlotName(binding)}\" to rewrite.");
            }
            if (secondary is not null) changes.Add(secondary);
            if (empty && !read.Present && !binding.Flag) continue;
            if (!empty && IsKey(element, attribute, binding))
            {
                var key = NewText.Plain(value, null);
                if (key != element.Key)
                {
                    if (reading.Elements.Any(e => e != element && e.Rule == element.Rule && e.Key == key))
                    {
                        Planning.Plan.Refuse($"Another {element.Rule.Type} is already named '{key}'.");
                    }
                    newKey = key;
                }
            }
            changes.Insert(secondary is null ? changes.Count : changes.Count - 1, new SlotChange(attribute, binding, rule, read, value, empty));
        }
        if (changes.Count > 0) reading.Family.Write(plan, element, changes);
        if (newKey is not null) RewriteReferences(plan, element, newKey);
        if (element.Rule.SnapshotUndo) plan.Snapshot = true;
    }

    /// <summary>The removal of an attribute's <c>secondary</c> slot, when the entry has it (FBL 0.10 §5.2); null otherwise.</summary>
    private static SlotChange? Secondary(BodyReading reading, ReadElement element, Rule rule, AttributeBinding binding)
    {
        if (binding.Secondary is not { } secondary) return null;
        var slot = BodyReading.SecondarySlot(secondary);
        var read = reading.ReadSlot(element.Candidate with { Rule = rule }, slot, element);
        return read.Present ? new SlotChange(SlotName(secondary), slot, rule, read, null, true) : null;
    }

    private static string SlotName(Slot slot) => slot.Kind switch
    {
        SlotKind.Key(var key) => key,
        SlotKind.XmlAttribute(var attribute) => attribute,
        SlotKind.Group(var group) => group,
        SlotKind.Capture(var capture) => capture,
        _ => slot.Child ?? "value",
    };

    /// <summary>
    /// The binding that stores <paramref name="attribute"/> for the element: its own rule's, else
    /// that of another rule that matches the same entry (a timeline Moment given an end is written
    /// by the Period rule's binding, FBL §5.1).
    /// </summary>
    private static (Rule Rule, AttributeBinding? Binding) Binding(BodyReading reading, ReadElement element, string attribute)
    {
        if (element.Rule.Attribute(attribute) is { } own) return (element.Rule, own);
        foreach (var rule in reading.Binding.AllRules)
        {
            if (rule == element.Rule || rule.Attribute(attribute) is not { } other) continue;
            if (reading.Family.Candidates(rule).Any(c => c.Entry == element.Entry)) return (rule, other);
        }
        return (element.Rule, null);
    }

    private static bool IsKey(ReadElement element, string attribute, AttributeBinding binding)
    {
        if (attribute == element.KeyAttribute) return true;
        return element.KeyAttribute is null && element.Rule.Id?.From is { } from && SameSlot(from, binding);
    }

    private static bool SameSlot(Slot a, Slot b) => a.Kind == b.Kind && a.Child == b.Child && a.Word == b.Word;

    /// <summary>
    /// A rename (FBL §5.7): every reference to the old value is rewritten in the same edit, one
    /// <c>rewrite-reference</c> splice each, word by word inside a group.
    /// </summary>
    private static void RewriteReferences(Plan plan, ReadElement renamed, string newKey)
    {
        var reading = plan.Reading;
        var oldKey = renamed.Key;
        foreach (var other in reading.Elements)
        {
            if (other.IsRelation)
            {
                if (other.SourceElement == renamed) Rewrite(plan, other.SourceRead, oldKey, newKey);
                if (other.TargetElement == renamed) Rewrite(plan, other.TargetRead, oldKey, newKey);
            }
            foreach ((string name, AttributeBinding binding) in other.Rule.Attributes)
            {
                if (binding.Reference is not { } reference || !reference.To.Contains(renamed.Rule.Name)) continue;
                if (other == renamed && name == renamed.KeyAttribute) continue;
                if (!other.Slots.TryGetValue(name, out var read) || !read.Present) continue;
                if (read.Words is { Count: > 1 } ||(read.Words is { Count: 1 } && other.Attributes.GetValueOrDefault(name) is List<object?>))
                {
                    foreach (var word in read.Words!)
                    {
                        if (word.Text != oldKey) continue;
                        Rewrite(plan, new SlotRead(word.Text, word.Span, true, true) { Quote = word.Quoted ? "\"" : null }, oldKey, newKey);
                    }
                    continue;
                }
                Rewrite(plan, read, oldKey, newKey);
            }
        }
    }

    private static void Rewrite(Plan plan, SlotRead? read, string oldKey, string newKey)
    {
        if (read is not { Present: true, Span: { } span } || read.Written != oldKey || plan.Touches(span)) return;
        plan.Add(SpliceOperation.RewriteReference, span, plan.Reading.Family.Format(read, null, newKey));
    }

    // ---- add ----

    /// <summary>An add put on a line of its own right after the entry it names. Only the lines and grammar families' entries can be, and an entry of any other family is refused.</summary>
    private static void PlanAddAfter(Plan plan, ModelChange.AddAfter added)
    {
        var anchor = Element(plan.Reading, added.After);
        if (plan.Reading.Family is not (LinesFamily or Grammar.GrammarFamily)) Planning.Plan.Refuse($"This file's binding cannot add a {added.Type} at that position.");
        PlanAdd(plan, new ModelChange.Add(added.Type, null, added.Attributes), anchor);
    }

    private static void PlanAdd(Plan plan, ModelChange.Add add, ReadElement? after = null)
    {
        var reading = plan.Reading;
        // A declared binding appends where its insert rule says; a position among siblings is a plugin's to honour,
        // or a family's whose nesting is its indentation (FBL 0.7 §6.3).
        if (add.Index is not null && !reading.Family.PlacesByPosition) Planning.Plan.Refuse($"This file's binding cannot add a {add.Type} at a position; only a persistence plugin can.");
        var rules = reading.Binding.AllRules.Where(r => r.Type == add.Type && (add.Slot is null || r.Parent?.Slot == add.Slot)).ToList();
        if (rules.Count == 0 && BlockInsert(reading.Binding, add.Type) is { } block) rules.Add(block);
        if (rules.Count == 0) Planning.Plan.Refuse($"This file has no place for a {add.Type}.");
        // Several rules may write one type (a cell's items are options or rows; a filter's
        // conditions are written at each level of nesting). The one meant is a rule the parent
        // given can contain, where any is, and among those the one that binds the most of the
        // attributes given, the first of them on a tie.
        var container = add.ParentId is { } containerId ? reading.Find(containerId) : null;
        var insertable = rules.Where(r => r.Insert is not null).ToList();
        var contained = insertable.Where(r => container is not null && r.Parent?.Rules.Contains(container.Rule.Name) == true).ToList();
        var ordered = (contained.Count > 0 ? contained : insertable)
            .OrderByDescending(r => add.Attributes.Keys.Count(name => r.Attribute(name) is not null))
            .ToList();
        // FBL 0.10 §6.2: of the rules of one type, the first whose insert's when holds plans the add; when none does,
        // the first refuses it, with its own sentence.
        var rule = ordered.FirstOrDefault(r => r.Insert!.When is not { } condition || reading.InsertAllowed(r, condition, InsertedAttributes(r, add))) ?? ordered.FirstOrDefault();
        if (rule is null)
        {
            Planning.Plan.Refuse(rules[0].ReadOnly is { Length: > 0 } text ? text : $"A {add.Type} cannot be added to this file.");
            return;
        }
        RefuseReadOnly(rule, "added");
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, object? value) in add.Attributes)
        {
            if (rule.IsRelation && name is "source" or "target") continue;
            values[name] = value;
        }
        // An empty value refused for a set is refused for an add too (FBL 0.6 §6.2), but for an emit,
        // which follows its own rule for optional segments.
        foreach ((string name, object? value) in values)
        {
            if (rule.Insert!.Emit is null && rule.Attribute(name) is { Flag: false, Empty: EmptyRule.Refuse } refused && NewText.IsEmpty(value))
            {
                Planning.Plan.Refuse(refused.EmptyRefusal ?? $"The {name} of a {add.Type} cannot be empty.");
            }
        }
        if (rule.Insert!.When is { } when && !reading.InsertAllowed(rule, when, values))
        {
            Planning.Plan.Refuse(rule.Insert.WhenRefusal ?? $"This {add.Type} cannot be added to the file.");
        }
        ReadElement? source = null;
        ReadElement? target = null;
        if (rule.IsRelation)
        {
            source = End(reading, add, "source");
            target = End(reading, add, "target");
        }
        ReadElement? parent = add.ParentId is { } parentId ? Element(reading, parentId) : null;
        if (parent is not null && rule.Parent is { } containment && !containment.Rules.Contains(parent.Rule.Name))
        {
            Planning.Plan.Refuse($"A {add.Type} cannot be placed inside a {parent.Rule.Type}.");
        }
        if (rule.Id?.From is { } from && add.Id is { } id && reading.Elements.Any(e => e.Rule.Id?.From is not null && e.Id == id))
        {
            Planning.Plan.Refuse($"Another element already has the id '{id}'.");
            _ = from;
        }
        reading.Family.Insert(plan, new InsertRequest(rule, add.Id, values, parent, source, target, add.Index, after));
        if (rule.SnapshotUndo) plan.Snapshot = true;
    }

    /// <summary>
    /// A block rule of the grammar family that an add names as its type, with its <c>insert</c> (FBL 0.11 §4.9): a rule of
    /// that name that selects the block's entries and binds no attribute, so that its entry is planned and undone as an
    /// element's is while it produces no element; the add's attributes are the values of the emit's placeholders.
    /// </summary>
    private static Rule? BlockInsert(FblBinding binding, string type) =>
        binding.Body.Family == Family.Grammar && binding.Blocks.FirstOrDefault(block => block.Name == type && block is { Insert: not null, At: not null }) is { } named
            ? new Rule { Name = named.Name, Type = named.Name, At = named.At, Insert = named.Insert }
            : null;

    /// <summary>The attributes an add gives, as a rule's insert <c>when</c> sees them: a relation's ends are not among them.</summary>
    private static Dictionary<string, object?> InsertedAttributes(Rule rule, ModelChange.Add add) =>
        add.Attributes.Where(given => !(rule.IsRelation && given.Key is "source" or "target")).ToDictionary(given => given.Key, given => given.Value, StringComparer.Ordinal);

    private static ReadElement End(BodyReading reading, ModelChange.Add add, string end)
    {
        if (!add.Attributes.TryGetValue(end, out var value) || value is null)
        {
            Planning.Plan.Refuse($"A {add.Type} needs a {end}.");
        }
        var id = NewText.Plain(value, null);
        var element = reading.Find(id);
        if (element is null || element.IsRelation) Planning.Plan.Refuse($"The {end} '{id}' names no element.");
        return element!;
    }

    // ---- move ----

    /// <summary>
    /// A reorder among siblings (FBL §5.5, §6.4): the entry is removed where it is and inserted
    /// where it goes, carrying its own bytes - its comments and unbound keys with it.
    /// </summary>
    private static void PlanMove(Plan plan, ModelChange.Move move)
    {
        var reading = plan.Reading;
        var element = Element(reading, move.Id);
        RefuseReadOnly(element.Rule, "moved");
        if (element.Rule.Insert is null || element.Rule.Remove is null)
        {
            Planning.Plan.Refuse($"A {element.Rule.Type} cannot be moved in this file.");
        }
        ReadElement? parent = move.NewParentId is { } parentId ? Element(reading, parentId) : null;
        if (reading.Family.PlacesByPosition)
        {
            PlanMoveTo(plan, move, element, parent);
            return;
        }
        if (parent != element.Parent)
        {
            Planning.Plan.Refuse($"This file's binding cannot move a {element.Rule.Type} to another parent; only a persistence plugin can.");
        }
        var siblings = reading.Elements.Where(other => AreSiblings(other, element)).ToList();
        var current = siblings.IndexOf(element);
        var index = move.Index < 0 || move.Index > siblings.Count ? siblings.Count : move.Index;
        if (index == current || index == current + 1) return; // It is there already: no splice.
        reading.Family.Move(plan, element, index < siblings.Count ? siblings[index] : null, siblings[^1]);
        if (element.Rule.SnapshotUndo) plan.Snapshot = true;
    }

    /// <summary>
    /// A move in a family that places by position (FBL 0.7 §5.5, §6.3), to a sibling's place under the same
    /// parent or under another: not into the element itself or anything it contains, and not into a parent its
    /// rule cannot be nested in. The index counts the siblings before the move; the family is given it as they
    /// are without the element.
    /// </summary>
    private static void PlanMoveTo(Plan plan, ModelChange.Move move, ReadElement element, ReadElement? parent)
    {
        var reading = plan.Reading;
        for (var enclosing = parent; enclosing is not null; enclosing = enclosing.Parent)
        {
            if (enclosing == element) Planning.Plan.Refuse($"A {element.Rule.Type} cannot be moved into itself.");
        }
        if (parent is not null && element.Rule.Parent is { } containment && !containment.Rules.Contains(parent.Rule.Name))
        {
            Planning.Plan.Refuse($"A {element.Rule.Type} cannot be placed inside a {parent.Rule.Type}.");
        }
        var siblings = reading.Elements.Where(other => other.Parent == parent && other.Rule.Parent?.Slot == element.Rule.Parent?.Slot && (parent is not null || other.Rule.Type == element.Rule.Type)).ToList();
        var index = move.Index < 0 || move.Index > siblings.Count ? siblings.Count : move.Index;
        var current = siblings.IndexOf(element);
        if (current >= 0)
        {
            if (index == current || index == current + 1) return; // It is there already: no splice.
            if (index > current) index--;
        }
        reading.Family.MoveTo(plan, element, parent, index);
        if (element.Rule.SnapshotUndo) plan.Snapshot = true;
    }

    /// <summary>
    /// Whether two elements are ordered among each other: the same parent and containment slot,
    /// and at the top level, where there is neither, the same type.
    /// </summary>
    private static bool AreSiblings(ReadElement other, ReadElement element) =>
        other.Parent == element.Parent &&
        other.Rule.Parent?.Slot == element.Rule.Parent?.Slot &&
        (element.Parent is not null || other.Rule.Type == element.Rule.Type);

    // ---- remove ----

    private static void PlanRemove(Plan plan, ModelChange.Remove remove)
    {
        var reading = plan.Reading;
        var element = Element(reading, remove.Id);
        RefuseReadOnly(element.Rule, "removed");
        if (element.Rule.Remove is not { } settings)
        {
            Planning.Plan.Refuse($"A {element.Rule.Type} cannot be removed from this file.");
            return;
        }
        var removed = new HashSet<ReadElement> { element };
        foreach (var other in reading.Elements)
        {
            if (other == element || !settings.Cascade.Contains(other.Rule.Name)) continue;
            if (References(reading, other, element)) removed.Add(other);
        }
        var outermost = removed
            .Where(r => !removed.Any(o => o != r && Contains(o.Entry.RemovalSpan, r.Entry.RemovalSpan)))
            .OrderByDescending(r => r.Entry.Own.Start)
            .ToList();
        foreach (var target in outermost) reading.Family.Remove(plan, target, removed);
        if (removed.Any(r => r.Rule.SnapshotUndo)) plan.Snapshot = true;
    }

    private static bool Contains(Span outer, Span inner) => outer.Start <= inner.Start && inner.End <= outer.End && outer != inner;

    /// <summary>
    /// Whether <paramref name="other"/> has a reference that names <paramref name="element"/> (FBL 6.2): one that resolves
    /// to it, as the reading resolves references (FBL 5.4), and not merely one written like its key. A later holder of a
    /// repeated key is addressed by its place (FBL 5.3), so what names the key names the first holder; and a reference that
    /// is not written names nothing.
    /// </summary>
    private static bool References(BodyReading reading, ReadElement other, ReadElement element)
    {
        if (other.IsRelation && (other.SourceElement == element || other.TargetElement == element)) return true;
        foreach ((string name, AttributeBinding binding) in other.Rule.Attributes)
        {
            if (binding.Reference is not { } reference || !reference.To.Contains(element.Rule.Name) || other == element) continue;
            if (!other.Attributes.TryGetValue(name, out var value)) continue;
            var names = value is List<object?> list ? list.Select(v => NewText.Plain(v, null)) : [other.TextOf(name)];
            if (names.Any(key => reading.ReferencedBy(reference, key) == element)) return true;
        }
        return false;
    }
}
