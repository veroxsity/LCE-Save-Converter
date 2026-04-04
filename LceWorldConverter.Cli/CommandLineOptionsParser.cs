using LceWorldConverter;

namespace LceWorldConverter.Cli;

public static class CommandLineOptionsParser
{
    public static IReadOnlyList<string> GetUsageLines()
    {
        return
        [
            "Usage:",
            "  LceWorldConverter --from java <java_world_folder_or_zip> <output_dir> [--world-type <classic|small|medium|large|flat|flat-small|flat-medium|flat-large>] [--all-dimensions] [--copy-players] [--preserve-entities]",
            "  LceWorldConverter --from lce <saveData.ms_path> <java_world_output_dir> [--all-dimensions] [--copy-players] [--target-version <version>]",
            string.Empty,
            "  --from java|lce             Conversion direction.",
            "  --target-version            LCE->Java: target modern MC version (e.g. 1.21.11)",
            "  java_world_folder_or_zip    Path to a Java world folder or a .zip archive containing one.",
            "  saveData.ms_path            Path to an LCE saveData.ms file.",
            "  output_dir                  Java->LCE: directory to write saveData.ms into.",
            "  java_world_output_dir       LCE->Java: output world folder to create/populate.",
            "  --world-type                Java->LCE world profile selector:",
            "                              classic, small, medium, large, flat, flat-small, flat-medium, flat-large",
            "  --small-world               Java->LCE: use 64-chunk (1024 block) world size",
            "  --medium-world              Java->LCE: use 192-chunk (3072 block) world size",
            "  --large-world               Java->LCE: use 320-chunk (5120 block) world size",
            "  --flat-world                Java->LCE: force output generatorName to flat",
            "  --all-dimensions            Convert Nether and End in addition to Overworld",
            "  --copy-players              Java->LCE: import numeric players/*.dat; LCE->Java: export players/*.dat",
            "  --preserve-entities         Java->LCE only: keep chunk dynamic entity/tile data",
            string.Empty,
            "Legacy Java->LCE positional mode still works:",
            "  LceWorldConverter <java_world_folder_or_zip> [output_dir] [flags...]",
            string.Empty,
            "Use the Windows GUI project if you want a desktop app for selecting a world zip and output folder.",
        ];
    }

    public static bool TryParse(string[] args, out ConversionRequest? request, out string? error)
    {
        request = null;

        if (args.Length < 1)
        {
            error = "Missing input path.";
            return false;
        }

        int fromIndex = Array.IndexOf(args, "--from");
        if (fromIndex < 0)
            return TryParseLegacyPositional(args, out request, out error);

        if (fromIndex + 1 >= args.Length || args[fromIndex + 1].StartsWith("--", StringComparison.Ordinal))
        {
            error = "--from requires a value: java or lce.";
            return false;
        }

        string from = args[fromIndex + 1].Trim().ToLowerInvariant();
        return from switch
        {
            "java" => TryParseFromJava(args, fromIndex, out request, out error),
            "lce" => TryParseFromLce(args, fromIndex, out request, out error),
            _ => Fail("Unknown --from value. Valid values are: java, lce.", out request, out error),
        };
    }

    private static bool TryParseLegacyPositional(string[] args, out ConversionRequest? request, out string? error)
    {
        string inputPath = args[0];
        if (LooksLikeLceInput(inputPath))
            return TryParseLegacyLceToJava(args, out request, out error);

        return TryParseLegacyJavaToLce(args, out request, out error);
    }

    private static bool TryParseLegacyJavaToLce(string[] args, out ConversionRequest? request, out string? error)
    {
        request = null;

        string inputPath = args[0];
        string? outputDirArg = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : null;
        if (!TryParseWorldProfile(args, out WorldProfile worldProfile, out error))
            return false;

        request = new ConversionRequest
        {
            Direction = ConversionDirection.JavaToLce,
            InputPath = inputPath,
            OutputDirectory = outputDirArg ?? ConversionDefaults.GetDefaultOutputDirectory(ConversionDirection.JavaToLce, inputPath, Directory.GetCurrentDirectory()),
            WorldProfile = worldProfile,
            ConvertAllDimensions = args.Contains("--all-dimensions"),
            CopyPlayers = args.Contains("--copy-players"),
            PreserveEntities = args.Contains("--preserve-entities"),
        };

        error = null;
        return true;
    }

