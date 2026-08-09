using System.Text.Json;
using OpenMusicVideoCreator.Api.Contracts.Projects;
using OpenMusicVideoCreator.Application.Projects;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", async (ProjectService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(cancellationToken)).Select(ProjectResponse.FromDomain).ToArray()))
            .WithName("ListProjects")
            .Produces<ProjectResponse[]>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, ProjectService service, CancellationToken cancellationToken) =>
        {
            var project = await service.GetAsync(id, cancellationToken);
            return project is null
                ? Results.NotFound()
                : Results.Ok(ProjectResponse.FromDomain(project));
        })
            .WithName("GetProject")
            .Produces<ProjectResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            ProjectUpsertRequest request,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var project = await service.CreateAsync(request.ToDraft(), cancellationToken);
                return Results.Created($"/api/projects/{project.Id}", ProjectResponse.FromDomain(project));
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["project"] = [exception.Message],
                });
            }
        })
            .WithName("CreateProject")
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPut("/{id:guid}", async (
            Guid id,
            ProjectUpsertRequest request,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var project = await service.UpdateAsync(id, request.ToDraft(), cancellationToken);
                return Results.Ok(ProjectResponse.FromDomain(project));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["project"] = [exception.Message],
                });
            }
        })
            .WithName("UpdateProject")
            .Produces<ProjectResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ProjectService service,
            CancellationToken cancellationToken) =>
            await service.DeleteAsync(id, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound())
            .WithName("DeleteProject")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/export", async (
            Guid id,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var json = await service.ExportAsync(id, cancellationToken);
                return Results.Text(json, "application/json");
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
            .WithName("ExportProject")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/import", async (
            JsonElement document,
            ProjectService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var project = await service.ImportAsync(document.GetRawText(), cancellationToken);
                return Results.Created($"/api/projects/{project.Id}", ProjectResponse.FromDomain(project));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["project"] = [exception.Message],
                });
            }
        })
            .WithName("ImportProject")
            .Produces<ProjectResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }
}
