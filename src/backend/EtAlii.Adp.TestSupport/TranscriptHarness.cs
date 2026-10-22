using System.Text.Json;

namespace EtAlii.Adp.TestSupport;

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
/// shipped copy costs a line while it stays identical and shows in full the moment it drifts.
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

            // The toolbox is static: it does not read the document, so it is written once.
            json.WriteStartArray("toolbox");
            foreach (var item in adapter.Toolbox)
            {
                json.WriteStringValue($"{item.Id} | {item.Label} | {item.Icon} | drops {item.DropActionId} | {item.Description}");
            }

            json.WriteEndArray();

            json.WriteStartArray("documents");
            var recorded = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var document in adapter.Corpus())
            {
                await WriteDocumentAsync(json, adapter, document, recorded, cancellationToken);
            }

            json.WriteEndArray();
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
        var sha = TranscriptText.Sha256(bytes);

        json.WriteStartObject();
        json.WriteString("document", document.Name);
        json.WriteString("sha256", sha);
        if (recorded.TryGetValue(sha, out var first))
        {
            json.WriteString("sameAs", first);
            json.WriteEndObject();
            return;
        }

        recorded[sha] = document.Name;

        using (var folder = TestFolder.Create(adapter.GetType().Name))
        {
            await adapter.WriteDocumentAsync(json, document, bytes, folder.Path, cancellationToken);
        }

        json.WriteEndObject();
    }
}
