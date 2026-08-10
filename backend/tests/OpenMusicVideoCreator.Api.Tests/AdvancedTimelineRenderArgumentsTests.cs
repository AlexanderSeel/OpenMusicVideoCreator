using OpenMusicVideoCreator.Domain.Rendering;
using OpenMusicVideoCreator.Domain.Timeline;
using OpenMusicVideoCreator.Infrastructure.Rendering;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class AdvancedTimelineRenderArgumentsTests
{
    [Fact]
    public void AdvancedEditsEffectsAndOverlay_AreRenderedWhileSongRemainsOnlyAudioMap()
    {
        var overlayId = Guid.NewGuid();
        var manifest = new ProjectRenderManifest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProjectRenderKind.Final,
            1920,
            1080,
            30,
            [
                new RenderTimelineClip(
                    Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 0, 5, "Fade",
                    SourceInSeconds: 0.5,
                    SourceDurationSeconds: 4,
                    PlaybackRate: 1.25,
                    FreezeExtensionSeconds: 0.8,
                    Transform: new TimelineClipTransform(1.2, 0.25, -0.2, 0.05, 0.02, 0.05, 0.02, 0.75),
                    Color: new TimelineColorAdjustment(0.1, 1.1, 0.8),
                    TransitionKind: TimelineTransitionKind.Fade,
                    TransitionDurationSeconds: 0.4),
            ],
            5,
            new string('d', 64),
            Guid.NewGuid(),
            [new TimelineOverlay(overlayId, Guid.NewGuid(), 1, 4, 0.4, -0.2, 0.5, 0.6)],
            [new TimelineEffect(Guid.NewGuid(), TimelineEffectKind.Grayscale, 2, 4, 0.5)]);

        var arguments = FfmpegProjectRenderEngine.BuildArguments(
            manifest,
            ["/tmp/clip source.mp4"],
            "/tmp/original song.flac",
            "/tmp/output.mp4",
            ["/tmp/logo.png"]).ToArray();
        var filter = arguments[Array.IndexOf(arguments, "-filter_complex") + 1];

        Assert.Contains("trim=start=0.5:duration=4", filter);
        Assert.Contains("setpts=(PTS-STARTPTS)/1.25", filter);
        Assert.Contains("eq=brightness=0.1:contrast=1.1:saturation=0.8", filter);
        Assert.Contains("colorchannelmixer=rr=0.75", filter);
        Assert.Contains("fade=t=in", filter);
        Assert.Contains("eq=saturation=0.5", filter);
        Assert.Contains("overlay=", filter);
        Assert.Contains("colorchannelmixer=aa=0.6", filter);
        Assert.Contains("-loop", arguments);

        var maps = arguments.Select((value, index) => (value, index))
            .Where(item => item.value == "-map")
            .Select(item => arguments[item.index + 1])
            .ToArray();
        Assert.Equal("2:a:0", maps[1]);
        Assert.DoesNotContain("0:a:0", maps);
        Assert.DoesNotContain("1:a:0", maps);
    }
}
