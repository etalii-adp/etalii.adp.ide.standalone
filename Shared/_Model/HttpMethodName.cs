namespace EtAlii.Adp;

public static class HttpMethodName
{
    /// <summary>
    /// Use for: Fetching data from the server.
    /// </summary>
    public const string Get = "GET";
    
    /// <summary>
    /// Use for: Submitting new data to the server.
    /// </summary>
    public const string Post = "POST";
    
    /// <summary>
    /// Use for: Replacing an entire resource with new data.
    /// </summary>
    public const string Put = "PUT";
    
    /// <summary>
    /// Use for: Partially updating a resource.
    /// </summary>
    public const string Patch = "PATCH";

    /// <summary>
    /// Use for: Deleting a resource.
    /// </summary>
    public const string Delete = "DELETE";
}
