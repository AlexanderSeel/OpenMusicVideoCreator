using DuckDB.NET.Data;

namespace OpenMusicVideoCreator.Infrastructure.Persistence;

public sealed class DuckDbConnectionFactory
{
    private readonly string _databasePath;

    public DuckDbConnectionFactory(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _databasePath = Path.GetFullPath(options.DatabasePath);
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public string DatabasePath => _databasePath;

    public DuckDBConnection Create() => new($"Data Source={_databasePath}");
}
