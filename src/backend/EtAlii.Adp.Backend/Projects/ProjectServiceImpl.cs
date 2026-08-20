using EtAlii.Adp.Backend.Sessions;
using Grpc.Core;
using ShortGuidContract = EtAlii.Adp.Contracts.ShortGuid;

namespace EtAlii.Adp.Backend.Projects;

public sealed class ProjectServiceImpl : ProjectService.ProjectServiceBase
{
    private readonly IProjectStore _projectStore;

    public ProjectServiceImpl(IProjectStore projectStore)
    {
        _projectStore = projectStore;
    }

    public override Task<ListProjectsResponse> ListProjects(ListProjectsRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUsername(context);
        var response = new ListProjectsResponse();
        response.Projects.AddRange(_projectStore.List(userId).Select(ToProto));
        return Task.FromResult(response);
    }

    public override Task<AddProjectResponse> AddProject(AddProjectRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUsername(context);
        try
        {
            var added = _projectStore.Add(userId, request.Name, request.Path.Segments.ToList());
            return Task.FromResult(new AddProjectResponse { Added = ToProto(added) });
        }
        catch (InvalidProjectPathException ex)
        {
            return Task.FromResult(new AddProjectResponse
            {
                Error = new AddProjectError { Message = ex.Message }
            });
        }
    }

    public override Task<RemoveProjectResponse> RemoveProject(RemoveProjectRequest request, ServerCallContext context)
    {
        var userId = SessionContext.GetUsername(context);
        _projectStore.Remove(userId, ShortGuid.Parse(request.ProjectId.Value));
        return Task.FromResult(new RemoveProjectResponse());
    }

    private static Project ToProto(ProjectRecord record)
    {
        var path = new EtAlii.Adp.Path();
        path.Segments.AddRange(record.PathSegments);
        return new Project
        {
            Id = new ShortGuidContract { Value = record.Id.ToString() },
            Name = record.Name,
            Path = path,
            DisplayPath = PathTruncator.Truncate(record.PathSegments)
        };
    }
}
