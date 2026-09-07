using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// A line from one element to another, carrying whatever the author says it means and nothing
/// this type assigns to it.
/// </summary>
/// <param name="Id">The stable identifier, from the document.</param>
/// <param name="From">The source element's id.</param>
/// <param name="To">The target element's id.</param>
/// <param name="Label">What the author calls it, or empty.</param>
/// <param name="Range">The lines that declare it.</param>
public sealed record TimelineConnection(
    string Id,
    string From,
    string To,
    string Label,
    LineRange Range);
