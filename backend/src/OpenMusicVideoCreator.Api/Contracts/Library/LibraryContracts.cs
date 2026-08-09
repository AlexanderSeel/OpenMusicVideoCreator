using OpenMusicVideoCreator.Domain.Library;

namespace OpenMusicVideoCreator.Api.Contracts.Library;

public sealed record VisualLibraryUpsertRequest(
    VisualLibraryKind Kind,
    string Name,
    string Description,
    IReadOnlyList<string>? Tags,
    bool IsFavorite,
    IReadOnlyList<Guid>? AssetEntryIds,
    CharacterLibraryData? Character,
    StyleLibraryData? Style,
    LocationLibraryData? Location)
{
    public VisualLibraryDraft ToDraft() => new(
        Kind,
        Name,
        Description,
        Tags?.ToArray() ?? [],
        IsFavorite,
        AssetEntryIds?.ToArray() ?? [],
        Character,
        Style,
        Location);
}

public sealed record VisualLibraryResponse(
    Guid Id,
    VisualLibraryKind Kind,
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    IReadOnlyList<Guid> AssetEntryIds,
    CharacterLibraryData? Character,
    StyleLibraryData? Style,
    LocationLibraryData? Location,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static VisualLibraryResponse FromDomain(VisualLibraryItem item) => new(
        item.Id,
        item.Kind,
        item.Name,
        item.Description,
        item.Tags,
        item.IsFavorite,
        item.AssetEntryIds,
        item.Character,
        item.Style,
        item.Location,
        item.CreatedUtc,
        item.UpdatedUtc);
}

public sealed record AssetLibraryUpdateRequest(
    string Name,
    IReadOnlyList<string>? Tags,
    bool IsFavorite,
    string? SourceDescription);

public sealed record AssetLibraryResponse(
    Guid Id,
    Guid MediaAssetId,
    Guid? PreviewMediaAssetId,
    string Name,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    string SourceDescription,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    public static AssetLibraryResponse FromDomain(AssetLibraryEntry item) => new(
        item.Id,
        item.MediaAssetId,
        item.PreviewMediaAssetId,
        item.Name,
        item.Tags,
        item.IsFavorite,
        item.SourceDescription,
        item.CreatedUtc,
        item.UpdatedUtc);
}

public sealed record ReferencedDeleteResponse(
    bool Deleted,
    IReadOnlyList<Guid> ReferencingIds);

public sealed record ProjectCharacterStateRequest(
    Guid? OutfitId,
    CharacterContinuityLocks Locks,
    IReadOnlyDictionary<string, double>? StateValues);

public sealed record ProjectCharacterStateResponse(
    Guid ProjectId,
    Guid CharacterId,
    Guid? OutfitId,
    CharacterContinuityLocks Locks,
    IReadOnlyDictionary<string, double> StateValues,
    DateTimeOffset UpdatedUtc)
{
    public static ProjectCharacterStateResponse FromDomain(ProjectCharacterState state) => new(
        state.ProjectId,
        state.CharacterId,
        state.OutfitId,
        state.Locks,
        state.StateValues,
        state.UpdatedUtc);
}
