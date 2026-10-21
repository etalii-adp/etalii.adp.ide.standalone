using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The diagram family's part of the Add dialog: the diagram types offered for a folder or a file,
/// and the command each choice comes down to - a new document, a registration of the folder
/// itself, or a registration of a file that is already there (code-structure FR-071).
/// </summary>
/// <remarks>
/// Not a context action provider of its own, for the reason <see cref="DesignerAddOptions"/> is
/// not: <see cref="AddDiagramContextActionProvider"/> owns <c>hierarchy.add</c> and asks each
/// family for its options and for what a chosen one creates. Every plan here is a
/// <see cref="CreateDiagramFileCommand"/> or the reason there is none; nothing is written until
/// the provider sends the command to the history.
/// </remarks>
internal sealed class DiagramAddOptions
{
    private const string FolderSubjectSuppression =
        "This type registers the folder itself, so the registration is created as `.adp` inside it.";

    private static readonly ILogger _logger = Log.ForContext<DiagramAddOptions>();

    private readonly IReadOnlyList<DiagramDefinition> _definitions;
    private readonly DiagramDocumentFactories _documentFactories;
    private readonly DiagramFileRouter _router;

    /// <param name="definitions">The diagram types to offer.</param>
    /// <param name="documentFactories">Where a type that keeps a body sibling gets that body's initial content.</param>
    /// <param name="router">How a registration already in a folder is recognised.</param>
    public DiagramAddOptions(IReadOnlyList<DiagramDefinition> definitions, DiagramDocumentFactories documentFactories, DiagramFileRouter router)
    {
        _definitions = definitions;
        _documentFactories = documentFactories;
        _router = router;
    }

    /// <summary>Whether any diagram type can be added.</summary>
    public bool Any => _definitions.Count > 0;

