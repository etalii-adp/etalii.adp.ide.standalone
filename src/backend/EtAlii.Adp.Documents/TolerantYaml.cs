using Serilog;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Documents;

/// <summary>
/// One tolerant read of one YAML file: the parsed document with its line marks intact, or the
/// parser's own account of why not (code-structure FR-037). Two modules each wrote this as their
/// own copy, word for word.
/// </summary>
/// <remarks>
/// <para>
/// For a diagram that is a folder of files, where a single unreadable one has to cost that file's
/// detail and nothing else - not the diagram, not the session, not the panel. Nothing here throws
/// for bad input.
/// </para>
/// <para>
/// The representation model rather than deserialization into types: the schemas such a folder
/// holds are loose (a value that is a string or a mapping, a string or a list), and a
/// strongly-typed binding would reject documents their own tools accept. Walking nodes also keeps
/// <see cref="YamlNode.Start"/>, which is the line a problem points at.
/// </para>
/// </remarks>
public static class TolerantYaml
{
    private static readonly ILogger _logger = Log.ForContext(typeof(TolerantYaml));

    /// <summary>
    /// Reads <paramref name="fullPath"/>, naming it <paramref name="relativePath"/> in anything
    /// it has to report.
    /// </summary>
    public static TolerantYamlResult Read(string fullPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(relativePath);

        string text;
        try
        {
            // Read-only and shared, through the one place that owns that decision: reading a
            // folder must never contend with the editor the user is fixing it in, and a
            // temp-then-move publish must not fail while this read is in flight.
            text = SharedDocumentReader.ReadAllText(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Could not read {Path}", relativePath);
            return new TolerantYamlUnreadable(new TolerantYamlFailure(relativePath, exception.Message, 0));
        }

        return Read(new StringReader(text), relativePath);
    }

    /// <summary>
    /// Parses <paramref name="text"/>, naming it <paramref name="relativePath"/> in anything it
    /// has to report: the read for a caller that holds the file's text rather than its path, as a
    /// persistence plugin holds the bytes it is handed (<c>PluginFileYaml</c>).
    /// </summary>
    public static TolerantYamlResult Read(TextReader text, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(relativePath);

        try
        {
            var yaml = new YamlStream();
            yaml.Load(text);
            // An empty file loads to no documents at all. Valid YAML, nothing to recognise.
            return new TolerantYamlDocument(yaml.Documents.Count == 0 ? null : yaml.Documents[0].RootNode);
        }
        catch (YamlException exception)
        {
            // The parser's own words and its own line, never a paraphrase: someone fixing YAML
            // is better served by what the parser actually objected to.
            return new TolerantYamlUnreadable(
                new TolerantYamlFailure(relativePath, Reason(exception), (uint)Math.Max(exception.Start.Line, 0)));
        }
        catch (Exception exception)
        {
            // A parser that fails in a way its own exception type does not cover still may not
            // take the diagram down with it.
            _logger.Warning(exception, "Reading {Path} failed in an unexpected way", relativePath);
            return new TolerantYamlUnreadable(new TolerantYamlFailure(relativePath, exception.Message, 0));
        }
    }

    /// <summary>
    /// The message without the position prefix YamlDotNet puts in front of it - the line travels
    /// as a number, so repeating it in the text would show the reader "(Line: 7, Col: 3): ..."
    /// beside a row already labelled <c>:7</c>.
    /// </summary>
    private static string Reason(YamlException exception)
    {
        var message = exception.Message;
        var end = message.IndexOf("): ", StringComparison.Ordinal);
        return end >= 0 && message.StartsWith('(') ? message[(end + 3)..] : message;
    }
}
