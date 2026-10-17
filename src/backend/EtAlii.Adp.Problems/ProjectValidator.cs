using System.Collections.Concurrent;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Hierarchy;
using Serilog;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

/// <summary>
/// Walks a <see cref="ValidationScope"/> and reports everything wrong within it: what the
/// router cannot place (core problems) and what each type's own validator finds (module
/// problems). Never trusts a module - a validator that throws or hangs costs its own file's
/// results and nothing more - and never writes a file (Requirement 6.7).
/// </summary>
/// <remarks>
/// One running validation per project root: a second request for the same root joins the
/// running one rather than starting a second traversal or refusing (Requirement 6.5,
/// design deviation 2) - the user asked for a fresh answer and gets the one being computed.
/// </remarks>
public sealed class ProjectValidator
{
    private static readonly ILogger _logger = Log.ForContext<ProjectValidator>();

    /// <summary>How long one validator may take for one file before its verdict is abandoned (Requirement 3.5).</summary>
    private static readonly TimeSpan DefaultValidatorTimeout = TimeSpan.FromSeconds(10);

    private readonly DiagramFileRouter _router;
    private readonly DiagramValidators _validators;
    private readonly ToolRouter _tools;
    private readonly TimeSpan _validatorTimeout;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<ProjectRoot, Task<ValidationOutcome>> _running = new();

    /// <param name="router">Says which diagram type a file is.</param>
    /// <param name="validators">The rules, by origin - a diagram type's or a designer type's alike.</param>
    /// <param name="validatorTimeout">How long one validator may take for one file.</param>
    /// <param name="tools">
    /// Says which tool serves a file, so a designer's document is judged as the designer's
    /// (knowledge-designer Requirement 10.2) by the one precedence every caller shares
    /// (code-structure FR-058). Null in a host without the designer and editor families, where
    /// only the diagram types are asked.
    /// </param>
    /// <param name="time">The clock a validator's time runs out on; the system's when not given.</param>
    public ProjectValidator(
        DiagramFileRouter router,
        DiagramValidators validators,
        TimeSpan? validatorTimeout = null,
        ToolRouter? tools = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(validators);
        _router = router;
        _validators = validators;
        _validatorTimeout = validatorTimeout ?? DefaultValidatorTimeout;
        _tools = tools ?? new ToolRouter(router, new DesignerFileRouter(new DesignerDefinitionCatalog { All = [] }), new EditorResolver(new EditorDefinitionCatalog { All = [] }));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Whether a validation is running for <paramref name="rootPath"/> right now - what a
    /// "Validate all" button reads to grey itself while one is in flight (Requirement 6.5).
    /// </summary>
    public bool IsValidating(string rootPath) => _running.ContainsKey(ProjectRoot.From(rootPath));

    public async ValueTask<ValidationOutcome> ValidateAsync(ValidationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var key = ProjectRoot.From(scope.RootPath);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_running.TryGetValue(key, out var running))
            {
                // Join the run in flight - one traversal, every asker gets its answer.
                return await running.WaitAsync(cancellationToken);
            }

            var self = new TaskCompletionSource<ValidationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_running.TryAdd(key, self.Task))
            {
                continue; // Someone else just started one; join it on the next pass.
            }

