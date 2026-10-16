using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Serilog;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One tolerant parse of one chart-owned YAML file the plugin is handed: the parsed document with its line
/// marks, or YamlDotNet's own account of why not. <see cref="TolerantYaml"/>'s parse, over bytes rather than a
/// path, because the plugin opens no file (FBL §11.2; helm-chart-disl-fbl, Requirement 3.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Decoded as the shared reader decodes</b>: UTF-8 unless a byte order mark says otherwise, which is what
/// <see cref="SharedDocumentReader.ReadAllText"/> does for <see cref="TolerantYaml"/>, so a file reads to the
/// same text, the same nodes and the same lines either way.
/// </para>
/// <para>
/// <b>The parser's words and its line</b>, with the position prefix YamlDotNet puts in front of its message
/// taken off, since the line travels as a number. A file the host did not read because it is larger than it
/// reads (<see cref="PluginFile.IsOversized"/>) does not parse either, and says so: it costs that file's detail
/// and nothing else.
/// </para>
/// </remarks>
internal static class HelmYaml
{
    private static readonly ILogger _logger = Log.ForContext(typeof(HelmYaml));

    /// <summary>Parses <paramref name="file"/>, naming it <paramref name="relativePath"/> in anything it has to report.</summary>
    public static TolerantYamlResult Read(PluginFile file, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(relativePath);

        if (file.IsOversized)
        {
            return new TolerantYamlUnreadable(new TolerantYamlFailure(relativePath, "The file is larger than this host reads, so it is not read.", 0));
        }

        try
        {
            var yaml = new YamlStream();
            using var text = Text(file);
            yaml.Load(text);
            // An empty file loads to no documents at all. Valid YAML, nothing to recognise.
            return new TolerantYamlDocument(yaml.Documents.Count == 0 ? null : yaml.Documents[0].RootNode);
        }
        catch (YamlException exception)
        {
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

    /// <summary>The file's text, decoded as <see cref="SharedDocumentReader"/> decodes a file on disk.</summary>
    public static StreamReader Text(PluginFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new StreamReader(new MemoryStream(file.Bytes, writable: false));
    }

    /// <summary>The message without the position prefix YamlDotNet puts in front of it.</summary>
    private static string Reason(YamlException exception)
    {
        var message = exception.Message;
        var end = message.IndexOf("): ", StringComparison.Ordinal);
        return end >= 0 && message.StartsWith('(') ? message[(end + 3)..] : message;
    }
}
