using OpenMusicVideoCreator.Application.Abstractions;

namespace OpenMusicVideoCreator.Application.Providers;

public enum ProviderCapability
{
    TextGeneration,
    ImageGeneration,
    ImageEditing,
    VideoGeneration,
    ImageToVideo,
    VideoToVideo,
    LipSync,
    Upscale,
    Transcription,
    VisionEvaluation,
    DirectorPlanning,
}

public enum CredentialReferenceKind
{
    Environment,
    OperatingSystem,
    External,
}

public sealed record CredentialReference(CredentialReferenceKind Kind, string Identifier)
{
    public override string ToString() => $"{Kind}:{Identifier}";
}

public sealed record ProviderModelDescriptor(
    string ProviderId,
    string ModelId,
    string DisplayName,
    IReadOnlySet<ProviderCapability> Capabilities,
    bool SupportsReferences,
    bool SupportsStartFrame,
    bool SupportsEndFrame,
    bool SupportsSeed,
    bool SupportsNegativePrompt,
    bool SupportsNativeAudio,
    int MaxReferences,
    IReadOnlyList<int> SupportedDurationsSeconds,
    IReadOnlyList<string> SupportedAspectRatios,
    IReadOnlyList<string> SupportedResolutions);

public sealed record ProviderDescriptor(
    string Id,
    string DisplayName,
    IReadOnlyList<ProviderModelDescriptor> Models);

public interface IProviderCatalog
{
    ValueTask<IReadOnlyList<ProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<ProviderDescriptor?> GetAsync(string providerId, CancellationToken cancellationToken = default);
}

public sealed record ProviderSettings(
    string ProviderId,
    bool Enabled,
    CredentialReference? Credential,
    IReadOnlyDictionary<ProviderCapability, string> DefaultModels,
    int MaxConcurrency,
    TimeSpan Timeout,
    int MaxRetries,
    IReadOnlySet<ProviderCapability> AllowedOperations,
    int Priority,
    int FallbackPriority);

public interface ICredentialResolver
{
    ValueTask<ResolvedCredential?> ResolveAsync(
        CredentialReference reference,
        CancellationToken cancellationToken = default);
}

public sealed class ResolvedCredential : IDisposable
{
    private char[]? _value;

    public ResolvedCredential(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value.ToCharArray();
    }

    public ReadOnlyMemory<char> Value => _value ?? throw new ObjectDisposedException(nameof(ResolvedCredential));

    public void Dispose()
    {
        if (_value is null)
        {
            return;
        }

        Array.Clear(_value);
        _value = null;
    }

    public override string ToString() => "***";
}

public enum ProviderFailureCode
{
    RateLimited,
    ProviderUnavailable,
    QuotaExhausted,
    InsufficientCredits,
    AuthenticationFailed,
    ModerationRejected,
    InvalidParameters,
    UnsupportedCapability,
    NetworkFailure,
    Timeout,
    TransientFailure,
    PermanentFailure,
}

public sealed record ProviderFailure(
    ProviderFailureCode Code,
    string Message,
    bool Retryable,
    TimeSpan? RetryAfter = null,
    string? ProviderCode = null);

public sealed record ProviderUsage(decimal? EstimatedCost, decimal? ActualCost, string? Currency = "USD");

public sealed record ProviderResult<T>(
    T? Value,
    ProviderFailure? Failure,
    ProviderUsage Usage,
    string? ProviderTaskId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public bool IsSuccess => Failure is null;

    public static ProviderResult<T> Success(T value, ProviderUsage? usage = null, string? providerTaskId = null) =>
        new(value, null, usage ?? new ProviderUsage(null, null), providerTaskId);

    public static ProviderResult<T> Failed(ProviderFailure failure) =>
        new(default, failure, new ProviderUsage(null, null));
}

public sealed record ProviderAsset(string Uri, string MimeType, int? Width = null, int? Height = null, TimeSpan? Duration = null);

public sealed record TextGenerationRequest(string ModelId, string Prompt, string? SystemPrompt = null);
public sealed record TextGenerationResponse(string Text);
public interface ITextGenerationProvider
{
    Task<ProviderResult<TextGenerationResponse>> GenerateTextAsync(TextGenerationRequest request, CancellationToken cancellationToken = default);
}

public sealed record ImageGenerationRequest(string ModelId, string Prompt, int Width, int Height, IReadOnlyList<MediaLocation> References, int? Seed = null, string? NegativePrompt = null);
public interface IImageGenerationProvider
{
    Task<ProviderResult<ProviderAsset>> GenerateImageAsync(ImageGenerationRequest request, CancellationToken cancellationToken = default);
}

public sealed record ImageEditingRequest(string ModelId, MediaLocation Source, string Prompt, IReadOnlyList<MediaLocation> References);
public interface IImageEditingProvider
{
    Task<ProviderResult<ProviderAsset>> EditImageAsync(ImageEditingRequest request, CancellationToken cancellationToken = default);
}

public sealed record VideoGenerationRequest(string ModelId, string Prompt, TimeSpan Duration, string AspectRatio, string Resolution);
public interface IVideoGenerationProvider
{
    Task<ProviderResult<ProviderAsset>> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken cancellationToken = default);
}

public sealed record ImageToVideoRequest(string ModelId, MediaLocation StartFrame, MediaLocation? EndFrame, string Prompt, TimeSpan Duration);
public interface IImageToVideoProvider
{
    Task<ProviderResult<ProviderAsset>> GenerateVideoAsync(ImageToVideoRequest request, CancellationToken cancellationToken = default);
}

public sealed record VideoToVideoRequest(string ModelId, MediaLocation Source, string Prompt);
public interface IVideoToVideoProvider
{
    Task<ProviderResult<ProviderAsset>> TransformVideoAsync(VideoToVideoRequest request, CancellationToken cancellationToken = default);
}

public sealed record LipSyncRequest(string ModelId, MediaLocation Video, MediaLocation Audio);
public interface ILipSyncProvider
{
    Task<ProviderResult<ProviderAsset>> LipSyncAsync(LipSyncRequest request, CancellationToken cancellationToken = default);
}

public sealed record UpscaleRequest(string ModelId, MediaLocation Source, int Width, int Height);
public interface IUpscaleProvider
{
    Task<ProviderResult<ProviderAsset>> UpscaleAsync(UpscaleRequest request, CancellationToken cancellationToken = default);
}

public sealed record TranscriptionRequest(string ModelId, MediaLocation Audio, string? Language = null);
public sealed record TranscriptionResponse(string Text, IReadOnlyList<TranscriptionSegment> Segments);
public sealed record TranscriptionSegment(TimeSpan Start, TimeSpan End, string Text);
public interface ITranscriptionProvider
{
    Task<ProviderResult<TranscriptionResponse>> TranscribeAsync(TranscriptionRequest request, CancellationToken cancellationToken = default);
}

public sealed record VisionEvaluationRequest(string ModelId, MediaLocation Asset, IReadOnlyList<string> Criteria);
public sealed record VisionEvaluationResponse(decimal Score, IReadOnlyList<string> Findings);
public interface IVisionEvaluationProvider
{
    Task<ProviderResult<VisionEvaluationResponse>> EvaluateAsync(VisionEvaluationRequest request, CancellationToken cancellationToken = default);
}

public sealed record DirectorRequest(string ModelId, string Prompt, TimeSpan SongDuration);
public sealed record DirectorResponse(string PlanJson);
public interface IDirectorProvider
{
    Task<ProviderResult<DirectorResponse>> PlanAsync(DirectorRequest request, CancellationToken cancellationToken = default);
}
