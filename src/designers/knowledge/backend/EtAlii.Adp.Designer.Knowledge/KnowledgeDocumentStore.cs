using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>What reading a knowledge file gave: its body, or why it could not be read from disk at all.</summary>
/// <param name="Body">The body as read, or null when the file could not be opened.</param>
/// <param name="Refusal">Why the file could not be opened, or empty.</param>
internal sealed record KnowledgeRead(KnowledgeBody? Body, string Refusal);

/// <summary>
/// Reads a knowledge file from disk. <b>Opening writes nothing</b>: a file is read with the
/// shared read every tool in this host uses, its bytes are kept exactly as they were, and an
/// unchanged file is never rewritten - not to add an id, not to tidy it.
/// </summary>
/// <remarks>
/// The read shares the file with whoever is writing it (<see cref="SharedDocumentReader"/>), so a
/// save through the central writer is never refused because a table happened to be opening. What
/// that costs is a read that can land mid-replace and find the name absent; the session retries
/// such a read rather than treating it as the file being gone.
/// </remarks>
internal static class KnowledgeDocumentStore
{
    /// <summary>Reads the body at <paramref name="bodyPath"/>. Never throws for what a file or a disk can do.</summary>
    public static KnowledgeRead Read(string bodyPath)
    {
        ArgumentNullException.ThrowIfNull(bodyPath);

        byte[] bytes;
        try
        {
            bytes = SharedDocumentReader.ReadAllBytes(bodyPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new KnowledgeRead(null, exception is FileNotFoundException or DirectoryNotFoundException ? "The file no longer exists." : $"The file cannot be read: {exception.Message}");
        }

        return new KnowledgeRead(KnowledgeBody.Read(bytes, bodyPath), "");
    }
}
