using System.Net.Http.Json;

namespace EtAlii.Adp.Client;

public class ChangePusher
{
    private readonly Queue<Change> _changes = new();
    private Task? _pushTask;

    private readonly HttpClient _client;
    
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
        // TODO: Add AsyncLock.
        _changes.Enqueue(change);
        if (_pushTask == null)
        {
            var changes = _changes.ToArray();
            _pushTask = Task.Run(() => PushChanges(changes));
            await _pushTask;
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