using System.Text;
using DuckDB.NET.Data;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Projects;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Projects;
using OpenMusicVideoCreator.Infrastructure.Media;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class PersistenceIntegrationTests
{
    [Fact]
    public async Task Project_RoundTripSurvivesRepositoryRecreation()
    {
        using var storage = new TemporaryStorage();
        var first = await CreatePersistenceAsync(storage.Options);
        var project = CreateProject();
        await first.Projects.UpsertAsync(project);

        var secondFactory = new DuckDbConnectionFactory(storage.Options);
        var secondDatabase = new DuckDbDatabase(secondFactory);
        await secondDatabase.InitializeAsync();
        var secondRepository = new DuckDbProjectRepository(secondFactory);
        var restored = await secondRepository.GetAsync(project.Id);

        Assert.Equal(project, restored);
    }

    [Fact]
    public async Task Settings_PersistForApplicationAndProjectScopes()
    {
        using var storage = new TemporaryStorage();
        var persistence = await CreatePersistenceAsync(storage.Options);
        var projectId = Guid.NewGuid();

        await persistence.Settings.SetAsync("ui.theme", "\"dark\"");
        await ((IProjectSettingsRepository)persistence.Settings).SetAsync(projectId, "continuity", "{\"enabled\":true}");

        var recreated = new DuckDbSettingsRepository(new DuckDbConnectionFactory(storage.Options));
        Assert.Equal("\"dark\"", await recreated.GetAsync("ui.theme"));
        Assert.Equal(
            "{\"enabled\":true}",
            await ((IProjectSettingsRepository)recreated).GetAsync(projectId, "continuity"));
    }

    [Fact]
    public async Task MediaStorage_WritesBytesToFilesystemAndOnlyMetadataToDuckDb()
    {
        using var storage = new TemporaryStorage();
        var persistence = await CreatePersistenceAsync(storage.Options);
        var mediaStorage = new LocalMediaStorage(storage.Options);
        var project = CreateProject();
        await persistence.Projects.UpsertAsync(project);

        var payload = Encoding.UTF8.GetBytes("audio-placeholder");
        await using var source = new MemoryStream(payload);
        var stored = await mediaStorage.SaveAsync(project.Id, MediaStorageArea.Source, source, "song.mp3");

        var asset = new MediaAssetMetadata(
            Guid.NewGuid(),
            project.Id,
            stored.Location.Value,
            stored.ChecksumSha256,
            "audio/mpeg",
            null,
            null,
            TimeSpan.FromSeconds(3),
            stored.FileSize,
            MediaCreationSource.Uploaded,
            DateTimeOffset.UtcNow);
        await persistence.Media.UpsertAsync(asset);

        var restored = await persistence.Media.GetAsync(asset.Id);
        Assert.Equal(asset, restored);

        await using var opened = await mediaStorage.OpenReadAsync(stored.Location);
        using var buffer = new MemoryStream();
        await opened.CopyToAsync(buffer);
        Assert.Equal(payload, buffer.ToArray());

        await using var connection = new DuckDBConnection($"Data Source={storage.Options.DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM information_schema.columns
            WHERE table_name = 'media_assets' AND upper(data_type) = 'BLOB';
            """;
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task PortableProject_ExportImportRoundTripPreservesMetadata()
    {
        using var storage = new TemporaryStorage();
        var persistence = await CreatePersistenceAsync(storage.Options);
        var service = new ProjectService(persistence.Projects, TimeProvider.System);
        var original = CreateProject();
        await persistence.Projects.UpsertAsync(original);

        var json = await service.ExportAsync(original.Id);
        Assert.True(await service.DeleteAsync(original.Id));
        var imported = await service.ImportAsync(json);

        Assert.Equal(original, imported);
        Assert.Equal(original, await persistence.Projects.GetAsync(original.Id));
    }

    [Fact]
    public async Task UpdatingProjectReferences_DoesNotDeleteGeneratedAssetMetadata()
    {
        using var storage = new TemporaryStorage();
        var persistence = await CreatePersistenceAsync(storage.Options);
        var original = CreateProject();
        await persistence.Projects.UpsertAsync(original);

        var asset = new MediaAssetMetadata(
            Guid.NewGuid(),
            original.Id,
            $"{original.Id:D}/generated/scene-001.mp4",
            new string('a', 64),
            "video/mp4",
            1920,
            1080,
            TimeSpan.FromSeconds(6),
            4096,
            MediaCreationSource.Generated,
            DateTimeOffset.UtcNow);
        await persistence.Media.UpsertAsync(asset);

        var replacementDraft = CreateDraft() with
        {
            References = [new ProjectReference(ProjectReferenceKind.Style, Guid.NewGuid())],
        };
        await persistence.Projects.UpsertAsync(original.Update(replacementDraft, DateTimeOffset.UtcNow));

        Assert.Equal(asset, await persistence.Media.GetAsync(asset.Id));
    }

    [Fact]
    public async Task MediaStorage_RejectsPathTraversalFileNames()
    {
        using var storage = new TemporaryStorage();
        var mediaStorage = new LocalMediaStorage(storage.Options);
        await using var source = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediaStorage.SaveAsync(Guid.NewGuid(), MediaStorageArea.Source, source, "../escape.mp3"));
    }

    private static async Task<PersistenceSet> CreatePersistenceAsync(StorageOptions options)
    {
        var factory = new DuckDbConnectionFactory(options);
        var database = new DuckDbDatabase(factory);
        await database.InitializeAsync();
        return new PersistenceSet(
            new DuckDbProjectRepository(factory),
            new DuckDbSettingsRepository(factory),
            new DuckDbMediaAssetRepository(factory));
    }

    private static MusicVideoProject CreateProject()
    {
        var now = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero);
        return MusicVideoProject.Create(Guid.NewGuid(), CreateDraft(), now);
    }

    private static ProjectDraft CreateDraft() => new(
        "Persistence Test",
        "Test Artist",
        "supplied lyrics",
        "a visual story",
        "song meaning",
        "cinematic and restrained",
        "mysterious",
        "drum & bass",
        ProjectAspectRatio.Landscape16x9,
        new OutputResolution(1920, 1080),
        ["YouTube", "Reels"],
        GenerationPreset.Balanced,
        12.5m,
        25m,
        [
            new ProjectReference(ProjectReferenceKind.Character, Guid.Parse("11111111-1111-4111-8111-111111111111")),
            new ProjectReference(ProjectReferenceKind.Style, Guid.Parse("22222222-2222-4222-8222-222222222222")),
        ]);

    private sealed record PersistenceSet(
        DuckDbProjectRepository Projects,
        DuckDbSettingsRepository Settings,
        DuckDbMediaAssetRepository Media);

    private sealed class TemporaryStorage : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "OpenMusicVideoCreator.PersistenceTests",
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
                // Best-effort test cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup.
            }
        }
    }
}
