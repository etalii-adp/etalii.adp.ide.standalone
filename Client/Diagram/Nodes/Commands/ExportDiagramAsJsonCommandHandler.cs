using System.Text.Json;
using BlazorBootstrap;
using Microsoft.JSInterop;

namespace EtAlii.Adp.Client;

public class ExportDiagramAsJsonCommandHandler : RibbonCommandHandler<ExportDiagramAsJsonCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    private readonly IJSRuntime _jsRuntime;

    public ExportDiagramAsJsonCommandHandler(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }


    public override IconName IconName => IconName.FileTypeJson;
    public override string IconTitle => "Export as<br/>JSON";

    public override bool CanHandle(DiagramContext context) => true;

    public override Command[] CreateCommands(DiagramContext _) => [ new ExportDiagramAsJsonCommand() ];
    
    protected override async Task Do(ExportDiagramAsJsonCommand command, DiagramContext context)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        var json = JsonSerializer.Serialize(context.Diagram, options);
        await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", json);

        var message = new ToastMessage(ToastType.Info, "Diagram copied as JSON to the clipboard");
        context.ToastService.Notify(message);
    }

    protected override Task Undo(ExportDiagramAsJsonCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}