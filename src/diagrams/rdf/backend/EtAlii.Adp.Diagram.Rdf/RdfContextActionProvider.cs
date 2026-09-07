using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// What can be done to a selected RDF element, offered as data and reached through the one path
/// the context service defines (rdf-diagram Requirement 6).
/// </summary>
/// <remarks>
/// <para>
/// Every edit dispatches a command through the project's history, so each is one undo away like
/// every other edit in the IDE. Term dialogs accept a full IRI or a prefixed name under the
/// document's own declarations, and an undeclared prefix is refused by name before any splice
/// (Requirement 5.7).
/// </para>
/// <para>
/// Under truncation every edit is withheld with the stated sentence: an edit through a partial
/// view could touch what the view does not show (Requirement 8.4). Blank-node-rooted selections
/// describe but never edit, per the identity boundary.
/// </para>
/// </remarks>
public sealed class RdfContextActionProvider : IContextActionProvider
{
    /// <summary>Rename a resource, rewriting every reference with it (Requirement 5.4).</summary>
    public const string RenameResourceActionId = "rdf.rename-resource";

    /// <summary>Remove a resource and every triple touching it, saying how many first (Requirement 6).</summary>
    public const string RemoveResourceActionId = "rdf.remove-resource";

    /// <summary>Remove a selected edge's triple.</summary>
    public const string RemoveEdgeActionId = "rdf.remove-edge";

    /// <summary>The relation gesture: relate two drawn resources, asking for the predicate.</summary>
    public const string ConnectActionId = "rdf.connect";

    /// <summary>Add a resource at a placement, asking for its name.</summary>
    public const string AddResourceActionId = "rdf.add-resource";

    /// <summary>Declare a prefix, asking for <c>prefix: iri</c> in one line.</summary>
    public const string AddPrefixActionId = "rdf.add-prefix";

    /// <summary>The subclass gesture: one <c>rdfs:subClassOf</c> splice, a duplicate refused first (owl-diagram Requirement 6.1).</summary>
    public const string SubclassActionId = "owl.subclass";

    /// <summary>Add a class at a placement: one <c>a owl:Class</c> declaration (owl-diagram Requirement 6.2).</summary>
    public const string AddClassActionId = "owl.add-class";

    /// <inheritdoc cref="AddClassActionId" />
    public const string AddObjectPropertyActionId = "owl.add-object-property";

    /// <inheritdoc cref="AddClassActionId" />
    public const string AddDatatypePropertyActionId = "owl.add-datatype-property";

    /// <inheritdoc cref="AddClassActionId" />
    public const string AddIndividualActionId = "owl.add-individual";

    /// <summary>What each OWL add action declares its new term as.</summary>
    private static readonly Dictionary<string, string> _owlDeclarationTypes = new(StringComparer.Ordinal)
    {
        [AddClassActionId] = OwlVocabulary.Class,
        [AddObjectPropertyActionId] = OwlVocabulary.ObjectProperty,
        [AddDatatypePropertyActionId] = OwlVocabulary.DatatypeProperty,
        [AddIndividualActionId] = OwlVocabulary.NamedIndividual,
    };

    private readonly IHistoryStackStore _historyStacks;
    private readonly IRdfDocumentStore _documents;

