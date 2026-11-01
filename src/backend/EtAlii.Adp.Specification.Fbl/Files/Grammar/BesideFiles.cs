using System.Reflection;
using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Specification.Fbl.Grammar;

/// <summary>
/// The files beside an FBL document that a binding of the <c>grammar</c> family reads (FBL 0.9 §4.9): its grammar file and
/// the grammar files that one imports, by their names. They are the files of the document's folder, or, for a document a
/// module embeds, the resources it embeds beside it under those names, so that a bundled binding names its grammar file
/// as the same binding read from a folder does.
/// </summary>
internal abstract class BesideFiles
{
    /// <summary>Whether a file of that name, such as <c>structurizr.lark</c>, is beside the document.</summary>
    public abstract bool Exists(string name);

    /// <summary>The text of the file of that name, without its BOM.</summary>
    public abstract string Read(string name);

    /// <summary>The names of the grammar files beside the document, without <c>.lark</c>.</summary>
    public abstract IReadOnlySet<string> Grammars { get; }

    /// <summary>A path the grammar is read as being at, for the parser's own messages; null when there is none.</summary>
    public abstract string? SourcePath { get; }

    /// <summary>The files of <paramref name="folder"/>; null without one.</summary>
    public static BesideFiles? InFolder(string? folder) => folder is null ? null : new Folder(folder);

    /// <summary>The resources <paramref name="assembly"/> embeds, by their logical names.</summary>
    public static BesideFiles Embedded(Assembly assembly) => new Resources(assembly);

    /// <summary>A text without its UTF-8 BOM.</summary>
    protected static string Decode(byte[] bytes)
    {
        var bom = bytes.AsSpan().StartsWith(System.Text.Encoding.UTF8.Preamble) ? 3 : 0;
        return System.Text.Encoding.UTF8.GetString(bytes, bom, bytes.Length - bom);
    }

    private sealed class Folder(string folder) : BesideFiles
    {
        public override bool Exists(string name) => File.Exists(Path.Combine(folder, name));

        // Read at the sharing the loader reads an FBL document with.
        public override string Read(string name) => Decode(FblDocumentLoader.ReadShared(Path.Combine(folder, name)));

        public override IReadOnlySet<string> Grammars =>
            Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*.lark").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

        public override string SourcePath => Path.Combine(folder, "binding.lark");
    }

    private sealed class Resources(Assembly assembly) : BesideFiles
    {
        public override bool Exists(string name) => assembly.GetManifestResourceNames().Contains(name, StringComparer.Ordinal);

        public override string Read(string name)
        {
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException($"{assembly.GetName().Name} embeds no resource {name}.", name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return Decode(buffer.ToArray());
        }

        public override IReadOnlySet<string> Grammars =>
            assembly.GetManifestResourceNames()
                .Where(name => name.EndsWith(".lark", StringComparison.Ordinal) && name.IndexOfAny(['/', '\\']) < 0)
                .Select(name => name[..^".lark".Length])
                .ToHashSet(StringComparer.Ordinal);

        public override string? SourcePath => null;
    }
}
