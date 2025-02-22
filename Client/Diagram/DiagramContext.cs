using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class DiagramContext
{
    public required DiagramView View { get; init; }
    public required Diagram Diagram { get; init; }
    public required DiagramRibbon Ribbon { get; init; } 
    public required HistoryManager History { get; init; }

    public required CommandManager Commands { get; init; }
    
    public required ICommandHandler[] CommandHandlers { get; init; }

    public SelectableModel[] Selection { get; set; } = [];

    public DiagramSelection SelectionType { get; set; } = DiagramSelection.Nothing;
    public bool CanGroup { get; set; }
    public bool CanUngroup { get; set; }
}