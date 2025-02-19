using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class LinkManager
{
    private readonly DiagramView _view;
    private readonly Diagram _diagram;
    private readonly ChangePusher _changePusher;
    private readonly ILogger _logger;

    public LinkManager(
        DiagramView view, 
        Diagram diagram, 
        ChangePusher changePusher, 
        ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<LinkManager>();
        _view = view;
        _diagram = diagram;
        _changePusher = changePusher;
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing link management");

        foreach (var l in _diagram.Links)
        {
            var sourceNode = _view.Nodes
                .Cast<NodeView>()
                .Single(n => n.Id == l.StartNode.Id);

            var sourcePort = sourceNode.Ports
                .Cast<PortView>()
                .Single(p => p.Id == l.StartPort);

            var endNode = _view.Nodes
                .Cast<NodeView>()
                .Single(n => n.Id == l.EndNode.Id);

            var endPort = endNode.Ports
                .Cast<PortView>()
                .Single(p => p.Id == l.EndPort);
            
            _view.Links.Add(new LinkView(l.Id, sourcePort, endPort));
        }
        
        _view.Links.Added += OnLinkAdded;
        _view.Links.Removed += OnLinkRemoved;

        await Task.CompletedTask;
    }
    
    private void OnLinkAdded(BaseLinkModel linkView)
    {
        _logger.LogInformation("Link add started: {Source} to {Target}", linkView.Source.ToString(), linkView.Target.ToString());
        linkView.TargetAttached += OnLinkCompleted;
    }

    private void OnLinkRemoved(BaseLinkModel linkView)
    {
        linkView.TargetAttached -= OnLinkCompleted;
        _logger.LogInformation("Link removed: {Source} to {Target}", linkView.Source.ToString(), linkView.Target.ToString());
    }

    private async void OnLinkCompleted(BaseLinkModel link)
    {
        try
        {
            var linkView = (LinkView)link;
            linkView.TargetAttached -= OnLinkCompleted;
            _logger.LogInformation("Link add completed: {Source} to {Target}", linkView.Source.ToString(), linkView.Target.ToString());
            
            var source = (PortView)((SinglePortAnchor)linkView.Source).Port;
            var target = (PortView)((SinglePortAnchor)linkView.Target).Port;

            var change = LinkAddChange.Apply(_diagram,
                linkView.Id, 
                source.NodeIdentifier, source.Id, 
                target.NodeIdentifier, target.Id);
            
            await _changePusher.Enqueue(change);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnLinkCompleted));
        }
    }
}