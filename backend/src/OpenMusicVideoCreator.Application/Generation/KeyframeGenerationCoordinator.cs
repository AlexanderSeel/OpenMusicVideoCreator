using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Costs;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Library;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Library;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Planning;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Generation;

public interface IKeyframeGenerationSettingsRepository
{
    Task<SceneKeyframeGenerationSettings?> GetAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default);
    Task UpsertAsync(SceneKeyframeGenerationSettings settings, CancellationToken cancellationToken = default);
}

public interface IImageGenerationProviderResolver
{
    IImageGenerationProvider Resolve(string providerId);
}

public sealed record KeyframeGenerationJobPayload(
    Guid VariantId,
    Guid PromptVersionId,
    KeyframeRole Role,
    string Prompt,
    int Width,
    int Height,
    IReadOnlyList<MediaLocation> References,
    int? Seed,
    string? NegativePrompt);

public sealed class KeyframeGenerationCoordinator
{
    public const string JobType = "keyframe.image.generate";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectRepository _projects;
    private readonly IStoryboardRepository _storyboards;
    private readonly IPromptHistoryRepository _prompts;
    private readonly IVisualLibraryRepository _visualLibrary;
    private readonly IAssetLibraryRepository _assetLibrary;
    private readonly IProjectCharacterStateRepository _characterStates;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IProviderCatalog _catalog;
    private readonly ProviderSettingsService _providerSettings;
    private readonly IKeyframeGenerationSettingsRepository _settings;
    private readonly KeyframeVariantService _variants;
    private readonly IJobQueue _jobs;
    private readonly TimeProvider _timeProvider;
    private readonly ProjectCostService? _costs;

    public KeyframeGenerationCoordinator(
        IProjectRepository projects,
        IStoryboardRepository storyboards,
        IPromptHistoryRepository prompts,
        IVisualLibraryRepository visualLibrary,
        IAssetLibraryRepository assetLibrary,
        IProjectCharacterStateRepository characterStates,
        IMediaAssetRepository mediaAssets,
        IProviderCatalog catalog,
        ProviderSettingsService providerSettings,
        IKeyframeGenerationSettingsRepository settings,
        KeyframeVariantService variants,
        IJobQueue jobs,
        TimeProvider timeProvider,
        ProjectCostService? costs = null)
    {
        _projects = projects;
        _storyboards = storyboards;
        _prompts = prompts;
        _visualLibrary = visualLibrary;
        _assetLibrary = assetLibrary;
        _characterStates = characterStates;
        _mediaAssets = mediaAssets;
        _catalog = catalog;
        _providerSettings = providerSettings;
        _settings = settings;
        _variants = variants;
        _jobs = jobs;
        _timeProvider = timeProvider;
        _costs = costs;
    }

