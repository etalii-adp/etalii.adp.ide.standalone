using System.Text.Json;
using EtAlii.Adp.Tests.Support;

namespace EtAlii.Adp.Diagram.Tests.Support;

/// <summary>
/// Writes a module's parity transcript: what today's code answers for every document of its corpus,
/// as the bytes the checked-in file must hold. What the module records is its
/// <see cref="TranscriptAdapter"/>'s; the frame around it is this.
/// </summary>
/// <remarks>
/// <para>
/// One harness for every module's transcript (code-structure FR-093). The frame lived in each
/// generator, a copy of the same 60 lines that differed in nothing but names.
/// </para>
/// <para>
/// <b>A document whose bytes equal one recorded earlier</b> is written as <c>sameAs</c> that one, so a
/// shipped copy costs a line while it stays identical and shows in full the moment it drifts. What
/// counts as equal is the adapter's <see cref="TranscriptAdapter.WriteIdentityAsync"/>: the bytes,
/// unless the module says more makes the same reading.
/// </para>
/// </remarks>
public static class TranscriptHarness
{
    /// <summary>Set to <c>1</c>, the transcript tests rewrite the checked-in file before comparing.</summary>
    public const string RegenerateVariable = "ADP_WRITE_PARITY_TRANSCRIPTS";

    /// <summary>The whole transcript, as the bytes the checked-in file must hold.</summary>
    public static async Task<byte[]> GenerateAsync(TranscriptAdapter adapter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        using var stream = new MemoryStream();
        await using (var json = new Utf8JsonWriter(stream, TranscriptText.WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("module", adapter.Origin);
            json.WriteString("generator", adapter.Generator);
            json.WriteString("regenerate", $"{RegenerateVariable}=1 dotnet test --project <this test project> (then review the diff)");

            // What does not read a document, the toolbox above all, is written once.
            await adapter.WriteBeforeDocumentsAsync(json, cancellationToken);

            json.WriteStartArray(adapter.DocumentsName);
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var document in adapter.Corpus())
            {
                await WriteDocumentAsync(json, adapter, document, recorded, cancellationToken);
            }

            json.WriteEndArray();
            await adapter.WriteAfterDocumentsAsync(json, cancellationToken);
            json.WriteEndObject();
            await json.FlushAsync(cancellationToken);
        }

        stream.Write("\r\n"u8);
        return stream.ToArray();
    }

    private static async Task WriteDocumentAsync(
        Utf8JsonWriter json,
        TranscriptAdapter adapter,
        TranscriptDocument document,
        Dictionary<string, string> recorded,
        CancellationToken cancellationToken)
    {
        var bytes = await document.Read(cancellationToken);

        json.WriteStartObject();
        json.WriteString(adapter.DocumentName, document.Name);
        var key = await adapter.WriteIdentityAsync(json, document, bytes, cancellationToken);
        if (key is not null)
        {
            if (recorded.TryGetValue(key, out var first))
            {
                json.WriteString("sameAs", first);
                json.WriteEndObject();
                return;
            }

            recorded[key] = document.Name;
        }

        using (var folder = TestFolder.Create(adapter.GetType().Name))
        {
            await adapter.WriteDocumentAsync(json, document, bytes, folder.Path, cancellationToken);
        }

        json.WriteEndObject();
    }
}
