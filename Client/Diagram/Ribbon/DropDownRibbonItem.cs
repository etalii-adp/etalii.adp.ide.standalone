using BlazorBootstrap;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace EtAlii.Adp.Client;

public abstract class DropDownRibbonItem<THandler> : ComponentBase
    where THandler : class, IRibbonCommandHandler
{
    [Parameter] public DiagramContext Context { get; set; } = null!;

    [Inject] protected IJSRuntime JsRuntime { get; set; } = null!;

    protected RibbonItem RibbonItem = null!;
    protected Card DropDownCard = null!;

    private bool _dropDownCardIsVisible;
    protected string? DropDownCardStyle = "visibility: collapse; position: absolute";
    
    private THandler _handler = null!;
    private readonly ILogger<DropDownRibbonItem<THandler>> _logger;

    protected IconName IconName { get; private set; }

    protected IconColor IconColor { get; private set; }
    
    protected MarkupString IconTitle { get; private set; }

    protected string ButtonStyle { get; private set; } = null!;
    protected string ButtonName { get; private set; } = null!;
    protected string ButtonClass { get; private set; } = null!;

    protected DropDownRibbonItem(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<DropDownRibbonItem<THandler>>();
    }
    
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (Context == null!) return;
        if (_handler == null!)
        {
            _handler = Context.CommandHandlers.OfType<THandler>().Single();
            _handler.Changed += OnHandlerChanged;
            _handler.Clicked += OnHandlerClicked;
        }
        OnHandlerChanged();
    }

    private async void OnHandlerClicked(Command[] commands)
    {
        try
        {
            await Toggle();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle ribbon dropdown click");
        }
    }

    private void OnHandlerChanged()
    {
        var isChanged = false;
        var newName = _handler.CommandName;
        isChanged |= string.Equals(newName, ButtonName, StringComparison.InvariantCulture);
        ButtonName = newName;

        var newColor = _handler.IconColor;
        isChanged |= newColor == IconColor;
        IconColor = newColor;

        var newIconName = _handler.IconName; 
        isChanged |= newIconName == IconName;
        IconName = newIconName;

        var newStyle = _handler.IsToggled(Context)
            ? "background-color: rgba(var(--bs-secondary-rgb), 0.10) !important"
            : "";
        isChanged |= newStyle == ButtonStyle;
        ButtonStyle = newStyle;
        ButtonClass = _handler.CanHandle(Context) 
            ? string.Empty
            : "disabled-ribbon-button";

        isChanged |= _handler.IconTitle != IconTitle.Value;
        IconTitle = new MarkupString(_handler.IconTitle);

        if (isChanged)
        {
            StateHasChanged();
        }
    }
    
    private async Task Toggle()
    {
        var buttonBounds = await JsRuntime.InvokeAsync<JsRect>("getElementPosition", RibbonItem.Id);

        _dropDownCardIsVisible = !_dropDownCardIsVisible;
            
        DropDownCardStyle = _dropDownCardIsVisible 
            ? $"visibility: visible; position: absolute; top: {buttonBounds.Bottom + 15}px; border-radius: 0; z-index: 99999"
            : "visibility: collapse; position: absolute";

        if (_dropDownCardIsVisible)
        {
            UpdateDropDown();
        }
        StateHasChanged();
    }

    protected abstract void UpdateDropDown();
}