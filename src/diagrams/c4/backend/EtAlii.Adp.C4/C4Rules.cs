using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.C4;

/// <summary>Every rule's stable id, prefixed with the module's short name as core expects.</summary>
public static class C4Rules
{
    public const string MissingDescription = "c4.missing-description";
    public const string MissingTechnology = "c4.missing-technology";
    public const string UnlabelledRelationship = "c4.unlabelled-relationship";
    public const string MissingProtocol = "c4.missing-protocol";
    public const string DanglingRelationship = "c4.dangling-relationship";
    public const string KindNotPermitted = "c4.kind-not-permitted-on-view";
    public const string MixedAbstractionLevels = "c4.mixed-abstraction-levels";
    public const string EmptyView = "c4.empty-view";
    public const string UnknownViewScope = "c4.unknown-view-scope";
    public const string MisplacedElement = "c4.misplaced-element";
    public const string IncludeNotFollowed = "c4.include-not-followed";
}
