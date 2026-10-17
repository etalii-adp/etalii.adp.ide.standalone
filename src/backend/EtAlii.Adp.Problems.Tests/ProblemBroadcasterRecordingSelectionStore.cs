using System.Threading.Channels;
using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Problems.Tests;

/// <summary>Records the problems pushes; every other member is unreached by the broadcaster.</summary>
internal sealed class ProblemBroadcasterRecordingSelectionStore : IContextSelectionStore
{
    private readonly List<(ProjectRoot Root, ProjectProblems Problems)> _pushes = [];

    public IReadOnlyList<(ProjectRoot Root, ProjectProblems Problems)> Pushes => _pushes;

    public void PushProblems(ProjectRoot root, ProjectProblems problems) => _pushes.Add((root, problems));

    public void PushNotice(ProjectRoot root, string message) => throw new NotSupportedException();

    public void Register(ShortGuid watchId, ProjectRoot root, ChannelWriter<ContextMessage> writer, IReadOnlyList<ContextActionGroupDefinition> rootActions, IReadOnlyList<ContextActionGroupDefinition> projectActions, ProjectProblems problems) => throw new NotSupportedException();

    public void PushProjectActions(ProjectRoot root, IReadOnlyList<ContextActionGroupDefinition> actions) => throw new NotSupportedException();

    public void Remove(ShortGuid watchId) => throw new NotSupportedException();

    public ContextSelectionRecord Get(ShortGuid watchId) => throw new NotSupportedException();

    public void Set(ShortGuid watchId, ProjectRoot root, ContextSelectionRecord record, ContextRediscovery rediscover) => throw new NotSupportedException();

    public void Clear(ShortGuid watchId) => throw new NotSupportedException();

    public void Refresh(ShortGuid watchId) => throw new NotSupportedException();

    public void PushTransient(ShortGuid watchId, ContextSelectionRecord record) => throw new NotSupportedException();

}
