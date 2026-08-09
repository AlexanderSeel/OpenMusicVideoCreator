using OpenMusicVideoCreator.Api.Contracts.Providers;
using OpenMusicVideoCreator.Application.Providers;

namespace OpenMusicVideoCreator.Api.Endpoints;

public static class ProviderEndpoints
{
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/providers").WithTags("Providers");

        group.MapGet("/", async (
            IProviderCatalog catalog,
            ProviderSettingsService settingsService,
            CancellationToken cancellationToken) =>
        {
            var providers = await catalog.ListAsync(cancellationToken);
            var settings = await settingsService.ListAsync(cancellationToken);
            var response = providers
                .Select(provider => ProviderCatalogResponse.FromDomain(provider, settings[provider.Id]))
                .ToArray();
            return Results.Ok(response);
        })
            .WithName("ListProviders")
            .Produces<ProviderCatalogResponse[]>(StatusCodes.Status200OK);

        group.MapGet("/{providerId}/settings", async (
            string providerId,
            ProviderSettingsService settingsService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var settings = await settingsService.GetAsync(providerId, cancellationToken);
                return (IResult)Results.Ok(ProviderSettingsResponse.FromDomain(settings));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        })
            .WithName("GetProviderSettings")
            .Produces<ProviderSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/{providerId}/settings", async (
            string providerId,
            ProviderSettingsRequest request,
            ProviderSettingsService settingsService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var saved = await settingsService.SaveAsync(request.ToDomain(providerId), cancellationToken);
                return (IResult)Results.Ok(ProviderSettingsResponse.FromDomain(saved));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ArgumentException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["provider"] = [exception.Message],
                });
            }
        })
            .WithName("UpdateProviderSettings")
            .Produces<ProviderSettingsResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }
}
