using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Analysis;

namespace OpenMusicVideoCreator.Infrastructure.Media;

public sealed class FfprobeMediaProbe : IMediaProbe
{
    private readonly LocalMediaPathResolver _paths;

    public FfprobeMediaProbe(LocalMediaPathResolver paths)
    {
        _paths = paths;
    }

    public async Task<MediaProbeResult> ProbeAsync(
        MediaLocation location,
        CancellationToken cancellationToken = default)
    {
        var path = _paths.Resolve(location);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Media file was not found.", path);
        }

        using var process = CreateProcess(path);
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("ffprobe process could not be started.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "ffprobe is required for song analysis but could not be started.",
                exception);
        }

        using var cancellationRegistration = cancellationToken.Register(() => Kill(process));
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException($"ffprobe failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        return Parse(output);
    }

    internal static MediaProbeResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var audioStream = root.TryGetProperty("streams", out var streams)
            ? streams.EnumerateArray().FirstOrDefault(stream =>
                stream.TryGetProperty("codec_type", out var codecType) && codecType.GetString() == "audio")
            : default;

        var format = root.TryGetProperty("format", out var formatElement) ? formatElement : default;
        var duration = ReadDouble(format, "duration") ?? ReadDouble(audioStream, "duration") ?? 0;
        var sampleRate = ReadInt(audioStream, "sample_rate");
        var channels = ReadInt(audioStream, "channels");
        var codec = ReadString(audioStream, "codec_name");
        var bitRate = ReadLong(format, "bit_rate") ?? ReadLong(audioStream, "bit_rate");

        return new MediaProbeResult(duration, sampleRate, channels, codec, bitRate);
    }

    private static Process CreateProcess(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffprobe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
        {
            "-v", "error",
            "-print_format", "json",
            "-show_entries", "format=duration,bit_rate:stream=codec_type,codec_name,duration,sample_rate,channels,bit_rate",
            path,
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return new Process { StartInfo = startInfo };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static double? ReadDouble(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
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
