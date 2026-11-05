using EtAlii.Adp.Context;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// Offers <b>Add…</b> on any folder - the project root included - and carries the chosen
/// diagram type to the point where a file would be created.
/// </summary>
/// <remarks>
/// This spec stops at that point: <see cref="CommitAsync"/> re-checks the folder, resolves the
/// choice, and answers that creating the type is not supported yet. The checks before that
/// answer are deliberately ordered and pinned by tests, because the spec that fills in
/// creation (create-diagram-file) replaces only the final answer and inherits them.
/// <para>
/// The definitions come in through the constructor with all diagram definitions
/// as the default, so a test can hand the provider its own list instead of filling the
/// process-wide cache.
/// </para>
/// </remarks>
public sealed class AddDiagramContextActionProvider : IContextActionProvider
{
    public const string AddActionId = "hierarchy.add";
    private const string NoDiagramTypes = "No diagram types are available.";
    private const string NotRegistrable = "No diagram type reads this kind of file.";

    private static readonly ContextShortcutDefinition AddShortcut = new("Insert");
    private static readonly ILogger _logger = Log.ForContext<AddDiagramContextActionProvider>();

    private readonly IHistoryStackStore _historyStacks;
    private readonly DiagramAddOptions _diagrams;
    private readonly DesignerAddOptions? _designers;

