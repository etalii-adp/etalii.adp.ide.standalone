using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Registration;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// One requirement document read through FBL with the bundled binding, and its registration when it has one, into the
/// designer's model (hierarchical-requirement FR-026, FR-027). The module has no parser of its own: the entries are the
/// library's reading of the binding's rules, and this class adds what DESL says of them. A placeholder is numbered in
/// memory only, the mark line is read, and every fault of the companion's table "What reading reports" is reported at
/// its bullet with every other entry usable. Reading writes nothing.
/// </summary>
internal sealed partial class HierarchicalRequirementBody
{
    private const string ItemType = "Item";
    private const string RequirementType = "Requirement";

    private HierarchicalRequirementBody(OpenBody body, OpenRegistration? registration, HierarchicalRequirementDocument document)
    {
        Body = body;
        Registration = registration;
        Document = document;
    }

    /// <summary>The Markdown file, open through the binding.</summary>
    public OpenBody Body { get; }

    /// <summary>The registration beside it, or null when there is none.</summary>
    public OpenRegistration? Registration { get; }

    /// <summary>What the designer shows.</summary>
    public HierarchicalRequirementDocument Document { get; }

    /// <summary>
    /// Reads the Markdown file <paramref name="bytes"/>, named <paramref name="fileName"/>, and the bytes of its
    /// registration, named <paramref name="registrationName"/>, when it has one.
    /// </summary>
    public static HierarchicalRequirementBody Read(byte[] bytes, string fileName, byte[]? registrationBytes = null, string registrationName = "")
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var body = OpenBody.Open(bytes, HierarchicalRequirementDefinition.Binding, HierarchicalRequirementDefinition.Options(fileName));
        var registration = registrationBytes is null ? null : OpenRegistration.Open(registrationBytes, HierarchicalRequirementDefinition.Binding);
        var model = body.Model;
        if (model.Unreadable)
        {
            var reason = model.Findings.FirstOrDefault()?.Message ?? "The file could not be read.";
            var document = new HierarchicalRequirementDocument(null, null, [], [], [], [], [], [], [.. model.Findings.Select(Of)],
                $"This file cannot be read as a requirement document: {reason} It is left exactly as it is and cannot be changed here.");
            return new HierarchicalRequirementBody(body, registration, document);
        }
        return new HierarchicalRequirementBody(body, registration, new Reading(bytes, model, registration, registrationName).Document());
    }

    private static HierarchicalRequirementFinding Of(Finding finding) => new(finding.Code, finding.Message, finding.Location?.Line ?? 0);

    /// <summary>The written number on a bullet's line: the first word after its marker.</summary>
    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+(?<id>\S+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex WrittenId();

    /// <summary>A counter's number, <c>ITEM-0004</c> or <c>REQ-0031</c>.</summary>
    [GeneratedRegex(@"^(?:ITEM|REQ)-(?<number>\d+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex CounterId();

    /// <summary>One reading: the library's model of the file, made into the designer's.</summary>
    private sealed class Reading(byte[] bytes, FblModel model, OpenRegistration? registration, string registrationName)
    {
        private readonly string[] _lines = Encoding.UTF8.GetString(bytes).Split('\n');
        private readonly FblElement? _marks = model.Elements.FirstOrDefault(element => element.Type == "Marks");

        public HierarchicalRequirementDocument Document()
        {
            var entries = Entries();
            var assessments = Assessments();
            var findings = Findings(entries, assessments);
            if (registration is not null)
            {
                registration.KnownIds = entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
                findings.AddRange(registration.Findings(registrationName).Select(finding => Of(finding) with { InRegistration = true }));
            }
            var envelope = DedEnvelope.Read(bytes);
            return new HierarchicalRequirementDocument(
                Mark("lastItem"),
                Mark("lastRequirement"),
                entries,
                assessments,
                [.. Members("tags", "tags", entries).Select(member => new Tag(member.Key, member.Value))],
                [.. Members("categories", "category", entries).Select(member => new Category(member.Key, member.Value))],
                [.. Members("states", "state", entries).Select(member => new State(member.Key, member.Value))],
                [.. Block("filters")?.Select(entry => new Filter(entry.Key, entry.Value)) ?? []],
                findings,
                envelope?.ReadOnlyReason ?? "");
        }

        private string? Mark(string attribute) => _marks?.Attributes.GetValueOrDefault(attribute) as string;

        /// <summary>
        /// The items and requirements in reading order, each by the number written on its bullet. A placeholder is given
        /// the next number of its kind, after the mark and every number the file holds, in reading order (DESL 0.2
        /// section 5.3.5); a duplicate keeps its written number, where the library gives it a place id.
        /// </summary>
        private List<RequirementEntry> Entries()
        {
            var read = model.Elements.Where(element => element.Type is ItemType or RequirementType).Select(element => (Element: element, Written: Written(element))).ToList();
            var next = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [ItemType] = Highest(ItemType, "lastItem"),
                [RequirementType] = Highest(RequirementType, "lastRequirement"),
            };
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            var collapsed = Block("collapsed")?.Where(entry => entry.Value == "yes").Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal) ?? [];
            var entries = new List<RequirementEntry>();
            foreach ((FblElement element, string written) in read)
            {
                var placeholder = written == Prefix(element.Type) + "NEW";
                var id = placeholder ? Counter(element.Type, ++next[element.Type]) : written;
                ids[element.Id] = id;
                var parent = element.ParentId is { } libraryParent ? ids.GetValueOrDefault(libraryParent, libraryParent) : null;
                var title = Text(element, "title");
                var description = Text(element, "description");
                var tags = Words(element, "tags");
                entries.Add(element.Type == ItemType
                    ? new Item(id, parent, title, description, tags, collapsed.Contains(id), element.Line, placeholder)
                    : new Requirement(id, parent, title, description, tags, element.Attributes.GetValueOrDefault("category") as string, element.Attributes.GetValueOrDefault("state") as string, collapsed.Contains(id), element.Line, placeholder));
            }
            return entries;

            int Highest(string type, string mark) =>
                Math.Max(Number(Mark(mark)), read.Where(entry => entry.Element.Type == type).Select(entry => Number(entry.Written)).DefaultIfEmpty(0).Max());
        }

        private List<Assessment> Assessments() =>
        [
            .. model.Elements.Where(element => element.Type == "Assessment").Select((element, place) => new Assessment(
                string.Create(CultureInfo.InvariantCulture, $"assessment/{place}"),
                Enum.Parse<AssessmentKind>(Text(element, "kind"), ignoreCase: true),
                Text(element, "about"),
                Text(element, "text"),
                Text(element, "detail"),
                element.Attributes.GetValueOrDefault("resolved") is true or "true",
                element.Line)),
        ];

        /// <summary>
        /// The findings in DESL 0.2 section 6.3's order: the reader's in reading order, the library's among them, then
        /// <c>std.required</c>, <c>std.containment</c> and <c>std.mentions</c>, each in document order.
        /// </summary>
        private List<HierarchicalRequirementFinding> Findings(List<RequirementEntry> entries, List<Assessment> assessments)
        {
            var placeholders = entries.Where(entry => entry.Placeholder).Select(entry => entry.Line).ToHashSet();
            var reader = model.Findings
                .Where(finding => !(finding.Code == FindingCodes.DuplicateId && placeholders.Contains(finding.Location?.Line ?? 0)))
                .Select(Of)
                .Concat(entries.Where(entry => entry.Placeholder).Select(entry => new HierarchicalRequirementFinding(
                    FindingCodes.MissingId, $"{entry.Id} is numbered when the document is next changed: the file holds {Prefix(entry is Item ? ItemType : RequirementType)}NEW.", entry.Line)))
                .OrderBy(finding => finding.Line);
            var required = entries.Where(entry => entry.Title.Length == 0)
                .Select(entry => new HierarchicalRequirementFinding("std.required", $"{entry.Id} has no title.", entry.Line));
            var byId = entries.GroupBy(entry => entry.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var containment = entries
                .Where(entry => (entry is Requirement && entry.ParentId is null) || (entry.ParentId is { } parent && byId.GetValueOrDefault(parent) is Requirement))
                .Select(entry => new HierarchicalRequirementFinding("std.containment", entry.ParentId is null
                    ? $"{entry.Id} stands in no item: a requirement stands in an item."
                    : $"{entry.Id} is nested in the requirement {entry.ParentId}: a requirement holds nothing.", entry.Line));
            var mentions = new List<HierarchicalRequirementFinding>();
            foreach (var entry in entries)
            {
                var type = entry is Item ? ItemType : RequirementType;
                mentions.AddRange(Unresolved(type, "description", entry.Description, entry.Line + 1, byId));
            }
            foreach (var assessment in assessments)
            {
                mentions.AddRange(Unresolved("Assessment", "about", assessment.About, assessment.Line, byId));
                mentions.AddRange(Unresolved("Assessment", "text", assessment.Text, assessment.Line, byId));
                mentions.AddRange(Unresolved("Assessment", "detail", assessment.Detail, assessment.Line + 1, byId));
            }
            return [.. reader, .. required.OrderBy(f => f.Line), .. containment.OrderBy(f => f.Line), .. mentions.OrderBy(f => f.Line)];
        }

        /// <summary>The mentions of <paramref name="text"/> that name no entry, each at the line it stands on.</summary>
        private static IEnumerable<HierarchicalRequirementFinding> Unresolved(string type, string attribute, string text, int firstLine, Dictionary<string, RequirementEntry> entries)
        {
            if (text.Length == 0 || HierarchicalRequirementDefinition.Mentions(type, attribute) is not { } pattern) yield break;
            foreach (Match mention in pattern.Matches(text))
            {
                if (!mention.Groups["id"].Success || entries.ContainsKey(mention.Groups["id"].Value)) continue;
                var line = firstLine + text.AsSpan(0, mention.Index).Count('\n');
                yield return new HierarchicalRequirementFinding("std.mentions", $"{mention.Groups["id"].Value} names no item or requirement of this document.", line);
            }
        }

        /// <summary>
        /// The members of a list (DESL 0.2 sections 5.5 and 7.7.2): its block in the registration when there is one, else
        /// its initial members and then those the entries imply, in the order they first appear, spelt as they first appear.
        /// </summary>
        private List<KeyValuePair<string, string>> Members(string block, string attribute, List<RequirementEntry> entries)
        {
            if (Block(block) is { } stored) return [.. stored];
            var members = HierarchicalRequirementDefinition.Initial(block).ToList();
            foreach (var entry in entries)
            {
                IEnumerable<string> names = attribute switch
                {
                    "tags" => entry.Tags,
                    "category" => entry is Requirement { Category: { } category } ? [category] : [],
                    _ => entry is Requirement { State: { } state } ? [state] : [],
                };
                foreach (var name in names)
                {
                    if (members.All(member => LowerAscii(member.Key) != LowerAscii(name))) members.Add(new KeyValuePair<string, string>(name, "default"));
                }
            }
            return members;
        }

        private IReadOnlyList<KeyValuePair<string, string>>? Block(string name) => registration?.BlockEntries(name);

        private string Written(FblElement element)
        {
            var line = element.Line > 0 && element.Line <= _lines.Length ? _lines[element.Line - 1] : "";
            return WrittenId().Match(line) is { Success: true } match ? match.Groups["id"].Value : element.Id;
        }

        private static string Text(FblElement element, string attribute) => element.Attributes.GetValueOrDefault(attribute) as string ?? "";

        private static List<string> Words(FblElement element, string attribute) =>
            element.Attributes.GetValueOrDefault(attribute) is List<object?> words ? [.. words.OfType<string>()] : [];
    }

    private static string Prefix(string type) => type == ItemType ? "ITEM-" : "REQ-";

    private static string Counter(string type, int number) => Prefix(type) + number.ToString("D4", CultureInfo.InvariantCulture);

    private static int Number(string? id) =>
        id is not null && CounterId().Match(id) is { Success: true } match ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) : 0;

    /// <summary>CEL's <c>lowerAscii</c>, which the specification's ids of list members are derived with.</summary>
    private static string LowerAscii(string text) => string.Concat(text.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c));
}
