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
        if (_handler == null!)
        {
            _handler = Context.CommandHandlers.OfType<THandler>().Single();
            _handler.Changed += OnHandlerChanged;
        }
        OnHandlerChanged();
    }

    private void OnHandlerChanged()
    {
        Name = _handler.CommandName;
        IconColor = _handler.IconColor;
        IconName = _handler.IconName;
        Style = _handler.IsToggled(Context)
            ? "background-color: rgba(var(--bs-secondary-rgb), 0.10) !important"
            : "";
        Class = _handler.CanHandle(Context) 
            ? string.Empty
            : "disabled-ribbon-button";
        ChildContent = builder => builder.AddMarkupContent(1, _handler.IconTitle);
        StateHasChanged();
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