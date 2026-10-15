using System.Runtime.CompilerServices;
using EtAlii.Adp.Context;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The definition's model of a project a test read with <see cref="AnsibleProjectReader"/> directly, as the
/// product builds it from a folder body's reading: what the plugin delivers for the project, built under the
/// definition once per project. Tests that read a fixture without opening a folder use this; the product
/// reads only through <see cref="AnsibleFolder"/> and the validator.
/// </summary>
internal static class AnsibleModels
{
    private static readonly ConditionalWeakTable<AnsibleProject, DislModel> Models = [];

    /// <summary>The model of <paramref name="project"/>.</summary>
    private static DislModel Of(AnsibleProject project) =>
        Models.GetValue(project, read =>
        {
            var result = AnsibleFolderPlugin.Result(read);
            return AnsibleDefinition.Model(new FblModel(result.Elements, result.Findings, result.Unreadable));
        });

    /// <summary>The diagram of <paramref name="project"/>'s model.</summary>
    public static DislDiagram ModelOf(AnsibleProject project) => Of(project).Diagram;

    /// <summary>The findings of <paramref name="project"/>'s model, as <see cref="AnsibleDefinition.Findings(DislModel)"/> gives them.</summary>
    public static IReadOnlyList<DislFinding> Findings(AnsibleProject project) => AnsibleDefinition.Findings(Of(project));

    /// <summary>The rows of <paramref name="elementId"/> in <paramref name="project"/>'s model.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(AnsibleProject project, string elementId) =>
        AnsibleDefinition.Rows(ModelOf(project), elementId);
}
