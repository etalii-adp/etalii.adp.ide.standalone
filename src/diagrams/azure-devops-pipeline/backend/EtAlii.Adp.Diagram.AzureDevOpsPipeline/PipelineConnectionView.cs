using System.Collections.Concurrent;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// What one connection has open on one pipeline: which stages are showing their jobs, and
/// which jobs are showing their steps.
/// </summary>
/// <remarks>
/// Not written to the YAML, ever. Which stages somebody has opened is a property of looking
/// rather than of the pipeline, so two viewers open stages independently and neither leaves
/// anything on the file or on the history.
/// </remarks>
public sealed class PipelineConnectionView
{
    private readonly ConcurrentDictionary<string, byte> _expanded = new(StringComparer.Ordinal);

    /// <summary>Whether this connection is showing what <paramref name="elementId"/> contains.</summary>
    public bool IsExpanded(string elementId) => _expanded.ContainsKey(elementId);

    /// <summary>
    /// What is currently open - stage ids and job ids together - for the mapper to decide what to
    /// send. One set rather than two: the ids are distinct by construction (a job's id carries its
    /// stage's), and the two levels ask the same question of it.
    /// </summary>
    public IReadOnlySet<string> ExpandedIds => _expanded.Keys.ToHashSet(StringComparer.Ordinal);

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
