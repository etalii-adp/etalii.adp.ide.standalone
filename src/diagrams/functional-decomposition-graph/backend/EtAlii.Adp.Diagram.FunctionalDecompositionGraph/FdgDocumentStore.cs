using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The <c>.fdg</c> document store: the shared store, told how a body reads through its binding and what
/// it means.
/// </summary>
/// <remarks>
/// <para>
/// <b>A body is read through the module's binding</b> (<see cref="FdgBody"/>), and its model is the DISL
/// model built from that reading, which the menus, the rows and the payloads are derived from.
/// </para>
/// <para>
/// <b>A body that is not YAML, or whose mapping repeats a key, is an ordinary state for a file somebody is
/// editing</b>, so it is held as unreadable with the reader's own sentence, rather than thrown out of the
/// store: it opens empty, and nothing is written to it until it reads (Q3 of
/// functional-decomposition-graph-disl-fbl).
/// </para>
/// </remarks>
public sealed class FdgDocumentStore(IFileWriter writer) : LifecycleDocumentStore<FdgBody, DislModel>(writer), IFdgDocumentStore
{
    /// <inheritdoc />
    protected override FdgBody Read(string text) => FdgBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(FdgBody body) => body.Text;

    /// <inheritdoc />
    protected override ParseOutcome<DislModel> Parse(FdgBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.IsUnreadable) return new ParseOutcome<DislModel>(body.Disl, []);

        var reason = FdgReaderSentences.All(body)[0];
        var problem = new ParseProblem(reason.Line, $"{FdgReaderSentences.NotYaml}{reason.Message}");
        return new ParseOutcome<DislModel>(body.Disl, [problem], problem.Message);
    }
}
