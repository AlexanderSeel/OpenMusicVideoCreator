using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenMusicVideoCreator.Api.Contracts.Projects;
using OpenMusicVideoCreator.Domain.Projects;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class ProjectEndpointTests : IClassFixture<TestApplicationFactory>
{
    private static readonly JsonSerializerOptions ApiJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TestApplicationFactory _factory;

    public ProjectEndpointTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProjectApi_SupportsCrudAndPortableRoundTrip()
    {
        using var client = _factory.CreateClient();
        var request = CreateRequest("First title");

        using var createResponse = await client.PostAsJsonAsync("/api/projects", request, ApiJsonOptions);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<ProjectResponse>(ApiJsonOptions);
        Assert.NotNull(created);
        Assert.Equal("First title", created.Title);

        using var getResponse = await client.GetAsync($"/api/projects/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        var fetched = await getResponse.Content.ReadFromJsonAsync<ProjectResponse>(ApiJsonOptions);
        Assert.NotNull(fetched);
        AssertProjectResponseEqual(created, fetched);

        using var updateResponse = await client.PutAsJsonAsync(
            $"/api/projects/{created.Id}",
            CreateRequest("Updated title"),
            ApiJsonOptions);
        updateResponse.EnsureSuccessStatusCode();
        var updated = await updateResponse.Content.ReadFromJsonAsync<ProjectResponse>(ApiJsonOptions);
        Assert.NotNull(updated);
        Assert.Equal("Updated title", updated.Title);
        Assert.Equal(created.Id, updated.Id);

        var listed = await client.GetFromJsonAsync<ProjectResponse[]>("/api/projects", ApiJsonOptions);
        Assert.NotNull(listed);
        Assert.Contains(listed, project => project.Id == created.Id);

        using var exportResponse = await client.GetAsync($"/api/projects/{created.Id}/export");
        exportResponse.EnsureSuccessStatusCode();
        var portableJson = await exportResponse.Content.ReadAsStringAsync();
        Assert.Contains("Updated title", portableJson, StringComparison.Ordinal);

        using var deleteResponse = await client.DeleteAsync($"/api/projects/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        using var missingResponse = await client.GetAsync($"/api/projects/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        using var importResponse = await client.PostAsync(
            "/api/projects/import",
            new StringContent(portableJson, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, importResponse.StatusCode);
        var imported = await importResponse.Content.ReadFromJsonAsync<ProjectResponse>(ApiJsonOptions);
        Assert.NotNull(imported);
        Assert.Equal(created.Id, imported.Id);
        Assert.Equal("Updated title", imported.Title);
    }

    private static void AssertProjectResponseEqual(ProjectResponse expected, ProjectResponse actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Artist, actual.Artist);
        Assert.Equal(expected.Lyrics, actual.Lyrics);
        Assert.Equal(expected.Storyline, actual.Storyline);
        Assert.Equal(expected.Meaning, actual.Meaning);
        Assert.Equal(expected.VisualDirection, actual.VisualDirection);
        Assert.Equal(expected.Mood, actual.Mood);
        Assert.Equal(expected.Genre, actual.Genre);
        Assert.Equal(expected.AspectRatio, actual.AspectRatio);
        Assert.Equal(expected.ResolutionWidth, actual.ResolutionWidth);
        Assert.Equal(expected.ResolutionHeight, actual.ResolutionHeight);
        Assert.Equal(expected.TargetPlatforms, actual.TargetPlatforms);
        Assert.Equal(expected.Preset, actual.Preset);
        Assert.Equal(expected.EstimatedBudget, actual.EstimatedBudget);
        Assert.Equal(expected.MaximumBudget, actual.MaximumBudget);
        Assert.Equal(expected.References, actual.References);
        Assert.Equal(expected.CreatedUtc, actual.CreatedUtc);
        Assert.Equal(expected.UpdatedUtc, actual.UpdatedUtc);
    }

    private static ProjectUpsertRequest CreateRequest(string title) => new(
        title,
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
        10m,
        25m,
        [new ProjectReferenceRequest(ProjectReferenceKind.Character, Guid.NewGuid())]);
}
