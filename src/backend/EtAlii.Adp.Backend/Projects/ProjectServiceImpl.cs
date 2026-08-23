using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using Serilog;

namespace EtAlii.Adp.Backend.Projects;

public sealed class ProjectServiceImpl : ProjectService.ProjectServiceBase
{
    private static readonly ILogger _logger = Log.ForContext<ProjectServiceImpl>();

    private readonly IProjectStore _projectStore;

    public ProjectServiceImpl(IProjectStore projectStore)
    {
        _projectStore = projectStore;
    }

    public override Task<ListProjectsResponse> ListProjects(ListProjectsRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        var response = new ListProjectsResponse();
        response.Projects.AddRange(_projectStore.List(userId).Select(ToProto));
        // Debug: routine, and issued on every visit to the project picker.
        _logger.Debug("Listed {Count} projects for {UserId}", response.Projects.Count, userId);
        return Task.FromResult(response);
    }

    public override Task<AddProjectResponse> AddProject(AddProjectRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        try
        {
            var added = _projectStore.Add(userId, request.Name, new PathRecord(request.Path.Segments.ToList()));
            // Information: the user's project list is durable state, and this changed it.
            _logger.Information(
                "Added project {ProjectName} ({ProjectId}) for {UserId} at {ProjectPath}",
                added.Name,
                added.Id,
                userId,
                System.IO.Path.Combine(added.Path.Segments.ToArray()));
            return Task.FromResult(new AddProjectResponse { Added = ToProto(added) });
        }
        catch (InvalidProjectPathException ex)
        {
            // The user pointed at a folder that is not there - their mistake to correct, not
            // a fault of ours, so a warning rather than an error.
            _logger.Warning("Rejected the project {UserId} tried to add: {Reason}", userId, ex.Message);
            return Task.FromResult(new AddProjectResponse
            {
                Error = new AddProjectError { Message = ex.Message }
            });
        }
    }

    public override Task<RemoveProjectResponse> RemoveProject(RemoveProjectRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUserId(context);
        _projectStore.Remove(userId, request.ProjectId);
        _logger.Information("Removed project {ProjectId} for {UserId}", request.ProjectId, userId);
        return Task.FromResult(new RemoveProjectResponse());
    }

    private static Project ToProto(ProjectRecord record)
    {
        var path = new Path();
        path.Segments.AddRange(record.Path.Segments);
        return new Project
        {
            Id = record.Id,
            Name = record.Name,
            Path = path,
            DisplayPath = PathTruncator.Truncate(record.Path.Segments)
        };
    }
}
