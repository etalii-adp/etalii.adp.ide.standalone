using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// One tolerant parse of one YAML file a persistence plugin is handed: the parsed document with its line
/// marks, or YamlDotNet's own account of why not. <see cref="TolerantYaml"/>'s parse, over bytes rather than a
/// path, because a plugin opens no file (FBL §11.2). Ansible's and Helm's folder plugins each wrote this as
/// their own copy, word for word (code-structure T294).
/// </summary>
/// <remarks>
/// <para>
/// <b>Decoded as the shared reader decodes</b>: UTF-8 unless a byte order mark says otherwise, which is what
/// <see cref="SharedDocumentReader.ReadAllText"/> does for <see cref="TolerantYaml"/>, so a file reads to the
/// same text, the same nodes and the same lines either way.
/// </para>
/// <para>
/// <b>The parser's words and its line</b>, as <see cref="TolerantYaml"/> gives them. A file the host did not
/// read because it is larger than it reads (<see cref="PluginFile.IsOversized"/>) does not parse either, and
/// says so: it costs that file's detail and nothing else.
/// </para>
/// </remarks>
public static class PluginFileYaml
{
    /// <summary>Parses <paramref name="file"/>, naming it <paramref name="relativePath"/> in anything it has to report.</summary>
    public static TolerantYamlResult Read(PluginFile file, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(relativePath);

        if (file.IsOversized)
        {
            return new TolerantYamlUnreadable(new TolerantYamlFailure(relativePath, "The file is larger than this host reads, so it is not read.", 0));
        }

        using var text = Text(file);
        return TolerantYaml.Read(text, relativePath);
    }

    /// <summary>The file's text, decoded as <see cref="SharedDocumentReader"/> decodes a file on disk.</summary>
    public static StreamReader Text(PluginFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return new StreamReader(new MemoryStream(file.Bytes, writable: false));
    }
}
