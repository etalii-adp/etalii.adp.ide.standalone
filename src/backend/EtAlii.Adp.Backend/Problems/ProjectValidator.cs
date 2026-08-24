using System.Collections.Concurrent;

using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Diagram;

using Serilog;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Problems;

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
    private readonly TimeSpan _validatorTimeout;
    private readonly ConcurrentDictionary<string, Task<ValidationOutcome>> _running = new(StringComparer.OrdinalIgnoreCase);

    public ProjectValidator(DiagramFileRouter router, DiagramValidators validators, TimeSpan? validatorTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(validators);
        _router = router;
        _validators = validators;
        _validatorTimeout = validatorTimeout ?? DefaultValidatorTimeout;
    }

    /// <summary>
    /// Whether a validation is running for <paramref name="rootPath"/> right now - what a
    /// "Validate all" button reads to grey itself while one is in flight (Requirement 6.5).
    /// </summary>
    public bool IsValidating(string rootPath) => _running.ContainsKey(IoPath.GetFullPath(rootPath));

    public async ValueTask<ValidationOutcome> ValidateAsync(ValidationScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var key = IoPath.GetFullPath(scope.RootPath);
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
        var collector = new Collector(root);

        switch (scope)
        {
            case ValidationScope.File file:
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

            case ValidationScope.Folder folder:
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

            case ValidationScope.Project:
                await WalkFolderAsync(root, collector, cancellationToken);
                break;
        }

        return new ValidationOutcome(collector.Problems, collector.FilesConsidered, collector.Skipped);
    }

    private async ValueTask WalkFolderAsync(string folder, Collector collector, CancellationToken cancellationToken)
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

    private async ValueTask ConsiderFileAsync(string path, Collector collector, CancellationToken cancellationToken)
    {
        switch (_router.Route(path))
        {
            case DiagramRouting.Routed routed:
                // A pair routes identically through either of its files - judge it once.
                if (!collector.MarkConsidered(routed.BodyPath))
                {
                    return;
                }
                collector.FilesConsidered++;
                await ValidateRoutedAsync(routed, collector, cancellationToken);
                return;

            case DiagramRouting.UnknownType unknown:
                if (!collector.MarkConsidered(unknown.Path))
                {
                    return;
                }
                collector.FilesConsidered++;
                collector.AddCore(unknown.Path, $"'{unknown.MimeType}' is not a known diagram type.", "core.unknown-type");
                return;

            case DiagramRouting.Ambiguous ambiguous:
                if (!collector.MarkConsidered(ambiguous.Path))
                {
                    return;
                }
                collector.FilesConsidered++;
                collector.AddCore(
                    ambiguous.Path,
                    $"'{ambiguous.Extension}' is claimed by more than one diagram type: " +
                    $"{string.Join(", ", ambiguous.Claimants.Select(claimant => claimant.Origin.Key))}.",
                    "core.ambiguous-extension");
                return;

            case DiagramRouting.Unreadable unreadable:
                if (!collector.MarkConsidered(unreadable.Path))
                {
                    return;
                }
                collector.FilesConsidered++;
                collector.AddCore(unreadable.Path, "The registration file could not be read.");
                return;

            case DiagramRouting.NotADiagram:
                return; // Requirement 2.3: not ours to judge.
        }
    }

    private async ValueTask ValidateRoutedAsync(DiagramRouting.Routed routed, Collector collector, CancellationToken cancellationToken)
    {
        var attribution = routed.RegistrationPath ?? routed.BodyPath;

        string document;
        try
        {
            // Read-only, shared: validation must never contend with an editor (Requirement 6.7).
            document = await File.ReadAllTextAsync(routed.BodyPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            collector.AddCore(attribution, $"The diagram could not be read: {exception.Message}");
            return;
        }

        var origin = routed.Definition.Origin;
        if (!_validators.TryGet(origin, out var validator))
        {
            return; // A type without rules has nothing to say (Requirement 3.3).
        }

        var baseName = DiagramFileName.StripExtension(IoPath.GetFileName(attribution));
        IReadOnlyList<DiagramProblem> problems;
        using var abandon = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var verdict = validator.ValidateAsync(document, baseName, abandon.Token).AsTask();
            var expired = Task.Delay(_validatorTimeout, abandon.Token);
            if (await Task.WhenAny(verdict, expired) != verdict)
            {
                // Abandoned, not awaited further: a hanging module costs its own file's
                // results and nothing more (Requirement 3.5).
                await abandon.CancelAsync();
                _ = verdict.ContinueWith(task => _ = task.Exception, TaskScheduler.Default);
                _logger.Warning("The {Origin} validator did not answer for {Path} within {Timeout}", origin.Key, attribution, _validatorTimeout);
                collector.AddCore(attribution, $"The {origin.Key} validator did not answer within {_validatorTimeout.TotalSeconds:0}s.", "core.validator-failed");
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
            collector.AddCore(attribution, $"The {origin.Key} validator failed: {exception.Message}", "core.validator-failed");
            return;
        }

        var rulesVersion = _validators.RulesVersion(origin);
        foreach (var problem in problems)
        {
            collector.Add(problem, attribution, routed.BodyPath, rulesVersion);
        }
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

    private static bool IsInside(string rootPath, string fullPath)
    {
        var root = IoPath.GetFullPath(rootPath)
            .TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar) + IoPath.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One run's growing answer: the problems, the counters and the two once-only guards.</summary>
    private sealed class Collector(string root)
    {
        private readonly List<StoredProblem> _problems = [];
        private readonly HashSet<string> _considered = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _visitedFolders = new(StringComparer.OrdinalIgnoreCase);

        public string Root { get; } = root;
        public IReadOnlyList<StoredProblem> Problems => _problems;
        public int FilesConsidered { get; set; }
        public int Skipped { get; set; }

        public bool MarkConsidered(string path) => _considered.Add(IoPath.GetFullPath(path));

        public bool MarkFolderVisited(string folder)
        {
            var info = new DirectoryInfo(folder);
            var canonical = (info.Attributes & FileAttributes.ReparsePoint) != 0
                ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? folder
                : folder;
            return _visitedFolders.Add(IoPath.GetFullPath(canonical));
        }

        public void Add(DiagramProblem problem, string attributionPath, string statsPath, string rulesVersion)
        {
            DateTime lastWriteTimeUtc;
            long length;
            try
            {
                var info = new FileInfo(statsPath);
                lastWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : default;
                length = info.Exists ? info.Length : 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastWriteTimeUtc = default;
                length = 0;
            }
            _problems.Add(new StoredProblem(problem, IoPath.GetRelativePath(Root, attributionPath), lastWriteTimeUtc, length, rulesVersion));
        }

        /// <summary>A problem core itself found - a rules version of its own would say nothing, so it stays empty.</summary>
        public void AddCore(string path, string message, string ruleId = "core.unreadable") =>
            Add(new DiagramProblem(DiagramProblemSeverity.Error, message, ruleId), path, path, rulesVersion: "");
    }
}
