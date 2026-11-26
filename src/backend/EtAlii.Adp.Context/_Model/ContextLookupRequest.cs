namespace EtAlii.Adp.Context;

/// <summary>
/// A lookup asking the user to pick an element of the workspace by name as they type (DISL 0.12 §9.4, etalii.adp spec 092):
/// described as data, and searched through <paramref name="Search"/>, so the context service knows nothing of what it searches.
/// </summary>
/// <param name="Search">The candidates for the text typed so far.</param>
public sealed record ContextLookupRequest(
    string Title,
    string Icon,
    string Placeholder,
    string EmptyMessage,
    Func<string, IReadOnlyList<ContextLookupCandidate>> Search);

/// <summary>One element a lookup offers: the value its commit receives, its name, and its document's name and folder.</summary>
public sealed record ContextLookupCandidate(string Value, string Name, string DocumentName, string Folder);

/// <summary>The action needs the user to pick an element of the workspace in the lookup before it can run.</summary>
public sealed record ContextExecutionRequiresLookup(ContextLookupRequest Request) : ContextExecutionResult;
