

using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Adds one element: a period when <paramref name="End"/> is set, a moment when it is null.
/// </summary>
/// <remarks>
/// The id is the caller's to supply, generated once where the gesture happens - so a redo
/// re-creates the element under the id it had, and every reference made in between stays good.
/// </remarks>
/// <param name="BodyPath">The document to add to.</param>
/// <param name="Id">The new element's id.</param>
/// <param name="Label">What it is called.</param>
/// <param name="Begin">When it starts, as document text.</param>
/// <param name="End">When it finishes, or null for a moment.</param>
/// <param name="Row">The row it lands on.</param>
public sealed record AddTimelineElementCommand(
    string BodyPath,
    string Id,
    string Label,
    string Begin,
    string? End,
    int Row) : ICommand;
