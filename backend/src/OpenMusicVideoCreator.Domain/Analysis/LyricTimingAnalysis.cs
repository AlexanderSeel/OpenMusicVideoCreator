namespace OpenMusicVideoCreator.Domain.Analysis;

public sealed record LyricTimingLine(
    int LineNumber,
    string Text,
    double? StartSeconds,
    double? EndSeconds,
    double Confidence)
{
    public bool IsMatched => StartSeconds.HasValue && EndSeconds.HasValue;
}

public sealed record LyricTimingAnalysis(
    Guid Id,
    Guid ProjectId,
    Guid SourceAssetId,
    Guid SongAnalysisId,
    int Version,
    string SuppliedLyricsSha256,
    IReadOnlyList<LyricTimingLine> Lines,
    DateTimeOffset CreatedUtc)
{
    public double MatchedFraction => Lines.Count == 0
        ? 0
        : Lines.Count(line => line.IsMatched) / (double)Lines.Count;
}
