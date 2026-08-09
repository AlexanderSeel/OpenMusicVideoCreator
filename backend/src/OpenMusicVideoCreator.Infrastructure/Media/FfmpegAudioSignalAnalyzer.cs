using System.Buffers.Binary;
using System.Diagnostics;
using OpenMusicVideoCreator.Application.Abstractions;
using OpenMusicVideoCreator.Application.Analysis;
using OpenMusicVideoCreator.Domain.Analysis;

namespace OpenMusicVideoCreator.Infrastructure.Media;

public sealed class FfmpegAudioSignalAnalyzer : IAudioSignalAnalyzer
{
    private const int AnalysisSampleRate = 8000;
    private const int EnergyWindowSamples = AnalysisSampleRate / 20; // 50 ms

    private readonly LocalMediaPathResolver _paths;

    public FfmpegAudioSignalAnalyzer(LocalMediaPathResolver paths)
    {
        _paths = paths;
    }

    public async Task<AudioSignalAnalysis> AnalyzeAsync(
        MediaLocation location,
        double durationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (durationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        }

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
                throw new InvalidOperationException("ffmpeg process could not be started.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "ffmpeg is required for waveform analysis but could not be started.",
                exception);
        }

        using var cancellationRegistration = cancellationToken.Register(() => Kill(process));
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var waveform = new List<WaveformBucket>();
        var rawEnergy = new List<EnergyPoint>();

        var targetWaveBuckets = Math.Clamp((int)Math.Ceiling(durationSeconds * 4), 240, 1200);
        var expectedSamples = Math.Max(1L, (long)Math.Ceiling(durationSeconds * AnalysisSampleRate));
        var samplesPerWaveBucket = Math.Max(1L, (long)Math.Ceiling(expectedSamples / (double)targetWaveBuckets));

        var buffer = new byte[32 * 1024];
        var hasCarry = false;
        byte carry = 0;
        long sampleIndex = 0;
        var wave = new SignalAccumulator(sampleIndex);
        var energy = new SignalAccumulator(sampleIndex);

        while (true)
        {
            var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            var offset = 0;
            if (hasCarry)
            {
                ProcessSample(unchecked((short)(carry | (buffer[0] << 8))));
                offset = 1;
                hasCarry = false;
            }

            while (offset + 1 < read)
            {
                ProcessSample(BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset, 2)));
                offset += 2;
            }

            if (offset < read)
            {
                carry = buffer[offset];
                hasCarry = true;
            }
        }

        if (hasCarry)
        {
            throw new InvalidDataException("FFmpeg returned an incomplete PCM sample.");
        }

        FlushWave();
        FlushEnergy();

        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException($"ffmpeg failed with exit code {process.ExitCode}: {error.Trim()}");
        }

        var normalizedEnergy = NormalizeEnergy(rawEnergy);
        var beats = DetectBeats(normalizedEnergy);
        var bpm = EstimateBpm(beats);
        return new AudioSignalAnalysis(waveform, normalizedEnergy, beats, bpm);

        void ProcessSample(short pcm)
        {
            var value = pcm / 32768d;
            wave.Add(value);
            energy.Add(value);
            sampleIndex++;

            if (wave.Count >= samplesPerWaveBucket)
            {
                FlushWave();
            }

            if (energy.Count >= EnergyWindowSamples)
            {
                FlushEnergy();
            }
        }

        void FlushWave()
        {
            if (wave.Count == 0)
            {
                return;
            }

            waveform.Add(new WaveformBucket(
                wave.StartSample / (double)AnalysisSampleRate,
                (wave.StartSample + wave.Count) / (double)AnalysisSampleRate,
                wave.Minimum,
                wave.Maximum,
                wave.Rms));
            wave = new SignalAccumulator(sampleIndex);
        }

        void FlushEnergy()
        {
            if (energy.Count == 0)
            {
                return;
            }

            rawEnergy.Add(new EnergyPoint(
                (energy.StartSample + energy.Count / 2d) / AnalysisSampleRate,
                energy.Rms));
            energy = new SignalAccumulator(sampleIndex);
        }
    }

    private static IReadOnlyList<EnergyPoint> NormalizeEnergy(IReadOnlyList<EnergyPoint> raw)
    {
        if (raw.Count == 0)
        {
            return [];
        }

        var maximum = raw.Max(point => point.Value);
        if (maximum <= 0)
        {
            return raw.Select(point => point with { Value = 0 }).ToArray();
        }

        return raw
            .Select(point => point with { Value = Math.Clamp(point.Value / maximum, 0, 1) })
            .ToArray();
    }

    internal static IReadOnlyList<BeatMarker> DetectBeats(IReadOnlyList<EnergyPoint> energy)
    {
        if (energy.Count < 8)
        {
            return [];
        }

        var beats = new List<BeatMarker>();
        var lastBeat = double.NegativeInfinity;
        const int history = 6;

        for (var index = history; index < energy.Count - 1; index++)
        {
            var baseline = 0d;
            for (var offset = index - history; offset < index; offset++)
            {
                baseline += energy[offset].Value;
            }
            baseline /= history;

            var current = energy[index];
            var previous = energy[index - 1].Value;
            var next = energy[index + 1].Value;
            var threshold = Math.Max(0.08, baseline * 1.32);
            if (current.Value < threshold || current.Value < previous || current.Value < next)
            {
                continue;
            }

            if (current.TimeSeconds - lastBeat < 0.25)
            {
                continue;
            }

            var ratio = baseline <= 0.0001 ? 2 : current.Value / baseline;
            var confidence = Math.Clamp((ratio - 1) / 1.4, 0.15, 1);
            beats.Add(new BeatMarker(current.TimeSeconds, confidence));
            lastBeat = current.TimeSeconds;
        }

        return beats;
    }

    internal static double? EstimateBpm(IReadOnlyList<BeatMarker> beats)
    {
        if (beats.Count < 5)
        {
            return null;
        }

        var intervals = beats
            .Zip(beats.Skip(1), (left, right) => right.TimeSeconds - left.TimeSeconds)
            .Where(interval => interval is >= 0.28 and <= 1.5)
            .OrderBy(interval => interval)
            .ToArray();
        if (intervals.Length < 4)
        {
            return null;
        }

        var median = intervals.Length % 2 == 1
            ? intervals[intervals.Length / 2]
            : (intervals[intervals.Length / 2 - 1] + intervals[intervals.Length / 2]) / 2;
        if (median <= 0)
        {
            return null;
        }

        var bpm = 60d / median;
        while (bpm < 70)
        {
            bpm *= 2;
        }
        while (bpm > 180)
        {
            bpm /= 2;
        }

        return Math.Round(bpm, 2);
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
            "-map", "0:a:0",
            "-vn",
            "-ac", "1",
            "-ar", AnalysisSampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-f", "s16le",
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

    private sealed class SignalAccumulator
    {
        private double _sumSquares;

        public SignalAccumulator(long startSample)
        {
            StartSample = startSample;
        }

        public long StartSample { get; }
        public long Count { get; private set; }
        public double Minimum { get; private set; } = 1;
        public double Maximum { get; private set; } = -1;
        public double Rms => Count == 0 ? 0 : Math.Sqrt(_sumSquares / Count);

        public void Add(double value)
        {
            Minimum = Math.Min(Minimum, value);
            Maximum = Math.Max(Maximum, value);
            _sumSquares += value * value;
            Count++;
        }
    }
}
