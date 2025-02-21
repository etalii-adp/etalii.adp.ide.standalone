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

    private async void HandleCommand(string commandName)
    {
        try
        {
            _logger.LogInformation("Handling command{CommandName}", commandName);

            var selection = _view
                .GetSelectedModels()
                .ToArray();
            var handler = _commandHandlers.Single(ch => ch.CommandName == commandName);
            var changes = await handler.Execute(selection);
            await _history.Push(changes);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(HandleCommand));
        }
    }
}
    
