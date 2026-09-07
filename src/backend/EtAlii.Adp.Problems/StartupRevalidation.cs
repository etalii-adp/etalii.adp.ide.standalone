using Microsoft.Extensions.Hosting;

using Serilog;

namespace EtAlii.Adp.Problems;

/// <summary>
/// After a restart, walks every project the store knows and re-validates it - sequentially,
/// in the background - so the cache converges on the truth without anyone having to ask
/// (Requirement 4.8). A project opened meanwhile is served from cache at once
/// (Requirement 4.2) and its turn is <see cref="Prioritize">moved to the front</see>,
/// because that is the one whose freshness the user can see.
/// </summary>
public sealed class StartupRevalidation : IHostedService
{
    private static readonly ILogger _logger = Log.ForContext<StartupRevalidation>();

    private readonly IProblemStore _store;
    private readonly ProjectValidator _validator;
    private readonly Lock _gate = new();
    private readonly List<string> _queue = [];
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stopping = new();

    public StartupRevalidation(IProblemStore store, ProjectValidator validator)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(validator);
        _store = store;
        _validator = validator;
    }

    /// <summary>The background pass; awaited by tests, observed by no one else.</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var root in _store.KnownRoots())
            {
                if (_seen.Add(root))
                {
                    _queue.Add(root);
                }
            }
        }
        // The host starts now; the walking happens behind it.
        Completion = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        try
        {
            await Completion.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Stopping is the one outcome a stop cannot fail at.
        }
    }

    /// <summary>
    /// This project was just opened: its freshness is the one the user can see, so it goes
    /// to the front of the queue - joining it if it was never queued at all.
    /// </summary>
    public void Prioritize(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        lock (_gate)
        {
            if (_seen.Add(root))
            {
                _queue.Insert(0, root);
                return;
            }
            var index = _queue.FindIndex(queued => string.Equals(queued, root, StringComparison.OrdinalIgnoreCase));
            if (index > 0)
            {
                _queue.RemoveAt(index);
                _queue.Insert(0, root);
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string root;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    return;
                }
                root = _queue[0];
                _queue.RemoveAt(0);
            }

            if (!Directory.Exists(root))
            {
                // Skipped with a warning, not retried in a loop: a project on an unplugged
                // drive stays exactly as its cache remembers it.
                _logger.Warning("Not re-validating {RootPath}: the folder is not there", root);
                continue;
            }

            try
            {
                _logger.Information("Re-validating {RootPath} in the background", root);
                var outcome = await _validator.ValidateAsync(new ProjectValidationScope(root), cancellationToken);
                _store.Replace(root, outcome.Problems);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // One project's failure does not cost the rest of the queue.
                _logger.Error(exception, "Re-validating {RootPath} failed; its cache stands as it was", root);
            }
        }
    }
}
