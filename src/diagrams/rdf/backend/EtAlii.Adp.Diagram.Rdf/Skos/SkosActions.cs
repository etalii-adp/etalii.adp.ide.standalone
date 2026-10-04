using System.Text;
using EtAlii.Adp.Context;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The scheme reading's gestures, offered and executed through the family provider trio's one
/// registration (skos-diagram Requirements 5, 6). Everything here is keyed off the file's own
/// assertions - both ends typed <c>skos:Concept</c>, a <c>skos:ConceptScheme</c> present - never
/// off the registration, which the context seam deliberately does not carry: an element is an
/// element whichever reading selected it, and the thesaurus gestures apply wherever the data
/// says a thesaurus is.
/// </summary>
public static class SkosActions
{
    /// <summary>File one concept under another: one <c>skos:broader</c> on the narrower end, no inverse written (Requirement 5.1).</summary>
    public const string FileUnderActionId = "skos.file-under";

    /// <summary>Cross-link two concepts: one <c>skos:related</c> (Requirement 5.2).</summary>
    public const string RelateActionId = "skos.relate";

    /// <summary>Create a concept at a placement: type, preferred label and scheme as one command (Requirement 5.3).</summary>
    public const string AddConceptActionId = "skos.add-concept";

    /// <summary>Remove every asserted triple of a hierarchy or related pair (Requirement 5.6).</summary>
    public const string DisconnectActionId = "skos.disconnect";

