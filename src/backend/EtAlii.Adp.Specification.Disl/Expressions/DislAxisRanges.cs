using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>One range of an axis (DISL 0.3 §5.3): its id and the part of the domain it names.</summary>
internal sealed record DislAxisRange(string Id, double From, double To);

/// <summary>
/// The ranges of a specification's axes (DISL 0.3 §5.3), by axis name, read once at load for <c>axisRange</c> (§12.4):
/// an axis with no <c>ranges</c> has none, and its <c>max</c> is where a range's <c>to</c> is included.
/// </summary>
internal sealed class DislAxisRanges
{
    private readonly Dictionary<string, (double? Max, IReadOnlyList<DislAxisRange> Ranges)> _axes;

    private DislAxisRanges(Dictionary<string, (double? Max, IReadOnlyList<DislAxisRange> Ranges)> axes) => _axes = axes;

    /// <summary>The axes of <c>coordinates.axes</c> in <paramref name="root"/>; a range without an id or numeric bounds is left out.</summary>
    public static DislAxisRanges Read(JsonElement root)
    {
        var axes = new Dictionary<string, (double? Max, IReadOnlyList<DislAxisRange> Ranges)>(StringComparer.Ordinal);
        if (root.TryGetProperty("coordinates", out var coordinates) && coordinates.ValueKind == JsonValueKind.Object
            && coordinates.TryGetProperty("axes", out var declared) && declared.ValueKind == JsonValueKind.Object)
        {
            foreach (var axis in declared.EnumerateObject())
            {
                if (axis.Value.ValueKind != JsonValueKind.Object) continue;
                var ranges = new List<DislAxisRange>();
                if (axis.Value.TryGetProperty("ranges", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var range in list.EnumerateArray())
                    {
                        if (JsonProperties.String(range, "id") is { } id && Bound(range, "from") is { } from && Bound(range, "to") is { } to)
                        {
                            ranges.Add(new DislAxisRange(id, from, to));
                        }
                    }
                }
                axes[axis.Name] = (Bound(axis.Value, "max"), ranges);
            }
        }
        return new DislAxisRanges(axes);
    }

    /// <summary>
    /// The id of the range of <paramref name="axis"/> that holds <paramref name="value"/>, the first in declaration order:
    /// <c>from</c> included, <c>to</c> excluded except at the axis maximum; <c>''</c> when none does, and null when there
    /// is no such axis.
    /// </summary>
    public string? Holding(string axis, double value)
    {
        if (!_axes.TryGetValue(axis, out var declared)) return null;
        foreach (var range in declared.Ranges)
        {
            if (value >= range.From && (value < range.To || (value == range.To && declared.Max is { } max && range.To == max))) return range.Id;
        }
        return "";
    }

    private static double? Bound(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object && owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
}
