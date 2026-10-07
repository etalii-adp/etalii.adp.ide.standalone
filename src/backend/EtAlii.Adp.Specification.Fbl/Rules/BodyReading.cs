using System.Text.Json;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Fbl.Json;
using EtAlii.Adp.Specification.Fbl.Lines;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Text;
using EtAlii.Adp.Specification.Fbl.Xml;
using EtAlii.Adp.Specification.Fbl.Yaml;

namespace EtAlii.Adp.Specification.Fbl.Rules;

/// <summary>
/// A body read through a binding (FBL §5): the family's lossless reading, which rule claimed which
/// entry, the elements and relations with where each value lives, and the findings. Reading is a
/// pure function of the bytes, the binding and the options, and never throws on content (FBL §7.4).
/// </summary>
internal sealed class BodyReading
{
    private readonly Dictionary<Entry, string> _claims = [];
    private readonly Dictionary<Entry, ReadElement> _byEntry = [];
    private readonly Dictionary<string, CelProgram?> _programs = new(StringComparer.Ordinal);

    // An entry's parent as CEL reads it, converted once per reading: every item of a list shares the
    // list as its parent, so converting it per item made reading quadratic in the list's length.
    private readonly Dictionary<object, object?> _parents = new(ReferenceEqualityComparer.Instance);

    // While claiming, the variables of the candidate being claimed: its when and its computed slots read the same ones.
    private bool _isClaiming;
    private (Candidate Candidate, Dictionary<string, object?> Variables)? _claiming;

    // Per reference, the first element of each key, built on first use once the elements are final.
    private readonly Dictionary<ReferenceBinding, Dictionary<string, ReadElement>> _referenced = new(ReferenceEqualityComparer.Instance);
    private readonly List<Finding> _findings = [];

    private BodyReading(BodyText text, FblBinding binding, FblOptions options, FamilyReader family)
    {
        Text = text;
        Binding = binding;
        Options = options;
        Family = family;
    }

    public BodyText Text { get; }

    public FblBinding Binding { get; }

    public FblOptions Options { get; }

    public FamilyReader Family { get; }

    /// <summary>Elements and relations in document order; relations whose ends name nothing are not among them.</summary>
    public List<ReadElement> Elements { get; } = [];

    public IReadOnlyList<Finding> Findings => _findings;

    /// <summary>The views the body's view blocks define (FBL §4.7), in document order.</summary>
    public List<FblView> Views { get; } = [];

    /// <summary>The values of the binding's resource capture (FBL §8.2) in document order: the resources the body holds.</summary>
    public List<string> Resources { get; } = [];

    public (int Offset, string Message)? Unreadable { get; private set; }

    public string? ClaimedBy(Entry entry) => _claims.GetValueOrDefault(entry);

    public ReadElement? ElementOf(Entry entry) => _byEntry.GetValueOrDefault(entry);

    public ReadElement? Find(string id) => Elements.FirstOrDefault(e => e.Id == id);

    public static FamilyReader CreateFamily(BodyText text, FblBinding binding, FblOptions options)
    {
        var family = binding.Body.Family ?? throw new InvalidOperationException($"The binding '{binding.Name}' declares no family.");
        var extension = Path.GetExtension(options.FileName).ToLowerInvariant();
        foreach (var also in binding.Body.AlsoRead)
        {
            if (FamilyOfExtension(extension) == also) family = also;
        }
        return family switch
        {
            Documents.Family.Lines => new LinesFamily(text, binding, options, blocks: false),
            Documents.Family.Blocks => new LinesFamily(text, binding, options, blocks: true),
            Documents.Family.Yaml => new YamlFamily(text, binding, options),
            Documents.Family.Json => new JsonFamily(text, binding, options),
            Documents.Family.Xml => new XmlFamily(text, binding, options),
            _ => throw new ArgumentOutOfRangeException(nameof(binding)),
        };
    }

    private static Family? FamilyOfExtension(string extension) => extension switch
    {
        ".yml" or ".yaml" => Documents.Family.Yaml,
        ".json" => Documents.Family.Json,
        ".xml" => Documents.Family.Xml,
        _ => null,
    };

