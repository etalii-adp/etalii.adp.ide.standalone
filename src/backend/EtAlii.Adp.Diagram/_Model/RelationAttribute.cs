using JetBrains.Annotations;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// Marks a module's element type constant as a <b>relation</b>: a <see cref="DiagramElement.Type"/>
/// the canvas draws as a line between two elements rather than as a box of its own.
/// </summary>
/// <remarks>
/// <para>
/// A declaration and nothing more: the host does not read it at run time and no behaviour changes
/// with it. It is the one fact about a wire type the constant's value does not say, put on the
/// constant itself so that no second list has to keep it.
/// </para>
/// <para>
/// Every other public constant of a module shaped <c>vendor/type+kind</c> is an element. The tests
/// that span modules read both by reflection: <c>ElementTypesTests</c> writes
/// <c>src/fixtures/cross-tier/element-types.json</c> from them, and <c>DrawnConnectionsTests</c>
/// checks that every relation a canvas draws resolves to a selection.
/// </para>
/// <para>
/// Being read by reflection, a marked constant counts as used for Rider and InspectCode
/// (<c>[MeansImplicitUse]</c>). An element constant that production code does not read says the same
/// with <c>[UsedImplicitly]</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field)]
[MeansImplicitUse]
public sealed class RelationAttribute : Attribute;
