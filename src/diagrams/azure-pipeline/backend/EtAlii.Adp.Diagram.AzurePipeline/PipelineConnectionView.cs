using System.Collections.Concurrent;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// What one connection has open on one pipeline: which stages are showing their jobs.
/// </summary>
/// <remarks>
/// Not written to the YAML, ever. Which stages somebody has opened is a property of looking
/// rather than of the pipeline, so two viewers open stages independently and neither leaves
/// anything on the file or on the history.
/// </remarks>
public sealed class PipelineConnectionView
{
    private readonly ConcurrentDictionary<string, byte> _expanded = new(StringComparer.Ordinal);

    /// <summary>Whether this connection is showing the jobs of <paramref name="stageId"/>.</summary>
    public bool IsExpanded(string stageId) => _expanded.ContainsKey(stageId);

    /// <summary>The stages currently open, for the mapper to decide what to send.</summary>
    public IReadOnlySet<string> ExpandedStageIds => _expanded.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>Opens or closes a stage, and answers with what it now is.</summary>
    public bool Toggle(string stageId)
    {
        if (_expanded.TryRemove(stageId, out _))
        {
            return false;
        }

        _expanded[stageId] = 0;
        return true;
    }

    /// <summary>Sets it outright, and answers whether anything changed.</summary>
    public bool Set(string stageId, bool expanded)
    {
        if (expanded == IsExpanded(stageId))
        {
            return false;
        }

        Toggle(stageId);
        return true;
    }
}
