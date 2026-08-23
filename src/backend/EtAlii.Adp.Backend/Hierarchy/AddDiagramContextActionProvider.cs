using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Diagram;
using Serilog;

namespace EtAlii.Adp.Backend.Hierarchy;

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
/// The definitions come in through the constructor with <see cref="DiagramDefinition.All"/>
/// as the default, so a test can hand the provider its own list instead of filling the
/// process-wide cache.
/// </para>
/// </remarks>
public sealed class AddDiagramContextActionProvider : IContextActionProvider
{
    public const string AddActionId = "hierarchy.add";
    private const string NoDiagramTypes = "No diagram types are available.";
    private static readonly ContextShortcutDefinition AddShortcut = new("Insert");
    private static readonly ILogger _logger = Log.ForContext<AddDiagramContextActionProvider>();

    private readonly Func<IReadOnlyList<DiagramDefinition>> _definitions;
    private readonly IHistoryStack _history;

    /// <param name="history">Where the create is sent; this provider writes nothing itself.</param>
    public AddDiagramContextActionProvider(IHistoryStack history)
        : this(history, null)
    {
    }

    /// <param name="history">Where the create is sent; this provider writes nothing itself.</param>
    /// <param name="definitions">
    /// The diagram types to offer, read at call time; <c>null</c> means
    /// <see cref="DiagramDefinition.All"/>. Read lazily rather than captured, because the
    /// host fills the cache after the container is built.
    /// </param>
    public AddDiagramContextActionProvider(IHistoryStack history, IReadOnlyList<DiagramDefinition>? definitions)
    {
        ArgumentNullException.ThrowIfNull(history);
        _history = history;
        _definitions = definitions is null ? () => DiagramDefinition.All : () => definitions;
    }

    public ContextScope Scope => ContextScope.Hierarchy;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        // A file cannot contain a new entry, and a folder that is gone cannot either.
        if (!target.IsContainer || !HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([]);
        }

        var available = _definitions().Count > 0;
        var group = new ContextActionGroupDefinition([
            new ContextActionDefinition(
                AddActionId,
                "Add…",
                "mdi-plus",
                AddShortcut,
                available,
                available ? "" : NoDiagramTypes),
        ]);

        return ValueTask.FromResult<IReadOnlyList<ContextActionGroupDefinition>>([group]);
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        if (actionId != AddActionId)
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionResult.Failed($"Unknown action '{actionId}'."));
        }

        if (!target.IsContainer || !HierarchyTargets.Exists(target))
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionResult.Failed("The folder no longer exists."));
        }

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionResult.RequiresChoice(
            new ContextChoiceRequest(
                Title: "Add diagram",
                Icon: "mdi-plus",
                ConfirmLabel: "Add",
                Options: DiagramOptionTree.Build(_definitions(), origin => DiagramFileName.Suggest(origin, target.ResolvedFullPath)),
                EmptyMessage: NoDiagramTypes,
                NameField: new ContextTextFieldRequest(Label: "Name"))));
    }

    /// <summary>
    /// Judges the name being typed into the dialog. The extension is part of what is judged:
    /// the collision that matters is the file that would be created, not the base name. The
    /// choice itself needs no judging - an option id is either known or it is not, which
    /// <see cref="CommitAsync"/> decides.
    /// </summary>
    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
        => ValueTask.FromResult(ValidateName(target, value));

    private static ContextValidationResult ValidateName(ContextTarget target, string name)
    {
        // Judged before the extension is added: without this, an empty name would become the
        // perfectly valid file ".adp" instead of being refused.
        if (DiagramFileName.StripExtension(name).Trim().Length == 0)
        {
            return ContextValidationResult.Rejected("Enter a name.");
        }

        return EntryNameRules.Validate(DiagramFileName.WithExtension(name), target.ResolvedFullPath);
    }

    /// <summary>
    /// Creates the diagram. In this order: the folder is re-checked (it may have vanished while
    /// the dialog was open), the option id is resolved to a definition, and the name is judged
    /// again - a client that skipped validation cannot get past this. Only then is anything
    /// written, and what is written is one line: the chosen type's MIME type.
    /// </summary>
    public async ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        if (actionId != AddActionId)
        {
            return ContextCommitResult.Failed($"Unknown action '{actionId}'.");
        }

        if (!target.IsContainer || !HierarchyTargets.Exists(target))
        {
            return ContextCommitResult.Failed("The folder no longer exists.");
        }

        var definition = _definitions().FirstOrDefault(candidate => candidate.Origin.Key == value);
        if (definition is null)
        {
            // The client offered an option that is not on the list it was given - a stale
            // dialog, or a client built against a different set of modules.
            _logger.Warning("Rejecting the Add: {OptionId} is not one of the {Count} diagram types on offer", value, _definitions().Count);
            return ContextCommitResult.Failed("That diagram type is not available.");
        }

        var validation = ValidateName(target, text);
        if (!validation.Valid)
        {
            _logger.Debug("Rejecting the name {Name} for a new {Origin}: {Reason}", text, definition.Origin.Key, validation.Reason);
            return ContextCommitResult.Failed(validation.Reason);
        }

        var fileName = DiagramFileName.WithExtension(text);
        _logger.Debug("Creating a {Origin} diagram named {FileName} in {Folder}", definition.Origin.Key, fileName, target.ResolvedFullPath);

        // The write is the command's; this provider only decides what to write and where. The
        // history is what makes the new file one undo away, by way of the delete the handler
        // reports as the inverse.
        var command = new CreateDiagramFileCommand(target.ResolvedFullPath, fileName, definition.Origin.MimeType);
        var result = await _history.ExecuteAsync(command, cancellationToken);

        return result.IsSuccess
            // The destination is the command's own, not something the handler had to report:
            // where the file lands follows from the folder and the name it was given.
            ? ContextCommitResult.Created(CreateDiagramFileCommandHandler.DestinationOf(command))
            : ContextCommitResult.Failed(result.Error);
    }
}
