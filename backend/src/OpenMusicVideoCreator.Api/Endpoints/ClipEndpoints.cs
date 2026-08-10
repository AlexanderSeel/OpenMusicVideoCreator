using OpenMusicVideoCreator.Api.Contracts.Generation;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class ClipEndpoints
{
    public static IEndpointRouteBuilder MapClipEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/scenes/{sceneId:guid}/clips")
            .WithTags("Clips");

        group.MapGet("/", async (
            Guid projectId,
            Guid sceneId,
            ClipVariantService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListSceneAsync(projectId, sceneId, cancellationToken))
                .Select(ClipVariantResponse.FromDomain)
                .ToArray()))
            .WithName("ListSceneClipVariants")
            .Produces<ClipVariantResponse[]>(StatusCodes.Status200OK);

        group.MapGet("/settings", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            VideoGenerationCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(VideoGenerationSettingsResponse.FromDomain(
                    await coordinator.GetSettingsAsync(projectId, sceneId, cancellationToken)));
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
            .WithName("GetSceneVideoGenerationSettings")
            .Produces<VideoGenerationSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut("/settings", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            VideoGenerationSettingsRequest request,
            VideoGenerationCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var settings = await coordinator.SaveSettingsAsync(
                    new SceneVideoGenerationSettings(
                        projectId,
                        sceneId,
                        Normalize(request.ProviderId),
                        Normalize(request.ModelId),
                        request.UseEndFrame,
                        Normalize(request.Resolution),
                        request.DurationSeconds,
                        request.AllowFallback,
                        DateTimeOffset.UnixEpoch),
                    cancellationToken);
                return Results.Ok(VideoGenerationSettingsResponse.FromDomain(settings));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                return Validation("settings", exception.Message);
            }
        })
            .WithName("UpdateSceneVideoGenerationSettings")
            .Produces<VideoGenerationSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        group.MapPost("/generate", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            VideoGenerationCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var variant = await coordinator.QueueAsync(projectId, sceneId, cancellationToken);
                return Results.Accepted(value: ClipGenerationResponse.FromDomain(variant));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
            {
                return Validation("generation", exception.Message);
            }
        })
            .WithName("GenerateSceneClip")
            .Produces<ClipGenerationResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        group.MapGet("/{variantId:guid}/preview", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            Guid variantId,
            ClipVariantService clips,
            IMediaAssetRepository mediaAssets,
            IMediaStorage mediaStorage,
            CancellationToken cancellationToken) =>
        {
            var variant = await clips.GetAsync(projectId, variantId, cancellationToken);
            if (variant is null || variant.SceneId != sceneId || variant.MediaAssetId is not Guid mediaAssetId)
            {
                return Results.NotFound();
            }
            var media = await mediaAssets.GetAsync(mediaAssetId, cancellationToken);
            if (media is null || media.ProjectId != projectId)
            {
                return Results.NotFound();
            }
            var stream = await mediaStorage.OpenReadAsync(new MediaLocation(media.Location), cancellationToken);
            return Results.File(stream, media.MimeType, enableRangeProcessing: true);
        })
            .WithName("GetClipVariantPreview")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{variantId:guid}/select", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            Guid variantId,
            ClipVariantService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var existing = await service.GetAsync(projectId, variantId, cancellationToken);
                if (existing is null || existing.SceneId != sceneId) return Results.NotFound();
                return Results.Ok(ClipVariantResponse.FromDomain(
                    await service.SelectAsync(projectId, variantId, cancellationToken)));
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
            .WithName("SelectClipVariant")
            .Produces<ClipVariantResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/{variantId:guid}", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            Guid variantId,
            ClipVariantService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var variant = await service.GetAsync(projectId, variantId, cancellationToken);
                if (variant is null || variant.SceneId != sceneId) return Results.NotFound();
                return await service.DeleteAsync(projectId, variantId, cancellationToken)
                    ? Results.NoContent()
                    : Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
        })
            .WithName("DeleteClipVariant")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Validation(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}
