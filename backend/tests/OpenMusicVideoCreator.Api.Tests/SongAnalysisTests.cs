using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Domain.Analysis;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Projects;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class SongAnalysisTests
{
    [Fact]
    public async Task AnalyzeAndEditSections_CreateDurableVersions()
    {
        using var storage = new TemporaryStorage();
        var factory = new DuckDbConnectionFactory(storage.Options);
        var database = new DuckDbDatabase(factory);
        await database.InitializeAsync();

        var projects = new DuckDbProjectRepository(factory);
        var media = new DuckDbMediaAssetRepository(factory);
        var analyses = new DuckDbSongAnalysisRepository(factory);
        var projectId = Guid.NewGuid();
        var songAssetId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        var project = MusicVideoProject.Create(
            projectId,
            new ProjectDraft(
                "Analysis test",
                "Artist",
                "Lyrics",
                "Story",
                "Meaning",
                "Direction",
                "Mood",
                "Genre",
                ProjectAspectRatio.Landscape16x9,
                OutputResolution.FullHd,
                ["YouTube"],
                GenerationPreset.Balanced,
                null,
                null,
                [new ProjectReference(ProjectReferenceKind.Song, songAssetId)]),
            now);
        await projects.UpsertAsync(project);
        await media.UpsertAsync(new MediaAssetMetadata(
            songAssetId,
            projectId,
            $"{projectId:D}/source/song.wav",
            new string('a', 64),
            "audio/wav",
            null,
            null,
            TimeSpan.FromSeconds(120),
            1024,
            MediaCreationSource.Uploaded,
            now));

        var service = new SongAnalysisService(
            projects,
            media,
            analyses,
            new FakeProbe(),
            new FakeSignalAnalyzer(),
            TimeProvider.System);

        var first = await service.AnalyzeAsync(projectId);
        Assert.Equal(1, first.Version);
        Assert.Equal(120, first.DurationSeconds);
        Assert.Equal(128, first.Bpm);
        Assert.NotEmpty(first.Waveform);
        Assert.NotEmpty(first.Energy);
        Assert.NotEmpty(first.Beats);
        Assert.NotEmpty(first.Sections);

        var editedSections = new[]
        {
            new SongSection(Guid.NewGuid(), "Intro", SongSectionKind.Intro, 0, 12, 0.2, AnalysisValueSource.Detected),
            new SongSection(Guid.NewGuid(), "Verse 1", SongSectionKind.Verse, 12, 44, 0.2, AnalysisValueSource.Detected),
            new SongSection(Guid.NewGuid(), "Chorus", SongSectionKind.Chorus, 44, 76, 0.2, AnalysisValueSource.Detected),
            new SongSection(Guid.NewGuid(), "Outro", SongSectionKind.Outro, 76, 120, 0.2, AnalysisValueSource.Detected),
        };
        var second = await service.SaveSectionsAsync(projectId, editedSections);

        Assert.Equal(2, second.Version);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Waveform, second.Waveform);
        Assert.All(second.Sections, section => Assert.Equal(AnalysisValueSource.UserEdited, section.Source));

        var recreated = new DuckDbSongAnalysisRepository(new DuckDbConnectionFactory(storage.Options));
        var versions = await recreated.ListVersionsAsync(projectId);
        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[0].Version);
        Assert.Equal(1, versions[1].Version);
        Assert.Equal("Verse 1", versions[0].Sections[1].Label);
    }

    [Fact]
    public async Task SavingOverlappingSections_IsRejectedWithoutReplacingLatestVersion()
    {
        using var storage = new TemporaryStorage();
        var factory = new DuckDbConnectionFactory(storage.Options);
        var database = new DuckDbDatabase(factory);
        await database.InitializeAsync();
        var repository = new DuckDbSongAnalysisRepository(factory);
        var projectId = Guid.NewGuid();
        var analysis = new SongAnalysis(
            Guid.NewGuid(),
            projectId,
            Guid.NewGuid(),
            1,
            60,
            120,
            44100,
            2,
            "pcm_s16le",
            1411200,
            [new WaveformBucket(0, 60, -0.5, 0.5, 0.2)],
            [new EnergyPoint(1, 0.5)],
            [],
            [new SongSection(Guid.NewGuid(), "Full", SongSectionKind.Unknown, 0, 60, 0.3, AnalysisValueSource.Detected)],
            DateTimeOffset.UtcNow);
        await repository.UpsertAsync(analysis);

        Assert.Throws<ArgumentException>(() => SongAnalysis.ValidateSections(
            60,
            [
                new SongSection(Guid.NewGuid(), "A", SongSectionKind.Verse, 0, 40, 1, AnalysisValueSource.UserEdited),
                new SongSection(Guid.NewGuid(), "B", SongSectionKind.Chorus, 30, 60, 1, AnalysisValueSource.UserEdited),
            ]));

        Assert.Equal(1, (await repository.GetLatestAsync(projectId))!.Version);
    }

    private sealed class FakeProbe : IMediaProbe
    {
        public Task<MediaProbeResult> ProbeAsync(MediaLocation location, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaProbeResult(120, 44100, 2, "pcm_s16le", 1411200));
    }

    private sealed class FakeSignalAnalyzer : IAudioSignalAnalyzer
    {
        public Task<AudioSignalAnalysis> AnalyzeAsync(
            MediaLocation location,
            double durationSeconds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AudioSignalAnalysis(
                [
                    new WaveformBucket(0, 30, -0.5, 0.5, 0.25),
                    new WaveformBucket(30, 60, -0.8, 0.8, 0.45),
                    new WaveformBucket(60, 90, -0.3, 0.3, 0.18),
                    new WaveformBucket(90, 120, -0.7, 0.7, 0.4),
                ],
                [
                    new EnergyPoint(10, 0.2),
                    new EnergyPoint(30, 0.75),
                    new EnergyPoint(50, 0.3),
                    new EnergyPoint(70, 0.85),
                    new EnergyPoint(90, 0.25),
                    new EnergyPoint(110, 0.65),
                ],
                [
                    new BeatMarker(0.5, 0.8),
                    new BeatMarker(0.96875, 0.8),
                    new BeatMarker(1.4375, 0.8),
                    new BeatMarker(1.90625, 0.8),
                    new BeatMarker(2.375, 0.8),
                ],
                128));
    }

    private sealed class TemporaryStorage : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "OpenMusicVideoCreator.AnalysisTests",
            Guid.NewGuid().ToString("N"));

        public TemporaryStorage()
        {
            Directory.CreateDirectory(_root);
            Options = new StorageOptions(
                Path.Combine(_root, "data", "app.duckdb"),
                Path.Combine(_root, "projects"));
        }

        public StorageOptions Options { get; }

        public void Dispose()
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
