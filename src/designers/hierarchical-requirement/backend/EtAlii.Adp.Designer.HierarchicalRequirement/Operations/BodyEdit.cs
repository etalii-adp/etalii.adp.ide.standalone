using System.Globalization;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// One edit of a requirement document's body, as the designer writes it: the changes an operation asks for, together
/// with what DESL 0.2 section 5.3.5 has every edit write besides itself - the number reading gave each placeholder, and
/// the marks of the counters (spec 029 R17). The changes are planned through the FBL library and made as one edit, so
/// one undo takes all of them back.
/// </summary>
internal sealed partial class BodyEdit
{
    private const string ItemType = "Item";
    private const string MarksType = "Marks";

    private readonly HierarchicalRequirementBody _read;
    private readonly Dictionary<string, Dictionary<string, object?>> _sets = new(StringComparer.Ordinal);
    private readonly List<ModelChange> _changes = [];

    public BodyEdit(HierarchicalRequirementBody read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _read = read;
    }

    /// <summary>The library's id of the entry the designer names <paramref name="entry"/>: its bullet's element.</summary>
    public string ElementOf(RequirementEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _read.Body.Model.Elements.First(element => element.Line == entry.Line && element.Type is ItemType or "Requirement").Id;
    }

    /// <summary>Sets <paramref name="attribute"/> of the library's element <paramref name="elementId"/> to <paramref name="value"/>.</summary>
    public void Set(string elementId, string attribute, object? value) => SetsOf(elementId)[attribute] = value;

    /// <summary>
    /// The body's bytes after the edit, with every placeholder numbered and the marks set; or the sentence saying why
    /// the library refused it, in which case the body is unchanged.
    /// </summary>
    public (byte[]? Bytes, string Refusal) Write()
    {
        Numbering();
        var changes = _sets.Select(set => (ModelChange)new ModelChange.Set(set.Key, set.Value)).Concat(_changes).ToList();
        var splices = new List<Splice>();
        foreach (var change in changes)
        {
            switch (_read.Body.Plan(change))
            {
                case PlanResult.Planned planned:
                    splices.AddRange(planned.Edit.Splices);
                    break;
                case PlanResult.Refused refused:
                    return (null, refused.Reason);
            }
        }

        // Every change was planned against the bytes as read, so their splices are made together, in the file's order.
        var combined = new Edit([.. splices.Select((splice, at) => (splice, at)).OrderBy(listed => listed.splice.Start).ThenBy(listed => listed.at).Select(listed => listed.splice)]);
        if (combined.Splices.Count > 0) _read.Body.Apply(combined);
        return (_read.Body.Bytes, "");
    }

    private Dictionary<string, object?> SetsOf(string elementId) =>
        _sets.TryGetValue(elementId, out var known) ? known : _sets[elementId] = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>
    /// What every edit writes besides itself (DESL 0.2 section 5.3.5): each placeholder's number, the one reading gave it,
    /// and the mark of each kind given a number, set to the last number of that kind, on the mark line or, when the file
    /// has none, on a mark line added.
    /// </summary>
    private void Numbering()
    {
        var document = _read.Document;
        var given = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in document.Entries.Where(entry => entry.Placeholder))
        {
            SetsOf(ElementOf(entry))["id"] = entry.Id;
            var type = entry is Item ? ItemType : "Requirement";
            given[type] = Math.Max(given.GetValueOrDefault(type), Number(entry.Id));
        }

        if (given.Count == 0) return;

        // Only the mark of a kind this edit gives a number to is set; the other is left as it is.
        var marks = _read.Body.Model.Elements.FirstOrDefault(element => element.Type == MarksType);
        var update = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string type, int number) in given)
        {
            var mark = type == ItemType ? "lastItem" : "lastRequirement";
            var written = type == ItemType ? document.LastItem : document.LastRequirement;
            var highest = document.Entries.Where(entry => entry is Item == (type == ItemType)).Select(entry => Number(entry.Id)).DefaultIfEmpty(0).Max();
            var last = Counter(type, Math.Max(number, Math.Max(Number(written), highest)));
            if (written != last) update[mark] = last;
        }

        if (update.Count == 0) return;
        if (marks is null) _changes.Insert(0, new ModelChange.Add(MarksType, null, update));
        else foreach ((string mark, object? value) in update) SetsOf(marks.Id)[mark] = value;
    }

    private static string Counter(string type, int number) => (type == ItemType ? "ITEM-" : "REQ-") + number.ToString("D4", CultureInfo.InvariantCulture);

    private static int Number(string? id) =>
        id is not null && CounterId().Match(id) is { Success: true } match ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) : 0;

    /// <summary>A counter's number, <c>ITEM-0004</c> or <c>REQ-0031</c>.</summary>
    [GeneratedRegex(@"^(?:ITEM|REQ)-(?<number>\d+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex CounterId();
}
