using System.Text;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// The minimal document a new diagram of each of the family's types starts as - complete enough
/// to open, parse and validate clean, because a skeleton that opens with findings would be a
/// refusal factory.
/// </summary>
/// <remarks>
/// CRLF and a trailing newline: the writers preserve whatever a file already uses (Requirement
/// 2.1), but a file ADP creates has no existing style to preserve, and CRLF is the repository's
/// own house style.
/// </remarks>
public sealed class DatabricksDocumentFactory(DiagramOrigin origin) : IDiagramDocumentFactory
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);

        var key = KeyOf(baseName);
        return Origin.Type switch
        {
            "job" =>
                "resources:\r\n"
                + "  jobs:\r\n"
                + $"    {key}:\r\n"
                + $"      name: {baseName}\r\n"
                + "      tasks:\r\n"
                + "        - task_key: main\r\n"
                + "          notebook_task:\r\n"
                + $"            notebook_path: notebooks/{key}\r\n",
            "pipeline" =>
                "{\r\n"
                + $"  \"name\": \"{key}\",\r\n"
                + "  \"libraries\": [\r\n"
                + $"    {{ \"notebook\": {{ \"path\": \"transformations/{key}\" }} }}\r\n"
                + "  ]\r\n"
                + "}\r\n",
            _ =>
                "bundle:\r\n"
                + $"  name: {key}\r\n"
                + "targets:\r\n"
                + "  dev:\r\n"
                + "    mode: development\r\n"
                + "    default: true\r\n",
        };
    }

    /// <summary>
    /// The base name as a Databricks key: lowercased, with anything a key cannot carry folded
    /// to underscores - so "Nightly Ingest.job" starts life as <c>nightly_ingest_job</c>.
    /// </summary>
    private static string KeyOf(string baseName)
    {
        var builder = new StringBuilder(baseName.Length);
        foreach (var character in baseName)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character)
                ? char.ToLowerInvariant(character)
                : '_');
        }

        var key = builder.ToString().Trim('_');
        return key.Length > 0 ? key : "untitled";
    }
}
