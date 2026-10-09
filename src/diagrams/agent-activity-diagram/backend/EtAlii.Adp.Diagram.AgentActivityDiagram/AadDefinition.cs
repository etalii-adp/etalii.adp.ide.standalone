using System.Text.Json;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// The bundled definition, loaded once: the DISL specification this module derives from, and the
/// few things the module reads off it by name.
/// </summary>
/// <remarks>
/// <b>What a status is called, which statuses there are and in what order are the definition's.</b>
/// The module reads them here and never writes them down a second time, so a status added in
/// etalii.adp reaches the canvas and the file by bundling the definition again and by nothing else.
/// </remarks>
internal static class AadDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(AadDefinition).Assembly, "agent-activity-diagram.dis"));
    private static readonly Lazy<IReadOnlyDictionary<string, AadEnum>> LoadedEnums = new(ReadEnums);

    /// <summary>The key of a location's one group of rows, in the file and on the wire.</summary>
    public const string PullRequestsGroup = "pullRequests";

    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>A task's statuses, in the order their groups are listed.</summary>
    public static AadEnum TaskStatus => LoadedEnums.Value["TaskStatus"];

    public static AadEnum SpecificationStatus => LoadedEnums.Value["SpecificationStatus"];

    public static AadEnum EnvironmentKind => LoadedEnums.Value["EnvironmentKind"];

    /// <summary>
    /// The groups that are folded until the reader unfolds them: the task statuses the definition
    /// marks collapsed, and a location's pull requests.
    /// </summary>
    public static IReadOnlySet<string> CollapsedByDefault { get; } = ReadCollapsedByDefault();

    private static IReadOnlyDictionary<string, AadEnum> ReadEnums()
    {
        var enums = new Dictionary<string, AadEnum>(StringComparer.Ordinal);
        foreach (var declared in Specification.Root.GetProperty("metamodel").GetProperty("enums").EnumerateObject())
        {
            var members = new List<AadEnumMember>();
            foreach (var member in declared.Value.GetProperty("values").EnumerateObject())
            {
                // A member is stored under its own name unless it says otherwise: `inputRequired` is
                // written `input-required`.
                var stored = member.Value.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : member.Name;
                var label = member.Value.TryGetProperty("label", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString()! : member.Name;
                members.Add(new AadEnumMember(member.Name, stored, label));
            }

            enums[declared.Name] = new AadEnum(members);
        }

        return enums;
    }

    private static HashSet<string> ReadCollapsedByDefault()
    {
        var collapsed = new HashSet<string>(StringComparer.Ordinal);
        var nodes = Specification.Root.GetProperty("notation").GetProperty("nodes");
        foreach (var node in nodes.EnumerateObject())
        {
            if (!node.Value.TryGetProperty("compartments", out var compartments)) continue;
            foreach (var compartment in compartments.EnumerateArray())
            {
                if (compartment.TryGetProperty("groupBy", out var grouping) && grouping.TryGetProperty("collapsed", out var groups) && groups.ValueKind == JsonValueKind.Object)
                {
                    foreach (var group in groups.EnumerateObject().Where(group => group.Value.ValueKind == JsonValueKind.True))
                    {
                        collapsed.Add(group.Name);
                    }
                }
                else if (compartment.TryGetProperty("collapsed", out var whole) && whole.ValueKind == JsonValueKind.True
                    && compartment.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    collapsed.Add(id.GetString()!);
                }
            }
        }

        return collapsed;
    }
}

/// <summary>One enum of the definition: its members in declared order.</summary>
internal sealed record AadEnum(IReadOnlyList<AadEnumMember> Members)
{
    /// <summary>The member whose name or stored form is <paramref name="text"/>, or null for a value the definition does not have.</summary>
    public AadEnumMember? Find(string? text) =>
        text is null ? null : Members.FirstOrDefault(member => member.Name == text || member.Stored == text);
}

/// <summary>One member: its name in the model, how the file writes it, and the words it is shown with.</summary>
internal sealed record AadEnumMember(string Name, string Stored, string Label);
