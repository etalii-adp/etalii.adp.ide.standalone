using BlazorBootstrap;
using Microsoft.AspNetCore.Components;

namespace EtAlii.Adp.Client;

public class DiagramRibbonItem<THandler> : RibbonItem
    where THandler : class, IRibbonCommandHandler
{
    private THandler _handler = null!;

    [Parameter] public DiagramContext Context { get; set; } = null!;

    public DiagramRibbonItem()
    {
        IconSize = IconSize.x3;
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (Context == null!) return;
        
        _handler = Context.CommandHandlers.OfType<THandler>().Single();
        Name = _handler.CommandName;
        
        IconColor = _handler.IconColor;
        IconName = _handler.IconName;
        ChildContent = builder => builder.AddMarkupContent(1, _handler.IconTitle);
        Class = _handler.CanHandle(Context) ? "" : "disabled-ribbon-button";
    }
}

public class DiagramRibbonPlaceStub : RibbonItem
{
    public DiagramRibbonPlaceStub()
    {
        IconColor = IconColor.Primary;
        IconSize = IconSize.x3;
    }
}