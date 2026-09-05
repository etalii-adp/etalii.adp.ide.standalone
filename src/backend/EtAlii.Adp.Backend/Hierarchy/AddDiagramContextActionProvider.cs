using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Diagram;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

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

    private readonly IReadOnlyList<DiagramDefinition> _definitions;
    private readonly IHistoryStackStore _historyStacks;
    private readonly DiagramDocumentFactories _documentFactories;

    /// <param name="historyStacks">The history stacks where the create is sent; this provider writes nothing itself.</param>
    /// <param name="documentFactories">Where a type that keeps a body sibling gets that body's initial content.</param>
    /// <param name="catalog">
    /// The diagram types to offer, read at call time; Read lazily rather than captured, because the
    /// host fills the cache after the container is built.
    /// </param>
    public AddDiagramContextActionProvider(
        IHistoryStackStore historyStacks,
        DiagramDocumentFactories documentFactories,
        IDiagramDefinitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documentFactories);
        _historyStacks = historyStacks;
        _documentFactories = documentFactories;
        _definitions = catalog.All;
    }

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
        var (available, label, reason) = target.IsContainer
            ? (_definitions.Count > 0, "Add…", NoDiagramTypes)
            : (RegistrableTypesFor(target).Count > 0, "Add as diagram…", NotRegistrable);

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

    /// <summary>
    /// The diagram types that could claim <paramref name="target"/>, which is a file: those
    /// declaring its extension. Empty when nothing declares it, or when an <c>.adp</c> already
    /// sits beside it - a file that is already a diagram is opened, not registered again.
    /// </summary>
    private IReadOnlyList<DiagramDefinition> RegistrableTypesFor(ContextTarget target)
    {
        var extension = IoPath.GetExtension(target.ResolvedFullPath);
        if (extension.Length == 0 || string.Equals(extension, DiagramFileName.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (File.Exists(IoPath.ChangeExtension(target.ResolvedFullPath, DiagramFileName.Extension)))
        {
            return [];
        }

        return
        [
            .. _definitions
                .Where(definition => string.Equals(definition.Extension, extension, StringComparison.OrdinalIgnoreCase))
                .Where(definition => SuggestsFor(definition, target)),
        ];
    }

    /// <summary>
    /// Whether <paramref name="definition"/> should be offered for this file: yes by default,
    /// and by its own marker test where it declares one - a family's alternative reading is
    /// suggested exactly on the bodies that carry its marker. A file that cannot be read right
    /// now is simply not suggested for such a reading; nothing fails.
    /// </summary>
    private static bool SuggestsFor(DiagramDefinition definition, ContextTarget target)
    {
        if (definition.SuggestsBody is null)
        {
            return true;
        }

        try
        {
            // Shared, for the reason RegistrationLayout's read is: this runs while the user
            // browses, over files ADP may be publishing at that moment, and a read sharing only
            // Read denies that publish.
            return definition.SuggestsBody(SharedDocumentReader.ReadAllText(target.ResolvedFullPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
            var registrable = RegistrableTypesFor(target);
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

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresChoice(
            new ContextChoiceRequest(
                Title: "Add diagram",
                Icon: "mdi-plus",
                ConfirmLabel: "Add",
                Options: DiagramOptionTree.Build(_definitions, origin => DiagramFileName.Suggest(origin, target.ResolvedFullPath)),
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

        if (!HierarchyTargets.Exists(target))
        {
            return ContextCommitResult.Failed(target.IsContainer ? "The folder no longer exists." : "The file no longer exists.");
        }

        if (!target.IsContainer)
        {
            return await RegisterAsync(target, value, cancellationToken);
        }


        var definition = _definitions.FirstOrDefault(candidate => candidate.Origin.Key == value);
        if (definition is null)
        {
            // The client offered an option that is not on the list it was given - a stale
            // dialog, or a client built against a different set of modules.
            _logger.Warning("Rejecting the Add: {OptionId} is not one of the {Count} diagram types on offer", value, _definitions.Count);
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

        // A type that keeps its body in a sibling file gets that body from its own factory,
        // written by the same command so both files appear or neither does. The factory is
        // resolved here rather than in the handler so the command stays plain data a redo can
        // replay, and so the handler never looks a diagram type up.
        var siblingFileName = "";
        var siblingContent = "";
        if (definition.HasDocumentSibling)
        {
            var factory = _documentFactories.Find(definition.Origin);
            if (factory is null)
            {
                // The host's startup check makes this unreachable in a consistent deployment;
                // answered anyway, because a silent .adp without its body is worse than a refusal.
                _logger.Error("No IDiagramDocumentFactory is registered for {Origin}, which declares {Extension}", definition.Origin.Key, definition.Extension);
                return ContextCommitResult.Failed("This diagram type cannot be created: its module is incomplete.");
            }

            var baseName = DiagramFileName.StripExtension(fileName);
            siblingFileName = baseName + definition.Extension;
            siblingContent = factory.CreateEmptyDocument(baseName);

            var siblingPath = IoPath.Combine(target.ResolvedFullPath, siblingFileName);
            if (File.Exists(siblingPath) || Directory.Exists(siblingPath))
            {
                return ContextCommitResult.Failed($"An item named '{siblingFileName}' already exists in this folder.");
            }
        }

        // The write is the command's; this provider only decides what to write and where. The
        // history is what makes the new file one undo away, by way of the delete the handler
        // reports as the inverse.
        var command = new CreateDiagramFileCommand(target.ResolvedFullPath, fileName, definition.Origin.MimeType, siblingFileName, siblingContent);
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);

        return result.IsSuccess
            // The destination is the command's own, not something the handler had to report:
            // where the file lands follows from the folder and the name it was given.
            ? ContextCommitResult.Created(CreateDiagramFileCommandHandler.DestinationOf(command))
            : ContextCommitResult.Failed(result.Error);
    }

    /// <summary>
    /// Registers a file the repository already has: writes the <c>.adp</c> beside it naming the
    /// chosen type, and touches the file itself not at all.
    /// </summary>
    /// <remarks>
    /// The same <see cref="CreateDiagramFileCommand"/> the folder path uses, with no sibling -
    /// because the body is the file being registered, and it is already there. That is the whole
    /// difference between creating a diagram and registering one, which is why this is a branch
    /// rather than a second command: undo removes the registration and leaves the pipeline alone.
    /// </remarks>
    private async ValueTask<ContextCommitResult> RegisterAsync(ContextTarget target, string optionId, CancellationToken cancellationToken)
    {
        var registrable = RegistrableTypesFor(target);
        var definition = registrable.FirstOrDefault(candidate => candidate.Origin.Key == optionId);
        if (definition is null)
        {
            // Either a stale dialog, or one built against a different set of modules. Checked
            // against the registrable list rather than every definition, so a type that does not
            // read this kind of file cannot be chosen for it.
            _logger.Warning(
                "Rejecting the registration: {OptionId} is not one of the {Count} types that read {Extension}",
                optionId,
                registrable.Count,
                IoPath.GetExtension(target.ResolvedFullPath));
            return ContextCommitResult.Failed("That diagram type is not available for this file.");
        }

        var folder = IoPath.GetDirectoryName(target.ResolvedFullPath) ?? "";
        var fileName = DiagramFileName.WithExtension(IoPath.GetFileNameWithoutExtension(target.ResolvedFullPath));
        _logger.Debug("Registering {File} as a {Origin} diagram", target.ResolvedFullPath, definition.Origin.Key);

        var command = new CreateDiagramFileCommand(folder, fileName, definition.Origin.MimeType);
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);

        return result.IsSuccess
            ? ContextCommitResult.Created(CreateDiagramFileCommandHandler.DestinationOf(command))
            : ContextCommitResult.Failed(result.Error);
    }
}
