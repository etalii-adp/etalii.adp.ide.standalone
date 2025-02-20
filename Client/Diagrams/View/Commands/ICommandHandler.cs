using Blazor.Diagrams.Core.Models.Base;

namespace EtAlii.Adp.Client;

public interface ICommandHandler
{
    string CommandName { get; }
    Change[] Execute(SelectableModel[] selection);
}