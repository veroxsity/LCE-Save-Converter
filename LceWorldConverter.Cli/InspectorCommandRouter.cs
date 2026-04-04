using LceWorldConverter;

namespace LceWorldConverter.Cli;

internal static class InspectorCommandRouter
{
    public static bool TryExecute(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0)
            return false;

        switch (args[0])
        {
            case "--scan-java-world":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: LceWorldConverter --scan-java-world <java_world_path>");
                    exitCode = 1;
                    return true;
                }

                SaveDataInspector.ScanJavaWorld(args[1]);
                return true;

            case "--inspect-region":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: LceWorldConverter --inspect-region <region_file_path>");
                    exitCode = 1;
                    return true;
                }

                SaveDataInspector.InspectJavaRegion(args[1]);
                return true;

            case "--inspect-java-chunk":
                if (args.Length < 4
                    || !int.TryParse(args[2], out int inspectJavaChunkX)
                    || !int.TryParse(args[3], out int inspectJavaChunkZ))
                {
                    Console.WriteLine("Usage: LceWorldConverter --inspect-java-chunk <java_world_path> <chunk_x> <chunk_z> [overworld|nether|end]");
                    exitCode = 1;
                    return true;
                }

                SaveDataInspector.InspectJavaChunk(args[1], inspectJavaChunkX, inspectJavaChunkZ, args.Length > 4 ? args[4] : "overworld");
                return true;

            case "--scan-java-chest-items":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: LceWorldConverter --scan-java-chest-items <java_world_path> [overworld|nether|end] [max_printed]");
                    exitCode = 1;
                    return true;
                }

                int maxPrinted = 30;
                if (args.Length > 3)
                    int.TryParse(args[3], out maxPrinted);

                SaveDataInspector.ScanJavaChestItems(args[1], args.Length > 2 ? args[2] : "overworld", maxPrinted);
                return true;

            case "--inspect":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: LceWorldConverter --inspect <saveData.ms_path>");
                    exitCode = 1;
                    return true;
                }

                SaveDataInspector.Inspect(args[1]);
                return true;

            case "--inspect-lce-chunk":
                if (args.Length < 4
                    || !int.TryParse(args[2], out int inspectLceChunkX)
                    || !int.TryParse(args[3], out int inspectLceChunkZ))
                {
                    Console.WriteLine("Usage: LceWorldConverter --inspect-lce-chunk <saveData.ms_path> <chunk_x> <chunk_z> [overworld|nether|end]");
                    exitCode = 1;
                    return true;
                }

                SaveDataInspector.InspectLceChunk(args[1], inspectLceChunkX, inspectLceChunkZ, args.Length > 4 ? args[4] : "overworld");
                return true;

            case "--scan-lce-coordinates":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: LceWorldConverter --scan-lce-coordinates <saveData.ms_path> [overworld|nether|end]");
                    exitCode = 1;
                    return true;
                }

                SaveDataInspector.ScanLceCoordinates(args[1], args.Length > 2 ? args[2] : "overworld");
                return true;

            case "--scan-lce-trailing-nbt":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: LceWorldConverter --scan-lce-trailing-nbt <saveData.ms_path> [overworld|nether|end]");
                    exitCode = 1;
                    return true;
                }

                SaveDataInspector.ScanLceTrailingNbt(args[1], args.Length > 2 ? args[2] : "overworld");
                return true;

            case "--scan-lce-chest-item-mappings":
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: LceWorldConverter --scan-lce-chest-item-mappings <saveData.ms_path> [overworld|nether|end]");
                    exitCode = 1;
                    return true;
                }

                SaveDataInspector.ScanLceChestItemMappings(args[1], args.Length > 2 ? args[2] : "overworld");
                return true;

            default:
                return false;
        }
    }
}
