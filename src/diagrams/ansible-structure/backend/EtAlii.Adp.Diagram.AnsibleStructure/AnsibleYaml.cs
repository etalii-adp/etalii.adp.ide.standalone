using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Common;
using Serilog;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One tolerant read of one YAML file: the parsed document with its line marks intact, or the
/// parser's own account of why not.
/// </summary>
/// <remarks>
/// <para>
/// The narrowest point of Requirement 1.2. A diagram of this type is a folder of files, so a
/// single unreadable one has to cost that file's detail and nothing else - not the diagram, not
/// the session, not the panel. Nothing here throws for bad input.
/// </para>
/// <para>
/// The representation model rather than deserialization into types: Ansible's schema is loose
/// (a <c>roles:</c> entry is a string or a mapping, a <c>when:</c> is a string or a list), and a
/// strongly-typed binding would reject documents Ansible itself accepts. Walking nodes also
/// keeps <see cref="YamlNode.Start"/>, which is the line a problem points at.
/// </para>
/// </remarks>
public static class AnsibleYaml
{
    private static readonly ILogger _logger = Log.ForContext(typeof(AnsibleYaml));

    /// <summary>
    /// Reads <paramref name="fullPath"/>, naming it <paramref name="relativePath"/> in anything
    /// it has to report.
    /// </summary>
    public static AnsibleYamlResult Read(string fullPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(relativePath);

        string text;
        try
        {
            // Read-only and shared, through the one place that owns that decision:
            // reading a project must never contend with the editor the user is fixing it in,
            // and this module has no business locking anything. The central reader also
            // shares Delete, which this hand-rolled open did not - without it a
            // temp-then-move publish fails while this read is in flight.
            text = SharedDocumentReader.ReadAllText(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Could not read {Path}", relativePath);
            return new AnsibleYamlUnreadable(new AnsibleYamlFailure(relativePath, exception.Message, 0));
        }

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(text));
            // An empty file loads to no documents at all. Valid YAML, nothing to recognise.
            return new AnsibleYamlDocument(yaml.Documents.Count == 0 ? null : yaml.Documents[0].RootNode);
        }
        catch (YamlException exception)
        {
            // The parser's own words and its own line, never a paraphrase: someone fixing YAML
            // is better served by what the parser actually objected to.
            return new AnsibleYamlUnreadable(
                new AnsibleYamlFailure(relativePath, Reason(exception), (uint)Math.Max(exception.Start.Line, 0)));
        }
        catch (Exception exception)
        {
            // A parser that fails in a way its own exception type does not cover still may not
            // take the diagram down with it.
            _logger.Warning(exception, "Reading {Path} failed in an unexpected way", relativePath);
            return new AnsibleYamlUnreadable(new AnsibleYamlFailure(relativePath, exception.Message, 0));
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
