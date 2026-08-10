using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Generation;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Planning;
using OpenMusicVideoCreator.Domain.Generation;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Projects;
using OpenMusicVideoCreator.Domain.Rendering;

namespace OpenMusicVideoCreator.Application.Rendering;

public interface IProjectRenderRepository
{
    Task<IReadOnlyList<ProjectRenderRecord>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectRenderRecord?> GetAsync(Guid projectId, Guid renderId, CancellationToken cancellationToken = default);
    Task UpsertAsync(ProjectRenderRecord render, CancellationToken cancellationToken = default);
}

public sealed record RenderEngineResult(
    Stream Content,
    string FileName,
    string MimeType,
    int Width,
    int Height,
    TimeSpan Duration,
    string CommandLog) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public interface IProjectRenderEngine
{
    Task<RenderEngineResult> RenderAsync(ProjectRenderManifest manifest, CancellationToken cancellationToken = default);
}

public sealed record ProjectRenderJobPayload(Guid RenderId);

public sealed class ProjectRenderService
{
    public const string JobType = "project.render";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProjectRepository _projects;
    private readonly IStoryboardRepository _storyboards;
    private readonly IClipVariantRepository _clips;
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly IProjectRenderRepository _renders;
    private readonly IJobQueue _jobs;
    private readonly TimeProvider _timeProvider;

    public ProjectRenderService(
        IProjectRepository projects,
        IStoryboardRepository storyboards,
        IClipVariantRepository clips,
        IMediaAssetRepository mediaAssets,
        IProjectRenderRepository renders,
        IJobQueue jobs,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _storyboards = storyboards;
        _clips = clips;
        _mediaAssets = mediaAssets;
        _renders = renders;
        _jobs = jobs;
        _timeProvider = timeProvider;
    }

    public Task<IReadOnlyList<ProjectRenderRecord>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        _renders.ListAsync(projectId, cancellationToken);

    public Task<ProjectRenderRecord?> GetAsync(
        Guid projectId,
        Guid renderId,
        CancellationToken cancellationToken = default) =>
        _renders.GetAsync(projectId, renderId, cancellationToken);

    public async Task<ProjectRenderRecord> QueueAsync(
        Guid projectId,
        ProjectRenderKind kind,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.GetAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");
        var storyboard = await _storyboards.GetLatestAsync(projectId, cancellationToken)
            ?? throw new InvalidOperationException("Create a storyboard before rendering.");
        var songReference = project.References.SingleOrDefault(reference => reference.Kind == ProjectReferenceKind.Song)
            ?? throw new InvalidOperationException("Attach the original song before rendering.");
        var song = await _mediaAssets.GetAsync(songReference.ReferenceId, cancellationToken)
            ?? throw new InvalidOperationException("The original song media asset is missing.");
        if (song.ProjectId != projectId)
        {
            throw new InvalidOperationException("The project's Song reference does not point to project-owned media.");
        }

        var projectClips = await _clips.ListByProjectAsync(projectId, cancellationToken);
        var timeline = new List<RenderTimelineClip>(storyboard.Scenes.Count);
        var dependencies = new List<Guid>(storyboard.Scenes.Count);
        var timelineStart = 0d;

        foreach (var scene in storyboard.Scenes.OrderBy(scene => scene.Sequence))
        {
            var selected = projectClips.SingleOrDefault(clip =>
                clip.SceneId == scene.Id &&
                clip.IsSelected &&
                clip.State == GenerationVariantState.Completed &&
                clip.MediaAssetId is not null)
                ?? throw new InvalidOperationException($"Scene {scene.Sequence} requires one selected completed clip before rendering.");
            var media = await _mediaAssets.GetAsync(selected.MediaAssetId!.Value, cancellationToken)
                ?? throw new InvalidOperationException($"Selected clip media for Scene {scene.Sequence} is missing.");
            if (media.ProjectId != projectId || !media.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Selected clip media for Scene {scene.Sequence} is not a project video asset.");
            }

            var duration = scene.DurationSeconds;
            timeline.Add(new RenderTimelineClip(
                scene.Id,
                scene.Sequence,
                selected.Id,
                media.Id,
                timelineStart,
                duration,
                scene.TransitionIn ?? string.Empty));
            timelineStart += duration;
            if (selected.JobId is Guid dependencyId)
            {
                dependencies.Add(dependencyId);
            }
        }

        var (width, height) = ResolveOutputSize(project, kind);
        var manifest = new ProjectRenderManifest(
            project.Id,
            storyboard.Id,
            song.Id,
            kind,
            width,
            height,
            30,
            timeline,
            timelineStart,
            ComputeTimelineHash(storyboard.Id, song.Id, timeline));
        manifest.Validate();

        var existing = await _renders.ListAsync(projectId, cancellationToken);
        var now = GetUtcNow();
        var record = new ProjectRenderRecord(
            Guid.NewGuid(),
            projectId,
            existing.Select(item => item.Version).DefaultIfEmpty(0).Max() + 1,
            manifest,
            null,
            null,
            ProjectRenderState.Planned,
            null,
            null,
            now,
            now);
        record.Validate();
        await _renders.UpsertAsync(record, cancellationToken);

        var job = await _jobs.EnqueueAsync(
            new JobDefinition(
                projectId,
                SceneId: null,
                ParentJobId: null,
                JobType,
                JsonSerializer.Serialize(new ProjectRenderJobPayload(record.Id), JsonOptions),
                Priority: kind == ProjectRenderKind.Preview ? 120 : 100,
                MaxRetries: 1,
                EstimatedCost: 0m,
                Currency: "USD"),
            dependencies.Distinct().ToArray(),
            cancellationToken);

        record = record with
        {
            JobId = job.Id,
            State = ProjectRenderState.Queued,
            UpdatedUtc = GetUtcNow(),
        };
        record.Validate();
        await _renders.UpsertAsync(record, cancellationToken);
        return record;
    }

