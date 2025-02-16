using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class LinkManager
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly HttpClient _client;
    private readonly ChangePusher _changePusher;
    private readonly ILogger _logger;

    public LinkManager(
        DiagramView view, Diagram diagram, HttpClient client,
        ChangePusher changePusher, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<LinkManager>();
        _view = view;
        _diagram = diagram;
        _client = client;
        _changePusher = changePusher;
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing link management");

        _view.Links.Added += OnLinkAdded;
        _view.Links.Removed += OnLinkRemoved;

        await Task.CompletedTask;
    }
    
    private void OnLinkAdded(BaseLinkModel obj)
    {
        
    }

    private void OnLinkRemoved(BaseLinkModel obj)
    {
        
    }
}