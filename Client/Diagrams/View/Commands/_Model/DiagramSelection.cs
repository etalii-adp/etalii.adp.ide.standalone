namespace EtAlii.Adp.Client;

[Flags]
public enum DiagramSelection
{
    Nothing = 0,
    SingleNode = 1,
    MultipleNodes = 2,
    SingleLink = 4,
    MultipleLinks = 8,
    NodesAndLinks = 16,
}