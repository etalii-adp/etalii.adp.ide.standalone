using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public partial class ViewManager
{
    private void OnSelectionChanged(SelectableModel obj)
    {
        _ribbon.UpdateBasedOnSelection(_view.GetSelectedModels().ToArray());
    }

    private async void HandleCommand(string commandName)
    {
        try
        {
            _logger.LogInformation("Handling command{CommandName}", commandName);

            var selection = _view
                .GetSelectedModels()
                .ToArray();
            var handler = _commandHandlers.Single(ch => ch.CommandName == commandName);
            var changes = handler.Execute(selection);
            await _changePusher.Enqueue(changes);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(HandleCommand));
        }
    }
}
    
