namespace EtAlii.Adp.Context;

/// <summary>
/// Finds the entry a connection knows a document of its project by, and the tool that opens it: what a reveal needs to open the
/// document's tab (etalii.adp spec 092, FR-014). The hierarchy implements it; the context service asks it without knowing it.
/// </summary>
public interface IContextEntryLocator
{
    /// <summary>The entry of <paramref name="fullPath"/> on the connection <paramref name="watchId"/>, or null when it holds none.</summary>
    ContextEntryLocation? Locate(ShortGuid watchId, ProjectRoot root, string fullPath);
}
