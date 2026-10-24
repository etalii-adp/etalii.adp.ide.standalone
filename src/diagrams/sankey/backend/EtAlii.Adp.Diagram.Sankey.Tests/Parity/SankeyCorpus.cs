using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests.Parity;

/// <summary>The parity transcript's corpus, each document read as the store reads it, and the targets it records menus and rows for.</summary>
internal static class SankeyCorpus
{
    /// <summary>Every document of the corpus, by its <c>src/</c>-relative name.</summary>
    public static TheoryData<string> Names() => [.. new SankeyTranscript().Corpus().Select(document => document.Name)];

    /// <summary>The document <paramref name="name"/>, read as the store reads it.</summary>
    public static async Task<DocumentEntry<LineDocument, SankeyModel>> ReadAsync(string name, CancellationToken cancellationToken)
    {
        var document = new SankeyTranscript().Corpus().Single(candidate => candidate.Name == name);
        var body = LineDocument.Parse(TranscriptText.Decode(await document.Read(cancellationToken)));
        return new DocumentEntry<LineDocument, SankeyModel>(body, SankeyParser.Outcome(body));
    }

    /// <summary>The targets the transcript records: every node and flow id, the empty id, a placement, a connect gesture and an unknown id.</summary>
    public static IReadOnlyList<string> Targets(SankeyModel model)
    {
        var nodeIds = model.Nodes.Select(node => node.Id).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var relation = nodeIds.Count >= 2 ? GestureIds.Relation(nodeIds[0], nodeIds[1]) : GestureIds.Relation("a", "b");
        return [.. nodeIds.Concat(model.Flows.Select(flow => flow.Id)).Distinct(StringComparer.Ordinal), "", GestureIds.Placement(330, 40), relation, "parity:unknown"];
    }
}
