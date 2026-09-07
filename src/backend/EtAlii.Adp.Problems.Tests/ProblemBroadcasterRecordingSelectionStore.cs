using System.Threading.Channels;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Context.Wire;

namespace EtAlii.Adp.Problems.Tests;

/// <summary>Records the problems pushes; every other member is unreached by the broadcaster.</summary>
internal sealed class ProblemBroadcasterRecordingSelectionStore : IContextSelectionStore
{
    private readonly List<(string RootPath, ProjectProblems Problems)> _pushes = [];

    public IReadOnlyList<(string RootPath, ProjectProblems Problems)> Pushes => _pushes;

    public void PushProblems(string rootPath, ProjectProblems problems) => _pushes.Add((rootPath, problems));

    public void PushNotice(string rootPath, string message) => throw new NotSupportedException();

    public void Register(ShortGuid watchId, string rootPath, ChannelWriter<ContextMessage> writer, IReadOnlyList<ContextActionGroupDefinition> rootActions, IReadOnlyList<ContextActionGroupDefinition> projectActions, ProjectProblems problems) => throw new NotSupportedException();

    public void PushProjectActions(string rootPath, IReadOnlyList<ContextActionGroupDefinition> actions) => throw new NotSupportedException();

    public void Remove(ShortGuid watchId) => throw new NotSupportedException();

    public ContextSelectionRecord Get(ShortGuid watchId) => throw new NotSupportedException();

    public void Set(ShortGuid watchId, string rootPath, ContextSelectionRecord record, ContextRediscovery rediscover) => throw new NotSupportedException();

    public void Clear(ShortGuid watchId) => throw new NotSupportedException();

    public void Refresh(ShortGuid watchId) => throw new NotSupportedException();

    public void PushTransient(ShortGuid watchId, ContextSelectionRecord record) => throw new NotSupportedException();

    public void UpdateFromTrack(ShortGuid watchId, int levelIndex, IReadOnlyList<string>? newRelativePath) => throw new NotSupportedException();
}
