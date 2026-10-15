using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// What the property grid shows for a selected Ansible element - everything, and none of it
/// editable (Requirement 10).
/// </summary>
/// <remarks>
/// <para>
/// <b>It holds no logic</b>: it finds the folder of the registration an element was selected in and
/// hands out <see cref="AnsibleDefinition.Rows"/>, the rows derived from the bundled definition
/// (ansible-structure-disl-fbl, Requirements 3.1 and 3.6). The hand-written rows they replace are kept
/// in the test project as the oracle they are compared with.
/// </para>
/// <para>
/// This is property-grid Requirement 4 exercised at 100%: <b>every</b> row carries a non-empty
/// <see cref="ContextPropertyDefinition.ReadOnlyReason"/>, and every reason names the file the
/// value actually lives in and how it is edited: "Defined in X; edit it in a text editor."
/// </para>
/// <para>
/// "Read-only" here is enforced rather than styled: <c>ContextPropertyResolver</c> refuses a
/// write to a property its owner described as read-only, server-side, whatever the client
/// claimed. <see cref="SetAsync"/> is therefore unreachable in practice - and is written to
/// refuse anyway, because a provider that would silently accept a write if the resolver ever
/// changed is a trap rather than a design. Its sentence stays here until the binding's
/// <c>readOnly</c> states it (finding A6).
/// </para>
/// <para>
/// <b>It does not derive from <see cref="DiagramPropertyProvider{TBody, TModel}"/></b>, because
/// everything that frame carries is for a document that can be edited: it loads a document of a
/// store, guards one that could not be read and dispatches a change through the project's history,
/// while this reads a folder and edits nothing (code-structure, spec 025: a module that cannot adopt
/// a shared type keeps its own).
/// </para>
/// </remarks>
public sealed class AnsibleContextPropertyProvider : IContextPropertyProvider
{
    private const string Refusal =
        "An Ansible structure diagram shows the folder as it is and changes nothing in it. " +
        "Edit the file this value comes from in a text editor.";

    private readonly IAnsibleProjectStore _store;

    public AnsibleContextPropertyProvider(IAnsibleProjectStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        // The target is the registration the element was selected in; the project is its folder.
        var folder = IoPath.GetDirectoryName(target.ResolvedFullPath);
        if (target.ElementId.Length == 0 || folder is null || !Directory.Exists(folder))
        {
            return Empty;
        }

        return ValueTask.FromResult(AnsibleDefinition.Rows(_store.GetOrLoad(folder), target.ElementId));
    }

    /// <summary>
    /// Refuses, always, with the property's own reason.
    /// </summary>
    /// <remarks>
    /// Unreachable through the grid - <c>ContextPropertyResolver</c> stops a write to a
    /// read-only property before a provider is consulted - and deliberately written all the
    /// same. A refusal, never an exception: the same discipline <c>CommandResult</c> imposes.
    /// </remarks>
    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target, string propertyId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextPropertyResult.Failure(Refusal));

    private static readonly ValueTask<IReadOnlyList<ContextPropertyDefinition>> Empty =
        ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
}
