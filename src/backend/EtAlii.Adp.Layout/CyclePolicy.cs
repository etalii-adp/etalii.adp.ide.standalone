namespace EtAlii.Adp.Layout;

/// <summary>What <see cref="Layering.LongestPath" /> does with an edge that closes a cycle.</summary>
/// <remarks>
/// The layouts that layer a graph agree on longest-path layering and disagree only on cycles, which
/// none of their formats forbids outright. Each passes the rule it already has, so joining them
/// moves no picture; agreeing on one rule is a separate decision (spec 025, behaviour change B-03).
/// A rule is added here by the first layout that needs it.
/// </remarks>
public enum CyclePolicy
{
    /// <summary>
    /// A depth-first walk meets a node already on its stack and counts that node's depth as zero,
    /// so the edge into it still pushes its dependent one layer on and the cycle is not chased.
    /// </summary>
    BackEdgeIsZero,

    /// <summary>
    /// A depth-first walk meets a node already on its stack and leaves the edge into it out, so
    /// the edge pushes nothing on: a cycle's members sit no further right than their other
    /// dependencies put them.
    /// </summary>
    BackEdgeIgnored,

    /// <summary>
    /// As <see cref="BackEdgeIsZero" />, but every node's layer comes from a walk of its own that
    /// remembers nothing, not even within itself. On a graph without cycles that is the same
    /// layering; on a cycle every member is measured from itself, so no member's layer depends on
    /// which was walked first. A node reached along several paths is measured once per path, so
    /// the cost grows with the number of paths and it suits small graphs only.
    /// </summary>
    BackEdgeIsZeroPerNode,
}
