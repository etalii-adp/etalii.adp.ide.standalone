namespace EtAlii.Adp.Specification.Fbl;

/// <summary>The eleven splice operations of FBL §6.1. Every write is one of these and nothing else.</summary>
public enum SpliceOperation
{
    ReplaceValue,
    InsertKey,
    RemoveKey,
    InsertEntry,
    RemoveEntry,
    EnsureContainer,
    RemoveContainer,
    RewriteReference,
    ReEmitLine,
    OpenBlock,
    SelfClose,
}
