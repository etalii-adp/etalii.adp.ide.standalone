using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The operations on stored filters of <c>hierarchical-requirement.des</c> (FR-026, FR-048): <c>storeFilter</c> keeps the
/// applied filter with its sorting under a name, <c>renameFilter</c> and <c>removeFilter</c> change a stored one. They
/// write the registration's <c>filters</c> block alone, as one step of the history, byte for byte as the conformance
/// fixture <c>hierarchical-requirement-filters</c> states; applying a filter or a sort writes nothing (FR-049).
/// </summary>
internal static class FilterOperations
{
    public const string StoreFilter = "storeFilter";
    public const string RenameFilter = "renameFilter";
    public const string RemoveFilter = "removeFilter";

    private const string FiltersBlock = "filters";

    /// <summary>The prefix of a filter's id, <c>'filter/' + self.name</c> (hierarchical-requirement.des, ids).</summary>
    private const string FilterIdPrefix = "filter/";

    /// <summary>Whether <paramref name="operation"/> is one of these.</summary>
    public static bool Handles(string operation) => operation is StoreFilter or RenameFilter or RemoveFilter;

    /// <summary>Whether <paramref name="operation"/> targets a stored filter rather than the document.</summary>
    public static bool TargetsAFilter(string operation) => operation is RenameFilter or RemoveFilter;

    /// <summary>Whether <paramref name="parameters"/> can be written by <paramref name="operation"/>: the sentence saying why not, or empty.</summary>
    public static string Refusal(string operation, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (operation is not (StoreFilter or RenameFilter)) return "";
        if (!parameters.TryGetValue("name", out var name) || name.Trim().Length == 0) return "A filter needs a name.";
        if (operation == StoreFilter && (!parameters.TryGetValue("query", out var query) || query.Trim().Length == 0)) return "There is no filter to store.";
        return "";
    }

    /// <summary>
    /// The registration before and after <paramref name="operation"/>; or the sentence saying why it cannot be written, in
    /// which case nothing changed. The body is never written. A filter the registration no longer holds is refused as a
    /// change made against an older file.
    /// </summary>
    /// <param name="registration">The registration's bytes, or null when the body has none yet.</param>
    public static ListEdit Apply(byte[] body, string bodyName, byte[]? registration, string registrationName, string operation, string targetId, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parameters);
        if (Refusal(operation, parameters) is { Length: > 0 } refusal) return ListEdit.Refused(refusal);

        var document = HierarchicalRequirementBody.Read(body, bodyName, registration, registrationName).Document;
        ModelChange.Setting setting;
        if (operation == StoreFilter)
        {
            setting = new ModelChange.Setting(FiltersBlock, parameters["name"].Trim(), parameters["query"].Trim());
        }
        else
        {
            if (document.Filters.FirstOrDefault(filter => FilterIdPrefix + filter.Name == targetId) is not { } stored) return ListEdit.Refused(HierarchicalRequirementGestures.Drifted);
            setting = operation == RenameFilter
                ? new ModelChange.Setting(FiltersBlock, stored.Name, Rename: parameters["name"].Trim())
                : new ModelChange.Setting(FiltersBlock, stored.Name, RemoveEntry: true);
        }

        (byte[]? before, byte[]? after, string refused) = RegistrationEdit.Apply(body, bodyName, registration, registrationName, block => RegistrationEdit.Members(document, block), setting);
        return after is null ? ListEdit.Refused(refused) : new ListEdit(null, before, after, "");
    }
}
