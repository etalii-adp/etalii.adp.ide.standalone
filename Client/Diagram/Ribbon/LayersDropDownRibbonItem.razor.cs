namespace EtAlii.Adp.Client;

public partial class LayersDropDownRibbonItem : DropDownRibbonItem<LayersCommandHandler>
{
    private bool _checkedValue = true;
    
    private Tag[] _layerTags = []; 
    public LayersDropDownRibbonItem(ILoggerFactory loggerFactory) : base(loggerFactory)
    {
    }

    protected override void UpdateDropDown()
    {
        _layerTags = Context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .DistinctBy(g => g.Id)
            .Where(g => g.Name == WellKnownTagGroup.Layer)
            .SelectMany(g => g.Tags)
            .DistinctBy(t => t.Id)
            .ToArray();
    }
}