    /// <param name="historyStacks">The history stacks where the create is sent; this provider writes nothing itself.</param>
    /// <param name="documentFactories">Where a type that keeps a body sibling gets that body's initial content.</param>
    /// <param name="catalog">
    /// The diagram types to offer, read at call time; Read lazily rather than captured, because the
    /// host fills the cache after the container is built.
    /// </param>
    /// <param name="router">
    /// How a registration already in a folder is recognised, for the one question this provider
    /// cannot answer from a name: whether a folder is already registered.
    /// <para>
    /// Required rather than optional deliberately. An optional router would have to degrade to
    /// "no folder is ever registered", which is not a safe default but a silent one - it turns
    /// the collision check into a check that always passes, and the second registration
    /// overwrites the first exactly where nobody is looking.
    /// </para>
    /// </param>
    /// <param name="designers">
    /// The designer family's entries of the same dialog, or null in a host without the family: a
    /// designer type is offered where the diagram types are, not under an action of its own.
    /// </param>
    public AddDiagramContextActionProvider(
        IHistoryStackStore historyStacks,
        DiagramDocumentFactories documentFactories,
        IDiagramDefinitionCatalog catalog,
        DiagramFileRouter router,
        DesignerAddOptions? designers = null)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documentFactories);
        ArgumentNullException.ThrowIfNull(router);
        _historyStacks = historyStacks;
        _diagrams = new DiagramAddOptions(catalog.All, documentFactories, router);
        _designers = designers;
    }

    /// <summary>Whether a folder has anything to add: a diagram type, or a designer type with a format.</summary>
    private bool HasTypes => _diagrams.Any || _designers is { Any: true };

    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        if (!HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        // On a file the action means something else: not "create a diagram inside this", which
        // is impossible, but "this file is a diagram of a type I am about to name", which writes
        // the .adp beside it (add-diagram-action Requirement 4.3, revised).
        (bool available, string label, string reason) = target.IsContainer
            ? (HasTypes, "Add…", NoDiagramTypes)
            : (_diagrams.RegistrableFor(target).Count > 0, "Add as diagram…", NotRegistrable);

        if (!target.IsContainer && !available)
        {
            // Nothing claims this extension, or it is already a diagram. Offering a disabled
            // action on every ordinary file in the tree would be noise, so it is not offered
            // at all - unlike the folder case, where an empty catalog is worth explaining.
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        var group = new ContextActionGroupDefinition([
            new ContextActionDefinition(
                AddActionId,
                label,
                "mdi-plus",
                AddShortcut,
                available,
                available ? "" : reason),
        ]);

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([group]);
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        if (actionId != AddActionId)
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed($"Unknown action '{actionId}'."));
        }

        if (!HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult<ContextExecutionResult>(
                new ContextExecutionFailed(target.IsContainer ? "The folder no longer exists." : "The file no longer exists."));
        }

        if (!target.IsContainer)
        {
            var registrable = _diagrams.RegistrableFor(target);
            if (registrable.Count == 0)
            {
                return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed(NotRegistrable));
            }

            // No name field: the file already has its name, and registering must not rename it.
            // No suggestion either, for the same reason - there is nothing to suggest.
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresChoice(
                new ContextChoiceRequest(
                    Title: $"Open {IoPath.GetFileName(target.ResolvedFullPath)} as",
                    Icon: "mdi-plus",
                    ConfirmLabel: "Register",
                    Options: DiagramOptionTree.Build(registrable),
                    EmptyMessage: NotRegistrable,
                    NameField: null)));
        }

        var options = _diagrams.Options(target);

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresChoice(
            new ContextChoiceRequest(
                Title: "Add diagram",
                Icon: "mdi-plus",
                ConfirmLabel: "Add",
                Options: _designers?.AddTo(options, target.ResolvedFullPath) ?? options,
                EmptyMessage: NoDiagramTypes,
                // The name field stays: a file-subject type still needs one, and serving both
                // kinds from one dialog is the point. A folder-subject option replaces the
                // field with its sentence rather than the dialog dropping it.
                NameField: new ContextTextFieldRequest(Label: "Name"))));
    }

    /// <summary>
    /// Judges the name being typed into the dialog. The extension is part of what is judged:
    /// the collision that matters is the file that would be created, not the base name. The
    /// choice itself needs no judging - an option id is either known or it is not, which
    /// <see cref="CommitAsync"/> decides.
    /// </summary>
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
        => ValueTask.FromResult(DiagramAddOptions.ValidateName(target, value));

    /// <summary>
    /// Creates what was chosen. In this order: the folder or file is re-checked (it may have
    /// vanished while the dialog was open), then the family the option belongs to plans the one
    /// command it comes down to - resolving the option and judging the name again, so a client
    /// that skipped validation cannot get past this. Only then is anything written, by that
    /// command through the history.
    /// </summary>
    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        if (actionId != AddActionId)
        {
            return ContextCommitResult.Failed($"Unknown action '{actionId}'.");
        }

        if (!HierarchyTargets.Exists(target))
        {
            return ContextCommitResult.Failed(target.IsContainer ? "The folder no longer exists." : "The file no longer exists.");
        }

        // One strategy per case (code-structure FR-071): registering a file that is there, a
        // designer's new document, or a diagram's - a new document or the folder itself.
        if (target.IsContainer && _designers?.Resolve(value) is { } designer)
        {
            return await CommitDesignerDocumentAsync(target, designer.Definition, designer.Format, text, cancellationToken);
        }

        (CreateDiagramFileCommand? command, string error) = !target.IsContainer
            ? _diagrams.PlanRegistration(target, value)
            : _diagrams.PlanInFolder(target, value, text);
        if (command is null)
        {
            return ContextCommitResult.Failed(error);
        }

        // The write is the command's; this provider only decides what to write and where. The
        // history is what makes the new file one undo away, by way of the delete the handler
        // reports as the inverse.
        var result = await _historyStacks.Get(target.Root).ExecuteAsync(command, cancellationToken);

        return result.IsSuccess
            // The destination is the command's own, not something the handler had to report:
            // where the file lands follows from the folder and the name it was given.
            ? ContextCommitResult.Created(CreateDiagramFileCommandHandler.DestinationOf(command))
            : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>
    /// A designer's document in the chosen format, as <see cref="DesignerAddOptions.Plan"/> decides it: written through
    /// the history, so one undo removes what was written, or a document that is already there opened as it is.
    /// </summary>
    private async ValueTask<ContextCommitResult> CommitDesignerDocumentAsync(
        ContextTarget target, DesignerDefinition definition, DesignerFormat format, string text, CancellationToken cancellationToken)
    {
        var validation = DiagramAddOptions.ValidateName(target, text);
        if (!validation.Valid)
        {
            _logger.Debug("Rejecting the name {Name} for a new {Origin}: {Reason}", text, definition.Origin, validation.Reason);
            return ContextCommitResult.Failed(validation.Reason);
        }

        var plan = _designers!.Plan(definition, format, target.ResolvedFullPath, DiagramFileName.StripExtension(text));
        if (plan.OpensExisting)
        {
            _logger.Debug("Opening the {Origin} document {Path} that is already there", definition.Origin, plan.Destination);
            return ContextCommitResult.Created(plan.Destination);
        }

        if (plan.Command is null)
        {
            return ContextCommitResult.Failed(plan.Error);
        }

        _logger.Debug("Creating a {Origin} document at {Path}", definition.Origin, plan.Destination);
        var result = await _historyStacks.Get(target.Root).ExecuteAsync(plan.Command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Created(plan.Destination) : ContextCommitResult.Failed(result.Error);
    }
}