    /// <summary>The skos entries for a discovery pass, or empty where the selection is not the reading's business.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Discover(RdfDocumentEntry entry, ContextTarget target)
    {
        if (RdfRelationGesture.TryParse(target.ElementId, out var from, out var to)
            && SkosSelection.ConceptOf(entry, from) is not null
            && SkosSelection.ConceptOf(entry, to) is not null)
        {
            // Both ends are asserted concepts: the thesaurus gestures lead, the generic
            // predicate dialog stays available below them from the family's own discovery.
            return
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(FileUnderActionId, "File under (broader)", "mdi-file-tree"),
                    new ContextActionDefinition(RelateActionId, "Relate (skos:related)", "mdi-swap-horizontal"),
                ]),
            ];
        }

        if (RdfNewPlacement.TryParse(target.ElementId, out _, out _) && SkosSelection.HasScheme(entry))
        {
            return
            [
                new ContextActionGroupDefinition(
                    [new ContextActionDefinition(AddConceptActionId, "Add concept here…", "mdi-tag-plus-outline")]),
            ];
        }

        if (SkosSelection.PairOf(entry, target.ElementId) is { } pair)
        {
            var label = pair.Kind == SkosEdgeKind.Hierarchy
                ? pair.Triples.Count > 1 ? "Disconnect (both directions)" : "Disconnect"
                : "Remove related link";
            return
            [
                new ContextActionGroupDefinition(
                    [new ContextActionDefinition(DisconnectActionId, label, "mdi-link-off", new ContextShortcutDefinition("Delete"))]),
            ];
        }

        return [];
    }

    /// <summary>Executes a skos action, or null when <paramref name="actionId"/> is not this reading's.</summary>
    public static async ValueTask<ContextExecutionResult?> ExecuteAsync(
        IHistoryStackStore historyStacks,
        RdfDocumentEntry entry,
        ContextTarget target,
        string actionId,
        CancellationToken cancellationToken)
    {
        switch (actionId)
        {
            case FileUnderActionId or RelateActionId
                when RdfRelationGesture.TryParse(target.ElementId, out var from, out var to):
            {
                var fromIri = SkosSelection.ConceptOf(entry, from);
                var toIri = SkosSelection.ConceptOf(entry, to);
                if (fromIri is null || toIri is null)
                {
                    return new ContextExecutionFailed("Both ends of this gesture must be concepts the file types as skos:Concept.");
                }

                if (fromIri == toIri)
                {
                    return new ContextExecutionFailed("A concept cannot be filed under or related to itself.");
                }

                // File-under: the dragged (from) concept goes under the target - one broader
                // triple on the narrower end, the direction thesauri are authored in; the
                // inverse is never co-written (Requirement 5.1).
                var command = actionId == FileUnderActionId
                    ? new AddRdfTripleCommand(target.ResolvedFullPath, fromIri, SkosVocabulary.Broader, toIri)
                    : new AddRdfTripleCommand(target.ResolvedFullPath, fromIri, SkosVocabulary.Related, toIri);
                return await DispatchAsync(historyStacks, target, command, cancellationToken);
            }

            case AddConceptActionId:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Add concept", "mdi-tag-plus-outline", "Preferred label", "", "Add"));

            case DisconnectActionId when SkosSelection.PairOf(entry, target.ElementId) is { } pair:
            {
                if (pair.Triples.Count > 1)
                {
                    return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                        "Disconnect",
                        "mdi-link-off",
                        "This pair is asserted in both directions; disconnecting removes both statements as one undo.",
                        "Disconnect",
                        Danger: false));
                }

                return await DispatchAsync(
                    historyStacks, target,
                    new DisconnectSkosPairCommand(target.ResolvedFullPath, pair.AIri, pair.BIri, pair.Kind == SkosEdgeKind.Hierarchy),
                    cancellationToken);
            }

            default:
                return null;
        }
    }

    /// <summary>Validates a skos dialog value, or null when the action is not this reading's.</summary>
    public static ContextValidationResult? Validate(RdfDocumentEntry entry, string actionId, string value)
    {
        if (actionId != AddConceptActionId)
        {
            return null;
        }

        if (value.Trim().Length == 0)
        {
            return ContextValidationResult.Rejected("A concept needs a preferred label.");
        }

        (string? iri, string error) = MintIri(entry, value);
        if (iri is null)
        {
            return ContextValidationResult.Rejected(error);
        }

        return entry.Model.Triples.Any(t =>
            (t.Subject is IriTerm s && s.Iri == iri) || (t.Object is IriTerm o && o.Iri == iri))
            ? ContextValidationResult.Rejected(
                $"{iri} already names something in this document. Choose a different label, or rename the existing resource first.")
            : ContextValidationResult.Accepted;
    }

    /// <summary>Commits a skos dialog or a confirmed disconnect, or null when the action is not this reading's.</summary>
    public static ICommand? CommandFor(RdfDocumentEntry entry, ContextTarget target, string actionId, string value)
    {
        switch (actionId)
        {
            case AddConceptActionId:
            {
                (string? iri, _) = MintIri(entry, value);
                // The label's tag is the default display language: the context channel does not
                // carry which registration - and so which language: header - a selection came
                // through, so the one deterministic choice is the chain's own default.
                return iri is null
                    ? null
                    : new AddSkosConceptCommand(
                        target.ResolvedFullPath, iri, value.Trim(), SkosLabels.DefaultLanguage,
                        SkosSelection.FirstScheme(entry) ?? "");
            }

            case DisconnectActionId when SkosSelection.PairOf(entry, target.ElementId) is { } pair:
                // The confirmed leg of the both-ways disconnect.
                return new DisconnectSkosPairCommand(
                    target.ResolvedFullPath, pair.AIri, pair.BIri, pair.Kind == SkosEdgeKind.Hierarchy);

            default:
                return null;
        }
    }

    /// <summary>
    /// The IRI a labeled concept starts life under: the namespace the file itself names things
    /// in - the prefix its first prefixed subject is written under, falling back to the first
    /// declared prefix - plus the label slugged the document-factory way. A file with no prefix
    /// at all is asked for one rather than guessed at.
    /// </summary>
    internal static (string? Iri, string Error) MintIri(RdfDocumentEntry entry, string label)
    {
        var subjectPrefix = entry.Model.Triples
            .Select(t => t.Subject)
            .OfType<IriTerm>()
            .Select(s => s.AsWritten)
            .Where(written => !written.StartsWith('<') && written.Contains(':'))
            .Select(written => written[..written.IndexOf(':')])
            .FirstOrDefault();
        var ns = (subjectPrefix is not null ? entry.Model.Expansion(subjectPrefix) : null)
            ?? (entry.Model.Prefixes.Count > 0 ? entry.Model.Prefixes[0].Iri : null);
        if (ns is null)
        {
            return (null, "This file declares no prefix to mint the concept's IRI under. Declare one first (the family's prefix action).");
        }

        var builder = new StringBuilder(label.Length);
        foreach (var character in label.Trim())
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' ? character : '_');
        }

        var local = builder.ToString().Trim('_');
        return local.Length > 0
            ? (ns + local, "")
            : (null, "The label leaves nothing usable for an IRI once slugged; use at least one letter or digit.");
    }

    private static async ValueTask<ContextExecutionResult> DispatchAsync(
        IHistoryStackStore historyStacks, ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess
            ? new ContextExecutionCompleted()
            : new ContextExecutionFailed(result.Error);
    }
}
