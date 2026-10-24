using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;

namespace EtAlii.Adp.Specification.Fbl.Tests.Support;

/// <summary>
/// A requirement document as the Hierarchical Requirement Designer sees it, standing in for DESL in the fixture run as
/// <see cref="NaturalIds"/> stands in for DISL: the body read through the library, its entries known by the ids the
/// designer gives them, and its changes written as the designer writes them. What it adds to the library is what
/// <c>hierarchical-requirement.des</c> says and FBL does not: the counter ids with their placeholders and marks (DESL
/// 0.2 section 5.3.5), the assessments' ids by place, the enumeration's keys, and the members of the lists (section 7.7).
/// </summary>
internal sealed partial class HierarchicalRequirementDocument
{
    private const string Item = "Item";
    private const string Requirement = "Requirement";

    private static readonly string[] _lists = ["tags", "categories", "states"];

    private readonly OpenBody? _body;
    private readonly OpenRegistration? _registration;

    private HierarchicalRequirementDocument(OpenBody? body, OpenRegistration? registration)
    {
        _body = body;
        _registration = registration;
        if (_registration is null) return;
        _registration.AbsentMembers = block => _lists.Contains(block) && Members(block) is { Count: > 0 } members ? members : null;
        if (_body is not null) _registration.KnownIds = Entries().Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The input of a fixture: a body, or a registration with the body of its base name beside it (FBL 0.7 section 8.1).</summary>
    public static HierarchicalRequirementDocument Open(string folder, string inputName, FblBinding binding)
    {
        var input = File.ReadAllBytes(Path.Combine(folder, inputName));
        if (!inputName.EndsWith(".adp", StringComparison.OrdinalIgnoreCase))
        {
            return new HierarchicalRequirementDocument(OpenBody.Open(input, binding, HierarchicalRequirementSpecification.Options(inputName)), null);
        }
        var registration = OpenRegistration.Open(input, binding);
        var bodyName = registration.Document.Body ?? Path.ChangeExtension(inputName, ".md");
        var bodyPath = Path.Combine(folder, bodyName);
        var body = File.Exists(bodyPath) ? OpenBody.Open(File.ReadAllBytes(bodyPath), binding, HierarchicalRequirementSpecification.Options(bodyName)) : null;
        return new HierarchicalRequirementDocument(body, registration);
    }

    /// <summary>The bytes a step changes: the registration's when the input is one, else the body's.</summary>
    public byte[] Bytes => _registration?.Bytes ?? _body!.Bytes;

    public bool Unreadable => _body?.Model.Unreadable ?? false;

    /// <summary>
    /// The findings of the reading that the library itself reports. A placeholder is no duplicate to DESL, which numbers
    /// it before ids are compared, so a duplicate the library reports at a placeholder is left out.
    /// </summary>
    public List<Finding> Findings(string inputName)
    {
        var findings = new List<Finding>();
        if (_body is not null)
        {
            var placeholders = Entries().Where(entry => entry.Placeholder).Select(entry => entry.Element.Line).ToHashSet();
            findings.AddRange(_body.Model.Findings.Where(finding => !(finding.Code == "std.duplicateId" && placeholders.Contains(finding.Location?.Line ?? 0))));
        }
        if (_registration is not null) findings.AddRange(_registration.Findings(inputName));
        return findings;
    }

    /// <summary>The document's elements as the designer has them: its entries by their ids, then the members of its lists.</summary>
    public List<DesignerElement> Elements()
    {
        var elements = new List<DesignerElement>();
        if (_body is null) return elements;
        var entries = Entries();
        var ids = entries.ToDictionary(entry => entry.Element.Id, entry => entry.Id, StringComparer.Ordinal);
        var collapsed = Block("collapsed")?.Entries.Where(entry => entry.Value == "yes").Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal) ?? [];
        foreach (var entry in entries)
        {
            var element = entry.Element;
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach ((string name, object? value) in element.Attributes) attributes[name] = View(element.Type, name, value);
            foreach (var name in HierarchicalRequirementSpecification.AttributesOf(element.Type))
            {
                if (attributes.TryGetValue(name, out var value) && value is not null) continue;
                attributes[name] = HierarchicalRequirementSpecification.Many(element.Type, name) ? new List<object?>()
                    : HierarchicalRequirementSpecification.TypeOf(element.Type, name) == "bool" ? false
                    : HierarchicalRequirementSpecification.TypeOf(element.Type, name) is "string" or "text" ? ""
                    : null;
            }
            if (element.Type is Item or Requirement) attributes["collapsed"] = collapsed.Contains(entry.Id);
            var parent = element.ParentId is { } libraryParent && ids.TryGetValue(libraryParent, out var mapped) ? mapped : element.ParentId;
            elements.Add(new DesignerElement(entry.Id, element.Type, parent, attributes));
        }
        foreach ((string block, string type) in new[] { ("tags", "Tag"), ("categories", "Category"), ("states", "State") })
        {
            foreach ((string name, string colour) in Members(block))
            {
                elements.Add(new DesignerElement(MemberId(type, name), type, null, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = name, ["colour"] = colour }));
            }
        }
        foreach (var filter in Block("filters")?.Entries ?? [])
        {
            elements.Add(new DesignerElement(DerivedIds.Filter(filter.Key), "Filter", null, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = filter.Key, ["query"] = filter.Value }));
        }
        return elements;
    }

    /// <summary>
    /// Plans and applies a fixture's edit, one change or a list of them, as one edit. A setting goes to the registration.
    /// A change to a list member writes only the registration, which a body subject does not have, so there it writes
    /// nothing. The first change that is no save also numbers the placeholders and sets the marks (DESL 0.2 section 5.3.5).
    /// </summary>
    public PlanResult Change(JsonElement edit)
    {
        if (edit.ValueKind == JsonValueKind.Object && edit.TryGetProperty("setting", out var setting)) return _registration!.Change(FixtureEdits.Setting(setting));
        if (_body is null) throw new InvalidOperationException("The fixture changes a body it does not have.");
        if (edit.ValueKind == JsonValueKind.Object && edit.TryGetProperty("save", out _)) return _body.Change(new ModelChange.Save());

        var entries = Entries();
        var changes = new List<ModelChange>();
        var sets = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var added = new List<string>();
        foreach (var part in edit.ValueKind == JsonValueKind.Array ? edit.EnumerateArray().ToList() : [edit])
        {
            if (part.TryGetProperty("set", out var set))
            {
                var occurrence = set.TryGetProperty("occurrence", out var nth) ? nth.GetInt32() : 1;
                if (Find(entries, set.GetProperty("element").GetString()!, occurrence) is not { } entry) continue;
                var attributes = sets.TryGetValue(entry.Element.Id, out var known) ? known : sets[entry.Element.Id] = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach ((string name, object? value) in FixtureEdits.Attributes(set.GetProperty("attributes"))) attributes[name] = Stored(entry.Element.Type, name, value);
                if (attributes.TryGetValue("id", out var renamed) && renamed is string id) added.Add(id);
            }
            else if (part.TryGetProperty("add", out var add))
            {
                var type = add.GetProperty("type").GetString()!;
                if (type is not (Item or Requirement or "Assessment")) continue;
                var attributes = (add.TryGetProperty("attributes", out var given) ? FixtureEdits.Attributes(given) : [])
                    .ToDictionary(attribute => attribute.Key, attribute => Stored(type, attribute.Key, attribute.Value), StringComparer.Ordinal);
                if (type == "Assessment") attributes.TryAdd("resolved", "false");
                string? id = type == "Assessment" ? null : add.GetProperty("id").GetString();
                if (id is not null) added.Add(id);
                var parent = add.TryGetProperty("parent", out var under) ? Library(entries, under.GetString()) : null;
                int? index = add.TryGetProperty("position", out var position) ? Index(position, parent, type) : null;
                changes.Add(new ModelChange.Add(type, id, attributes, parent, index));
            }
            else if (part.TryGetProperty("remove", out var remove))
            {
                if (Find(entries, remove.GetProperty("element").GetString()!, 1) is { } entry) changes.Add(new ModelChange.Remove(entry.Element.Id));
            }
            else if (part.TryGetProperty("move", out var move))
            {
                var moved = Find(entries, move.GetProperty("element").GetString()!, 1)!;
                var parent = Library(entries, move.GetProperty("parent").GetString());
                changes.Add(new ModelChange.Move(moved.Element.Id, parent, Index(move.GetProperty("position"), parent, moved.Element.Type) ?? -1));
            }
            else
            {
                throw new InvalidOperationException($"Unknown fixture edit: {part}.");
            }
        }
        Numbering(entries, sets, changes, added);
        changes.InsertRange(0, sets.Select(set => (ModelChange)new ModelChange.Set(set.Key, set.Value)));

        var splices = new List<Splice>();
        foreach (var change in changes)
        {
            var planned = _body.Plan(change);
            if (planned is not PlanResult.Planned { Edit: var part }) return planned;
            splices.AddRange(part.Splices);
        }
        var combined = new Edit([.. splices.Select((splice, at) => (splice, at)).OrderBy(listed => listed.splice.Start).ThenBy(listed => listed.at).Select(listed => listed.splice)]);
        if (combined.Splices.Count > 0) _body.Apply(combined);
        return new PlanResult.Planned(combined);
    }

    public UndoResult Undo() => _registration is not null ? _registration.Undo() : _body!.Undo();

    public UndoResult Redo() => _registration is not null ? _registration.Redo() : _body!.Redo();

    /// <summary>
    /// What the first edit writes besides itself (DESL 0.2 section 5.3.5): a number for every placeholder, the next of
    /// its kind in reading order, and the marks, set to the last number given of each kind, by setting the mark line or,
    /// when there is none, adding it.
    /// </summary>
    private void Numbering(List<Entry> entries, Dictionary<string, Dictionary<string, object?>> sets, List<ModelChange> changes, List<string> added)
    {
        var marks = _body!.Model.Elements.FirstOrDefault(element => element.Type == "Marks");
        var given = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in entries.Where(entry => entry.Placeholder))
        {
            var attributes = sets.TryGetValue(entry.Element.Id, out var known) ? known : sets[entry.Element.Id] = new Dictionary<string, object?>(StringComparer.Ordinal);
            attributes["id"] = entry.Id;
            Give(entry.Element.Type, entry.Id);
        }
        foreach (var id in added) Give(id.StartsWith(Prefix(Item), StringComparison.Ordinal) ? Item : Requirement, id);
        if (given.Count == 0) return;

        // Only the mark of a kind this edit gives a number to is set; the other is left as it is (DESL 0.2 section 5.3.5).
        var update = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string type, int number) in given)
        {
            var mark = Mark(type);
            var last = Math.Max(number, Math.Max(Number(marks?.Attributes.GetValueOrDefault(mark) as string), entries.Where(e => e.Element.Type == type).Select(e => Number(e.Id)).DefaultIfEmpty(0).Max()));
            if (marks?.Attributes.GetValueOrDefault(mark) as string != Counter(type, last)) update[mark] = Counter(type, last);
        }
        if (update.Count == 0) return;
        if (marks is null) changes.Insert(0, new ModelChange.Add("Marks", null, update));
        else sets[marks.Id] = update;

        void Give(string type, string id) => given[type] = Math.Max(given.GetValueOrDefault(type), Number(id));
    }

    private static string Mark(string type) => type == Item ? "lastItem" : "lastRequirement";

    /// <summary>
    /// The body's entries by the designer's ids: an item or a requirement by the id written on its line, a placeholder by
    /// the number it is given, the next of its kind after the mark and every number the document holds, in reading order;
    /// an assessment by its place. A duplicate keeps its written number, which the library replaces by a place id.
    /// </summary>
    private List<Entry> Entries()
    {
        var model = _body!.Model;
        var lines = Encoding.UTF8.GetString(_body.Bytes).Split('\n');
        var marks = model.Elements.FirstOrDefault(element => element.Type == "Marks");
        var written = new List<(FblElement Element, string? Id)>();
        foreach (var element in model.Elements)
        {
            if (element.Type is not (Item or Requirement)) continue;
            var line = element.Line > 0 && element.Line <= lines.Length ? lines[element.Line - 1] : "";
            written.Add((element, WrittenId().Match(line) is { Success: true } match ? match.Groups["id"].Value : element.Id));
        }
        var next = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [Item] = Math.Max(Number(marks?.Attributes.GetValueOrDefault("lastItem") as string), written.Where(w => w.Element.Type == Item).Select(w => Number(w.Id)).DefaultIfEmpty(0).Max()),
            [Requirement] = Math.Max(Number(marks?.Attributes.GetValueOrDefault("lastRequirement") as string), written.Where(w => w.Element.Type == Requirement).Select(w => Number(w.Id)).DefaultIfEmpty(0).Max()),
        };
        var entries = new List<Entry>();
        var assessments = 0;
        foreach (var element in model.Elements)
        {
            if (element.Type is Item or Requirement)
            {
                var id = written.First(w => w.Element == element).Id!;
                entries.Add(id == Prefix(element.Type) + "NEW" ? new Entry(element, Counter(element.Type, ++next[element.Type]), true) : new Entry(element, id, false));
            }
            else if (element.Type == "Assessment")
            {
                entries.Add(new Entry(element, DerivedIds.Assessment(assessments++), false));
            }
        }
        return entries;
    }

    /// <summary>The members of a list (DESL 0.2 sections 5.5, 7.7.2): its block when there is one, else its initial members and then those the body implies, in their order.</summary>
    private List<KeyValuePair<string, string>> Members(string block)
    {
        if (Block(block) is { } stored) return stored.Entries.Select(entry => new KeyValuePair<string, string>(entry.Key, entry.Value)).ToList();
        var members = HierarchicalRequirementSpecification.Initial(block);
        if (_body is null) return members;
        var attribute = block switch { "tags" => "tags", "categories" => "category", _ => "state" };
        foreach (var element in _body.Model.Elements.Where(element => element.Type is Item or Requirement))
        {
            var value = element.Attributes.GetValueOrDefault(attribute);
            foreach (var name in value is List<object?> many ? many.OfType<string>() : value is string one ? [one] : [])
            {
                if (members.All(member => Lower(member.Key) != Lower(name))) members.Add(new KeyValuePair<string, string>(name, "default"));
            }
        }
        return members;
    }

    private RegistrationBlock? Block(string name) => _registration?.Document.Block(name);

    private static string MemberId(string type, string name) => type switch
    {
        "Tag" => DerivedIds.Tag(name),
        "Category" => DerivedIds.Category(name),
        _ => DerivedIds.State(name),
    };

    /// <summary>A value as the designer holds it: an enumeration by its key, a bool as a bool.</summary>
    private static object? View(string type, string attribute, object? value)
    {
        var declared = HierarchicalRequirementSpecification.TypeOf(type, attribute);
        if (declared is null || value is not string text) return value;
        if (HierarchicalRequirementSpecification.IsEnumeration(declared)) return HierarchicalRequirementSpecification.Key(declared, text);
        return declared == "bool" ? text == "true" : value;
    }

    /// <summary>A value as the file stores it: an enumeration by its stored form, a bool as its text.</summary>
    private static object? Stored(string type, string attribute, object? value)
    {
        var declared = HierarchicalRequirementSpecification.TypeOf(type, attribute);
        if (value is bool flag && declared == "bool") return flag ? "true" : "false";
        if (declared is not null && value is string key && HierarchicalRequirementSpecification.IsEnumeration(declared)) return HierarchicalRequirementSpecification.Stored(declared, key);
        return value;
    }

    private static Entry? Find(List<Entry> entries, string id, int occurrence) =>
        entries.Where(entry => entry.Id == id).Skip(occurrence - 1).FirstOrDefault();

    private static string? Library(List<Entry> entries, string? id) => id is null ? null : Find(entries, id, 1)?.Element.Id ?? id;

    /// <summary>The model change's index for a fixture's position, among the siblings of <paramref name="type"/> under <paramref name="parent"/> as they are before the change.</summary>
    private int? Index(JsonElement position, string? parent, string type)
    {
        if (position.TryGetProperty("index", out var index)) return index.GetInt32();
        var entries = Entries();
        var siblings = entries.Where(entry => entry.Element.ParentId == parent && (parent is not null || entry.Element.Type == type)).Select(entry => entry.Id).ToList();
        if (position.TryGetProperty("after", out var after)) return siblings.IndexOf(after.GetString()!) + 1;
        return position.TryGetProperty("before", out var before)
            ? siblings.IndexOf(before.GetString()!)
            : throw new InvalidOperationException($"Unknown fixture position: {position}.");
    }

    private static string Prefix(string type) => type == Item ? "ITEM-" : "REQ-";

    private static string Counter(string type, int number) => Prefix(type) + number.ToString("D4", CultureInfo.InvariantCulture);

    private static int Number(string? id) =>
        id is not null && CounterId().Match(id) is { Success: true } match ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) : 0;

    private static string Lower(string text) => string.Concat(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));

    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+(?<id>\S+)")]
    private static partial Regex WrittenId();

    [GeneratedRegex(@"^(?:ITEM|REQ)-(?<number>\d+)$")]
    private static partial Regex CounterId();

    /// <summary>An entry of the body with the id the designer gives it, and whether it was a placeholder.</summary>
    private sealed record Entry(FblElement Element, string Id, bool Placeholder);
}
