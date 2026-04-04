namespace LceWorldConverter;

public sealed class ChunkConversionContext
{
    private readonly HashSet<string> _unknownModernBlocks = new(StringComparer.Ordinal);

    public ChunkConversionContext(bool preserveDynamicChunkData)
    {
        PreserveDynamicChunkData = preserveDynamicChunkData;
    }

    public bool PreserveDynamicChunkData { get; }

    internal int? GlobalModernSectionShift { get; set; }

    public IReadOnlyList<string> GetUnknownModernBlocksSnapshot()
    {
        return _unknownModernBlocks.OrderBy(name => name, StringComparer.Ordinal).ToList();
    }

    internal void RecordUnknownModernBlock(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (name is "air" or "cave_air" or "void_air")
            return;

        _unknownModernBlocks.Add(name);
    }
}
