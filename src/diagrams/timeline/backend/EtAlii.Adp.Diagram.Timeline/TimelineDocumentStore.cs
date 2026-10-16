using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using Serilog;

namespace EtAlii.Adp.Diagram.Timeline;

/// <inheritdoc cref="ITimelineDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a timeline reads (code-structure FR-029).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save
/// (R2.3) and telling the sessions are all <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s.
/// What stays here is what is this module's: what a body's lines mean, read as a
/// <see cref="ParseOutcome{TModel}"/> that says why a body that is not YAML cannot be read.
/// </para>
/// <para>
/// <b>A file that is not YAML is an ordinary state for a file somebody is editing</b>, so it is held
/// as unreadable with the YAML reader's sentence and the line it stopped at, rather than thrown out of
/// the store, and nothing is written to it until it parses (Requirement 2.4).
/// </para>
/// </remarks>
public sealed class TimelineDocumentStore : LifecycleDocumentStore<LineDocument, TimelineModel>, ITimelineDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<TimelineDocumentStore>();

    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;

    /// <inheritdoc />
    protected override TimelineModel Rebuild(LineDocument body) => Parse(body).Model;

    /// <inheritdoc />
    /// <remarks>
    /// The model is empty for a body that is not YAML (<see cref="TimelineYamlSyntax"/>): what it says
    /// is unknown. Such a body, and one the binding cannot read, is unreadable with the refusal's
    /// sentence, which is also its one problem.
    /// </remarks>
    protected override ParseOutcome<TimelineModel> Parse(LineDocument body)
    {
        var model = TimelineYamlSyntax.Check(body.Text) is null ? TimelineReading.Of(body.Text) : TimelineModel.Empty;
        if (Refusal(body.Text) is not { } refusal)
        {
            return new(model, []);
        }

        _logger.Debug("A timeline does not parse at line {Line}: {Reason}", refusal.Line, refusal.Message);
        return new(model, [new ParseProblem(refusal.Line - 1, refusal.Message)], $"it does not parse at line {refusal.Line}. {refusal.Message}");
    }

    /// <summary>
    /// Why <paramref name="text"/> cannot be read, and at which 1-based line: YamlDotNet's refusal first,
    /// else the library's unreadable body; null for a body that reads.
    /// </summary>
    private static (string Message, int Line)? Refusal(string text)
    {
        if (TimelineYamlSyntax.Check(text) is { } refusal)
        {
            return refusal;
        }

        var model = TimelineBody.Parse(text).Model;
        if (!model.Unreadable)
        {
            return null;
        }

        var unreadable = model.Findings.FirstOrDefault(finding => finding.Code == FindingCodes.Unparseable);
        return (unreadable?.Message ?? "the binding cannot read it.", Math.Max(unreadable?.Location?.Line ?? 1, 1));
    }
}
