namespace OpenMusicVideoCreator.Domain.Library;

public enum VisualLibraryKind
{
    Character,
    Style,
    Location,
}

public enum CharacterReferenceType
{
    Photo,
    Turnaround,
    Illustration,
    ConceptArt,
    Other,
}

public sealed record CharacterContinuityLocks(
    bool Identity,
    bool Face,
    bool Hair,
    bool Body,
    bool Age,
    bool Wardrobe);

public sealed record CharacterOutfit(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<Guid> AssetEntryIds);

public sealed record CharacterLibraryData(
    CharacterReferenceType ReferenceType,
    string AppearanceDescription,
    IReadOnlyList<string> ForbiddenChanges,
    IReadOnlyList<CharacterOutfit> Outfits,
    CharacterContinuityLocks DefaultLocks);

public sealed record StyleLibraryData(
    string Prompt,
    string CameraCharacteristics,
    string LightingCharacteristics,
    string AnimationCharacteristics);

public sealed record LocationLibraryData(
    string EnvironmentDescription,
    IReadOnlyList<string> Constraints,
    string Lighting,
    string Weather,
    string TimeOfDay);

public sealed record VisualLibraryItem(
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
    public static VisualLibraryItem Create(
        Guid id,
        VisualLibraryDraft draft,
        DateTimeOffset nowUtc)
    {
        Validate(draft);
        return new VisualLibraryItem(
            id,
            draft.Kind,
            draft.Name.Trim(),
            draft.Description.Trim(),
            NormalizeStrings(draft.Tags),
            draft.IsFavorite,
            draft.AssetEntryIds.Distinct().ToArray(),
            NormalizeCharacter(draft.Character),
            draft.Style,
            NormalizeLocation(draft.Location),
            nowUtc,
            nowUtc);
    }

    public VisualLibraryItem Update(VisualLibraryDraft draft, DateTimeOffset nowUtc)
    {
        Validate(draft);
        return this with
        {
            Kind = draft.Kind,
            Name = draft.Name.Trim(),
            Description = draft.Description.Trim(),
            Tags = NormalizeStrings(draft.Tags),
            IsFavorite = draft.IsFavorite,
            AssetEntryIds = draft.AssetEntryIds.Distinct().ToArray(),
            Character = NormalizeCharacter(draft.Character),
            Style = draft.Style,
            Location = NormalizeLocation(draft.Location),
            UpdatedUtc = nowUtc,
        };
    }

    private static void Validate(VisualLibraryDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            throw new ArgumentException("Library item name is required.", nameof(draft));
        }

        var payloadCount = (draft.Character is null ? 0 : 1) +
            (draft.Style is null ? 0 : 1) +
            (draft.Location is null ? 0 : 1);
        if (payloadCount != 1 ||
            (draft.Kind == VisualLibraryKind.Character && draft.Character is null) ||
            (draft.Kind == VisualLibraryKind.Style && draft.Style is null) ||
            (draft.Kind == VisualLibraryKind.Location && draft.Location is null))
        {
            throw new ArgumentException("Exactly one detail payload matching the library item kind is required.", nameof(draft));
        }

        if (draft.Character is not null)
        {
            foreach (var outfit in draft.Character.Outfits)
            {
                if (outfit.Id == Guid.Empty || string.IsNullOrWhiteSpace(outfit.Name))
                {
                    throw new ArgumentException("Character outfits require an ID and name.", nameof(draft));
                }
            }
        }
    }

    private static CharacterLibraryData? NormalizeCharacter(CharacterLibraryData? character) => character is null
        ? null
        : character with
        {
            AppearanceDescription = character.AppearanceDescription.Trim(),
            ForbiddenChanges = NormalizeStrings(character.ForbiddenChanges),
            Outfits = character.Outfits.Select(outfit => outfit with
            {
                Name = outfit.Name.Trim(),
                Description = outfit.Description.Trim(),
                AssetEntryIds = outfit.AssetEntryIds.Distinct().ToArray(),
            }).ToArray(),
        };

    private static LocationLibraryData? NormalizeLocation(LocationLibraryData? location) => location is null
        ? null
        : location with
        {
            EnvironmentDescription = location.EnvironmentDescription.Trim(),
            Constraints = NormalizeStrings(location.Constraints),
            Lighting = location.Lighting.Trim(),
            Weather = location.Weather.Trim(),
            TimeOfDay = location.TimeOfDay.Trim(),
        };

    private static IReadOnlyList<string> NormalizeStrings(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public sealed record VisualLibraryDraft(
    VisualLibraryKind Kind,
    string Name,
    string Description,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    IReadOnlyList<Guid> AssetEntryIds,
    CharacterLibraryData? Character,
    StyleLibraryData? Style,
    LocationLibraryData? Location);

public sealed record AssetLibraryEntry(
    Guid Id,
    Guid MediaAssetId,
    Guid? PreviewMediaAssetId,
    string Name,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    string SourceDescription,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record ProjectCharacterState(
    Guid ProjectId,
    Guid CharacterId,
    Guid? OutfitId,
    CharacterContinuityLocks Locks,
    IReadOnlyDictionary<string, double> StateValues,
    DateTimeOffset UpdatedUtc)
{
    public static void ValidateStateValues(IReadOnlyDictionary<string, double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !double.IsFinite(pair.Value) || pair.Value is < 0 or > 1)
            {
                throw new ArgumentException("Character state values require non-empty keys and normalized values from 0 to 1.", nameof(values));
            }
        }
    }
}
