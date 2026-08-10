using OpenMusicVideoCreator.Application.Timeline;
using OpenMusicVideoCreator.Domain.Timeline;

namespace OpenMusicVideoCreator.Api.Endpoints;

public sealed record TimelineReorderRequest(IReadOnlyList<Guid> ClipIds);
public sealed record TimelineReplaceClipRequest(Guid ClipVariantId);
public sealed record TimelineSplitClipRequest(double SplitAtSeconds);

public static class TimelineEndpoints
{
    public static IEndpointRouteBuilder MapTimelineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/timeline")
            .WithTags("Timeline");

        group.MapGet("/", async Task<IResult> (
            Guid projectId,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
        {
            var timeline = await service.GetLatestAsync(projectId, cancellationToken);
            return timeline is null ? Results.NotFound() : Results.Ok(timeline);
        })
            .WithName("GetProjectTimeline")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/versions", async (
            Guid projectId,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListVersionsAsync(projectId, cancellationToken)))
            .WithName("ListProjectTimelineVersions")
            .Produces<ProjectTimelineVersion[]>(StatusCodes.Status200OK);

        group.MapPost("/initialize", async Task<IResult> (
            Guid projectId,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.GetOrCreateAsync(projectId, cancellationToken)))
            .WithName("InitializeProjectTimeline")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/reset", async Task<IResult> (
            Guid projectId,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.ResetFromStoryboardAsync(projectId, cancellationToken)))
            .WithName("ResetProjectTimeline")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut("/clips/{clipId:guid}", async Task<IResult> (
            Guid projectId,
            Guid clipId,
            TimelineClipEdit request,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.UpdateClipAsync(projectId, clipId, request, cancellationToken)))
            .WithName("UpdateTimelineClip")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/clips/reorder", async Task<IResult> (
            Guid projectId,
            TimelineReorderRequest request,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.ReorderAsync(projectId, request.ClipIds, cancellationToken)))
            .WithName("ReorderTimelineClips")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/clips/{clipId:guid}/replace", async Task<IResult> (
            Guid projectId,
            Guid clipId,
            TimelineReplaceClipRequest request,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.ReplaceClipVariantAsync(projectId, clipId, request.ClipVariantId, cancellationToken)))
            .WithName("ReplaceTimelineClip")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPost("/clips/{clipId:guid}/split", async Task<IResult> (
            Guid projectId,
            Guid clipId,
            TimelineSplitClipRequest request,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.SplitClipAsync(projectId, clipId, request.SplitAtSeconds, cancellationToken)))
            .WithName("SplitTimelineClip")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/overlays", async Task<IResult> (
            Guid projectId,
            TimelineOverlayEdit request,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.UpsertOverlayAsync(projectId, request, cancellationToken)))
            .WithName("UpsertTimelineOverlay")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/overlays/{overlayId:guid}", async Task<IResult> (
            Guid projectId,
            Guid overlayId,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.DeleteOverlayAsync(projectId, overlayId, cancellationToken)))
            .WithName("DeleteTimelineOverlay")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/effects", async Task<IResult> (
            Guid projectId,
            TimelineEffectEdit request,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.UpsertEffectAsync(projectId, request, cancellationToken)))
            .WithName("UpsertTimelineEffect")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/effects/{effectId:guid}", async Task<IResult> (
            Guid projectId,
            Guid effectId,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.DeleteEffectAsync(projectId, effectId, cancellationToken)))
            .WithName("DeleteTimelineEffect")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/restore/{versionId:guid}", async Task<IResult> (
            Guid projectId,
            Guid versionId,
            TimelineEditorService service,
            CancellationToken cancellationToken) =>
            await ExecuteAsync(() => service.RestoreVersionAsync(projectId, versionId, cancellationToken)))
            .WithName("RestoreTimelineVersion")
            .Produces<ProjectTimelineVersion>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<ProjectTimelineVersion>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }
}
