using OpenMusicVideoCreator.Application.Providers;

namespace OpenMusicVideoCreator.Infrastructure.Providers;

public sealed class MockProviderCatalog : IProviderCatalog
{
    private static readonly IReadOnlyList<ProviderDescriptor> Providers =
    [
        new ProviderDescriptor(
            "mock-director",
            "Mock Director",
            [
                new ProviderModelDescriptor(
                    "mock-director",
                    "mock-director-v1",
                    "Mock Director v1",
                    new HashSet<ProviderCapability> { ProviderCapability.DirectorPlanning },
                    SupportsReferences: false,
                    SupportsStartFrame: false,
                    SupportsEndFrame: false,
                    SupportsSeed: false,
                    SupportsNegativePrompt: false,
                    SupportsNativeAudio: false,
                    MaxReferences: 0,
                    SupportedDurationsSeconds: [],
                    SupportedAspectRatios: [],
                    SupportedResolutions: []),
            ]),
        new ProviderDescriptor(
            "mock-image",
            "Mock Image",
            [
                new ProviderModelDescriptor(
                    "mock-image",
                    "mock-image-v1",
                    "Mock Image v1",
                    new HashSet<ProviderCapability>
                    {
                        ProviderCapability.ImageGeneration,
                        ProviderCapability.ImageEditing,
                    },
                    SupportsReferences: true,
                    SupportsStartFrame: false,
                    SupportsEndFrame: false,
                    SupportsSeed: true,
                    SupportsNegativePrompt: true,
                    SupportsNativeAudio: false,
                    MaxReferences: 8,
                    SupportedDurationsSeconds: [],
                    SupportedAspectRatios: ["16:9", "9:16", "1:1"],
                    SupportedResolutions: ["1024x1024", "1536x1024", "1024x1536"]),
            ]),
        new ProviderDescriptor(
            "mock-video",
            "Mock Video",
            [
                new ProviderModelDescriptor(
                    "mock-video",
                    "mock-video-v1",
                    "Mock Video v1",
                    new HashSet<ProviderCapability>
                    {
                        ProviderCapability.VideoGeneration,
                        ProviderCapability.ImageToVideo,
                        ProviderCapability.VideoToVideo,
                    },
                    SupportsReferences: true,
                    SupportsStartFrame: true,
                    SupportsEndFrame: true,
                    SupportsSeed: false,
                    SupportsNegativePrompt: false,
                    SupportsNativeAudio: false,
                    MaxReferences: 2,
                    SupportedDurationsSeconds: [4, 5, 6, 8, 10],
                    SupportedAspectRatios: ["16:9", "9:16", "1:1"],
                    SupportedResolutions: ["1280x720", "1920x1080"]),
            ]),
    ];

    public ValueTask<IReadOnlyList<ProviderDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Providers);
    }

    public ValueTask<ProviderDescriptor?> GetAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = Providers.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, providerId, StringComparison.Ordinal));
        return ValueTask.FromResult(provider);
    }
}
