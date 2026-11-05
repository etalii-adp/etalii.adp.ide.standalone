using System.Globalization;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The designer's model onto the host's hierarchy surface (DESL section 7.7, spec 029 contracts/hierarchy-surface.md):
/// the items as its levels, the requirements as its entries, the tags, category and state as its labels and the
/// assessments as its notes. The surface knows none of these words; this is where they are said.
/// </summary>
internal static partial class HierarchicalRequirementSurfaceMapper
{
    private const string ItemType = "Item";
    private const string RequirementType = "Requirement";
    private const string AssessmentType = "Assessment";

    /// <summary>The id of a node without a usable number: the line of its bullet, valid for one model.</summary>
    private static string LineId(int line) => string.Create(CultureInfo.InvariantCulture, $"line/{line}");

    /// <param name="document">What reading the file gave.</param>
    /// <param name="title">The document's title, for the trail.</param>
    /// <param name="cause">What this model follows, one of <see cref="HierarchySurfaceCauses"/>.</param>
    /// <param name="stored">The names each list's block holds, by block, or null for a block the registration does not hold.</param>
    public static HierarchySurfaceModel Map(HierarchicalRequirementDocument document, string title, string cause, Func<string, IReadOnlyList<string>?> stored)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stored);
        var ids = NodeIds(document.Entries);
        var byNumber = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.Entries)
        {
            byNumber.TryAdd(entry.Id, ids[entry]);
        }

        var nodes = document.Entries.Select(entry => Node(entry, ids[entry], byNumber)).ToList();
        // A finding stands at the entry whose bullet or description holds its line, so selecting it shows that box (FR-021).
        var nodeAt = nodes.Zip(document.Entries)
            .SelectMany(pair => Enumerable.Range(pair.Second.Line, 1 + DescriptionLines(pair.Second)).Select(line => (Line: line, pair.First.Id)))
            .GroupBy(at => at.Line)
            .ToDictionary(group => group.Key, group => group.First().Id);
        return new HierarchySurfaceModel(
            title,
            document.ReadOnlyReason,
            cause,
            nodes,
            [
                Options("tags", "tag", "tags", document.Tags.Select(tag => (tag.Name, tag.Colour)), nodes, stored),
                Options("category", "category", "categories", document.Categories.Select(category => (category.Name, category.Colour)), nodes, stored),
                Options("state", "state", "states", document.States.Select(state => (state.Name, state.Colour)), nodes, stored),
            ],
            [.. document.Filters.Select(filter => new HierarchySurfaceFilter("filter/" + filter.Name, filter.Name, filter.Query))],
            [.. document.Assessments.Select(assessment => Note(assessment, byNumber))],
            [.. document.Findings.Select(finding => new HierarchySurfaceFinding(
                finding.Code,
                Severity(finding.Code),
                finding.Message,
                finding.InRegistration ? "" : nodeAt.GetValueOrDefault(finding.Line, ""),
                finding.Line))],
            [.. document.Columns.Select(column => new HierarchySurfaceColumnWidth(ColumnOperations.IdPrefix + column.Level.ToString(CultureInfo.InvariantCulture), column.Level, column.Width))]);
    }

    /// <summary>The lines below an entry's bullet that its description takes.</summary>
    private static int DescriptionLines(RequirementEntry entry) => entry.Description.Length == 0 ? 0 : entry.Description.Split('\n').Length;

    /// <summary>
    /// The id of each entry: its number when that is usable, else the line of its bullet. A placeholder's number is
    /// given in memory only, and a later duplicate's is the earlier entry's, so neither can be named by it.
    /// </summary>
    internal static Dictionary<RequirementEntry, string> NodeIds(IReadOnlyList<RequirementEntry> entries)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new Dictionary<RequirementEntry, string>(ReferenceEqualityComparer.Instance);
        foreach (var entry in entries)
        {
            ids[entry] = !entry.Placeholder && seen.Add(entry.Id) ? entry.Id : LineId(entry.Line);
        }

        return ids;
    }

    private static HierarchySurfaceNode Node(RequirementEntry entry, string id, Dictionary<string, string> byNumber)
    {
        var type = entry is Item ? ItemType : RequirementType;
        List<HierarchySurfaceLabelValues> labels = [new("tags", entry.Tags)];
        if (entry is Requirement requirement)
        {
            if (requirement.Category is { } category) labels.Add(new HierarchySurfaceLabelValues("category", [category]));
            if (requirement.State is { } state) labels.Add(new HierarchySurfaceLabelValues("state", [state]));
        }

        return new HierarchySurfaceNode(
            id,
            entry.Placeholder ? (entry is Item ? "ITEM-NEW" : "REQ-NEW") : entry.Id,
            entry is Item ? HierarchySurfaceRoles.Level : HierarchySurfaceRoles.Entry,
            type,
            entry.ParentId is { } parent ? byNumber.GetValueOrDefault(parent, "") : "",
            entry.Title,
            entry.Description,
            labels,
            References(type, "description", entry.Description, 0, byNumber),
            entry.Collapsed && id == entry.Id);
    }

    private static HierarchySurfaceNote Note(Assessment assessment, Dictionary<string, string> byNumber) => new(
        assessment.Id,
        assessment.Kind.ToString(),
        assessment.About,
        byNumber.GetValueOrDefault(assessment.About, ""),
        assessment.Text,
        assessment.Detail,
        assessment.Resolved,
        [
            .. References(AssessmentType, "text", assessment.Text, 0, byNumber),
            .. References(AssessmentType, "detail", assessment.Detail, assessment.Text.Length + 1, byNumber),
        ]);

    /// <summary>The bracketed numbers <paramref name="text"/> names, as the specification's mentions find them; a link is none.</summary>
    private static List<HierarchySurfaceReference> References(string type, string attribute, string text, int offset, Dictionary<string, string> byNumber)
    {
        if (text.Length == 0 || HierarchicalRequirementDefinition.Mentions(type, attribute) is not { } pattern) return [];
        return
        [
            .. pattern.Matches(text).Where(mention => mention.Groups["id"].Success).Select(mention =>
                new HierarchySurfaceReference(offset + mention.Index, mention.Length, byNumber.GetValueOrDefault(mention.Groups["id"].Value, ""))),
        ];
    }

    /// <summary>
    /// One label's list: its members in their order, then any a node carries that the list does not hold. A member is
    /// implicit when the list does not hold it: neither its block nor, while there is none, its initial members.
    /// </summary>
    private static HierarchySurfaceOptionList Options(
        string label, string idPrefix, string block, IEnumerable<(string Name, string Colour)> members, List<HierarchySurfaceNode> nodes, Func<string, IReadOnlyList<string>?> stored)
    {
        var held = (stored(block) ?? HierarchicalRequirementDefinition.Initial(block).Select(member => member.Key).ToList()).Select(LowerAscii).ToHashSet(StringComparer.Ordinal);
        var options = new List<HierarchySurfaceOption>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string name, string colour) in members.Concat(nodes.SelectMany(node => node.Labels).Where(values => values.Label == label).SelectMany(values => values.Values).Select(name => (name, "default"))))
        {
            var key = LowerAscii(name);
            if (seen.Add(key)) options.Add(new HierarchySurfaceOption($"{idPrefix}/{key}", name, colour, !held.Contains(key)));
        }

        return new HierarchySurfaceOptionList(label, options);
    }

    /// <summary>How much a finding weighs, as the companion's table "What reading reports" says.</summary>
    private static string Severity(string code) => code switch
    {
        "std.missingId" or "std.mentions" => "warning",
        "fbl.unknown-block" => "info",
        _ => "error",
    };

    /// <summary>The document's title: its first heading of level one, or the file's base name when it has none.</summary>
    public static string Title(string text, string fileName)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TitleLine().Match(text) is { Success: true } match ? match.Groups["title"].Value.Trim() : Path.GetFileNameWithoutExtension(fileName);
    }

    /// <summary>CEL's <c>lowerAscii</c>, which the specification's ids of list members are derived with.</summary>
    private static string LowerAscii(string text) => string.Concat(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));

    [GeneratedRegex(@"^#[ \t]+(?<title>[^\r\n]+)", RegexOptions.Multiline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex TitleLine();
}
