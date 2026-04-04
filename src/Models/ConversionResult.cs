namespace LceWorldConverter;

public sealed class ConversionResult
{
    public required string SourceWorldPath { get; init; }
    public required string OutputDirectory { get; init; }
    public required string OutputPath { get; init; }
    public required int OverworldChunks { get; init; }
    public required int NetherChunks { get; init; }
    public required int EndChunks { get; init; }
    public required int PlayersCopied { get; init; }
    public required IReadOnlyList<string> UnknownModernBlocks { get; init; }
    public string? UnknownBlocksPath { get; init; }
}