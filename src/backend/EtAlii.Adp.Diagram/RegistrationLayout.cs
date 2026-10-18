using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram;

/// <summary>An authored position stored in a registration's layout block.</summary>
public readonly record struct RegistrationPosition(double X, double Y);

/// <summary>
/// The <c>layout:</c> block of an <c>.adp</c> registration file: authored element positions,
/// stored as metadata enriching a body whose own format carries no layout (tech.md's
/// layout-in-.adp rule, databricks-diagrams Requirement 7).
/// </summary>
/// <remarks>
/// The block sits after the MIME line and the headers (<c>body:</c>, <c>view:</c> and their
/// kin): a <c>layout:</c> line followed by indented <c>&lt;element-id&gt;: &lt;x&gt; &lt;y&gt;</c>
/// entries. The <c>.adp</c> is ADP's own file, so the block itself may be normalized freely
/// on write - but everything above it is preserved byte for byte, because the headers belong
/// to the hierarchy's <c>DiagramFilePair</c> contract and a layout write must never disturb them.
/// Defined once and offered to every module; no module-specific knowledge lives in this
/// class.
/// </remarks>
public static class RegistrationLayout
{
    /// <summary>
    /// The stored positions, empty when the file or the block is absent. Malformed entries
    /// are ignored rather than fatal: the block is metadata, and a hand-mangled line must
    /// never stop a diagram from opening.
    /// </summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Read(string adpPath) =>
        ReadRegistration(adpPath)?.Layout ?? new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

    /// <summary>
    /// Stored positions overlaid on computed ones, element by element: an element with a
    /// stored position takes it, every other keeps its computed place - so a config change
    /// adding elements degrades gracefully, and stale stored ids simply have nothing to
    /// override (Requirement 7.4/7.5).
    /// </summary>
    public static IReadOnlyDictionary<string, RegistrationPosition> Apply(
        IReadOnlyDictionary<string, RegistrationPosition> computed,
        IReadOnlyDictionary<string, RegistrationPosition> stored)
    {
        ArgumentNullException.ThrowIfNull(computed);
        ArgumentNullException.ThrowIfNull(stored);

        var merged = new Dictionary<string, RegistrationPosition>(computed, StringComparer.Ordinal);
        foreach ((string id, RegistrationPosition position) in stored)
        {
            if (merged.ContainsKey(id))
            {
                merged[id] = position;
            }
        }

        return merged;
    }

    /// <summary>
    /// Writes one element's position and answers with the entry it replaced - null when the
    /// element had none - which is exactly what an undoable command needs for its inverse.
    /// </summary>
    /// <param name="writer">What publishes the registration, waiting for another save of it without blocking a thread.</param>
    public static async Task<RegistrationPosition?> SetPositionAsync(IFileWriter writer, string adpPath, string elementId, RegistrationPosition position, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        var positions = new Dictionary<string, RegistrationPosition>(Read(adpPath), StringComparer.Ordinal);
        positions.TryGetValue(elementId, out var existing);
        var had = positions.ContainsKey(elementId);
        positions[elementId] = position;
        await WriteBlockAsync(writer, adpPath, positions, cancellationToken);
        return had ? existing : null;
    }

    /// <summary>Removes one element's entry; removing the last entry removes the block itself.</summary>
    public static async Task RemovePositionAsync(IFileWriter writer, string adpPath, string elementId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        var positions = new Dictionary<string, RegistrationPosition>(Read(adpPath), StringComparer.Ordinal);
        if (positions.Remove(elementId))
        {
            await WriteBlockAsync(writer, adpPath, positions, cancellationToken);
        }
    }

    /// <summary>
    /// Replaces every stored position at once, for a module whose one gesture moves many
    /// elements - and whose undo must put all of them back, not only the one that was dragged.
    /// An empty set removes the block.
    /// </summary>
    public static Task ReplaceAsync(IFileWriter writer, string adpPath, IReadOnlyDictionary<string, RegistrationPosition> positions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(positions);

        return WriteBlockAsync(writer, adpPath, positions, cancellationToken);
    }

    /// <summary>
    /// Drops every stored id that is not in <paramref name="liveElementIds"/> - the "dropped
    /// on the next write" half of Requirement 7.5, for a module that knows which elements
    /// its document still holds.
    /// </summary>
    public static async Task PruneAsync(IFileWriter writer, string adpPath, IReadOnlySet<string> liveElementIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(liveElementIds);

        var positions = Read(adpPath);
        var kept = positions
            .Where(entry => liveElementIds.Contains(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        if (kept.Count != positions.Count)
        {
            await WriteBlockAsync(writer, adpPath, kept, cancellationToken);
        }
    }

    /// <summary>
    /// Replaces the layout block, leaving every byte above and below it untouched. The block
    /// lands where it was, or - when the file had none - directly after the header region:
    /// the MIME line and the contiguous <c>key: value</c> headers that follow it. The block is
    /// read and written by <see cref="AdpRegistration"/>, the one parser of a registration
    /// (code-structure FR-061).
    /// </summary>
    private static Task WriteBlockAsync(IFileWriter writer, string adpPath, IReadOnlyDictionary<string, RegistrationPosition> positions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var registration = ReadRegistration(adpPath) ?? throw new FileNotFoundException("The registration file is not there.", adpPath);
        return writer.SaveAsync(adpPath, registration.WithLayout(positions), cancellationToken);
    }

    /// <summary>
    /// The registration as it is on disk now, or null when it cannot be read. Parsed from a fresh
    /// read rather than <see cref="AdpRegistration.Read"/>'s cache: a position is read back right
    /// after it is written, and a rewrite that keeps the file's length within one tick of the clock
    /// would be answered from the cache.
    /// </summary>
    private static AdpRegistration? ReadRegistration(string adpPath)
    {
        string text;
        try
        {
            // Shared - and it is ADP's own writes this protects, not a user's editor. A
            // registration is read on every reload the watcher triggers and rewritten by every
            // reposition, so this read and WriteBlock above meet on the same file whenever a
            // move is followed closely by an undo. File.ReadAllText shares Read only, which
            // denies the replace WriteBlock performs; the undo then fails with "Could not
            // remove the position: the process cannot access the file". That is the OwlFlow
            // reposition flake - ADP blocking its own write, about one concurrent run in
            // twenty. Reading through SharedDocumentReader shares Delete and lets it land.
            text = SharedDocumentReader.ReadAllText(adpPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return AdpRegistration.Parse(text);
    }
}
