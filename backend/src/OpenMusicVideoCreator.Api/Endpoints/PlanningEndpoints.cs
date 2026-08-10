using OpenMusicVideoCreator.Api.Contracts.Planning;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Domain.Planning;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/director").WithTags("Director Planning");

        group.MapPost("/plan", async Task<IResult> (
            Guid projectId,
            DirectorPlanRequest request,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.PlanAsync(projectId, request.Controls, cancellationToken);
                return Results.Ok(new DirectorPlanResponse(
                    VisualArcResponse.FromDomain(result.VisualArc),
                    StoryboardResponse.FromDomain(result.Storyboard),
                    result.InitialPrompts.Select(PromptVersionResponse.FromDomain).ToArray()));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (DirectorPlanningException exception)
            {
                return Results.Problem(
                    title: "Director planning failed",
                    detail: exception.Message,
                    statusCode: exception.Retryable
                        ? StatusCodes.Status503ServiceUnavailable
                        : StatusCodes.Status422UnprocessableEntity);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                return Validation("director", exception.Message);
            }
        })
            .WithName("PlanStoryboard")
            .Produces<DirectorPlanResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/visual-arc", async Task<IResult> (
            Guid projectId,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
        {
            var arc = await service.GetLatestVisualArcAsync(projectId, cancellationToken);
            return arc is null ? Results.NotFound() : Results.Ok(VisualArcResponse.FromDomain(arc));
        })
            .WithName("GetLatestVisualArc")
            .Produces<VisualArcResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/visual-arc/versions", async (
            Guid projectId,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListVisualArcVersionsAsync(projectId, cancellationToken))
                .Select(VisualArcResponse.FromDomain)
                .ToArray()))
            .WithName("ListVisualArcVersions")
            .Produces<VisualArcResponse[]>(StatusCodes.Status200OK);

        group.MapPut("/visual-arc", async Task<IResult> (
            Guid projectId,
            VisualArcUpdateRequest request,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var points = (request.Points ?? []).Select(point => new VisualArcPoint(
                    point.Id ?? Guid.NewGuid(),
                    point.TimeSeconds,
                    point.Label,
                    point.Description,
                    point.EmotionalIntensity,
                    point.VisualIntensity,
                    point.CameraEnergy)).ToArray();
                var arc = await service.SaveVisualArcAsync(
                    projectId,
                    request.Summary,
                    request.Controls,
                    points,
                    cancellationToken);
                return Results.Ok(VisualArcResponse.FromDomain(arc));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException)
            {
                return Validation("visualArc", exception.Message);
            }
        })
            .WithName("UpdateVisualArc")
            .Produces<VisualArcResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapGet("/storyboard", async Task<IResult> (
            Guid projectId,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
        {
            var storyboard = await service.GetLatestStoryboardAsync(projectId, cancellationToken);
            return storyboard is null ? Results.NotFound() : Results.Ok(StoryboardResponse.FromDomain(storyboard));
        })
            .WithName("GetLatestStoryboard")
            .Produces<StoryboardResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/storyboard/versions", async (
            Guid projectId,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListStoryboardVersionsAsync(projectId, cancellationToken))
                .Select(StoryboardResponse.FromDomain)
                .ToArray()))
            .WithName("ListStoryboardVersions")
            .Produces<StoryboardResponse[]>(StatusCodes.Status200OK);

        group.MapPut("/storyboard/scenes/{sceneId:guid}", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            SceneUpdateRequest request,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var latest = await service.GetLatestStoryboardAsync(projectId, cancellationToken)
                    ?? throw new KeyNotFoundException();
                var existing = latest.Scenes.SingleOrDefault(scene => scene.Id == sceneId)
                    ?? throw new KeyNotFoundException();
                var edited = existing with
                {
                    StartSeconds = request.StartSeconds,
                    EndSeconds = request.EndSeconds,
                    Title = request.Title,
                    DirectorIntent = request.DirectorIntent,
                    Action = request.Action,
                    Environment = request.Environment,
                    Camera = request.Camera,
                    TransitionIn = request.TransitionIn,
                    CharacterIds = request.CharacterIds?.Distinct().ToArray() ?? [],
                    StyleIds = request.StyleIds?.Distinct().ToArray() ?? [],
                    LocationIds = request.LocationIds?.Distinct().ToArray() ?? [],
                    Details = request.Details?.ToDomain() ?? existing.Details,
                };
                var storyboard = await service.UpdateSceneAsync(projectId, sceneId, edited, cancellationToken);
                return Results.Ok(StoryboardResponse.FromDomain(storyboard));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException)
            {
                return Validation("scene", exception.Message);
            }
        })
            .WithName("UpdateStoryboardScene")
            .Produces<StoryboardResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapPost("/storyboard/reorder", async Task<IResult> (
            Guid projectId,
            SceneReorderRequest request,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var storyboard = await service.ReorderScenesAsync(
                    projectId,
                    request.SceneIds?.ToArray() ?? [],
                    cancellationToken);
                return Results.Ok(StoryboardResponse.FromDomain(storyboard));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                return Validation("sceneOrder", exception.Message);
            }
        })
            .WithName("ReorderStoryboardScenes")
            .Produces<StoryboardResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapGet("/storyboard/scenes/{sceneId:guid}/prompts", async (
            Guid projectId,
            Guid sceneId,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListPromptHistoryAsync(projectId, sceneId, cancellationToken))
                .Select(PromptVersionResponse.FromDomain)
                .ToArray()))
            .WithName("ListScenePromptHistory")
            .Produces<PromptVersionResponse[]>(StatusCodes.Status200OK);

        group.MapPost("/storyboard/scenes/{sceneId:guid}/prompts/regenerate", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            PromptRegenerateRequest request,
            DirectorPlanningService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.RegeneratePromptAsync(projectId, sceneId, request.Notes, cancellationToken);
                return Results.Ok(new PromptRegenerateResponse(
                    StoryboardResponse.FromDomain(result.Storyboard),
                    PromptVersionResponse.FromDomain(result.Prompt)));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException)
            {
                return Validation("prompt", exception.Message);
            }
        })
            .WithName("RegenerateScenePrompt")
            .Produces<PromptRegenerateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static IResult Validation(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}