    public async Task<ProjectRenderRecord> MarkRenderingAsync(
        Guid projectId,
        Guid renderId,
        CancellationToken cancellationToken = default) =>
        await UpdateStateAsync(projectId, renderId, ProjectRenderState.Rendering, null, null, cancellationToken);

    public async Task<ProjectRenderRecord> CompleteAsync(
        Guid projectId,
        Guid renderId,
        Guid outputMediaAssetId,
        string commandLog,
        CancellationToken cancellationToken = default)
    {
        if (outputMediaAssetId == Guid.Empty) throw new ArgumentException("Output media asset ID is required.", nameof(outputMediaAssetId));
        var existing = await RequireAsync(projectId, renderId, cancellationToken);
        var completed = existing with
        {
            OutputMediaAssetId = outputMediaAssetId,
            State = ProjectRenderState.Completed,
            CommandLog = commandLog,
            ErrorMessage = null,
            UpdatedUtc = GetUtcNow(),
        };
        completed.Validate();
        await _renders.UpsertAsync(completed, cancellationToken);
        return completed;
    }

    public async Task<ProjectRenderRecord> FailAsync(
        Guid projectId,
        Guid renderId,
        string errorMessage,
        string? commandLog,
        CancellationToken cancellationToken = default) =>
        await UpdateStateAsync(projectId, renderId, ProjectRenderState.Failed, errorMessage, commandLog, cancellationToken);

    public static ProjectRenderJobPayload DeserializePayload(string json) =>
        JsonSerializer.Deserialize<ProjectRenderJobPayload>(json, JsonOptions)
        ?? throw new InvalidDataException("Render job payload could not be deserialized.");

    private async Task<ProjectRenderRecord> UpdateStateAsync(
        Guid projectId,
        Guid renderId,
        ProjectRenderState state,
        string? errorMessage,
        string? commandLog,
        CancellationToken cancellationToken)
    {
        var existing = await RequireAsync(projectId, renderId, cancellationToken);
        var updated = existing with
        {
            State = state,
            ErrorMessage = errorMessage,
            CommandLog = commandLog ?? existing.CommandLog,
            UpdatedUtc = GetUtcNow(),
        };
        updated.Validate();
        await _renders.UpsertAsync(updated, cancellationToken);
        return updated;
    }

    private async Task<ProjectRenderRecord> RequireAsync(Guid projectId, Guid renderId, CancellationToken cancellationToken) =>
        await _renders.GetAsync(projectId, renderId, cancellationToken)
        ?? throw new KeyNotFoundException($"Render '{renderId}' was not found.");

    private static (int Width, int Height) ResolveOutputSize(MusicVideoProject project, ProjectRenderKind kind)
    {
        if (kind == ProjectRenderKind.Final)
        {
            return (Even(project.Resolution.Width), Even(project.Resolution.Height));
        }

        const int maxDimension = 960;
        var scale = Math.Min(1d, maxDimension / (double)Math.Max(project.Resolution.Width, project.Resolution.Height));
        return (Even(Math.Max(2, (int)Math.Round(project.Resolution.Width * scale))),
            Even(Math.Max(2, (int)Math.Round(project.Resolution.Height * scale))));
    }

    private static int Even(int value) => value % 2 == 0 ? value : value - 1;

    private static string ComputeTimelineHash(Guid storyboardId, Guid songId, IReadOnlyList<RenderTimelineClip> clips)
    {
        var canonical = new StringBuilder()
            .Append(storyboardId.ToString("N")).Append('|')
            .Append(songId.ToString("N"));
        foreach (var clip in clips.OrderBy(item => item.Sequence))
        {
            canonical.Append('|')
                .Append(clip.Sequence).Append(':')
                .Append(clip.SceneId.ToString("N")).Append(':')
                .Append(clip.ClipVariantId.ToString("N")).Append(':')
                .Append(clip.MediaAssetId.ToString("N")).Append(':')
                .Append(clip.TimelineStartSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                .Append(clip.DurationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
                .Append(clip.TransitionIn);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private DateTimeOffset GetUtcNow()
    {
        var now = _timeProvider.GetUtcNow();
        var ticks = now.Ticks - (now.Ticks % TimeSpan.TicksPerMicrosecond);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
