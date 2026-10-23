using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One query as an entry of the store holds it: what the parser read, the text it read it from, and - built once,
/// when first asked for - the DISL model of its records and what that model draws, so that every surface of one
/// reading reads one model.
/// </summary>
public sealed class SparqlDocument
{
    private readonly Lazy<DislModel> _disl;

    private readonly Lazy<SparqlProjectionResult> _drawing;

    /// <summary>A query <paramref name="query"/> read from <paramref name="text"/>.</summary>
    /// <param name="query">What the parser read; <see cref="SparqlQueryModel.Empty"/> for a text it could not.</param>
    /// <param name="text">The text it was read from; empty for a file that is missing or could not be read.</param>
    public SparqlDocument(SparqlQueryModel query, string text)
    {
        Query = query ?? throw new ArgumentNullException(nameof(query));
        Text = text ?? throw new ArgumentNullException(nameof(text));
        _disl = new Lazy<DislModel>(() => SparqlDefinition.Model(Query, Text));
        _drawing = new Lazy<SparqlProjectionResult>(() => SparqlDefinition.Drawing(this));
    }

    /// <summary>The document of a file that is missing or could not be read: no query and no text.</summary>
    public static SparqlDocument Empty { get; } = new(SparqlQueryModel.Empty, "");

    /// <summary>What the parser read.</summary>
    public SparqlQueryModel Query { get; }

    /// <summary>The text it was read from.</summary>
    public string Text { get; }

    /// <summary>The model the shared builder built from the records of <see cref="Query"/>.</summary>
    internal DislModel Disl => _disl.Value;

    /// <summary>What the model draws (<see cref="SparqlDefinition.Drawing"/>).</summary>
    public SparqlProjectionResult Drawing => _drawing.Value;
}
