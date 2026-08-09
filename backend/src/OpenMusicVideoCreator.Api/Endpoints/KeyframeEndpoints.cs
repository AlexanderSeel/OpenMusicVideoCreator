using OpenMusicVideoCreator.Api.Contracts.Generation;
using OpenMusicVideoCreator.Application.Generation;

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

        group.MapPost("/{variantId:guid}/select", async Task<IResult> (
            Guid projectId,
            Guid sceneId,
            Guid variantId,
            KeyframeVariantService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var variant = await service.SelectAsync(projectId, variantId, cancellationToken);
                if (variant.SceneId != sceneId) return Results.NotFound();
                return Results.Ok(KeyframeVariantResponse.FromDomain(variant));
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
                var variants = await service.ListSceneAsync(projectId, sceneId, cancellationToken);
                if (variants.All(variant => variant.Id != variantId)) return Results.NotFound();
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

        return endpoints;
    }
}
