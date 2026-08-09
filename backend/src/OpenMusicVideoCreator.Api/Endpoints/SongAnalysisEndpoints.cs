using OpenMusicVideoCreator.Api.Contracts.Analysis;
using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Domain.Analysis;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class SongAnalysisEndpoints
{
    public static IEndpointRouteBuilder MapSongAnalysisEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/projects/{projectId:guid}/analysis").WithTags("Song Analysis");

        group.MapGet("/", async Task<IResult> (
            Guid projectId,
            SongAnalysisService service,
            CancellationToken cancellationToken) =>
        {
            var analysis = await service.GetLatestAsync(projectId, cancellationToken);
            return analysis is null
                ? Results.NotFound()
                : Results.Ok(SongAnalysisResponse.FromDomain(analysis));
        })
            .WithName("GetLatestSongAnalysis")
            .Produces<SongAnalysisResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/versions", async (
            Guid projectId,
            SongAnalysisService service,
            CancellationToken cancellationToken) =>
            Results.Ok((await service.ListVersionsAsync(projectId, cancellationToken))
                .Select(SongAnalysisResponse.FromDomain)
                .ToArray()))
            .WithName("ListSongAnalysisVersions")
            .Produces<SongAnalysisResponse[]>(StatusCodes.Status200OK);

        group.MapPost("/", async Task<IResult> (
            Guid projectId,
            SongAnalysisService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var analysis = await service.AnalyzeAsync(projectId, cancellationToken);
                return Results.Ok(SongAnalysisResponse.FromDomain(analysis));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["song"] = [exception.Message],
                });
            }
            catch (InvalidDataException exception)
            {
                return Results.Problem(
                    title: "Song analysis failed",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
            .WithName("AnalyzeSong")
            .Produces<SongAnalysisResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/sections", async Task<IResult> (
            Guid projectId,
            IReadOnlyList<SongSectionRequest> sections,
            SongAnalysisService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var domainSections = sections.Select(section => new SongSection(
                    section.Id ?? Guid.NewGuid(),
                    section.Label,
                    section.Kind,
                    section.StartSeconds,
                    section.EndSeconds,
                    Confidence: 1,
                    AnalysisValueSource.UserEdited)).ToArray();
                var analysis = await service.SaveSectionsAsync(projectId, domainSections, cancellationToken);
                return Results.Ok(SongAnalysisResponse.FromDomain(analysis));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sections"] = [exception.Message],
                });
            }
        })
            .WithName("UpdateSongAnalysisSections")
            .Produces<SongAnalysisResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }
}