    public RdfContextActionProvider(IHistoryStackStore historyStacks, IRdfDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);

        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!RdfSelection.AnswersFor(target))
        {
            // Another type's element; a provider consulted for every element in its scope
            // answers with nothing rather than parsing another notation's file.
            return Result([]);
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable || RdfSelection.IsTruncated(entry))
        {
            // A broken file withholds every edit (Requirement 1.5); so does a truncated view
            // (Requirement 8.4) - executing anyway answers with the sentence.
            return Result([]);
        }

        if ((RdfSelection.ResourceOf(entry, target.ElementId) ?? OwlSelection.IndividualIriOf(entry, target.ElementId)) is { } iri)
        {
            var touching = RdfSelection.Touching(entry, iri).Count;
            return Result(
            [
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RenameResourceActionId, "Rename…", "mdi-pencil-outline", new ContextShortcutDefinition("F2")),
                    new ContextActionDefinition(
                        RemoveResourceActionId,
                        touching == 1 ? "Remove" : $"Remove (with {touching} statements)",
                        "mdi-delete-outline",
                        new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (SkosSelection.PairOf(entry, target.ElementId) is not null)
        {
            // A hierarchy or related pair is the scheme reading's edge: its disconnect takes
            // every asserted direction as one undo, which the generic remove-statement cannot.
            return Result([.. Shacl.ShaclActions.Discover(entry, target), .. SkosActions.Discover(entry, target)]);
        }

        if (RdfSelection.EdgeOf(entry, target.ElementId) is not null)
        {
            // A skos hierarchy or related pair is drawn as ONE edge whose id happens to be a
            // valid family edge id too, so the reading's disconnect - which takes every asserted
            // direction as one undo - leads, and the family's single-statement removal stays
            // beneath it for the reader who means exactly that statement.
            return Result(
            [
                .. Shacl.ShaclActions.Discover(entry, target),
                .. SkosActions.Discover(entry, target),
                new ContextActionGroupDefinition(
                [
                    new ContextActionDefinition(RemoveEdgeActionId, "Remove statement", "mdi-vector-polyline-remove", new ContextShortcutDefinition("Delete")),
                ]),
            ]);
        }

        if (RdfNewPlacement.TryParse(target.ElementId, out _, out _))
        {
            var placementActions = new List<ContextActionDefinition>
            {
                new(AddResourceActionId, "Add resource here…", "mdi-card-plus-outline"),
                new(AddPrefixActionId, "Declare prefix…", "mdi-at"),
            };
            if (OwlSelection.IsOntologyDocument(entry))
            {
                // The ontology vocabulary, offered exactly where the document carries the
                // marker - the same entries the OWL toolbox drops (owl-diagram Requirement 7.1).
                placementActions.Add(new ContextActionDefinition(AddClassActionId, "Add class here…", "mdi-shape-circle-plus"));
                placementActions.Add(new ContextActionDefinition(AddObjectPropertyActionId, "Add object property…", "mdi-ray-start-arrow"));
                placementActions.Add(new ContextActionDefinition(AddDatatypePropertyActionId, "Add datatype property…", "mdi-form-textbox"));
                placementActions.Add(new ContextActionDefinition(AddIndividualActionId, "Add individual…", "mdi-account-outline"));
            }

            // Each reading's entries lead where the file's own assertions say that reading
            // applies - a thesaurus for skos, an ontology marker for owl; the family's generic
            // pair stays beneath both.
            return Result([.. Shacl.ShaclActions.Discover(entry, target),
                .. SkosActions.Discover(entry, target), new ContextActionGroupDefinition(placementActions)]);
        }

        if (RdfRelationGesture.TryParse(target.ElementId, out var gestureFrom, out var gestureTo))
        {
            var relationActions = new List<ContextActionDefinition>();

            // Between two classes the gesture's first meaning is hierarchy: one
            // rdfs:subClassOf splice, no dialog (owl-diagram Requirement 6.1).
            var fromIri = RdfSelection.ResourceOf(entry, gestureFrom);
            var toIri = RdfSelection.ResourceOf(entry, gestureTo);
            if (fromIri is not null && toIri is not null
                && OwlSelection.IsClass(entry, fromIri) && OwlSelection.IsClass(entry, toIri))
            {
                relationActions.Add(new ContextActionDefinition(SubclassActionId, "Subclass of", "mdi-file-tree"));
            }

            relationActions.Add(new ContextActionDefinition(ConnectActionId, "Relate…", "mdi-ray-start-arrow"));

            // The scheme reading's file-under and relate lead between two asserted concepts;
            // the ontology's subclass and the family's dialog stay beneath.
            return Result([.. Shacl.ShaclActions.Discover(entry, target),
                .. SkosActions.Discover(entry, target), new ContextActionGroupDefinition(relationActions)]);
        }

        // A skos edge id (the canonical broader-direction shape) is the scheme reading's alone;
        // blank nodes and the banner stay describable, never editable.
        return Result([.. Shacl.ShaclActions.Discover(entry, target), .. SkosActions.Discover(entry, target)]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (RdfSelection.IsTruncated(entry))
        {
            return new ContextExecutionFailed(RdfSelection.TruncatedRefusal);
        }

        if (await Shacl.ShaclActions.ExecuteAsync(_historyStacks, entry, target, actionId, cancellationToken) is { } shaclResult)
        {
            return shaclResult;
        }

        if (await SkosActions.ExecuteAsync(_historyStacks, entry, target, actionId, cancellationToken) is { } skosResult)
        {
            return skosResult;
        }

        var iri = RdfSelection.ResourceOf(entry, target.ElementId) ?? OwlSelection.IndividualIriOf(entry, target.ElementId);

        switch (actionId)
        {
            case SubclassActionId when RdfRelationGesture.TryParse(target.ElementId, out var from, out var to):
            {
                var fromIri = RdfSelection.ResourceOf(entry, from);
                var toIri = RdfSelection.ResourceOf(entry, to);
                if (fromIri is null || toIri is null)
                {
                    return new ContextExecutionFailed("A subclass relation needs two named classes.");
                }

                // Refused before any splice (owl-diagram Requirement 6.1).
                var alreadyAsserted = entry.Model.Triples.Any(t =>
                    t.Subject is IriTerm s && s.Iri == fromIri
                    && t.Predicate.Iri == OwlVocabulary.SubClassOf
                    && t.Object is IriTerm o && o.Iri == toIri);
                if (alreadyAsserted)
                {
                    return new ContextExecutionFailed(OwlSelection.DuplicateSubclassRefusal);
                }

                return await DispatchAsync(
                    target, new AddRdfTripleCommand(target.ResolvedFullPath, fromIri, OwlVocabulary.SubClassOf, toIri), cancellationToken);
            }

            case AddClassActionId or AddObjectPropertyActionId or AddDatatypePropertyActionId or AddIndividualActionId:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Add " + actionId["owl.add-".Length..].Replace('-', ' '),
                    "mdi-shape-circle-plus", "IRI or prefixed name", "", "Add"));

            case RenameResourceActionId when iri is not null:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Rename resource", "mdi-pencil-outline", "New IRI or prefixed name",
                    RdfWriter.Compress(entry.Model, iri), "Rename"));

            case RemoveResourceActionId when iri is not null:
            {
                // The action says how many statements go with it, before it runs - and an
                // untouched removal needs no ceremony, so a single statement removes HERE.
                var touching = RdfSelection.Touching(entry, iri).Count;
                if (touching <= 1)
                {
                    return await DispatchAsync(target, new RemoveRdfResourceCommand(target.ResolvedFullPath, iri), cancellationToken);
                }

                return new ContextExecutionRequiresConfirmation(new ContextConfirmationRequest(
                    "Remove resource",
                    "mdi-delete-outline",
                    $"Removing this resource also removes the {touching} statements it appears in.",
                    "Remove",
                    Danger: true));
            }

            case RemoveEdgeActionId when RdfSelection.EdgeOf(entry, target.ElementId) is { } edge:
                return await DispatchAsync(target, new RemoveRdfTripleCommand(
                    target.ResolvedFullPath,
                    ((IriTerm)edge.Subject).Iri,
                    edge.Predicate.Iri,
                    ((IriTerm)edge.Object).Iri), cancellationToken);

            case ConnectActionId when RdfRelationGesture.TryParse(target.ElementId, out var from, out var to):
            {
                if (RdfSelection.IsBlank(from) || RdfSelection.IsBlank(to))
                {
                    return new ContextExecutionFailed(
                        "A blank node's identity does not survive a reparse, so relations to it cannot land in the file. Name it with an IRI first.");
                }

                if (RdfNewPlacement.TryParse(from, out _, out _) || RdfNewPlacement.TryParse(to, out _, out _))
                {
                    return new ContextExecutionFailed("Drop the relation on a resource; a statement needs both of its ends.");
                }

                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Relate", "mdi-ray-start-arrow", "Predicate (IRI or prefixed name)", "", "Relate"));
            }

            case AddResourceActionId:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Add resource", "mdi-card-plus-outline", "IRI or prefixed name", "", "Add"));

            case AddPrefixActionId:
                return new ContextExecutionRequiresInput(new ContextInputRequest(
                    "Declare prefix", "mdi-at", "Declaration, e.g. ex: http://example.org/", "", "Declare"));

            default:
                return new ContextExecutionCompleted();
        }
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // The writers refuse on exactly these terms at commit; validating here lets the dialog
        // refuse first - an undeclared prefix by name, never silently invented (Requirement 5.7).
        if (Shacl.ShaclActions.Validate(_documents.GetOrLoad(target.ResolvedFullPath), actionId, value) is { } shaclValidation)
        {
            return ValueTask.FromResult(shaclValidation);
        }

        if (SkosActions.Validate(_documents.GetOrLoad(target.ResolvedFullPath), actionId, value) is { } skosValidation)
        {
            return ValueTask.FromResult(skosValidation);
        }

        if (actionId is RenameResourceActionId or ConnectActionId or AddResourceActionId
            || _owlDeclarationTypes.ContainsKey(actionId))
        {
            var entry = _documents.GetOrLoad(target.ResolvedFullPath);
            var (resolved, error) = RdfTermInput.Resolve(entry.Model, value);
            if (resolved is null)
            {
                return ValueTask.FromResult(ContextValidationResult.Rejected(error));
            }

            if (actionId == RenameResourceActionId
                && entry.Model.Triples.Any(t =>
                    (t.Subject is IriTerm s && s.Iri == resolved) || (t.Object is IriTerm o && o.Iri == resolved)))
            {
                return ValueTask.FromResult(ContextValidationResult.Rejected(
                    $"{resolved} already names something in this document; renaming onto it would silently merge two resources."));
            }
        }

        if (actionId == AddPrefixActionId && ParsePrefixDeclaration(value) is null)
        {
            return ValueTask.FromResult(ContextValidationResult.Rejected(
                "Write the declaration as 'prefix: iri', e.g. ex: http://example.org/."));
        }

        return ValueTask.FromResult(ContextValidationResult.Accepted);
    }

    /// <inheritdoc />
    public async ValueTask<ContextCommitResult> CommitAsync(
        ContextTarget target,
        string actionId,
        string value,
        string text,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = text;

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (RdfSelection.IsTruncated(entry))
        {
            return ContextCommitResult.Failed(RdfSelection.TruncatedRefusal);
        }

        var command = CommandFor(entry, target, actionId, value.Trim());
        if (command is null)
        {
            return ContextCommitResult.Failed($"'{actionId}' does not apply to this selection.");
        }

        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }

    private static ICommand? CommandFor(RdfDocumentEntry entry, ContextTarget target, string actionId, string value)
    {
        if (Shacl.ShaclActions.CommandFor(entry, target, actionId, value) is { } shaclCommand)
        {
            return shaclCommand;
        }

        if (SkosActions.CommandFor(entry, target, actionId, value) is { } skosCommand)
        {
            return skosCommand;
        }

        var body = target.ResolvedFullPath;
        var iri = RdfSelection.ResourceOf(entry, target.ElementId) ?? OwlSelection.IndividualIriOf(entry, target.ElementId);

        if (_owlDeclarationTypes.TryGetValue(actionId, out var declarationType))
        {
            // A new ontology term is one stated triple: itself, declared as what the palette
            // entry says it is (owl-diagram Requirement 6.2).
            var (declared, _) = RdfTermInput.Resolve(entry.Model, value);
            return declared is null ? null : new AddRdfTripleCommand(body, declared, RdfVocabulary.Type, declarationType);
        }

        switch (actionId)
        {
            case RenameResourceActionId when iri is not null:
            {
                var (resolved, _) = RdfTermInput.Resolve(entry.Model, value);
                return resolved is null ? null : new RenameRdfTermCommand(body, iri, resolved);
            }

            case RemoveResourceActionId when iri is not null:
                return new RemoveRdfResourceCommand(body, iri);

            case ConnectActionId when RdfRelationGesture.TryParse(target.ElementId, out var from, out var to):
            {
                var fromIri = RdfSelection.ResourceOf(entry, from);
                var toIri = RdfSelection.ResourceOf(entry, to);
                var (predicate, _) = RdfTermInput.Resolve(entry.Model, value);
                return fromIri is null || toIri is null || predicate is null
                    ? null
                    : new AddRdfTripleCommand(body, fromIri, predicate, toIri);
            }

            case AddResourceActionId:
            {
                var (resolved, _) = RdfTermInput.Resolve(entry.Model, value);
                // A new resource is one stated triple: itself, typed as the most general thing
                // there is. The authored drop position is the client's follow-up layout write.
                return resolved is null
                    ? null
                    : new AddRdfTripleCommand(body, resolved, RdfVocabulary.Type, RdfVocabulary.Resource);
            }

            case AddPrefixActionId:
            {
                var declaration = ParsePrefixDeclaration(value);
                return declaration is null ? null : new AddRdfPrefixCommand(body, declaration.Value.Prefix, declaration.Value.Iri);
            }

            default:
                return null;
        }
    }

    /// <summary>One line, two facts: <c>prefix: iri</c>, brackets tolerated.</summary>
    private static (string Prefix, string Iri)? ParsePrefixDeclaration(string value)
    {
        var colon = value.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        var prefix = value[..colon].Trim();
        var iri = value[(colon + 1)..].Trim().TrimStart('<').TrimEnd('>').Trim();
        return prefix.Length == 0 || iri.Length == 0 || prefix.Contains(' ') || iri.Contains(' ') || !RdfParser.IsAbsolute(iri)
            ? null
            : (prefix, iri);
    }

    private async ValueTask<ContextExecutionResult> DispatchAsync(ContextTarget target, ICommand command, CancellationToken cancellationToken)
    {
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess
            ? new ContextExecutionCompleted()
            : new ContextExecutionFailed(result.Error);
    }

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Result(IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);
}
