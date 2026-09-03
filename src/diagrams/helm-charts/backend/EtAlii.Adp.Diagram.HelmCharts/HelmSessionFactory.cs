using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// Opens a <see cref="HelmSession"/> per connection, resolving the folder subject the way the
/// first folder-subject module pinned it - and passing the registration on, because this one's
/// sessions write authored positions into it.
/// </summary>
/// <remarks>
/// The three seam facts this composition rests on were verified in core before the design was
/// written, and an implementer finding any of them false stops and reports rather than working
/// around it: the router hands a folder-subject factory the <c>.adp</c> as both
/// <c>bodyPath</c> and <c>registrationPath</c>; <c>RegistrationLayout.Read</c> tolerates a
/// registration whose only header is the MIME line; and <c>IDiagramSession.MoveElementToAsync</c>
/// is a default-refused member a session may override.
/// </remarks>
public sealed class HelmSessionFactory : IDiagramSessionFactory
{
    private readonly IHelmChartStore _store;
    private readonly HelmElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public HelmSessionFactory(IHelmChartStore store, HelmElementMapper mapper, IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _store = store;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin => Diagram.HelmCharts.Origin;

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);

        // For a type that declares no document extension, the router routes the registration
        // as its own body - so bodyPath and registrationPath are the same file, and either
        // names the folder. Preferring the registration is deliberate: it is the file that
        // made this folder a diagram, and the file the layout: block lives in.
        var marker = registrationPath ?? bodyPath;
        var folder = IoPath.GetDirectoryName(IoPath.GetFullPath(marker))
                     ?? throw new InvalidOperationException($"'{marker}' has no folder to read.");

        return new HelmSession(
            folder,
            IoPath.GetFullPath(marker),
            _store,
            _mapper,
            // The project's history, so a drag on this canvas is one undo away like every other edit.
            _historyStacks.Get(rootPath));
    }
}
