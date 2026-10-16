using EtAlii.Adp.Authentication.Wire;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.Projects.Wire;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.TestSupport.Host;

/// <summary>
/// The steps every flow test takes against the host over gRPC - connect, sign in, add a project,
/// read a stream until the message it waits for - written once for the channel and the streams
/// they act on.
/// </summary>
/// <remarks>
/// Each flow test used to carry its own copy of these as private methods: twenty-two of
/// <c>CreateChannel</c>, <c>LoginAsync</c> and <c>AddProjectAsync</c>, in four slightly different
/// spellings that all did the same thing.
/// </remarks>
public static class FlowExtensions
{
    /// <summary>A gRPC channel to <paramref name="factory"/>'s in-process host. The caller disposes it.</summary>
    public static GrpcChannel CreateGrpcChannel<TEntryPoint>(this WebApplicationFactory<TEntryPoint> factory)
        where TEntryPoint : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        var httpClient = factory.CreateDefaultClient();
        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }

    /// <summary>Signs in as the developer on <paramref name="channel"/> and returns the headers a call carries.</summary>
    public static async Task<Metadata> SignInAsync(this GrpcChannel channel, CancellationToken cancellationToken)
    {
        var client = new AuthenticationService.AuthenticationServiceClient(channel);
        var response = await client.LoginAsync(
            new LoginRequest { Username = FlowConstants.DeveloperUsername, Credential = FlowConstants.DeveloperCredential },
            cancellationToken: cancellationToken);
        return new Metadata { { FlowConstants.SessionTokenHeader, response.Session.Value } };
    }

    /// <summary>Adds <paramref name="folder"/> as a project and returns its id.</summary>
    public static async Task<ShortGuid> AddProjectAsync(this GrpcChannel channel, Metadata headers, string folder, CancellationToken cancellationToken)
    {
        var client = new ProjectService.ProjectServiceClient(channel);
        var response = await client.AddProjectAsync(new AddProjectRequest { Path = Path.OfLocation(folder) }, headers, cancellationToken: cancellationToken);
        return response.Added.Id;
    }

    /// <summary>
    /// The first message on <paramref name="stream"/> that <paramref name="wanted"/> accepts, passing
    /// over whatever arrives before it; a stream that ends first is a failure. It waits on the stream
    /// rather than on a clock, so it needs no patience of its own: the caller's token bounds it.
    /// </summary>
    public static async Task<TMessage> FirstAsync<TMessage>(this IAsyncStreamReader<TMessage> stream, Func<TMessage, bool> wanted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(wanted);
        while (await stream.MoveNext(cancellationToken))
        {
            if (wanted(stream.Current))
            {
                return stream.Current;
            }
        }

        throw new InvalidOperationException("The stream ended before the message waited for arrived.");
    }

    /// <summary>Reads <paramref name="stream"/> until a prompt arrives; a stream that ends first is a failure.</summary>
    public static async Task<ContextPrompt> ReadUntilPromptAsync(this IAsyncStreamReader<ContextMessage> stream, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Prompt)
            {
                return stream.Current.Prompt;
            }
        }

        throw new InvalidOperationException("The stream ended before a prompt arrived.");
    }

    /// <summary>
    /// The elements of the next Add delta on <paramref name="stream"/>, or none when reading is
    /// cancelled first - so the caller's assertion reports an empty baseline rather than a
    /// cancellation nobody can read.
    /// </summary>
    public static async Task<IReadOnlyList<Element>> ReadAddAsync(this IAsyncStreamReader<Delta> stream, CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    return [.. stream.Current.Add.Elements];
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Reading stopped at the deadline.
        }
        catch (OperationCanceledException)
        {
            // Same story, thrown the other way.
        }

        return [];
    }

    /// <summary>Reads <paramref name="stream"/> until a change <paramref name="wanted"/> accepts arrives; a stream that ends first is a failure.</summary>
    public static Task<HierarchyChange> ReadUntilChangeAsync(
        this IAsyncStreamReader<HierarchyMessage> stream, Func<HierarchyChange, bool> wanted, CancellationToken cancellationToken) =>
        ReadUntilChangeAsync(stream, wanted, "the expected change", cancellationToken);

    /// <summary>Reads <paramref name="stream"/> until a change of the <paramref name="wanted"/> kind arrives; a stream that ends first is a failure.</summary>
    public static Task<HierarchyChange> ReadUntilChangeAsync(
        this IAsyncStreamReader<HierarchyMessage> stream, HierarchyChange.ChangeOneofCase wanted, CancellationToken cancellationToken) =>
        ReadUntilChangeAsync(stream, change => change.ChangeCase == wanted, $"a {wanted} change", cancellationToken);

    private static async Task<HierarchyChange> ReadUntilChangeAsync(
        IAsyncStreamReader<HierarchyMessage> stream, Func<HierarchyChange, bool> wanted, string expected, CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            var message = stream.Current;
            if (message.MessageCase == HierarchyMessage.MessageOneofCase.Change && wanted(message.Change))
            {
                return message.Change;
            }
        }

        throw new InvalidOperationException($"The stream ended before {expected} arrived.");
    }
}
