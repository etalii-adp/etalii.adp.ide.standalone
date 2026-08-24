namespace EtAlii.Adp.Backend.Problems;

/// <summary>
/// What a project's problem set means. Deliberately not a bool: an empty list that was
/// never checked and an empty list that was checked and found clean are different truths,
/// and the panel says different things for them (Requirement 1.7).
/// </summary>
public enum ProjectProblemSetState
{
    /// <summary>No validation has ever run for this project - the list says nothing yet.</summary>
    NeverValidated,

    /// <summary>A validation is running now; the list shows what the previous one found.</summary>
    Validating,

    /// <summary>The list is what the most recent validation found - empty means clean.</summary>
    Validated,
}
