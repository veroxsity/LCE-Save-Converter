namespace LceWorldConverter;

public sealed record ConversionRequest
{
    public required ConversionDirection Direction { get; init; }
    public required string InputPath { get; init; }
    public required string OutputDirectory { get; init; }
    public WorldProfile WorldProfile { get; init; } = WorldProfile.Classic;
    public string TargetVersion { get; init; } = ConversionDefaults.DefaultTargetVersion;
    public bool ConvertAllDimensions { get; init; }
    public bool CopyPlayers { get; init; }
    public bool PreserveEntities { get; init; }

    public int XzSize => WorldProfiles.Get(WorldProfile).XzSize;
    public string SizeLabel => WorldProfiles.Get(WorldProfile).SizeLabel;
    public bool FlatWorld => WorldProfiles.Get(WorldProfile).FlatWorld;
}
