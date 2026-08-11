using System.Diagnostics;
using System.Globalization;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Rendering;
using OpenMusicVideoCreator.Domain.Rendering;
using OpenMusicVideoCreator.Domain.Timeline;
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

        var overlayPaths = new List<string>(manifest.ResolveOverlays().Count);
        foreach (var overlay in manifest.ResolveOverlays())
        {
            var media = await _mediaAssets.GetAsync(overlay.MediaAssetId, cancellationToken)
                ?? throw new InvalidDataException($"Render overlay media '{overlay.MediaAssetId}' was not found.");
            overlayPaths.Add(ResolveExisting(media.Location));
        }

        var song = await _mediaAssets.GetAsync(manifest.SongMediaAssetId, cancellationToken)
            ?? throw new InvalidDataException("Original Song media asset was not found.");
        var songPath = ResolveExisting(song.Location);
        var outputDirectory = Path.Combine(_paths.GetProjectRoot(manifest.ProjectId), "renders", ".work");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = _paths.EnsureInsideRoot(Path.Combine(outputDirectory, $"{Guid.NewGuid():N}.mp4"));

        var arguments = BuildArguments(manifest, clipPaths, songPath, outputPath, overlayPaths);
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
        string outputPath,
        IReadOnlyList<string>? overlayPaths = null)
    {
        if (clipPaths.Count != manifest.Clips.Count) throw new ArgumentException("One clip path is required per manifest clip.", nameof(clipPaths));
        overlayPaths ??= [];
        if (overlayPaths.Count != manifest.ResolveOverlays().Count) throw new ArgumentException("One overlay path is required per manifest overlay.", nameof(overlayPaths));

        var args = new List<string> { "-hide_banner", "-y", "-loglevel", "error" };
        foreach (var clipPath in clipPaths)
        {
            args.Add("-i");
            args.Add(clipPath);
        }
        foreach (var overlayPath in overlayPaths)
        {
            if (IsStillImage(overlayPath))
            {
                args.Add("-loop");
                args.Add("1");
            }
            args.Add("-i");
            args.Add(overlayPath);
        }
        args.Add("-i");
        args.Add(songPath);

        var filters = new List<string>(manifest.Clips.Count * 2 + manifest.ResolveOverlays().Count * 2 + manifest.ResolveEffects().Count + manifest.ResolveSubtitles().Count + 2);
        for (var index = 0; index < manifest.Clips.Count; index++)
        {
            var clip = manifest.Clips[index];
            var outgoingCrossfade = index + 1 < manifest.Clips.Count
                ? ResolveCrossfadeDuration(manifest.Clips[index + 1])
                : 0;
            var renderDuration = clip.DurationSeconds + outgoingCrossfade;
            var sourceDuration = F(clip.SourceDurationSeconds ?? clip.DurationSeconds);
            var transform = clip.ResolveTransform();
            var color = clip.ResolveColor();
            var chain = new List<string>
            {
                $"trim=start={F(clip.SourceInSeconds)}:duration={sourceDuration}",
                $"setpts=(PTS-STARTPTS)/{F(clip.PlaybackRate)}",
            };

            if (transform.CropLeft > 0 || transform.CropTop > 0 || transform.CropRight > 0 || transform.CropBottom > 0)
            {
                chain.Add($"crop=iw*{F(1 - transform.CropLeft - transform.CropRight)}:ih*{F(1 - transform.CropTop - transform.CropBottom)}:iw*{F(transform.CropLeft)}:ih*{F(transform.CropTop)}");
            }

            chain.Add($"eq=brightness={F(color.Brightness)}:contrast={F(color.Contrast)}:saturation={F(color.Saturation)}");
            chain.Add($"scale={manifest.Width}:{manifest.Height}:force_original_aspect_ratio=increase");
            chain.Add($"crop={manifest.Width}:{manifest.Height}");
            AppendTransform(chain, manifest.Width, manifest.Height, transform);
            if (transform.Opacity < 0.999)
            {
                chain.Add($"colorchannelmixer=rr={F(transform.Opacity)}:gg={F(transform.Opacity)}:bb={F(transform.Opacity)}");
            }

            var minimumPadding = Math.Max(clip.FreezeExtensionSeconds, renderDuration);
            chain.Add($"tpad=stop_mode=clone:stop_duration={F(minimumPadding)}");
            chain.Add($"trim=duration={F(renderDuration)}");
            chain.Add("setpts=PTS-STARTPTS");
            chain.Add($"fps={manifest.FramesPerSecond}");
            chain.Add("settb=AVTB");

            var transitionKind = clip.TransitionKind ?? ParseTransition(clip.TransitionIn);
            if (transitionKind == TimelineTransitionKind.Fade)
            {
                chain.Add($"fade=t=in:st=0:d={F(ResolveTransitionDuration(clip))}");
            }

            chain.Add("format=yuv420p");
            filters.Add($"[{index}:v:0]{string.Join(',', chain)}[v{index}]");
        }

        var currentLabel = "v0";
        for (var index = 1; index < manifest.Clips.Count; index++)
        {
            var clip = manifest.Clips[index];
            var transitionKind = clip.TransitionKind ?? ParseTransition(clip.TransitionIn);
            var nextLabel = $"join{index}";
            if (transitionKind == TimelineTransitionKind.Crossfade)
            {
                var duration = ResolveTransitionDuration(clip);
                filters.Add($"[{currentLabel}][v{index}]xfade=transition=fade:duration={F(duration)}:offset={F(clip.TimelineStartSeconds)}[{nextLabel}]");
            }
            else
            {
                filters.Add($"[{currentLabel}][v{index}]concat=n=2:v=1:a=0[{nextLabel}]");
            }
            currentLabel = nextLabel;
        }

        filters.Add($"[{currentLabel}]null[outv]");
        currentLabel = "outv";

        var effectNumber = 0;
        foreach (var effect in manifest.ResolveEffects().OrderBy(effect => effect.StartSeconds).ThenBy(effect => effect.Id))
        {
            var next = $"fx{effectNumber++}";
            var enable = $"between(t,{F(effect.StartSeconds)},{F(effect.EndSeconds)})";
            var filter = effect.Kind switch
            {
                TimelineEffectKind.FadeToBlack => $"drawbox=x=0:y=0:w=iw:h=ih:color=black@{F(effect.Strength)}:t=fill:enable='{enable}'",
                TimelineEffectKind.Grayscale => $"eq=saturation={F(1 - effect.Strength)}:enable='{enable}'",
                TimelineEffectKind.Vignette => $"vignette={F(Math.Max(0.05, Math.PI / 2 * effect.Strength))}:eval=frame:enable='{enable}'",
                _ => throw new InvalidDataException($"Unsupported timeline effect '{effect.Kind}'."),
            };
            filters.Add($"[{currentLabel}]{filter}[{next}]");
            currentLabel = next;
        }

        for (var overlayIndex = 0; overlayIndex < manifest.ResolveOverlays().Count; overlayIndex++)
        {
            var overlay = manifest.ResolveOverlays()[overlayIndex];
            var inputIndex = manifest.Clips.Count + overlayIndex;
            var overlayLabel = $"ov{overlayIndex}";
            var next = $"mix{overlayIndex}";
            filters.Add($"[{inputIndex}:v:0]setpts=PTS-STARTPTS,scale=trunc(iw*{F(overlay.Scale)}/2)*2:trunc(ih*{F(overlay.Scale)}/2)*2,format=rgba,colorchannelmixer=aa={F(overlay.Opacity)}[{overlayLabel}]");
            var x = F((overlay.PositionX + 1) / 2);
            var y = F((overlay.PositionY + 1) / 2);
            filters.Add($"[{currentLabel}][{overlayLabel}]overlay=x='(W-w)*{x}':y='(H-h)*{y}':enable='between(t,{F(overlay.StartSeconds)},{F(overlay.EndSeconds)})':eof_action=pass[{next}]");
            currentLabel = next;
        }

        var subtitleNumber = 0;
        foreach (var subtitle in manifest.ResolveSubtitles().OrderBy(item => item.StartSeconds).ThenBy(item => item.Id))
        {
            var next = $"sub{subtitleNumber++}";
            var yFactor = 0.05 + (((subtitle.PositionY + 1) / 2) * 0.9);
            var fontSizeFactor = 0.05 * subtitle.Size;
            filters.Add(
                $"[{currentLabel}]drawtext=text='{EscapeDrawtextText(subtitle.Text)}':expansion=none:fontcolor=white@{F(subtitle.Opacity)}:fontsize=h*{F(fontSizeFactor)}:borderw=2:bordercolor=black@{F(subtitle.Opacity)}:x=(w-text_w)/2:y=(h-text_h)*{F(yFactor)}:enable='between(t,{F(subtitle.StartSeconds)},{F(subtitle.EndSeconds)})'[{next}]");
            currentLabel = next;
        }

        args.Add("-filter_complex");
        args.Add(string.Join(';', filters));
        args.Add("-map");
        args.Add($"[{currentLabel}]");
        args.Add("-map");
        var songInputIndex = manifest.Clips.Count + overlayPaths.Count;
        args.Add($"{songInputIndex}:a:0");
        args.Add("-t");
        args.Add(F(manifest.DurationSeconds));
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

    private static double ResolveCrossfadeDuration(RenderTimelineClip clip)
    {
        var kind = clip.TransitionKind ?? ParseTransition(clip.TransitionIn);
        return kind == TimelineTransitionKind.Crossfade ? ResolveTransitionDuration(clip) : 0;
    }

    private static double ResolveTransitionDuration(RenderTimelineClip clip) =>
        clip.TransitionDurationSeconds > 0
            ? clip.TransitionDurationSeconds
            : Math.Min(0.35, clip.DurationSeconds / 4d);

    private static void AppendTransform(List<string> chain, int width, int height, TimelineClipTransform transform)
    {
        if (Math.Abs(transform.Scale - 1) < 0.001 && Math.Abs(transform.PositionX) < 0.001 && Math.Abs(transform.PositionY) < 0.001)
        {
            return;
        }

        var xFactor = F((transform.PositionX + 1) / 2);
        var yFactor = F((transform.PositionY + 1) / 2);
        if (transform.Scale >= 1)
        {
            chain.Add($"scale=trunc(iw*{F(transform.Scale)}/2)*2:trunc(ih*{F(transform.Scale)}/2)*2");
            chain.Add($"crop={width}:{height}:(iw-ow)*{xFactor}:(ih-oh)*{yFactor}");
        }
        else
        {
            chain.Add($"scale=trunc(iw*{F(transform.Scale)}/2)*2:trunc(ih*{F(transform.Scale)}/2)*2");
            chain.Add($"pad={width}:{height}:(ow-iw)*{xFactor}:(oh-ih)*{yFactor}:black");
        }
    }

    private static TimelineTransitionKind ParseTransition(string transition)
    {
        if (transition.Contains("cross", StringComparison.OrdinalIgnoreCase)) return TimelineTransitionKind.Crossfade;
        if (transition.Contains("fade", StringComparison.OrdinalIgnoreCase)) return TimelineTransitionKind.Fade;
        return TimelineTransitionKind.Cut;
    }

    private static string EscapeDrawtextText(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static bool IsStillImage(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

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
