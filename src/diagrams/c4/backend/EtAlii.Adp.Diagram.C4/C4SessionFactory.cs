using EtAlii.Adp.History;
using EtAlii.Adp.Sessions;

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

    public C4SessionFactory(ToolOrigin origin, IC4DocumentStore documents, C4ElementMapper mapper, IHistoryStackStore historyStacks)
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

    public ToolOrigin Origin { get; }

    public IDiagramSession Open(ShortGuid watchId, ProjectRoot root, string bodyPath, string? registrationPath)
    {
        // The registration's first view: header, which is what c4 has always opened; AdpRegistration's
        // View takes the last, and the two differ only on a registration holding two (code-structure T295).
        var viewKey = registrationPath is { Length: > 0 } ? AdpRegistration.Read(registrationPath)?.FirstView : null;
        // The project's history, so a drag on this canvas is one undo away like every other edit.
        return new C4Session(bodyPath, viewKey, _documents, _mapper, _historyStacks.Get(root));
    }
}
