using OpenMusicVideoCreator.Application.Providers;

namespace OpenMusicVideoCreator.Api.Contracts.Providers;

public sealed record ProviderCatalogResponse(
    string Id,
    string DisplayName,
    IReadOnlyList<ProviderModelResponse> Models,
    ProviderSettingsResponse Settings)
{
    public static ProviderCatalogResponse FromDomain(
        ProviderDescriptor provider,
        ProviderSettings settings) => new(
        provider.Id,
        provider.DisplayName,
        provider.Models.Select(ProviderModelResponse.FromDomain).ToArray(),
        ProviderSettingsResponse.FromDomain(settings));
}

public sealed record ProviderModelResponse(
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
    IReadOnlyList<string> SupportedResolutions)
{
    public static ProviderModelResponse FromDomain(ProviderModelDescriptor model) => new(
        model.ModelId,
        model.DisplayName,
        model.Capabilities,
        model.SupportsReferences,
        model.SupportsStartFrame,
        model.SupportsEndFrame,
        model.SupportsSeed,
        model.SupportsNegativePrompt,
        model.SupportsNativeAudio,
        model.MaxReferences,
        model.SupportedDurationsSeconds,
        model.SupportedAspectRatios,
        model.SupportedResolutions);
}

public sealed record ProviderSettingsResponse(
    string ProviderId,
    bool Enabled,
    CredentialReference? CredentialReference,
    IReadOnlyDictionary<ProviderCapability, string> DefaultModels,
    int MaxConcurrency,
    double TimeoutSeconds,
    int MaxRetries,
    IReadOnlySet<ProviderCapability> AllowedOperations,
    int Priority,
    int FallbackPriority)
{
    public static ProviderSettingsResponse FromDomain(ProviderSettings settings) => new(
        settings.ProviderId,
        settings.Enabled,
        settings.Credential,
        settings.DefaultModels,
        settings.MaxConcurrency,
        settings.Timeout.TotalSeconds,
        settings.MaxRetries,
        settings.AllowedOperations,
        settings.Priority,
        settings.FallbackPriority);
}

public sealed record ProviderSettingsRequest(
    bool Enabled,
    CredentialReference? CredentialReference,
    IReadOnlyDictionary<ProviderCapability, string> DefaultModels,
    int MaxConcurrency,
    double TimeoutSeconds,
    int MaxRetries,
    IReadOnlySet<ProviderCapability> AllowedOperations,
    int Priority,
    int FallbackPriority)
{
    public ProviderSettings ToDomain(string providerId) => new(
        providerId,
        Enabled,
        CredentialReference,
        DefaultModels,
        MaxConcurrency,
        TimeSpan.FromSeconds(TimeoutSeconds),
        MaxRetries,
        AllowedOperations,
        Priority,
        FallbackPriority);
}