    public static BodyReading Read(byte[] bytes, FblBinding binding, FblOptions options)
    {
        var text = new BodyText(bytes);
        var family = CreateFamily(text, binding, options);
        var reading = new BodyReading(text, binding, options, family);
        if (bytes.Length > options.MaxBodyBytes)
        {
            return reading.MakeUnreadable(0, $"The body is larger than the {options.MaxBodyBytes} bytes this host reads, so it is not read at all.");
        }
        if (!text.IsValidUtf8)
        {
            return reading.MakeUnreadable(text.InvalidOffset, "The body is not valid UTF-8.");
        }
        family.Parse();
        if (family.Unreadable is { } problem) return reading.MakeUnreadable(problem.Offset, problem.Message);
        if (family.Entries.Count > options.MaxEntries)
        {
            return reading.MakeUnreadable(0, $"The body has more than the {options.MaxEntries} entries this host reads, so it is not read at all.");
        }
        if (binding.Header is { } header && !family.HeaderHolds(header))
        {
            var mark = header.Key is not null ? $"'{header.Key}: {(header.Value is { } v ? BindingReader.ScalarText(v) : "")}'" : "its first line";
            if (header.Required) return reading.MakeUnreadable(0, $"The body does not start with the mark the binding requires ({mark}).");
            family.Report(FindingCodes.HeaderMismatch, FindingSeverity.Warning, $"The body does not carry the binding's header mark ({mark}); it is read anyway.", new Span(text.BomLength, text.Lines[0].ContentEnd));
        }
        reading._isClaiming = true;
        reading.Claim();
        reading._isClaiming = false;
        reading._claiming = null;
        reading.Resolve();
        family.AfterRead(reading.ClaimedBy);
        reading._findings.InsertRange(0, family.Findings);
        return reading;
    }

    private BodyReading MakeUnreadable(int offset, string message)
    {
        Unreadable = (offset, message);
        _claims.Clear();
        _byEntry.Clear();
        Elements.Clear();
        _findings.Clear();
        (int line, int column) = Text.Position(Math.Min(offset, Text.Length));
        _findings.Add(new Finding(FindingCodes.Unparseable, FindingSeverity.Error, message, new SourceLocation(Options.FileName, line, column, 0)));
        return this;
    }

    // ---- claiming entries (FBL §5.1) ----

    private void Claim()
    {
        var offered = new Dictionary<Entry, List<Candidate>>();
        foreach (var block in Binding.Blocks)
        {
            foreach (var candidate in Family.BlockCandidates(block)) Offer(offered, candidate);
        }
        foreach (var rule in Binding.AllRules)
        {
            foreach (var candidate in Family.Candidates(rule)) Offer(offered, candidate);
        }
        foreach (var entry in Family.Entries)
        {
            if (!offered.TryGetValue(entry, out var candidates)) continue;
            string? error = null;
            foreach (var candidate in candidates)
            {
                var within = candidate.Rule?.Within ?? candidate.Block?.Within;
                if (!Family.Admits(within, entry, ClaimedBy)) continue;
                if (candidate.Rule is { } rule)
                {
                    if (!SelectedResource(candidate)) continue;
                    if (rule.When is { } when)
                    {
                        var holds = Evaluate(when, candidate, out var problem);
                        if (problem is not null) error ??= problem;
                        if (holds is not true) continue;
                    }
                    _claims[entry] = rule.Name;
                    var element = new ReadElement { Rule = rule, Candidate = candidate, Line = Text.Position(entry.Own.Start).Line };
                    _byEntry[entry] = element;
                    Elements.Add(element);
                    ReadSlots(element);
                }
                else
                {
                    _claims[entry] = candidate.Block!.Name;
                    if (candidate.Block.View is { } group && candidate.Captures.TryGetValue(group, out var view))
                    {
                        Views.Add(new FblView(view, candidate.Block.Name, entry.Own, Text.Position(entry.Own.Start).Line));
                    }
                }
                error = null;
                break;
            }
            if (error is not null)
            {
                Family.Report(FindingCodes.UnreadableEntry, FindingSeverity.Warning, $"This entry cannot be read: {error}", entry.Own);
            }
        }
    }

    private static void Offer(Dictionary<Entry, List<Candidate>> offered, Candidate candidate)
    {
        if (!offered.TryGetValue(candidate.Entry, out var list))
        {
            list = [];
            offered[candidate.Entry] = list;
        }
        list.Add(candidate);
    }

    /// <summary>
    /// The registration's <c>resource</c> header selects one value of the binding's resource capture;
    /// without it, the first value in document order (FBL §8.2).
    /// </summary>
    private string? _resource;

    private bool SelectedResource(Candidate candidate)
    {
        if (Binding.Registration.ResourceCapture is not { } capture) return true;
        if (!candidate.Captures.TryGetValue(capture, out var value)) return true;
        if (!Resources.Contains(value)) Resources.Add(value);
        _resource ??= Options.Resource ?? value;
        return value == _resource;
    }

