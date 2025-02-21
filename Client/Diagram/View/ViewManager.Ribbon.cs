using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public partial class ViewManager
{
    private void OnHistoryChanged()
    {
        _ribbon.UpdateBasedOnHistory(_history);
    }

    private void OnSelectionChanged(SelectableModel obj)
    {
        _ribbon.UpdateBasedOnSelection(_view.GetSelectedModels().ToArray());
    }
}
    
