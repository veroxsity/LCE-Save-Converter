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
    public bool FlatWorld { get; init; }
    public bool ConvertAllDimensions { get; init; }
    public bool CopyPlayers { get; init; }
    public bool PreserveEntities { get; init; }
}