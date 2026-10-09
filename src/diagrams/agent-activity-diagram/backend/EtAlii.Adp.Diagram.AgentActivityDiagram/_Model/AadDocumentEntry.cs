using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>What the store keeps for one activity file: the open body and the model read off it.</summary>
/// <param name="Unreadable">
/// Empty for a file that was read, and for one that is not there yet. Otherwise the reason the file
/// could not be read at all - and then nothing is written to it until it can be (Requirement 8.5).
/// </param>
public sealed record AadDocumentEntry(AadBody Document, AadModel Model, string Unreadable = "")
{
    /// <summary>Whether the file may be edited: it was read, or it does not exist yet.</summary>
    public bool IsUsable => Unreadable.Length == 0;

    /// <summary>Reads <paramref name="text"/> as an activity file.</summary>
    public static AadDocumentEntry Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var document = AadBody.Parse(text);
        var entry = new AadDocumentEntry(document, AadModel.Read(document));

        // A file the binding could not read at all - not YAML, or not an activity file - is held as
        // unreadable with the reader's own sentence, which names the format's error and its place.
        return document.Disl.IsUnreadable
            ? entry with { Unreadable = document.Model.Findings.FirstOrDefault()?.Message ?? "the file could not be read as an activity file" }
            : entry;
    }

    /// <summary>The entry for a body that is missing or could not be opened.</summary>
    public static AadDocumentEntry Unavailable(DocumentUnavailability unavailability, string reason) =>
        unavailability == DocumentUnavailability.Missing
            ? Read("")
            : Read("") with { Unreadable = reason.Length > 0 ? reason : "the body could not be read" };
}
