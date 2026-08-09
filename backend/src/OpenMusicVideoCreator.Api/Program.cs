using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using OpenMusicVideoCreator.Api.Endpoints;
using OpenMusicVideoCreator.Api.Jobs;
using OpenMusicVideoCreator.Api.Middleware;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Application.Jobs;
using OpenMusicVideoCreator.Application.Projects;
using OpenMusicVideoCreator.Application.Providers;
using OpenMusicVideoCreator.Application.SystemInfo;
using OpenMusicVideoCreator.Infrastructure.Jobs;
using OpenMusicVideoCreator.Infrastructure.Media;
using OpenMusicVideoCreator.Infrastructure.Persistence;
using OpenMusicVideoCreator.Infrastructure.Providers;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = ProjectMediaService.MaxSongBytes;
});

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffK";
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = ProjectMediaService.MaxSongBytes;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
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
builder.Services.AddSingleton<LocalMediaPathResolver>();
builder.Services.AddSingleton<IMediaStorage, LocalMediaStorage>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<ProjectMediaService>();

builder.Services.AddSingleton<ISongAnalysisRepository, DuckDbSongAnalysisRepository>();
builder.Services.AddSingleton<ILyricTimingRepository, DuckDbLyricTimingRepository>();
builder.Services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
builder.Services.AddSingleton<IAudioSignalAnalyzer, FfmpegAudioSignalAnalyzer>();
builder.Services.AddSingleton<SongAnalysisService>();
builder.Services.AddSingleton<LyricTimingService>();

builder.Services.AddSingleton<IProviderCatalog, MockProviderCatalog>();
builder.Services.AddSingleton<ICredentialResolver, CredentialResolver>();
builder.Services.AddSingleton<ProviderSettingsService>();
builder.Services.AddSingleton<MockProviderControl>();
builder.Services.AddSingleton<MockDirectorProvider>();
builder.Services.AddSingleton<IDirectorProvider>(services => services.GetRequiredService<MockDirectorProvider>());
builder.Services.AddSingleton<MockImageProvider>();
builder.Services.AddSingleton<IImageGenerationProvider>(services => services.GetRequiredService<MockImageProvider>());
builder.Services.AddSingleton<IImageEditingProvider>(services => services.GetRequiredService<MockImageProvider>());
builder.Services.AddSingleton<MockVideoProvider>();
builder.Services.AddSingleton<IVideoGenerationProvider>(services => services.GetRequiredService<MockVideoProvider>());
builder.Services.AddSingleton<IImageToVideoProvider>(services => services.GetRequiredService<MockVideoProvider>());
builder.Services.AddSingleton<IVideoToVideoProvider>(services => services.GetRequiredService<MockVideoProvider>());

builder.Services.AddSingleton<IJobRepository, DuckDbJobRepository>();
builder.Services.AddSingleton<JobChangeHub>();
builder.Services.AddSingleton<IJobChangePublisher>(services => services.GetRequiredService<JobChangeHub>());
builder.Services.AddSingleton<IJobChangeStream>(services => services.GetRequiredService<JobChangeHub>());
builder.Services.AddSingleton<JobService>();
builder.Services.AddSingleton<IJobQueue>(services => services.GetRequiredService<JobService>());
builder.Services.AddSingleton<IJobExecutionDispatcher, MockJobExecutionDispatcher>();
builder.Services.AddSingleton<JobProcessor>();
if (builder.Configuration.GetValue("Jobs:WorkerEnabled", true))
{
    builder.Services.AddHostedService<PersistentJobWorker>();
}

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
app.MapSongAnalysisEndpoints();
app.MapProviderEndpoints();
app.MapJobEndpoints();

app.Run();

public partial class Program
{
}
