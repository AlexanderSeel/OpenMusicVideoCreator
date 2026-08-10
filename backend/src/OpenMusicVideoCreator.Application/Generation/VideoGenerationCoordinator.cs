using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Planning;
using OpenMusicVideoCreator.Domain.Projects;

namespace OpenMusicVideoCreator.Application.Generation;

public interface IVideoGenerationSettingsRepository
{
    Task<SceneVideoGenerationSettings?> GetAsync(Guid projectId, Guid sceneId, CancellationToken cancellationToken = default);
    Task UpsertAsync(SceneVideoGenerationSettings settings, CancellationToken cancellationToken = default);
}

public interface IImageToVideoProviderResolver
{
    IImageToVideoProvider Resolve(string providerId);
}

public sealed record SceneVideoGenerationJobPayload(
    Guid VariantId,
    Guid PromptVersionId,
    Guid StartKeyframeVariantId,
    Guid? EndKeyframeVariantId,
    string Prompt,
    MediaLocation StartFrame,
    MediaLocation? EndFrame,
    double DurationSeconds,
    string AspectRatio,
    string Resolution,
    bool AllowFallback);

public sealed class VideoGenerationCoordinator
{
    public const string JobType = "scene.video.generate";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IProjectRepository _projects;
    private readonly IStoryboardRepository _storyboards;
    private readonly IPromptHistoryRepository _prompts;
    private readonly KeyframeVariantService _keyframes;
    private readonly KeyframeApprovalService _keyframeApprovals;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IProviderCatalog _catalog;
    private readonly ProviderSettingsService _providerSettings;
    private readonly IVideoGenerationSettingsRepository _settings;
    private readonly ClipVariantService _clips;
    private readonly IJobQueue _jobs;
    private readonly TimeProvider _timeProvider;

    public VideoGenerationCoordinator(
        IProjectRepository projects,
        IStoryboardRepository storyboards,
        IPromptHistoryRepository prompts,
        KeyframeVariantService keyframes,
        KeyframeApprovalService keyframeApprovals,
        IMediaAssetRepository mediaAssets,
        IProviderCatalog catalog,
        ProviderSettingsService providerSettings,
        IVideoGenerationSettingsRepository settings,
        ClipVariantService clips,
        IJobQueue jobs,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _storyboards = storyboards;
        _prompts = prompts;
        _keyframes = keyframes;
        _keyframeApprovals = keyframeApprovals;
        _mediaAssets = mediaAssets;
        _catalog = catalog;
        _providerSettings = providerSettings;
        _settings = settings;
        _clips = clips;
        _jobs = jobs;
        _timeProvider = timeProvider;
    }

