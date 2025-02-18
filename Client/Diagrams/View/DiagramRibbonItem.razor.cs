using BlazorBootstrap;

namespace EtAlii.Adp.Client;

public partial class DiagramRibbonItem : RibbonItem
{
    // class="default-node @(Node.Group != null ? "grouped" : "") @(Node.Selected ? "selected" : "")"

    public DiagramRibbonItem()
    {
        IconColor = IconColor.Success;
        IconSize = IconSize.x3;
    }
}