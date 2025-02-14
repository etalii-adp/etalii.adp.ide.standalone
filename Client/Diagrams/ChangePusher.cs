using System.Net.Http.Json;

namespace EtAlii.Adp.Client;

public class ChangePusher
{
    private readonly Queue<Change> _changes = new();
    private Task? _pushTask;

    private readonly HttpClient _client;

    public ChangePusher(HttpClient client)
    {
        _client = client;
    }

    public async Task Enqueue(Change change)
    {
        if (_pushTask != null)
        {
            _changes.Enqueue(change);
        }
        else
        {
            var changes = _changes.ToArray();
            _pushTask = Task.Run(() => PushChanges(changes));
            await _pushTask;
            _pushTask = null;
        }
    }

    private async Task PushChanges(Change[] changes)
    {
        foreach (var change in changes)
        {
            await _client.PostAsJsonAsync("/api/", change);
        }
    }
}