    public async Task<SceneVideoGenerationSettings> GetSettingsAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var (project, _) = await RequireSceneAsync(projectId, sceneId, cancellationToken);
        return await _settings.GetAsync(projectId, sceneId, cancellationToken)
            ?? CreateDefaultSettings(project, sceneId);
    }

    public async Task<SceneVideoGenerationSettings> SaveSettingsAsync(
        SceneVideoGenerationSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings.Validate();
        var (project, scene) = await RequireSceneAsync(settings.ProjectId, settings.SceneId, cancellationToken);
        if (settings.ProviderId is not null)
        {
            var selection = await ResolveProviderAsync(project, settings, cancellationToken);
            ResolveDuration(scene, settings, selection.Model);
            ResolveResolution(project, settings, selection.Model);
            ValidateAspectRatio(project, selection.Model);
            if (settings.UseEndFrame && !selection.Model.SupportsEndFrame)
            {
                throw new ArgumentException($"Model '{selection.Model.ModelId}' does not support an end frame.");
            }
        }

        var saved = settings with { UpdatedUtc = GetUtcNow() };
        await _settings.UpsertAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<SceneClipVariant> QueueAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var (project, scene) = await RequireSceneAsync(projectId, sceneId, cancellationToken);
        var settings = await _settings.GetAsync(projectId, sceneId, cancellationToken)
            ?? CreateDefaultSettings(project, sceneId);
        var selection = await ResolveProviderAsync(project, settings, cancellationToken);
        var duration = ResolveDuration(scene, settings, selection.Model);
        var resolution = ResolveResolution(project, settings, selection.Model);
        var aspectRatio = ResolveAspectRatio(project.AspectRatio);
        ValidateAspectRatio(project, selection.Model);

        if (settings.UseEndFrame && !selection.Model.SupportsEndFrame)
        {
            throw new ArgumentException($"Model '{selection.Model.ModelId}' does not support an end frame.");
        }

        var approved = await ResolveApprovedKeyframesAsync(projectId, sceneId, settings.UseEndFrame, cancellationToken);
        var prompt = await RequireSelectedPromptAsync(projectId, scene, cancellationToken);
        var estimatedCost = selection.Provider.Id == "mock-video" ? 0m : (decimal?)null;

        var planned = await _clips.RegisterPlannedAsync(
            projectId,
            sceneId,
            prompt.Id,
            approved.Start.Id,
            approved.End?.Id,
            selection.Provider.Id,
            selection.Model.ModelId,
            duration,
            aspectRatio,
            resolution,
            estimatedCost,
            "USD",
            cancellationToken);

        try
        {
            var motionPrompt = $"{prompt.FinalProviderPrompt}\n\nAnimation directive: preserve the approved keyframe identity and composition while animating the described action/camera motion for approximately {duration.TotalSeconds:0.##} seconds.";
            var payload = new SceneVideoGenerationJobPayload(
                planned.Id,
                prompt.Id,
                approved.Start.Id,
                approved.End?.Id,
                motionPrompt,
                approved.StartLocation,
                approved.EndLocation,
                duration.TotalSeconds,
                aspectRatio,
                resolution,
                settings.AllowFallback);

            var dependencies = new[] { approved.Start.JobId, approved.End?.JobId }
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();

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
                dependencies,
                cancellationToken);
            return await _clips.AttachJobAsync(projectId, planned.Id, job.Id, cancellationToken);
        }
        catch
        {
            await _clips.DeleteAsync(projectId, planned.Id, cancellationToken);
            throw;
        }
    }

    public static SceneVideoGenerationJobPayload DeserializePayload(string payloadJson) =>
        JsonSerializer.Deserialize<SceneVideoGenerationJobPayload>(payloadJson, JsonOptions)
        ?? throw new InvalidDataException("Scene video generation job payload is invalid.");

    private async Task<(MusicVideoProject Project, StoryboardScene Scene)> RequireSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        var storyboard = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Create a Director storyboard before generating video clips.");
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
            throw new InvalidOperationException("Select or generate a scene prompt before video generation.");
        }

        return (await _prompts.ListBySceneAsync(projectId, scene.Id, cancellationToken))
            .SingleOrDefault(prompt => prompt.Id == selectedPromptId)
            ?? throw new InvalidDataException($"Scene references missing prompt version '{selectedPromptId}'.");
    }

    private async Task<ApprovedKeyframes> ResolveApprovedKeyframesAsync(
        Guid projectId,
        Guid sceneId,
        bool useEndFrame,
        CancellationToken cancellationToken)
    {
        if (!await _keyframeApprovals.IsCurrentSelectionApprovedAsync(projectId, sceneId, cancellationToken))
        {
            throw new InvalidOperationException("Approve the current scene keyframes before video generation.");
        }

        var approval = await _keyframeApprovals.GetAsync(projectId, sceneId, cancellationToken)
            ?? throw new InvalidDataException("Approved keyframe selection could not be loaded.");
        var start = await _keyframes.GetAsync(projectId, approval.StartVariantId, cancellationToken)
            ?? throw new InvalidDataException("Approved start keyframe variant is missing.");
        var end = approval.EndVariantId is Guid endId
            ? await _keyframes.GetAsync(projectId, endId, cancellationToken)
            : null;

        if (start.State != GenerationVariantState.Completed || start.MediaAssetId is not Guid startMediaId)
        {
            throw new InvalidOperationException("Approved start keyframe is not complete.");
        }
        if (useEndFrame && (end is null || end.State != GenerationVariantState.Completed || end.MediaAssetId is null))
        {
            throw new InvalidOperationException("This scene requires an approved completed end keyframe.");
        }

        var startMedia = await _mediaAssets.GetAsync(startMediaId, cancellationToken)
            ?? throw new InvalidDataException("Approved start keyframe media is missing.");
        MediaLocation? endLocation = null;
        if (useEndFrame && end?.MediaAssetId is Guid endMediaId)
        {
            var endMedia = await _mediaAssets.GetAsync(endMediaId, cancellationToken)
                ?? throw new InvalidDataException("Approved end keyframe media is missing.");
            endLocation = new MediaLocation(endMedia.Location);
        }

        return new ApprovedKeyframes(start, useEndFrame ? end : null, new MediaLocation(startMedia.Location), endLocation);
    }

    private async Task<ProviderSelection> ResolveProviderAsync(
        MusicVideoProject project,
        SceneVideoGenerationSettings settings,
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
            if (!providerSettings.AllowedOperations.Contains(ProviderCapability.ImageToVideo)) continue;
            if (!providerSettings.DefaultModels.TryGetValue(ProviderCapability.ImageToVideo, out var modelId)) continue;
            try
            {
                return ValidateSelection(descriptor, providerSettings, modelId);
            }
            catch (ArgumentException)
            {
                // Continue across enabled providers when one has stale model settings.
            }
        }

        throw new InvalidOperationException($"No enabled image-to-video provider is available for project '{project.Id}'.");
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
        if (!settings.AllowedOperations.Contains(ProviderCapability.ImageToVideo))
        {
            throw new InvalidOperationException($"Provider '{provider.Id}' does not allow image-to-video generation.");
        }
        var model = provider.Models.SingleOrDefault(candidate => string.Equals(candidate.ModelId, modelId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Model '{modelId}' is not registered for provider '{provider.Id}'.");
        if (!model.Capabilities.Contains(ProviderCapability.ImageToVideo) || !model.SupportsStartFrame)
        {
            throw new ArgumentException($"Model '{modelId}' does not support start-frame image-to-video generation.");
        }
        return new ProviderSelection(provider, model, settings);
    }

    private static TimeSpan ResolveDuration(
        StoryboardScene scene,
        SceneVideoGenerationSettings settings,
        ProviderModelDescriptor model)
    {
        var desired = settings.DurationSeconds ?? Math.Max(1, (int)Math.Round(scene.End.TotalSeconds - scene.Start.TotalSeconds));
        if (model.SupportedDurationsSeconds.Count == 0) return TimeSpan.FromSeconds(desired);
        var resolved = model.SupportedDurationsSeconds
            .OrderBy(candidate => Math.Abs(candidate - desired))
            .ThenBy(candidate => candidate)
            .First();
        return TimeSpan.FromSeconds(resolved);
    }

    private static string ResolveResolution(
        MusicVideoProject project,
        SceneVideoGenerationSettings settings,
        ProviderModelDescriptor model)
    {
        var desired = settings.Resolution ?? $"{project.Resolution.Width}x{project.Resolution.Height}";
        if (model.SupportedResolutions.Count == 0) return desired;
        return model.SupportedResolutions.FirstOrDefault(candidate => string.Equals(candidate, desired, StringComparison.OrdinalIgnoreCase))
            ?? model.SupportedResolutions.FirstOrDefault(candidate => AspectMatches(candidate, project.AspectRatio))
            ?? model.SupportedResolutions[0];
    }

    private static void ValidateAspectRatio(MusicVideoProject project, ProviderModelDescriptor model)
    {
        if (model.SupportedAspectRatios.Count == 0) return;
        var aspect = ResolveAspectRatio(project.AspectRatio);
        if (!model.SupportedAspectRatios.Contains(aspect, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Model '{model.ModelId}' does not support project aspect ratio '{aspect}'.");
        }
    }

    private static bool AspectMatches(string resolution, ProjectAspectRatio aspectRatio)
    {
        if (!SceneVideoGenerationSettings.TryParseResolution(resolution, out var width, out var height)) return false;
        return aspectRatio switch
        {
            ProjectAspectRatio.Landscape16x9 => width > height,
            ProjectAspectRatio.Portrait9x16 => height > width,
            ProjectAspectRatio.Square1x1 => width == height,
            _ => false,
        };
    }

    private static string ResolveAspectRatio(ProjectAspectRatio aspectRatio) => aspectRatio switch
    {
        ProjectAspectRatio.Landscape16x9 => "16:9",
        ProjectAspectRatio.Portrait9x16 => "9:16",
        ProjectAspectRatio.Square1x1 => "1:1",
        _ => throw new ArgumentOutOfRangeException(nameof(aspectRatio), aspectRatio, "Unknown project aspect ratio."),
    };

    private SceneVideoGenerationSettings CreateDefaultSettings(MusicVideoProject project, Guid sceneId) =>
        new(project.Id, sceneId, null, null, false, null, null, project.Preset != GenerationPreset.Custom, GetUtcNow());

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

    private sealed record ApprovedKeyframes(
        KeyframeVariant Start,
        KeyframeVariant? End,
        MediaLocation StartLocation,
        MediaLocation? EndLocation);
}
