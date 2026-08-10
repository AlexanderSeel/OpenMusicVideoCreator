using OpenMusicVideoCreator.Domain.Rendering;
using OpenMusicVideoCreator.Domain.Timeline;
using OpenMusicVideoCreator.Infrastructure.Rendering;
using Xunit;

namespace OpenMusicVideoCreator.Api.Tests;

public sealed class CrossfadeRenderArgumentsTests
{
    [Fact]
    public void Crossfade_ExtendsOutgoingClipAndXfadesAtNominalBoundary()
    {
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
                    Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 0, 4, "Cut",
                    SourceDurationSeconds: 4,
                    Transform: TimelineClipTransform.Default,
                    Color: TimelineColorAdjustment.Neutral,
                    TransitionKind: TimelineTransitionKind.Cut),
                new RenderTimelineClip(
                    Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), 4, 6, "Crossfade",
                    SourceDurationSeconds: 6,
                    Transform: TimelineClipTransform.Default,
                    Color: TimelineColorAdjustment.Neutral,
                    TransitionKind: TimelineTransitionKind.Crossfade,
                    TransitionDurationSeconds: 0.5),
            ],
            10,
            new string('e', 64));

        var arguments = FfmpegProjectRenderEngine.BuildArguments(
            manifest,
            ["/tmp/one.mp4", "/tmp/two.mp4"],
            "/tmp/song.flac",
            "/tmp/out.mp4").ToArray();
        var filter = arguments[Array.IndexOf(arguments, "-filter_complex") + 1];

        Assert.Contains("[0:v:0]", filter);
        Assert.Contains("trim=duration=4.5", filter);
        Assert.Contains("[v0][v1]xfade=transition=fade:duration=0.5:offset=4[join1]", filter);
        Assert.Contains("[join1]null[outv]", filter);
        Assert.DoesNotContain("[v0][v1]concat=n=2", filter);

        var maps = arguments.Select((value, index) => (value, index))
            .Where(item => item.value == "-map")
            .Select(item => arguments[item.index + 1])
            .ToArray();
        Assert.Equal("[outv]", maps[0]);
        Assert.Equal("2:a:0", maps[1]);
        Assert.Equal("10", arguments[Array.IndexOf(arguments, "-t") + 1]);
    }
}