    private static bool TryParseLegacyLceToJava(string[] args, out ConversionRequest? request, out string? error)
    {
        request = null;

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

        request = new ConversionRequest
        {
            Direction = ConversionDirection.LceToJava,
            InputPath = inputPath,
            OutputDirectory = outputDirArg ?? ConversionDefaults.GetDefaultOutputDirectory(ConversionDirection.LceToJava, inputPath, Directory.GetCurrentDirectory()),
            WorldProfile = WorldProfile.Classic,
            ConvertAllDimensions = args.Contains("--all-dimensions"),
            CopyPlayers = args.Contains("--copy-players"),
            PreserveEntities = false,
            TargetVersion = GetOptionValue(args, "--target-version") ?? ConversionDefaults.DefaultTargetVersion,
        };

        error = null;
        return true;
    }

    private static bool TryParseFromJava(string[] args, int fromIndex, out ConversionRequest? request, out string? error)
    {
        request = null;

        var positionals = CollectPositionals(args, fromIndex);
        if (positionals.Count < 2)
        {
            error = "Expected <java_world_folder_or_zip> and <output_dir> after --from java.";
            return false;
        }

        if (!TryParseWorldProfile(args, out WorldProfile worldProfile, out error))
            return false;

        request = new ConversionRequest
        {
            Direction = ConversionDirection.JavaToLce,
            InputPath = positionals[0],
            OutputDirectory = positionals[1],
            WorldProfile = worldProfile,
            ConvertAllDimensions = args.Contains("--all-dimensions"),
            CopyPlayers = args.Contains("--copy-players"),
            PreserveEntities = args.Contains("--preserve-entities"),
        };

        error = null;
        return true;
    }

    private static bool TryParseFromLce(string[] args, int fromIndex, out ConversionRequest? request, out string? error)
    {
        request = null;

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

        request = new ConversionRequest
        {
            Direction = ConversionDirection.LceToJava,
            InputPath = positionals[0],
            OutputDirectory = positionals[1],
            WorldProfile = WorldProfile.Classic,
            ConvertAllDimensions = args.Contains("--all-dimensions"),
            CopyPlayers = args.Contains("--copy-players"),
            PreserveEntities = false,
            TargetVersion = GetOptionValue(args, "--target-version") ?? ConversionDefaults.DefaultTargetVersion,
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

    private static bool TryParseWorldProfile(string[] args, out WorldProfile worldProfile, out string? error)
    {
        worldProfile = WorldProfile.Classic;
        string? worldType = GetOptionValue(args, "--world-type");
        if (worldType != null)
        {
            if (!WorldProfiles.TryParse(worldType, out worldProfile))
            {
                error = $"Unknown world type '{worldType}'. Valid values: {string.Join(", ", WorldProfiles.Keys)}";
                return false;
            }

            if (args.Contains("--small-world") || args.Contains("--medium-world") || args.Contains("--large-world") || args.Contains("--flat-world"))
            {
                error = "Do not mix --world-type with legacy size flags.";
                return false;
            }

            error = null;
            return true;
        }

        bool smallWorld = args.Contains("--small-world");
        bool mediumWorld = args.Contains("--medium-world");
        bool largeWorld = args.Contains("--large-world");
        bool flatWorld = args.Contains("--flat-world");

        int sizeFlags = (smallWorld ? 1 : 0) + (mediumWorld ? 1 : 0) + (largeWorld ? 1 : 0);
        if (sizeFlags > 1)
        {
            error = "Only one of --small-world, --medium-world, or --large-world may be specified.";
            return false;
        }

        worldProfile = (smallWorld, mediumWorld, largeWorld, flatWorld) switch
        {
            (true, false, false, false) => WorldProfile.Small,
            (false, true, false, false) => WorldProfile.Medium,
            (false, false, true, false) => WorldProfile.Large,
            (false, false, false, true) => WorldProfile.Flat,
            (true, false, false, true) => WorldProfile.FlatSmall,
            (false, true, false, true) => WorldProfile.FlatMedium,
            (false, false, true, true) => WorldProfile.FlatLarge,
            _ => WorldProfile.Classic,
        };

        error = null;
        return true;
    }

    private static string? GetOptionValue(string[] args, string flag)
    {
        int index = Array.IndexOf(args, flag);
        if (index >= 0 && index + 1 < args.Length)
            return args[index + 1];

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

    private static bool Fail(string message, out ConversionRequest? request, out string? error)
    {
        request = null;
        error = message;
        return false;
    }
}
