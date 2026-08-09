using OpenMusicVideoCreator.Api.Contracts.Library;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Library;
using OpenMusicVideoCreator.Domain.Library;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var library = endpoints.MapGroup("/api/library").WithTags("Visual Library");

        library.MapGet("/items", async (
            VisualLibraryKind? kind,
            string? query,
            string[]? tags,
            bool favoritesOnly,
            VisualLibraryService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(kind, query, tags, favoritesOnly, cancellationToken))
                .Select(VisualLibraryResponse.FromDomain)
                .ToArray()))
            .WithName("ListVisualLibraryItems")
            .Produces<VisualLibraryResponse[]>(StatusCodes.Status200OK);

        library.MapGet("/items/{id:guid}", async Task<IResult> (
            Guid id,
            VisualLibraryService service,
            CancellationToken cancellationToken) =>
        {
            var item = await service.GetAsync(id, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(VisualLibraryResponse.FromDomain(item));
        })
            .WithName("GetVisualLibraryItem")
            .Produces<VisualLibraryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        library.MapPost("/items", async Task<IResult> (
            VisualLibraryUpsertRequest request,
            VisualLibraryService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var item = await service.CreateAsync(request.ToDraft(), cancellationToken);
                return Results.Created($"/api/library/items/{item.Id}", VisualLibraryResponse.FromDomain(item));
            }
            catch (ArgumentException exception)
            {
                return Validation("library", exception.Message);
            }
        })
            .WithName("CreateVisualLibraryItem")
            .Produces<VisualLibraryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        library.MapPut("/items/{id:guid}", async Task<IResult> (
            Guid id,
            VisualLibraryUpsertRequest request,
            VisualLibraryService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var item = await service.UpdateAsync(id, request.ToDraft(), cancellationToken);
                return Results.Ok(VisualLibraryResponse.FromDomain(item));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return Validation("library", exception.Message);
            }
        })
            .WithName("UpdateVisualLibraryItem")
            .Produces<VisualLibraryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        library.MapDelete("/items/{id:guid}", async Task<IResult> (
            Guid id,
            VisualLibraryService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            if (!result.Deleted && result.ReferencingProjectIds.Count == 0)
            {
                return Results.NotFound();
            }
            if (!result.Deleted)
            {
                return Results.Conflict(new ReferencedDeleteResponse(false, result.ReferencingProjectIds));
            }
            return Results.Ok(new ReferencedDeleteResponse(true, []));
        })
            .WithName("DeleteVisualLibraryItem")
            .Produces<ReferencedDeleteResponse>(StatusCodes.Status200OK)
            .Produces<ReferencedDeleteResponse>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound);

        library.MapGet("/assets", async (
            string? query,
            string[]? tags,
            bool favoritesOnly,
            AssetLibraryService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(query, tags, favoritesOnly, cancellationToken))
                .Select(AssetLibraryResponse.FromDomain)
                .ToArray()))
            .WithName("ListAssetLibraryEntries")
            .Produces<AssetLibraryResponse[]>(StatusCodes.Status200OK);

        library.MapGet("/assets/{id:guid}", async Task<IResult> (
            Guid id,
            AssetLibraryService service,
            CancellationToken cancellationToken) =>
        {
            var item = await service.GetAsync(id, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(AssetLibraryResponse.FromDomain(item));
        })
            .WithName("GetAssetLibraryEntry")
            .Produces<AssetLibraryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        library.MapPost("/assets", async Task<IResult> (
            IFormFile file,
            string? name,
            string? tags,
            string? sourceDescription,
            AssetLibraryService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await using var stream = file.OpenReadStream(AssetLibraryService.MaxAssetBytes);
                var item = await service.UploadAsync(
                    stream,
                    file.FileName,
                    file.ContentType,
                    file.Length,
                    name,
                    SplitTags(tags),
                    sourceDescription,
                    cancellationToken);
                return Results.Created($"/api/library/assets/{item.Id}", AssetLibraryResponse.FromDomain(item));
            }
            catch (ArgumentException exception)
            {
                return Validation("asset", exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return Results.Problem(
                    title: "Preview generation failed",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
            .DisableAntiforgery()
            .WithName("UploadAssetLibraryEntry")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<AssetLibraryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        library.MapPut("/assets/{id:guid}", async Task<IResult> (
            Guid id,
            AssetLibraryUpdateRequest request,
            AssetLibraryService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var item = await service.UpdateAsync(
                    id,
                    request.Name,
                    request.Tags,
                    request.IsFavorite,
                    request.SourceDescription,
                    cancellationToken);
                return Results.Ok(AssetLibraryResponse.FromDomain(item));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return Validation("asset", exception.Message);
            }
        })
            .WithName("UpdateAssetLibraryEntry")
            .Produces<AssetLibraryResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        library.MapDelete("/assets/{id:guid}", async Task<IResult> (
            Guid id,
            AssetLibraryService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            if (!result.Deleted && result.ReferencingLibraryItemIds.Count == 0)
            {
                return Results.NotFound();
            }
            if (!result.Deleted)
            {
                return Results.Conflict(new ReferencedDeleteResponse(false, result.ReferencingLibraryItemIds));
            }
            return Results.Ok(new ReferencedDeleteResponse(true, []));
        })
            .WithName("DeleteAssetLibraryEntry")
            .Produces<ReferencedDeleteResponse>(StatusCodes.Status200OK)
            .Produces<ReferencedDeleteResponse>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound);

        library.MapGet("/assets/{id:guid}/preview", async Task<IResult> (
            Guid id,
            AssetLibraryService service,
            IMediaAssetRepository mediaAssets,
            IMediaStorage mediaStorage,
            CancellationToken cancellationToken) =>
        {
            var entry = await service.GetAsync(id, cancellationToken);
            if (entry?.PreviewMediaAssetId is null)
            {
                return Results.NotFound();
            }
            var media = await mediaAssets.GetAsync(entry.PreviewMediaAssetId.Value, cancellationToken);
            if (media is null)
            {
                return Results.NotFound();
            }
            var stream = await mediaStorage.OpenReadAsync(new MediaLocation(media.Location), cancellationToken);
            return Results.Stream(stream, media.MimeType, enableRangeProcessing: true);
        })
            .WithName("GetAssetLibraryPreview")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        var characterStates = endpoints.MapGroup("/api/projects/{projectId:guid}/characters/states")
            .WithTags("Character State");

        characterStates.MapGet("/", async (
            Guid projectId,
            ProjectCharacterStateService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListAsync(projectId, cancellationToken))
                .Select(ProjectCharacterStateResponse.FromDomain)
                .ToArray()))
            .WithName("ListProjectCharacterStates")
            .Produces<ProjectCharacterStateResponse[]>(StatusCodes.Status200OK);

        characterStates.MapPut("/{characterId:guid}", async Task<IResult> (
            Guid projectId,
            Guid characterId,
            ProjectCharacterStateRequest request,
            ProjectCharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var state = await service.SaveAsync(
                    projectId,
                    characterId,
                    request.OutfitId,
                    request.Locks,
                    request.StateValues ?? new Dictionary<string, double>(),
                    cancellationToken);
                return Results.Ok(ProjectCharacterStateResponse.FromDomain(state));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Validation("character", exception.Message);
            }
        })
            .WithName("SaveProjectCharacterState")
            .Produces<ProjectCharacterStateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static IResult Validation(string key, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    private static IReadOnlyList<string> SplitTags(string? tags) => string.IsNullOrWhiteSpace(tags)
        ? []
        : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
