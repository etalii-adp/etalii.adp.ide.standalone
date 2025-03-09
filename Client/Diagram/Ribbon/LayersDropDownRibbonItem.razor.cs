namespace EtAlii.Adp.Client;

public partial class LayersDropDownRibbonItem : DropDownRibbonItem<LayersCommandHandler>
{
    private bool _checkedValue = true;
    
    private LayerFilterOption[] _layerFilterOptions = []; 
    public LayersDropDownRibbonItem(ILoggerFactory loggerFactory) : base(loggerFactory)
    {
    }

    protected override bool IsToggled(DiagramContext context)
    {
        var all = _layerFilterOptions.Length;
        var selected = _layerFilterOptions.Count(o => o.IsChecked);
        return selected != all && all > 0;
    }

    protected override void UpdateDropDown()
    {
        var hasOptions = _layerFilterOptions.Any();
        
        _layerFilterOptions = Context.Diagram.Nodes
            .SelectMany(n => n.TagGroups)
            .DistinctBy(g => g.Id)
            .Where(g => g.Name == WellKnownTagGroup.Layer)
            .SelectMany(g => g.Tags)
            .DistinctBy(t => t.Id)
            .Select(t =>
            {
                return hasOptions switch
                {
                    false => new LayerFilterOption { Tag = t, IsChecked = true },
                    _ => _layerFilterOptions.SingleOrDefault(o => o.Tag == t) ?? new LayerFilterOption { Tag = t }
                };
            })
            .ToArray();
    }

    private Task OnToggleFilter(bool value, LayerFilterOption option)
    {
        option.IsChecked = value;
        var visibleTags = _layerFilterOptions
            .Where(o => o.IsChecked)
            .Select(o => o.Tag)
            .ToArray();
        
        foreach (var view in Context.View.Nodes.Cast<NodeView>())
        {
            var group = view.Node.TagGroups.Single(g => g.Name == WellKnownTagGroup.Layer);
            var visible = group.Tags.Any(t =>  visibleTags.Any(vt => vt.Id == t.Id));

            if (view.Selected && !visible)
            {
                Context.View.UnselectModel(view);
            }
            view.Visible = visible;
        }

        foreach (var view in Context.View.Nodes.Cast<NodeView>())
        {
            foreach (var link in view.PortLinks)
            {
                var sourceIsVisible = (link.Source.Model as PortView)!.Parent.Visible;
                var targetIsVisible = (link.Target.Model as PortView)!.Parent.Visible;
                var visible = sourceIsVisible && targetIsVisible;
                
                if (link.Selected && !visible)
                {
                    Context.View.UnselectModel(link);
                }
                link.Visible = visible;
                link.Refresh();
            }
        }

        UpdateButton();
        
        return Task.CompletedTask;
    }
}