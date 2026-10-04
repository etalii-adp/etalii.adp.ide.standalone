using EtAlii.Adp.Diagram.AgentBehaviorModelling;
using EtAlii.Adp.Diagram.AnsibleStructure;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline;
using EtAlii.Adp.Diagram.C4;
using EtAlii.Adp.Diagram.CausalLoopDiagram;
using EtAlii.Adp.Diagram.Databricks;
using EtAlii.Adp.Diagram.DependencyGraph;
using EtAlii.Adp.Diagram.DotNetDependencyGraph;
using EtAlii.Adp.Diagram.FunctionalDecompositionGraph;
using EtAlii.Adp.Diagram.GartnerHypeCycleGraph;
using EtAlii.Adp.Diagram.HelmChart;
using EtAlii.Adp.Diagram.Mindmap;
using EtAlii.Adp.Diagram.Rdf;
using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Diagram.Sparql;
using EtAlii.Adp.Diagram.SupplyChain;
using EtAlii.Adp.Diagram.Timeline;
using EtAlii.Adp.Diagram.WardleyMap;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// Every element and relation type string a diagram module puts on the wire, keyed by the module's
/// folder under <c>src/diagrams</c> - the list <c>src/fixtures/cross-tier/element-types.json</c> is
/// written from (backend-centralization R12.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Each entry is the mapper's own constant, never a literal.</b> The constants stay where they are
/// and stay the source; this list only says which module each belongs to and whether it is drawn as an
/// element or as a relation, which is the one thing the constants themselves do not say.
/// </para>
/// <para>
/// <b>A constant nobody added here is still caught.</b> <see cref="ElementTypesTests"/> finds every
/// public constant shaped like a wire type (<c>vendor/type+kind</c>) in the application's assemblies
/// and fails on any this list does not name, so a module adding a type cannot leave the fixture short
/// by forgetting this file.
/// </para>
/// <para>
/// A relation is a type the canvas draws between two others - the split
/// <c>DrawnConnectionsTests</c> uses for the types the examples emit, extended here to every type a
/// module declares. Mindmap has none: a branch is drawn client-side from each node's parent.
/// </para>
/// </remarks>
internal static class ElementTypeCatalog
{
    /// <summary>One module's type strings, split into what is drawn as a box and what is drawn between two.</summary>
    internal sealed record ModuleTypes(string Module, IReadOnlyList<string> Elements, IReadOnlyList<string> Relations);

