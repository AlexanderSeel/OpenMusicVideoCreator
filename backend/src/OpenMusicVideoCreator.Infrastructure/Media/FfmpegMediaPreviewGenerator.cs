using System.Diagnostics;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Library;

namespace OpenMusicVideoCreator.Infrastructure.Media;

public sealed class FfmpegMediaPreviewGenerator : IMediaPreviewGenerator
{
    private const int MaxPreviewBytes = 16 * 1024 * 1024;
    private readonly LocalMediaPathResolver _paths;

    public FfmpegMediaPreviewGenerator(LocalMediaPathResolver paths)
    {
        _paths = paths;
    }

    public async Task<GeneratedMediaPreview?> GenerateAsync(
        MediaLocation source,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (!mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            !mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = _paths.Resolve(source);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Library source media was not found.", path);
        }

        using var process = CreateProcess(path);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("ffmpeg preview process could not be started.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("ffmpeg is required for visual preview generation.", exception);
        }

        using var cancellationRegistration = cancellationToken.Register(() => Kill(process));
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (output.Length + read > MaxPreviewBytes)
            {
                Kill(process);
                throw new InvalidDataException("Generated visual preview exceeded the 16 MB safety limit.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            output.Dispose();
            throw new InvalidDataException($"ffmpeg preview generation failed with exit code {process.ExitCode}: {error.Trim()}");
        }
        if (output.Length == 0)
        {
            output.Dispose();
            throw new InvalidDataException("ffmpeg did not produce a visual preview.");
        }

        output.Position = 0;
        return new GeneratedMediaPreview(output, $"{Guid.NewGuid():N}-preview.png", "image/png");
    }

    private static Process CreateProcess(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-i", path,
            "-map", "0:v:0",
            "-frames:v", "1",
            "-vf", "scale=480:-2:force_original_aspect_ratio=decrease",
            "-f", "image2pipe",
            "-vcodec", "png",
            "pipe:1",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        return new Process { StartInfo = startInfo };
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
    }
}
