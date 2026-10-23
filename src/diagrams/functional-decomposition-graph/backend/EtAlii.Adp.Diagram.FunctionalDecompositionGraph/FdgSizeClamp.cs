using System.Text.Json;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Raises a committed width below its minimum, or a Comment's height below its minimum, to that minimum
/// before it is written: what the definition's <c>outOfRange: { "typed": "clamp" }</c> on <c>width</c> and
/// <c>height</c> says (DISL 4.3), with the <c>min</c> each declares.
/// </summary>
/// <remarks>
/// <b>Stands in for the runtime</b>, not for the language (gap 9 of the companion, answered by DISL's
/// <c>outOfRange: clamp</c>): <c>EtAlii.Adp.Specification.Disl</c> does not clamp a value yet, so the module does,
/// reading the bound from the definition rather than stating it. It is deleted when the runtime clamps.
/// </remarks>
internal static class FdgSizeClamp
{
    /// <summary><paramref name="value"/> for <paramref name="attribute"/> of <paramref name="element"/>, raised to the attribute's <c>min</c> when it clamps a typed value below it.</summary>
    public static double Of(DislElement element, string attribute, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.Type.Attributes.TryGetValue(attribute, out var declared)
               && declared.Json is { ValueKind: JsonValueKind.Object } json
               && json.TryGetProperty("min", out var min) && min.ValueKind == JsonValueKind.Number
               && json.TryGetProperty("outOfRange", out var outOfRange) && outOfRange.ValueKind == JsonValueKind.Object
               && outOfRange.TryGetProperty("typed", out var typed) && typed.ValueKind == JsonValueKind.String && typed.GetString() == "clamp"
            ? Math.Max(value, min.GetDouble())
            : value;
    }
}
