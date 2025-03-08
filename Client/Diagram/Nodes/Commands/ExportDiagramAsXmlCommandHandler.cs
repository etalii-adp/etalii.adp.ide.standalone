using System.Xml;
using System.Xml.Serialization;
using BlazorBootstrap;
using Microsoft.JSInterop;

namespace EtAlii.Adp.Client;

public class ExportDiagramAsXmlCommandHandler : RibbonCommandHandler<ExportDiagramAsXmlCommand>
{
    public override bool SendToBackend => false;
    public override bool UseInUndoRedo => false;

    private readonly IJSRuntime _jsRuntime;

    public ExportDiagramAsXmlCommandHandler(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }


    public override IconName IconName => IconName.FileTypeXml;
    public override string IconTitle => "Export as<br/>XML";

    public override bool CanHandle(DiagramContext context) => true;

    public override Command[] CreateCommands(DiagramContext _) => [ new ExportDiagramAsXmlCommand() ];
    
    protected override async Task Do(ExportDiagramAsXmlCommand command, DiagramContext context)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Async = true,
            NewLineHandling = NewLineHandling.Entitize,
        };
        
        var xmlSerializer = new XmlSerializer(typeof(Diagram));

        await using var stringWriter = new StringWriter();

        await using var xmlWriter = XmlWriter.Create(stringWriter, settings);

        xmlSerializer.Serialize(xmlWriter, context.Diagram);
        var xml = stringWriter.ToString();
        
        await _jsRuntime.InvokeVoidAsync("navigator.clipboard.writeText", xml);

        var message = new ToastMessage(ToastType.Info, "Diagram copied as XML to the clipboard");
        context.ToastService.Notify(message);
    }

    protected override Task Undo(ExportDiagramAsXmlCommand change, DiagramContext context)
    {
        return Task.CompletedTask;
    }
}