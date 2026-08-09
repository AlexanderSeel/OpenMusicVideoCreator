using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Domain.Analysis;
using OpenMusicVideoCreator.Domain.Projects;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class LyricTimingServiceTests
{
    [Fact]
    public async Task ApplyTranscription_PreservesAuthoritativeLyricsAndCreatesVersions()
    {
        var songAssetId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var project = CreateProject(songAssetId, "  First exact line  \nSecond exact line");
        var projectRepository = new SingleProjectRepository(project);
        var analysisRepository = new SingleAnalysisRepository(CreateAnalysis(project.Id, songAssetId));
        var timingRepository = new MemoryLyricTimingRepository();
        var service = new LyricTimingService(
            projectRepository,
            analysisRepository,
            timingRepository,
            new FixedTimeProvider());

        var segments = new[]
        {
            new TranscriptionSegment(TimeSpan.Zero, TimeSpan.FromSeconds(2.5), "first exact line"),
            new TranscriptionSegment(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5.5), "second exact line"),
        };

        var first = await service.ApplyTranscriptionAsync(project.Id, segments);
        var second = await service.ApplyTranscriptionAsync(project.Id, segments);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(2, timingRepository.Items.Count);
        Assert.Equal("  First exact line  ", first.Lines[0].Text);
        Assert.Equal("Second exact line", first.Lines[1].Text);
        Assert.Equal(0, first.Lines[0].StartSeconds);
        Assert.Equal(5.5, first.Lines[1].EndSeconds);
        Assert.All(first.Lines, line => Assert.True(line.IsMatched));
        Assert.Equal(1d, first.MatchedFraction);
        Assert.Equal("  First exact line  \nSecond exact line", project.Lyrics);
    }

    [Fact]
    public async Task ApplyTranscription_RejectsTimingOutsideAnalyzedSong()
    {
        var songAssetId = Guid.NewGuid();
        var project = CreateProject(songAssetId, "Only line");
        var service = new LyricTimingService(
            new SingleProjectRepository(project),
            new SingleAnalysisRepository(CreateAnalysis(project.Id, songAssetId)),
            new MemoryLyricTimingRepository(),
            new FixedTimeProvider());

        var segments = new[]
        {
            new TranscriptionSegment(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(31), "only line"),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.ApplyTranscriptionAsync(project.Id, segments));
    }

    private static MusicVideoProject CreateProject(Guid songAssetId, string lyrics)
    {
        var draft = new ProjectDraft(
            "Lyric timing",
            "Artist",
            lyrics,
            "Story",
            "Meaning",
            "Direction",
            "Mood",
            "Genre",
            ProjectAspectRatio.Landscape16x9,
            new OutputResolution(1920, 1080),
            ["YouTube"],
            GenerationPreset.Balanced,
            null,
            null,
            [new ProjectReference(ProjectReferenceKind.Song, songAssetId)]);
        return MusicVideoProject.Create(Guid.NewGuid(), draft, FixedUtc());
    }

    private static SongAnalysis CreateAnalysis(Guid projectId, Guid songAssetId) => new(
        Guid.NewGuid(),
        projectId,
        songAssetId,
        1,
        30,
        120,
        48000,
        2,
        "aac",
        192000,
        [],
        [],
        [],
        [new SongSection(Guid.NewGuid(), "Full song", SongSectionKind.Unknown, 0, 30, 0.5, AnalysisValueSource.Detected)],
        FixedUtc());

    private static DateTimeOffset FixedUtc() => new(2026, 8, 9, 17, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtc();
    }

    private sealed class SingleProjectRepository : OpenMusicVideoCreator.Application.Abstractions.IProjectRepository
    {
        private MusicVideoProject _project;

        public SingleProjectRepository(MusicVideoProject project) => _project = project;

        public Task<IReadOnlyList<MusicVideoProject>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MusicVideoProject>>([_project]);

        public Task<MusicVideoProject?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<MusicVideoProject?>(_project.Id == id ? _project : null);

        public Task UpsertAsync(MusicVideoProject project, CancellationToken cancellationToken = default)
        {
            _project = project;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class SingleAnalysisRepository : ISongAnalysisRepository
    {
        private readonly SongAnalysis _analysis;

        public SingleAnalysisRepository(SongAnalysis analysis) => _analysis = analysis;

        public Task<SongAnalysis?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SongAnalysis?>(_analysis.ProjectId == projectId ? _analysis : null);

        public Task<SongAnalysis?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<SongAnalysis?>(_analysis.Id == id ? _analysis : null);

        public Task<IReadOnlyList<SongAnalysis>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SongAnalysis>>([_analysis]);

        public Task UpsertAsync(SongAnalysis analysis, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemoryLyricTimingRepository : ILyricTimingRepository
    {
        public List<LyricTimingAnalysis> Items { get; } = [];

        public Task<LyricTimingAnalysis?> GetLatestAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Where(item => item.ProjectId == projectId).OrderByDescending(item => item.Version).FirstOrDefault());

        public Task<IReadOnlyList<LyricTimingAnalysis>> ListVersionsAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LyricTimingAnalysis>>(Items.Where(item => item.ProjectId == projectId).OrderByDescending(item => item.Version).ToArray());

        public Task UpsertAsync(LyricTimingAnalysis analysis, CancellationToken cancellationToken = default)
        {
            Items.Add(analysis);
            return Task.CompletedTask;
        }
    }
}
