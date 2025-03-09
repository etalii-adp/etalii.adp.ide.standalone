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
        var isChanged = false;
        var newName = _handler.CommandName;
        isChanged |= string.Equals(newName, Name, StringComparison.InvariantCulture);
        Name = newName;

        var newColor = _handler.IconColor;
        isChanged |= newColor == IconColor;
        IconColor = newColor;

        var newIconName = _handler.IconName; 
        isChanged |= newIconName == IconName;
        IconName = newIconName;

        var newStyle = _handler.IsToggled(Context)
            ? "background-color: rgba(var(--bs-secondary-rgb), 0.10) !important"
            : "";
        isChanged |= newStyle == Style;
        Style = newStyle;
        Class = _handler.CanHandle(Context) 
            ? string.Empty
            : "disabled-ribbon-button";

        RenderFragment newChildContent = builder => builder.AddMarkupContent(1, _handler.IconTitle); 
        isChanged |= newChildContent != ChildContent;
        ChildContent = newChildContent;

        if (isChanged)
        {
            StateHasChanged();
        }
    }
}