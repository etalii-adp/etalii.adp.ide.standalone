using System.Text.Json;
using EtAlii.Adp.Context;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The two things DISL calls chrome and this host's wire carries as elements: the header band, which states the query's
/// form and modifiers, and the truncation banner, which reports the cut. Their ids and element type strings are the
/// definition's <c>x-sparql</c> block's.
/// </summary>
/// <remarks>
/// <para>
/// A stand-in for a language gap (finding A12): DISL draws the header as <c>canvas.chrome.header</c> and the banner as a
/// budget's <c>notice</c>, and gives a host no way to name either on its wire. The class goes when DISL does.
/// </para>
/// <para>
/// <b>The header's rows are the query's own</b>, the definition's form for the diagram; the banner has none. <b>The two
/// move refusals are this class's sentences</b>, because chrome has no gesture in DISL and so no place for a refusal: the
/// eighth gap of the definition's companion.
/// </para>
/// </remarks>
internal static class SparqlChrome
{
    private static readonly Lazy<(string HeaderId, string HeaderType, string TruncationId, string TruncationType)> Wire = new(Read);

    /// <summary>The header band, always, and the truncation banner when the budget cut what <paramref name="drawing"/> draws.</summary>
    public static IReadOnlyList<DiagramElement> Elements(SparqlProjectionResult drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        (string headerId, string headerType, string truncationId, string truncationType) = Wire.Value;
        var elements = new List<DiagramElement>
        {
            SparqlElementMapper.Pack(headerId, default, headerType, new SparqlHeaderPayload
            {
                Form = drawing.HeaderForm,
                ModifierRows = { drawing.HeaderRows },
            }),
        };
        if (drawing.Truncated)
        {
            elements.Add(SparqlElementMapper.Pack(truncationId, default, truncationType, new SparqlTruncationPayload
            {
                Shown = drawing.Shown,
                Total = drawing.Total,
            }));
        }

        return elements;
    }

    /// <summary>Whether <paramref name="wireId"/> names the header band or the truncation banner.</summary>
    public static bool IsChrome(string wireId)
    {
        (string headerId, _, string truncationId, _) = Wire.Value;
        return wireId == headerId || wireId == truncationId;
    }

    /// <summary>The rows of the header band, the query's own; none for the banner or any other id.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(SparqlDocument document, string wireId)
    {
        ArgumentNullException.ThrowIfNull(document);
        return wireId == Wire.Value.HeaderId ? SparqlDefinition.DiagramRows(document) : [];
    }

    /// <summary>Why the header band or the banner cannot be moved; null for any other id.</summary>
    public static string? MoveRefusal(string wireId)
    {
        (string headerId, _, string truncationId, _) = Wire.Value;
        if (wireId == headerId) return "The header band states the query's form and modifiers, and stays at the top of the diagram.";
        if (wireId == truncationId) return "That banner reports the truncation and is not something this diagram can move.";
        return null;
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
