using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// What a selected pipeline element shows in the Property Grid, and which of it may be changed.
/// </summary>
/// <remarks>
/// <para>
/// Almost everything here is <b>shown but not editable</b>, and that is the point rather than an
/// omission. A pipeline file is executable configuration: a pool, a strategy, an environment or a
/// condition is edited better in a text editor, and a bad write breaks a build. So the panel
/// answers "what decides whether this runs" - which is the question a reader has - while only the
/// three properties Requirement 9.1 permits can be written from here.
/// </para>
/// <para>
/// A read-only property carries a <b>reason</b>, not a flag. The value is real and worth seeing,
/// and the reader's next question is where it is written; a greyed-out box answers the first and
/// not the second. For an element that came from a template the reason names the file, which is
/// the case that makes the difference obvious (Requirement 13.7).
/// </para>
/// <para>
/// A property that is <b>absent</b> from the document is not contributed at all. A stage with no
/// <c>condition</c> has no condition row; a stage with <c>condition: ''</c> has a row whose value
/// is empty. Those are different states of the file, and a sentinel value would flatten them into
/// one (Requirement 13.10).
/// </para>
/// </remarks>
public sealed class PipelineContextPropertyProvider : DiagramPropertyProvider<LineDocument, PipelineModel>
{
    /// <summary>The element's <c>displayName</c> - one of the three editable properties.</summary>
    public const string DisplayNamePropertyId = "azure-pipeline.display-name";

    /// <summary>What the element waits for. Editable, and validated before it is written.</summary>
    public const string DependsOnPropertyId = "azure-pipeline.depends-on";

    /// <summary>Whether the element runs at all.</summary>
    public const string EnabledPropertyId = "azure-pipeline.enabled";

    /// <summary>The heading identity properties sort under.</summary>
    public const string IdentityGroup = "Identity";

    /// <summary>The heading for what decides when this runs relative to everything else.</summary>
    public const string OrderingGroup = "Ordering";

    /// <summary>The heading for what decides how it runs once it does.</summary>
    public const string ExecutionGroup = "Execution";

    /// <summary>Creates the provider.</summary>
    /// <remarks>A pipeline is read from the target's workspace root as well as its path, for its templates.</remarks>
    public PipelineContextPropertyProvider(IHistoryStackStore historyStacks, IPipelineDocumentStore documents)
        : base(historyStacks, IPipelineDocumentStore.LoaderFor(documents))
    {
    }

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsPipelineFile(path);

    /// <inheritdoc />
    protected override bool Serves(ContextTarget target) => base.Serves(target) && target.ElementId.Length > 0;

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the bundled definition (<see cref="PipelineDefinition.Rows"/>). An element whose
    /// text lives in a template, or that the pipeline's shape implies, shows every row with the reason
    /// it cannot be written here (Requirement 13.7).
    /// </remarks>
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<LineDocument, PipelineModel> entry, string elementId) =>
        PipelineDefinition.Rows(entry, elementId);

    /// <inheritdoc />
    /// <remarks>
    /// The same command the canvas and the context menu use, on the project's history - so a
    /// rename from the panel and a rename from the diagram are one implementation and one undo
    /// (Requirement 13.11).
    /// </remarks>
    protected override DiagramPropertyChange Change(ContextTarget target, PipelineModel model, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (PipelineEdits.Locate(model, target.ElementId) is not { } location)
        {
            return DiagramPropertyChange.Refused("That element is no longer in this pipeline.");
        }

        return CommandFor(target, propertyId, value, location) is { } command
            ? DiagramPropertyChange.By(command)
            : DiagramPropertyChange.Refused($"'{propertyId}' cannot be set on this element.");
    }

    private static ICommand? CommandFor(ContextTarget target, string propertyId, string value, PipelineElementLocation location)
    {
        var root = target.RootPath;
        var body = target.ResolvedFullPath;
        var id = location.Id;

        return propertyId switch
        {
            DisplayNamePropertyId => new RenamePipelineElementCommand(root, body, id, value.Trim()),
            EnabledPropertyId => new SetPipelineElementEnabledCommand(root, body, id, IsTrue(value)),
            DependsOnPropertyId when location.Kind != PipelineElementLocationKind.Step =>
                DependenciesCommand(root, body, id, value),
            _ => null,
        };
    }

    /// <summary>
    /// The candidate meaning "wait for nothing" - <c>dependsOn: []</c>, which is a real
    /// instruction and not the same as having no <c>dependsOn</c> at all.
    /// </summary>
    public const string NothingCandidate = "(nothing)";

    /// <summary>
    /// The candidate meaning "put it back on the schema's default" - no <c>dependsOn</c> key.
    /// </summary>
    /// <remarks>
    /// Offered as a candidate rather than left to the context menu because the difference between
    /// it and <see cref="NothingCandidate"/> is the whole subtlety of stage ordering, and a list
    /// showing both side by side is the clearest place a user will ever meet it.
    /// </remarks>
    public const string DefaultCandidate = "(the default order)";

    /// <summary>
    /// A <c>dependsOn</c> picked from the list.
    /// </summary>
    /// <remarks>
    /// One name, or one of the two special candidates. A single <c>Value</c> cannot carry a list
    /// without a delimiter convention, and Requirement 13.14 declines to invent one - so where an
    /// element already waits for several things, the property is shown read-only rather than
    /// offered as a picker that would silently drop all but one of them.
    /// </remarks>
    private static ICommand DependenciesCommand(string root, string body, string id, string value)
    {
        var chosen = value.Trim();
        return chosen switch
        {
            DefaultCandidate => new SetPipelineDependenciesCommand(root, body, id, [], Declared: false),
            NothingCandidate or "" => new SetPipelineDependenciesCommand(root, body, id, [], Declared: true),
            _ => new SetPipelineDependenciesCommand(root, body, id, [chosen], Declared: true),
        };
    }

    private static bool IsTrue(string value) => string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}
