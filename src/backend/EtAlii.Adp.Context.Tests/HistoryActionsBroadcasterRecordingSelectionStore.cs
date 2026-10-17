using System.Threading.Channels;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Context.Tests;

/// <summary>Records the project-actions pushes; every other member is unreached by the broadcaster.</summary>
internal sealed class HistoryActionsBroadcasterRecordingSelectionStore : IContextSelectionStore
{
    private readonly Lock _gate = new();
    private readonly List<(ProjectRoot Root, IReadOnlyList<ContextActionGroupDefinition> Actions)> _pushes = [];

    public IReadOnlyList<(ProjectRoot Root, IReadOnlyList<ContextActionGroupDefinition> Actions)> Pushes
    {
        get
        {
            lock (_gate)
            {
                return _pushes.ToList();
            }
        }
    }

    public void PushProjectActions(ProjectRoot root, IReadOnlyList<ContextActionGroupDefinition> actions)
    {
        lock (_gate)
        {
            _pushes.Add((root, actions));
        }
    }

    public void Register(ShortGuid watchId, ProjectRoot root, ChannelWriter<ContextMessage> writer, IReadOnlyList<ContextActionGroupDefinition> rootActions, IReadOnlyList<ContextActionGroupDefinition> projectActions, ProjectProblems problems) => throw new NotSupportedException();

    public void PushProblems(ProjectRoot root, ProjectProblems problems) => throw new NotSupportedException();

    public void PushNotice(ProjectRoot root, string message) => throw new NotSupportedException();

    public void Remove(ShortGuid watchId) => throw new NotSupportedException();

    public ContextSelectionRecord Get(ShortGuid watchId) => throw new NotSupportedException();

    public void Set(ShortGuid watchId, ProjectRoot root, ContextSelectionRecord record, ContextRediscovery rediscover) => throw new NotSupportedException();

    public void Clear(ShortGuid watchId) => throw new NotSupportedException();

    public void Refresh(ShortGuid watchId) => throw new NotSupportedException();

    public void PushTransient(ShortGuid watchId, ContextSelectionRecord record) => throw new NotSupportedException();

}
