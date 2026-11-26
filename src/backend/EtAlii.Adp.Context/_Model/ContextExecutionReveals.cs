namespace EtAlii.Adp.Context;

/// <summary>
/// The action did its work by pointing somewhere: the element <paramref name="ElementId"/> of the document at
/// <paramref name="FullPath"/>, which the user's workspace opens, or brings to the front, with that element selected
/// (etalii.adp spec 092, FR-014). An empty <paramref name="ElementId"/> opens the document alone.
/// </summary>
public sealed record ContextExecutionReveals(string FullPath, string ElementId) : ContextExecutionResult;
