using System.Collections.Concurrent;
using System.Text.Json;
using OpenMusicVideoCreator.Application.Providers;

namespace OpenMusicVideoCreator.Infrastructure.Providers;

public enum MockProviderScenario
{
    Success,
    DelayedSuccess,
    RateLimited,
    QuotaExhausted,
    Rejected,
    TransientFailure,
    PermanentFailure,
}

public sealed record MockProviderBehavior(
    MockProviderScenario Scenario,
    TimeSpan Delay,
    TimeSpan? RetryAfter = null)
{
    public static MockProviderBehavior Default { get; } = new(MockProviderScenario.Success, TimeSpan.Zero);
}

public sealed class MockProviderControl
{
    private readonly ConcurrentDictionary<string, MockProviderBehavior> _behaviors = new(StringComparer.Ordinal);

    public void Set(string providerId, MockProviderBehavior behavior)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentNullException.ThrowIfNull(behavior);
        _behaviors[providerId] = behavior;
    }

    public void Reset(string providerId) => _behaviors.TryRemove(providerId, out _);

    public MockProviderBehavior Get(string providerId) =>
        _behaviors.TryGetValue(providerId, out var behavior) ? behavior : MockProviderBehavior.Default;
}

public sealed class MockDirectorProvider : IDirectorProvider
{
    public const string ProviderId = "mock-director";
    private readonly MockProviderControl _control;

    public MockDirectorProvider(MockProviderControl control)
    {
        _control = control;
    }

    public async Task<ProviderResult<DirectorResponse>> PlanAsync(
        DirectorRequest request,
        CancellationToken cancellationToken = default)
    {
        var failure = await MockProviderExecution.ApplyBehaviorAsync(
            _control.Get(ProviderId),
            cancellationToken);
        if (failure is not null)
        {
            return ProviderResult<DirectorResponse>.Failed(failure);
        }

        var planJson = JsonSerializer.Serialize(new
        {
            provider = ProviderId,
            model = request.ModelId,
            durationSeconds = request.SongDuration.TotalSeconds,
            prompt = "mock-director-output",
        });

        return ProviderResult<DirectorResponse>.Success(
            new DirectorResponse(planJson, [], []),
            providerTaskId: $"mock:{Guid.NewGuid():N}");
    }
}

public sealed class MockImageProvider : IImageGenerationProvider, IImageEditingProvider
{
    public const string ProviderId = "mock-image";
    private readonly MockProviderControl _control;

    public MockImageProvider(MockProviderControl control)
    {
        _control = control;
    }

    public async Task<ProviderResult<ProviderAsset>> GenerateImageAsync(
        ImageGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var failure = await MockProviderExecution.ApplyBehaviorAsync(
            _control.Get(ProviderId),
            cancellationToken);
        if (failure is not null)
        {
            return ProviderResult<ProviderAsset>.Failed(failure);
        }

        return ProviderResult<ProviderAsset>.Success(
            new ProviderAsset(
                $"mock://image/{Guid.NewGuid():N}.png",
                "image/png",
                request.Width,
                request.Height),
            providerTaskId: $"mock:{Guid.NewGuid():N}");
    }

    public async Task<ProviderResult<ProviderAsset>> EditImageAsync(
        ImageEditingRequest request,
        CancellationToken cancellationToken = default)
    {
        var failure = await MockProviderExecution.ApplyBehaviorAsync(
            _control.Get(ProviderId),
            cancellationToken);
        if (failure is not null)
        {
            return ProviderResult<ProviderAsset>.Failed(failure);
        }

        return ProviderResult<ProviderAsset>.Success(
            new ProviderAsset($"mock://image/{Guid.NewGuid():N}.png", "image/png"),
            providerTaskId: $"mock:{Guid.NewGuid():N}");
    }
}

public sealed class MockVideoProvider : IVideoGenerationProvider, IImageToVideoProvider, IVideoToVideoProvider
{
    public const string ProviderId = "mock-video";
    private readonly MockProviderControl _control;

    public MockVideoProvider(MockProviderControl control)
    {
        _control = control;
    }

    public async Task<ProviderResult<ProviderAsset>> GenerateVideoAsync(
        VideoGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GenerateAsync(request.Duration, cancellationToken);
    }

    public async Task<ProviderResult<ProviderAsset>> GenerateVideoAsync(
        ImageToVideoRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GenerateAsync(request.Duration, cancellationToken);
    }

    public async Task<ProviderResult<ProviderAsset>> TransformVideoAsync(
        VideoToVideoRequest request,
        CancellationToken cancellationToken = default)
    {
        return await GenerateAsync(duration: null, cancellationToken);
    }

    private async Task<ProviderResult<ProviderAsset>> GenerateAsync(
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        var failure = await MockProviderExecution.ApplyBehaviorAsync(
            _control.Get(ProviderId),
            cancellationToken);
        if (failure is not null)
        {
            return ProviderResult<ProviderAsset>.Failed(failure);
        }

        return ProviderResult<ProviderAsset>.Success(
            new ProviderAsset(
                $"mock://video/{Guid.NewGuid():N}.mp4",
                "video/mp4",
                Duration: duration),
            providerTaskId: $"mock:{Guid.NewGuid():N}");
    }
}

internal static class MockProviderExecution
{
    public static async Task<ProviderFailure?> ApplyBehaviorAsync(
        MockProviderBehavior behavior,
        CancellationToken cancellationToken)
    {
        if (behavior.Delay > TimeSpan.Zero)
        {
            await Task.Delay(behavior.Delay, cancellationToken);
        }

        return behavior.Scenario switch
        {
            MockProviderScenario.Success or MockProviderScenario.DelayedSuccess => null,
            MockProviderScenario.RateLimited => new ProviderFailure(
                ProviderFailureCode.RateLimited,
                "Mock provider rate limit reached.",
                Retryable: true,
                behavior.RetryAfter ?? TimeSpan.FromSeconds(30),
                "mock_rate_limit"),
            MockProviderScenario.QuotaExhausted => new ProviderFailure(
                ProviderFailureCode.QuotaExhausted,
                "Mock provider quota exhausted.",
                Retryable: false,
                ProviderCode: "mock_quota"),
            MockProviderScenario.Rejected => new ProviderFailure(
                ProviderFailureCode.ModerationRejected,
                "Mock provider rejected the request.",
                Retryable: false,
                ProviderCode: "mock_rejected"),
            MockProviderScenario.TransientFailure => new ProviderFailure(
                ProviderFailureCode.TransientFailure,
                "Mock transient provider failure.",
                Retryable: true,
                behavior.RetryAfter ?? TimeSpan.FromSeconds(1),
                "mock_transient"),
            MockProviderScenario.PermanentFailure => new ProviderFailure(
                ProviderFailureCode.PermanentFailure,
                "Mock permanent provider failure.",
                Retryable: false,
                ProviderCode: "mock_permanent"),
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior.Scenario, "Unknown mock provider scenario."),
        };
    }
}
