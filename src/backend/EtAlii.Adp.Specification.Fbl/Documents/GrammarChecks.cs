using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Grammar;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>
/// The checks of FBL 0.9 §4.9 and §14.1 step 6 that the grammar family adds (etalii.adp spec 072, Requirement 2.6): what
/// a binding of the family may write, and that its selectors name what its grammar has.
/// </summary>
internal static class GrammarChecks
{
    /// <summary>The checks that need no grammar: the body, the rules and the kinds of their slots, and every <c>match</c>.</summary>
    public static void Check(FblBinding binding, string pointer, List<LoadDiagnostic> problems)
    {
        var family = binding.Body.Family == Family.Grammar;
        if (binding.Body.Grammar is not null && !family)
        {
            problems.Add(Error(pointer + "/body/grammar", "A grammar is given only on a body of the grammar family (FBL 0.9 4.9)."));
        }
        if (family && binding.Body.Grammar is null)
        {
            problems.Add(Error(pointer + "/body", "A body of the grammar family carries its grammar (FBL 0.9 4.9)."));
        }
        for (var i = 0; i < binding.Blocks.Count; i++)
        {
            var block = binding.Blocks[i];
            var at = $"{pointer}/blocks/{i}";
            if (family && (block.At is null || block.Line.Length > 0)) problems.Add(Error(at, "A block rule of the grammar family has 'at' and no 'line' (FBL 0.11 4.9)."));
            if (!family && (block.At is not null || block.ViewSlot is not null || block.Insert is not null))
            {
                problems.Add(Error(at, "A block rule's 'at', a view slot and an insert stand only in the grammar family (FBL 0.11 4.9)."));
            }
            if (block.ViewSlot is { Kind: not (SlotKind.Node or SlotKind.Value) }) problems.Add(Error(at + "/view", "A block's view is a {node} or a {value} slot (FBL 0.11 4.9)."));
            if (block.ViewSlot?.Match is { } match) CheckMatch(match, at + "/view/match", problems);
            if (block.Insert is { Emit: null }) problems.Add(Error(at + "/insert/emit", "A block's insert writes its entry from an emit (FBL 0.11 4.9)."));
        }
        foreach ((Rule rule, string at) in Rules(binding, pointer))
        {
            if (family && (rule.At is null || rule.Line is not null))
            {
                problems.Add(Error(at, "A rule of the grammar family has 'at' and no 'line' (FBL 0.9 4.9)."));
            }
            if (!family && (rule.SelectedView || rule.OpensWith is not null))
            {
                problems.Add(Error(at, "A rule's selectedView, and an opens with its open and close, stand only in the grammar family (FBL 0.11 4.9)."));
            }
            foreach ((Slot slot, string place) in Slots(rule, at))
            {
                if (family && Unadmitted(slot.Kind) is { } kind)
                {
                    problems.Add(Error(place, $"A {{{kind}}} slot is not admitted in the grammar family (FBL 0.9 4.9)."));
                }
                if (!family && slot.Kind is SlotKind.Node)
                {
                    problems.Add(Error(place + "/node", "A {node} slot is admitted only in the grammar family (FBL 0.9 4.9)."));
                }
                if (slot.Match is { } match) CheckMatch(match, place + "/match", problems);
                CheckParts(rule, slot, family, place, problems);
            }
        }
    }

    /// <summary>
    /// <c>split</c> and <c>escape</c> stand only on a <c>{node}</c> slot of the family, <c>split</c> holding neither
    /// character of the escape, and <c>create.after</c> only in the family, naming other attributes of the same rule
    /// (FBL 0.11 and 0.13 §14.1 step 6).
    /// </summary>
    private static void CheckParts(Rule rule, Slot slot, bool family, string place, List<LoadDiagnostic> problems)
    {
        var node = family && slot.Kind is SlotKind.Node;
        if (slot.Split is { } split)
        {
            if (!node) problems.Add(Error(place + "/split", "A split stands only on a {node} slot of the grammar family (FBL 0.13 14.1)."));
            else if (slot.Escape is { } escape && (split.Contains(escape.With) || split.Contains(escape.Quote)))
            {
                problems.Add(Error(place + "/split", $"The split '{split}' holds a character of the slot's escape, so cutting before its escapes are resolved would give other items (FBL 0.13 4.9)."));
            }
        }
        if (slot.Escape is not null && !node) problems.Add(Error(place + "/escape", "An escape stands only on a {node} slot of the grammar family (FBL 0.11 14.1)."));
        if (slot.Gather.Count > 0)
        {
            if (!node || slot.Gather.Any(gathered => gathered.Kind is not SlotKind.Node)) problems.Add(Error(place + "/gather", "A gather stands only on a {node} slot of the grammar family, and gathers {node} slots (FBL 0.11 14.1)."));
            else if (slot is AttributeBinding { ReadOnly: null } && rule.ReadOnly is null) problems.Add(Error(place + "/gather", "A gathered attribute is read-only, since it is never written (FBL 0.11 3.3, 14.1)."));
        }
        if (slot is not AttributeBinding { Create.After: { } after }) return;
        if (!family) problems.Add(Error(place + "/create/after", "A create's after stands only in the grammar family (FBL 0.11 14.1)."));
        foreach (var name in after.Where(name => rule.Attribute(name) is null || ReferenceEquals(rule.Attribute(name), slot)))
        {
            problems.Add(Error(place + "/create/after", $"A create's after names '{name}', which is no other attribute of the rule (FBL 0.11 14.1)."));
        }
    }

