using EtAlii.Adp.Documents;
using Serilog;
using YamlDotNet.Core;

namespace EtAlii.Adp.Diagram.Timeline;

/// <inheritdoc cref="ITimelineDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a timeline reads (code-structure FR-029).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save
/// (R2.3) and telling the sessions are all <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s.
/// What stays here is what is this module's: what a body's lines mean, and why a body that is not
/// YAML cannot be read.
/// </para>
/// <para>
/// <b>A file that is not YAML is an ordinary state for a file somebody is editing</b>, so it is held
/// as unreadable with the parser's sentence and the line it stopped at, rather than thrown out of
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
    /// <remarks>Empty for a body that does not parse: what it says is unknown, and <see cref="UnreadableOf"/> says why.</remarks>
    protected override TimelineModel Rebuild(LineDocument body)
    {
        try
        {
            return TimelineParser.Parse(body);
        }
        catch (YamlException)
        {
            return TimelineModel.Empty;
        }
    }

    /// <inheritdoc />
    protected override string UnreadableOf(LineDocument body)
    {
        try
        {
            _ = TimelineParser.Parse(body);
            return "";
        }
        catch (YamlException exception)
        {
            var line = Math.Max((int)exception.Start.Line, 1);
            _logger.Debug(exception, "A timeline does not parse at line {Line}", line);
            return $"it does not parse at line {line}. {exception.Message}";
        }
    }
}