    // ---- CEL ----

    private CelProgram? Program(string expression, CelContext context, out string? problem)
    {
        problem = null;
        var key = context + ":" + expression;
        if (_programs.TryGetValue(key, out var cached)) return cached;
        try
        {
            cached = FblCel.Compile(expression, context);
        }
        catch (CelException e)
        {
            problem = e.Message;
            cached = null;
        }
        _programs[key] = cached;
        return cached;
    }

    private CelContext RuleContext => Family is LinesFamily ? CelContext.Lines : CelContext.Tree;

    public object? Evaluate(string expression, Candidate candidate, out string? problem)
    {
        var program = Program(expression, RuleContext, out problem);
        if (program is null) return null;
        try
        {
            var value = program.Evaluate(ClaimVariables(candidate));
            if (value is not CelError failed) return value;
            problem = failed.Message;
            return null;
        }
        catch (CelException e)
        {
            problem = e.Message;
            return null;
        }
    }

    public bool InsertAllowed(string expression, IReadOnlyDictionary<string, object?> attributes)
    {
        var program = Program(expression, CelContext.Insert, out _);
        if (program is null) return false;
        var map = new CelMap();
        foreach ((string name, object? value) in attributes) map[name] = value is int i ? (long)i : value;
        try
        {
            return program.IsTrue(new Dictionary<string, object?> { ["attributes"] = map });
        }
        catch (CelException)
        {
            return false;
        }
    }

    /// <summary>
    /// The variables of <paramref name="candidate"/>, kept while it is the one being read: while
    /// claiming, nothing they are made of changes between its <c>when</c> and its computed slots.
    /// </summary>
    private Dictionary<string, object?> ClaimVariables(Candidate candidate)
    {
        if (!_isClaiming) return Variables(candidate);
        if (_claiming is { } claiming && ReferenceEquals(claiming.Candidate, candidate)) return claiming.Variables;
        var variables = Variables(candidate);
        _claiming = (candidate, variables);
        return variables;
    }

