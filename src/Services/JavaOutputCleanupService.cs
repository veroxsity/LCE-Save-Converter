namespace LceWorldConverter;

public sealed class JavaOutputCleanupService
{
    public void Clean(string outputDir)
    {
        DeleteStaleJavaRuntimeState(outputDir);
        DeleteLegacyOutputRegions(outputDir);
    }

    private static void DeleteStaleJavaRuntimeState(string outputDir)
    {
        string[] staleDirs =
        [
            Path.Combine(outputDir, "playerdata"),
            Path.Combine(outputDir, "players"),
            Path.Combine(outputDir, "entities"),
            Path.Combine(outputDir, "poi"),
            Path.Combine(outputDir, "stats"),
            Path.Combine(outputDir, "advancements"),
        ];

        foreach (string dir in staleDirs)
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }

        string levelDatPath = Path.Combine(outputDir, "level.dat");
        if (File.Exists(levelDatPath))
            File.Delete(levelDatPath);
    }

    private static void DeleteLegacyOutputRegions(string outputDir)
    {
        string[] regionDirs =
        [
            Path.Combine(outputDir, "region"),
            Path.Combine(outputDir, "DIM-1", "region"),
            Path.Combine(outputDir, "DIM1", "region"),
        ];

        foreach (string dir in regionDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string file in Directory.GetFiles(dir, "*.mcr"))
                File.Delete(file);
            foreach (string file in Directory.GetFiles(dir, "*.mca"))
                File.Delete(file);
        }
    }
}
