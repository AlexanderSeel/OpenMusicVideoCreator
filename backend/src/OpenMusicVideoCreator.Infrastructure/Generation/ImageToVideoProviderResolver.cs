using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Infrastructure.Providers;

namespace OpenMusicVideoCreator.Infrastructure.Generation;

public sealed class ImageToVideoProviderResolver : IImageToVideoProviderResolver
{
    private readonly IReadOnlyDictionary<string, IImageToVideoProvider> _providers;

    public ImageToVideoProviderResolver(MockVideoProvider mockVideo)
    {
        _providers = new Dictionary<string, IImageToVideoProvider>(StringComparer.Ordinal)
        {
            [MockVideoProvider.ProviderId] = mockVideo,
        };
    }

    public IImageToVideoProvider Resolve(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId, out var provider)
            ? provider
            : throw new KeyNotFoundException($"No image-to-video provider adapter is registered for '{providerId}'.");
    }
}
