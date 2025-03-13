namespace EtAlii.Adp.Client;

public partial class ReadOnlyDiagramPage : DiagramPageBase
{
    protected override void ConfigureContext(DiagramContext context)
    {
        if (!context.Diagram.AllowPublicAccess)
        {
            throw new UnauthorizedAccessException();
        }
        context.Diagram.IsReadOnly = true;
    }
}