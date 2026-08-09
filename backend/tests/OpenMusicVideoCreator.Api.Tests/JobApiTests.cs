using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenMusicVideoCreator.Api.Contracts.Jobs;
using OpenMusicVideoCreator.Infrastructure.Jobs;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class JobApiTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public JobApiTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task JobApi_CreatesAndControlsPersistedJob()
    {
        using var client = _factory.CreateClient();
        var request = new JobCreateRequest(
            ProjectId: Guid.NewGuid(),
            SceneId: Guid.NewGuid(),
            ParentJobId: null,
            Type: "mock:success",
            PayloadJson: "{}",
            ProviderId: null,
            ModelId: null,
            Priority: 100,
            MaxRetries: 3,
            EstimatedCost: null,
            Currency: "USD",
            Dependencies: []);

        using var createResponse = await client.PostAsJsonAsync("/api/jobs/", request);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createDocument = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var jobId = createDocument.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Queued", createDocument.RootElement.GetProperty("state").GetString());

        using var pauseResponse = await client.PostAsync($"/api/jobs/{jobId}/pause", content: null);
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);
        Assert.Equal("Paused", await ReadStateAsync(client, jobId));

        using var resumeResponse = await client.PostAsync($"/api/jobs/{jobId}/resume", content: null);
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        Assert.Equal("Queued", await ReadStateAsync(client, jobId));

        using var cancelResponse = await client.PostAsync($"/api/jobs/{jobId}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        Assert.Equal("Cancelled", await ReadStateAsync(client, jobId));
    }

    [Fact]
    public async Task JobChangeHub_BroadcastsJobIdsWithoutOwningJobState()
    {
        var hub = _factory.Services.GetRequiredService<JobChangeHub>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var enumerator = hub.SubscribeAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        var moveNext = enumerator.MoveNextAsync().AsTask();
        var jobId = Guid.NewGuid();

        await hub.PublishAsync(jobId, cancellation.Token);

        Assert.True(await moveNext);
        Assert.Equal(jobId, enumerator.Current);
    }

    private static async Task<string?> ReadStateAsync(HttpClient client, Guid jobId)
    {
        using var response = await client.GetAsync($"/api/jobs/{jobId}");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("state").GetString();
    }
}
