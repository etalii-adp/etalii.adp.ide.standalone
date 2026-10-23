using EtAlii.Adp.Files;
using EtAlii.Adp.Sessions;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// Opens a session per connection for a requirement document, with its registration when it has one (spec 029 R4): each
/// connection has its own focus and narrowing, which are the client's, over the one pair of files.
/// </summary>
internal sealed class HierarchicalRequirementSessionFactory(IFileWriter writer, ProjectFileEvents? events = null) : IDesignerSessionFactory
{
    public string Origin => Designer.Origin;

    public IDesignerSession Open(ShortGuid watchId, ProjectRoot root, string? registrationPath, string bodyPath) =>
        new HierarchicalRequirementSession(bodyPath, registrationPath, writer, events: events);
}
