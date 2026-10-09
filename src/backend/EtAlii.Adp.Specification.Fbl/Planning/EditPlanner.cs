using EtAlii.Adp.Specification.Fbl.Documents;
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
        if (reading.Unreadable is not null) return new PlanResult.Refused("The file could not be read, so it is never written.");
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
                case ModelChange.Remove remove:
                    PlanRemove(plan, remove);
                    break;
                case ModelChange.Move move:
                    PlanMove(plan, move);
                    break;
                case ModelChange.Retype:
                    return new PlanResult.Refused("This file's binding cannot change an element's type; only a persistence plugin can.");
                case ModelChange.Place or ModelChange.Identify:
                    throw new ArgumentException("Placements and stored ids are edits of the registration, not of the body.", nameof(change));
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
            return new PlanResult.Planned(new Edit(plan.Ordered(), plan.Snapshot));
        }
        catch (RefusedException refused)
        {
            return new PlanResult.Refused(refused.Message);
        }
    }

    private static ReadElement Element(BodyReading reading, string id) =>
        reading.Find(id) ?? throw new ArgumentException($"The model has no element or relation '{id}'.", nameof(id));

    private static void RefuseReadOnly(Rule rule, string gesture)
    {
        if (rule.ReadOnly is { } reason) Planning.Plan.Refuse(reason.Length > 0 ? reason : $"A {rule.Type} cannot be {gesture} in this file.");
    }

    // ---- set ----

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
                read = binding is null ? SlotRead.Absent : rule == element.Rule && element.Slots.TryGetValue(attribute, out var known) ? known : reading.ReadSlot(element.Candidate with { Rule = rule }, binding, element);
            }
            if (binding is null)
            {
                Planning.Plan.Refuse($"A {element.Rule.Type} keeps no '{attribute}' in this file.");
                return;
            }
            if (binding.IsComputed || binding.Parent is not null || !read.Writable)
            {
                Planning.Plan.Refuse(read.Reason is { Length: > 0 } reason ? reason : $"The {attribute} of a {element.Rule.Type} cannot be changed in this file.");
            }
            var empty = NewText.IsEmpty(value) || (binding.Flag && value is false);
            if (empty && binding is { Flag: false, Empty: "refuse" }) Planning.Plan.Refuse($"The {attribute} of a {element.Rule.Type} cannot be empty.");
            if (!read.Present && !empty && !binding.Flag && binding.Absent.TryGetValue(reading.Family.FamilyName, out var absent) && absent == "refuse")
            {
                Planning.Plan.Refuse($"The {reading.Binding.Name} file has no \"{SlotName(binding)}\" to rewrite.");
            }
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
            changes.Add(new SlotChange(attribute, binding, rule, read, value, empty));
        }
        if (changes.Count > 0) reading.Family.Write(plan, element, changes);
        if (newKey is not null) RewriteReferences(plan, element, newKey);
        if (element.Rule.SnapshotUndo) plan.Snapshot = true;
    }

    private static string SlotName(Slot slot) => slot.Key ?? slot.XmlAttribute ?? slot.Group ?? slot.Capture ?? slot.Child ?? "value";

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

    private static bool SameSlot(Slot a, Slot b) =>
        a.Key == b.Key && a.XmlAttribute == b.XmlAttribute && a.Group == b.Group && a.Text == b.Text && a.Child == b.Child && a.Word == b.Word && a.Capture == b.Capture;

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
        if (read is not { Present: true, Span: { } span } || NewText.Plain(read.Value, null) != oldKey || plan.Touches(span)) return;
        plan.Add(SpliceOperation.RewriteReference, span, plan.Reading.Family.Format(read, null, newKey));
    }

    // ---- add ----

    private static void PlanAdd(Plan plan, ModelChange.Add add)
    {
        var reading = plan.Reading;
        // A declared binding appends where its insert rule says; a position among siblings is a plugin's to honour.
        if (add.Index is not null) Planning.Plan.Refuse($"This file's binding cannot add a {add.Type} at a position; only a persistence plugin can.");
        var rules = reading.Binding.AllRules.Where(r => r.Type == add.Type && (add.Slot is null || r.Parent?.Slot == add.Slot)).ToList();
        if (rules.Count == 0) Planning.Plan.Refuse($"This file has no place for a {add.Type}.");
        // Several rules may write one type (a cell's items are options or rows): the one that
        // binds the most of the attributes given is the one meant, the first of them on a tie.
        var rule = rules
            .Where(r => r.Insert is not null)
            .OrderByDescending(r => add.Attributes.Keys.Count(name => r.Attribute(name) is not null))
            .FirstOrDefault();
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
        if (rule.Insert!.When is { } when && !reading.InsertAllowed(when, values))
        {
            Planning.Plan.Refuse($"This {add.Type} cannot be added to the file.");
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
        reading.Family.Insert(plan, new InsertRequest(rule, add.Id, values, parent, source, target));
        if (rule.SnapshotUndo) plan.Snapshot = true;
    }

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
            if (References(other, element)) removed.Add(other);
        }
        var outermost = removed
            .Where(r => !removed.Any(o => o != r && Contains(o.Entry.RemovalSpan, r.Entry.RemovalSpan)))
            .OrderByDescending(r => r.Entry.Own.Start)
            .ToList();
        foreach (var target in outermost) reading.Family.Remove(plan, target, removed);
        if (removed.Any(r => r.Rule.SnapshotUndo)) plan.Snapshot = true;
    }

    private static bool Contains(Span outer, Span inner) => outer.Start <= inner.Start && inner.End <= outer.End && outer != inner;

    private static bool References(ReadElement other, ReadElement element)
    {
        if (other.IsRelation && (other.SourceElement == element || other.TargetElement == element)) return true;
        foreach ((string name, AttributeBinding binding) in other.Rule.Attributes)
        {
            if (binding.Reference is not { } reference || !reference.To.Contains(element.Rule.Name) || other == element) continue;
            var value = other.Attributes.GetValueOrDefault(name);
            if (value is List<object?> list ? list.Any(v => NewText.Plain(v, null) == element.Key) : NewText.Plain(value, null) == element.Key) return true;
        }
        return false;
    }
}
