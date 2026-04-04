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
            "Usage:",
            "  LceWorldConverter --from java <java_world_folder_or_zip> <output_dir> [--world-type <classic|small|medium|large|flat|flat-small|flat-medium|flat-large>] [--all-dimensions] [--copy-players] [--preserve-entities]",
            "  LceWorldConverter --from lce <saveData.ms_path> <java_world_output_dir> [--all-dimensions] [--copy-players]",
            string.Empty,
            "  --from java|lce             Conversion direction.",
            "  --target-version          LCE->Java: target modern MC version (e.g. 1.21.11)",
            "  java_world_folder_or_zip  Path to a Java world folder or a .zip archive containing one.",
            "  saveData.ms_path          Path to an LCE saveData.ms file.",
            "  output_dir                Java->LCE: directory to write saveData.ms into.",
            "  java_world_output_dir     LCE->Java: output world folder to create/populate.",
            "  --world-type              Java->LCE world profile selector:",
            "                            classic, small, medium, large, flat, flat-small, flat-medium, flat-large",
            "                            (flat = classic size + flat generator)",
            "  --small-world             Java->LCE: use 64-chunk (1024 block) world size",
            "  --medium-world            Java->LCE: use 192-chunk (3072 block) world size",
            "  --large-world             Java->LCE: use 320-chunk (5120 block) world size",
            "  --flat-world              Java->LCE: force output generatorName to flat",
            "  --all-dimensions          Convert Nether and End in addition to Overworld",
            "  --copy-players            Java->LCE: import numeric players/*.dat; LCE->Java: export players/*.dat",
            "  --preserve-entities       Java->LCE only: keep chunk dynamic entity/tile data",
            string.Empty,
            "Legacy Java->LCE positional mode still works:",
            "  LceWorldConverter <java_world_folder_or_zip> [output_dir] [flags...]",
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

        int fromIndex = Array.IndexOf(args, "--from");
        if (fromIndex < 0)
            return TryParseLegacyPositional(args, out options, out error);

        if (fromIndex + 1 >= args.Length || args[fromIndex + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = "--from requires a value: java or lce.";
            return false;
        }

        string from = args[fromIndex + 1].Trim().ToLowerInvariant();
        return from switch
        {
            "java" => TryParseFromJava(args, fromIndex, out options, out error),
            "lce" => TryParseFromLce(args, fromIndex, out options, out error),
            _ => Fail("Unknown --from value. Valid values are: java, lce.", out options, out error),
        };
    }

    private static bool TryParseLegacyPositional(string[] args, out ConversionOptions? options, out string? error)
    {
        string inputPath = args[0];
        if (LooksLikeLceInput(inputPath))
            return TryParseLegacyLceToJava(args, out options, out error);

        return TryParseLegacyJavaToLce(args, out options, out error);
    }

    private static bool TryParseLegacyJavaToLce(string[] args, out ConversionOptions? options, out string? error)
    {
        options = null;

        string inputPath = args[0];
        string? outputDirArg = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;

        if (!TryParseWorldSettings(args, out int xzSize, out string sizeLabel, out bool flatWorld, out error))
            return false;

        string outputName = Path.GetFileNameWithoutExtension(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(outputName))
            outputName = "ConvertedWorld";

        options = new ConversionOptions
        {
            Direction = ConversionDirection.JavaToLce,
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

    private static bool TryParseLegacyLceToJava(string[] args, out ConversionOptions? options, out string? error)
    {
        options = null;

        if (args.Contains("--preserve-entities"))
        {
            error = "--preserve-entities is only valid with Java->LCE conversion.";
            return false;
        }

        if (args.Contains("--world-type") || args.Contains("--small-world") || args.Contains("--medium-world") || args.Contains("--large-world") || args.Contains("--flat-world"))
        {
            error = "World-size and flat-world flags are only valid with Java->LCE conversion.";
            return false;
        }

        string inputPath = args[0];
        string? outputDirArg = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
        string outputName = Path.GetFileNameWithoutExtension(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(outputName))
            outputName = "RecoveredJavaWorld";

        options = new ConversionOptions
        {
            Direction = ConversionDirection.LceToJava,
            InputPath = inputPath,
            OutputDirectory = outputDirArg ?? Path.Combine(Directory.GetCurrentDirectory(), outputName),
            XzSize = ClassicWorldSize,
            SizeLabel = "Classic",
            FlatWorld = false,
            ConvertAllDimensions = args.Contains("--all-dimensions"),
            CopyPlayers = args.Contains("--copy-players"),
            PreserveEntities = false,
            TargetVersion = GetOptionValue(args, "--target-version") ?? "1.12.2"
        };

        error = null;
        return true;
    }

    private static bool LooksLikeLceInput(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return false;

        if (inputPath.EndsWith(".ms", StringComparison.OrdinalIgnoreCase))
            return true;

        string fileName = Path.GetFileName(inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return fileName.Equals("saveData.ms", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseFromJava(string[] args, int fromIndex, out ConversionOptions? options, out string? error)
    {
        options = null;

        var positionals = CollectPositionals(args, fromIndex);
        if (positionals.Count < 2)
        {
            error = "Expected <java_world_folder_or_zip> and <output_dir> after --from java.";
            return false;
        }

        string inputPath = positionals[0];
        string outputDirectory = positionals[1];

        if (!TryParseWorldSettings(args, out int xzSize, out string sizeLabel, out bool flatWorld, out error))
            return false;

        options = new ConversionOptions
        {
            Direction = ConversionDirection.JavaToLce,
            InputPath = inputPath,
            OutputDirectory = outputDirectory,
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

    private static bool TryParseFromLce(string[] args, int fromIndex, out ConversionOptions? options, out string? error)
    {
        options = null;

        var positionals = CollectPositionals(args, fromIndex);
        if (positionals.Count < 2)
        {
            error = "Expected <saveData.ms_path> and <java_world_output_dir> after --from lce.";
            return false;
        }

        if (args.Contains("--preserve-entities"))
        {
            error = "--preserve-entities is only valid with --from java.";
            return false;
        }

        if (args.Contains("--world-type") || args.Contains("--small-world") || args.Contains("--medium-world") || args.Contains("--large-world") || args.Contains("--flat-world"))
        {
            error = "World-size and flat-world flags are only valid with --from java.";
            return false;
        }

        options = new ConversionOptions
        {
            Direction = ConversionDirection.LceToJava,
            InputPath = positionals[0],
            OutputDirectory = positionals[1],
            XzSize = ClassicWorldSize,
            SizeLabel = "Classic",
            FlatWorld = false,
            ConvertAllDimensions = args.Contains("--all-dimensions"),
            CopyPlayers = args.Contains("--copy-players"),
            PreserveEntities = false,
            TargetVersion = GetOptionValue(args, "--target-version") ?? "1.12.2"
        };

        error = null;
        return true;
    }


    private static string? GetOptionValue(string[] args, string flag)
    {
        int index = Array.IndexOf(args, flag);
        if (index >= 0 && index + 1 < args.Length)
        {
            return args[index + 1];
        }
        return null;
    }

    private static List<string> CollectPositionals(string[] args, int fromIndex)
    {
        var positionals = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (i == fromIndex || i == fromIndex + 1)
                continue;

            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if ((args[i].Equals("--world-type", StringComparison.Ordinal) || args[i].Equals("--target-version", StringComparison.Ordinal)) && i + 1 < args.Length)
                    i++;

                continue;
            }

            positionals.Add(args[i]);
        }

        return positionals;
    }

    private static bool Fail(string message, out ConversionOptions? options, out string? error)
    {
        options = null;
        error = message;
        return false;
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
