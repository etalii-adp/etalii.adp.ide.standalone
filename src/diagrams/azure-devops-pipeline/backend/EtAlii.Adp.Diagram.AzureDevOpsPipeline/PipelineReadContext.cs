namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// What the module hands its persistence plugin (<see cref="PipelineYamlPlugin"/>) beside the FBL contract:
/// where the body is, and the resolver that follows its templates inside the workspace and nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// <b>It stands in for language gap 1</b> of azure-devops-pipeline-disl-fbl (finding B2): FBL 11.2's
/// <c>read</c> receives the body's bytes and nothing else, so a plugin is told neither where the body is
/// nor where the workspace ends, and cannot open <c>templates/build-jobs.yml</c>. It is the one named type
/// the module adds beside the contract, and it goes when FBL gives a plugin the body's path and the
/// workspace's edge.
/// </para>
/// <para>
/// <b>It makes the reading depend on more than the bytes</b>, which FBL 11.4 does not allow: what a followed
/// template says is part of it. That is part of the gap. Within one reading a template is read once, through
/// the resolver's cache, which is per workspace and outlives the reading.
/// </para>
/// </remarks>
/// <param name="BodyPath">The body's absolute path, which a relative template reference starts from; empty when nothing is followed.</param>
/// <param name="Templates">The workspace's resolver, which bounds what may be followed and remembers what it read; null to follow nothing.</param>
internal sealed record PipelineReadContext(string BodyPath, PipelineTemplates? Templates)
{
    /// <summary>
    /// A reading that follows no template and names no reason for one, as the validator's: it is given the
    /// text and no path.
    /// </summary>
    public static PipelineReadContext Unbound { get; } = new("", null);
}
