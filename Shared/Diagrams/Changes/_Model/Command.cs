using System.Text.Json.Serialization;

namespace EtAlii.Adp;

[JsonDerivedType(typeof(DiagramPositionCommand), typeDiscriminator: nameof(DiagramPositionCommand))]
[JsonDerivedType(typeof(DiagramZoomCommand), typeDiscriminator: nameof(DiagramZoomCommand))]
[JsonDerivedType(typeof(NodeAddCommand), typeDiscriminator: nameof(NodeAddCommand))]
[JsonDerivedType(typeof(NodeRemoveCommand), typeDiscriminator: nameof(NodeRemoveCommand))]
[JsonDerivedType(typeof(NodeMoveCommand), typeDiscriminator: nameof(NodeMoveCommand))]
[JsonDerivedType(typeof(NodeRenameCommand), typeDiscriminator: nameof(NodeRenameCommand))]
[JsonDerivedType(typeof(LinkAddCommand), typeDiscriminator: nameof(LinkAddCommand))]
[JsonDerivedType(typeof(LinkRemoveCommand), typeDiscriminator: nameof(LinkRemoveCommand))]
[JsonDerivedType(typeof(TagGroupAddCommand), typeDiscriminator: nameof(TagGroupAddCommand))]
[JsonDerivedType(typeof(TagGroupRemoveCommand), typeDiscriminator: nameof(TagGroupRemoveCommand))]
[JsonDerivedType(typeof(TagGroupRenameCommand), typeDiscriminator: nameof(TagGroupRenameCommand))]
[JsonDerivedType(typeof(TagAddCommand), typeDiscriminator: nameof(TagAddCommand))]
[JsonDerivedType(typeof(TagRemoveCommand), typeDiscriminator: nameof(TagRemoveCommand))]
[JsonDerivedType(typeof(TagAssignCommand), typeDiscriminator: nameof(TagAssignCommand))]
[JsonDerivedType(typeof(TagUnassignCommand), typeDiscriminator: nameof(TagUnassignCommand))]
[JsonDerivedType(typeof(ToggleShowPropertiesCommand), typeDiscriminator: nameof(ToggleShowPropertiesCommand))]
[JsonDerivedType(typeof(ToggleShowNavigationCommand), typeDiscriminator: nameof(ToggleShowNavigationCommand))]
public abstract class Command
{
    public bool Undo { get; set; }
}