namespace LceWorldConverter;

public sealed class UnknownBlockReportService
{
    public string? Write(string outputDir, IReadOnlyList<string> unknownBlocks, IConversionLogger logger)
    {
        if (unknownBlocks.Count == 0)
            return null;

        string unknownPath = Path.Combine(outputDir, "unknown-modern-blocks.txt");
        File.WriteAllLines(unknownPath, unknownBlocks);

        logger.Info(string.Empty);
        logger.Info($"Unknown modern blocks mapped to air: {unknownBlocks.Count}");
        foreach (string blockName in unknownBlocks.Take(40))
            logger.Info($"  - {blockName}");
        if (unknownBlocks.Count > 40)
            logger.Info($"  ... and {unknownBlocks.Count - 40} more");
        logger.Info($"Full list written to: {unknownPath}");

        return unknownPath;
    }
}
