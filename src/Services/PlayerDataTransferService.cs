using fNbt;

namespace LceWorldConverter;

public sealed class PlayerDataTransferService
{
    public int ExportPlayers(LceSaveDataReader saveReader, string outputDir, string targetVersion)
    {
        string playersDir = Path.Combine(outputDir, "playerdata");
        Directory.CreateDirectory(playersDir);

        int count = 0;
        foreach (var entry in saveReader.EnumerateEntries())
        {
            if (!entry.Name.StartsWith("players/", StringComparison.OrdinalIgnoreCase) || !entry.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!saveReader.TryGetFileBytes(entry.Name, out byte[] bytes))
                continue;

            string filename = Path.GetFileName(entry.Name.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                var nbtFile = new NbtFile();
                nbtFile.LoadFromBuffer(bytes, 0, bytes.Length, NbtCompression.AutoDetect);
                var playerTag = nbtFile.RootTag as NbtCompound;
                if (playerTag != null)
                {
                    ChunkConverter.SanitizeLegacyItemStacks(playerTag, targetVersion);
                    nbtFile.SaveToFile(Path.Combine(playersDir, filename), NbtCompression.GZip);
                }
                else
                {
                    File.WriteAllBytes(Path.Combine(playersDir, filename), bytes);
                }
            }
            catch
            {
                File.WriteAllBytes(Path.Combine(playersDir, filename), bytes);
            }

            count++;
        }

        return count;
    }

    public int CopyPlayers(string javaWorldPath, SaveDataContainer container, int blockOffsetX, int blockOffsetZ)
    {
        string[] candidateDirs =
        [
            Path.Combine(javaWorldPath, "players")
        ];

        int count = 0;
        foreach (string dir in candidateDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string filePath in Directory.GetFiles(dir, "*.dat"))
            {
                try
                {
                    string fileStem = Path.GetFileNameWithoutExtension(filePath);
                    if (!ulong.TryParse(fileStem, out ulong parsedPlayerId))
                        continue;

                    var nbtFile = new NbtFile();
                    nbtFile.LoadFromFile(filePath);
                    var player = nbtFile.RootTag;

                    var pos = player.Get<NbtList>("Pos");
                    if (pos != null && pos.Count >= 3)
                    {
                        ((NbtDouble)pos[0]).Value -= blockOffsetX;
                        ((NbtDouble)pos[2]).Value -= blockOffsetZ;
                    }

                    var spawnX = player.Get<NbtInt>("SpawnX");
                    var spawnZ = player.Get<NbtInt>("SpawnZ");
                    if (spawnX != null)
                        spawnX.Value -= blockOffsetX;
                    if (spawnZ != null)
                        spawnZ.Value -= blockOffsetZ;

                    using var ms = new MemoryStream();
                    nbtFile.SaveToStream(ms, NbtCompression.None);
                    byte[] remapped = ms.ToArray();

                    string entryName = "players/" + parsedPlayerId + ".dat";
                    var entry = container.CreateFile(entryName);
                    container.WriteToFile(entry, remapped);
                    count++;
                }
                catch
                {
                }
            }
        }

        return count;
    }
}
