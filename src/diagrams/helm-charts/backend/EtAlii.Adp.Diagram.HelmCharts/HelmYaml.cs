using EtAlii.Adp.Common;
using EtAlii.Adp.Hierarchy;
using Serilog;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// One tolerant read of one of the chart's own YAML files: the parsed document with its line
/// marks intact, or the parser's own account of why not.
/// </summary>
/// <remarks>
/// <para>
/// The narrowest point of Requirement 3.1. A diagram of this type is a folder of files, so a
/// single unreadable one has to cost that file's detail and nothing else - not the diagram, not
/// the session, not the panel. Nothing here throws for bad input.
/// </para>
/// <para>
/// This reader is pointed only at YAML the chart itself owns as YAML - <c>Chart.yaml</c>,
/// values files, <c>Chart.lock</c>, <c>requirements.yaml</c>, files under <c>crds/</c>. It is
/// never pointed at <c>templates/</c>: a Go-templated manifest is not YAML until rendered
/// (Requirement 3.2), and those files go through <c>TemplateScan</c>'s line scanner instead.
/// </para>
/// <para>
/// The representation model rather than deserialization into types: Helm's schema has loose
/// corners (a dependency's <c>tags</c>, free-form <c>annotations</c>, arbitrary values trees),
/// and a strongly-typed binding would reject documents Helm itself accepts. Walking nodes also
/// keeps <see cref="YamlNode.Start"/>, which is the line a problem points at.
/// </para>
/// </remarks>
public static class HelmYaml
{
    private static readonly ILogger _logger = Log.ForContext(typeof(HelmYaml));

    /// <summary>
    /// Reads <paramref name="fullPath"/>, naming it <paramref name="relativePath"/> in anything
    /// it has to report.
    /// </summary>
    public static HelmYamlResult Read(string fullPath, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(relativePath);

        string text;
        try
        {
            // Read-only and shared, through the one place that owns that decision:
            // reading a chart must never contend with the editor the user is fixing it in,
            // and this module has no business locking anything. The central reader also
            // shares Delete, which this hand-rolled open did not - without it a
            // temp-then-move publish fails while this read is in flight.
            text = SharedDocumentReader.ReadAllText(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Could not read {Path}", relativePath);
            return new HelmYamlUnreadable(new HelmYamlFailure(relativePath, exception.Message, 0));
        }

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(text));
            // An empty file loads to no documents at all. Valid YAML, nothing to recognise.
            return new HelmYamlDocument(yaml.Documents.Count == 0 ? null : yaml.Documents[0].RootNode);
        }
        catch (YamlException exception)
        {
            // The parser's own words and its own line, never a paraphrase: someone fixing YAML
            // is better served by what the parser actually objected to.
            return new HelmYamlUnreadable(
                new HelmYamlFailure(relativePath, Reason(exception), (uint)Math.Max(exception.Start.Line, 0)));
        }
        catch (Exception exception)
        {
            // A parser that fails in a way its own exception type does not cover still may not
            // take the diagram down with it.
            _logger.Warning(exception, "Reading {Path} failed in an unexpected way", relativePath);
            return new HelmYamlUnreadable(new HelmYamlFailure(relativePath, exception.Message, 0));
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
