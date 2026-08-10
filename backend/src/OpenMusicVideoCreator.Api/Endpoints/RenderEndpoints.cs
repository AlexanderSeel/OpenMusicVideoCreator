using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Rendering;
using OpenMusicVideoCreator.Domain.Rendering;

namespace OpenMusicVideoCreator.Api.Endpoints;

public sealed record QueueProjectRenderRequest(ProjectRenderKind Kind);

public static class RenderEndpoints
{
    public static IEndpointRouteBuilder MapRenderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/renders")
            .WithTags("Renders");

        group.MapGet("/", async (
            Guid projectId,
            ProjectRenderService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, cancellationToken)))
            .WithName("ListProjectRenders")
            .Produces<ProjectRenderRecord[]>(StatusCodes.Status200OK);

        group.MapPost("/", async Task<IResult> (
            Guid projectId,
            QueueProjectRenderRequest request,
            ProjectRenderService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var render = await service.QueueAsync(projectId, request.Kind, cancellationToken);
                return Results.Accepted($"/api/projects/{projectId}/renders/{render.Id}", render);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        })
            .WithName("QueueProjectRender")
            .Produces<ProjectRenderRecord>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/{renderId:guid}", async Task<IResult> (
            Guid projectId,
            Guid renderId,
            ProjectRenderService service,
            CancellationToken cancellationToken) =>
        {
            var render = await service.GetAsync(projectId, renderId, cancellationToken);
            return render is null ? Results.NotFound() : Results.Ok(render);
        })
            .WithName("GetProjectRender")
            .Produces<ProjectRenderRecord>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{renderId:guid}/cancel", async Task<IResult> (
            Guid projectId,
            Guid renderId,
            ProjectRenderService service,
            IJobExecutionCancellationRegistry executionCancellations,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var render = await service.CancelAsync(projectId, renderId, cancellationToken);
                if (render.JobId is Guid jobId)
                {
                    executionCancellations.Cancel(jobId);
                }
                return Results.Ok(render);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
        })
            .WithName("CancelProjectRender")
            .Produces<ProjectRenderRecord>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/{renderId:guid}/retry", async Task<IResult> (
            Guid projectId,
            Guid renderId,
            ProjectRenderService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.RetryAsync(projectId, renderId, cancellationToken));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
        })
            .WithName("RetryProjectRender")
            .Produces<ProjectRenderRecord>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/{renderId:guid}/output", async Task<IResult> (
            Guid projectId,
            Guid renderId,
            ProjectRenderService service,
            IMediaAssetRepository mediaAssets,
            IMediaStorage mediaStorage,
            CancellationToken cancellationToken) =>
        {
            var render = await service.GetAsync(projectId, renderId, cancellationToken);
            if (render?.OutputMediaAssetId is not Guid mediaId) return Results.NotFound();
            var media = await mediaAssets.GetAsync(mediaId, cancellationToken);
            if (media is null || media.ProjectId != projectId) return Results.NotFound();
            try
            {
                var stream = await mediaStorage.OpenReadAsync(new MediaLocation(media.Location), cancellationToken);
                var name = render.Manifest.Kind == ProjectRenderKind.Preview ? "preview.mp4" : "music-video.mp4";
                return Results.File(stream, media.MimeType, name, enableRangeProcessing: true);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound();
            }
        })
            .WithName("DownloadProjectRender")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
