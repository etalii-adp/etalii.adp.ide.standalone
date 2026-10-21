using EtAlii.Adp.Documents;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The parity transcript's corpus as entries the providers read, and the targets it records menus and
/// rows for, so that a derived answer is compared with the hand-written one on every document and
/// element the transcript holds.
/// </summary>
internal static class FdgCorpus
{
    /// <summary>The corpus, as paths relative to <c>src/</c>, for a theory's data.</summary>
    public static TheoryData<string> Documents() => [.. FdgTranscript.Corpus()];

    /// <summary>The document at <paramref name="relative"/>, read as the store reads it, and the same entry read-only.</summary>
    public static (DocumentEntry<LineDocument, FdgModel> Readable, DocumentEntry<LineDocument, FdgModel> ReadOnly) Entries(string relative)
    {
        var body = LineDocument.Parse(File.ReadAllText(Path.Combine(FdgTranscript.SourceFolder, relative)));
        var readable = new DocumentEntry<LineDocument, FdgModel>(body, FdgParser.Parse(body));
        return (readable, readable with { Unreadable = "parity: read-only" });
    }

    /// <summary>Every element and connection id, the empty id, a placement, a connect gesture and an unknown id, as the transcript lists them.</summary>
    public static IReadOnlyList<string> Targets(FdgModel model)
    {
        var elementIds = model.Elements.Select(element => element.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var relation = elementIds.Count >= 2 ? GestureIds.Relation(elementIds[0], elementIds[1]) : GestureIds.Relation("a", "b");
        return
        [
            .. model.Elements.Select(element => element.Id)
                .Concat(model.Connections.Select(connection => connection.Id))
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.Ordinal),
            "",
            GestureIds.Placement(240, 160),
            relation,
            "parity:unknown",
        ];
    }
}
