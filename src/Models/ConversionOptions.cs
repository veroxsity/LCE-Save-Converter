namespace LceWorldConverter;

public enum ConversionDirection
{
    JavaToLce,
    LceToJava,
}

public sealed class ConversionOptions
{
    public required ConversionDirection Direction { get; init; }
    public required string InputPath { get; init; }
    public required string OutputDirectory { get; init; }
    public required int XzSize { get; init; }
    public required string SizeLabel { get; init; }
    public string TargetVersion { get; init; } = "1.12.2";
    public bool FlatWorld { get; init; }
    public bool ConvertAllDimensions { get; init; }
    public bool CopyPlayers { get; init; }
    public bool PreserveEntities { get; init; }

    public static ConversionOptions FromRequest(ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ConversionOptions
        {
            Direction = request.Direction,
            InputPath = request.InputPath,
            OutputDirectory = request.OutputDirectory,
            XzSize = request.XzSize,
            SizeLabel = request.SizeLabel,
            TargetVersion = ConversionDefaults.NormalizeTargetVersion(request.TargetVersion),
            FlatWorld = request.FlatWorld,
            ConvertAllDimensions = request.ConvertAllDimensions,
            CopyPlayers = request.CopyPlayers,
            PreserveEntities = request.PreserveEntities,
        };
    }

    public ConversionRequest ToRequest()
    {
        return new ConversionRequest
        {
            Direction = Direction,
            InputPath = InputPath,
            OutputDirectory = OutputDirectory,
            WorldProfile = WorldProfiles.FromLegacySettings(XzSize, FlatWorld),
            TargetVersion = ConversionDefaults.NormalizeTargetVersion(TargetVersion),
            ConvertAllDimensions = ConvertAllDimensions,
            CopyPlayers = CopyPlayers,
            PreserveEntities = PreserveEntities,
        };
    }
}
