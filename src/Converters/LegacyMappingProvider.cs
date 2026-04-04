using System.Text.Json;

namespace LceWorldConverter;

public sealed class LegacyMappingProvider
{
    private readonly Lazy<IReadOnlyDictionary<string, string>> _blockMappings;
    private readonly Lazy<IReadOnlyDictionary<string, string>> _itemMappings;

    public LegacyMappingProvider()
    {
        _blockMappings = new Lazy<IReadOnlyDictionary<string, string>>(
            () => LoadMappings("legacy_to_modern_block_mapping.json"),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _itemMappings = new Lazy<IReadOnlyDictionary<string, string>>(
            () => LoadMappings("legacy_to_modern_item_mapping.json"),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string GetModernBlockName(byte id, byte meta)
    {
        string key = $"{id}:{meta}";
        if (_blockMappings.Value.TryGetValue(key, out string? modernName))
            return modernName;

        return _blockMappings.Value.TryGetValue($"{id}:0", out modernName)
            ? modernName
            : "minecraft:air";
    }

    public string GetModernItemName(short id)
    {
        return _itemMappings.Value.TryGetValue(id.ToString(), out string? modernName)
            ? modernName
            : "minecraft:air";
    }

    private static IReadOnlyDictionary<string, string> LoadMappings(string fileName)
    {
        string mapPath = Path.Combine(AppContext.BaseDirectory, "Resources", fileName);
        if (!File.Exists(mapPath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        string json = File.ReadAllText(mapPath);
        Dictionary<string, string>? map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return map == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(map, StringComparer.Ordinal);
    }
}