    public async Task<SceneKeyframeGenerationSettings> GetSettingsAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        await RequireSceneAsync(projectId, sceneId, cancellationToken);
        return await _settings.GetAsync(projectId, sceneId, cancellationToken)
            ?? new SceneKeyframeGenerationSettings(projectId, sceneId, null, null, false, null, null, null, GetUtcNow());
    }

    public async Task<SceneKeyframeGenerationSettings> SaveSettingsAsync(
        SceneKeyframeGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings.Validate();
        var (project, _) = await RequireSceneAsync(settings.ProjectId, settings.SceneId, cancellationToken);
        if (settings.ProviderId is not null)
        {
            var selection = await ResolveProviderAsync(project, settings, cancellationToken);
            ValidateOptionalControls(selection.Model, settings);
            ResolveDimensions(project, settings, selection.Model);
        }

        var saved = settings with { UpdatedUtc = GetUtcNow() };
        await _settings.UpsertAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<KeyframeVariant>> QueueSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(projectId, sceneId, cancellationToken);
        var result = new List<KeyframeVariant>
        {
            await QueueAsync(projectId, sceneId, KeyframeRole.Start, cancellationToken),
        };
        if (settings.GenerateEndFrame)
        {
            result.Add(await QueueAsync(projectId, sceneId, KeyframeRole.End, cancellationToken));
        }
        return result;
    }

    public async Task<KeyframeVariant> QueueAsync(
        Guid projectId,
        Guid sceneId,
        KeyframeRole role,
        CancellationToken cancellationToken = default)
    {
        var (project, scene) = await RequireSceneAsync(projectId, sceneId, cancellationToken);
        var settings = await _settings.GetAsync(projectId, sceneId, cancellationToken)
            ?? new SceneKeyframeGenerationSettings(projectId, sceneId, null, null, false, null, null, null, GetUtcNow());
        var selection = await ResolveProviderAsync(project, settings, cancellationToken);
        ValidateOptionalControls(selection.Model, settings);
        var (width, height) = ResolveDimensions(project, settings, selection.Model);
        var prompt = await RequireSelectedPromptAsync(projectId, scene, cancellationToken);
        var references = await ResolveReferencesAsync(projectId, scene, selection.Model, cancellationToken);
        var estimatedCost = selection.Provider.Id == "mock-image" ? 0m : (decimal?)null;
        if (_costs is not null)
        {
            await _costs.EnsureCanReserveAsync(project, estimatedCost, "USD", cancellationToken);
        }

        var planned = await _variants.RegisterPlannedAsync(
            projectId,
            sceneId,
            role,
            prompt.Id,
            null,
            selection.Provider.Id,
            selection.Model.ModelId,
            estimatedCost,
            "USD",
            cancellationToken);

        try
        {
            var payload = new KeyframeGenerationJobPayload(
                planned.Id,
                prompt.Id,
                role,
                prompt.FinalProviderPrompt,
                width,
                height,
                references,
                selection.Model.SupportsSeed ? settings.Seed : null,
                selection.Model.SupportsNegativePrompt ? NormalizeOptional(settings.NegativePrompt) : null);
            var job = await _jobs.EnqueueAsync(
                new JobDefinition(
                    projectId,
                    sceneId,
                    null,
                    JobType,
                    JsonSerializer.Serialize(payload, JsonOptions),
                    selection.Provider.Id,
                    selection.Model.ModelId,
                    Priority: 100,
                    MaxRetries: selection.Settings.MaxRetries,
                    EstimatedCost: estimatedCost,
                    Currency: "USD"),
                cancellationToken: cancellationToken);
            return await _variants.AttachJobAsync(projectId, planned.Id, job.Id, cancellationToken);
        }
        catch
        {
            await _variants.DeleteAsync(projectId, planned.Id, cancellationToken);
            throw;
        }
    }

    public static KeyframeGenerationJobPayload DeserializePayload(string payloadJson) =>
        JsonSerializer.Deserialize<KeyframeGenerationJobPayload>(payloadJson, JsonOptions)
        ?? throw new InvalidDataException("Keyframe generation job payload is invalid.");

    private async Task<(MusicVideoProject Project, StoryboardScene Scene)> RequireSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        var storyboard = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Create a Director storyboard before generating keyframes.");
        var scene = storyboard.Scenes.SingleOrDefault(candidate => candidate.Id == sceneId)
            ?? throw new KeyNotFoundException($"Scene '{sceneId}' was not found in the latest storyboard.");
        return (project, scene);
    }

    private async Task<PromptVersion> RequireSelectedPromptAsync(
        Guid projectId,
        StoryboardScene scene,
        CancellationToken cancellationToken)
    {
        if (scene.SelectedPromptVersionId is not Guid selectedPromptId)
        {
            throw new InvalidOperationException("Select or generate a scene prompt before keyframe generation.");
        }
        return (await _prompts.ListBySceneAsync(projectId, scene.Id, cancellationToken))
            .SingleOrDefault(prompt => prompt.Id == selectedPromptId)
            ?? throw new InvalidDataException($"Scene references missing prompt version '{selectedPromptId}'.");
    }

    private async Task<ProviderSelection> ResolveProviderAsync(
        MusicVideoProject project,
        SceneKeyframeGenerationSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.ProviderId is not null && settings.ModelId is not null)
        {
            var descriptor = await _catalog.GetAsync(settings.ProviderId, cancellationToken)
                ?? throw new KeyNotFoundException($"Provider '{settings.ProviderId}' was not found.");
            var providerSettings = await _providerSettings.GetAsync(descriptor.Id, cancellationToken);
            return ValidateSelection(descriptor, providerSettings, settings.ModelId);
        }

        var descriptors = await _catalog.ListAsync(cancellationToken);
        var providerSettingsById = await _providerSettings.ListAsync(cancellationToken);
        foreach (var descriptor in descriptors
                     .Where(descriptor => providerSettingsById.TryGetValue(descriptor.Id, out var providerSettings) && providerSettings.Enabled)
                     .OrderBy(descriptor => providerSettingsById[descriptor.Id].Priority)
                     .ThenBy(descriptor => descriptor.Id, StringComparer.Ordinal))
        {
            var providerSettings = providerSettingsById[descriptor.Id];
            if (!providerSettings.AllowedOperations.Contains(ProviderCapability.ImageGeneration)) continue;
            if (!providerSettings.DefaultModels.TryGetValue(ProviderCapability.ImageGeneration, out var modelId)) continue;
            try
            {
                return ValidateSelection(descriptor, providerSettings, modelId);
            }
            catch (ArgumentException)
            {
                // Keep routing across other enabled providers when a stale provider setting is encountered.
            }
        }

        throw new InvalidOperationException($"No enabled image-generation provider is available for project '{project.Id}'.");
    }

    private static ProviderSelection ValidateSelection(
        ProviderDescriptor provider,
        ProviderSettings settings,
        string modelId)
    {
        if (!settings.Enabled)
        {
            throw new InvalidOperationException($"Provider '{provider.Id}' is disabled.");
        }
        if (!settings.AllowedOperations.Contains(ProviderCapability.ImageGeneration))
        {
            throw new InvalidOperationException($"Provider '{provider.Id}' does not allow image generation.");
        }
        var model = provider.Models.SingleOrDefault(candidate => string.Equals(candidate.ModelId, modelId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Model '{modelId}' is not registered for provider '{provider.Id}'.");
        if (!model.Capabilities.Contains(ProviderCapability.ImageGeneration))
        {
            throw new ArgumentException($"Model '{modelId}' does not support image generation.");
        }
        return new ProviderSelection(provider, model, settings);
    }

    private static void ValidateOptionalControls(ProviderModelDescriptor model, SceneKeyframeGenerationSettings settings)
    {
        if (settings.Seed is not null && !model.SupportsSeed)
        {
            throw new ArgumentException($"Model '{model.ModelId}' does not support seed control.");
        }
        if (!string.IsNullOrWhiteSpace(settings.NegativePrompt) && !model.SupportsNegativePrompt)
        {
            throw new ArgumentException($"Model '{model.ModelId}' does not support negative prompts.");
        }
    }

    private static (int Width, int Height) ResolveDimensions(
        MusicVideoProject project,
        SceneKeyframeGenerationSettings settings,
        ProviderModelDescriptor model)
    {
        var desired = settings.Resolution ?? $"{project.Resolution.Width}x{project.Resolution.Height}";
        if (model.SupportedResolutions.Count == 0)
        {
            if (!SceneKeyframeGenerationSettings.TryParseResolution(desired, out var width, out var height))
            {
                throw new ArgumentException("Could not resolve keyframe generation resolution.");
            }
            return (width, height);
        }

        var supported = model.SupportedResolutions.FirstOrDefault(candidate => string.Equals(candidate, desired, StringComparison.OrdinalIgnoreCase));
        if (supported is null)
        {
            supported = model.SupportedResolutions.FirstOrDefault(candidate => AspectMatches(candidate, project.AspectRatio))
                ?? model.SupportedResolutions[0];
        }
        if (!SceneKeyframeGenerationSettings.TryParseResolution(supported, out var resolvedWidth, out var resolvedHeight))
        {
            throw new InvalidDataException($"Provider model '{model.ModelId}' contains invalid resolution '{supported}'.");
        }
        return (resolvedWidth, resolvedHeight);
    }

    private async Task<IReadOnlyList<MediaLocation>> ResolveReferencesAsync(
        Guid projectId,
        StoryboardScene scene,
        ProviderModelDescriptor model,
        CancellationToken cancellationToken)
    {
        if (!model.SupportsReferences || model.MaxReferences <= 0) return [];

        var visualItems = await _visualLibrary.ListAsync(cancellationToken);
        var assetEntries = await _assetLibrary.ListAsync(cancellationToken);
        var assetsById = assetEntries.ToDictionary(entry => entry.Id);
        var characterStates = await _characterStates.ListAsync(projectId, cancellationToken);
        var entryIds = new List<Guid>();

        foreach (var characterId in scene.CharacterIds)
        {
            var character = visualItems.SingleOrDefault(item => item.Id == characterId && item.Kind == VisualLibraryKind.Character);
            if (character is null) continue;
            var state = characterStates.SingleOrDefault(candidate => candidate.CharacterId == characterId);
            if (state?.OutfitId is Guid outfitId && character.Character is not null)
            {
                var outfit = character.Character.Outfits.SingleOrDefault(candidate => candidate.Id == outfitId);
                if (outfit is not null) entryIds.AddRange(outfit.AssetEntryIds);
            }
            entryIds.AddRange(character.AssetEntryIds);
        }
        foreach (var styleId in scene.StyleIds)
        {
            var style = visualItems.SingleOrDefault(item => item.Id == styleId && item.Kind == VisualLibraryKind.Style);
            if (style is not null) entryIds.AddRange(style.AssetEntryIds);
        }
        foreach (var locationId in scene.LocationIds)
        {
            var location = visualItems.SingleOrDefault(item => item.Id == locationId && item.Kind == VisualLibraryKind.Location);
            if (location is not null) entryIds.AddRange(location.AssetEntryIds);
        }

        var locations = new List<MediaLocation>(model.MaxReferences);
        foreach (var entryId in entryIds.Distinct())
        {
            if (!assetsById.TryGetValue(entryId, out var entry)) continue;
            var media = await _mediaAssets.GetAsync(entry.MediaAssetId, cancellationToken);
            if (media is null) continue;
            locations.Add(new MediaLocation(media.Location));
            if (locations.Count >= model.MaxReferences) break;
        }
        return locations;
    }

    private static bool AspectMatches(string resolution, ProjectAspectRatio aspectRatio)
    {
        if (!SceneKeyframeGenerationSettings.TryParseResolution(resolution, out var width, out var height)) return false;
        return aspectRatio switch
        {
            ProjectAspectRatio.Landscape16x9 => width > height,
            ProjectAspectRatio.Portrait9x16 => height > width,
            ProjectAspectRatio.Square1x1 => width == height,
            _ => false,
        };
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private sealed record ProviderSelection(
        ProviderDescriptor Provider,
        ProviderModelDescriptor Model,
        ProviderSettings Settings);
}