    /// <summary>Every segment of a rule's <c>at</c> and of a <c>{node}</c> slot names a rule, alias or terminal of <paramref name="grammar"/>.</summary>
    public static void CheckSelectors(FblBinding binding, GrammarBuild grammar, string pointer, List<LoadDiagnostic> problems)
    {
        for (var i = 0; i < binding.Blocks.Count; i++)
        {
            var block = binding.Blocks[i];
            if (block.At is { } selector) CheckSelector(selector, slot: false, grammar.Names, $"{pointer}/blocks/{i}/at", problems);
            if (block.ViewSlot?.Kind is SlotKind.Node(var node)) CheckSelector(node, slot: true, grammar.Names, $"{pointer}/blocks/{i}/view/node", problems);
            if (block.Insert?.Container is { } container) CheckSelector(container, slot: false, grammar.Names, $"{pointer}/blocks/{i}/insert/container", problems);
        }
        foreach ((Rule rule, string at) in Rules(binding, pointer))
        {
            if (rule.At is { } selector) CheckSelector(selector, slot: false, grammar.Names, at + "/at", problems);
            foreach ((Slot slot, string place) in Slots(rule, at))
            {
                if (slot.Kind is SlotKind.Node(var node)) CheckSelector(node, slot: true, grammar.Names, place + "/node", problems);
            }
        }
    }

    private static void CheckSelector(string selector, bool slot, IReadOnlySet<string> names, string pointer, List<LoadDiagnostic> problems)
    {
        if (GrammarSelector.Parse(selector, slot) is not { } segments)
        {
            var dot = slot ? ", or '.' alone" : "";
            problems.Add(Error(pointer, $"'{selector}' is not a selector of the grammar family: names, '*', '**' and name[path='v']{dot} (FBL 0.9 4.9)."));
            return;
        }
        foreach (var name in GrammarSelector.UnknownNames(segments, names))
        {
            problems.Add(Error(pointer, $"The selector '{selector}' names '{name}', which is no rule, alias or terminal of the grammar (FBL 0.9 4.9)."));
        }
    }

    /// <summary>A <c>match</c> is a regular expression of FBL's subset with the named group <c>value</c> (FBL 0.9 §5.2).</summary>
    private static void CheckMatch(string match, string pointer, List<LoadDiagnostic> problems)
    {
        if (RegexSubset.Check(match) is { } problem)
        {
            problems.Add(Error(pointer, problem));
            return;
        }
        if (!new Regex(match, RegexOptions.CultureInvariant).GetGroupNames().Contains("value"))
        {
            problems.Add(Error(pointer, "A slot's match has the named group 'value' (FBL 0.9 5.2)."));
        }
    }

    /// <summary>The name of a slot kind the grammar family does not admit, or null for one it does.</summary>
    private static string? Unadmitted(SlotKind kind) => kind switch
    {
        SlotKind.Key => "key",
        SlotKind.XmlAttribute => "attribute",
        SlotKind.Text => "text",
        SlotKind.Group => "group",
        SlotKind.Continuation => "continuation",
        SlotKind.Capture => "capture",
        _ => null,
    };

    /// <summary>The binding's element and relation rules, each with its JSON Pointer.</summary>
    private static IEnumerable<(Rule Rule, string At)> Rules(FblBinding binding, string pointer)
    {
        for (var i = 0; i < binding.Elements.Count; i++) yield return (binding.Elements[i], $"{pointer}/elements/{i}");
        for (var i = 0; i < binding.Relations.Count; i++) yield return (binding.Relations[i], $"{pointer}/relations/{i}");
    }

    /// <summary>Every slot of <paramref name="rule"/>, each with its JSON Pointer: the id's, the attributes' with their overrides and secondaries, the source's and the target's.</summary>
    private static IEnumerable<(Slot Slot, string At)> Slots(Rule rule, string at)
    {
        if (rule.Id?.From is { } from) yield return (from, at + "/id/from");
        foreach ((string name, AttributeBinding attribute) in rule.Attributes)
        {
            var a = $"{at}/attributes/{JsonPointer.Token(name)}";
            yield return (attribute, a);
            if (attribute.Override is { } o) yield return (o, a + "/override");
            if (attribute.Secondary is { } s) yield return (s, a + "/secondary");
            for (var i = 0; i < attribute.Gather.Count; i++) yield return (attribute.Gather[i], $"{a}/gather/{i}");
        }
        if (rule.Source is { } source) yield return (source, at + "/source");
        if (rule.Target is { } target) yield return (target, at + "/target");
    }

    private static LoadDiagnostic Error(string pointer, string message) => new(pointer, LoadSeverity.Error, message);
}
