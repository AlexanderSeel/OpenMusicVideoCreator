namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed record StorageOptions(string DatabasePath, string ProjectsRoot)
{
    public static readonly StorageOptions Default = new("data/app.duckdb", "projects");
}
