using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;

namespace OpenMusicVideoCreator.Application.Providers;

public sealed class ProviderSettingsService
{
    private const string KeyPrefix = "providers.settings.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IApplicationSettingsRepository _settings;
    private readonly IProviderCatalog _catalog;

    public ProviderSettingsService(IApplicationSettingsRepository settings, IProviderCatalog catalog)
    {
        _settings = settings;
        _catalog = catalog;
    }

    public async Task<ProviderSettings> GetAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        var provider = await _catalog.GetAsync(providerId, cancellationToken)
            ?? throw new KeyNotFoundException($"Provider '{providerId}' was not found.");

        var value = await _settings.GetAsync(Key(provider.Id), cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            return CreateDefault(provider);
        }

        var persisted = JsonSerializer.Deserialize<PersistedProviderSettings>(value, JsonOptions)
            ?? throw new InvalidDataException($"Provider settings for '{provider.Id}' are invalid.");

        return Validate(provider, persisted.ToSettings(provider.Id));
    }

    public async Task<IReadOnlyDictionary<string, ProviderSettings>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = await _catalog.ListAsync(cancellationToken);
        var result = new Dictionary<string, ProviderSettings>(StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            result[provider.Id] = await GetAsync(provider.Id, cancellationToken);
        }

        return result;
    }

    public async Task<ProviderSettings> SaveAsync(
        ProviderSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var provider = await _catalog.GetAsync(settings.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Provider '{settings.ProviderId}' was not found.");

        var validated = Validate(provider, settings);
        var persisted = PersistedProviderSettings.FromSettings(validated);
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        await _settings.SetAsync(Key(provider.Id), json, cancellationToken);
        return validated;
    }

    private static string Key(string providerId) => KeyPrefix + providerId;

    private static ProviderSettings CreateDefault(ProviderDescriptor provider)
    {
        var capabilities = provider.Models
            .SelectMany(model => model.Capabilities)
            .ToHashSet();

        var defaultModels = capabilities
            .Select(capability => new
            {
                Capability = capability,
                Model = provider.Models.First(model => model.Capabilities.Contains(capability)).ModelId,
            })
            .ToDictionary(item => item.Capability, item => item.Model);

        return new ProviderSettings(
            provider.Id,
            Enabled: true,
            Credential: null,
            defaultModels,
            MaxConcurrency: 2,
            Timeout: TimeSpan.FromMinutes(5),
            MaxRetries: 2,
            capabilities,
            Priority: 100,
            FallbackPriority: 100);
    }

    private static ProviderSettings Validate(ProviderDescriptor provider, ProviderSettings settings)
    {
        if (!string.Equals(provider.Id, settings.ProviderId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Provider settings target does not match provider descriptor.", nameof(settings));
        }

        if (settings.MaxConcurrency < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Provider concurrency must be at least 1.");
        }

        if (settings.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Provider timeout must be positive.");
        }

        if (settings.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Provider retry count cannot be negative.");
        }

        var supportedCapabilities = provider.Models
            .SelectMany(model => model.Capabilities)
            .ToHashSet();

        if (settings.AllowedOperations.Any(capability => !supportedCapabilities.Contains(capability)))
        {
            throw new ArgumentException("Provider settings contain an unsupported operation.", nameof(settings));
        }

        foreach (var (capability, modelId) in settings.DefaultModels)
        {
            if (!settings.AllowedOperations.Contains(capability))
            {
                throw new ArgumentException($"Default model is configured for disabled capability '{capability}'.", nameof(settings));
            }

            var model = provider.Models.FirstOrDefault(candidate =>
                string.Equals(candidate.ModelId, modelId, StringComparison.Ordinal));
            if (model is null || !model.Capabilities.Contains(capability))
            {
                throw new ArgumentException(
                    $"Model '{modelId}' does not support capability '{capability}'.",
                    nameof(settings));
            }
        }

        if (settings.Credential is { Identifier.Length: 0 })
        {
            throw new ArgumentException("Credential reference identifier cannot be empty.", nameof(settings));
        }

        return settings with
        {
            DefaultModels = new Dictionary<ProviderCapability, string>(settings.DefaultModels),
            AllowedOperations = settings.AllowedOperations.ToHashSet(),
        };
    }

    private sealed record PersistedProviderSettings(
        bool Enabled,
        CredentialReference? Credential,
        IReadOnlyDictionary<ProviderCapability, string> DefaultModels,
        int MaxConcurrency,
        double TimeoutSeconds,
        int MaxRetries,
        IReadOnlySet<ProviderCapability> AllowedOperations,
        int Priority,
        int FallbackPriority)
    {
        public ProviderSettings ToSettings(string providerId) => new(
            providerId,
            Enabled,
            Credential,
            DefaultModels,
            MaxConcurrency,
            TimeSpan.FromSeconds(TimeoutSeconds),
            MaxRetries,
            AllowedOperations,
            Priority,
            FallbackPriority);

        public static PersistedProviderSettings FromSettings(ProviderSettings settings) => new(
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
}
