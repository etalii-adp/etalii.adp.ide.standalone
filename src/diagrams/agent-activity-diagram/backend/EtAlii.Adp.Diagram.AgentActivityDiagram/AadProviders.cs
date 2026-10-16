using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram;

/// <summary>
/// The property grid of an element or a row: its type's attributes, each written through a command
/// (Requirement 9.4). A relation shows nothing; it is its two ends.
/// </summary>
public sealed class AadContextPropertyProvider(IHistoryStackStore historyStacks, IAadDocumentStore documents)
    : DiagramPropertyProvider<AadBody, AadModel>(historyStacks, documents)
{
    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<AadBody, AadModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return AadDefinition.ElementOf(entry.Document.Disl.Diagram, elementId) is { Type.IsRelation: false } element
            ? AadDefinition.Rows(element, readOnly: !entry.IsUsable)
            : [];
    }

    /// <inheritdoc />
    /// <remarks>Through the project's history and out through the delta stream - never written by the grid.</remarks>
    protected override DiagramPropertyChange Change(ContextTarget target, AadModel model, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        return DiagramPropertyChange.By(new SetAadAttributeCommand(target.ResolvedFullPath, target.ElementId, propertyId, value));
    }
}

/// <summary>
/// What an activity file has to say for itself in the Errors and Warnings panel: what the
/// definition's constraints find, and what the reader could not read (Requirements 3.7, 3.8, 8.5).
/// </summary>
/// <remarks>
/// A file that breaks a rule still opens and draws. Nothing here refuses anything; a gesture is
/// refused by its command.
/// </remarks>
public sealed class AadValidator : IDiagramValidator
{
    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.AgentActivity.Origin;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(DiagramValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Validate(AadBody.Parse(request.Document)));
    }

    /// <summary>The findings of one body, the constraints' first and then the reader's, each on the line it is about.</summary>
    public static IReadOnlyList<DiagramProblem> Validate(AadBody document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var findings = ConstraintEvaluator.Evaluate(AadDefinition.Specification, document.Disl.Diagram, new DislConstraintOptions(AadDefinition.Env()));
        return
        [
            .. findings.Select(finding => new DiagramProblem(
                DislDiagramDefinition.SeverityOf(finding),
                finding.Message,
                finding.Code,
                new DiagramProblemLineLocation((uint)Math.Max(finding.Line ?? 1, 1)))),
            .. document.Model.Findings.Where(finding => !IsAboutAnOrphan(document, finding)).Select(finding => new DiagramProblem(
                SeverityOf(document, finding),
                finding.Code == HeaderMismatch && document.Version > AadBody.KnownVersion
                    ? $"This file was written for version {document.Version} of the activity file. It is shown, and it is not changed."
                    : finding.Message,
                finding.Code,
                new DiagramProblemLineLocation((uint)Math.Max(finding.Location?.Line ?? 1, 1)))),
            .. UnknownKeys(document),
        ];
    }

    private const string DanglingReference = "fbl.dangling-reference";
    private const string HeaderMismatch = "fbl.header-mismatch";

    /// <summary>
    /// What a reader's finding weighs here. A reference to something the file does not have and an
    /// id used twice break the file's own rules and are errors (Requirement 3.8); an entry an agent
    /// wrote without an id is shown and merely mentioned (Requirement 8.7); a file of a newer
    /// version is an error, since nothing can be changed in it (Requirement 2.8).
    /// </summary>
    private static DiagramProblemSeverity SeverityOf(AadBody document, Finding finding) => finding.Code switch
    {
        DanglingReference or "std.duplicateId" => DiagramProblemSeverity.Error,
        "std.missingId" => DiagramProblemSeverity.Info,
        HeaderMismatch when document.Version > AadBody.KnownVersion => DiagramProblemSeverity.Error,
        _ => finding.Severity switch { FindingSeverity.Error => DiagramProblemSeverity.Error, FindingSeverity.Info => DiagramProblemSeverity.Info, _ => DiagramProblemSeverity.Warning },
    };

    /// <summary>A lock or a group state whose element is gone is removed on the next write and not reported (Requirement 8.8).</summary>
    private static bool IsAboutAnOrphan(AadBody document, Finding finding)
    {
        if (finding.Code != DanglingReference || finding.Location is not { } location) return false;
        var orphans = document.OrphanedViewEntries.ToHashSet(StringComparer.Ordinal);
        return document.Model.Elements.Any(element => orphans.Contains(element.Id) && location.Line >= element.Line && location.Line < element.Line + 4);
    }

    /// <summary>Keys the diagram does not read: kept on every write, and mentioned once each (Requirement 2.9).</summary>
    private static IEnumerable<DiagramProblem> UnknownKeys(AadBody document) =>
        document.Model.Elements.SelectMany(element =>
            (element.Attributes.GetValueOrDefault("unknownKeys") as IEnumerable<object?> ?? [])
                .OfType<string>()
                .Select(key => new DiagramProblem(
                    DiagramProblemSeverity.Info,
                    $"The key \"{key}\" is not one the diagram reads. It is kept as it is.",
                    "aad.unknown-key",
                    new DiagramProblemLineLocation((uint)Math.Max(element.Line, 1)))));
}
