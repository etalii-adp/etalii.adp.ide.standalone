using EtAlii.Adp.Common.Wire;
namespace EtAlii.Adp.Common;

/// <summary>
/// Contributes the properties of whatever is selected, and applies a change to one.
/// </summary>
/// <remarks>
/// <para>
/// The same seam shape as <see cref="IContextActionProvider"/>, and resolved the same way: by
/// the scope the selection's source belongs to. A module adds properties by registering another
/// implementation, and no core code learns what they are - the property grid renders labels,
/// values and editors it does not understand, exactly as the context menu renders actions and
/// the toolbox renders entries it does not understand.
/// </para>
/// <para>
/// Kept separate from <see cref="IContextActionProvider"/> rather than bolted onto it because
/// the two answer different questions. An action is a verb offered to the user; a property is a
/// value that is already there. A module may well have one and not the other - a type whose
/// file is executable configuration may want everything visible and almost nothing editable.
/// </para>
/// <para>
/// A provider never resolves a location itself; <see cref="ContextTarget.ResolvedFullPath"/>
/// arrives already resolved and containment-checked by the service layer.
/// </para>
/// </remarks>
public interface IContextPropertyProvider
{
    /// <summary>The scope this provider contributes to; it is consulted for no other.</summary>
    ContextScope Scope { get; }

    /// <summary>
    /// The properties of <paramref name="target"/>, in the order they should be shown. A
    /// property that cannot currently be edited is returned with a reason rather than omitted;
    /// a target that no longer exists yields none at all.
    /// </summary>
    ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Applies a new value, through a command on the project's history - so a property edit is
    /// one undo away like every other edit (tech.md's Commands rule).
    /// </summary>
    /// <remarks>
    /// Refuses rather than throws: a value the provider will not accept, or a property it does
    /// not offer, comes back as a failure with a sentence for the user. The grid puts the old
    /// value back and says why.
    /// </remarks>
    ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken);
}
