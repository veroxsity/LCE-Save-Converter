namespace LceWorldConverter;

public readonly record struct WorldProfileSettings(string Key, string SizeLabel, int XzSize, bool FlatWorld);

public static class WorldProfiles
{
    private static readonly IReadOnlyList<string> _keys =
    [
        "classic",
        "small",
        "medium",
        "large",
        "flat",
        "flat-small",
        "flat-medium",
        "flat-large",
    ];

    public static IReadOnlyList<string> Keys => _keys;

    public static WorldProfileSettings Get(WorldProfile profile)
    {
        return profile switch
        {
            WorldProfile.Classic => new WorldProfileSettings("classic", "Classic", 54, FlatWorld: false),
            WorldProfile.Small => new WorldProfileSettings("small", "Small", 64, FlatWorld: false),
            WorldProfile.Medium => new WorldProfileSettings("medium", "Medium", 192, FlatWorld: false),
            WorldProfile.Large => new WorldProfileSettings("large", "Large", 320, FlatWorld: false),
            WorldProfile.Flat => new WorldProfileSettings("flat", "Classic", 54, FlatWorld: true),
            WorldProfile.FlatSmall => new WorldProfileSettings("flat-small", "Small", 64, FlatWorld: true),
            WorldProfile.FlatMedium => new WorldProfileSettings("flat-medium", "Medium", 192, FlatWorld: true),
            WorldProfile.FlatLarge => new WorldProfileSettings("flat-large", "Large", 320, FlatWorld: true),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported world profile."),
        };
    }

    public static string GetKey(WorldProfile profile) => Get(profile).Key;

    public static bool TryParse(string? value, out WorldProfile profile)
    {
        profile = WorldProfile.Classic;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "classic":
                profile = WorldProfile.Classic;
                return true;
            case "small":
                profile = WorldProfile.Small;
                return true;
            case "medium":
                profile = WorldProfile.Medium;
                return true;
            case "large":
                profile = WorldProfile.Large;
                return true;
            case "flat":
                profile = WorldProfile.Flat;
                return true;
            case "flat-small":
                profile = WorldProfile.FlatSmall;
                return true;
            case "flat-medium":
                profile = WorldProfile.FlatMedium;
                return true;
            case "flat-large":
                profile = WorldProfile.FlatLarge;
                return true;
            default:
                return false;
        }
    }

    public static WorldProfile FromLegacySettings(int xzSize, bool flatWorld)
    {
        return (xzSize, flatWorld) switch
        {
            (64, false) => WorldProfile.Small,
            (192, false) => WorldProfile.Medium,
            (320, false) => WorldProfile.Large,
            (54, true) => WorldProfile.Flat,
            (64, true) => WorldProfile.FlatSmall,
            (192, true) => WorldProfile.FlatMedium,
            (320, true) => WorldProfile.FlatLarge,
            _ => WorldProfile.Classic,
        };
    }
}
