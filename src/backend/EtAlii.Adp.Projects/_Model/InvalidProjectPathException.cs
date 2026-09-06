namespace EtAlii.Adp.Projects;

public sealed class InvalidProjectPathException : Exception
{
    public InvalidProjectPathException(string message) : base(message)
    {
    }
}
