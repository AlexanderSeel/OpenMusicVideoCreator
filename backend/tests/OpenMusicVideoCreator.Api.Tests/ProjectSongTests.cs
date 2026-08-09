using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using OpenMusicVideoCreator.Api.Contracts.Projects;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Domain.Projects;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class ProjectSongTests : IClassFixture<TestApplicationFactory>
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TestApplicationFactory _factory;

    public ProjectSongTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SongUpload_PersistsMetadataAndReplacesReferenceNonDestructively()
    {
        using var client = _factory.CreateClient();
        var created = await CreateProjectAsync(client);

        var first = await UploadAsync(client, created.Id, "first.mp3", "audio/mpeg", [1, 2, 3, 4, 5]);
        Assert.Equal("audio/mpeg", first.MimeType);
        Assert.Equal(5, first.FileSize);

        var fetched = await client.GetFromJsonAsync<ProjectSongResponse>(
            $"/api/projects/{created.Id}/song",
            ApiJsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal(first.AssetId, fetched.AssetId);

        var afterFirstUpload = await client.GetFromJsonAsync<ProjectResponse>(
            $"/api/projects/{created.Id}",
            ApiJsonOptions);
        Assert.NotNull(afterFirstUpload);
        var firstSongReference = Assert.Single(
            afterFirstUpload.References.Where(reference => reference.Kind == ProjectReferenceKind.Song));
        Assert.Equal(first.AssetId, firstSongReference.ReferenceId);

        var second = await UploadAsync(client, created.Id, "second.wav", "audio/wav", [6, 7, 8]);
        Assert.NotEqual(first.AssetId, second.AssetId);

        var afterSecondUpload = await client.GetFromJsonAsync<ProjectResponse>(
            $"/api/projects/{created.Id}",
            ApiJsonOptions);
        Assert.NotNull(afterSecondUpload);
        var secondSongReference = Assert.Single(
            afterSecondUpload.References.Where(reference => reference.Kind == ProjectReferenceKind.Song));
        Assert.Equal(second.AssetId, secondSongReference.ReferenceId);

        var mediaAssets = _factory.Services.GetRequiredService<IMediaAssetRepository>();
        Assert.NotNull(await mediaAssets.GetAsync(first.AssetId));
        Assert.NotNull(await mediaAssets.GetAsync(second.AssetId));
    }

    [Fact]
    public async Task SongUpload_RejectsUnsupportedFileTypes()
    {
        using var client = _factory.CreateClient();
        var created = await CreateProjectAsync(client);
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", "song.exe");

        using var response = await client.PostAsync($"/api/projects/{created.Id}/song", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<ProjectSongResponse> UploadAsync(
        HttpClient client,
        Guid projectId,
        string fileName,
        string contentType,
        byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(content, "file", fileName);
        using var response = await client.PostAsync($"/api/projects/{projectId}/song", form);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProjectSongResponse>(ApiJsonOptions);
        return result ?? throw new InvalidOperationException("Song upload response was empty.");
    }

    private static async Task<ProjectResponse> CreateProjectAsync(HttpClient client)
    {
        var request = new ProjectUpsertRequest(
            "Song project",
            "Artist",
            "Lyrics",
            "Storyline",
            "Meaning",
            "Visual direction",
            "Mystic",
            "Drum & Bass",
            ProjectAspectRatio.Landscape16x9,
            1920,
            1080,
            ["YouTube"],
            GenerationPreset.Balanced,
            null,
            25m,
            []);

        using var response = await client.PostAsJsonAsync("/api/projects", request, ApiJsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectResponse>(ApiJsonOptions)
            ?? throw new InvalidOperationException("Project response was empty.");
    }
}
