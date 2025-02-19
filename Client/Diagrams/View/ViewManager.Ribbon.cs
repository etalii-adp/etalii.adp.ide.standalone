using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public partial class ViewManager
{
    private void OnSelectionChanged(SelectableModel obj)
    {
        _ribbon.UpdateBasedOnSelection(_view.GetSelectedModels().ToArray());
    }

    private void HandleCommand(string commandName)
    {
        var selection = _view
            .GetSelectedModels()
            .ToArray();
        var handler = _commandHandlers.Single(ch => ch.CommandName == commandName);
        handler.Execute(selection);
    }

}
    
