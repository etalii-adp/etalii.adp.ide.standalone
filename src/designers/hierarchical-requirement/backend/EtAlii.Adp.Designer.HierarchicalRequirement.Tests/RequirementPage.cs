using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// A requirement document open on one page as the host opens it: the module's session over the project's history and the
/// module's store, with the commands registered as the host registers them, and everything the page was sent, in order.
/// </summary>
/// <remarks>The history is the stack the application's undo and redo walk; nothing here is a test double.</remarks>
internal sealed class RequirementPage : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly Lock _gate = new();
    private readonly List<HierarchySurfaceChangedEventArgs> _sent = [];

    private RequirementPage(ServiceProvider services, string bodyPath, string? registrationPath, Func<Task>? beforeEachCommand, TimeProvider? time)
    {
        _services = services;
        History = new HistoryStack(services.GetRequiredService<ICommandDispatcher>());
        Documents = services.GetRequiredService<IHierarchicalRequirementDocumentStore>();
        Session = new HierarchicalRequirementSession(
            bodyPath,
            registrationPath,
            services.GetRequiredService<Files.IFileWriter>(),
            async command =>
            {
                if (beforeEachCommand is not null) await beforeEachCommand();
                return await History.ExecuteAsync(command);
            },
            Documents,
            time);
        Session.Changed += (_, sent) =>
        {
            lock (_gate)
            {
                _sent.Add(sent);
            }
        };
    }

    public HierarchicalRequirementSession Session { get; }

    /// <summary>The module's store, through which its commands say what they write.</summary>
    public IHierarchicalRequirementDocumentStore Documents { get; }

    /// <summary>The project's history, which the application's undo and redo walk.</summary>
    public IHistoryStack History { get; }

    /// <summary>Opens <paramref name="bodyPath"/>, with its registration when it has one.</summary>
    /// <param name="beforeEachCommand">
    /// What happens after the page accepted a gesture and before its command runs: where another program's write lands in
    /// a guard that needs it to land exactly there, since the watcher would otherwise race it.
    /// </param>
    /// <param name="time">The clock a read the page retries waits on; the system's when null.</param>
    public static RequirementPage Open(string bodyPath, string? registrationPath = null, Func<Task>? beforeEachCommand = null, TimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddCommands();
        services.AddHierarchicalRequirement();
        return new RequirementPage(services.BuildServiceProvider(), bodyPath, registrationPath, beforeEachCommand, time);
    }

    /// <summary>The models the page was sent so far, in order.</summary>
    public IReadOnlyList<HierarchySurfaceModel> Models
    {
        get
        {
            lock (_gate)
            {
                return [.. _sent.Select(sent => sent.Model).OfType<HierarchySurfaceModel>()];
            }
        }
    }

    /// <summary>The first model after the first <paramref name="skip"/> sent that <paramref name="wanted"/> holds for, or null after <paramref name="patience"/>.</summary>
    public Task<HierarchySurfaceModel?> ModelAsync(int skip, Func<HierarchySurfaceModel, bool> wanted, TimeSpan? patience = null) =>
        Eventually.ValueAsync(() => Models.Skip(skip).FirstOrDefault(wanted), TestContext.Current.CancellationToken, patience);

    /// <summary>What became of the gesture <paramref name="editId"/>, or null when nothing was said of it in time.</summary>
    public Task<HierarchySurfaceEditOutcome?> OutcomeAsync(ShortGuid editId) =>
        Eventually.ValueAsync(
            () =>
            {
                lock (_gate)
                {
                    return _sent.Select(sent => sent.Outcome).OfType<HierarchySurfaceEditOutcome>().FirstOrDefault(outcome => outcome.EditId == editId);
                }
            },
            TestContext.Current.CancellationToken);

    /// <summary>Sends one gesture as the page would, and returns its id with the immediate answer.</summary>
    public (ShortGuid EditId, string Refusal) Gesture(string operation, string targetId, params (string Name, string Value)[] parameters)
    {
        var editId = ShortGuid.NewShortGuid();
        var refusal = Session.Gesture(editId, new HierarchySurfaceGesture(operation, targetId, parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value)));
        return (editId, refusal);
    }

    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        await _services.DisposeAsync();
    }
}
