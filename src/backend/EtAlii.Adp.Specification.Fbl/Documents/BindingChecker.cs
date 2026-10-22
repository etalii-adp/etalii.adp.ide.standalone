using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>The checks of FBL §14.1 steps 4 to 6 that need no DISL specification.</summary>
internal static class BindingChecker
{
    public static void Check(FblBinding binding, string pointer, List<LoadDiagnostic> problems)
    {
        var rules = binding.AllRules.ToList();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ruleNames = rules.Select(r => r.Name).Concat(binding.Blocks.Select(b => b.Name)).ToHashSet(StringComparer.Ordinal);

        CheckBlocks(binding, ruleNames, names, pointer, problems);
        var elementCount = binding.Elements.Count;
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var at = rule.IsRelation ? $"{pointer}/relations/{i - elementCount}" : $"{pointer}/elements/{i}";
            CheckRule(binding, rule, rules, ruleNames, names, at, problems);
        }
        CheckBinding(binding, rules.Count, pointer, problems);
    }

    private static void CheckBlocks(FblBinding binding, HashSet<string> ruleNames, HashSet<string> names, string pointer, List<LoadDiagnostic> problems)
    {
        for (var i = 0; i < binding.Blocks.Count; i++)
        {
            var block = binding.Blocks[i];
            var at = $"{pointer}/blocks/{i}";
            if (!names.Add(block.Name)) problems.Add(new LoadDiagnostic(at + "/name", LoadSeverity.Error, $"The rule name '{block.Name}' is used twice."));
            CheckRegex(block.Line, at + "/line", problems);
            CheckWithin(block.Within, ruleNames, at + "/within", problems);
        }
    }

    private static void CheckRule(FblBinding binding, Rule rule, List<Rule> rules, HashSet<string> ruleNames, HashSet<string> names, string at, List<LoadDiagnostic> problems)
    {
        if (!Names.IsName(rule.Name)) problems.Add(new LoadDiagnostic(at + "/name", LoadSeverity.Error, $"'{rule.Name}' is not a valid rule name."));
        if (!names.Add(rule.Name)) problems.Add(new LoadDiagnostic(at + "/name", LoadSeverity.Error, $"The rule name '{rule.Name}' is used twice."));
        if ((rule.At is null) == (rule.Line is null) && binding.Plugin is null)
        {
            problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A rule has exactly one of 'at' and 'line'."));
        }
        if (rule.Line is not null) CheckRegex(rule.Line, at + "/line", problems);
        CheckWithin(rule.Within, ruleNames, at + "/within", problems);
        CheckReferences(binding, rule, rules, at, problems);

        var families = FamiliesOf(binding, rule);
        var scalars = families.Count > 0 && families.All(f => f is Family.Yaml or Family.Json);
        var context = rule.At is null ? CelContext.Lines : scalars ? CelContext.Scalars : CelContext.Tree;
        Compile(rule, rule.When, context, at + "/when", problems);
        Compile(rule, rule.Insert?.When, CelContext.Insert, at + "/insert/when", problems);
        if (rule.Id?.From is { } from) CheckSlot(rule, from, context, at + "/id/from", problems);
        if (rule.Id?.SidecarKey is { } sidecar) Compile(rule, sidecar, context, at + "/id/sidecar/key", problems);
        if (rule.IsRelation && (rule.Source is null || rule.Target is null))
        {
            problems.Add(new LoadDiagnostic(at, LoadSeverity.Error, "A relation rule has a source and a target."));
        }
        CheckAttributes(rule, rules, context, scalars, at, problems);
        if (rule.Source is not null) CheckSlot(rule, rule.Source, context, at + "/source", problems);
        if (rule.Target is not null) CheckSlot(rule, rule.Target, context, at + "/target", problems);
        if (rule.Insert?.Constant is { } constant) CheckConstant(rule, constant, scalars, at + "/insert/constant", problems);
    }

    /// <summary>
    /// FBL 0.8 §14.1 step 6: an insert's constant is given only in the yaml and json families and never beside
    /// <c>emit</c>, and none of its keys is one the same rule writes, by a writable attribute binding, <c>id.from</c>,
    /// <c>source</c> or <c>target</c>, since a place has one writer (§3.3). A read-only binding may read its key.
    /// </summary>
    private static void CheckConstant(Rule rule, IReadOnlyList<KeyValuePair<string, object?>> constant, bool scalars, string pointer, List<LoadDiagnostic> problems)
    {
        if (!scalars) problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "An insert's constant is given only in the yaml and json families (FBL 0.8 6.2)."));
        if (rule.Insert!.Emit is not null) problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "An insert has a constant or an emit, not both (FBL 0.8 6.2)."));
        foreach ((string key, _) in constant)
        {
            if (Writer(rule, key) is { } writer)
            {
                problems.Add(new LoadDiagnostic(JsonPointer.Append(pointer, key), LoadSeverity.Error, $"The constant's key '{key}' is written by the rule's {writer} too, and a place has one writer (FBL 0.8 14.1)."));
            }
        }
    }

    /// <summary>What of <paramref name="rule"/> writes the key <paramref name="key"/> of its own entry, or null when nothing does.</summary>
    private static string? Writer(Rule rule, string key)
    {
        var slot = new SlotKind.Key(key);
        if (rule.Id?.From is { Child: null } from && from.Kind == slot) return "id.from";
        if (rule.Source is { Child: null } source && source.Kind == slot) return "source";
        if (rule.Target is { Child: null } target && target.Kind == slot) return "target";
        if (rule.ReadOnly is not null) return null;
        foreach ((string name, AttributeBinding attribute) in rule.Attributes)
        {
            if (attribute is { Child: null, ReadOnly: null } && attribute.Kind == slot) return $"attribute '{name}'";
        }
        return null;
    }

    /// <summary>The file rules, parent rules and cascaded rules a rule names exist.</summary>
    private static void CheckReferences(FblBinding binding, Rule rule, List<Rule> rules, string at, List<LoadDiagnostic> problems)
    {
        foreach (var file in rule.Files)
        {
            if (binding.Body.Files.All(f => f.Name != file)) problems.Add(new LoadDiagnostic(at + "/files", LoadSeverity.Error, $"No file rule is named '{file}'."));
        }
        if (rule.Parent is not null)
        {
            foreach (var p in rule.Parent.Rules) Require(p, rules, at + "/parent/rules", problems);
        }
        if (rule.Remove is not null)
        {
            foreach (var c in rule.Remove.Cascade) Require(c, rules, at + "/remove/cascade", problems);
        }
    }

    private static void CheckAttributes(Rule rule, List<Rule> rules, CelContext context, bool scalars, string at, List<LoadDiagnostic> problems)
    {
        foreach ((string name, AttributeBinding attribute) in rule.Attributes)
        {
            var a = $"{at}/attributes/{JsonPointer.Token(name)}";
            CheckSlot(rule, attribute, context, a, problems);
            if (attribute.Reference is { } reference)
            {
                foreach (var to in reference.To) Require(to, rules, a + "/reference/to", problems);
            }
            if (attribute.Override is { } o) CheckSlot(rule, o, context, a + "/override", problems);
            CheckScalarOptions(attribute, scalars, a, problems);
        }
    }

    /// <summary>The checks of the binding as a whole: its body, its rule count, its claims and its patterns.</summary>
    private static void CheckBinding(FblBinding binding, int ruleCount, string pointer, List<LoadDiagnostic> problems)
    {
        if (binding.Plugin is null)
        {
            if (binding.Body is { IsFolder: false, Family: null })
            {
                problems.Add(new LoadDiagnostic(pointer + "/body/family", LoadSeverity.Error, "A declared file body names its family."));
            }
            if (ruleCount == 0)
            {
                problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "A declared binding has at least one element or relation rule."));
            }
        }
        else if (ruleCount > 0)
        {
            problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, "A binding read by a plugin has no rules; its model is what the plugin reads."));
        }
        if (binding.Claims is { Shared: true, Marker: null, RegistrationOnly: false })
        {
            problems.Add(new LoadDiagnostic(pointer + "/claims", LoadSeverity.Error, "A shared claim has a marker or is registrationOnly."));
        }
        if (binding.Claims.Readings.Values.Count(r => r.Bare) > 1)
        {
            problems.Add(new LoadDiagnostic(pointer + "/claims/readings", LoadSeverity.Error, "At most one reading is bare."));
        }
        if (binding.Comment is not null) CheckRegex(binding.Comment, pointer + "/comment", problems);
        if (binding.Header?.Line is { } headerLine) CheckRegex(headerLine, pointer + "/header/line", problems);
        if (binding.Claims.Marker?.Pattern is { } pattern) CheckRegex(pattern, pointer + "/claims/marker/pattern", problems);
    }

    /// <summary>The families a rule reads: those of the file rules it names, else the body's, else those of every file rule.</summary>
    private static List<Family> FamiliesOf(FblBinding binding, Rule rule)
    {
        if (rule.Files.Count > 0) return binding.Body.Files.Where(f => f.Name is { } name && rule.Files.Contains(name)).Select(f => f.Family).OfType<Family>().ToList();
        if (binding.Body.Family is { } family) return [family];
        return binding.Body.Files.Select(f => f.Family).OfType<Family>().ToList();
    }

    /// <summary>
    /// FBL 0.5 §5.2: <c>read</c>, <c>quoted</c> and <c>fallback</c> are given only on a slot of the yaml
    /// or json family that reads a scalar, so never in another family nor on a <c>value</c> or <c>capture</c> slot.
    /// </summary>
    private static void CheckScalarOptions(AttributeBinding attribute, bool scalars, string pointer, List<LoadDiagnostic> problems)
    {
        var where = (scalars, attribute.Kind) switch
        {
            (false, _) => "a slot of the xml, lines or blocks family",
            (_, SlotKind.Value) => "a value slot",
            (_, SlotKind.Capture) => "a capture slot",
            _ => null,
        };
        if (where is null) return;
        foreach ((string option, bool given) in new[] { ("read", attribute.Read is not null), ("quoted", attribute.Quoted is not null), ("fallback", attribute.Fallback is not null) })
        {
            if (given) problems.Add(new LoadDiagnostic($"{pointer}/{option}", LoadSeverity.Error, $"'{option}' is not given on {where} (FBL 5.2)."));
        }
    }

    private static void CheckSlot(Rule rule, Slot slot, CelContext context, string pointer, List<LoadDiagnostic> problems)
    {
        if (slot.Kind is SlotKind.Value(var expression)) Compile(rule, expression, context, pointer + "/value", problems);
        if (slot.Word is not null) CheckRegex(slot.Word, pointer + "/word", problems);
    }

    private static void Require(string name, List<Rule> rules, string pointer, List<LoadDiagnostic> problems)
    {
        if (rules.All(r => r.Name != name)) problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, $"No rule is named '{name}'."));
    }

    private static void CheckWithin(IReadOnlyList<string>? within, HashSet<string> names, string pointer, List<LoadDiagnostic> problems)
    {
        if (within is null) return;
        foreach (var w in within)
        {
            if (w != "^" && !names.Contains(w)) problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, $"No rule is named '{w}'."));
        }
    }

    private static void CheckRegex(string expression, string pointer, List<LoadDiagnostic> problems)
    {
        var problem = RegexSubset.Check(expression);
        if (problem is not null) problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, problem));
    }

    /// <summary>Compiles <paramref name="expression"/> once, keeping its program on the rule for every reading that follows.</summary>
    private static void Compile(Rule rule, string? expression, CelContext context, string pointer, List<LoadDiagnostic> problems)
    {
        if (expression is null) return;
        try
        {
            rule.Keep(context, expression, FblCel.Compile(expression, context));
        }
        catch (CelException e)
        {
            problems.Add(new LoadDiagnostic(pointer, LoadSeverity.Error, e.Message));
        }
    }
}
