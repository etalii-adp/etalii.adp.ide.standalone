using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The operations on assessments of <c>hierarchical-requirement.des</c> (FR-026): <c>addAssessment</c> on an entry, and
/// <c>changeAssessment</c>, <c>resolveAssessment</c> and <c>removeAssessment</c> on an assessment, with the numbering every
/// edit writes (spec 029 R17). What they write is what the conformance fixtures state, byte for byte: an add at the end of
/// Assessments, the section appended when the file has none, and a resolve one character where the line has a checkbox.
/// </summary>
internal static class AssessmentOperations
{
    public const string AddAssessment = "addAssessment";
    public const string ChangeAssessment = "changeAssessment";
    public const string ResolveAssessment = "resolveAssessment";
    public const string RemoveAssessment = "removeAssessment";

    private const string AssessmentType = "Assessment";

    /// <summary>Whether <paramref name="operation"/> is one of these.</summary>
    public static bool Handles(string operation) => operation is AddAssessment or ChangeAssessment or ResolveAssessment or RemoveAssessment;

    /// <summary>Whether <paramref name="operation"/> targets an assessment rather than an entry.</summary>
    public static bool TargetsAnAssessment(string operation) => operation is ChangeAssessment or ResolveAssessment or RemoveAssessment;

    /// <summary>
    /// Whether <paramref name="parameters"/> can be written by <paramref name="operation"/>: the sentence saying why not, or
    /// empty. The text is the rest of its bullet's line, so it is one line and says something; the kind is one of the four.
    /// </summary>
    public static string Refusal(string operation, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (operation is not (AddAssessment or ChangeAssessment)) return "";
        if (parameters.TryGetValue("kind", out var kind) && !Enum.TryParse<AssessmentKind>(kind, ignoreCase: true, out _)) return $"'{kind}' is not a kind of assessment: a question, remark, warning or idea.";
        if (operation == ChangeAssessment && !parameters.ContainsKey("kind")) return $"'{operation}' needs its kind.";
        var text = parameters.GetValueOrDefault("text", "");
        if (text.Trim().Length == 0) return "An assessment needs a text.";
        return text.AsSpan().IndexOfAny('\r', '\n') >= 0 ? "The text of an assessment is one line; its detail holds the lines below it." : "";
    }

    /// <summary>
    /// The bytes of the body <paramref name="body"/>, named <paramref name="fileName"/>, after <paramref name="operation"/>
    /// on <paramref name="targetId"/>: the surface's id of an entry for an add, the assessment's id for the others; or the
    /// sentence saying why it cannot be, in which case nothing changed. A target the file no longer has is refused as a
    /// change made against an older file.
    /// </summary>
    public static (byte[]? Bytes, string Refusal) Apply(byte[] body, string fileName, string operation, string targetId, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parameters);
        if (Refusal(operation, parameters) is { Length: > 0 } refusal) return (null, refusal);

        var read = HierarchicalRequirementBody.Read(body, fileName);
        if (read.Document.ReadOnlyReason.Length > 0) return (null, read.Document.ReadOnlyReason);
        var edit = new BodyEdit(read);
        var elements = read.Body.Model.Elements.Where(element => element.Type == AssessmentType).Select(element => element.Id).ToList();

        if (operation == AddAssessment)
        {
            var ids = HierarchicalRequirementSurfaceMapper.NodeIds(read.Document.Entries);
            if (read.Document.Entries.FirstOrDefault(candidate => ids[candidate] == targetId) is not { } entry) return (null, HierarchicalRequirementGestures.Drifted);

            // An assessment names its entry by number; a placeholder's is the one this same edit writes for it.
            var kind = parameters.TryGetValue("kind", out var named) ? Stored(named) : Stored(nameof(AssessmentKind.Question));
            edit.Change(new ModelChange.Add(AssessmentType, $"assessment/{elements.Count}", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["kind"] = kind,
                ["about"] = entry.Id,
                ["text"] = parameters["text"],
                ["resolved"] = false,
            }));
            return edit.Write();
        }

        var assessment = read.Document.Assessments.FirstOrDefault(candidate => candidate.Id == targetId);
        if (assessment is null) return (null, HierarchicalRequirementGestures.Drifted);
        var element = elements[Place(read.Document.Assessments, assessment)];
        switch (operation)
        {
            case ChangeAssessment:
                edit.Set(element, "kind", Stored(parameters["kind"]));
                edit.Set(element, "text", parameters["text"]);
                edit.Set(element, "detail", parameters.GetValueOrDefault("detail", ""));
                break;
            case ResolveAssessment:
                edit.Set(element, "resolved", !assessment.Resolved);
                break;
            default:
                edit.Change(new ModelChange.Remove(element));
                break;
        }

        return edit.Write();
    }

    private static int Place(IReadOnlyList<Assessment> assessments, Assessment assessment)
    {
        for (var at = 0; at < assessments.Count; at++)
        {
            if (ReferenceEquals(assessments[at], assessment)) return at;
        }

        throw new ArgumentException("The assessment is not one of these.", nameof(assessment));
    }

    /// <summary>A kind as the file writes it, its stored form: <c>Question</c> for question in any case.</summary>
    private static string Stored(string kind) => Enum.Parse<AssessmentKind>(kind, ignoreCase: true).ToString();
}
