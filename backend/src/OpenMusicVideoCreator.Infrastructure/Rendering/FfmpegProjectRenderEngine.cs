using System.Diagnostics;
using System.Globalization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Rendering;
using OpenMusicVideoCreator.Domain.Rendering;
using OpenMusicVideoCreator.Infrastructure.Media;

namespace OpenMusicVideoCreator.Infrastructure.Rendering;

public sealed class FfmpegProjectRenderEngine : IProjectRenderEngine
{
    private readonly IMediaAssetRepository _mediaAssets;
    private readonly LocalMediaPathResolver _paths;

    public FfmpegProjectRenderEngine(IMediaAssetRepository mediaAssets, LocalMediaPathResolver paths)
    {
        _mediaAssets = mediaAssets;
        _paths = paths;
    }

    public async Task<RenderEngineResult> RenderAsync(
        ProjectRenderManifest manifest,
        CancellationToken cancellationToken = default)
    {
        manifest.Validate();
        var ordered = manifest.Clips.OrderBy(clip => clip.Sequence).ToArray();
        var clipPaths = new List<string>(ordered.Length);
        foreach (var clip in ordered)
        {
            var media = await _mediaAssets.GetAsync(clip.MediaAssetId, cancellationToken)
                ?? throw new InvalidDataException($"Render clip media '{clip.MediaAssetId}' was not found.");
            clipPaths.Add(ResolveExisting(media.Location));
        }

        var song = await _mediaAssets.GetAsync(manifest.SongMediaAssetId, cancellationToken)
            ?? throw new InvalidDataException("Original Song media asset was not found.");
        var songPath = ResolveExisting(song.Location);
        var outputDirectory = Path.Combine(_paths.GetProjectRoot(manifest.ProjectId), "renders", ".work");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = _paths.EnsureInsideRoot(Path.Combine(outputDirectory, $"{Guid.NewGuid():N}.mp4"));

        var arguments = BuildArguments(manifest, clipPaths, songPath, outputPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("ffmpeg process could not be started.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("ffmpeg is required for project rendering but could not be started.", exception);
        }

        using var cancellationRegistration = cancellationToken.Register(() => Kill(process));
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        _ = await outputTask;
        if (process.ExitCode != 0)
        {
            TryDelete(outputPath);
            throw new InvalidDataException($"ffmpeg render failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        var stream = new FileStream(
            outputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        var fileName = manifest.Kind == ProjectRenderKind.Preview ? "preview.mp4" : "final.mp4";
        return new RenderEngineResult(
            stream,
            fileName,
            "video/mp4",
            manifest.Width,
            manifest.Height,
            TimeSpan.FromSeconds(manifest.DurationSeconds),
            FormatCommandLog(arguments));
    }

    internal static IReadOnlyList<string> BuildArguments(
        ProjectRenderManifest manifest,
        IReadOnlyList<string> clipPaths,
        string songPath,
        string outputPath)
    {
        if (clipPaths.Count != manifest.Clips.Count) throw new ArgumentException("One clip path is required per manifest clip.", nameof(clipPaths));
        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "error" };
        foreach (var clipPath in clipPaths)
        {
            args.Add("-i");
            args.Add(clipPath);
        }
        args.Add("-i");
        args.Add(songPath);

        var filters = new List<string>(manifest.Clips.Count + 1);
        for (var index = 0; index < manifest.Clips.Count; index++)
        {
            var clip = manifest.Clips[index];
            var duration = clip.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture);
            var fade = IsFadeTransition(clip.TransitionIn)
                ? $",fade=t=in:st=0:d={Math.Min(0.35, clip.DurationSeconds / 4d).ToString("0.###", CultureInfo.InvariantCulture)}"
                : string.Empty;
            filters.Add($"[{index}:v:0]tpad=stop_mode=clone:stop_duration={duration},trim=duration={duration},setpts=PTS-STARTPTS,scale={manifest.Width}:{manifest.Height}:force_original_aspect_ratio=increase,crop={manifest.Width}:{manifest.Height},fps={manifest.FramesPerSecond}{fade},format=yuv420p[v{index}]");
        }
        filters.Add(string.Concat(Enumerable.Range(0, manifest.Clips.Count).Select(index => $"[v{index}]")) + $"concat=n={manifest.Clips.Count}:v=1:a=0[outv]");
        args.Add("-filter_complex");
        args.Add(string.Join(';', filters));
        args.Add("-map");
        args.Add("[outv]");
        args.Add("-map");
        args.Add($"{manifest.Clips.Count}:a:0");
        args.Add("-t");
        args.Add(manifest.DurationSeconds.ToString("0.######", CultureInfo.InvariantCulture));
        args.Add("-c:v");
        args.Add("libx264");
        args.Add("-preset");
        args.Add(manifest.Kind == ProjectRenderKind.Preview ? "veryfast" : "medium");
        args.Add("-crf");
        args.Add(manifest.Kind == ProjectRenderKind.Preview ? "30" : "18");
        args.Add("-c:a");
        args.Add("aac");
        args.Add("-b:a");
        args.Add(manifest.Kind == ProjectRenderKind.Preview ? "128k" : "192k");
        args.Add("-movflags");
        args.Add("+faststart");
        args.Add("-shortest");
        args.Add(outputPath);
        return args;
    }

    private static bool IsFadeTransition(string transition) =>
        transition.Contains("fade", StringComparison.OrdinalIgnoreCase) &&
        !transition.Contains("crossfade", StringComparison.OrdinalIgnoreCase);

    private string ResolveExisting(string location)
    {
        var path = _paths.Resolve(new MediaLocation(location));
        if (!File.Exists(path)) throw new FileNotFoundException("Render source media was not found.", path);
        return path;
    }

    private static string FormatCommandLog(IReadOnlyList<string> arguments) =>
        "ffmpeg " + string.Join(' ', arguments.Select(QuoteForLog));

    private static string QuoteForLog(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
