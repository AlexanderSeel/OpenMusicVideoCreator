using System.Text;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Library;
using OpenMusicVideoCreator.Domain.Library;
using OpenMusicVideoCreator.Domain.Media;
using OpenMusicVideoCreator.Domain.Projects;
using OpenMusicVideoCreator.Infrastructure.Media;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class LibraryIntegrationTests
{
    [Fact]
    public async Task VisualItem_CanBeReusedAcrossProjectsAndCannotBeDeletedWhileReferenced()
    {
        using var storage = new TemporaryStorage();
        var set = await CreatePersistenceAsync(storage.Options);
        var service = new VisualLibraryService(set.Visual, set.Assets, set.Projects, set.States, new FixedTimeProvider());
        var character = await service.CreateAsync(CharacterDraft("Shared Hero"));
        var first = CreateProject(character.Id);
        var second = CreateProject(character.Id);
        await set.Projects.UpsertAsync(first);
        await set.Projects.UpsertAsync(second);

        var blocked = await service.DeleteAsync(character.Id);

        Assert.False(blocked.Deleted);
        Assert.Equal(2, blocked.ReferencingProjectIds.Count);
        Assert.NotNull(await set.Visual.GetAsync(character.Id));

        await set.Projects.UpsertAsync(RemoveReferences(first));
        await set.Projects.UpsertAsync(RemoveReferences(second));
        var deleted = await service.DeleteAsync(character.Id);

        Assert.True(deleted.Deleted);
        Assert.Null(await set.Visual.GetAsync(character.Id));
    }

    [Fact]
    public async Task ProjectCharacterState_SurvivesRepositoryRecreation()
    {
        using var storage = new TemporaryStorage();
        var set = await CreatePersistenceAsync(storage.Options);
        var libraryService = new VisualLibraryService(set.Visual, set.Assets, set.Projects, set.States, new FixedTimeProvider());
        var character = await libraryService.CreateAsync(CharacterDraft("Continuity Hero"));
        var project = CreateProject(character.Id);
        await set.Projects.UpsertAsync(project);
        var stateService = new ProjectCharacterStateService(set.Projects, set.Visual, set.States, new FixedTimeProvider());
        var locks = new CharacterContinuityLocks(true, true, true, true, true, false);

        await stateService.SaveAsync(
            project.Id,
            character.Id,
            character.Character!.Outfits[0].Id,
            locks,
            new Dictionary<string, double> { ["presence"] = 1, ["confidence"] = 0.65, ["isolation"] = 0.25 });

        var recreated = new DuckDbProjectCharacterStateRepository(new DuckDbConnectionFactory(storage.Options));
        var restored = await recreated.GetAsync(project.Id, character.Id);

        Assert.NotNull(restored);
        Assert.Equal(character.Id, restored.CharacterId);
        Assert.Equal(character.Character.Outfits[0].Id, restored.OutfitId);
        Assert.Equal(0.65, restored.StateValues["confidence"]);
        Assert.False(restored.Locks.Wardrobe);
    }

    [Fact]
    public async Task AssetEntry_CannotBeDeletedWhileReferencedByVisualItem()
    {
        using var storage = new TemporaryStorage();
        var set = await CreatePersistenceAsync(storage.Options);
        var now = FixedUtc();
        var asset = new AssetLibraryEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            "Portrait",
            ["face"],
            false,
            "Uploaded reference",
            now,
            now);
        await set.Assets.UpsertAsync(asset);
        var visualService = new VisualLibraryService(set.Visual, set.Assets, set.Projects, set.States, new FixedTimeProvider());
        await visualService.CreateAsync(CharacterDraft("Asset Hero") with { AssetEntryIds = [asset.Id] });
        var assetService = new AssetLibraryService(
            set.Assets,
            set.Visual,
            new NoopMediaAssetRepository(),
            new NoopLibraryMediaStorage(),
            new NoopPreviewGenerator(),
            new FixedTimeProvider());

        var result = await assetService.DeleteAsync(asset.Id);

        Assert.False(result.Deleted);
        Assert.Single(result.ReferencingLibraryItemIds);
        Assert.NotNull(await set.Assets.GetAsync(asset.Id));
    }

    [Fact]
    public async Task LocalLibraryStorage_IsGlobalAndRejectsTraversal()
    {
        using var storage = new TemporaryStorage();
        var paths = new LocalMediaPathResolver(storage.Options);
        var mediaStorage = new LocalLibraryMediaStorage(paths);
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("reference"));

        var stored = await mediaStorage.SaveOriginalAsync(source, "portrait.png");

        Assert.StartsWith("library/originals/", stored.Location.Value, StringComparison.Ordinal);
        Assert.True(File.Exists(paths.Resolve(stored.Location)));
        await using var invalid = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediaStorage.SaveOriginalAsync(invalid, "../escape.png"));
    }

    [Fact]
    public async Task VisualLibrary_RoundTripPreservesTypedPayloadsAndTags()
    {
        using var storage = new TemporaryStorage();
        var set = await CreatePersistenceAsync(storage.Options);
        var original = VisualLibraryItem.Create(Guid.NewGuid(), CharacterDraft("Round Trip"), FixedUtc());
        await set.Visual.UpsertAsync(original);

        var recreated = new DuckDbVisualLibraryRepository(new DuckDbConnectionFactory(storage.Options));
        var restored = await recreated.GetAsync(original.Id);

        Assert.NotNull(restored);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Tags, restored.Tags);
        Assert.Equal(original.Character, restored.Character);
        Assert.Equal(original.AssetEntryIds, restored.AssetEntryIds);
    }

    private static async Task<PersistenceSet> CreatePersistenceAsync(StorageOptions options)
    {
        var factory = new DuckDbConnectionFactory(options);
        var database = new DuckDbDatabase(factory);
        await database.InitializeAsync();
        return new PersistenceSet(
            new DuckDbProjectRepository(factory),
            new DuckDbVisualLibraryRepository(factory),
            new DuckDbAssetLibraryRepository(factory),
            new DuckDbProjectCharacterStateRepository(factory));
    }

    private static VisualLibraryDraft CharacterDraft(string name)
    {
        var outfit = new CharacterOutfit(
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "Default",
            "Primary outfit",
            []);
        return new VisualLibraryDraft(
            VisualLibraryKind.Character,
            name,
            "Reusable character",
            ["hero", "continuity"],
            true,
            [],
            new CharacterLibraryData(
                CharacterReferenceType.Photo,
                "Recognizable face and dark jacket",
                ["Do not change eye color"],
                [outfit],
                new CharacterContinuityLocks(true, true, true, true, true, true)),
            null,
            null);
    }

    private static MusicVideoProject CreateProject(Guid characterId)
    {
        var draft = new ProjectDraft(
            "Library project",
            "Artist",
            "Lyrics",
            "Storyline",
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
            [new ProjectReference(ProjectReferenceKind.Character, characterId)]);
        return MusicVideoProject.Create(Guid.NewGuid(), draft, FixedUtc());
    }

    private static MusicVideoProject RemoveReferences(MusicVideoProject project)
    {
        var draft = new ProjectDraft(
            project.Title,
            project.Artist,
            project.Lyrics,
            project.Storyline,
            project.Meaning,
            project.VisualDirection,
            project.Mood,
            project.Genre,
            project.AspectRatio,
            project.Resolution,
            project.TargetPlatforms,
            project.Preset,
            project.EstimatedBudget,
            project.MaximumBudget,
            []);
        return project.Update(draft, FixedUtc().AddMinutes(1));
    }

    private static DateTimeOffset FixedUtc() => new(2026, 8, 9, 18, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedUtc();
    }

    private sealed record PersistenceSet(
        DuckDbProjectRepository Projects,
        DuckDbVisualLibraryRepository Visual,
        DuckDbAssetLibraryRepository Assets,
        DuckDbProjectCharacterStateRepository States);

    private sealed class TemporaryStorage : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "OpenMusicVideoCreator.LibraryTests", Guid.NewGuid().ToString("N"));

        public TemporaryStorage()
        {
            Directory.CreateDirectory(_root);
            Options = new StorageOptions(Path.Combine(_root, "data", "app.duckdb"), Path.Combine(_root, "projects"));
        }

        public StorageOptions Options { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class NoopMediaAssetRepository : IMediaAssetRepository
    {
        public Task<MediaAssetMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<MediaAssetMetadata?>(null);
        public Task<IReadOnlyList<MediaAssetMetadata>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<MediaAssetMetadata>>([]);
        public Task UpsertAsync(MediaAssetMetadata asset, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class NoopLibraryMediaStorage : ILibraryMediaStorage
    {
        public Task<StoredMedia> SaveOriginalAsync(Stream source, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredMedia> SavePreviewAsync(Stream source, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoopPreviewGenerator : IMediaPreviewGenerator
    {
        public Task<GeneratedMediaPreview?> GenerateAsync(MediaLocation source, string mimeType, CancellationToken cancellationToken = default) => Task.FromResult<GeneratedMediaPreview?>(null);
    }
}
