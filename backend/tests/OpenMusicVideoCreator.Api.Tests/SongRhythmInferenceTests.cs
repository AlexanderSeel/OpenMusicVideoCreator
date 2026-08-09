using OpenMusicVideoCreator.Domain.Analysis;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class SongRhythmInferenceTests
{
    [Fact]
    public void BeatsProduceFourBeatBarsAndFourBarPhrases()
    {
        var beats = Enumerable.Range(0, 32)
            .Select(index => new BeatMarker(index * 0.5, 0.9))
            .ToArray();

        var bars = SongRhythmInference.InferBars(beats);
        var phrases = SongRhythmInference.InferPhrases(bars, 16);

        Assert.Equal(8, bars.Count);
        Assert.Equal(0, bars[0].TimeSeconds);
        Assert.Equal(2, bars[1].TimeSeconds);
        Assert.Equal(2, phrases.Count);
        Assert.Equal(0, phrases[0].StartSeconds);
        Assert.Equal(8, phrases[0].EndSeconds);
        Assert.Equal(8, phrases[1].StartSeconds);
        Assert.Equal(16, phrases[1].EndSeconds);
    }

    [Fact]
    public void QuietEnergyWindowsBecomeBoundedQuietRanges()
    {
        var energy = Enumerable.Range(0, 80)
            .Select(index => new EnergyPoint(
                index * 0.05,
                index is >= 20 and <= 55 ? 0.05 : 0.5))
            .ToArray();

        var quiet = SongRhythmInference.DetectQuietRanges(energy, durationSeconds: 4);

        var range = Assert.Single(quiet);
        Assert.InRange(range.StartSeconds, 0.95, 1.05);
        Assert.InRange(range.EndSeconds, 2.75, 2.85);
        Assert.InRange(range.AverageEnergy, 0.049, 0.051);
    }
}
