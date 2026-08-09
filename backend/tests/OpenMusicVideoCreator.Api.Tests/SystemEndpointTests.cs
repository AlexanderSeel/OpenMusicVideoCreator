using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenMusicVideoCreator.Api.Middleware;
using OpenMusicVideoCreator.Application.SystemInfo;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class SystemEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SystemEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOkAndCorrelationId()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));
    }

    [Fact]
    public async Task VersionEndpoint_ReturnsTypedContract()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/system/version");

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SystemVersionResponse>();

        Assert.NotNull(payload);
        Assert.Equal("OpenMusicVideoCreator.Api", payload.ApplicationName);
        Assert.Equal("Development", payload.Environment);
        Assert.False(string.IsNullOrWhiteSpace(payload.Version));
    }

    [Fact]
    public async Task CorrelationId_PreservesCallerSuppliedValue()
    {
        const string correlationId = "test-correlation-id";
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());
    }
}