    /// <summary>
    /// The diagram types that could claim <paramref name="target"/>, which is a file: those
    /// declaring its extension, the alternate a second serialization of the format uses included.
    /// Empty when nothing declares it, or when an <c>.adp</c> already sits beside it - a file that
    /// is already a diagram is opened, not registered again.
    /// </summary>
    public IReadOnlyList<DiagramDefinition> RegistrableFor(ContextTarget target)
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
                .Where(definition => definition.DeclaresExtension(extension))
                .Where(definition => SuggestsFor(definition, target)),
        ];
    }

    /// <summary>
    /// The option tree of the diagram types for <paramref name="target"/>, a folder: a file-subject
    /// type with the name it suggests, a folder-subject type with the sentence that stands in for
    /// the name, greyed when the folder is already registered.
    /// </summary>
    public IReadOnlyList<ContextOptionNode> Options(ContextTarget target)
    {
        // Read once for the whole tree rather than per option: every folder-subject type asks
        // the same question of the same folder, and the answer cannot change while one prompt
        // is being built.
        var registeredBy = ExistingFolderRegistration(target);
        return DiagramOptionTree.Build(
            _definitions,
            definition => Annotate(definition, target, registeredBy));
    }

    /// <summary>
    /// The command that creates what <paramref name="optionId"/> names in <paramref name="target"/>,
    /// a folder: the registration of the folder itself for a folder-subject type, a new document
    /// named <paramref name="name"/> otherwise. Null with a reason when it cannot be created.
    /// </summary>
    public (CreateDiagramFileCommand? Command, string Error) PlanInFolder(ContextTarget target, string optionId, string name)
    {
        var definition = _definitions.FirstOrDefault(candidate => candidate.Origin.Key == optionId);
        if (definition is null)
        {
            // The client offered an option that is not on the list it was given - a stale
            // dialog, or a client built against a different set of modules.
            _logger.Warning("Rejecting the Add: {OptionId} is not one of the {Count} diagram types on offer", optionId, _definitions.Count);
            return (null, "That diagram type is not available.");
        }

        return definition.HasFolderSubject
            ? PlanFolderSubject(target, definition, name)
            : PlanNewDocument(target, definition, name);
    }

    /// <summary>
    /// The command that registers <paramref name="target"/>, a file the repository already has:
    /// the <c>.adp</c> beside it naming the chosen type, the file itself untouched. Null with a
    /// reason when <paramref name="optionId"/> names no type that reads the file.
    /// </summary>
    /// <remarks>
    /// The same <see cref="CreateDiagramFileCommand"/> a new document is created by, with no
    /// sibling - because the body is the file being registered, and it is already there. That is
    /// the whole difference between creating a diagram and registering one: undo removes the
    /// registration and leaves the file alone.
    /// </remarks>
    public (CreateDiagramFileCommand? Command, string Error) PlanRegistration(ContextTarget target, string optionId)
    {
        var registrable = RegistrableFor(target);
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
            return (null, "That diagram type is not available for this file.");
        }

        var folder = IoPath.GetDirectoryName(target.ResolvedFullPath) ?? "";
        var fileName = DiagramFileName.WithExtension(IoPath.GetFileNameWithoutExtension(target.ResolvedFullPath));
        _logger.Debug("Registering {File} as a {Origin} diagram", target.ResolvedFullPath, definition.Origin.Key);

        // A registration without a body: header opens the file its own name derives, which is the
        // file being registered unless another serialization of the same name sits beside it - a
        // body of the alternate extension beside one of the primary. Then it names its body, so
        // what was registered is what opens.
        var derived = DiagramFilePair.SiblingPathFor(IoPath.Combine(folder, fileName), definition);
        var namedBody = string.Equals(IoPath.GetFullPath(derived), IoPath.GetFullPath(target.ResolvedFullPath), StringComparison.Ordinal)
            ? ""
            : IoPath.GetFileName(target.ResolvedFullPath);

        return (new CreateDiagramFileCommand(folder, fileName, definition.Origin.MimeType, NamedBody: namedBody), "");
    }

    /// <summary>
    /// Judges a name for a new document. The extension is part of what is judged: the collision
    /// that matters is the file that would be created, not the base name.
    /// </summary>
    public static ContextValidationResult ValidateName(ContextTarget target, string name)
    {
        return DiagramFileName.StripExtension(name).Trim().Length == 0
            // Judged before the extension is added: without this, an empty name would become the
            // perfectly valid file ".adp" instead of being refused.
            ? ContextValidationResult.Rejected("Enter a name.")
            : EntryNameRules.Validate(DiagramFileName.WithExtension(name), target.ResolvedFullPath);
    }

    /// <summary>The registration of the folder itself, as <c>.adp</c> inside it, unless one is there.</summary>
    private (CreateDiagramFileCommand? Command, string Error) PlanFolderSubject(ContextTarget target, DiagramDefinition definition, string name)
    {
        // The folder is the diagram, so there is no name to take. A submitted one means a
        // stale client rather than a user's intent: ignored rather than obeyed, and logged
        // so that a client still sending one is visible rather than silently tolerated.
        if (name.Trim().Length > 0)
        {
            _logger.Warning(
                "Ignoring the name {Name} submitted for {Origin}, which registers the folder itself",
                name,
                definition.Origin.Key);
        }

        // ValidateName is skipped rather than called with an empty string. It rejects an
        // empty base name precisely so that nothing creates a file called ".adp" - which is
        // the file this path creates on purpose, so the guard would refuse the one case it
        // was written to prevent everywhere else.
        var registeredBy = ExistingFolderRegistration(target);
        if (registeredBy is not null)
        {
            // Re-consulted here rather than trusted from the prompt: the dialog may have
            // been open while another registration landed, and overwriting one is the
            // outcome worth spending a second read to avoid.
            _logger.Debug(
                "Refusing a {Origin} registration for {Folder}, already registered by {Existing}",
                definition.Origin.Key,
                target.ResolvedFullPath,
                registeredBy);
            return (null, $"This folder is already registered by '{registeredBy}'.");
        }

        _logger.Debug("Creating a {Origin} diagram named {FileName} in {Folder}", definition.Origin.Key, DiagramFileName.Extension, target.ResolvedFullPath);
        return (Created(target, definition, DiagramFileName.Extension, "", ""), "");
    }

    /// <summary>
    /// A new document named <paramref name="name"/>, with the body sibling its type keeps from the
    /// type's own factory, written by the same command so both files appear or neither does.
    /// </summary>
    private (CreateDiagramFileCommand? Command, string Error) PlanNewDocument(ContextTarget target, DiagramDefinition definition, string name)
    {
        var validation = ValidateName(target, name);
        if (!validation.Valid)
        {
            _logger.Debug("Rejecting the name {Name} for a new {Origin}: {Reason}", name, definition.Origin.Key, validation.Reason);
            return (null, validation.Reason);
        }

        var fileName = DiagramFileName.WithExtension(name);
        _logger.Debug("Creating a {Origin} diagram named {FileName} in {Folder}", definition.Origin.Key, fileName, target.ResolvedFullPath);
        if (!definition.HasDocumentSibling)
        {
            return (Created(target, definition, fileName, "", ""), "");
        }

        // The factory is resolved here rather than in the handler so the command stays plain
        // data a redo can replay, and so the handler never looks a diagram type up.
        var factory = _documentFactories.Find(definition.Origin);
        if (factory is null)
        {
            // The host's startup check makes this unreachable in a consistent deployment;
            // answered anyway, because a silent .adp without its body is worse than a refusal.
            _logger.Error("No IDiagramDocumentFactory is registered for {Origin}, which declares {Extension}", definition.Origin.Key, definition.Extension);
            return (null, "This diagram type cannot be created: its module is incomplete.");
        }

        var baseName = DiagramFileName.StripExtension(fileName);
        var siblingFileName = baseName + definition.Extension;

        // Nothing is ever written over: not the body's own name, and not the alternate serialization
        // of it either, beside which a registration without a body: header would open the new file
        // rather than the one that was there.
        string[] names = definition.AlternateExtension.Length > 0 ? [siblingFileName, baseName + definition.AlternateExtension] : [siblingFileName];
        foreach (var taken in names)
        {
            var takenPath = IoPath.Combine(target.ResolvedFullPath, taken);
            if (File.Exists(takenPath) || Directory.Exists(takenPath))
            {
                return (null, $"An item named '{taken}' already exists in this folder.");
            }
        }

        var siblingContent = factory.CreateEmptyDocument(baseName);

        return (Created(target, definition, fileName, siblingFileName, siblingContent), "");
    }

    /// <summary>The command that writes the registration <paramref name="fileName"/>, and the body sibling where there is one.</summary>
    private static CreateDiagramFileCommand Created(ContextTarget target, DiagramDefinition definition, string fileName, string siblingFileName, string siblingContent) =>
        new(target.ResolvedFullPath, fileName, definition.Origin.MimeType, siblingFileName, siblingContent);

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

    /// <summary>
    /// What this family has to say about one diagram type, offered for one folder. A
    /// folder-subject type wants no name and says why; if the folder already holds a
    /// registration it is also shown greyed, naming the file so the user can go and look at it.
    /// </summary>
    private static ContextOptionAnnotations Annotate(
        DiagramDefinition definition,
        ContextTarget target,
        string? registeredBy)
    {
        if (!definition.HasFolderSubject)
        {
            return new ContextOptionAnnotations(
                SuggestedValue: DiagramFileName.Suggest(definition.Origin, target.ResolvedFullPath));
        }

        return new ContextOptionAnnotations(
            NameSuppressedReason: FolderSubjectSuppression,
            UnavailableReason: registeredBy is null ? "" : $"This folder is already registered by `{registeredBy}`");
    }

    /// <summary>
    /// The name of the registration this folder is already registered by, or null. Both shapes
    /// count - a bare <c>.adp</c> and a legacy named one alike - because what makes a folder
    /// registered is the type its registration routes to, never the file's name.
    /// </summary>
    /// <remarks>
    /// The catalog probe comes first so a deployment with no folder-subject type pays no disk
    /// read at all: with none deployed there is nothing a registration could route to.
    /// </remarks>
    private string? ExistingFolderRegistration(ContextTarget target)
    {
        if (!_router.HasFolderSubjectTypes)
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(target.ResolvedFullPath))
        {
            if (DiagramFilePair.IsRegistrationFile(path)
                && _router.Route(path, target.Root.FullPath) is DiagramRouted { Definition.HasFolderSubject: true })
            {
                return IoPath.GetFileName(path);
            }
        }

        return null;
    }
}
