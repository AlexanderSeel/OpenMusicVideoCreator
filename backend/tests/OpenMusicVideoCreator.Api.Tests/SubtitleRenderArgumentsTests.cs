using OpenMusicVideoCreator.Domain.Rendering;
using OpenMusicVideoCreator.Domain.Timeline;
using OpenMusicVideoCreator.Infrastructure.Rendering;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class SubtitleRenderArgumentsTests
{
    [Fact]
    public void Subtitle_IsEscapedTimedAndDoesNotChangeSongOnlyAudioMapping()
    {
        var manifest = new ProjectRenderManifest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProjectRenderKind.Final,
            1920,
            1080,
            30,
            [new RenderTimelineClip(
                Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 0, 5, "Cut",
                SourceDurationSeconds: 5,
                Transform: TimelineClipTransform.Default,
                Color: TimelineColorAdjustment.Neutral,
                TransitionKind: TimelineTransitionKind.Cut)],
            5,
            new string('f', 64),
            Subtitles:
            [
                new TimelineSubtitle(
                    Guid.NewGuid(),
                    "It's 10:30\nGo\\now",
                    1,
                    3,
                    0.75,
                    1.1,
                    0.8),
            ]);

        var arguments = FfmpegProjectRenderEngine.BuildArguments(
            manifest,
            ["/tmp/clip.mp4"],
            "/tmp/original song.flac",
            "/tmp/out.mp4").ToArray();
        var filter = arguments[Array.IndexOf(arguments, "-filter_complex") + 1];

        Assert.Contains("drawtext=", filter);
        Assert.Contains("expansion=none", filter);
        Assert.Contains("enable='between(t,1,3)'", filter);
        Assert.Contains("It\\'s 10\\:30\\nGo\\\\now", filter);
        Assert.Contains("fontcolor=white@0.8", filter);
        Assert.Contains("fontsize=h*0.055", filter);

        var maps = arguments.Select((value, index) => (value, index))
            .Where(item => item.value == "-map")
            .Select(item => arguments[item.index + 1])
            .ToArray();
        Assert.Equal("1:a:0", maps[1]);
        Assert.DoesNotContain("0:a:0", maps);
    }
}
