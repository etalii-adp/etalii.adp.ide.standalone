using EtAlii.Adp.Documents;
using JetBrains.Annotations;
namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// This diagram type's identity, cataloged in docs/tools.md as `dotnet/dependency-graph`.
/// </summary>
/// <remarks>
/// <para>
/// <b>A derived type, like `ansible/structure`, but bound like a `c4` one.</b> Its nodes and
/// edges are a projection of files the user maintains elsewhere - a solution and the project
/// files it names - so nothing here is authored and the only thing ADP writes is the
/// <c>layout:</c> block of its own <c>.adp</c>. Where it departs from the ansible template is
/// binding: a bodyless registration takes its containing folder as the subject, which cannot
/// say <em>which</em> solution when a folder holds two (Requirement 2.3). So this type names a
/// document sibling and the registration states the solution through a <c>body:</c> header.
/// </para>
/// <para>
/// <b>Two extensions, one notation.</b> <c>.slnx</c> is the newer XML serialization of the same
/// solution <c>.sln</c> has always described, which is precisely what
/// <see cref="DiagramDefinition.AlternateExtension"/> exists for: one engine, one catalog row,
/// two readable forms.
/// </para>
/// <para>
/// <b>The extension is shared, deliberately.</b> A workspace is full of solutions their owners
/// have no wish to see drawn, and a type that claimed the extension on sight would turn every
/// one of them into a diagram. A solution becomes a diagram when the user says so through Add,
/// which writes the registration that routes it from then on - the azure-pipeline stance, and
/// the one Requirement 2.3's "never inferred" asks for.
/// </para>
/// </remarks>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Diagram
{
    /// <summary>The classic solution serialization; the one most repositories still carry.</summary>
    public const string DocumentExtension = ".sln";

    /// <summary>The XML serialization of the same solution, which this repository itself uses.</summary>
    public const string AlternateDocumentExtension = ".slnx";

    /// <summary>
    /// This module's one type, named so the module's own registrations can say which type they
    /// serve without indexing into the array. Discovery reads <see cref="Definitions"/>; the
    /// module reads this.
    /// </summary>
    public static DiagramDefinition DependencyGraph { get; } = new(
        new DiagramOrigin("dotnet", "dependency-graph"),
        ".NET dependency graph",
        "Which projects a solution builds, which NuGet packages they consume, and every "
        + "reference between them - read from the project files rather than drawn by hand.",
        Icon: "mdi-file-tree-outline",
        Extension: DocumentExtension,
        SharedExtension: true,
        AlternateExtension: AlternateDocumentExtension,
        // Read-only apart from arrangement: the solution is the truth, the .adp holds positions.
        Build: builder => builder.Services.AddDotNetDependencyGraph());

    /// <summary>What discovery reads. One entry: this module carries one notation.</summary>
    public static DiagramDefinition[] Definitions { get; } = [DependencyGraph];
}
