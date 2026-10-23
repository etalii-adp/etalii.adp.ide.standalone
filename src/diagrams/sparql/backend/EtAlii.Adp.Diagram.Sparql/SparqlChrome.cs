using System.Text.Json;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The two things DISL calls chrome and this host's wire carries as elements: the header band, which states the query's
/// form and modifiers, and the truncation banner, which reports the cut. Their ids and element type strings are the
/// definition's <c>x-sparql</c> block's.
/// </summary>
/// <remarks>
/// A stand-in for a language gap (finding A12): DISL draws the header as <c>canvas.chrome.header</c> and the banner as a
/// budget's <c>notice</c>, and gives a host no way to name either on its wire. The class goes when DISL does.
/// </remarks>
internal static class SparqlChrome
{
    private static readonly Lazy<(string HeaderId, string HeaderType, string TruncationId, string TruncationType)> Wire = new(Read);

    /// <summary>The header band, always, and the truncation banner when the budget cut what <paramref name="drawing"/> draws.</summary>
    public static IReadOnlyList<DiagramElement> Elements(SparqlProjectionResult drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        var wire = Wire.Value;
        var elements = new List<DiagramElement>
        {
            SparqlElementMapper.Pack(wire.HeaderId, default, wire.HeaderType, new SparqlHeaderPayload
            {
                Form = drawing.HeaderForm,
                ModifierRows = { drawing.HeaderRows },
            }),
        };
        if (drawing.Truncated)
        {
            elements.Add(SparqlElementMapper.Pack(wire.TruncationId, default, wire.TruncationType, new SparqlTruncationPayload
            {
                Shown = drawing.Shown,
                Total = drawing.Total,
            }));
        }

        return elements;
    }

    private static (string, string, string, string) Read()
    {
        var block = SparqlDefinition.Specification.Json.GetProperty("x-sparql");
        var elements = block.GetProperty("elements");
        var types = block.GetProperty("types");
        return (Text(elements, "header"), Text(types, "diagram"), Text(elements, "truncation"), Text(types, "budget:drawn"));
    }

    private static string Text(JsonElement block, string key) =>
        block.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new InvalidOperationException($"The definition's x-sparql block names no '{key}'.");
}
