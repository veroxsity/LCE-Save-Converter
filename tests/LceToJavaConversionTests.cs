using fNbt;
using Xunit;

namespace LceWorldConverter.Tests;

public sealed class LceToJavaConversionTests
{
    [Fact]
    public void ConvertLceToJava_CleansStaleRuntimeState_AndEmbedsPlayerInLevelDat()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"lce-to-java-{Guid.NewGuid():N}");
        string outputDir = Path.Combine(tempRoot, "out");
        string saveDataPath = Path.Combine(tempRoot, "saveData.ms");

        Directory.CreateDirectory(outputDir);

        try
        {
            SeedStaleRuntimeState(outputDir);
            BuildMinimalLceSave(saveDataPath);

            var service = new LceWorldConversionService();
            var options = new ConversionOptions
            {
                Direction = ConversionDirection.LceToJava,
                InputPath = saveDataPath,
                OutputDirectory = outputDir,
                XzSize = 54,
                SizeLabel = "Classic",
                FlatWorld = false,
                ConvertAllDimensions = false,
                CopyPlayers = false,
                PreserveEntities = false,
            };

            ConversionResult result = service.Convert(options);

            Assert.Equal(0, result.OverworldChunks);
            Assert.False(Directory.Exists(Path.Combine(outputDir, "playerdata")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "entities")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "poi")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "stats")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "advancements")));
            Assert.False(Directory.Exists(Path.Combine(outputDir, "players")));

            string levelDatPath = Path.Combine(outputDir, "level.dat");
            Assert.True(File.Exists(levelDatPath));

            var javaLevel = new NbtFile();
            byte[] levelBytes = File.ReadAllBytes(levelDatPath);
            javaLevel.LoadFromBuffer(levelBytes, 0, levelBytes.Length, NbtCompression.AutoDetect);

            NbtCompound data = javaLevel.RootTag.Get<NbtCompound>("Data")!;
            NbtCompound? player = data.Get<NbtCompound>("Player");
            Assert.NotNull(player);

            NbtList? pos = player!.Get<NbtList>("Pos");
            Assert.NotNull(pos);
            Assert.Equal(3, pos!.Count);
            Assert.Equal(-24.635, ((NbtDouble)pos[0]).Value, 3);
            Assert.Equal(64.0, ((NbtDouble)pos[1]).Value, 3);
            Assert.Equal(280.7, ((NbtDouble)pos[2]).Value, 3);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void BuildMinimalLceSave(string outputPath)
    {
        var container = new SaveDataContainer(7, 9);

        var levelDatRoot = new NbtCompound(string.Empty)
        {
            new NbtCompound("Data")
            {
                new NbtLong("RandomSeed", 123456L),
                new NbtInt("SpawnX", 0),
                new NbtInt("SpawnY", 64),
                new NbtInt("SpawnZ", 0),
                new NbtString("LevelName", "Test LCE"),
            },
        };

        var levelFile = new NbtFile(levelDatRoot);
        using (var levelMs = new MemoryStream())
        {
            levelFile.SaveToStream(levelMs, NbtCompression.None);
            var levelEntry = container.CreateFile("level.dat");
            container.WriteToFile(levelEntry, levelMs.ToArray());
        }

        var playerRoot = new NbtCompound(string.Empty)
        {
            new NbtList("Pos", NbtTagType.Double)
            {
                new NbtDouble(null!, -24.635),
                new NbtDouble(null!, 64.0),
                new NbtDouble(null!, 280.7),
            },
            new NbtFloat("Health", 20f),
        };

        var playerFile = new NbtFile(playerRoot);
        using (var playerMs = new MemoryStream())
        {
            playerFile.SaveToStream(playerMs, NbtCompression.GZip);
            var playerEntry = container.CreateFile("players/0.dat");
            container.WriteToFile(playerEntry, playerMs.ToArray());
        }

        container.Save(outputPath);
    }

    private static void SeedStaleRuntimeState(string outputDir)
    {
        string[] staleDirs =
        [
            "playerdata",
            "players",
            "entities",
            "poi",
            "stats",
            "advancements",
        ];

        foreach (string dirName in staleDirs)
        {
            string dirPath = Path.Combine(outputDir, dirName);
            Directory.CreateDirectory(dirPath);
            File.WriteAllText(Path.Combine(dirPath, "stale.txt"), "stale");
        }

        File.WriteAllText(Path.Combine(outputDir, "level.dat"), "stale-level");
    }
}
