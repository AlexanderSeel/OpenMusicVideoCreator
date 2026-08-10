using OpenMusicVideoCreator.Api.Contracts.Generation;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Domain.Generation;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class KeyframeEndpoints
{
    public static IEndpointRouteBuilder MapKeyframeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/scenes/{sceneId:guid}/keyframes")
            .WithTags("Keyframes");

        group.MapGet("/", async (
            Guid projectId,
            Guid sceneId,
            KeyframeVariantService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListSceneAsync(projectId, sceneId, cancellationToken))
                .Select(KeyframeVariantResponse.FromDomain)
                .ToArray()))
            .WithName("ListSceneKeyframeVariants")
            .Produces<KeyframeVariantResponse[]>(StatusCodes.Status200OK);

        group.MapGet("/settings", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            KeyframeGenerationCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(KeyframeGenerationSettingsResponse.FromDomain(
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
            .WithName("GetSceneKeyframeSettings")
            .Produces<KeyframeGenerationSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut("/settings", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            KeyframeGenerationSettingsRequest request,
            KeyframeGenerationCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var settings = await coordinator.SaveSettingsAsync(
                    new SceneKeyframeGenerationSettings(
                        projectId,
                        sceneId,
                        Normalize(request.ProviderId),
                        Normalize(request.ModelId),
                        request.GenerateEndFrame,
                        Normalize(request.Resolution),
                        request.Seed,
                        Normalize(request.NegativePrompt),
                        DateTimeOffset.UnixEpoch),
                    cancellationToken);
                return Results.Ok(KeyframeGenerationSettingsResponse.FromDomain(settings));
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
            .WithName("UpdateSceneKeyframeSettings")
            .Produces<KeyframeGenerationSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        group.MapPost("/generate", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            KeyframeGenerateRequest request,
            KeyframeGenerationCoordinator coordinator,
            CancellationToken cancellationToken) =>
        {
            try
            {
                IReadOnlyList<KeyframeVariant> variants = request.Role is KeyframeRole role
                    ? new[] { await coordinator.QueueAsync(projectId, sceneId, role, cancellationToken) }
                    : await coordinator.QueueSceneAsync(projectId, sceneId, cancellationToken);
                return Results.Accepted(value: KeyframeGenerationResponse.FromDomain(variants));
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
            .WithName("GenerateSceneKeyframes")
            .Produces<KeyframeGenerationResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        group.MapGet("/{variantId:guid}/preview", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            Guid variantId,
            KeyframeVariantService variants,
            IMediaAssetRepository mediaAssets,
            IMediaStorage mediaStorage,
            CancellationToken cancellationToken) =>
        {
            var variant = await variants.GetAsync(projectId, variantId, cancellationToken);
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
            .WithName("GetKeyframeVariantPreview")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{variantId:guid}/select", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            Guid variantId,
            KeyframeVariantService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var existing = await service.GetAsync(projectId, variantId, cancellationToken);
                if (existing is null || existing.SceneId != sceneId) return Results.NotFound();
                return Results.Ok(KeyframeVariantResponse.FromDomain(
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
            .WithName("SelectKeyframeVariant")
            .Produces<KeyframeVariantResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapDelete("/{variantId:guid}", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            Guid variantId,
            KeyframeVariantService service,
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
            .WithName("DeleteKeyframeVariant")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/approval", async (
            Guid projectId,
            Guid sceneId,
            KeyframeApprovalService service,
            CancellationToken cancellationToken) =>
        {
            var approval = await service.GetAsync(projectId, sceneId, cancellationToken);
            var current = await service.IsCurrentSelectionApprovedAsync(projectId, sceneId, cancellationToken);
            return Results.Ok(KeyframeApprovalStatusResponse.FromDomain(approval, current));
        })
            .WithName("GetSceneKeyframeApproval")
            .Produces<KeyframeApprovalStatusResponse>(StatusCodes.Status200OK);

        group.MapPost("/approval", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            KeyframeApprovalService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var approval = await service.ApproveAsync(projectId, sceneId, cancellationToken);
                return Results.Ok(KeyframeApprovalStatusResponse.FromDomain(approval, true));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(new { error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Validation("approval", exception.Message);
            }
        })
            .WithName("ApproveSceneKeyframes")
            .Produces<KeyframeApprovalStatusResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        group.MapDelete("/approval", async (
            Guid projectId,
            Guid sceneId,
            KeyframeApprovalService service,
            CancellationToken cancellationToken) =>
            await service.RevokeAsync(projectId, sceneId, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound())
            .WithName("RevokeSceneKeyframeApproval")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult Validation(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });
}
