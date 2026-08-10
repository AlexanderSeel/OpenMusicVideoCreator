using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Infrastructure.Providers;

namespace OpenMusicVideoCreator.Infrastructure.Generation;

public sealed class ImageGenerationProviderResolver : IImageGenerationProviderResolver
{
    private readonly IReadOnlyDictionary<string, IImageGenerationProvider> _providers;

    public ImageGenerationProviderResolver(MockImageProvider mockImageProvider)
    {
        _providers = new Dictionary<string, IImageGenerationProvider>(StringComparer.Ordinal)
        {
            [MockImageProvider.ProviderId] = mockImageProvider,
        };
    }

    public IImageGenerationProvider Resolve(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId, out var provider)
            ? provider
            : throw new KeyNotFoundException($"No image-generation adapter is registered for provider '{providerId}'.");
    }
}
