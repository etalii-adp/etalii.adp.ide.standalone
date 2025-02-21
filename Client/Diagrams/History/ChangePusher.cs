using System.Net.Http.Json;
using NeoSmart.AsyncLock;

namespace EtAlii.Adp.Client;

public class ChangePusher
{
    private readonly Queue<Change> _changes = new();
    private Task? _pushTask;

    private readonly HttpClient _client;
    private readonly AsyncLock _lock = new();
    
    private readonly DiagramManager _diagramManager;
    
    private readonly Type[] _takeLastChangeTypes =
    [
        typeof(DiagramZoomChange),
        typeof(DiagramPositionChange)
    ];

    public ChangePusher(HttpClient client, DiagramManager diagramManager)
    {
        _client = client;
        _diagramManager = diagramManager;
    }

    public async Task Enqueue(Change change)
    {
        using (await _lock.LockAsync())
        {
            _changes.Enqueue(change);
            _pushTask ??= Task.Run(PushChanges);
        }
    }

    public async Task Enqueue(Change[] changes)
    {
        using (await _lock.LockAsync())
        {
            foreach (var change in changes)
            {
                _changes.Enqueue(change);
            }
            _pushTask ??= Task.Run(PushChanges);
        }
    }

    private async Task PushChanges()
    {
        Change[] changesToPush;
        do
        {
            using (await _lock.LockAsync())
            {
                changesToPush = _changes.ToArray();
                _changes.Clear();
            }

            if (changesToPush.Any())
            {
                await PushChanges(changesToPush);
            }
            
        } while (changesToPush.Any());

        using (await _lock.LockAsync())
        {
            _pushTask = null;
        }
    }

    private async Task PushChanges(Change[] changes)
    {
        changes = Flatten(changes);
        
        await _client.PostAsJsonAsync(ApplicationApi.Diagrams.Changes.Request(_diagramManager.CurrentDiagram!.Id), changes);
    }

    private Change[] Flatten(Change[] changes)
    {
        foreach (var takeLastChangeType in _takeLastChangeTypes)
        {
            var last = changes.LastOrDefault(c => c.GetType() == takeLastChangeType);
            if (last != null)
            {
                changes = changes
                    .Where(c => c.GetType() != takeLastChangeType)
                    .Concat([last])
                    .ToArray();
            }
        }
        return changes;
    }
}