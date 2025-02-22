using Blazor.Diagrams.Core.Anchors;
using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public class LinkManager
{
    private readonly DiagramContext _context;
    private readonly ILogger _logger;

    public LinkManager(
        DiagramContext context,
        ILoggerFactory loggerFactory)
    {
        _context = context;
        _logger = loggerFactory.CreateLogger<LinkManager>();
    }

    public async Task Initialize()
    {
        _logger.LogInformation("Initializing link management");

        foreach (var l in _context.Diagram.Links)
        {
            var sourceNode = _context.View.Nodes
                .Cast<NodeView>()
                .Single(n => n.Id == l.SourceNode.Id);

            var sourcePort = sourceNode.Ports
                .Cast<PortView>()
                .Single(p => p.Id == l.SourcePort);

            var targetNode = _context.View.Nodes
                .Cast<NodeView>()
                .Single(n => n.Id == l.TargetNode.Id);

            var targetPort = targetNode.Ports
                .Cast<PortView>()
                .Single(p => p.Id == l.TargetPort);
            
            _context.View.Links.Add(new LinkView(l.Id, sourcePort, targetPort));
        }
        
        _context.View.Links.Added += OnLinkAdded;
        _context.View.Links.Removed += OnLinkRemoved;

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

    private void OnLinkCompleted(BaseLinkModel link)
    {
        try
        {
            var linkView = (LinkView)link;
            linkView.TargetAttached -= OnLinkCompleted;
            _logger.LogInformation("Link completed: {Source} to {Target}", linkView.Source.ToString(), linkView.Target.ToString());
            
            var source = (PortView)((SinglePortAnchor)linkView.Source).Port;
            var target = (PortView)((SinglePortAnchor)linkView.Target).Port;

            var command = AddLinkCommandHandler.CreateCommand(_context.Diagram,
                linkView.Id, 
                source.NodeIdentifier, source.Id, 
                target.NodeIdentifier, target.Id);
            
            _context.Commands.Handle(command);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to handle {MethodName}", nameof(OnLinkCompleted));
        }
    }
}