using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests.Parity;

/// <summary>The parity transcript's corpus, read as the module reads it, and the targets the transcript records for a document.</summary>
internal static class SupplyChainCorpus
{
    private static readonly SupplyChainTranscript _transcript = new();

    /// <summary>The documents, by the name the transcript records them under.</summary>
    public static TheoryData<string> Names() => [.. _transcript.Corpus().Select(document => document.Name)];

    /// <summary>The document <paramref name="name"/>, parsed.</summary>
    public static async Task<SupplyChainModel> ReadAsync(string name, CancellationToken cancellationToken) =>
        SupplyChainReading.Parse(await TextAsync(name, cancellationToken));

    /// <summary>The text of the document <paramref name="name"/>, decoded as the module decodes it.</summary>
    public static async Task<string> TextAsync(string name, CancellationToken cancellationToken)
    {
        var bytes = await _transcript.Corpus().Single(document => document.Name == name).Read(cancellationToken);
        return TranscriptText.Decode(bytes);
    }

    /// <summary>The targets the transcript records menus and rows for.</summary>
    public static IReadOnlyList<string> Targets(SupplyChainModel model) => SupplyChainTranscript.Targets(model);
}
