
namespace EtAlii.Adp.C4;

/// <summary>Which construct the parser is currently inside, so a line is read the right way.</summary>
internal enum C4ParseScope
{
    Root,
    Workspace,
    Model,
    Element,
    Views,
    View,
    Styles,
    Style,
    Unknown,
}
