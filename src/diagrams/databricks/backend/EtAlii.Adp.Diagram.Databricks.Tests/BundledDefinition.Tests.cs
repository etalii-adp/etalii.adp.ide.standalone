using System.Reflection;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The three DISL definitions bundled from etalii-adp/etalii.adp, one per Databricks tool type, are the bytes their
/// provenance says they are, the bytes in <c>definition/</c>, and they load, through the loader that checks their
/// provenance, with no diagnostic of severity error (databricks-disl-fbl Requirements 2.3 and 2.4).
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "databricks";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("databricks-bundle", Diagram.Bundle.Origin.Key, "net.etalii.adp.databricks.bundle"),
        new("databricks-job", Diagram.Job.Origin.Key, "net.etalii.adp.databricks.job"),
        new("databricks-pipeline", Diagram.Pipeline.Origin.Key, "net.etalii.adp.databricks.pipeline"),
    ];

    protected override bool AWarningIsAllowed => true;
}
