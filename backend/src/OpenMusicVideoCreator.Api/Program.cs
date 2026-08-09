using System.Reflection;
using OpenMusicVideoCreator.Api.Middleware;
using OpenMusicVideoCreator.Application.SystemInfo;

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

var app = builder.Build();

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

app.Run();

public partial class Program;
