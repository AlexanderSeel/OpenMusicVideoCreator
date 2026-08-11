using OpenMusicVideoCreator.Application.Costs;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class CostEndpoints
{
    public static IEndpointRouteBuilder MapCostEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/projects/{projectId:guid}/costs", async Task<IResult> (
            Guid projectId,
            ProjectCostService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.GetAsync(projectId, cancellationToken));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
            .WithTags("Costs")
            .WithName("GetProjectCosts")
            .Produces<ProjectCostSummary>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
