using System.Reflection;
using OpenMusicVideoCreator.Api.Endpoints;
using OpenMusicVideoCreator.Api.Middleware;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Projects;
using OpenMusicVideoCreator.Application.SystemInfo;
using OpenMusicVideoCreator.Infrastructure.Media;
using OpenMusicVideoCreator.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffK";
});

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Frontend:Origins")
            .GetChildren()
            .Select(section => section.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        if (origins.Length > 0)
        {
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var storageOptions = new StorageOptions(
    builder.Configuration["Storage:DatabasePath"] ?? StorageOptions.Default.DatabasePath,
    builder.Configuration["Storage:ProjectsRoot"] ?? StorageOptions.Default.ProjectsRoot);

builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<DuckDbConnectionFactory>();
builder.Services.AddSingleton<DuckDbDatabase>();
builder.Services.AddSingleton<IApplicationPersistence>(services => services.GetRequiredService<DuckDbDatabase>());
builder.Services.AddSingleton<IProjectRepository, DuckDbProjectRepository>();
builder.Services.AddSingleton<DuckDbSettingsRepository>();
builder.Services.AddSingleton<IApplicationSettingsRepository>(services => services.GetRequiredService<DuckDbSettingsRepository>());
builder.Services.AddSingleton<IProjectSettingsRepository>(services => services.GetRequiredService<DuckDbSettingsRepository>());
builder.Services.AddSingleton<IMediaAssetRepository, DuckDbMediaAssetRepository>();
builder.Services.AddSingleton<IMediaStorage, LocalMediaStorage>();
builder.Services.AddSingleton<ProjectService>();

var app = builder.Build();

await app.Services.GetRequiredService<IApplicationPersistence>().InitializeAsync();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors("Frontend");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.MapGet("/api/system/version", (IHostEnvironment environment) =>
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

        return TypedResults.Ok(new SystemVersionResponse(
            "OpenMusicVideoCreator.Api",
            version,
            environment.EnvironmentName));
    })
    .WithName("GetSystemVersion")
    .Produces<SystemVersionResponse>(StatusCodes.Status200OK);

app.MapProjectEndpoints();

app.Run();

public partial class Program
{
}
