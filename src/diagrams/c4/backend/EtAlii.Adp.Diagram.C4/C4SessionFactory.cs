using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Opens a <see cref="C4Session"/> for one C4 diagram type. Six of these are registered, one
/// per type, and they differ only in the origin they answer to: the view a session shows comes
/// from the <c>.adp</c> file's <c>view:</c> header, not from which factory opened it
/// (c4-diagrams Requirement 2.4).
/// </summary>
public sealed class C4SessionFactory : IDiagramSessionFactory
{
    private readonly IC4DocumentStore _documents;
    private readonly C4ElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public C4SessionFactory(DiagramOrigin origin, IC4DocumentStore documents, C4ElementMapper mapper, IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);

        Origin = origin;
        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin { get; }

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        var viewKey = registrationPath is { Length: > 0 } ? ReadViewKey(registrationPath) : null;
        // The project's history, so a drag on this canvas is one undo away like every other edit.
        return new C4Session(watchId, bodyPath, viewKey, _documents, _mapper, _historyStacks.Get(rootPath));
    }

    /// <summary>
    /// The <c>view:</c> header of a registration file, or null. Read here rather than in core:
    /// core hands the module its own file and interprets nothing in it.
    /// </summary>
    private static string? ReadViewKey(string registrationPath)
    {
        try
        {
            using var reader = SharedDocumentReader.OpenText(registrationPath);
            reader.ReadLine(); // the MIME line
            for (var scanned = 0; scanned < 8; scanned++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    return null;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("view:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed["view:".Length..].Trim();
                    return value.Length == 0 ? null : value;
                }

                if (!trimmed.StartsWith("body:", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