    internal static readonly IReadOnlyList<ModuleTypes> Modules =
    [
        new("agent-behavior-modelling",
            [AbmElementMapper.SequenceType, AbmElementMapper.FallbackType, AbmElementMapper.ParallelType, AbmElementMapper.RetryType, AbmElementMapper.RepeatType, AbmElementMapper.GuardType, AbmElementMapper.ApprovalType, AbmElementMapper.CheckType, AbmElementMapper.ActionType, AbmElementMapper.AskType, AbmElementMapper.DelegateType],
            [AbmElementMapper.ChildType]),
        new("ansible-structure",
            [AnsibleElementMapper.PlaybookType, AnsibleElementMapper.PlayType, AnsibleElementMapper.RoleType, AnsibleElementMapper.TaskFileType, AnsibleElementMapper.InventoryType, AnsibleElementMapper.VariableFolderType],
            [AnsibleElementMapper.EdgeType]),
        new("azure-devops-pipeline",
            [PipelineElementMapper.StageType, PipelineElementMapper.JobType, PipelineElementMapper.StepType, PipelineElementMapper.TemplateType],
            [PipelineElementMapper.EdgeType]),
        new("c4",
            [C4ElementMapper.NodeType, C4ElementMapper.BoundaryType, C4ElementMapper.ViewType],
            [C4ElementMapper.RelationshipType]),
        new("causal-loop-diagram",
            [CausalLoopElementMapper.VariableType, CausalLoopElementMapper.LoopType],
            [CausalLoopElementMapper.LinkType]),
        new("databricks",
            [DatabricksElementMapper.TaskType, DatabricksElementMapper.ClusterType, DatabricksElementMapper.BundleType, DatabricksElementMapper.ResourceType, DatabricksElementMapper.TargetType, DatabricksElementMapper.PipelineNodeType],
            [DatabricksElementMapper.EdgeType, DatabricksElementMapper.OverrideEdgeType, DatabricksElementMapper.FlowEdgeType]),
        new("dependency-graph",
            [DependencyGraphElementMapper.NodeType],
            [DependencyGraphElementMapper.RelationType]),
        new("dotnet-dependency-graph",
            [DependencyElementMapper.ProjectType, DependencyElementMapper.PackageType],
            [DependencyElementMapper.EdgeType]),
        new("functional-decomposition-graph",
            [FdgElementMapper.UiElementType, FdgElementMapper.DataElementType, FdgElementMapper.ActionType, FdgElementMapper.FunctionType, FdgElementMapper.CommentType],
            [FdgElementMapper.UiChildType, FdgElementMapper.OwnsActionType, FdgElementMapper.OwnsDataType, FdgElementMapper.OwnsFunctionType, FdgElementMapper.ShowsType]),
        new("gartner-hype-cycle-graph",
            [GhgElementMapper.TrendType, GhgElementMapper.TriggerType, GhgElementMapper.NoteType],
            [GhgElementMapper.InfluenceType]),
        new("helm-chart",
            [HelmElementMapper.ChartType, HelmElementMapper.ValuesType, HelmElementMapper.SchemaType, HelmElementMapper.TemplateType, HelmElementMapper.PartialType, HelmElementMapper.CrdsType, HelmElementMapper.SubchartType, HelmElementMapper.ArchiveType, HelmElementMapper.LockType],
            [HelmElementMapper.EdgeType, HelmElementMapper.DependencyType]),
        new("mindmap",
            [MindmapElementMapper.NodeType],
            []),
        // One module folder, four vocabularies: RDF itself, OWL, SKOS and SHACL each have their own canvas.
        new("rdf",
            [
                RdfElementMapper.ResourceType, RdfElementMapper.TruncationType,
                OwlElementMapper.NodeType, OwlElementMapper.ExpressionType, OwlElementMapper.TruncationType,
                SkosElementMapper.ConceptType, SkosElementMapper.SchemeType, SkosElementMapper.CollectionType, SkosElementMapper.TruncationType,
                ShaclElementMapper.ShapeType, ShaclElementMapper.TruncationType,
            ],
            [RdfElementMapper.EdgeType, OwlElementMapper.EdgeType, SkosElementMapper.EdgeType, ShaclElementMapper.EdgeType]),
        new("sparql",
            [SparqlElementMapper.VariableType, SparqlElementMapper.TermType, SparqlElementMapper.RegionType, SparqlElementMapper.AnnotationType, SparqlElementMapper.HeaderType, SparqlElementMapper.TruncationType],
            [SparqlElementMapper.EdgeType]),
        new("supply-chain",
            [SupplyChainElementMapper.GroupType, SupplyChainElementMapper.RawMaterialType, SupplyChainElementMapper.SupplierType, SupplyChainElementMapper.ManufacturerType, SupplyChainElementMapper.AssemblerType, SupplyChainElementMapper.DistributorType, SupplyChainElementMapper.RetailerType, SupplyChainElementMapper.ConsumerType],
            [SupplyChainElementMapper.FlowType]),
        new("timeline",
            [TimelineElementMapper.PeriodType, TimelineElementMapper.MomentType],
            [TimelineElementMapper.ConnectionType]),
        new("wardley-map",
            [WardleyElementTypes.Element, WardleyElementTypes.Note, WardleyElementTypes.Annotation, WardleyElementTypes.Accelerator, WardleyElementTypes.Attitude, WardleyElementTypes.EvolutionAxis],
            [WardleyElementTypes.Link]),
    ];
}
