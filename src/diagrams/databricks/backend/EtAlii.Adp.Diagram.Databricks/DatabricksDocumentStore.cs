using EtAlii.Adp.Documents;
using Serilog;
using YamlDotNet.Core;

namespace EtAlii.Adp.Diagram.Databricks;

/// <inheritdoc cref="IDatabricksDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store (code-structure FR-029).</b> Loading, the retries, keeping the last good
/// document through a reload that cannot read, clearing it only on the watcher's delete, ignoring
/// this store's own save and telling the sessions are <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s.
/// What stays here is how a body's text becomes the family's three readings.
/// </para>
/// <para>
/// <b>A file that is not YAML is an ordinary state for a file somebody is editing</b>, so it is held
/// as unreadable with the parser's sentence, rather than thrown out of the store, and nothing is
/// written to it until it parses (Requirement 2.3).
/// </para>
/// </remarks>
public sealed class DatabricksDocumentStore : LifecycleDocumentStore<LineDocument, DatabricksModel>, IDatabricksDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<DatabricksDocumentStore>();

    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;

    /// <inheritdoc />
    /// <remarks>Empty for a body that does not parse: what it says is unknown, and <see cref="UnreadableOf"/> says why.</remarks>
    protected override DatabricksModel Rebuild(LineDocument body)
    {
        try
        {
            return DatabricksModel.Read(body);
        }
        catch (YamlException)
        {
            return DatabricksModel.Empty;
        }
    }

    /// <inheritdoc />
    protected override string UnreadableOf(LineDocument body)
    {
        try
        {
            _ = DatabricksModel.Read(body);
            return "";
        }
        catch (YamlException exception)
        {
            _logger.Debug(exception, "A Databricks file does not parse at line {Line}", exception.Start.Line);
            return exception.Message;
        }
    }
}
