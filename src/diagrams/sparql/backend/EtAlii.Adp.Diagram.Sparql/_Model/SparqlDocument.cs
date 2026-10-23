using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// One query as an entry of the store holds it: its body, read through the module's binding, and - built once, when
/// first asked for - what the DISL model of that reading draws, so that every surface of one reading reads one model.
/// </summary>
public sealed class SparqlDocument
{
    private readonly Lazy<SparqlProjectionResult> _drawing;

    /// <summary>A query read as <paramref name="body"/>.</summary>
    /// <param name="body">The body; an empty, unreadable one for a file that is missing or could not be read.</param>
    public SparqlDocument(SparqlBody body)
    {
        Body = body ?? throw new ArgumentNullException(nameof(body));
        _drawing = new Lazy<SparqlProjectionResult>(() => SparqlDefinition.Drawing(this));
    }

    /// <summary>The document of a file that is missing or could not be read: an empty body, which reads as no query.</summary>
    public static SparqlDocument Empty { get; } = new(SparqlBody.Parse(""));

    /// <summary>The body, as the plugin read it.</summary>
    public SparqlBody Body { get; }

    /// <summary>The model the shared builder built from what the plugin read.</summary>
    internal DislModel Disl => Body.Disl;

    /// <summary>What the model draws (<see cref="SparqlDefinition.Drawing"/>).</summary>
    public SparqlProjectionResult Drawing => _drawing.Value;
}
