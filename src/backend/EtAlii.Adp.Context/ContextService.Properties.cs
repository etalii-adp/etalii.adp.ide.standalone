using EtAlii.Adp.Context.Wire;
using Grpc.Core;

namespace EtAlii.Adp.Context;

/// <summary>
/// The property half of <see cref="ContextService"/>: what the selection has, and changing
/// one of them. An explicit source names the target; without one, the properties are those of
/// whatever the connection currently has selected - the same rule the action half follows.
/// </summary>
/// <remarks>
/// Nothing here knows what a description or a technology is. Every property, its editor, its
/// read-only reason and the effect of writing to it come from
/// <see cref="IContextPropertyResolver"/> and whichever provider it routes to.
/// </remarks>
public sealed partial class ContextService
{
    public override async Task<DescribePropertiesResponse> DescribeProperties(DescribePropertiesRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var response = new DescribePropertiesResponse();
        var target = await TryResolveTargetAsync(request.ProjectId, request.WatchId, request.Source, context);
        if (target is null)
        {
            // Unauthorized, unknown, or already gone - answered the same way, so a caller
            // learns nothing about what it may not already see.
            _logger.Debug(
                "No properties for {Source} on watch {WatchId}: it resolved to nothing",
                Describe(request.Source),
                request.WatchId);
            return response;
        }

        var properties = await _contextPropertyResolver.DescribeAsync(target, context.CancellationToken);
        response.Properties.AddRange(properties.Select(ToProto));
        _logger.Debug(
            "Described {Count} properties for {TargetPath} on watch {WatchId}",
            response.Properties.Count,
            target.ResolvedFullPath,
            request.WatchId);
        return response;
    }

    public override async Task<SetPropertyResponse> SetProperty(SetPropertyRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var target = await TryResolveTargetAsync(request.ProjectId, request.WatchId, request.Source, context);
        if (target is null)
        {
            return new SetPropertyResponse { Accepted = false, Error = "What was selected is no longer there." };
        }

        var result = await _contextPropertyResolver.SetAsync(
            target,
            request.PropertyId,
            request.Value,
            context.CancellationToken);

        if (!result.IsSuccess)
        {
            _logger.Debug(
                "Refused {PropertyId} on {TargetPath}: {Error}",
                request.PropertyId,
                target.ResolvedFullPath,
                result.Error);
            return new SetPropertyResponse { Accepted = false, Error = result.Error };
        }

        // The provider committed through a command, so the edit is on the project history and
        // whatever it changed has already been pushed to every watcher. Nothing to echo back:
        // the grid learns the new value from the same stream everything else does.
        _logger.Information(
            "Set {PropertyId} on {TargetPath} for watch {WatchId}",
            request.PropertyId,
            target.ResolvedFullPath,
            request.WatchId);
        return new SetPropertyResponse { Accepted = true };
    }

    private static ContextProperty ToProto(ContextPropertyDefinition definition)
    {
        var property = new ContextProperty
        {
            Id = definition.Id,
            Label = definition.Label,
            Value = definition.Value,
            Editor = definition.Editor,
            ReadOnlyReason = definition.ReadOnlyReason,
            Group = definition.Group,
        };

        // Only a Choice, a Slider and a Tags row have any, and an empty repeated field costs nothing on the wire - so this
        // adds nothing to every property that is not one.
        property.Candidates.AddRange(definition.Choices);
        return property;
    }
}