    private Dictionary<string, object?> Variables(Candidate candidate)
    {
        var variables = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["entry"] = Family.CelValue(candidate),
            ["line"] = (long)Text.Position(candidate.Entry.Own.Start).Line,
        };
        var registration = new CelMap();
        foreach ((string key, string value) in Options.RegistrationHeaders) registration[key] = value;
        variables["registration"] = registration;
        (string name, object? extra) = Family.CelExtra(candidate);
        variables[name] = extra;
        object? parent = null;
        foreach (var enclosing in Family.Enclosing(candidate.Entry))
        {
            if (_byEntry.TryGetValue(enclosing, out var element))
            {
                parent = ParentValue(element, () => Family.CelValue(element.Candidate));
                break;
            }
            parent ??= ParentValue(enclosing, () => Family.CelValue(new Candidate(null, null, enclosing, new Dictionary<string, string>())));
            break;
        }
        variables["parent"] = parent;
        return variables;
    }

    /// <summary>The CEL value of a parent, by the element or the entry it is a pure function of, converted on first use.</summary>
    private object? ParentValue(object key, Func<object?> convert)
    {
        if (!_parents.TryGetValue(key, out var value)) _parents[key] = value = convert();
        return value;
    }

    // ---- slots (FBL §5.2) ----

    private void ReadSlots(ReadElement element)
    {
        var rule = element.Rule;
        foreach ((string name, AttributeBinding binding) in rule.Attributes)
        {
            var read = ReadSlot(element.Candidate, binding, element);
            element.Slots[name] = read;
            object? value = read.Present ? read.Value : null;
            if (read.Present && binding.Map is { } map && value is string wire)
            {
                var mapped = map.FirstOrDefault(m => m.Key == wire);
                if (mapped.Key is not null) value = mapped.Value;
            }
            if (binding.Flag) value = read.Present;
            else if (read.Present && binding.Reference is { } reference && !reference.To.Contains(rule.Name) && read.Words is { Count: > 0 } words)
            {
                value = words.Select(w => (object?)w.Text).ToList();
            }
            if (!read.Present && binding is { Flag: false, Default: { } fallback }) value = FromJson(fallback);
            if (read.Present || binding.Flag || binding.Default is not null) element.Attributes[name] = value;
        }
        if (rule.Source is { } source) element.SourceRead = ReadSlot(element.Candidate, source, element);
        if (rule.Target is { } target) element.TargetRead = ReadSlot(element.Candidate, target, element);
        if (rule.Id?.From is { } from) element.IdRead = ReadSlot(element.Candidate, from, element);
    }

    public SlotRead ReadSlot(Candidate candidate, Slot slot, ReadElement element)
    {
        var read = ReadSlotUnchecked(candidate, slot, element);
        var reason = (slot as AttributeBinding)?.ReadOnly ?? element.Rule.ReadOnly ?? Binding.ReadOnly;
        if (reason is not null && read.Writable) read = read with { Writable = false, Reason = reason };
        return read;
    }

    private SlotRead ReadSlotUnchecked(Candidate candidate, Slot slot, ReadElement element)
    {
        if (slot.Value is { } expression)
        {
            var value = Evaluate(expression, candidate, out var problem);
            return problem is null
                ? new SlotRead(value, null, true, false, "The value is computed from the file.")
                : SlotRead.ReadOnlyAbsent($"The value cannot be computed: {problem}");
        }
        if (slot.Parent is { } name)
        {
            foreach (var enclosing in Family.Enclosing(candidate.Entry))
            {
                if (!_byEntry.TryGetValue(enclosing, out var parent)) continue;
                if (element.Rule.Parent is { } containment && !containment.Rules.Contains(parent.Rule.Name)) continue;
                return parent.Slots.TryGetValue(name, out var read) ? read : Family.ReadRaw(enclosing, name);
            }
            return SlotRead.ReadOnlyAbsent("No enclosing entry holds this value.");
        }
        if (slot is AttributeBinding { Override: { } over } attribute)
        {
            var overriding = Family.Read(candidate, new AttributeBinding
            {
                Key = over.Key, XmlAttribute = over.XmlAttribute, Text = over.Text, Child = over.Child, Group = over.Group,
                Capture = over.Capture, Word = over.Word, HtmlParagraphs = attribute.HtmlParagraphs,
            });
            if (overriding.Present) return overriding with { Node = new OverrideNode(overriding.Node, over) };
        }
        return Family.Read(candidate, slot);
    }

    private static object? FromJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    // ---- ids, references, containment (FBL §5.3 to §5.5) ----

    private void Resolve()
    {
        foreach (var element in Elements.Where(e => !e.IsRelation)) AssignId(element);
        foreach (var element in Elements.Where(e => !e.IsRelation)) element.Key = KeyOf(element);
        var dangling = new List<ReadElement>();
        foreach (var relation in Elements.Where(e => e.IsRelation))
        {
            relation.SourceElement = End(relation, relation.SourceRead, "source");
            relation.TargetElement = End(relation, relation.TargetRead, "target");
            if (relation.SourceElement is null || relation.TargetElement is null) dangling.Add(relation);
        }
        foreach (var relation in dangling)
        {
            Elements.Remove(relation);
            _byEntry.Remove(relation.Entry);
        }
        foreach (var relation in Elements.Where(e => e.IsRelation))
        {
            AssignId(relation);
            relation.Key = KeyOf(relation);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in Elements)
        {
            if (seen.Add(element.Id)) continue;
            Family.Report(FindingCodes.DuplicateId, FindingSeverity.Warning, $"Another entry already has the id '{element.Id}'; this one is addressed by its place.", element.Entry.Own);
            element.Id = PlaceId(element);
            element.IdStored = false;
            seen.Add(element.Id);
        }
        foreach (var element in Elements)
        {
            if (element.Rule.Parent is not { } containment) continue;
            foreach (var enclosing in Family.Enclosing(element.Entry))
            {
                if (_byEntry.TryGetValue(enclosing, out var parent) && containment.Rules.Contains(parent.Rule.Name))
                {
                    element.Parent = parent;
                    break;
                }
            }
        }
        foreach (var element in Elements) CheckReferences(element);
    }

    private void AssignId(ReadElement element)
    {
        var rule = element.Rule;
        if (rule.Id?.From is not null)
        {
            if (element.IdRead is { Present: true, Value: { } value } && NewText.Plain(value, null) is { Length: > 0 } id)
            {
                element.Id = id;
                element.IdStored = true;
                return;
            }
            Family.Report(FindingCodes.MissingId, FindingSeverity.Warning, $"This {rule.Type} has no id; it is addressed by its place.", element.Entry.Own);
            element.Id = PlaceId(element);
            return;
        }
        if (rule.Id?.SidecarKey is { } keyExpression)
        {
            var key = Evaluate(keyExpression, element.Candidate, out _);
            if (key is not null && Options.Identities.TryGetValue(NewText.Plain(key, null), out var stored))
            {
                element.Id = stored;
                element.IdStored = true;
                return;
            }
        }
        var request = new IdRequest(rule.Name, rule.Type, element.Attributes, element.SourceElement?.Id, element.TargetElement?.Id, element.Line);
        element.Id = Options.DeriveId?.Invoke(request) ?? PlaceId(element);
    }

    private static string PlaceId(ReadElement element) => $"{element.Rule.Name}@{element.Line}";

    /// <summary>
    /// The value references name an element by: the attribute its own rules reference it by, else
    /// its stored id, else its id (FBL §5.7).
    /// </summary>
    private static string KeyOf(ReadElement element)
    {
        foreach ((string name, AttributeBinding binding) in element.Rule.Attributes)
        {
            if (binding.Reference is { } reference && reference.To.Contains(element.Rule.Name) && reference.By == name)
            {
                element.KeyAttribute = name;
                return NewText.Plain(element.Attributes.GetValueOrDefault(name), null);
            }
        }
        if (element.Rule.Id?.From is not null && element.IdRead is { Present: true } read) return NewText.Plain(read.Value, null);
        return element.Id;
    }

    private ReadElement? End(ReadElement relation, SlotRead? read, string end)
    {
        if (read is not { Present: true, Value: { } value })
        {
            Family.Report(FindingCodes.DanglingReference, FindingSeverity.Warning, $"This {relation.Rule.Type} has no {end}, so it is not read as a relation.", relation.Entry.Own);
            return null;
        }
        var key = NewText.Plain(value, null);
        var found = Elements.FirstOrDefault(e => !e.IsRelation && e.Key == key);
        if (found is null)
        {
            Family.Report(FindingCodes.DanglingReference, FindingSeverity.Warning, $"The {end} '{key}' of this {relation.Rule.Type} names no element, so it is not read as a relation.", read.Span ?? relation.Entry.Own);
        }
        return found;
    }

    private void CheckReferences(ReadElement element)
    {
        foreach ((string name, AttributeBinding binding) in element.Rule.Attributes)
        {
            if (binding.Reference is not { } reference || reference.To.Contains(element.Rule.Name)) continue;
            if (!element.Slots.TryGetValue(name, out var read) || !read.Present) continue;
            var names = element.Attributes.GetValueOrDefault(name) is List<object?> list ? list.Select(v => NewText.Plain(v, null)) : [NewText.Plain(read.Value, null)];
            foreach (var key in names)
            {
                if (ReferencedBy(reference, key) is null)
                {
                    Family.Report(FindingCodes.DanglingReference, FindingSeverity.Warning, $"'{key}' in {name} names no {string.Join(" or ", reference.To)}.", read.Span ?? element.Entry.Own);
                }
            }
        }
    }

    /// <summary>The first element, in document order, that <paramref name="reference"/> can name and whose key is <paramref name="key"/>.</summary>
    /// <remarks>Read once the elements are final, so each reference's keys are indexed on first use rather than searched per key.</remarks>
    public ReadElement? ReferencedBy(ReferenceBinding reference, string key)
    {
        if (!_referenced.TryGetValue(reference, out var index))
        {
            index = new Dictionary<string, ReadElement>(StringComparer.Ordinal);
            foreach (var element in Elements.Where(e => reference.To.Contains(e.Rule.Name)))
            {
                index.TryAdd(NewText.Plain(element.Attributes.GetValueOrDefault(reference.By), null), element);
            }
            _referenced[reference] = index;
        }
        return index.GetValueOrDefault(key);
    }

    // ---- the public model ----

    public FblModel ToModel() => new(
        Elements.Select(e => new FblElement(
            e.Id,
            e.IdStored,
            e.Rule.Type,
            e.Rule.Name,
            e.IsRelation,
            new Dictionary<string, object?>(e.Attributes, StringComparer.Ordinal),
            e.Parent?.Id,
            e.Parent is null ? null : e.Rule.Parent?.Slot,
            e.SourceElement?.Id,
            e.TargetElement?.Id,
            e.Entry.Own,
            e.Line)).ToList(),
        _findings.ToList(),
        Unreadable is not null)
    {
        Views = Views.ToList(),
        Resources = Resources.ToList(),
    };
}

/// <summary>A value read from an attribute's <c>override</c> slot, which writing removes (FBL §5.2).</summary>
internal sealed record OverrideNode(object? Node, Slot Slot);