            try
            {
                var outcome = await RunAsync(scope, cancellationToken);
                self.SetResult(outcome);
                return outcome;
            }
            catch (Exception exception)
            {
                self.SetException(exception);
                throw;
            }
            finally
            {
                _running.TryRemove(key, out _);
            }
        }
    }

    private async ValueTask<ValidationOutcome> RunAsync(ValidationScope scope, CancellationToken cancellationToken)
    {
        var root = IoPath.GetFullPath(scope.RootPath);
        var collector = new ProblemCollector(root);

        switch (scope)
        {
            case FileValidationScope file:
            {
                var full = IoPath.GetFullPath(IoPath.Combine(root, file.RelativePath));
                if (!IsInside(root, full) || !File.Exists(full))
                {
                    // A path that escapes the root resolves to nothing (Requirement 2.5).
                    _logger.Warning("Not validating {Path}: outside the project root or gone", file.RelativePath);
                    collector.Skipped++;
                    break;
                }
                await ConsiderFileAsync(full, collector, cancellationToken);
                break;
            }

            case FolderValidationScope folder:
            {
                var full = IoPath.GetFullPath(IoPath.Combine(root, folder.RelativePath));
                if (!IsInside(root, full) || !Directory.Exists(full))
                {
                    _logger.Warning("Not validating {Path}: outside the project root or gone", folder.RelativePath);
                    collector.Skipped++;
                    break;
                }
                await WalkFolderAsync(full, collector, cancellationToken);
                break;
            }

            case ProjectValidationScope:
                await WalkFolderAsync(root, collector, cancellationToken);
                break;
        }

        return new ValidationOutcome(collector.Problems, collector.FilesConsidered, collector.Skipped);
    }

    private async ValueTask WalkFolderAsync(string folder, ProblemCollector collector, CancellationToken cancellationToken)
    {
        if (!collector.MarkFolderVisited(folder))
        {
            return; // A reparse point led back here - once is enough.
        }

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(folder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The folder keeps its secrets; its siblings still get walked (Requirement 2.6).
            collector.AddCore(folder, $"The folder could not be read: {exception.Message}");
            collector.Skipped++;
            return;
        }

        Array.Sort(entries, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(entry))
            {
                if (LeavesRoot(collector.Root, entry))
                {
                    // A reparse point pointing outside the project is not part of it (Requirement 2.5).
                    _logger.Warning("Not entering {Path}: it leaves the project root", entry);
                    collector.Skipped++;
                    continue;
                }
                await WalkFolderAsync(entry, collector, cancellationToken);
            }
            else
            {
                await ConsiderFileAsync(entry, collector, cancellationToken);
            }
        }
    }

    private async ValueTask ConsiderFileAsync(string path, ProblemCollector collector, CancellationToken cancellationToken)
    {
        // Routed against the project root, so a registration that names its document through a
        // `body:` header resolves to that document. Without the root the router cannot follow
        // the header, and several diagrams over one model - the reason C4 projects are written
        // the way they are - would each arrive here with nowhere to read.
        switch (_tools.Route(path, collector.Root))
        {
            case DiagramRouted { BodyPath: null }:
                // The header names something outside the project, or names nothing resolvable.
                // Refused rather than followed (Requirement 2.4), and counted as skipped so the
                // run says so rather than quietly judging one file fewer.
                _logger.Warning("Not validating {Path}: its body: header does not resolve inside the project", path);
                collector.Skipped++;
                return;

            case DiagramRouted { BodyPath: { } bodyPath } routed:
                // A pair routes identically through either of its files - judge it once.
                if (!collector.MarkConsidered(bodyPath))
                {
                    return;
                }
                collector.FilesConsidered++;
                await ValidateDocumentAsync(
                    routed.Definition.Origin,
                    bodyPath,
                    routed.RegistrationPath,
                    routed.Definition.HasFolderSubject,
                    "diagram",
                    collector,
                    cancellationToken);
                return;

            case DesignerRouted designer:
                await ConsiderDesignerFileAsync(designer, collector, cancellationToken);
                return;

            case ToolUnreadable unreadable:
                if (!collector.MarkConsidered(unreadable.Path))
                {
                    return;
                }
                collector.FilesConsidered++;
                collector.AddCore(unreadable.Path, unreadable.Reason);
                return;

            default:
                // The editors': no diagram or designer type serves it. What the diagram types
                // still have to say about it - a registration of a type nothing deployed knows,
                // an extension two of them claim - is reported; anything else is not ours to
                // judge (Requirement 2.3).
                ConsiderUnservedFile(_router.Route(path, collector.Root), collector);
                return;
        }
    }

    /// <summary>What the diagram types say of a file only an editor serves.</summary>
    private static void ConsiderUnservedFile(DiagramRouting routing, ProblemCollector collector)
    {
        switch (routing)
        {
            case DiagramUnknownType unknown:
                if (!collector.MarkConsidered(unknown.Path))
                {
                    return;
                }
                collector.FilesConsidered++;
                collector.AddCore(unknown.Path, $"'{unknown.MimeType}' is not a known diagram type.", CoreRuleIds.UnknownType);
                return;

            case DiagramAmbiguousExtension ambiguous:
                if (!collector.MarkConsidered(ambiguous.Path))
                {
                    return;
                }
                collector.FilesConsidered++;
                collector.AddCore(
                    ambiguous.Path,
                    $"'{ambiguous.Extension}' is claimed by more than one diagram type: " +
                    $"{string.Join(", ", ambiguous.Claimants.Select(claimant => claimant.Origin.Key))}.",
                    CoreRuleIds.AmbiguousExtension);
                return;
        }
    }

    /// <summary>A file a designer type serves: its document, judged by that type's rules.</summary>
    private async ValueTask ConsiderDesignerFileAsync(DesignerRouted designer, ProblemCollector collector, CancellationToken cancellationToken)
    {
        if (designer.BodyPath is not { } bodyPath)
        {
            // A designer's document is two files, and this one's body is not there: said
            // against the registration, which is the file that names it.
            if (collector.MarkConsidered(designer.RegistrationPath))
            {
                collector.FilesConsidered++;
                collector.AddCore(designer.RegistrationPath, "The file this registration names does not exist.");
            }
            return;
        }

        // The pair routes identically through either of its files - judge it once.
        if (!collector.MarkConsidered(bodyPath))
        {
            return;
        }
        collector.FilesConsidered++;
        if (designer.Definition.ToolOrigin is not { } origin)
        {
            return; // An origin that is not vendor/type can have registered no rules.
        }
        await ValidateDocumentAsync(origin, bodyPath, designer.RegistrationPath, hasFolderSubject: false, "document", collector, cancellationToken);
    }

    /// <param name="origin">The type whose rules judge the document.</param>
    /// <param name="bodyPath">The document, resolved; the caller refuses a route without one.</param>
    /// <param name="registrationPath">The registration beside it, when there is one.</param>
    /// <param name="hasFolderSubject">Whether the type's rules are about the folder around its registration.</param>
    /// <param name="noun">What the document is called in a message: a diagram or a document.</param>
    /// <param name="collector">The problem collector.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private async ValueTask ValidateDocumentAsync(
        ToolOrigin origin,
        string bodyPath,
        string? registrationPath,
        bool hasFolderSubject,
        string noun,
        ProblemCollector collector,
        CancellationToken cancellationToken)
    {
        var attribution = registrationPath ?? bodyPath;

        string document;
        try
        {
            // Read-only, shared: validation must never contend with an editor (Requirement 6.7).
            document = await SharedDocumentReader.ReadAllTextAsync(bodyPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            collector.AddCore(attribution, $"The {noun} could not be read: {exception.Message}");
            return;
        }

        if (!_validators.TryGet(origin, out var validator))
        {
            return; // A type without rules has nothing to say (Requirement 3.3).
        }

        var baseName = DiagramFileName.StripExtension(IoPath.GetFileName(attribution));

        // A folder-subject type's rules are about the tree around its registration, not about
        // the one MIME line the registration holds. The folder is the registration's own, so it
        // inherits the containment check the route already passed
        // (ansible-structure-diagram Requirement 2.2).
        var request = new DiagramValidationRequest(document, baseName, collector.Root, bodyPath, registrationPath)
        {
            SubjectFolder = hasFolderSubject ? IoPath.GetDirectoryName(attribution) : null,
        };

        IReadOnlyList<DiagramProblem> problems;
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var verdict = validator.ValidateAsync(request, abandon.Token).AsTask();
            var expired = Task.Delay(_validatorTimeout, _time, abandon.Token);
            if (await Task.WhenAny(verdict, expired) != verdict)
            {
                // Abandoned, not awaited further: a hanging module costs its own file's
                // results and nothing more (Requirement 3.5).
                await abandon.CancelAsync();
                _ = verdict.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
                _logger.Warning("The {Origin} validator did not answer for {Path} within {Timeout}", origin.Key, attribution, _validatorTimeout);
                collector.AddCore(
                    attribution,
                    $"The {origin.Key} validator did not answer within {_validatorTimeout.TotalSeconds:0}s.",
                    CoreRuleIds.ValidatorFailed,
                    _validators.RulesVersion(origin));
                return;
            }
            problems = await verdict;
            await abandon.CancelAsync(); // Stop the expiry clock.
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A throwing module costs its own file's results and nothing more (Requirement 3.4).
            _logger.Warning(exception, "The {Origin} validator failed for {Path}", origin.Key, attribution);
            collector.AddCore(
                attribution,
                $"The {origin.Key} validator failed: {exception.Message}",
                CoreRuleIds.ValidatorFailed,
                _validators.RulesVersion(origin));
            return;
        }

        var rulesVersion = _validators.RulesVersion(origin);
        foreach (var problem in problems)
        {
            // Pinned to the attribution file's stats - the same file the store's staleness
            // check reads later. Pinning the body's stats against the registration's path
            // marked every pair's problem stale the moment it was found (caught by the
            // manual pass); a body-only edit is covered by the watcher and the startup
            // pass, not by this comparison.
            var located = LocatedFile(problem, collector.Root) ?? attribution;
            collector.Add(problem, located, located, rulesVersion);
        }
    }

    /// <summary>
    /// The file a <see cref="DiagramProblemFileLocation"/> named, when the rule named one that
    /// really is inside the project.
    /// </summary>
    /// <remarks>
    /// It becomes both the attribution and the staleness pin, and the second is the one that
    /// matters: a folder diagram's registration never changes, so pinning its problems to the
    /// <c>.adp</c> would leave every verdict looking fresh for ever. Editing the file that
    /// declared the mistake is what should mark the verdict stale.
    /// <para>
    /// A path that escapes the root is refused rather than followed - the location comes from a
    /// module, and a module is not trusted to stay inside the project any more than a
    /// user-editable <c>body:</c> header is. Refusing falls back to the diagram's own file, so
    /// the problem is still reported, merely attributed less precisely.
    /// </para>
    /// </remarks>
    private static string? LocatedFile(DiagramProblem problem, string root)
    {
        if (problem.Location is not DiagramProblemFileLocation { RelativePath: var relative } ||
            relative.Length == 0 ||
            IoPath.IsPathRooted(relative))
        {
            return null;
        }

        var full = IoPath.GetFullPath(IoPath.Combine(root, relative));
        if (!IsInside(IoPath.GetFullPath(root), full))
        {
            _logger.Warning("Ignoring a problem location naming {Path}, which is outside the project", relative);
            return null;
        }

        return full;
    }

    private static bool LeavesRoot(string root, string folder)
    {
        var info = new DirectoryInfo(folder);
        if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }
        try
        {
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target is null || !IsInside(root, IoPath.GetFullPath(target.FullName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true; // A link that cannot even be resolved is not followed.
        }
    }

    // Followed links and the file system's own case (D6): a link inside the root that leads out
    // of it does not make what lies beyond it the project's.
    private static bool IsInside(string rootPath, string fullPath) => PathContainment.IsInside(rootPath, fullPath);

}
