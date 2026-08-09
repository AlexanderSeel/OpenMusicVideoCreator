using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenMusicVideoCreator.Api.Contracts.Providers;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Infrastructure.Providers;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class ProviderSubsystemTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public ProviderSubsystemTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProviderApi_ListsCapabilityDrivenMockCatalog()
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync("/api/providers/");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var providers = document.RootElement.EnumerateArray().ToArray();

        Assert.Contains(providers, provider =>
            provider.GetProperty("id").GetString() == MockImageProvider.ProviderId &&
            provider.GetProperty("models")[0]
                .GetProperty("capabilities")
                .EnumerateArray()
                .Any(capability => capability.GetString() == nameof(ProviderCapability.ImageGeneration)));
        Assert.Contains(providers, provider =>
            provider.GetProperty("id").GetString() == MockVideoProvider.ProviderId &&
            provider.GetProperty("models")[0].GetProperty("supportsStartFrame").GetBoolean() &&
            provider.GetProperty("models")[0].GetProperty("supportsEndFrame").GetBoolean());
        Assert.Contains(providers, provider =>
            provider.GetProperty("id").GetString() == MockDirectorProvider.ProviderId &&
            provider.GetProperty("models")[0]
                .GetProperty("capabilities")
                .EnumerateArray()
                .Any(capability => capability.GetString() == nameof(ProviderCapability.DirectorPlanning)));
    }

    [Fact]
    public async Task ProviderSettings_PersistReferenceButNeverSecretValue()
    {
        const string environmentName = "OMVC_TEST_PROVIDER_SECRET";
        const string secretValue = "never-return-this-secret";
        Environment.SetEnvironmentVariable(environmentName, secretValue);

        try
        {
            using var client = _factory.CreateClient();
            var request = new ProviderSettingsRequest(
                Enabled: true,
                CredentialReference: new CredentialReference(CredentialReferenceKind.Environment, environmentName),
                DefaultModels: new Dictionary<ProviderCapability, string>
                {
                    [ProviderCapability.ImageGeneration] = "mock-image-v1",
                    [ProviderCapability.ImageEditing] = "mock-image-v1",
                },
                MaxConcurrency: 3,
                TimeoutSeconds: 90,
                MaxRetries: 4,
                AllowedOperations:
                [
                    ProviderCapability.ImageGeneration,
                    ProviderCapability.ImageEditing,
                ],
                Priority: 10,
                FallbackPriority: 20);

            using var saveResponse = await client.PutAsJsonAsync(
                $"/api/providers/{MockImageProvider.ProviderId}/settings",
                request);
            Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);
            var responseBody = await saveResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain(secretValue, responseBody, StringComparison.Ordinal);
            Assert.Contains(environmentName, responseBody, StringComparison.Ordinal);

            var applicationSettings = _factory.Services.GetRequiredService<IApplicationSettingsRepository>();
            var persisted = await applicationSettings.GetAsync($"providers.settings.{MockImageProvider.ProviderId}");
            Assert.NotNull(persisted);
            Assert.Contains(environmentName, persisted, StringComparison.Ordinal);
            Assert.DoesNotContain(secretValue, persisted, StringComparison.Ordinal);

            var credentialResolver = _factory.Services.GetRequiredService<ICredentialResolver>();
            var resolved = await credentialResolver.ResolveAsync(request.CredentialReference!);
            Assert.NotNull(resolved);
            using (resolved)
            {
                Assert.Equal(secretValue, new string(resolved.Value.Span));
                Assert.Equal("***", resolved.ToString());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, null);
        }
    }

    [Fact]
    public async Task ProviderSettings_RejectUnsupportedModelCapabilityPair()
    {
        using var client = _factory.CreateClient();
        var request = new ProviderSettingsRequest(
            Enabled: true,
            CredentialReference: null,
            DefaultModels: new Dictionary<ProviderCapability, string>
            {
                [ProviderCapability.ImageGeneration] = "mock-video-v1",
            },
            MaxConcurrency: 1,
            TimeoutSeconds: 30,
            MaxRetries: 1,
            AllowedOperations: [ProviderCapability.ImageGeneration],
            Priority: 100,
            FallbackPriority: 100);

        using var response = await client.PutAsJsonAsync(
            $"/api/providers/{MockImageProvider.ProviderId}/settings",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MockProviders_NormalizeSuccessDelayAndFailureScenarios()
    {
        var control = _factory.Services.GetRequiredService<MockProviderControl>();
        var director = _factory.Services.GetRequiredService<IDirectorProvider>();
        var image = _factory.Services.GetRequiredService<IImageGenerationProvider>();
        var video = _factory.Services.GetRequiredService<IVideoGenerationProvider>();

        try
        {
            control.Set(
                MockDirectorProvider.ProviderId,
                new MockProviderBehavior(MockProviderScenario.DelayedSuccess, TimeSpan.FromMilliseconds(1)));
            var directorResult = await director.PlanAsync(new DirectorRequest(
                "mock-director-v1",
                "plan this song",
                TimeSpan.FromMinutes(3)));
            Assert.True(directorResult.IsSuccess);
            Assert.NotNull(directorResult.Value);

            control.Set(
                MockImageProvider.ProviderId,
                new MockProviderBehavior(
                    MockProviderScenario.RateLimited,
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(12)));
            var imageResult = await image.GenerateImageAsync(new ImageGenerationRequest(
                "mock-image-v1",
                "frame",
                1024,
                1024,
                []));
            Assert.False(imageResult.IsSuccess);
            Assert.NotNull(imageResult.Failure);
            Assert.Equal(ProviderFailureCode.RateLimited, imageResult.Failure.Code);
            Assert.True(imageResult.Failure.Retryable);
            Assert.Equal(TimeSpan.FromSeconds(12), imageResult.Failure.RetryAfter);

            control.Set(
                MockVideoProvider.ProviderId,
                new MockProviderBehavior(MockProviderScenario.QuotaExhausted, TimeSpan.Zero));
            var videoResult = await video.GenerateVideoAsync(new VideoGenerationRequest(
                "mock-video-v1",
                "animate",
                TimeSpan.FromSeconds(6),
                "16:9",
                "1920x1080"));
            Assert.False(videoResult.IsSuccess);
            Assert.NotNull(videoResult.Failure);
            Assert.Equal(ProviderFailureCode.QuotaExhausted, videoResult.Failure.Code);
            Assert.False(videoResult.Failure.Retryable);

            control.Set(
                MockImageProvider.ProviderId,
                new MockProviderBehavior(MockProviderScenario.Rejected, TimeSpan.Zero));
            imageResult = await image.GenerateImageAsync(new ImageGenerationRequest(
                "mock-image-v1",
                "rejected",
                1024,
                1024,
                []));
            Assert.NotNull(imageResult.Failure);
            Assert.Equal(ProviderFailureCode.ModerationRejected, imageResult.Failure.Code);

            control.Set(
                MockVideoProvider.ProviderId,
                new MockProviderBehavior(MockProviderScenario.TransientFailure, TimeSpan.Zero));
            videoResult = await video.GenerateVideoAsync(new VideoGenerationRequest(
                "mock-video-v1",
                "transient",
                TimeSpan.FromSeconds(4),
                "16:9",
                "1280x720"));
            Assert.NotNull(videoResult.Failure);
            Assert.Equal(ProviderFailureCode.TransientFailure, videoResult.Failure.Code);
            Assert.True(videoResult.Failure.Retryable);

            control.Set(
                MockVideoProvider.ProviderId,
                new MockProviderBehavior(MockProviderScenario.PermanentFailure, TimeSpan.Zero));
            videoResult = await video.GenerateVideoAsync(new VideoGenerationRequest(
                "mock-video-v1",
                "permanent",
                TimeSpan.FromSeconds(4),
                "16:9",
                "1280x720"));
            Assert.NotNull(videoResult.Failure);
            Assert.Equal(ProviderFailureCode.PermanentFailure, videoResult.Failure.Code);
            Assert.False(videoResult.Failure.Retryable);
        }
        finally
        {
            control.Reset(MockDirectorProvider.ProviderId);
            control.Reset(MockImageProvider.ProviderId);
            control.Reset(MockVideoProvider.ProviderId);
        }
    }
}
