namespace LceWorldConverter;

public static class CommandLineOptionsParser
{
    private const int ClassicWorldSize = 54;
    private const int SmallWorldSize = 64;
    private const int MediumWorldSize = 192;
    private const int LargeWorldSize = 320;

    public static IReadOnlyList<string> GetUsageLines()
    {
        return
        [
            "Usage: LceWorldConverter <java_world_folder_or_zip> [output_dir] [--world-type <classic|small|medium|large|flat|flat-small|flat-medium|flat-large>] [--all-dimensions] [--copy-players] [--preserve-entities]",
            string.Empty,
            "  java_world_folder_or_zip  Path to a Java world folder or a .zip archive containing one.",
            "  output_dir                Optional: directory to write saveData.ms into.",
            "                            Defaults to a folder named after the source world in the current directory.",
            "  --world-type              Unified world profile selector (recommended):",
            "                            classic, small, medium, large, flat, flat-small, flat-medium, flat-large",
            "                            (flat = classic size + flat generator)",
            "  --small-world             Use 64-chunk (1024 block) world size",
            "  --medium-world            Use 192-chunk (3072 block) world size",
            "  --large-world             Use 320-chunk (5120 block) world size",
            "  --flat-world              Force output level.dat generatorName to flat",
            "  --all-dimensions          Convert Nether and End in addition to Overworld (experimental)",
            "  --copy-players            Import Java players/*.dat (numeric filenames only)",
            "  --preserve-entities       Keep chunk Entities/TileEntities/TileTicks (may reduce compatibility)",
            string.Empty,
            "Use the Windows GUI project if you want a desktop app for selecting a world zip and output folder.",
        ];
    }

    public static bool TryParse(string[] args, out ConversionOptions? options, out string? error)
    {
        options = null;

        if (args.Length < 1)
        {
            error = "Missing input path.";
            return false;
        }

        string inputPath = args[0];
        string? outputDirArg = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;

        if (!TryParseWorldSettings(args, out int xzSize, out string sizeLabel, out bool flatWorld, out error))
            return false;

        string outputName = Path.GetFileNameWithoutExtension(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(outputName))
            outputName = "ConvertedWorld";

        options = new ConversionOptions
        {
            InputPath = inputPath,
            OutputDirectory = outputDirArg ?? Path.Combine(Directory.GetCurrentDirectory(), outputName),
            XzSize = xzSize,
            SizeLabel = sizeLabel,
            FlatWorld = flatWorld,
            ConvertAllDimensions = args.Contains("--all-dimensions"),
            CopyPlayers = args.Contains("--copy-players"),
            PreserveEntities = args.Contains("--preserve-entities"),
        };

        error = null;
        return true;
    }

    private static bool TryParseWorldSettings(
        string[] args,
        out int xzSize,
        out string sizeLabel,
        out bool flatWorld,
        out string? error)
    {
        int worldTypeIndex = Array.IndexOf(args, "--world-type");

        bool smallWorld = args.Contains("--small-world");
        bool mediumWorld = args.Contains("--medium-world");
        bool largeWorld = args.Contains("--large-world");
        flatWorld = args.Contains("--flat-world");

        if (worldTypeIndex >= 0)
        {
            if (smallWorld || mediumWorld || largeWorld || flatWorld)
            {
                xzSize = ClassicWorldSize;
                sizeLabel = "Classic";
                error = "Do not mix --world-type with legacy flags (--small-world/--medium-world/--large-world/--flat-world).";
                return false;
            }

            if (worldTypeIndex + 1 >= args.Length || args[worldTypeIndex + 1].StartsWith("--", StringComparison.Ordinal))
            {
                xzSize = ClassicWorldSize;
                sizeLabel = "Classic";
                error = "--world-type requires a value: classic, small, medium, large, flat, flat-small, flat-medium, flat-large.";
                return false;
            }

            string worldType = args[worldTypeIndex + 1].Trim().ToLowerInvariant();
            switch (worldType)
            {
                case "classic":
                    xzSize = ClassicWorldSize;
                    sizeLabel = "Classic";
                    flatWorld = false;
                    break;
                case "small":
                    xzSize = SmallWorldSize;
                    sizeLabel = "Small";
                    flatWorld = false;
                    break;
                case "medium":
                    xzSize = MediumWorldSize;
                    sizeLabel = "Medium";
                    flatWorld = false;
                    break;
                case "large":
                    xzSize = LargeWorldSize;
                    sizeLabel = "Large";
                    flatWorld = false;
                    break;
                case "flat":
                case "flat-classic":
                    xzSize = ClassicWorldSize;
                    sizeLabel = "Classic";
                    flatWorld = true;
                    break;
                case "flat-small":
                    xzSize = SmallWorldSize;
                    sizeLabel = "Small";
                    flatWorld = true;
                    break;
                case "flat-medium":
                    xzSize = MediumWorldSize;
                    sizeLabel = "Medium";
                    flatWorld = true;
                    break;
                case "flat-large":
                    xzSize = LargeWorldSize;
                    sizeLabel = "Large";
                    flatWorld = true;
                    break;
                default:
                    xzSize = ClassicWorldSize;
                    sizeLabel = "Classic";
                    error = $"Unknown --world-type '{worldType}'. Valid values: classic, small, medium, large, flat, flat-small, flat-medium, flat-large.";
                    return false;
            }

            error = null;
            return true;
        }

        int sizeFlagCount = (smallWorld ? 1 : 0) + (mediumWorld ? 1 : 0) + (largeWorld ? 1 : 0);
        if (sizeFlagCount > 1)
        {
            xzSize = ClassicWorldSize;
            sizeLabel = "Classic";
            error = "Use only one size flag: --small-world, --medium-world, or --large-world.";
            return false;
        }

        if (smallWorld)
        {
            xzSize = SmallWorldSize;
            sizeLabel = "Small";
        }
        else if (mediumWorld)
        {
            xzSize = MediumWorldSize;
            sizeLabel = "Medium";
        }
        else if (largeWorld)
        {
            xzSize = LargeWorldSize;
            sizeLabel = "Large";
        }
        else
        {
            xzSize = ClassicWorldSize;
            sizeLabel = "Classic";
        }

        error = null;
        return true;
    }
}